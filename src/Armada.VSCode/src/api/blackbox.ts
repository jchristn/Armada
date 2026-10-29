/*
 * Armada black box REST client for the VS Code extension.
 *
 * Every route path, query parameter, request body and WebSocket event name the extension uses lives in this file
 * (DTOs in types.ts), matching src/Armada.Server/Routes/BlackBoxRoutes.cs and the WebSocket surface. Errors carry the
 * server's stable code (Data.Code, for example BlackBoxNotFound). This module does not import `vscode`, so it is unit
 * tested with a mocked fetch.
 */

import {
  ApiErrorResponse,
  BlackBoxAudience,
  BlackBoxEntry,
  BlackBoxEntrySummary,
  BlackBoxEnumerateQuery,
  BlackBoxHotspot,
  BlackBoxNote,
  BlackBoxObjectSummary,
  BlackBoxRecoveryRequest,
  BlackBoxRecoveryResult,
  BlackBoxResolveRequest,
  BlackBoxSectionName,
  BlackBoxSectionResult,
  EnumerationResult,
  Vessel,
} from './types';

/** Every REST route the extension calls. */
export const Routes = {
  vessels: (pageNumber: number, pageSize: number): string =>
    `/api/v1/vessels?pageNumber=${pageNumber}&pageSize=${pageSize}`,
  missionEntry: (missionId: string): string => `/api/v1/missions/${enc(missionId)}/blackbox`,
  missionBrief: (missionId: string, audience: BlackBoxAudience): string =>
    `/api/v1/missions/${enc(missionId)}/blackbox/brief?audience=${enc(audience)}`,
  missionEntries: (missionId: string): string => `/api/v1/missions/${enc(missionId)}/blackbox/entries`,
  voyageEntry: (voyageId: string): string => `/api/v1/voyages/${enc(voyageId)}/blackbox`,
  voyageBrief: (voyageId: string, audience: BlackBoxAudience): string =>
    `/api/v1/voyages/${enc(voyageId)}/blackbox/brief?audience=${enc(audience)}`,
  entry: (entryId: string): string => `/api/v1/blackbox/${enc(entryId)}`,
  entryBrief: (entryId: string, audience: BlackBoxAudience): string =>
    `/api/v1/blackbox/${enc(entryId)}/brief?audience=${enc(audience)}`,
  section: (entryId: string, section: BlackBoxSectionName): string =>
    `/api/v1/blackbox/${enc(entryId)}/sections/${enc(section)}`,
  objects: (entryId: string): string => `/api/v1/blackbox/${enc(entryId)}/objects`,
  object: (entryId: string, name: string, offset?: number, length?: number): string => {
    const query: string[] = [];
    if (offset !== undefined) query.push(`offset=${offset}`);
    if (length !== undefined) query.push(`length=${length}`);
    // The name is one path segment: a folder separator is sent encoded (evidence%2Fgit-status.txt).
    return `/api/v1/blackbox/${enc(entryId)}/objects/${enc(name)}${query.length ? `?${query.join('&')}` : ''}`;
  },
  enumerate: (): string => '/api/v1/blackbox/enumerate',
  notes: (entryId: string): string => `/api/v1/blackbox/${enc(entryId)}/notes`,
  resolve: (entryId: string): string => `/api/v1/blackbox/${enc(entryId)}/resolve`,
  recovery: (entryId: string, pathId: string): string =>
    `/api/v1/blackbox/${enc(entryId)}/recovery/${enc(pathId)}`,
  hotspots: (): string => '/api/v1/blackbox/hotspots/enumerate',
  webSocket: (): string => '/ws',
  dashboardAsk: (): string => '/ask',
} as const;

/** WebSocket events the extension listens for (declared in the server's WebSocketSurface). */
export const BlackBoxEvents = {
  captured: 'blackbox.captured',
  analyzed: 'blackbox.analyzed',
  resolved: 'blackbox.resolved',
  deleted: 'blackbox.deleted',
  recoveryRun: 'blackbox.recovery_run',
} as const;

/** All black box event names, for membership checks. */
export const BLACKBOX_EVENT_TYPES: readonly string[] = Object.values(BlackBoxEvents);

/**
 * The dashboard Ask page query parameter that pre-fills the composer of a new conversation (AskArmada.tsx reads it
 * and removes it from the URL; dashboard lib/askNavigation.ts ASK_PREFILL_QUERY_PARAM). Null would make the
 * extension copy the prompt instead.
 */
export const ASK_PREFILL_QUERY_PARAM: string | null = 'prompt';

/** Canonical Ask Armada prompt (plan 10.3). */
export function askArmadaPrompt(missionId: string): string {
  return `Show me the pertinent details from the black box for mission ${missionId} and give me your opinion on why it failed and what could have been done differently`;
}

/** Classifies a failed request so the UI can explain it. */
export type ArmadaErrorKind =
  | 'notConfigured'
  | 'network'
  | 'timeout'
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'conflict'
  | 'gone'
  | 'badRequest'
  | 'unavailable'
  | 'notImplemented'
  | 'server'
  | 'invalidResponse';

/** Error thrown by every client method. Never thrown for a successful (2xx) response. */
export class ArmadaApiError extends Error {
  public readonly kind: ArmadaErrorKind;
  public readonly status: number | null;
  public readonly code: string | null;
  public readonly serverMessage: string | null;

  public constructor(kind: ArmadaErrorKind, message: string, status: number | null = null, code: string | null = null, serverMessage: string | null = null) {
    super(message);
    this.name = 'ArmadaApiError';
    this.kind = kind;
    this.status = status;
    this.code = code;
    this.serverMessage = serverMessage;
  }
}

/** Maps an HTTP status to an error kind. */
export function kindForStatus(status: number): ArmadaErrorKind {
  if (status === 401) return 'unauthorized';
  if (status === 403) return 'forbidden';
  if (status === 404) return 'notFound';
  if (status === 409) return 'conflict';
  if (status === 410) return 'gone';
  if (status === 400 || status === 422) return 'badRequest';
  if (status === 501) return 'notImplemented';
  if (status === 503) return 'unavailable';
  return 'server';
}

/** A user-facing explanation for any error thrown by this client (or anything else). Never throws. */
export function describeError(err: unknown): string {
  if (err instanceof ArmadaApiError) {
    const detail = err.serverMessage ? ` (${err.serverMessage})` : '';
    switch (err.kind) {
      case 'notConfigured':
        return 'Armada is not connected. Run "Armada: Connect" to set the server URL and API key.';
      case 'network':
        return `Cannot reach the Armada server: ${err.message}. Check armada.url and that the Admiral is running.`;
      case 'timeout':
        return 'The Armada server did not respond in time. Try again, or check the Admiral.';
      case 'unauthorized':
        return 'Armada rejected the API key (401). Run "Armada: Connect" and enter a valid key.';
      case 'forbidden':
        return `Your Armada credential is not allowed to do this (403)${detail}.`;
      case 'notFound':
        return err.code === 'BlackBoxNotFound'
          ? `No black box entry was found (404)${detail}. The mission may not have failed, or the entry expired.`
          : `Not found (404)${detail}. The id may be wrong, or this Armada server does not have black box routes yet.`;
      case 'conflict':
        return `The request conflicts with the current state (409)${detail}.`;
      case 'gone':
        return `A black box object is missing from storage (410 ${err.code ?? 'BlackBoxObjectMissing'})${detail}.`;
      case 'badRequest':
        return `Armada rejected the request (${err.status ?? 400})${detail}.`;
      case 'unavailable':
        return `The black box store is unavailable (503 ${err.code ?? 'BlackBoxStoreUnavailable'})${detail}. Try again later.`;
      case 'notImplemented':
        return `This Armada server does not run that black box operation yet (501 ${err.code ?? 'NotImplemented'}): the recovery runner may not be merged on it${detail}.`;
      case 'invalidResponse':
        return `Armada returned an unexpected response: ${err.message}`;
      default:
        return `Armada server error (${err.status ?? 'unknown'})${detail}.`;
    }
  }
  if (err instanceof Error) return err.message;
  return String(err);
}

/** Minimal fetch signature so tests can inject a mock. */
export type FetchLike = (url: string, init: { method: string; headers: Record<string, string>; body?: string; signal?: AbortSignal }) => Promise<{
  status: number;
  ok: boolean;
  headers: { get(name: string): string | null };
  text(): Promise<string>;
}>;

/** How the API key is presented. */
export type AuthHeaderMode = 'Bearer' | 'X-Api-Key';

export interface ArmadaClientOptions {
  baseUrl: string;
  apiKey: string;
  tenant?: string;
  authHeader?: AuthHeaderMode;
  timeoutMs?: number;
  fetchImpl?: FetchLike;
}

/** Typed client for the black box routes (plus the vessel list used for repository matching). */
export class ArmadaBlackBoxClient {
  public readonly baseUrl: string;
  private readonly _ApiKey: string;
  private readonly _Tenant: string | undefined;
  private readonly _AuthHeader: AuthHeaderMode;
  private readonly _TimeoutMs: number;
  private readonly _Fetch: FetchLike;

  /**
   * @throws ArmadaApiError (notConfigured) when baseUrl or apiKey is empty.
   */
  public constructor(options: ArmadaClientOptions) {
    if (!options.baseUrl || !options.baseUrl.trim()) throw new ArmadaApiError('notConfigured', 'The Armada URL is not set.');
    if (!options.apiKey) throw new ArmadaApiError('notConfigured', 'The Armada API key is not set.');
    this.baseUrl = normalizeBaseUrl(options.baseUrl);
    this._ApiKey = options.apiKey;
    this._Tenant = options.tenant && options.tenant.trim() ? options.tenant.trim() : undefined;
    this._AuthHeader = options.authHeader ?? 'Bearer';
    this._TimeoutMs = options.timeoutMs ?? 30000;
    const globalFetch = (globalThis as { fetch?: FetchLike }).fetch;
    const chosen = options.fetchImpl ?? globalFetch;
    if (!chosen) throw new ArmadaApiError('notConfigured', 'No fetch implementation is available in this host.');
    this._Fetch = chosen;
  }

  /** The tenant filter, if configured. */
  public get tenant(): string | undefined {
    return this._Tenant;
  }

  /** The WebSocket URL with the credential in the `token` query parameter (docs/WEBSOCKET_API.md). */
  public webSocketUrl(): string {
    const url = new URL(this.baseUrl + Routes.webSocket());
    url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:';
    url.searchParams.set('token', this._ApiKey);
    return url.toString();
  }

  /** One page of vessels. */
  public async listVessels(pageNumber = 1, pageSize = 1000): Promise<EnumerationResult<Vessel>> {
    let path = Routes.vessels(pageNumber, pageSize);
    if (this._Tenant) path += `&tenantId=${enc(this._Tenant)}`;
    return this.requestEnumeration<Vessel>('GET', path);
  }

  /** Every vessel, reading at most maxPages pages. */
  public async listAllVessels(maxPages = 10): Promise<Vessel[]> {
    const all: Vessel[] = [];
    for (let page = 1; page <= maxPages; page++) {
      const result = await this.listVessels(page, 1000);
      all.push(...result.Objects);
      if (page >= result.TotalPages || result.Objects.length === 0) break;
    }
    return all;
  }

  /** Current entry for a mission (404 when none). */
  public getMissionEntry(missionId: string): Promise<BlackBoxEntry> {
    return this.requestJson<BlackBoxEntry>('GET', Routes.missionEntry(missionId));
  }

  /** Current entry for a voyage. */
  public getVoyageEntry(voyageId: string): Promise<BlackBoxEntry> {
    return this.requestJson<BlackBoxEntry>('GET', Routes.voyageEntry(voyageId));
  }

  /** An entry by id, with its record. */
  public getEntry(entryId: string): Promise<BlackBoxEntry> {
    return this.requestJson<BlackBoxEntry>('GET', Routes.entry(entryId));
  }

  /**
   * Resolves a mission id (msn_), voyage id (vyg_) or entry id (anything else) to an entry.
   * @throws ArmadaApiError (badRequest) for an empty id.
   */
  public getEntryForAnyId(id: string): Promise<BlackBoxEntry> {
    const trimmed = id.trim();
    if (!trimmed) return Promise.reject(new ArmadaApiError('badRequest', 'An id is required.'));
    if (trimmed.startsWith('msn_')) return this.getMissionEntry(trimmed);
    if (trimmed.startsWith('vyg_')) return this.getVoyageEntry(trimmed);
    return this.getEntry(trimmed);
  }

  /** All attempts' entries for a mission. */
  public listMissionEntries(missionId: string): Promise<EnumerationResult<BlackBoxEntrySummary>> {
    return this.requestEnumeration<BlackBoxEntrySummary>('GET', Routes.missionEntries(missionId));
  }

  /** The FIX THIS brief (Markdown) for an entry. */
  public getBrief(entryId: string, audience: BlackBoxAudience = 'agent'): Promise<string> {
    return this.requestText('GET', Routes.entryBrief(entryId, audience));
  }

  /** The FIX THIS brief for a mission's current entry. */
  public getMissionBrief(missionId: string, audience: BlackBoxAudience = 'agent'): Promise<string> {
    return this.requestText('GET', Routes.missionBrief(missionId, audience));
  }

  /** The FIX THIS brief for a voyage's entry. */
  public getVoyageBrief(voyageId: string, audience: BlackBoxAudience = 'agent'): Promise<string> {
    return this.requestText('GET', Routes.voyageBrief(voyageId, audience));
  }

  /** One record section: a record carrying only that section (the others are omitted). */
  public getSection(entryId: string, section: BlackBoxSectionName): Promise<BlackBoxSectionResult> {
    return this.requestJson<BlackBoxSectionResult>('GET', Routes.section(entryId, section));
  }

  /** The objects in an entry. */
  public listObjects(entryId: string): Promise<EnumerationResult<BlackBoxObjectSummary>> {
    return this.requestEnumeration<BlackBoxObjectSummary>('GET', Routes.objects(entryId));
  }

  /** Text of one object, optionally a byte range (the server caps a read at 1 MB). */
  public readObjectText(entryId: string, name: string, offset?: number, length?: number): Promise<string> {
    return this.requestText('GET', Routes.object(entryId, name, offset, length));
  }

  /** One page of entries (newest first by default). */
  public enumerateEntries(query: BlackBoxEnumerateQuery = {}): Promise<EnumerationResult<BlackBoxEntrySummary>> {
    const body: BlackBoxEnumerateQuery = { PageNumber: 1, PageSize: 50, Order: 'CreatedDescending', ...query };
    if (this._Tenant && !body.TenantId) body.TenantId = this._Tenant;
    return this.requestEnumeration<BlackBoxEntrySummary>('POST', Routes.enumerate(), body);
  }

  /** Notes on an entry. */
  public listNotes(entryId: string): Promise<EnumerationResult<BlackBoxNote>> {
    return this.requestEnumeration<BlackBoxNote>('GET', Routes.notes(entryId));
  }

  /**
   * Adds a note.
   * @throws ArmadaApiError (badRequest) when the body is empty or over 8000 characters.
   */
  public addNote(entryId: string, body: string): Promise<BlackBoxNote> {
    const text = body.trim();
    if (!text) return Promise.reject(new ArmadaApiError('badRequest', 'A note cannot be empty.'));
    if (text.length > 8000) return Promise.reject(new ArmadaApiError('badRequest', 'A note is limited to 8000 characters.'));
    return this.requestJson<BlackBoxNote>('POST', Routes.notes(entryId), { Body: text });
  }

  /** Resolves an entry. */
  public resolve(entryId: string, request: BlackBoxResolveRequest): Promise<BlackBoxEntry> {
    const body: BlackBoxResolveRequest = { Resolution: request.Resolution };
    if (request.Note && request.Note.trim()) body.Note = request.Note.trim();
    if (request.Link && request.Link.trim()) body.Link = request.Link.trim();
    return this.requestJson<BlackBoxEntry>('POST', Routes.resolve(entryId), body);
  }

  /** Dry run of a recovery path; returns the plan and a confirm token. */
  public dryRunRecovery(entryId: string, pathId: string): Promise<BlackBoxRecoveryResult> {
    const body: BlackBoxRecoveryRequest = { DryRun: true, AuthorKind: 'Operator' };
    return this.requestJson<BlackBoxRecoveryResult>('POST', Routes.recovery(entryId, pathId), body);
  }

  /**
   * Real run of a recovery path.
   * @throws ArmadaApiError (badRequest) when confirmToken is empty.
   */
  public runRecovery(entryId: string, pathId: string, confirmToken: string, acknowledgeDataLoss: boolean): Promise<BlackBoxRecoveryResult> {
    if (!confirmToken) return Promise.reject(new ArmadaApiError('badRequest', 'A confirm token from a dry run is required.'));
    const body: BlackBoxRecoveryRequest = { DryRun: false, ConfirmToken: confirmToken, AuthorKind: 'Operator' };
    if (acknowledgeDataLoss) body.AcknowledgeDataLoss = true;
    return this.requestJson<BlackBoxRecoveryResult>('POST', Routes.recovery(entryId, pathId), body);
  }

  /** Conflict hotspot directories for a vessel. */
  public listHotspots(vesselId: string, pageSize = 10): Promise<EnumerationResult<BlackBoxHotspot>> {
    const body: Record<string, unknown> = { VesselId: vesselId, PageNumber: 1, PageSize: pageSize };
    if (this._Tenant) body.TenantId = this._Tenant;
    return this.requestEnumeration<BlackBoxHotspot>('POST', Routes.hotspots(), body);
  }

  private async requestEnumeration<T>(method: string, path: string, body?: unknown): Promise<EnumerationResult<T>> {
    const result = await this.requestJson<EnumerationResult<T>>(method, path, body);
    if (!result || !Array.isArray(result.Objects)) {
      throw new ArmadaApiError('invalidResponse', `expected an EnumerationResult from ${path}`);
    }
    return result;
  }

  private async requestJson<T>(method: string, path: string, body?: unknown): Promise<T> {
    const text = await this.send(method, path, body, 'application/json');
    if (!text) throw new ArmadaApiError('invalidResponse', `empty body from ${path}`);
    try {
      return JSON.parse(text) as T;
    } catch {
      throw new ArmadaApiError('invalidResponse', `invalid JSON from ${path}`);
    }
  }

  private requestText(method: string, path: string): Promise<string> {
    return this.send(method, path, undefined, 'text/markdown, text/plain, */*');
  }

  private async send(method: string, path: string, body: unknown, accept: string): Promise<string> {
    const headers: Record<string, string> = { Accept: accept };
    if (this._AuthHeader === 'X-Api-Key') headers['X-Api-Key'] = this._ApiKey;
    else headers.Authorization = `Bearer ${this._ApiKey}`;
    if (body !== undefined) headers['Content-Type'] = 'application/json';

    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this._TimeoutMs);
    let response: Awaited<ReturnType<FetchLike>>;
    try {
      response = await this._Fetch(this.baseUrl + path, {
        method,
        headers,
        body: body !== undefined ? JSON.stringify(body) : undefined,
        signal: controller.signal,
      });
    } catch (err) {
      if (controller.signal.aborted) throw new ArmadaApiError('timeout', `request to ${path} timed out`);
      throw new ArmadaApiError('network', networkMessage(err));
    } finally {
      clearTimeout(timer);
    }

    let text = '';
    try {
      text = await response.text();
    } catch {
      text = '';
    }
    if (response.ok) return text;

    let code: string | null = null;
    let serverMessage: string | null = null;
    try {
      const parsed = JSON.parse(text) as ApiErrorResponse;
      code = parsed.Data?.Code ?? parsed.Error ?? null;
      serverMessage = parsed.Message ?? parsed.Description ?? null;
    } catch {
      serverMessage = text ? text.slice(0, 300) : null;
    }
    throw new ArmadaApiError(kindForStatus(response.status), `${method} ${path} returned ${response.status}`, response.status, code, serverMessage);
  }
}

/** Removes trailing slashes and a trailing `/dashboard` so either the Admiral or the dashboard URL works. */
export function normalizeBaseUrl(url: string): string {
  let value = url.trim().replace(/\/+$/, '');
  if (/\/dashboard$/i.test(value)) value = value.slice(0, -'/dashboard'.length);
  if (!/^https?:\/\//i.test(value)) value = `http://${value}`;
  return value;
}

/** The dashboard Ask page URL, pre-filled when the dashboard supports it. */
export function dashboardAskUrl(dashboardBase: string, prompt: string): { url: string; prefilled: boolean } {
  const base = dashboardBase.replace(/\/+$/, '');
  const url = `${base}${Routes.dashboardAsk()}`;
  if (ASK_PREFILL_QUERY_PARAM) {
    return { url: `${url}?${ASK_PREFILL_QUERY_PARAM}=${encodeURIComponent(prompt)}`, prefilled: true };
  }
  return { url, prefilled: false };
}

function enc(value: string): string {
  return encodeURIComponent(value);
}

function networkMessage(err: unknown): string {
  if (err instanceof Error) {
    const cause = (err as { cause?: unknown }).cause;
    if (cause instanceof Error && cause.message) return `${err.message} (${cause.message})`;
    return err.message;
  }
  return String(err);
}
