/*
 * The extension host's side of the shared dashboard API client (@dashboard/api/client, the same module the dashboard
 * and the mobile app use). This file points the shared client at one Admiral with the extension's credential and adds
 * the few things the shared client does not have (README "Shared client gaps"). Every other call is imported from
 * @dashboard/api/client directly. No `vscode` import, so it is unit tested with a stubbed global fetch.
 */

import { configureClient, getBlackBox, getHealth, getMissionBlackBox, getVoyageBlackBox, listVessels, setAuthToken } from '@dashboard/api/client';
import { fetchAllPages } from '@dashboard/api/paging';
import { askPrefillPath } from '@dashboard/lib/askNavigation';
import type { BlackBoxEntry } from '@dashboard/types/blackbox';
import type { Vessel } from '@dashboard/types/models';

/** How a credential is presented to the Admiral. */
export type AuthHeaderMode = 'Bearer' | 'X-Api-Key' | 'X-Token';

/** A credential the host sends with every request. */
export interface HostCredential {
  /** The API key or bearer token. Never logged. */
  secret: string;
  /** Header that carries it: Authorization: Bearer (credential tokens), X-Api-Key (the settings API key) or X-Token
   * (a session token from signing in with email and password). */
  header: AuthHeaderMode;
}

/** GET /api/v1/status/health, narrowed to what the extension reads. The shared client types it as a plain record. */
export interface AdmiralHealth {
  status: string | null;
  version: string | null;
  contractVersion: string | null;
  admiralPort: number | null;
  mcpPort: number | null;
}

/** The request headers for a credential. */
export function credentialHeaders(credential: HostCredential | undefined): Record<string, string> {
  if (!credential || !credential.secret) return {};
  if (credential.header === 'X-Api-Key') return { 'X-Api-Key': credential.secret };
  if (credential.header === 'X-Token') return { 'X-Token': credential.secret };
  return { Authorization: `Bearer ${credential.secret}` };
}

/**
 * Points the shared client at an Admiral (or at nothing, for a disconnected host). The shared client keeps one target
 * per process, so the extension talks to one Admiral at a time.
 */
export function useAdmiral(baseUrl: string | undefined, credential: HostCredential | undefined): void {
  apiBase = baseUrl ? normalizeBaseUrl(baseUrl) : '';
  apiHeaders = credentialHeaders(credential);
  configureClient({ baseUrl: apiBase, headers: apiHeaders });
  setAuthToken(null);
}

let apiBase = '';
let apiHeaders: Record<string, string> = {};

/** One stage attempt of a mission (GET /api/v1/missions/{id}/attempts), oldest first. */
export interface MissionAttempt {
  outcomeId: string | null;
  attempt: number;
  reworkRound: number;
  captainId: string | null;
  captainName: string | null;
  runtime: string | null;
  model: string | null;
  modelProvider: string | null;
  modelEndpointId: string | null;
  disposition: string | null;
  failureKind: string | null;
  failureReason: string | null;
  runtimeFailureKind: string | null;
  contractRepairKind: string | null;
  isRepair: boolean;
  escalated: boolean;
  nextCaptainId: string | null;
  judgeVerdict: string | null;
  suspectedCause: string | null;
  isEvaluation: boolean;
  startedUtc: string | null;
  endedUtc: string | null;
  durationMs: number | null;
}

/** A mission's attempt history: its escalation and recovery counters and every stage attempt. */
export interface MissionAttemptHistory {
  missionId: string;
  voyageId: string | null;
  persona: string | null;
  mode: string | null;
  status: string | null;
  captainId: string | null;
  tier: string | null;
  escalationCount: number;
  escalatedFromCaptainId: string | null;
  escalatedFromModel: string | null;
  reworkEscalated: boolean;
  redispatchAttempts: number;
  outputRepairAttempts: number;
  noOpRedispatchAttempts: number;
  stallRecoveryAttempts: number;
  attempts: MissionAttempt[];
}

/**
 * A mission's attempt history, or undefined when the Admiral does not serve the route (older than 1.5.0) or the
 * mission is out of scope. The shared client has no call for this route yet.
 * @throws Error for an empty id or when no Admiral is configured.
 */
export async function getMissionAttempts(id: string): Promise<MissionAttemptHistory | undefined> {
  const trimmed = id.trim();
  if (!trimmed) throw new Error('A mission id is required.');
  if (!apiBase) throw new Error('No Admiral is connected.');
  const res = await fetch(`${apiBase}/api/v1/missions/${encodeURIComponent(trimmed)}/attempts`, { method: 'GET', headers: { Accept: 'application/json', ...apiHeaders } });
  if (res.status === 404 || res.status === 405) return undefined;
  if (!res.ok) throw new Error(`Attempt history failed (HTTP ${res.status}).`);
  const body = (await res.json()) as Partial<MissionAttemptHistory> | null;
  if (!body || typeof body !== 'object') return undefined;
  return { ...(body as MissionAttemptHistory), attempts: Array.isArray(body.attempts) ? body.attempts : [] };
}

/** Reads the health route (no credential needed) and narrows its fields. */
export async function readHealth(): Promise<AdmiralHealth> {
  const raw = (await getHealth()) ?? {};
  const ports = raw.ports && typeof raw.ports === 'object' ? (raw.ports as Record<string, unknown>) : {};
  return {
    status: stringOrNull(raw.status),
    version: stringOrNull(raw.version),
    contractVersion: stringOrNull(raw.contractVersion),
    admiralPort: numberOrNull(ports.admiral),
    mcpPort: numberOrNull(ports.mcp),
  };
}

/**
 * The black box entry for a mission id (msn_), a voyage id (vyg_) or an entry id (anything else).
 * @throws Error for an empty id.
 */
export function getEntryForAnyId(id: string): Promise<BlackBoxEntry> {
  const trimmed = id.trim();
  if (!trimmed) return Promise.reject(new Error('An id is required.'));
  if (trimmed.startsWith('msn_')) return getMissionBlackBox(trimmed);
  if (trimmed.startsWith('vyg_')) return getVoyageBlackBox(trimmed);
  return getBlackBox(trimmed);
}

/** Every vessel visible to the credential (optionally one tenant's), read page by page. */
export async function listAllVessels(tenantId?: string): Promise<Vessel[]> {
  const filters = tenantId ? { tenantId } : undefined;
  const all = await fetchAllPages((pageNumber, pageSize) => listVessels({ pageNumber, pageSize, filters }), { maxPages: 10 });
  return all.objects ?? [];
}

/** Removes trailing slashes and a trailing `/dashboard`, and adds http:// when no scheme is given. */
export function normalizeBaseUrl(url: string): string {
  let value = url.trim().replace(/\/+$/, '');
  if (/\/dashboard$/i.test(value)) value = value.slice(0, -'/dashboard'.length);
  if (!/^https?:\/\//i.test(value)) value = `http://${value}`;
  return value;
}

/** The dashboard Ask page URL with the prompt pre-filled (the Ask page reads ?prompt=). */
export function dashboardAskUrl(dashboardBase: string, prompt: string): string {
  return `${dashboardBase.replace(/\/+$/, '')}${askPrefillPath(prompt)}`;
}

/** A dashboard page URL: the dashboard base followed by a route such as /missions?tab=blackbox. */
export function dashboardPageUrl(dashboardBase: string, route: string): string {
  const base = dashboardBase.replace(/\/+$/, '');
  if (!route || route === '/') return `${base}/`;
  return `${base}${route.startsWith('/') ? route : `/${route}`}`;
}

function stringOrNull(value: unknown): string | null {
  return typeof value === 'string' && value ? value : null;
}

function numberOrNull(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}
