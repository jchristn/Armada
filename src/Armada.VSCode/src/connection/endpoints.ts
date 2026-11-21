/*
 * Finds every Admiral this machine already knows about, with the credentials already stored for it, so Connect can
 * offer them and startup can connect with no prompt (VSCODE_EXTENSION_PLAN.md 4.4). Sources, in priority order:
 *
 * 1. Harbor: ~/.armada-harbor/settings.json (ARMADA_HARBOR_HOME moves it). ServerLinkUrl names the Admiral
 *    (ws://host:port/v1.0/harbor/connect, whose REST base is http://host:port, as LocalAdmiralInfo.RestBaseUrl maps it)
 *    and AccessKey is an ordinary Armada credential the REST API accepts as a bearer token.
 * 2. The local Admiral: ~/.armada/settings.json (localDiscovery.ts): admiralPort and the local apiKey (X-Api-Key).
 * 3. The TUI's server profiles: ~/.armada/tui.json (ARMADA_TUI_PREFERENCES moves it), with each profile's session
 *    token from the TUI's credential store (macOS Keychain or libsecret service "armada-tui", account
 *    "profile:<name>@<url>", falling back to tui-credentials.json next to tui.json).
 *
 * Endpoints that resolve to the same Admiral are merged, keeping every credential in order. Nothing here throws, and
 * no secret is ever logged or returned in a description. No `vscode` import, so it is unit tested directly.
 */

import { execFile } from 'child_process';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import type { AuthHeaderMode } from '../transport/hostClient';
import { armadaDataDirectory, discoverLocalAdmiral, DiscoveryEnvironment, isLocalAdmiral, isLoopbackUrl } from './localDiscovery';

/** Environment variable that moves Harbor's settings directory (Armada.Harbor HarborAppSettings). */
export const HARBOR_HOME_ENV = 'ARMADA_HARBOR_HOME';

/** Environment variable that moves the TUI preferences file (Armada.Tui TuiPaths). */
export const TUI_PREFERENCES_ENV = 'ARMADA_TUI_PREFERENCES';

/** Environment variable that moves the TUI credential file (Armada.Tui TuiPaths). */
export const TUI_CREDENTIALS_ENV = 'ARMADA_TUI_CREDENTIALS';

/** Keychain / libsecret service the TUI stores session tokens under. */
export const TUI_CREDENTIAL_SERVICE = 'armada-tui';

/** Where an endpoint was found. */
export type EndpointSource = 'harbor' | 'local' | 'tui' | 'saved';

/** One credential to try against an endpoint. */
export interface EndpointCredential {
  /** Never logged or shown. */
  secret: string;
  header: AuthHeaderMode;
  /** What it is, for the log and the picker ("Harbor access key"). */
  origin: string;
}

/** An Admiral this machine knows about. */
export interface AdmiralEndpoint {
  /** REST base URL, normalized (scheme://host:port, no path). */
  baseUrl: string;
  /** local when it is the Admiral of ~/.armada/settings.json. */
  kind: 'local' | 'remote';
  /** Short name for the picker and the status bar. */
  label: string;
  sources: EndpointSource[];
  /** Where it was found, for the picker ("Harbor (running), ~/.armada-harbor/settings.json"). */
  detail: string;
  /** In the order to try them. */
  credentials: EndpointCredential[];
  /** True when the Harbor that names this Admiral is running. */
  harborRunning?: boolean;
  /** Dashboard URL configured for this Admiral (Harbor's DashboardUrl), when it is not <baseUrl>/dashboard. */
  dashboardUrl?: string;
}

/** What Harbor's settings file says. */
export interface HarborLink {
  settingsPath: string;
  /** REST base URL of the Admiral Harbor links to. */
  baseUrl: string;
  accessKey: string | null;
  dashboardUrl: string | null;
  /** This machine's Harbor id (hbr_), or null. */
  harborId: string | null;
}

/** A TUI server profile. */
export interface TuiProfile {
  name: string;
  url: string;
  active: boolean;
  followsLocalAdmiral: boolean;
}

/** Inputs, injectable for tests. */
export interface EndpointEnvironment extends DiscoveryEnvironment {
  platform?: NodeJS.Platform;
  /** True when an Armada Harbor process is running. */
  isHarborRunning?: () => Promise<boolean>;
  /** Reads a secret from the OS credential store, or null. */
  readOsSecret?: (service: string, account: string) => Promise<string | null>;
}

/** Harbor's settings directory: ARMADA_HARBOR_HOME when set, else ~/.armada-harbor. */
export function harborDirectory(environment: DiscoveryEnvironment = {}): string {
  const env = environment.env ?? process.env;
  const override = env[HARBOR_HOME_ENV];
  if (override && override.trim()) return path.resolve(override.trim());
  return path.join(environment.homeDir ?? os.homedir(), '.armada-harbor');
}

/** The TUI preferences file: ARMADA_TUI_PREFERENCES when set, else <data dir>/tui.json. */
export function tuiPreferencesPath(environment: DiscoveryEnvironment = {}): string {
  const env = environment.env ?? process.env;
  const override = env[TUI_PREFERENCES_ENV];
  if (override && override.trim()) return path.resolve(override.trim());
  return path.join(armadaDataDirectory(environment), 'tui.json');
}

/** The TUI credential file: ARMADA_TUI_CREDENTIALS when set, else tui-credentials.json next to tui.json. */
export function tuiCredentialsPath(environment: DiscoveryEnvironment = {}): string {
  const env = environment.env ?? process.env;
  const override = env[TUI_CREDENTIALS_ENV];
  if (override && override.trim()) return path.resolve(override.trim());
  return path.join(path.dirname(tuiPreferencesPath(environment)), 'tui-credentials.json');
}

/** The REST base URL for a Harbor link URL: ws becomes http, wss becomes https, and the path is dropped. */
export function restBaseFromLinkUrl(linkUrl: string): string | null {
  let parsed: URL;
  try {
    parsed = new URL(linkUrl.trim());
  } catch {
    return null;
  }
  const scheme = parsed.protocol === 'wss:' || parsed.protocol === 'https:' ? 'https' : parsed.protocol === 'ws:' || parsed.protocol === 'http:' ? 'http' : null;
  if (!scheme || !parsed.host) return null;
  return `${scheme}://${parsed.host}`;
}

/** Reads Harbor's settings file. Null when there is none or it names no Admiral. Never throws. */
export function discoverHarbor(environment: DiscoveryEnvironment = {}): HarborLink | null {
  const settingsPath = path.join(harborDirectory(environment), 'settings.json');
  const settings = readJsonObject(settingsPath, environment);
  if (!settings) return null;
  const link = stringProperty(settings, 'ServerLinkUrl');
  const baseUrl = link ? restBaseFromLinkUrl(link) : null;
  if (!baseUrl) return null;
  return { settingsPath, baseUrl, accessKey: stringProperty(settings, 'AccessKey'), dashboardUrl: stringProperty(settings, 'DashboardUrl'), harborId: stringProperty(settings, 'HarborId') };
}

/** Reads the TUI's server profiles. Empty when there is no tui.json. Never throws. */
export function discoverTuiProfiles(environment: DiscoveryEnvironment = {}): TuiProfile[] {
  const prefs = readJsonObject(tuiPreferencesPath(environment), environment);
  if (!prefs) return [];
  const profiles = property(prefs, 'Profiles');
  if (!Array.isArray(profiles)) return [];
  const active = stringProperty(prefs, 'ActiveProfile');
  const result: TuiProfile[] = [];
  for (const raw of profiles) {
    if (!raw || typeof raw !== 'object') continue;
    const profile = raw as Record<string, unknown>;
    const name = stringProperty(profile, 'Name');
    const url = stringProperty(profile, 'Url');
    if (!name || !url) continue;
    result.push({ name, url, active: name === active, followsLocalAdmiral: property(profile, 'FollowsLocalAdmiral') === true });
  }
  return result;
}

/** The TUI's credential store account for a profile (Armada.Tui SessionService.CredentialKey). */
export function tuiCredentialKey(profile: TuiProfile): string {
  return `profile:${profile.name}@${profile.url}`;
}

/** A comparable form of a base URL: loopback hosts become 127.0.0.1 and default ports are explicit. */
export function canonicalBaseUrl(url: string): string {
  let parsed: URL;
  try {
    parsed = new URL(/^[a-z]+:\/\//i.test(url.trim()) ? url.trim() : `http://${url.trim()}`);
  } catch {
    return url.trim().toLowerCase();
  }
  const host = isLoopbackUrl(`${parsed.protocol}//${parsed.host}`) ? '127.0.0.1' : parsed.hostname.toLowerCase();
  const port = parsed.port || (parsed.protocol === 'https:' ? '443' : '80');
  return `${parsed.protocol}//${host}:${port}`;
}

/** Every Admiral this machine knows about, merged and in priority order. Never throws. */
export async function discoverEndpoints(environment: EndpointEnvironment = {}): Promise<AdmiralEndpoint[]> {
  const endpoints: AdmiralEndpoint[] = [];
  const add = (endpoint: AdmiralEndpoint): void => {
    const key = canonicalBaseUrl(endpoint.baseUrl);
    const existing = endpoints.find((e) => canonicalBaseUrl(e.baseUrl) === key);
    if (!existing) {
      endpoints.push(endpoint);
      return;
    }
    for (const source of endpoint.sources) if (!existing.sources.includes(source)) existing.sources.push(source);
    for (const credential of endpoint.credentials) {
      if (!existing.credentials.some((c) => c.secret === credential.secret && c.header === credential.header)) existing.credentials.push(credential);
    }
    if (endpoint.kind === 'local') existing.kind = 'local';
    existing.harborRunning = existing.harborRunning || endpoint.harborRunning;
    existing.dashboardUrl ??= endpoint.dashboardUrl;
    existing.detail = `${existing.detail}; ${endpoint.detail}`;
  };

  const local = discoverLocalAdmiral(environment);
  const localKey = isLocalAdmiral(local) ? canonicalBaseUrl(local.baseUrl) : undefined;

  const harbor = discoverHarbor(environment);
  if (harbor) {
    const running = await safe(() => (environment.isHarborRunning ?? (() => isHarborRunning(environment.platform)))(), false);
    const isLocal = localKey !== undefined && canonicalBaseUrl(harbor.baseUrl) === localKey;
    add({
      baseUrl: harbor.baseUrl,
      kind: isLocal ? 'local' : 'remote',
      label: isLocal ? 'Local Admiral' : `Harbor's Admiral (${hostOf(harbor.baseUrl)})`,
      sources: ['harbor'],
      detail: `Harbor${running ? ' (running)' : ''}, ${displayPath(harbor.settingsPath, environment)}`,
      credentials: harbor.accessKey
        ? [
            { secret: harbor.accessKey, header: 'Bearer', origin: 'Harbor access key' },
            { secret: harbor.accessKey, header: 'X-Api-Key', origin: 'Harbor access key' },
          ]
        : [],
      harborRunning: running,
      dashboardUrl: harbor.dashboardUrl ?? undefined,
    });
  }

  if (isLocalAdmiral(local)) {
    add({
      baseUrl: local.baseUrl,
      kind: 'local',
      label: 'Local Admiral',
      sources: ['local'],
      detail: displayPath(local.settingsPath, environment),
      credentials: local.apiKey ? [{ secret: local.apiKey, header: 'X-Api-Key', origin: 'local Admiral API key' }] : [],
    });
  }

  const profiles = discoverTuiProfiles(environment).sort((a, b) => Number(b.active) - Number(a.active));
  for (const profile of profiles) {
    const baseUrl = profile.followsLocalAdmiral && isLocalAdmiral(local) ? local.baseUrl : normalizeUrl(profile.url);
    if (!baseUrl) continue;
    const token = await readTuiToken(profile, environment);
    const isLocal = localKey !== undefined && canonicalBaseUrl(baseUrl) === localKey;
    add({
      baseUrl,
      kind: isLocal ? 'local' : 'remote',
      label: isLocal ? 'Local Admiral' : `${profile.name} (TUI profile)`,
      sources: ['tui'],
      detail: `TUI profile "${profile.name}"${profile.active ? ' (active)' : ''}, ${displayPath(tuiPreferencesPath(environment), environment)}`,
      credentials: token
        ? [
            { secret: token, header: 'Bearer', origin: `TUI session for "${profile.name}"` },
            { secret: token, header: 'X-Api-Key', origin: `TUI session for "${profile.name}"` },
          ]
        : [],
    });
  }

  return endpoints;
}

/** True when an Armada Harbor process is running. Never throws. */
export async function isHarborRunning(platform: NodeJS.Platform = process.platform): Promise<boolean> {
  if (platform === 'win32') {
    const out = await run('tasklist', ['/FI', 'IMAGENAME eq Armada.Harbor.exe', '/NH']);
    return out !== null && /Armada\.Harbor\.exe/i.test(out);
  }
  const out = await run('pgrep', ['-f', 'Armada.Harbor']);
  return out !== null && out.trim().length > 0;
}

/** Reads a secret from the macOS Keychain or libsecret. Null when absent, unsupported or failing. Never throws. */
export async function readOsSecret(service: string, account: string, platform: NodeJS.Platform = process.platform): Promise<string | null> {
  let out: string | null = null;
  if (platform === 'darwin') out = await run('/usr/bin/security', ['find-generic-password', '-s', service, '-a', account, '-w']);
  else if (platform === 'linux') out = await run('secret-tool', ['lookup', 'service', service, 'account', account]);
  const secret = out?.replace(/\r?\n$/, '') ?? '';
  return secret.trim() ? secret : null;
}

async function readTuiToken(profile: TuiProfile, environment: EndpointEnvironment): Promise<string | null> {
  const key = tuiCredentialKey(profile);
  const reader = environment.readOsSecret ?? ((service: string, account: string) => readOsSecret(service, account, environment.platform));
  const fromOs = await safe(() => reader(TUI_CREDENTIAL_SERVICE, key), null);
  if (fromOs) return fromOs;
  const file = readJsonObject(tuiCredentialsPath(environment), environment);
  const value = file ? file[key] : undefined;
  return typeof value === 'string' && value.trim() ? value : null;
}

function run(command: string, args: string[]): Promise<string | null> {
  return new Promise((resolve) => {
    try {
      execFile(command, args, { timeout: 5000, windowsHide: true }, (err, stdout) => resolve(err ? null : String(stdout)));
    } catch {
      resolve(null);
    }
  });
}

async function safe<T>(fn: () => Promise<T>, fallback: T): Promise<T> {
  try {
    return await fn();
  } catch {
    return fallback;
  }
}

function readJsonObject(file: string, environment: DiscoveryEnvironment): Record<string, unknown> | null {
  const read = environment.readFile ?? ((f: string) => fs.readFileSync(f, 'utf8'));
  try {
    const parsed: unknown = JSON.parse(read(file).replace(/^﻿/, ''));
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

function normalizeUrl(url: string): string | null {
  try {
    const parsed = new URL(/^[a-z]+:\/\//i.test(url.trim()) ? url.trim() : `http://${url.trim()}`);
    return `${parsed.protocol}//${parsed.host}`;
  } catch {
    return null;
  }
}

function hostOf(baseUrl: string): string {
  try {
    return new URL(baseUrl).host;
  } catch {
    return baseUrl;
  }
}

function displayPath(file: string, environment: DiscoveryEnvironment): string {
  const home = environment.homeDir ?? os.homedir();
  return file.startsWith(home + path.sep) ? `~${file.slice(home.length)}` : file;
}

// The .NET settings files are read case-insensitively, so accept any casing.
function property(settings: Record<string, unknown>, name: string): unknown {
  if (name in settings) return settings[name];
  const lower = name.toLowerCase();
  const key = Object.keys(settings).find((k) => k.toLowerCase() === lower);
  return key ? settings[key] : undefined;
}

function stringProperty(settings: Record<string, unknown>, name: string): string | null {
  const value = property(settings, name);
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}
