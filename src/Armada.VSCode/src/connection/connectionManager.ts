/*
 * The connection to one Admiral (VSCODE_EXTENSION_PLAN.md 4.4, modeled on Mux's src/server/lifecycle.ts):
 *
 * 1. Endpoints come from what the machine already has (endpoints.ts): a running Harbor's Admiral
 *    (~/.armada-harbor/settings.json), the local Admiral (~/.armada/settings.json) and the TUI's server profiles
 *    (~/.armada/tui.json), each with the credentials already stored for it. Nothing is configured in VS Code settings.
 * 2. Startup connects with no prompt: to the endpoint last picked with "Armada: Connect", else to the first
 *    discovered endpoint that answers and accepts one of its credentials.
 * 3. "Armada: Connect" is the one connect command: a picker of the discovered endpoints plus "Enter an Admiral
 *    URL...". A key is asked for only when no stored credential works, and is then kept in SecretStorage for that URL.
 * 4. Each attempt probes GET /api/v1/status/health, checks the contract major, then proves a credential with
 *    GET /api/v1/whoami and sets the role context keys. Concurrent callers share one in-flight attempt.
 *
 * Starting an Admiral is not done here yet; it needs the bundled binaries (plan 4.7).
 */

import * as vscode from 'vscode';
import { ApiError, authenticate, lookupTenants, whoami } from '@dashboard/api/client';
import type { WhoAmIResult } from '@dashboard/types/models';
import { ArmadaSettings, readSettings } from '../config';
import { describeError, NotConnectedError } from '../transport/errors';
import { HostCredential, normalizeBaseUrl, readHealth, useAdmiral } from '../transport/hostClient';
import { checkContract } from './contract';
import { AdmiralEndpoint, canonicalBaseUrl, discoverEndpoints, EndpointCredential } from './endpoints';
import { discoverLocalAdmiral, isLocalAdmiral, isLoopbackUrl } from './localDiscovery';

/** Context key: connected to an Admiral. */
export const CONNECTED_CONTEXT_KEY = 'armada.connected';

/** Context key: the connection phase (disconnected, connecting, connected, failed). */
export const STATE_CONTEXT_KEY = 'armada.connectionState';

/** Context key: the credential is a global administrator. */
export const GLOBAL_ADMIN_CONTEXT_KEY = 'armada.role.globalAdmin';

/** Context key: the credential is a tenant administrator (or a global administrator, as on the server). */
export const TENANT_ADMIN_CONTEXT_KEY = 'armada.role.tenantAdmin';

/** globalState key set by "Disconnect", so a restart does not reconnect until "Connect". */
export const MANUAL_DISCONNECT_KEY = 'armada.connection.manualDisconnect';

/** globalState key: the base URL last picked with "Armada: Connect"; startup reconnects to it. */
export const ENDPOINT_KEY = 'armada.connection.endpoint';

/** SecretStorage key of the credential entered for an Admiral (JSON { secret, header }). */
export function credentialSecretKey(baseUrl: string): string {
  return `armada.credential:${canonicalBaseUrl(baseUrl)}`;
}

/** Connection phase. */
export type ConnectionPhase = 'disconnected' | 'connecting' | 'connected' | 'failed';

/** Why the last attempt failed. */
export type FailureKind = 'noAdmiral' | 'noCredential' | 'unreachable' | 'unauthorized' | 'incompatible' | 'error';

/** The Admiral being talked to. */
export interface ConnectionTarget {
  kind: 'local' | 'remote';
  baseUrl: string;
  /** "local" or host:port. */
  label: string;
  /** Where the endpoint was found (a settings file, a TUI profile, or entered). */
  source: string;
}

/** Who the credential is. */
export interface ConnectionIdentity {
  email: string | null;
  tenantId: string | null;
  tenantName: string | null;
  globalAdmin: boolean;
  tenantAdmin: boolean;
}

/** Snapshot of the connection. */
export interface ConnectionState {
  phase: ConnectionPhase;
  target?: ConnectionTarget;
  failure?: { kind: FailureKind; summary: string; message: string };
  serverVersion?: string | null;
  contractVersion?: string | null;
  identity?: ConnectionIdentity;
  /** What the credential in use is ("Harbor access key"). */
  credentialOrigin?: string;
}

/** What surfaces need while connected. */
export interface ActiveConnection {
  target: ConnectionTarget;
  baseUrl: string;
  /** Tenant filter from armada.tenant, if any. */
  tenant: string | undefined;
  /** The dashboard base URL (armada.dashboardUrl, Harbor's DashboardUrl, or the Admiral's /dashboard). */
  dashboardUrl: string;
  /** The credential for the WebSocket `token` query parameter. */
  socketToken: string | undefined;
  /** The credential in use, for requests the extension sends itself (the API explorer). Never logged. */
  credential: HostCredential | undefined;
}

/** Diagnostics sink (the "Armada" log output channel). Never given a secret. */
export interface ConnectionLog {
  info(line: string): void;
  warn(line: string): void;
  error(line: string): void;
}

/** Host dependencies, injectable for tests. */
export interface ConnectionManagerDeps {
  secrets: vscode.SecretStorage;
  globalState?: vscode.Memento;
  log: ConnectionLog;
  getSettings?: () => ArmadaSettings;
  /** The endpoints this machine knows about, in priority order. */
  discover?: () => Promise<AdmiralEndpoint[]>;
  /** True when an Admiral answers its health route; used to annotate the picker. */
  probe?: (baseUrl: string) => Promise<boolean>;
}

interface Failure {
  kind: FailureKind;
  summary: string;
  message: string;
}

interface PickItem extends vscode.QuickPickItem {
  endpoint?: AdmiralEndpoint;
  enterUrl?: boolean;
}

/** Connection lifecycle for the extension. */
export class ConnectionManager implements vscode.Disposable {
  private readonly _Deps: ConnectionManagerDeps;
  private readonly _GetSettings: () => ArmadaSettings;
  private readonly _Discover: () => Promise<AdmiralEndpoint[]>;
  private readonly _Probe: (baseUrl: string) => Promise<boolean>;
  private readonly _OnDidChange = new vscode.EventEmitter<ConnectionState>();
  private _State: ConnectionState = { phase: 'disconnected' };
  private _Credential: HostCredential | undefined;
  private _Endpoint: AdmiralEndpoint | undefined;
  private _InFlight: Promise<Failure | undefined> | undefined;

  /** Fires after every state change. */
  public readonly onDidChange: vscode.Event<ConnectionState> = this._OnDidChange.event;

  /**
   * @throws Error when deps, secrets or log is missing.
   */
  public constructor(deps: ConnectionManagerDeps) {
    if (!deps) throw new Error('deps is required');
    if (!deps.secrets) throw new Error('secrets is required');
    if (!deps.log) throw new Error('log is required');
    this._Deps = deps;
    this._GetSettings = deps.getSettings ?? readSettings;
    this._Discover = deps.discover ?? (() => discoverEndpoints());
    this._Probe = deps.probe ?? probeHealth;
  }

  /** The current state. */
  public get state(): ConnectionState {
    return this._State;
  }

  /** True while connected. */
  public get connected(): boolean {
    return this._State.phase === 'connected';
  }

  /** The active connection, or undefined. */
  public get active(): ActiveConnection | undefined {
    const target = this._State.target;
    if (this._State.phase !== 'connected' || !target) return undefined;
    const settings = this._GetSettings();
    return {
      target,
      baseUrl: target.baseUrl,
      tenant: settings.tenant || undefined,
      dashboardUrl: this.dashboardUrl(),
      socketToken: this._Credential?.secret,
      credential: this._Credential,
    };
  }

  /**
   * The active connection.
   * @throws NotConnectedError when not connected.
   */
  public requireActive(): ActiveConnection {
    const active = this.active;
    if (!active) throw new NotConnectedError();
    return active;
  }

  /** The dashboard base URL for links: armada.dashboardUrl, Harbor's DashboardUrl, or the target's /dashboard. */
  public dashboardUrl(): string {
    const settings = this._GetSettings();
    if (settings.dashboardUrl) return settings.dashboardUrl;
    if (this._Endpoint?.dashboardUrl) return this._Endpoint.dashboardUrl.replace(/\/+$/, '');
    const target = this._State.target;
    return target ? `${target.baseUrl}/dashboard` : '';
  }

  /** Startup: connects without prompting unless the user disconnected last time. Never throws. */
  public async restore(): Promise<boolean> {
    if (this._Deps.globalState?.get<boolean>(MANUAL_DISCONNECT_KEY) === true) {
      this._Deps.log.info('[connect] skipped at startup: disconnected by the user (run "Armada: Connect")');
      await this.setState({ phase: 'disconnected' }, undefined);
      return false;
    }
    return this.connect({ interactive: false });
  }

  /**
   * Interactive: shows the endpoint picker and connects to the pick, asking for a key only when no stored credential
   * works. Not interactive: connects to the endpoint last picked, else the first discovered endpoint that works.
   * Concurrent callers share one attempt. Never throws.
   */
  public async connect(options: { interactive?: boolean } = {}): Promise<boolean> {
    if (options.interactive !== true) return (await this.exclusive(() => this.autoConnect())) === undefined;
    const endpoint = await this.pick();
    if (!endpoint) return false;
    return this.connectTo(endpoint);
  }

  /**
   * Connects to one endpoint, asking for a key when no stored credential works; on success remembers it for startup.
   * Never throws.
   */
  public async connectTo(endpoint: AdmiralEndpoint): Promise<boolean> {
    await this._Deps.globalState?.update(MANUAL_DISCONNECT_KEY, undefined);
    const failure = await this.exclusive(() => this.tryEndpoint(endpoint));
    if (!failure) {
      await this._Deps.globalState?.update(ENDPOINT_KEY, endpoint.baseUrl);
      void vscode.window.showInformationMessage(`Armada: connected to ${endpoint.baseUrl}.`);
      return true;
    }
    if (failure && (failure.kind === 'noCredential' || failure.kind === 'unauthorized')) return this.promptForKey(endpoint, failure);
    this.explain();
    return false;
  }

  /** Reconnects to the current or last target without the picker; with none, shows the picker. Never throws. */
  public async reconnect(): Promise<boolean> {
    if (!this._Endpoint) return this.connect({ interactive: true });
    const endpoint = this._Endpoint;
    const failure = await this.exclusive(() => this.tryEndpoint(endpoint));
    if (failure) this.explain();
    return !failure;
  }

  /**
   * Drops the connection, removes a key entered for this Admiral from SecretStorage, and remembers the choice so a
   * restart does not reconnect until "Connect".
   */
  public async disconnect(): Promise<void> {
    await this._Deps.globalState?.update(MANUAL_DISCONNECT_KEY, true);
    const baseUrl = this._State.target?.baseUrl;
    const key = baseUrl ? credentialSecretKey(baseUrl) : undefined;
    const hadKey = key !== undefined && (await this._Deps.secrets.get(key)) !== undefined;
    if (hadKey && key) await this._Deps.secrets.delete(key);
    useAdmiral(undefined, undefined);
    await this.setState({ phase: 'disconnected' }, undefined);
    this._Deps.log.info('[connect] disconnected by the user');
    void vscode.window.showInformationMessage(hadKey
      ? 'Armada: disconnected. The key entered for this Admiral was removed from secret storage.'
      : 'Armada: disconnected. Run "Armada: Connect" to connect again.');
  }

  public dispose(): void {
    this._OnDidChange.dispose();
  }

  private exclusive(run: () => Promise<Failure | undefined>): Promise<Failure | undefined> {
    if (this._InFlight) {
      // Concurrent callers share the attempt already running.
      return this._InFlight;
    }
    this._InFlight = run().catch((err): Failure => {
      this._Deps.log.error(`[connect] unexpected failure: ${describeError(err)}`);
      return { kind: 'error', summary: 'error', message: describeError(err) };
    }).finally(() => {
      this._InFlight = undefined;
    });
    return this._InFlight;
  }

  private async autoConnect(): Promise<Failure | undefined> {
    let endpoints: AdmiralEndpoint[];
    try {
      endpoints = await this._Discover();
    } catch (err) {
      this._Deps.log.error(`[connect] discovery failed: ${describeError(err)}`);
      endpoints = [];
    }
    const saved = this._Deps.globalState?.get<string>(ENDPOINT_KEY);
    if (saved) {
      const endpoint = findEndpoint(endpoints, saved) ?? enteredEndpoint(saved);
      this._Deps.log.info(`[connect] reconnecting to ${endpoint.baseUrl}, picked with "Armada: Connect"`);
      return this.tryEndpoint(endpoint);
    }
    if (!endpoints.length) {
      return this.fail(undefined, {
        kind: 'noAdmiral',
        summary: 'no Admiral found',
        message: 'No Armada Admiral was found on this machine (no ~/.armada/settings.json, Harbor settings or TUI profile). Run "Armada: Connect" and enter the Admiral URL.',
      });
    }
    this._Deps.log.info(`[connect] found ${endpoints.map((e) => `${e.baseUrl} (${e.sources.join(', ')})`).join('; ')}`);
    let first: { endpoint: AdmiralEndpoint; failure: Failure } | undefined;
    for (const endpoint of endpoints) {
      const failure = await this.tryEndpoint(endpoint);
      if (!failure) return undefined;
      first ??= { endpoint, failure };
    }
    if (!first) return undefined;
    this._Endpoint = first.endpoint;
    if (endpoints.length === 1) return first.failure;
    // Report the first (highest priority) endpoint's failure, and say what else was tried.
    return this.fail(toTarget(first.endpoint), {
      ...first.failure,
      message: `${first.failure.message} Also tried ${endpoints.slice(1).map((e) => e.baseUrl).join(', ')}.`,
    });
  }

  /** One attempt against an endpoint with every credential it has. Undefined on success, else why it failed. */
  private async tryEndpoint(endpoint: AdmiralEndpoint): Promise<Failure | undefined> {
    const credentials = [...(await this.storedCredentials(endpoint.baseUrl)), ...endpoint.credentials];
    this._Endpoint = endpoint;
    return this.attempt(toTarget(endpoint), credentials);
  }

  private async storedCredentials(baseUrl: string): Promise<EndpointCredential[]> {
    const raw = await this._Deps.secrets.get(credentialSecretKey(baseUrl));
    if (!raw) return [];
    try {
      const parsed = JSON.parse(raw) as { secret?: unknown; header?: unknown };
      if (typeof parsed.secret !== 'string' || !parsed.secret) return [];
      const header = parsed.header === 'X-Api-Key' || parsed.header === 'X-Token' ? parsed.header : 'Bearer';
      return [{ secret: parsed.secret, header, origin: header === 'X-Token' ? 'session from signing in' : 'key entered in VS Code' }];
    } catch {
      return [];
    }
  }

  private async attempt(target: ConnectionTarget, credentials: EndpointCredential[]): Promise<Failure | undefined> {
    await this.setState({ phase: 'connecting', target }, undefined);
    this._Deps.log.info(`[connect] connecting to ${target.baseUrl} (${target.kind}, from ${target.source})`);
    useAdmiral(target.baseUrl, undefined);

    let serverVersion: string | null = null;
    let contractVersion: string | null = null;
    try {
      const health = await readHealth();
      serverVersion = health.version;
      contractVersion = health.contractVersion;
      this._Deps.log.info(`[connect] health ${health.status ?? 'unknown'}, Armada ${serverVersion ?? 'unknown'}, contract ${contractVersion ?? 'not reported'}`);
    } catch (err) {
      return this.fail(target, { kind: 'unreachable', summary: 'unreachable', message: describeError(err) });
    }
    const versions = { serverVersion, contractVersion };

    const contract = checkContract(contractVersion, serverVersion);
    if (!contract.compatible) return this.fail(target, { kind: 'incompatible', summary: contract.summary, message: contract.reason }, versions);
    if (contract.reason) this._Deps.log.info(`[connect] ${contract.reason}`);

    if (!credentials.length) {
      return this.fail(target, {
        kind: 'noCredential',
        summary: 'no credential',
        message: target.kind === 'local'
          ? 'The local Admiral has not written an API key to its settings.json yet. Start it once, then run "Armada: Connect".'
          : `No credential is stored for ${target.baseUrl}. Run "Armada: Connect" to enter an API key or token.`,
      }, versions);
    }

    for (const credential of credentials) {
      useAdmiral(target.baseUrl, credential);
      let who: WhoAmIResult;
      try {
        who = await whoami();
      } catch (err) {
        if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
          this._Deps.log.info(`[connect] ${target.baseUrl} rejected the ${credential.origin} as ${credential.header}`);
          continue;
        }
        return this.fail(target, { kind: 'error', summary: 'error', message: describeError(err) }, versions);
      }
      if (!who.user) continue;
      const user = who.user;
      const identity: ConnectionIdentity = {
        email: user.email ?? null,
        tenantId: who.tenant?.id ?? user.tenantId ?? null,
        tenantName: who.tenant?.name ?? null,
        globalAdmin: user.isAdmin === true,
        tenantAdmin: user.isAdmin === true || user.isTenantAdmin === true,
      };
      await this.setState({ phase: 'connected', target, ...versions, identity, credentialOrigin: credential.origin }, credential);
      this._Deps.log.info(`[connect] connected to ${target.baseUrl} with the ${credential.origin} as ${identity.email ?? 'unknown user'}${identity.tenantName ? ` (${identity.tenantName})` : ''}; global admin ${identity.globalAdmin}, tenant admin ${identity.tenantAdmin}`);
      return undefined;
    }
    return this.fail(target, {
      kind: 'unauthorized',
      summary: 'credential rejected',
      message: `${target.baseUrl} rejected ${credentials.length === 1 ? 'the stored credential' : 'every stored credential'} (${unique(credentials.map((c) => c.origin)).join(', ')}).`,
    }, versions);
  }

  private async pick(): Promise<AdmiralEndpoint | undefined> {
    const discovered = this._Discover().catch(() => [] as AdmiralEndpoint[]);
    const items = discovered.then(async (endpoints) => {
      const reachable = await Promise.all(endpoints.map((e) => this._Probe(e.baseUrl).catch(() => false)));
      const current = this.connected ? this._State.target?.baseUrl : undefined;
      const list: PickItem[] = endpoints.map((endpoint, i) => ({
        label: `$(${endpoint.kind === 'local' ? 'home' : 'remote'}) ${endpoint.label}`,
        description: `${endpoint.baseUrl}${current && canonicalBaseUrl(current) === canonicalBaseUrl(endpoint.baseUrl) ? '  (connected)' : ''}`,
        detail: [endpoint.detail, reachable[i] ? 'responding' : 'not responding', endpoint.credentials.length ? 'credential found' : 'will ask for a key'].join('  ·  '),
        endpoint,
      }));
      list.push({ label: '$(edit) Enter an Admiral URL...', detail: 'Any Armada Admiral, for example https://armada.example.com or http://10.0.0.5:7890', enterUrl: true });
      return list;
    });
    const picked = await vscode.window.showQuickPick(items, {
      title: 'Armada: Connect to an Admiral',
      placeHolder: 'Pick an Admiral found on this machine, or enter a URL',
      matchOnDescription: true,
      matchOnDetail: true,
      ignoreFocusOut: true,
    });
    if (!picked) return undefined;
    if (picked.endpoint) return picked.endpoint;
    const url = await vscode.window.showInputBox({
      title: 'Armada: Connect to an Admiral',
      prompt: 'Armada Admiral URL',
      value: this._State.target?.baseUrl ?? '',
      placeHolder: 'https://armada.example.com',
      ignoreFocusOut: true,
      validateInput: (value) => (/^\s*(https?:\/\/)?[^\s/]+/i.test(value) ? undefined : 'Enter a URL such as https://armada.example.com or http://10.0.0.5:7890'),
    });
    if (url === undefined || !url.trim()) return undefined;
    return findEndpoint(await discovered, url) ?? enteredEndpoint(url);
  }

  private async promptForKey(endpoint: AdmiralEndpoint, failure: Failure): Promise<boolean> {
    const how = await vscode.window.showQuickPick([
      { label: '$(account) Sign in with email and password', value: 'password' as const },
      { label: '$(key) Enter an API key or bearer token', value: 'key' as const },
    ], { title: `Armada: sign in to ${endpoint.baseUrl}`, placeHolder: failure.kind === 'unauthorized' ? failure.message : 'No stored credential works for this Admiral' });
    if (!how) return false;
    if (how.value === 'password') return this.signInWithPassword(endpoint);
    const key = await vscode.window.showInputBox({
      title: `Armada: sign in to ${endpoint.baseUrl}`,
      prompt: failure.kind === 'unauthorized' ? `${failure.message} Enter an API key or bearer token.` : 'API key or bearer token',
      password: true,
      ignoreFocusOut: true,
    });
    if (key === undefined) return false;
    const secret = key.trim();
    if (!secret) {
      void vscode.window.showErrorMessage('Armada: an API key or token is required.');
      return false;
    }
    const entered: EndpointCredential[] = [
      { secret, header: 'Bearer', origin: 'key entered in VS Code' },
      { secret, header: 'X-Api-Key', origin: 'key entered in VS Code' },
    ];
    const result = await this.exclusive(async () => {
      this._Endpoint = endpoint;
      return this.attempt(toTarget(endpoint), entered);
    });
    if (result) {
      void vscode.window.showErrorMessage(`Armada: could not connect to ${endpoint.baseUrl}. ${result.message}`);
      return false;
    }
    await this._Deps.secrets.store(credentialSecretKey(endpoint.baseUrl), JSON.stringify({ secret, header: this._Credential?.header ?? 'Bearer' }));
    await this._Deps.globalState?.update(ENDPOINT_KEY, endpoint.baseUrl);
    void vscode.window.showInformationMessage(`Armada: connected to ${endpoint.baseUrl}.`);
    return true;
  }

  /** Signs in with email and password (picking the tenant when the email has several) and keeps the session token. */
  private async signInWithPassword(endpoint: AdmiralEndpoint): Promise<boolean> {
    const email = await vscode.window.showInputBox({ title: `Armada: sign in to ${endpoint.baseUrl} (1/2)`, prompt: 'Email', placeHolder: 'admin@armada', ignoreFocusOut: true });
    if (!email?.trim()) return false;
    useAdmiral(endpoint.baseUrl, undefined);
    const tenants = (await lookupTenants(email.trim()).catch(() => undefined))?.tenants ?? [];
    let tenantId: string | undefined = tenants[0]?.id;
    if (tenants.length > 1) {
      tenantId = (await vscode.window.showQuickPick(tenants.map((t) => ({ label: t.name, description: t.id, id: t.id })), { title: 'Armada: tenant', placeHolder: 'Which tenant?' }))?.id;
      if (!tenantId) return false;
    }
    if (!tenantId) {
      void vscode.window.showErrorMessage(`Armada: no tenant has the user ${email.trim()}.`);
      return false;
    }
    const password = await vscode.window.showInputBox({ title: `Armada: sign in to ${endpoint.baseUrl} (2/2)`, prompt: `Password for ${email.trim()}`, password: true, ignoreFocusOut: true });
    if (password === undefined) return false;
    let token: string | null = null;
    try {
      const result = await authenticate({ email: email.trim(), password, tenantId });
      token = result.success ? result.token : null;
      if (result.passwordChangeRequired) void vscode.window.showWarningMessage('Armada: this account still uses its default password. Change it with "Armada: Change Password".');
    } catch (err) {
      void vscode.window.showErrorMessage(`Armada: sign-in failed. ${describeError(err)}`);
      return false;
    }
    if (!token) {
      void vscode.window.showErrorMessage('Armada: the email or password is not right.');
      return false;
    }
    const credential: EndpointCredential = { secret: token, header: 'X-Token', origin: `session for ${email.trim()}` };
    const result = await this.exclusive(async () => {
      this._Endpoint = endpoint;
      return this.attempt(toTarget(endpoint), [credential]);
    });
    if (result) {
      void vscode.window.showErrorMessage(`Armada: could not connect to ${endpoint.baseUrl}. ${result.message}`);
      return false;
    }
    await this._Deps.secrets.store(credentialSecretKey(endpoint.baseUrl), JSON.stringify({ secret: token, header: 'X-Token' }));
    await this._Deps.globalState?.update(ENDPOINT_KEY, endpoint.baseUrl);
    void vscode.window.showInformationMessage(`Armada: signed in to ${endpoint.baseUrl} as ${email.trim()}.`);
    return true;
  }

  private async fail(target: ConnectionTarget | undefined, failure: Failure, versions: { serverVersion?: string | null; contractVersion?: string | null } = {}): Promise<Failure> {
    useAdmiral(undefined, undefined);
    await this.setState({ phase: 'failed', target, failure, ...versions }, undefined);
    this._Deps.log.warn(`[connect] ${target ? `${target.baseUrl}: ` : ''}${failure.message}`);
    return failure;
  }

  private explain(): void {
    const failure = this._State.failure;
    if (!failure) return;
    const actions: string[] = [];
    if (failure.kind === 'unreachable' || failure.kind === 'error') actions.push('Retry');
    actions.push('Connect...', 'Show Output');
    void vscode.window.showErrorMessage(`Armada: ${failure.message}`, ...actions).then((choice) => {
      if (choice === 'Retry') void this.reconnect();
      else if (choice === 'Connect...') void this.connect({ interactive: true });
      else if (choice === 'Show Output') void vscode.commands.executeCommand('armada.showOutput');
    });
  }

  private async setState(state: ConnectionState, credential: HostCredential | undefined): Promise<void> {
    this._State = state;
    this._Credential = state.phase === 'connected' ? credential : undefined;
    const identity = state.phase === 'connected' ? state.identity : undefined;
    await vscode.commands.executeCommand('setContext', CONNECTED_CONTEXT_KEY, state.phase === 'connected');
    await vscode.commands.executeCommand('setContext', STATE_CONTEXT_KEY, state.phase);
    await vscode.commands.executeCommand('setContext', GLOBAL_ADMIN_CONTEXT_KEY, identity?.globalAdmin === true);
    await vscode.commands.executeCommand('setContext', TENANT_ADMIN_CONTEXT_KEY, identity?.tenantAdmin === true);
    this._OnDidChange.fire(state);
  }
}

/** True when the Admiral answers GET /api/v1/status/health within 2.5 seconds. Never throws. */
export async function probeHealth(baseUrl: string): Promise<boolean> {
  try {
    const response = await fetch(`${normalizeBaseUrl(baseUrl)}/api/v1/status/health`, { signal: AbortSignal.timeout(2500) });
    return response.ok;
  } catch {
    return false;
  }
}

function toTarget(endpoint: AdmiralEndpoint): ConnectionTarget {
  return {
    kind: endpoint.kind,
    baseUrl: endpoint.baseUrl,
    label: endpoint.kind === 'local' ? 'local' : hostLabel(endpoint.baseUrl),
    source: endpoint.detail,
  };
}

function findEndpoint(endpoints: AdmiralEndpoint[], url: string): AdmiralEndpoint | undefined {
  const key = canonicalBaseUrl(normalizeBaseUrl(url));
  return endpoints.find((e) => canonicalBaseUrl(e.baseUrl) === key);
}

/** An endpoint for a URL that was typed in (or picked earlier and no longer discovered). */
function enteredEndpoint(url: string): AdmiralEndpoint {
  const baseUrl = normalizeBaseUrl(url);
  const local = discoverLocalAdmiral();
  const isLocal = isLocalAdmiral(local) && isLoopbackUrl(baseUrl, local.port);
  return {
    baseUrl,
    kind: isLocal ? 'local' : 'remote',
    label: isLocal ? 'Local Admiral' : hostLabel(baseUrl),
    sources: ['saved'],
    detail: isLocal ? local.settingsPath : 'entered in VS Code',
    credentials: isLocal && local.apiKey ? [{ secret: local.apiKey, header: 'X-Api-Key', origin: 'local Admiral API key' }] : [],
  };
}

function hostLabel(baseUrl: string): string {
  try {
    return new URL(baseUrl).host || baseUrl;
  } catch {
    return baseUrl;
  }
}

function unique(values: string[]): string[] {
  return values.filter((v, i) => values.indexOf(v) === i);
}
