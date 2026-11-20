/*
 * The connection to one Admiral (VSCODE_EXTENSION_PLAN.md 4.4, modeled on Mux's src/server/lifecycle.ts):
 *
 * 1. Target: with `armada.url` empty, the local Admiral from ~/.armada/settings.json (port and API key, no prompt);
 *    otherwise that URL with the key kept in SecretStorage (or the local key when the URL is the local Admiral).
 * 2. Probe GET /api/v1/status/health, check the contract major, then prove the credential with GET /api/v1/whoami and
 *    set the role context keys.
 * 3. Concurrent callers share one in-flight attempt.
 *
 * Starting an Admiral is not done here yet; it needs the bundled binaries (plan 4.7).
 */

import * as vscode from 'vscode';
import { ApiError, whoami } from '@dashboard/api/client';
import type { WhoAmIResult } from '@dashboard/types/models';
import { ArmadaSettings, readSettings } from '../config';
import { describeError, NotConnectedError } from '../transport/errors';
import { HostCredential, normalizeBaseUrl, readHealth, useAdmiral } from '../transport/hostClient';
import { checkContract } from './contract';
import { discoverLocalAdmiral, isLocalAdmiral, isLoopbackUrl, LocalAdmiral, LocalAdmiralMissing } from './localDiscovery';

/** SecretStorage key of the remote API key or token. */
export const API_KEY_SECRET = 'armada.apiKey';

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

/** Connection phase. */
export type ConnectionPhase = 'disconnected' | 'connecting' | 'connected' | 'failed';

/** Why the last attempt failed. */
export type FailureKind = 'noLocalAdmiral' | 'noCredential' | 'unreachable' | 'unauthorized' | 'incompatible' | 'error';

/** The Admiral being talked to. */
export interface ConnectionTarget {
  kind: 'local' | 'remote';
  baseUrl: string;
  /** "local" or host:port. */
  label: string;
  /** Where the target came from: the settings file path, or armada.url. */
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
}

/** What surfaces need while connected. */
export interface ActiveConnection {
  target: ConnectionTarget;
  baseUrl: string;
  /** Tenant filter from armada.tenant, if any. */
  tenant: string | undefined;
  /** The dashboard base URL (armada.dashboardUrl, or the Admiral's /dashboard). */
  dashboardUrl: string;
  /** The credential for the WebSocket `token` query parameter. */
  socketToken: string | undefined;
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
  discover?: () => LocalAdmiral | LocalAdmiralMissing;
}

interface ResolvedTarget {
  target: ConnectionTarget;
  credential: HostCredential | undefined;
}

/** Connection lifecycle for the extension. */
export class ConnectionManager implements vscode.Disposable {
  private readonly _Deps: ConnectionManagerDeps;
  private readonly _GetSettings: () => ArmadaSettings;
  private readonly _Discover: () => LocalAdmiral | LocalAdmiralMissing;
  private readonly _OnDidChange = new vscode.EventEmitter<ConnectionState>();
  private _State: ConnectionState = { phase: 'disconnected' };
  private _Credential: HostCredential | undefined;
  private _InFlight: Promise<boolean> | undefined;

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
    this._Discover = deps.discover ?? (() => discoverLocalAdmiral());
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
      dashboardUrl: settings.dashboardUrl || `${target.baseUrl}/dashboard`,
      socketToken: this._Credential?.secret,
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

  /** The dashboard base URL for links: armada.dashboardUrl, the connected Admiral's /dashboard, or the target's. */
  public dashboardUrl(): string {
    const settings = this._GetSettings();
    if (settings.dashboardUrl) return settings.dashboardUrl;
    const target = this._State.target ?? this.tryResolve()?.target;
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
   * Connects to the configured target. Concurrent callers share one attempt. Interactive attempts explain a failure
   * with follow-up actions; others only log it. Never throws.
   */
  public async connect(options: { interactive?: boolean } = {}): Promise<boolean> {
    const interactive = options.interactive === true;
    if (interactive) await this._Deps.globalState?.update(MANUAL_DISCONNECT_KEY, undefined);
    return this.exclusive(async () => {
      const resolved = await this.resolveTarget();
      if ('failure' in resolved) {
        await this.fail(resolved.target, resolved.failure.kind, resolved.failure.summary, resolved.failure.message);
        if (interactive) this.explain();
        return false;
      }
      const ok = await this.attempt(resolved);
      if (!ok && interactive) this.explain();
      return ok;
    });
  }

  /** Reconnects to the configured target (same as Connect). */
  public reconnect(): Promise<boolean> {
    return this.connect({ interactive: true });
  }

  /**
   * Prompts for a remote Admiral URL and an API key or token, verifies them, then stores the key in SecretStorage and
   * the URL in armada.url. Returns true on success. Never throws.
   */
  public async connectRemote(): Promise<boolean> {
    const settings = this._GetSettings();
    const url = await vscode.window.showInputBox({
      title: 'Armada: Connect to a Remote Admiral (1/2)',
      prompt: 'Armada Admiral URL',
      value: settings.url || 'http://localhost:7890',
      ignoreFocusOut: true,
      validateInput: (value) => (/^\s*(https?:\/\/)?[^\s/]+/i.test(value) ? undefined : 'Enter a URL such as https://armada.example.com or http://10.0.0.5:7890'),
    });
    if (url === undefined) return false;
    const existing = await this._Deps.secrets.get(API_KEY_SECRET);
    const key = await vscode.window.showInputBox({
      title: 'Armada: Connect to a Remote Admiral (2/2)',
      prompt: existing ? 'API key or bearer token (leave empty to keep the stored one)' : 'API key or bearer token',
      password: true,
      ignoreFocusOut: true,
    });
    if (key === undefined) return false;
    const secret = key.trim() || existing || '';
    if (!secret) {
      void vscode.window.showErrorMessage('Armada: an API key or token is required.');
      return false;
    }
    const baseUrl = normalizeBaseUrl(url);
    await this._Deps.globalState?.update(MANUAL_DISCONNECT_KEY, undefined);
    const ok = await this.exclusive(() => this.attempt({
      target: { kind: 'remote', baseUrl, label: hostLabel(baseUrl), source: 'armada.url' },
      credential: { secret, header: settings.authHeader },
    }));
    if (!ok) {
      void vscode.window.showErrorMessage(`Armada: could not connect to ${baseUrl}. ${this._State.failure?.message ?? ''}`.trim());
      return false;
    }
    await this._Deps.secrets.store(API_KEY_SECRET, secret);
    if (normalizeBaseUrl(settings.url || '') !== baseUrl || !settings.url) {
      await vscode.workspace.getConfiguration('armada').update('url', baseUrl, vscode.ConfigurationTarget.Global);
    }
    void vscode.window.showInformationMessage(`Armada: connected to ${baseUrl}.`);
    return true;
  }

  /** Clears armada.url so the local Admiral is used, and connects to it. */
  public async useLocal(): Promise<boolean> {
    if (this._GetSettings().url) await vscode.workspace.getConfiguration('armada').update('url', '', vscode.ConfigurationTarget.Global);
    return this.connect({ interactive: true });
  }

  /**
   * Drops the connection, removes a stored remote key from SecretStorage, and remembers the choice so a restart does
   * not reconnect until "Connect".
   */
  public async disconnect(): Promise<void> {
    await this._Deps.globalState?.update(MANUAL_DISCONNECT_KEY, true);
    const hadKey = (await this._Deps.secrets.get(API_KEY_SECRET)) !== undefined;
    if (hadKey) await this._Deps.secrets.delete(API_KEY_SECRET);
    useAdmiral(undefined, undefined);
    await this.setState({ phase: 'disconnected' }, undefined);
    this._Deps.log.info('[connect] disconnected by the user');
    void vscode.window.showInformationMessage(hadKey
      ? 'Armada: disconnected. The API key was removed from secret storage.'
      : 'Armada: disconnected. Run "Armada: Connect" to connect again.');
  }

  /** Reconnects when a connection setting changed (armada.url, armada.authHeader). Never throws. */
  public async onSettingsChanged(): Promise<void> {
    if (this._Deps.globalState?.get<boolean>(MANUAL_DISCONNECT_KEY) === true) return;
    const wanted = this.tryResolve();
    const current = this._State.target;
    if (this.connected && wanted && current && wanted.target.baseUrl === current.baseUrl && wanted.target.kind === current.kind && wanted.credentialHeader === this._Credential?.header) return;
    await this.connect({ interactive: false });
  }

  public dispose(): void {
    this._OnDidChange.dispose();
  }

  private exclusive(run: () => Promise<boolean>): Promise<boolean> {
    if (this._InFlight) {
      // Concurrent callers share the attempt already running.
      return this._InFlight;
    }
    this._InFlight = run().catch((err) => {
      this._Deps.log.error(`[connect] unexpected failure: ${describeError(err)}`);
      return false;
    }).finally(() => {
      this._InFlight = undefined;
    });
    return this._InFlight;
  }

  /** Synchronous target resolution for comparisons (no SecretStorage read). */
  private tryResolve(): { target: ConnectionTarget; credentialHeader: string | undefined } | undefined {
    const settings = this._GetSettings();
    if (!settings.url) {
      const local = this._Discover();
      if (!isLocalAdmiral(local)) return undefined;
      return { target: localTarget(local), credentialHeader: 'X-Api-Key' };
    }
    const baseUrl = normalizeBaseUrl(settings.url);
    return { target: { kind: 'remote', baseUrl, label: hostLabel(baseUrl), source: 'armada.url' }, credentialHeader: settings.authHeader };
  }

  private async resolveTarget(): Promise<ResolvedTarget | { target?: ConnectionTarget; failure: { kind: FailureKind; summary: string; message: string } }> {
    const settings = this._GetSettings();
    if (!settings.url) {
      const local = this._Discover();
      if (!isLocalAdmiral(local)) return { failure: { kind: 'noLocalAdmiral', summary: 'no local Admiral', message: local.reason } };
      this._Deps.log.info(`[connect] local Admiral from ${local.settingsPath}: ${local.baseUrl}${local.apiKey ? ', local API key found' : ', no local API key yet'}`);
      return { target: localTarget(local), credential: local.apiKey ? { secret: local.apiKey, header: 'X-Api-Key' } : undefined };
    }
    const baseUrl = normalizeBaseUrl(settings.url);
    const target: ConnectionTarget = { kind: 'remote', baseUrl, label: hostLabel(baseUrl), source: 'armada.url' };
    const stored = await this._Deps.secrets.get(API_KEY_SECRET);
    if (stored) return { target, credential: { secret: stored, header: settings.authHeader } };
    const local = this._Discover();
    if (isLocalAdmiral(local) && local.apiKey && isLoopbackUrl(baseUrl, local.port)) {
      this._Deps.log.info(`[connect] ${baseUrl} is the local Admiral; using the local API key from ${local.settingsPath}`);
      return { target: { ...target, kind: 'local', label: 'local' }, credential: { secret: local.apiKey, header: 'X-Api-Key' } };
    }
    return { target, credential: undefined };
  }

  private async attempt(resolved: ResolvedTarget): Promise<boolean> {
    const { target, credential } = resolved;
    await this.setState({ phase: 'connecting', target }, undefined);
    this._Deps.log.info(`[connect] connecting to ${target.baseUrl} (${target.kind}, from ${target.source})`);
    useAdmiral(target.baseUrl, credential);

    let serverVersion: string | null = null;
    let contractVersion: string | null = null;
    try {
      const health = await readHealth();
      serverVersion = health.version;
      contractVersion = health.contractVersion;
      this._Deps.log.info(`[connect] health ${health.status ?? 'unknown'}, Armada ${serverVersion ?? 'unknown'}, contract ${contractVersion ?? 'none'}`);
    } catch (err) {
      return this.fail(target, 'unreachable', 'unreachable', describeError(err));
    }

    const contract = checkContract(contractVersion, serverVersion);
    if (!contract.compatible) return this.fail(target, 'incompatible', contract.summary, contract.reason, { serverVersion, contractVersion });

    if (!credential) {
      const message = target.kind === 'local'
        ? 'The local Admiral has not written an API key to its settings.json yet. Start it once, then run "Armada: Connect".'
        : `No API key is stored for ${target.baseUrl}. Run "Armada: Connect to a Remote Admiral" to enter one.`;
      return this.fail(target, 'noCredential', 'no credential', message, { serverVersion, contractVersion });
    }

    let who: WhoAmIResult;
    try {
      who = await whoami();
    } catch (err) {
      const unauthorized = err instanceof ApiError && err.status === 401;
      return this.fail(target, unauthorized ? 'unauthorized' : 'error', unauthorized ? 'credential rejected' : 'error', describeError(err), { serverVersion, contractVersion });
    }

    const user = who.user;
    const identity: ConnectionIdentity = {
      email: user?.email ?? null,
      tenantId: who.tenant?.id ?? user?.tenantId ?? null,
      tenantName: who.tenant?.name ?? null,
      globalAdmin: user?.isAdmin === true,
      tenantAdmin: user?.isAdmin === true || user?.isTenantAdmin === true,
    };
    await this.setState({ phase: 'connected', target, serverVersion, contractVersion, identity }, credential);
    this._Deps.log.info(`[connect] connected to ${target.baseUrl} as ${identity.email ?? 'unknown user'}${identity.tenantName ? ` (${identity.tenantName})` : ''}; global admin ${identity.globalAdmin}, tenant admin ${identity.tenantAdmin}`);
    return true;
  }

  private async fail(target: ConnectionTarget | undefined, kind: FailureKind, summary: string, message: string, versions: { serverVersion?: string | null; contractVersion?: string | null } = {}): Promise<false> {
    useAdmiral(undefined, undefined);
    await this.setState({ phase: 'failed', target, failure: { kind, summary, message }, ...versions }, undefined);
    this._Deps.log.warn(`[connect] ${target ? `${target.baseUrl}: ` : ''}${message}`);
    return false;
  }

  private explain(): void {
    const failure = this._State.failure;
    if (!failure) return;
    const actions: string[] = [];
    if (failure.kind === 'unreachable' || failure.kind === 'error') actions.push('Retry');
    if (failure.kind === 'noLocalAdmiral' || failure.kind === 'unreachable' || failure.kind === 'noCredential' || failure.kind === 'unauthorized') actions.push('Connect to a Remote Admiral');
    actions.push('Show Output');
    void vscode.window.showErrorMessage(`Armada: ${failure.message}`, ...actions).then((choice) => {
      if (choice === 'Retry') void this.connect({ interactive: true });
      else if (choice === 'Connect to a Remote Admiral') void this.connectRemote();
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

function localTarget(local: LocalAdmiral): ConnectionTarget {
  return { kind: 'local', baseUrl: local.baseUrl, label: 'local', source: local.settingsPath };
}

function hostLabel(baseUrl: string): string {
  try {
    return new URL(baseUrl).host || baseUrl;
  } catch {
    return baseUrl;
  }
}
