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
export type AuthHeaderMode = 'Bearer' | 'X-Api-Key';

/** A credential the host sends with every request. */
export interface HostCredential {
  /** The API key or bearer token. Never logged. */
  secret: string;
  /** Header that carries it: Authorization: Bearer (credential tokens) or X-Api-Key (the settings API key). */
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
  return { Authorization: `Bearer ${credential.secret}` };
}

/**
 * Points the shared client at an Admiral (or at nothing, for a disconnected host). The shared client keeps one target
 * per process, so the extension talks to one Admiral at a time.
 */
export function useAdmiral(baseUrl: string | undefined, credential: HostCredential | undefined): void {
  configureClient({ baseUrl: baseUrl ? normalizeBaseUrl(baseUrl) : '', headers: credentialHeaders(credential), platform: null });
  setAuthToken(null);
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
