import type {
  ScopeEnum,
  AuthenticateRequest,
  AuthenticateResult,
  WhoAmIResult,
  TenantLookupResult,
  TenantMetadata,
  UserMaster,
  UserUpsertRequest,
  TenantCreateRequest,
  TenantCreateResult,
  Credential,
  EnumerationResult,
  Job,
  Fleet,
  FleetDetailResponse,
  Vessel,
  Captain,
  CaptainToolAccessResult,
  CaptainToolsQuery,
  Mission,
  MissionSummary,
  MissionHistorySummaryResult,
  TokenUsageSummaryResult,
  TokenUsageSyntheticRequest,
  TokenUsageSyntheticResult,
  Voyage,
  Objective,
  GitHubActionsSyncRequest,
  GitHubActionsSyncResult,
  GitHubObjectiveImportRequest,
  GitHubPullRequestDetail,
  GitHubPullRequestReview,
  GitHubPullRequestComment,
  GitHubPullRequestCheck,
  VoyageProgress,
  VoyageCancelResult,
  ObjectiveQuery,
  ObjectiveReorderRequest,
  ObjectiveReorderResult,
  ObjectiveUpsertRequest,
  ArmadaEvent,
  MergeEntry,
  Signal,
  Dock,
  DiffResult,
  LogResult,
  InstructionsResult,
  DispatchRequest,
  VoyageCreateRequest,
  PlanningSession,
  PlanningSessionCreateRequest,
  PlanningSessionDetail,
  PlanningSessionDispatchRequest,
  PlanningSessionMessageRequest,
  PlanningSessionSummaryRequest,
  PlanningSessionSummaryResponse,
  ObjectiveRefinementApplyRequest,
  ObjectiveRefinementApplyResponse,
  ObjectiveRefinementMessageRequest,
  ObjectiveRefinementSession,
  ObjectiveRefinementSessionCreateRequest,
  ObjectiveRefinementSessionDetail,
  ObjectiveRefinementSummaryRequest,
  ObjectiveRefinementSummaryResponse,
  TransitionRequest,
  SendSignalRequest,
  SettingsData,
  BatchDeleteResult,
  DoctorCheck,
  StatusSnapshot,
  PromptTemplate,
  Persona,
  Pipeline,
  Playbook,
  WorkflowProfile,
  WorkflowProfileResolutionPreviewResult,
  WorkflowProfileValidationResult,
  ProjectProfile,
  ProjectProfileValidationResult,
  ProjectProfileResolutionResult,
  PersonaPromptPreview,
  Skill,
  Harbor,
  HarborMetrics,
  HarborMetricsRange,
  HarborJobRecord,
  HarborJobStopResult,
  ModelEndpoint,
  Memory,
  MemoryEnumerateQuery,
  ModelEndpointProbeResult,
  ModelEndpointHealthSweepResponse,
  PasswordChangeRequest,
  CaptainChatRequest,
  CaptainChatResponse,
  InboxItem,
  CheckRun,
  CheckRunImportRequest,
  CheckRunRequest,
  Deployment,
  DeploymentEnvironment,
  DeploymentEnvironmentQuery,
  DeploymentEnvironmentUpsertRequest,
  DeploymentQuery,
  DeploymentUpsertRequest,
  Incident,
  IncidentQuery,
  IncidentUpsertRequest,
  Release,
  ReleaseQuery,
  ReleaseUpsertRequest,
  HistoricalTimelineEntry,
  HistoricalTimelineQuery,
  VesselReadinessResult,
  VesselCommitActivity,
  VesselCommitActivityQuery,
  VesselCommitPage,
  VesselCommitQuery,
  LandingPreviewResult,
  MuxEndpointPage,
  MuxEndpointShowResult,
  WorkspaceChangesResult,
  WorkspaceCreateDirectoryRequest,
  WorkspaceFileResponse,
  WorkspaceOperationResult,
  WorkspaceRenameRequest,
  WorkspaceSaveRequest,
  WorkspaceSaveResult,
  WorkspaceSearchResult,
  WorkspaceStatusResult,
  WorkspaceExecResult,
  WorkspaceOpenTerminalResult,
  WorkspaceDiffResult,
  WorkspaceTreeResult,
  RequestHistoryEntry,
  RequestHistoryQuery,
  RequestHistoryRecord,
  RequestHistorySummaryResult,
  Runbook,
  RunbookExecution,
  RunbookExecutionQuery,
  RunbookExecutionStartRequest,
  RunbookExecutionUpdateRequest,
  RunbookQuery,
  RunbookUpsertRequest,
  VesselBrowseResult,
  VesselDiscoveryRequest,
  VesselImportDiscoverResponse,
  VesselImportRequest,
  VesselImportResponse,
  VesselImportBatch,
  VesselImportBatchDetail,
  VesselImportCategorizationRequest,
  FleetRecommendationApplyRequest,
  FleetRecommendationApplyResult,
  FleetCategorizationDefaultPrompt,
  FleetAction,
  FleetActionUpsertRequest,
  FleetActionRunRequest,
  FleetActionRunStartResult,
  FleetActionRun,
  FleetActionRunDetail,
  FleetActionRunTarget,
  FleetActionRunTargetSummary,
  FleetActionRunStatus,
  FleetActionTargetStatus,
  VesselHealth,
  VesselHealthCriterion,
  VesselHealthDetail,
  VesselDependency,
  WorkspaceActiveMission,
  VesselHealthEnumerateRequest,
  VesselHealthEvaluateRequest,
  VesselHealthEvaluationStart,
  VesselHealthStatus,
  VesselHealthSummary,
  AskThread,
  AskThreadDeleteMultipleResult,
  AskThreadDetail,
  AskThreadEnumerateQuery,
  AskThreadUpdateRequest,
  AskMessagePage,
  AskSendMessageResult,
  AskActionProposal,
  AskWorkSnapshot,
  AskQuickAction,
  AskTrackedWork,
  AskMissionSnapshot,
  AskTargetSnapshot,
  AskToolCall,
  PagedQuery,
  PlanningSessionMessage,
  ObjectiveRefinementMessage,
  CliPermissionDecisionRequest,
  CliPermissionPolicy,
  CliPermissionRequest,
  CliPermissionRequestQuery,
  CliPermissionRule,
  CliPermissionRuleCreateRequest,
  CliPermissionRuleScope,
  FailureCause,
  FailureCauseRule,
  FailureCauseRuleMatch,
  FailureCauseRuleUpsertRequest,
  StageOutcome,
  StageOutcomeQuery,
  StageOutcomeReclassifyResult,
  ModelProbeDepth,
  ModelProbeRun,
  ModelProbeRunQuery,
} from '../types/models';
import type {
  BlackBoxBackfillRequest,
  BlackBoxBackfillResult,
  BlackBoxBundleQuery,
  BlackBoxBundleResult,
  BlackBoxDeleteResult,
  BlackBoxEntry,
  BlackBoxEntrySummary,
  BlackBoxEnumerateQuery,
  BlackBoxFingerprintQuery,
  BlackBoxFingerprintSummary,
  BlackBoxGcReport,
  BlackBoxHotspot,
  BlackBoxHotspotQuery,
  BlackBoxNote,
  BlackBoxObjectRead,
  BlackBoxObjectSummary,
  BlackBoxPurgeQuery,
  BlackBoxPurgeResult,
  BlackBoxRecommendationApplyRequest,
  BlackBoxRecommendationApplyResult,
  BlackBoxRecoveryRunRequest,
  BlackBoxRecoveryRunResult,
  BlackBoxResolveRequest,
  BlackBoxRetentionStatus,
  BlackBoxSearchQuery,
  BlackBoxSection,
  BlackBoxSectionResult,
  BlackBoxStorageMigrationJob,
  BlackBoxStorageMigrationRequest,
  BlackBoxStorageStatus,
  BlackBoxFingerprintTrend,
  BlackBoxFixture,
  BlackBoxAttemptDiff,
  BlackBoxCascadeGraph,
  BlackBoxDoctorPage,
  BlackBoxDoctorQuery,
  BlackBoxNearbySuccess,
  BlackBoxRecoveryStat,
  BlackBoxRecoveryStatsQuery,
  BlackBoxTimelinePage,
  BlackBoxTimelineQuery,
  BriefLintRequest,
  BriefLintResult,
  MissionOverlapPreview,
  MissionOverlapPreviewRequest,
  VesselReservationPage,
} from '../types/blackbox';
import { MAX_PAGE_SIZE, MAX_PAGES } from './paging';

/**
 * Host services the client cannot implement portably. The dashboard registers a browser adapter (anchor
 * download); the mobile app registers one that writes the file and opens the share sheet.
 */
export interface ClientPlatformAdapter {
  /** Hand a downloaded file to the user (browser download, share sheet, ...). */
  saveFile: (file: DownloadedFile) => Promise<void>;
}

/** A binary response body plus the file name the server suggested. */
export interface DownloadedFile {
  filename: string;
  contentType: string;
  body: Blob;
}

/** A file to upload: a browser `File` satisfies it, and native hosts supply the same two members. */
export interface UploadFile {
  name: string;
  arrayBuffer: () => Promise<ArrayBuffer>;
}

/** Host configuration for the shared API client. Fields left out keep their current value. */
export interface ClientConfig {
  /** Server origin prefixed to every path ('' means same origin, as when the Admiral serves the dashboard). */
  baseUrl?: string;
  /** Host services for file download and similar browser-only operations. */
  platform?: ClientPlatformAdapter | null;
  /**
   * Extra headers sent with every request to the server (null clears them). The mobile app sets
   * `X-Armada-Proxy-Session` here when it reaches an Admiral through Armada.Proxy; the browser dashboard sets none.
   * They never replace the client's own Content-Type or X-Token.
   */
  headers?: Record<string, string> | null;
}

// The client is host-agnostic: no import.meta, window, or document here. The host calls configureClient once at
// startup (the dashboard from main.tsx with VITE_ARMADA_SERVER_URL; the mobile app per active server profile).
let BASE_URL = '';
let platformAdapter: ClientPlatformAdapter | null = null;
let extraHeaders: Record<string, string> = {};

/** Configure the API client for this host. Trailing slashes on the base URL are removed. */
export function configureClient(config: ClientConfig): void {
  if (config.baseUrl !== undefined) BASE_URL = (config.baseUrl || '').replace(/\/+$/, '');
  if (config.platform !== undefined) platformAdapter = config.platform;
  if (config.headers !== undefined) extraHeaders = { ...(config.headers ?? {}) };
}

/** Request headers: the host's extra headers first, then the client's own (which win on a name clash). */
function withExtraHeaders(own: Record<string, string>): Record<string, string> {
  return { ...extraHeaders, ...own };
}

/** The base URL the client currently sends requests to ('' for same origin). */
export function getClientBaseUrl(): string {
  return BASE_URL;
}

/**
 * Error thrown for non-2xx API responses. It is still an `Error` whose message is the server's message, so
 * existing callers keep working; callers that need more read `status` and the camelized `data` payload
 * (for example vessel import errors carry `{ code, path }`).
 */
export class ApiError extends Error {
  status: number;
  data: unknown;

  constructor(message: string, status: number, data: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.data = data;
  }
}

/**
 * Error thrown when a request exceeds its client-side timeout. Callers detect it with `instanceof TimeoutError`,
 * never by comparing the message text.
 */
export class TimeoutError extends Error {
  constructor(message = 'Request timed out') {
    super(message);
    this.name = 'TimeoutError';
  }
}

/**
 * Error thrown when no HTTP response arrived: DNS failure, connection refused, TLS failure, or a request the operating
 * system blocked. Hosts reject fetch with different classes for this (TypeError in browsers, FetchError under Expo),
 * so the client normalizes them; callers detect it with `instanceof NetworkError`. The original error is `cause`.
 */
export class NetworkError extends Error {
  cause: unknown;

  constructor(message: string, cause: unknown) {
    super(message);
    this.name = 'NetworkError';
    this.cause = cause;
  }
}

/** fetch, with a rejection that is not an abort turned into a NetworkError. */
async function fetchOrNetworkError(url: string, init: RequestInit): Promise<Response> {
  try {
    return await fetch(url, init);
  } catch (err) {
    if (init.signal?.aborted || isAbortError(err)) throw err;
    throw new NetworkError(err instanceof Error ? err.message : String(err), err);
  }
}

/**
 * Error thrown when the caller's own abort signal cancelled the request (for example the chat Stop button).
 * Its `name` stays 'AbortError' for compatibility with code that checks the conventional abort name.
 */
export class RequestCancelledError extends Error {
  constructor(message = 'Aborted') {
    super(message);
    this.name = 'AbortError';
  }
}

/** Build the error thrown for an HTTP 401: an ApiError with status 401 (message 'Unauthorized'). */
function unauthorizedError(): ApiError {
  return new ApiError('Unauthorized', 401, null);
}

/** True when the error is an ApiError carrying the given HTTP status. */
export function isApiStatus(err: unknown, status: number): boolean {
  return err instanceof ApiError && err.status === status;
}

/**
 * The message to show for a failed request: the server's own message when the error is an ApiError that carries
 * one (for example the 409 DuplicateEntity text "A fleet named X already exists"), otherwise the caller's
 * fallback. Network, timeout, and other non-API errors get the fallback.
 */
export function apiErrorMessage(err: unknown, fallback: string): string {
  return err instanceof ApiError && err.message ? err.message : fallback;
}

/** Read the machine-readable `code` from an API error's data payload, or null. */
export function apiErrorCode(err: unknown): string | null {
  if (err instanceof ApiError && err.data && typeof err.data === 'object') {
    const code = (err.data as { code?: unknown }).code;
    return typeof code === 'string' ? code : null;
  }
  return null;
}

let authToken: string | null = null;
let onUnauthorized: (() => void) | null = null;

export function setAuthToken(token: string | null) {
  authToken = token;
}

export function setOnUnauthorized(cb: () => void) {
  onUnauthorized = cb;
}

/** Convert a PascalCase key to camelCase. */
function keyToCamel(key: string): string {
  return key.charAt(0).toLowerCase() + key.slice(1);
}

/** Recursively convert all object keys from PascalCase to camelCase. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function camelizeKeys(obj: any, dictionaryKeys: string[] = []): any {
  if (Array.isArray(obj)) return obj.map(value => camelizeKeys(value, dictionaryKeys));
  if (obj !== null && typeof obj === 'object' && !(obj instanceof Date)) {
    return Object.fromEntries(
      Object.entries(obj).map(([k, v]) => [keyToCamel(k), dictionaryKeys.includes(keyToCamel(k)) && v !== null && typeof v === 'object' && !Array.isArray(v) ? v : camelizeKeys(v, dictionaryKeys)])
    );
  }
  return obj;
}

interface RequestOptions {
  /** Numeric dictionary keys are domain values, not DTO property names. */
  dictionaryKeys?: string[];
  timeout?: number;
  rawText?: boolean;
  /** External abort signal; when it fires the in-flight request is cancelled (used by chat Stop). */
  signal?: AbortSignal;
  /** Non-2xx statuses whose JSON body is returned like a success instead of throwing (e.g. 409 with a payload). */
  acceptStatuses?: number[];
}

export interface ProxySessionContext {
  isAuthenticated?: boolean;
  expiresUtc?: string;
  selectedInstanceId?: string | null;
  selectedInstance?: {
    instanceId?: string;
    state?: string;
    armadaVersion?: string | null;
    lastSeenUtc?: string | null;
  } | null;
  relay?: {
    dashboard?: boolean;
    api?: boolean;
    websocket?: boolean;
  } | null;
}

const PLANNING_CREATE_TIMEOUT_MS = 5 * 60 * 1000;
const PLANNING_SUMMARIZE_TIMEOUT_MS = 3 * 60 * 1000;

async function request<T>(method: string, path: string, body?: unknown, opts?: RequestOptions): Promise<T> {
  const own: Record<string, string> = { 'Content-Type': 'application/json' };
  if (authToken) own['X-Token'] = authToken;
  const headers = withExtraHeaders(own);

  const controller = new AbortController();
  const timeoutMs = opts?.timeout ?? 30000;
  const timeoutId = setTimeout(() => controller.abort(), timeoutMs);

  // An external signal (e.g. the chat Stop button) cancels the in-flight request; the server observes
  // the dropped request and cancels the captain runtime, so the stop is real rather than cosmetic.
  const external = opts?.signal;
  const onExternalAbort = () => controller.abort();
  if (external) {
    if (external.aborted) controller.abort();
    else external.addEventListener('abort', onExternalAbort);
  }

  try {
    const res = await fetchOrNetworkError(`${BASE_URL}${path}`, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });

    if (res.status === 401) {
      onUnauthorized?.();
      throw unauthorizedError();
    }

    if (!res.ok && !opts?.acceptStatuses?.includes(res.status)) {
      const text = await res.text();
      let msg = `${res.status}: ${text}`;
      let data: unknown = null;
      try {
        const e = JSON.parse(text);
        msg = e.Message || e.message || e.Error || e.error || msg;
        data = camelizeKeys(e.Data ?? e.data ?? null, opts?.dictionaryKeys);
      } catch {
        // use raw text
      }
      throw new ApiError(msg, res.status, data);
    }

    if (res.status === 204) return undefined as T;

    if (opts?.rawText) {
      const text = await res.text();
      return text as unknown as T;
    }

    const json = await res.json();
    return camelizeKeys(json, opts?.dictionaryKeys) as T;
  } catch (err) {
    // An external abort is a deliberate cancel (Stop), not a timeout; surface it distinctly so callers
    // can treat it as a clean stop instead of an error.
    if (external?.aborted) {
      throw new RequestCancelledError();
    }
    // Our own timeout aborted the request. Checked on the signal (and by name, not `instanceof DOMException`): hosts
    // reject an aborted fetch with different classes, and not every host defines DOMException (React Native does not).
    if (controller.signal.aborted || isAbortError(err)) {
      throw new TimeoutError();
    }
    throw err;
  } finally {
    clearTimeout(timeoutId);
    if (external) external.removeEventListener('abort', onExternalAbort);
  }
}

/** True for the error fetch rejects with when its signal aborts (a DOMException in browsers, an Error elsewhere). */
function isAbortError(err: unknown): boolean {
  return typeof err === 'object' && err !== null && (err as { name?: unknown }).name === 'AbortError';
}

function get<T>(path: string, opts?: RequestOptions) { return request<T>('GET', path, undefined, opts); }
function post<T>(path: string, body?: unknown, opts?: RequestOptions) { return request<T>('POST', path, body, opts); }
function put<T>(path: string, body?: unknown, opts?: RequestOptions) { return request<T>('PUT', path, body, opts); }
function del<T>(path: string, opts?: RequestOptions) { return request<T>('DELETE', path, undefined, opts); }

async function proxyRequest<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {};
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }

  const res = await fetchOrNetworkError(`${BASE_URL}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    credentials: 'same-origin',
  });

  if (res.status === 404 || res.status === 401) {
    throw new ApiError(`proxy:${res.status}`, res.status, null);
  }

  if (!res.ok) {
    const text = await res.text();
    let message = text || `${res.status}`;
    try {
      const parsed = JSON.parse(text);
      message = parsed.error || parsed.message || message;
    } catch {
      // Fall back to raw text.
    }
    throw new Error(message);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  const contentType = res.headers.get('content-type') || '';
  if (!contentType.includes('application/json')) {
    return undefined as T;
  }

  const json = await res.json();
  return camelizeKeys(json) as T;
}

// Build query string from pagination/filter params. Routes clamp pageSize (1000 at most, 500 on many), so to load a
// complete set use listAll / fetchAllPages from ./paging instead of a large fixed pageSize.
function buildQuery(params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }): string {
  if (!params) return '';
  const parts: string[] = [];
  if (params.pageNumber) parts.push(`pageNumber=${params.pageNumber}`);
  if (params.pageSize) parts.push(`pageSize=${params.pageSize}`);
  if (params.filters) {
    for (const [k, v] of Object.entries(params.filters)) {
      if (v) parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(v)}`);
    }
  }
  return parts.length ? '?' + parts.join('&') : '';
}

/**
 * Every row of a paged enumerate route, collected page by page. Each request returns at most one page; use this where a
 * screen needs the whole set (pickers, maps), and the route itself for one page.
 */
/** One page (default the first, of the largest size) of a POST .../enumerate route, with its true totals. */
async function enumeratePage<T>(path: string, params: { pageNumber?: number; pageSize?: number } = {}, body: Record<string, unknown> = {}): Promise<EnumerationResult<T>> {
  return post<EnumerationResult<T>>(path, { ...body, PageNumber: params.pageNumber ?? 1, PageSize: params.pageSize ?? MAX_PAGE_SIZE });
}

/**
 * One bounded page (the first MAX_PAGE_SIZE rows) of an enumerate route, for the array-returning list helpers below.
 * It never loops over pages: a caller that needs more pages the route itself, explicitly (see api/paging.ts).
 */
async function enumerateFirstPage<T>(path: string, body: Record<string, unknown> = {}): Promise<T[]> {
  const result = await post<EnumerationResult<T>>(path, { ...body, PageNumber: 1, PageSize: MAX_PAGE_SIZE });
  return result?.objects ?? [];
}

function buildRequestHistoryQuery(params?: RequestHistoryQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  const parts: string[] = [];
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.bucketMinutes) search.set('bucketMinutes', String(params.bucketMinutes));
  if (params.method) search.set('method', params.method);
  if (params.principal) search.set('principal', params.principal);
  if (params.tenantId) search.set('tenantId', params.tenantId);
  if (params.userId) search.set('userId', params.userId);
  if (params.credentialId) search.set('credentialId', params.credentialId);
  if (params.statusCode !== undefined && params.statusCode !== null) search.set('statusCode', String(params.statusCode));
  if (params.isSuccess !== undefined && params.isSuccess !== null) search.set('isSuccess', String(params.isSuccess));
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);

  const query = search.toString();
  if (query) parts.push(query);
  if (params.route) parts.push(`route=${encodeWorkspaceQueryPath(params.route)}`);
  return parts.length ? `?${parts.join('&')}` : '';
}

function buildHistoryQuery(params?: HistoricalTimelineQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.objectiveId) search.set('objectiveId', params.objectiveId);
  if (params.vesselId) search.set('vesselId', params.vesselId);
    if (params.environmentId) search.set('environmentId', params.environmentId);
    if (params.deploymentId) search.set('deploymentId', params.deploymentId);
    if (params.incidentId) search.set('incidentId', params.incidentId);
    if (params.postmortemOnly) search.set('postmortemOnly', String(params.postmortemOnly));
    if (params.missionId) search.set('missionId', params.missionId);
  if (params.voyageId) search.set('voyageId', params.voyageId);
  if (params.actor) search.set('actor', params.actor);
  if (params.text) search.set('text', params.text);
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  if (params.sourceTypes && params.sourceTypes.length > 0) search.set('sourceType', params.sourceTypes.join(','));

  const query = search.toString();
  return query ? `?${query}` : '';
}

function buildObjectiveQuery(params?: ObjectiveQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.userId) search.set('userId', params.userId);
  if (params.owner) search.set('owner', params.owner);
  if (params.category) search.set('category', params.category);
  if (params.parentObjectiveId) search.set('parentObjectiveId', params.parentObjectiveId);
  if (params.vesselId) search.set('vesselId', params.vesselId);
  if (params.fleetId) search.set('fleetId', params.fleetId);
  if (params.planningSessionId) search.set('planningSessionId', params.planningSessionId);
  if (params.voyageId) search.set('voyageId', params.voyageId);
  if (params.missionId) search.set('missionId', params.missionId);
  if (params.checkRunId) search.set('checkRunId', params.checkRunId);
  if (params.releaseId) search.set('releaseId', params.releaseId);
  if (params.deploymentId) search.set('deploymentId', params.deploymentId);
  if (params.incidentId) search.set('incidentId', params.incidentId);
  if (params.tag) search.set('tag', params.tag);
  if (params.status) search.set('status', params.status);
  if (params.backlogState) search.set('backlogState', params.backlogState);
  if (params.kind) search.set('kind', params.kind);
  if (params.priority) search.set('priority', params.priority);
  if (params.effort) search.set('effort', params.effort);
  if (params.targetVersion) search.set('targetVersion', params.targetVersion);
  if (params.search) search.set('search', params.search);
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  const query = search.toString();
  return query ? `?${query}` : '';
}

function buildReleaseQuery(params?: ReleaseQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.vesselId) search.set('vesselId', params.vesselId);
  if (params.workflowProfileId) search.set('workflowProfileId', params.workflowProfileId);
  if (params.voyageId) search.set('voyageId', params.voyageId);
  if (params.missionId) search.set('missionId', params.missionId);
  if (params.checkRunId) search.set('checkRunId', params.checkRunId);
  if (params.status) search.set('status', params.status);
  if (params.search) search.set('search', params.search);
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  const query = search.toString();
  return query ? `?${query}` : '';
}

function buildEnvironmentQuery(params?: DeploymentEnvironmentQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.vesselId) search.set('vesselId', params.vesselId);
  if (params.kind) search.set('kind', params.kind);
  if (params.isDefault !== undefined && params.isDefault !== null) search.set('isDefault', String(params.isDefault));
  if (params.active !== undefined && params.active !== null) search.set('active', String(params.active));
  if (params.search) search.set('search', params.search);
  const query = search.toString();
  return query ? `?${query}` : '';
}

function buildDeploymentQuery(params?: DeploymentQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.vesselId) search.set('vesselId', params.vesselId);
  if (params.workflowProfileId) search.set('workflowProfileId', params.workflowProfileId);
  if (params.environmentId) search.set('environmentId', params.environmentId);
  if (params.environmentName) search.set('environmentName', params.environmentName);
  if (params.releaseId) search.set('releaseId', params.releaseId);
  if (params.missionId) search.set('missionId', params.missionId);
  if (params.voyageId) search.set('voyageId', params.voyageId);
  if (params.checkRunId) search.set('checkRunId', params.checkRunId);
  if (params.status) search.set('status', params.status);
  if (params.verificationStatus) search.set('verificationStatus', params.verificationStatus);
  if (params.search) search.set('search', params.search);
  if (params.fromUtc) search.set('fromUtc', params.fromUtc);
  if (params.toUtc) search.set('toUtc', params.toUtc);
  const query = search.toString();
  return query ? `?${query}` : '';
}

function buildIncidentQuery(params?: IncidentQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.vesselId) search.set('vesselId', params.vesselId);
  if (params.environmentId) search.set('environmentId', params.environmentId);
  if (params.deploymentId) search.set('deploymentId', params.deploymentId);
  if (params.releaseId) search.set('releaseId', params.releaseId);
  if (params.missionId) search.set('missionId', params.missionId);
  if (params.voyageId) search.set('voyageId', params.voyageId);
  if (params.status) search.set('status', params.status);
  if (params.severity) search.set('severity', params.severity);
  if (params.search) search.set('search', params.search);
  const query = search.toString();
  return query ? `?${query}` : '';
}

function buildRunbookQuery(params?: RunbookQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.workflowProfileId) search.set('workflowProfileId', params.workflowProfileId);
  if (params.environmentId) search.set('environmentId', params.environmentId);
  if (params.defaultCheckType) search.set('defaultCheckType', params.defaultCheckType);
  if (params.active !== undefined && params.active !== null) search.set('active', String(params.active));
  if (params.search) search.set('search', params.search);
  const query = search.toString();
  return query ? `?${query}` : '';
}

function buildRunbookExecutionQuery(params?: RunbookExecutionQuery): string {
  if (!params) return '';

  const search = new URLSearchParams();
  if (params.pageNumber) search.set('pageNumber', String(params.pageNumber));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.runbookId) search.set('runbookId', params.runbookId);
  if (params.deploymentId) search.set('deploymentId', params.deploymentId);
  if (params.incidentId) search.set('incidentId', params.incidentId);
  if (params.status) search.set('status', params.status);
  if (params.search) search.set('search', params.search);
  const query = search.toString();
  return query ? `?${query}` : '';
}

// ==================== Auth ====================
export const authenticate = (req: AuthenticateRequest) =>
  post<AuthenticateResult>('/api/v1/authenticate', req);

export const whoami = () => get<WhoAmIResult>('/api/v1/whoami');

/** Change the signed-in user's password (required before a default-password session can use the API). */
export const changePassword = (req: PasswordChangeRequest) => put<WhoAmIResult>('/api/v1/account/password', req);

export async function getProxySessionContext(): Promise<ProxySessionContext | null> {
  try {
    return await proxyRequest<ProxySessionContext>('GET', '/proxy-api/v1/session/context');
  } catch (error) {
    if (isApiStatus(error, 404) || isApiStatus(error, 401)) {
      return null;
    }
    throw error;
  }
}

export async function clearProxySessionInstance(): Promise<void> {
  await proxyRequest('POST', '/proxy-api/v1/session/logout-instance');
}

export async function logoutProxy(): Promise<void> {
  try {
    await proxyRequest('POST', '/proxy-api/v1/auth/logout');
  } catch (error) {
    if (isApiStatus(error, 404)) {
      return;
    }
    throw error;
  }
}

/** One page of the tenants where the email exists (pageSize 1 to 1000, default 10); see lib/tenantLookup for every page. */
export const lookupTenants = (email: string, page?: { pageNumber?: number; pageSize?: number }) =>
  post<TenantLookupResult>('/api/v1/tenants/lookup', { Email: email, PageNumber: page?.pageNumber ?? 1, PageSize: page?.pageSize ?? 10 });

// ==================== Tenants (admin) ====================
export const listTenants = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) => get<EnumerationResult<TenantMetadata>>(`/api/v1/tenants${buildQuery(params)}`);
export const createTenant = (data: TenantCreateRequest) => post<TenantCreateResult>('/api/v1/tenants', data);
export const updateTenant = (id: string, data: Partial<TenantMetadata>) => put<TenantMetadata>(`/api/v1/tenants/${id}`, data);
export const deleteTenant = (id: string) => del<void>(`/api/v1/tenants/${id}`);

// ==================== Users (admin) ====================
export const listUsers = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) => get<EnumerationResult<UserMaster>>(`/api/v1/users${buildQuery(params)}`);
export const createUser = (data: UserUpsertRequest) => post<UserMaster>('/api/v1/users', data);
export const updateUser = (id: string, data: UserUpsertRequest) => put<UserMaster>(`/api/v1/users/${id}`, data);
export const deleteUser = (id: string) => del<void>(`/api/v1/users/${id}`);

// ==================== Credentials (admin) ====================
export const listCredentials = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) => get<EnumerationResult<Credential>>(`/api/v1/credentials${buildQuery(params)}`);
export const createCredential = (data: Partial<Credential>) => post<Credential>('/api/v1/credentials', data);
export const updateCredential = (id: string, data: Partial<Credential>) => put<Credential>(`/api/v1/credentials/${id}`, data);
export const deleteCredential = (id: string) => del<void>(`/api/v1/credentials/${id}`);

// ==================== Fleets ====================
export const listFleets = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Fleet>>(`/api/v1/fleets${buildQuery(params)}`);
/** The fleet and its vessel count; the vessels themselves are paged with listVessels and a fleetId filter. */
export const getFleetDetail = (id: string) => get<FleetDetailResponse>(`/api/v1/fleets/${id}`);
export const getFleet = async (id: string): Promise<Fleet> => (await getFleetDetail(id)).fleet;
export const createFleet = (data: Partial<Fleet>) => post<Fleet>('/api/v1/fleets', data);
export const updateFleet = (id: string, data: Partial<Fleet>) => put<Fleet>(`/api/v1/fleets/${id}`, data);
export const deleteFleet = (id: string) => del<void>(`/api/v1/fleets/${id}`);
/** The server accepts at most 1000 ids per bulk delete; larger selections are sent in chunks and the results merged. */
export const BULK_DELETE_CHUNK = 1000;
export async function deleteInChunks(ids: string[], send: (chunk: string[]) => Promise<BatchDeleteResult>): Promise<BatchDeleteResult> {
  const merged: BatchDeleteResult = { deleted: 0, skipped: [] };
  for (let i = 0; i < ids.length; i += BULK_DELETE_CHUNK) {
    const result = await send(ids.slice(i, i + BULK_DELETE_CHUNK));
    merged.deleted += result?.deleted ?? 0;
    merged.skipped.push(...(result?.skipped ?? []));
  }
  return merged;
}
export const deleteFleetsBatch = (ids: string[]) =>
  deleteInChunks(ids, chunk => post<BatchDeleteResult>('/api/v1/fleets/delete/multiple', { Ids: chunk }));

// ==================== Vessels ====================
export const listVessels = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Vessel>>(`/api/v1/vessels${buildQuery(params)}`);
export const getVessel = (id: string) => get<Vessel>(`/api/v1/vessels/${id}`);
export const createVessel = (data: Partial<Vessel>) => post<Vessel>('/api/v1/vessels', data);
export const updateVessel = (id: string, data: Partial<Vessel>) => put<Vessel>(`/api/v1/vessels/${id}`, data);
export const deleteVessel = (id: string) => del<void>(`/api/v1/vessels/${id}`);
// Building a Model Context launches a captain to analyze the repository and can take minutes; allow a long
// timeout so the request does not abort before the captain finishes.
export const buildVesselContext = (id: string, data: { captainId: string; notes?: string }) =>
  post<Vessel>(`/api/v1/vessels/${id}/build-context`, data, { timeout: 900000 });
export const getVesselReadiness = (
  id: string,
  params?: {
    workflowProfileId?: string | null;
    checkType?: string | null;
    environmentName?: string | null;
    includeWorkflowRequirements?: boolean;
  },
) => {
  const query = new URLSearchParams();
  if (params?.workflowProfileId) query.set('workflowProfileId', params.workflowProfileId);
  if (params?.checkType) query.set('checkType', params.checkType);
  if (params?.environmentName) query.set('environmentName', params.environmentName);
  if (typeof params?.includeWorkflowRequirements === 'boolean') query.set('includeWorkflowRequirements', String(params.includeWorkflowRequirements));
  const suffix = query.toString().length > 0 ? `?${query.toString()}` : '';
  return get<VesselReadinessResult>(`/api/v1/vessels/${encodeURIComponent(id)}/readiness${suffix}`);
};
export const getVesselLandingPreview = (id: string, sourceBranch?: string | null) =>
  get<LandingPreviewResult>(`/api/v1/vessels/${encodeURIComponent(id)}/landing-preview${sourceBranch ? `?sourceBranch=${encodeURIComponent(sourceBranch)}` : ''}`);
export const getVesselGitStatus = (id: string) => get<{ vesselId: string; commitsAhead: number | null; commitsBehind: number | null; error?: string }>(`/api/v1/vessels/${id}/git-status`);

export interface BranchInfo {
  name: string;
  isCurrent: boolean;
  isDefault: boolean;
  commitHash: string | null;
  commitSubject: string | null;
  commitDate: string | null;
  ahead: number;
  behind: number;
}

/**
 * The first MAX_PAGE_SIZE branches of a vessel (one page of the paged branches route, never a loop over every page),
 * with its default branch, any git error, and the true branch count from the route's total.
 */
export const getVesselBranches = async (id: string): Promise<{ vesselId: string; defaultBranch?: string; branches: BranchInfo[]; branchCount: number; error?: string }> => {
  const page = await post<EnumerationResult<BranchInfo> & { vesselId: string; defaultBranch?: string; error?: string }>(
    `/api/v1/vessels/${encodeURIComponent(id)}/branches/enumerate`, { PageNumber: 1, PageSize: MAX_PAGE_SIZE });
  const branches = page?.objects ?? [];
  return {
    vesselId: page?.vesselId ?? id,
    defaultBranch: page?.defaultBranch,
    branches,
    branchCount: typeof page?.totalRecords === 'number' ? Math.max(page.totalRecords, branches.length) : branches.length,
    error: page?.error ?? undefined,
  };
};

/** Number of branches of a vessel: a one-branch page of the paged branches route, so the server measures ahead/behind for at most one branch. */
export const getVesselBranchCount = async (id: string): Promise<number> => {
  const page = await post<EnumerationResult<BranchInfo>>(
    `/api/v1/vessels/${encodeURIComponent(id)}/branches/enumerate`, { PageNumber: 1, PageSize: 1 });
  return page.totalRecords;
};

export const pushVesselBranch = (id: string, branch: string) =>
  post<{ vesselId: string; branch: string; pushed: boolean }>(`/api/v1/vessels/${id}/branches/push`, { Branch: branch });

export const mergeVesselBranch = (id: string, source: string, target: string, push: boolean) =>
  post<{ vesselId: string; source: string; target: string; merged: boolean; pushed: boolean }>(`/api/v1/vessels/${id}/branches/merge`, { Source: source, Target: target, Push: push });

export type VesselCleanupDisposition = 'Safe' | 'InUse' | 'Unknown';
export type VesselCleanupResourceType = 'Dock' | 'Worktree' | 'Branch' | 'Repository';

/** One resource in a vessel cleanup inventory. sizeBytes is null when it could not be measured (never estimated). */
export interface VesselCleanupResource {
  key: string;
  resourceType: VesselCleanupResourceType;
  disposition: VesselCleanupDisposition;
  reason: string;
  identity: string;
  path: string | null;
  branchName: string | null;
  repositoryPath: string | null;
  harborId: string | null;
  dockId: string | null;
  missionId: string | null;
  missionStatus: string | null;
  voyageId: string | null;
  captainId: string | null;
  sizeBytes: number | null;
}

export interface VesselCleanupInventory {
  vesselId: string;
  vesselName: string;
  scannedUtc: string;
  /** Resources are not embedded; page them with listVesselCleanupResources. */
  resourceCount: number;
  safeCount: number;
  inUseCount: number;
  unknownCount: number;
  safeMeasuredBytes: number;
  safeUnmeasuredCount: number;
}

export interface VesselCleanupItemResult {
  key: string;
  resourceType: VesselCleanupResourceType | null;
  identity: string | null;
  status: 'Deleted' | 'Skipped' | 'Failed';
  message: string;
}

export interface VesselCleanupResult {
  results: VesselCleanupItemResult[];
  deleted: number;
  skipped: number;
  failed: number;
  inventory: VesselCleanupInventory;
}

/** Read-only cleanup inventory of a vessel's docks, worktrees, and Armada-managed branches. Deletes nothing. */
export const getVesselCleanupInventory = (id: string) =>
  get<VesselCleanupInventory>(`/api/v1/vessels/${encodeURIComponent(id)}/cleanup`, { timeout: 300000 });

/** One page of a vessel's cleanup resources, optionally filtered by disposition. Deletes nothing. */
export const listVesselCleanupResources = (id: string, params?: { pageNumber?: number; pageSize?: number; status?: VesselCleanupDisposition }) =>
  post<EnumerationResult<VesselCleanupResource>>(`/api/v1/vessels/${encodeURIComponent(id)}/cleanup/resources/enumerate`,
    { PageNumber: params?.pageNumber ?? 1, PageSize: params?.pageSize ?? 10, Status: params?.status ?? null }, { timeout: 300000 });

/** Most keys one cleanup request may carry. */
export const CLEANUP_KEY_CHUNK = 1000;

/**
 * Clean up the selected inventory keys; the server revalidates each one and returns per-item outcomes plus a fresh
 * inventory summary. More than 1000 keys are sent in chunks of 1000 and the outcomes merged.
 */
export async function runVesselCleanup(id: string, keys: string[]): Promise<VesselCleanupResult> {
  let merged: VesselCleanupResult | null = null;
  for (let i = 0; i < keys.length || (i === 0 && keys.length === 0); i += CLEANUP_KEY_CHUNK) {
    const result = await post<VesselCleanupResult>(`/api/v1/vessels/${encodeURIComponent(id)}/cleanup`, { Keys: keys.slice(i, i + CLEANUP_KEY_CHUNK) }, { timeout: 600000 });
    if (!merged) merged = { ...result, results: [...(result.results ?? [])] };
    else {
      merged.results.push(...(result.results ?? []));
      merged.deleted += result.deleted;
      merged.skipped += result.skipped;
      merged.failed += result.failed;
      merged.inventory = result.inventory;
    }
    if (keys.length === 0) break;
  }
  return merged!;
}

// ==================== Vessel History ====================
/** Per-day commit counts for a vessel branch (heatmap). A missing repo or branch returns 200 with `error` set. */
export const getVesselCommitActivity = (id: string, params?: VesselCommitActivityQuery) => {
  const query = new URLSearchParams();
  if (params?.branch) query.set('branch', params.branch);
  if (params?.from) query.set('from', params.from);
  if (params?.to) query.set('to', params.to);
  if (typeof params?.utcOffsetMinutes === 'number') query.set('utcOffsetMinutes', String(params.utcOffsetMinutes));
  const suffix = query.toString().length > 0 ? `?${query.toString()}` : '';
  return get<VesselCommitActivity>(`/api/v1/vessels/${encodeURIComponent(id)}/history/activity${suffix}`);
};

/** One page of a vessel's commit history, newest first. Pass `nextCursor` back as `cursor` for the next page. */
export const getVesselCommits = (id: string, params?: VesselCommitQuery) => {
  const query = new URLSearchParams();
  if (params?.branch) query.set('branch', params.branch);
  if (params?.before) query.set('before', params.before);
  if (params?.cursor) query.set('cursor', params.cursor);
  if (typeof params?.limit === 'number') query.set('limit', String(params.limit));
  const suffix = query.toString().length > 0 ? `?${query.toString()}` : '';
  return get<VesselCommitPage>(`/api/v1/vessels/${encodeURIComponent(id)}/history/commits${suffix}`);
};

// ==================== Workspace ====================
function encodeWorkspaceQueryPath(path: string) {
  return encodeURIComponent(path).replace(/%2F/g, '/');
}

export const getWorkspaceStatus = (vesselId: string) =>
  get<WorkspaceStatusResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/status`);
/** One page of one workspace directory (the server default page size is 10; pass pageSize up to 1000). */
export const getWorkspaceTree = (vesselId: string, path?: string, page?: { pageNumber?: number; pageSize?: number }) => {
  const parts: string[] = [];
  if (path) parts.push(`path=${encodeWorkspaceQueryPath(path)}`);
  if (page?.pageNumber) parts.push(`pageNumber=${page.pageNumber}`);
  if (page?.pageSize) parts.push(`pageSize=${page.pageSize}`);
  return get<WorkspaceTreeResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/tree${parts.length ? `?${parts.join('&')}` : ''}`);
};
/** Every entry of one workspace directory, read in explicit pages of 1000 (bounded by MAX_WORKSPACE_PAGES). */
export const MAX_WORKSPACE_PAGES = 100;
export async function listWorkspaceDirectory(vesselId: string, path?: string): Promise<WorkspaceTreeResult> {
  const first = await getWorkspaceTree(vesselId, path, { pageNumber: 1, pageSize: MAX_PAGE_SIZE });
  const entries = [...(first.entries ?? [])];
  const pages = Math.min(first.totalPages ?? 1, MAX_WORKSPACE_PAGES);
  for (let page = 2; page <= pages; page++) {
    const next = await getWorkspaceTree(vesselId, path, { pageNumber: page, pageSize: MAX_PAGE_SIZE });
    if (!next.entries?.length) break;
    entries.push(...next.entries);
  }
  return { ...first, entries };
}
export const getWorkspaceFile = (vesselId: string, path: string) =>
  get<WorkspaceFileResponse>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/file?path=${encodeWorkspaceQueryPath(path)}`);
export const saveWorkspaceFile = (vesselId: string, data: WorkspaceSaveRequest) =>
  put<WorkspaceSaveResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/file`, data);
export const createWorkspaceDirectory = (vesselId: string, data: WorkspaceCreateDirectoryRequest) =>
  post<WorkspaceOperationResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/directory`, data);
export const renameWorkspaceEntry = (vesselId: string, data: WorkspaceRenameRequest) =>
  post<WorkspaceOperationResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/rename`, data);
export const deleteWorkspaceEntry = (vesselId: string, path: string) =>
  del<WorkspaceOperationResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/entry?path=${encodeWorkspaceQueryPath(path)}`);
export const searchWorkspace = (vesselId: string, query: string, maxResults = 200) =>
  get<WorkspaceSearchResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/search?q=${encodeURIComponent(query)}&maxResults=${maxResults}`);
/** Branch state and one page of changed files (server default page size 10; totalRecords counts every change). */
export const getWorkspaceChanges = (vesselId: string, page?: { pageNumber?: number; pageSize?: number }) => {
  const parts: string[] = [];
  if (page?.pageNumber) parts.push(`pageNumber=${page.pageNumber}`);
  if (page?.pageSize) parts.push(`pageSize=${page.pageSize}`);
  return get<WorkspaceChangesResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/changes${parts.length ? `?${parts.join('&')}` : ''}`);
};
/** Every changed file, read in explicit pages of 1000 (bounded by MAX_WORKSPACE_PAGES). */
export async function listAllWorkspaceChanges(vesselId: string): Promise<WorkspaceChangesResult> {
  const first = await getWorkspaceChanges(vesselId, { pageNumber: 1, pageSize: MAX_PAGE_SIZE });
  const changes = [...(first.changes ?? [])];
  const pages = Math.min(first.totalPages ?? 1, MAX_WORKSPACE_PAGES);
  for (let page = 2; page <= pages; page++) {
    const next = await getWorkspaceChanges(vesselId, { pageNumber: page, pageSize: MAX_PAGE_SIZE });
    if (!next.changes?.length) break;
    changes.push(...next.changes);
  }
  return { ...first, changes };
}
/** One page of the vessel's active missions with their scoped files (the status carries only activeMissionCount). */
export const listWorkspaceActiveMissions = (vesselId: string, page?: { pageNumber?: number; pageSize?: number }) =>
  post<EnumerationResult<WorkspaceActiveMission>>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/active-missions/enumerate`,
    { PageNumber: page?.pageNumber ?? 1, PageSize: page?.pageSize ?? 10 });
/** Every active mission on the vessel, read in explicit pages of 1000. */
export async function listAllWorkspaceActiveMissions(vesselId: string): Promise<WorkspaceActiveMission[]> {
  const all: WorkspaceActiveMission[] = [];
  for (let page = 1; page <= MAX_WORKSPACE_PAGES; page++) {
    const result = await listWorkspaceActiveMissions(vesselId, { pageNumber: page, pageSize: MAX_PAGE_SIZE });
    all.push(...(result.objects ?? []));
    if (!result.objects?.length || page >= (result.totalPages ?? 1)) break;
  }
  return all;
}
/** Open a terminal window at the vessel's checkout on the machine that holds it (its Harbor, or the Admiral host). */
export const openWorkspaceTerminal = (vesselId: string) =>
  post<WorkspaceOpenTerminalResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/open-terminal`, {});

// ==================== Request History ====================
export const listRequestHistory = (params?: RequestHistoryQuery) =>
  get<EnumerationResult<RequestHistoryEntry>>(`/api/v1/request-history${buildRequestHistoryQuery(params)}`);
export const getRequestHistorySummary = (params?: RequestHistoryQuery) =>
  get<RequestHistorySummaryResult>(`/api/v1/request-history/summary${buildRequestHistoryQuery(params)}`);
export const getRequestHistoryEntry = (id: string) =>
  get<RequestHistoryRecord>(`/api/v1/request-history/${encodeURIComponent(id)}`);
export const deleteRequestHistoryEntry = (id: string) =>
  del<void>(`/api/v1/request-history/${encodeURIComponent(id)}`);
export const deleteRequestHistoryEntries = (ids: string[]) =>
  post<BatchDeleteResult>('/api/v1/request-history/delete/multiple', { Ids: ids });
export const deleteRequestHistoryByFilter = (query: RequestHistoryQuery) =>
  post<BatchDeleteResult>('/api/v1/request-history/delete/by-filter', query);

// ==================== History ====================
export const listHistoryTimeline = (params?: HistoricalTimelineQuery) =>
  get<EnumerationResult<HistoricalTimelineEntry>>(`/api/v1/history${buildHistoryQuery(params)}`);
export const enumerateHistoryTimeline = (query?: HistoricalTimelineQuery) =>
  post<EnumerationResult<HistoricalTimelineEntry>>('/api/v1/history/enumerate', query || {});

// ==================== Objectives ====================
export const listObjectives = (params?: ObjectiveQuery) =>
  get<EnumerationResult<Objective>>(`/api/v1/objectives${buildObjectiveQuery(params)}`);
export const enumerateObjectives = (query?: ObjectiveQuery) =>
  post<EnumerationResult<Objective>>('/api/v1/objectives/enumerate', query || {});
export const reorderObjectives = (data: ObjectiveReorderRequest) => post<ObjectiveReorderResult>('/api/v1/objectives/reorder', data);
export const getObjective = (id: string) => get<Objective>(`/api/v1/objectives/${encodeURIComponent(id)}`);
export const createObjective = (data: ObjectiveUpsertRequest) => post<Objective>('/api/v1/objectives', data);
export const updateObjective = (id: string, data: ObjectiveUpsertRequest) => put<Objective>(`/api/v1/objectives/${encodeURIComponent(id)}`, data);
export const deleteObjective = (id: string) => del<void>(`/api/v1/objectives/${encodeURIComponent(id)}`);
export const importObjectiveFromGitHub = (data: GitHubObjectiveImportRequest) => post<Objective>('/api/v1/objectives/import/github', data);
export const listBacklog = (params?: ObjectiveQuery) =>
  get<EnumerationResult<Objective>>(`/api/v1/backlog${buildObjectiveQuery(params)}`);
export const enumerateBacklog = (query?: ObjectiveQuery) =>
  post<EnumerationResult<Objective>>('/api/v1/backlog/enumerate', query || {});
export const reorderBacklog = (data: ObjectiveReorderRequest) => post<ObjectiveReorderResult>('/api/v1/backlog/reorder', data);
export const getBacklogItem = (id: string) => get<Objective>(`/api/v1/backlog/${encodeURIComponent(id)}`);
export const createBacklogItem = (data: ObjectiveUpsertRequest) => post<Objective>('/api/v1/backlog', data);
export const updateBacklogItem = (id: string, data: ObjectiveUpsertRequest) => put<Objective>(`/api/v1/backlog/${encodeURIComponent(id)}`, data);
export const deleteBacklogItem = (id: string) => del<void>(`/api/v1/backlog/${encodeURIComponent(id)}`);
/** Paging body for the POST .../enumerate routes (PascalCase for the C# server). */
function pagedBody(query?: PagedQuery): Record<string, unknown> {
  return {
    PageNumber: query?.pageNumber ?? 1,
    PageSize: query?.pageSize ?? 10,
    ...(query?.order ? { Order: query.order } : {}),
    ...(query?.status ? { Status: query.status } : {}),
  };
}

/** One page of an objective's refinement sessions, most recently updated first (TotalRecords counts them all). */
export const listObjectiveRefinementSessions = (objectiveId: string, query?: PagedQuery) =>
  post<EnumerationResult<ObjectiveRefinementSession>>(`/api/v1/objectives/${encodeURIComponent(objectiveId)}/refinement-sessions/enumerate`, pagedBody(query));
/** One page of a backlog item's refinement sessions, most recently updated first (TotalRecords counts them all). */
export const listBacklogRefinementSessions = (objectiveId: string, query?: PagedQuery) =>
  post<EnumerationResult<ObjectiveRefinementSession>>(`/api/v1/backlog/${encodeURIComponent(objectiveId)}/refinement-sessions/enumerate`, pagedBody(query));
/** One page of a refinement session's transcript; newest first unless `order` is CreatedAscending. */
export const listObjectiveRefinementMessages = (sessionId: string, query?: PagedQuery) =>
  post<EnumerationResult<ObjectiveRefinementMessage>>(`/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/messages/enumerate`, pagedBody(query));
export const createObjectiveRefinementSession = (objectiveId: string, data: ObjectiveRefinementSessionCreateRequest) =>
  post<ObjectiveRefinementSessionDetail>(`/api/v1/objectives/${encodeURIComponent(objectiveId)}/refinement-sessions`, data, { timeout: PLANNING_CREATE_TIMEOUT_MS });
export const createBacklogRefinementSession = (objectiveId: string, data: ObjectiveRefinementSessionCreateRequest) =>
  post<ObjectiveRefinementSessionDetail>(`/api/v1/backlog/${encodeURIComponent(objectiveId)}/refinement-sessions`, data, { timeout: PLANNING_CREATE_TIMEOUT_MS });
export const getObjectiveRefinementSession = (sessionId: string) =>
  get<ObjectiveRefinementSessionDetail>(`/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}`);
export const sendObjectiveRefinementMessage = (sessionId: string, data: ObjectiveRefinementMessageRequest) =>
  post<ObjectiveRefinementSessionDetail>(`/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/messages`, data);
export const summarizeObjectiveRefinementSession = (sessionId: string, data?: ObjectiveRefinementSummaryRequest) =>
  post<ObjectiveRefinementSummaryResponse>(`/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/summarize`, data || {}, { timeout: PLANNING_SUMMARIZE_TIMEOUT_MS });
export const applyObjectiveRefinementSummary = (sessionId: string, data?: ObjectiveRefinementApplyRequest) =>
  post<ObjectiveRefinementApplyResponse>(`/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/apply`, data || {}, { timeout: PLANNING_SUMMARIZE_TIMEOUT_MS });
export const stopObjectiveRefinementSession = (sessionId: string) =>
  post<ObjectiveRefinementSessionDetail>(`/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/stop`);
export const deleteObjectiveRefinementSession = (sessionId: string) =>
  del<void>(`/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}`);

// ==================== Captains ====================
export const listCaptains = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Captain>>(`/api/v1/captains${buildQuery(params)}`);
export const getCaptain = (id: string) => get<Captain>(`/api/v1/captains/${id}`);
// The runtime tool probe (e.g. Mux) launches the CLI and can take tens of seconds; allow well
// beyond the default 30s so slow probes resolve instead of aborting and reading as "unknown".
// Tools come back one page at a time (Tools is an EnumerationResult); the server reuses its probe snapshot for 60
// seconds unless refresh is set, so paging does not probe again.
export const getCaptainTools = (id: string, query: CaptainToolsQuery = {}) => {
  const search = new URLSearchParams();
  if (query.pageNumber) search.set('pageNumber', String(query.pageNumber));
  if (query.pageSize) search.set('pageSize', String(query.pageSize));
  if (query.sourceKind) search.set('sourceKind', query.sourceKind);
  if (query.refresh) search.set('refresh', 'true');
  const qs = search.toString();
  return get<CaptainToolAccessResult>(`/api/v1/captains/${id}/tools${qs ? `?${qs}` : ''}`, { timeout: 120000 });
};
export const createCaptain = (data: Partial<Captain>) => post<Captain>('/api/v1/captains', data);
export const updateCaptain = (id: string, data: Partial<Captain>) => put<Captain>(`/api/v1/captains/${id}`, data);
export const deleteCaptain = (id: string) => del<void>(`/api/v1/captains/${id}`);
export const getCaptainLog = (id: string, lines = 500, formatted = false) => get<LogResult>(`/api/v1/captains/${id}/log?lines=${lines}${formatted ? '&formatted=true' : ''}`);
export const stopCaptain = (id: string) => post<void>(`/api/v1/captains/${id}/stop`);
export const unquarantineCaptain = (id: string) => post<Captain>(`/api/v1/captains/${id}/unquarantine`);
/** Recall a captain: the server's stop route kills its process and returns it to Idle. */
export const recallCaptain = (id: string) => post<void>(`/api/v1/captains/${encodeURIComponent(id)}/stop`);
export const stopAllCaptains = () => post<void>('/api/v1/captains/stop-all');

// Background jobs
/**
 * Background jobs, newest first. With no arguments the server returns every job in scope (the Jobs page). Pass a
 * status list, kind, or page size to get one filtered page instead; the header indicator polls only active jobs.
 */
export const listJobs = (params?: { status?: Job['status'][]; kind?: Job['kind']; pageNumber?: number; pageSize?: number }) => {
  const parts: string[] = [];
  if (params?.status && params.status.length > 0) parts.push(`status=${encodeURIComponent(params.status.join(','))}`);
  if (params?.kind) parts.push(`kind=${encodeURIComponent(params.kind)}`);
  // Always one page: without paging parameters the route returns every job (deprecated).
  parts.push(`pageNumber=${params?.pageNumber ?? 1}`);
  parts.push(`pageSize=${params?.pageSize ?? 100}`);
  return get<EnumerationResult<Job>>(`/api/v1/jobs?${parts.join('&')}`);
};
export const getJob = (id: string) => get<Job>(`/api/v1/jobs/${id}`);
export const cancelJob = (id: string) => post<Job>(`/api/v1/jobs/${id}/cancel`);

// ==================== Vessel Health ====================
export const enumerateVesselHealth = (query: VesselHealthEnumerateRequest) =>
  post<EnumerationResult<VesselHealth>>('/api/v1/vessel-health/enumerate', query);
export const getVesselHealthSummary = () => get<VesselHealthSummary>('/api/v1/vessel-health/summary');
export const getVesselHealth = (vesselId: string) =>
  get<VesselHealthDetail>(`/api/v1/vessels/${encodeURIComponent(vesselId)}/health`);
/** One page of a vessel's outdated or vulnerable dependency rows (the health detail carries only dependencyCount). */
export const listVesselDependencies = (vesselId: string, page?: { pageNumber?: number; pageSize?: number }) =>
  post<EnumerationResult<VesselDependency>>(`/api/v1/vessels/${encodeURIComponent(vesselId)}/health/dependencies/enumerate`,
    { PageNumber: page?.pageNumber ?? 1, PageSize: page?.pageSize ?? 10 });
/** Starts an evaluation job. 409 (already running) is not an error: it resolves with `alreadyRunning: true` and the running job id. */
export const evaluateVesselHealth = (body: VesselHealthEvaluateRequest = {}) =>
  post<VesselHealthEvaluationStart>('/api/v1/vessel-health/evaluate', body, { acceptStatuses: [409] });
export const setVesselHealthOverride = (vesselId: string, criterion: VesselHealthCriterion, status: VesselHealthStatus, note?: string | null) =>
  put<VesselHealthDetail>(`/api/v1/vessels/${encodeURIComponent(vesselId)}/health/overrides/${encodeURIComponent(criterion)}`, { Status: status, Note: note ?? null });
export const deleteVesselHealthOverride = (vesselId: string, criterion: VesselHealthCriterion) =>
  del<VesselHealthDetail>(`/api/v1/vessels/${encodeURIComponent(vesselId)}/health/overrides/${encodeURIComponent(criterion)}`);
/** One page of the saved Mux endpoints, ordered by name. */
export const enumerateMuxEndpoints = (params: { configDirectory?: string | null; pageNumber?: number; pageSize?: number } = {}) => {
  const search = new URLSearchParams();
  search.set('pageNumber', String(params.pageNumber ?? 1));
  search.set('pageSize', String(params.pageSize ?? 10));
  if (params.configDirectory) search.set('configDirectory', params.configDirectory);
  return get<MuxEndpointPage>(`/api/v1/runtimes/mux/endpoints?${search.toString()}`);
};
/**
 * Every saved Mux endpoint for a picker: an explicit, bounded iteration over pages of 1000 until the server's
 * totalPages (or the MAX_PAGES safety stop) is reached. Returns the first page's metadata with every endpoint in objects (totalRecords is the
 * server's count); a failed page is returned as is.
 */
export const listAllMuxEndpoints = async (configDirectory?: string | null): Promise<MuxEndpointPage> => {
  const first = await enumerateMuxEndpoints({ configDirectory, pageNumber: 1, pageSize: MAX_PAGE_SIZE });
  if (!first || !first.success) return first;
  const objects = [...(first.objects ?? [])];
  for (let page = 2; page <= Math.min(first.totalPages ?? 1, MAX_PAGES); page++) {
    const next = await enumerateMuxEndpoints({ configDirectory, pageNumber: page, pageSize: MAX_PAGE_SIZE });
    if (!next || !next.success) return next;
    objects.push(...(next.objects ?? []));
  }
  return { ...first, objects };
};
/** Picker helper used by the captain forms: every saved Mux endpoint, read page by page (see listAllMuxEndpoints). */
export const listMuxEndpoints = (configDirectory?: string | null) => listAllMuxEndpoints(configDirectory);
export const getMuxEndpoint = (name: string, configDirectory?: string | null) =>
  get<MuxEndpointShowResult>(`/api/v1/runtimes/mux/endpoints/${encodeURIComponent(name)}${configDirectory ? `?configDirectory=${encodeURIComponent(configDirectory)}` : ''}`);

/** Restart a captain by deleting and recreating it with the same persisted configuration. */
export async function restartCaptain(id: string): Promise<Captain> {
  const captain = await getCaptain(id);
  await deleteCaptain(id);
  return createCaptain({
    name: captain.name,
    runtime: captain.runtime,
    systemInstructions: captain.systemInstructions,
    model: captain.model,
    allowedPersonas: captain.allowedPersonas,
    preferredPersona: captain.preferredPersona,
    runtimeOptionsJson: captain.runtimeOptionsJson,
  });
}

// ==================== Missions ====================
export const listMissions = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Mission>>(`/api/v1/missions${buildQuery(params)}`);
export const listMissionSummaries = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<MissionSummary>>(`/api/v1/missions/summaries${buildQuery(params)}`);
export const getMission = (id: string) => get<Mission>(`/api/v1/missions/${id}`);
export const getMissionHistory = (params?: { fromUtc?: string; toUtc?: string; bucketMinutes?: number; fleetId?: string; vesselId?: string }) =>
  get<MissionHistorySummaryResult>(`/api/v1/missions/history${buildQuery(params ? {
    filters: Object.fromEntries(
      Object.entries(params)
        .filter(([, value]) => value !== undefined && value !== null && value !== '')
        .map(([key, value]) => [key, String(value)]),
    ),
  } : undefined)}`);
/** Token-usage summary: at most 1000 buckets (a larger window is a 400); byModel lists the modelLimit top models and otherModels sums the rest. */
export const getTokenUsage = (params?: { fromUtc?: string; toUtc?: string; bucketMinutes?: number; modelLimit?: number; model?: string; runtime?: string; source?: string; sourceId?: string; vesselId?: string; captainId?: string; harborId?: string }) =>
  get<TokenUsageSummaryResult>(`/api/v1/token-usage/summary${buildQuery(params ? {
    filters: Object.fromEntries(
      Object.entries(params)
        .filter(([, value]) => value !== undefined && value !== null && value !== '')
        .map(([key, value]) => [key, String(value)]),
    ),
  } : undefined)}`);
export const getMissionLandingPreview = (id: string) => get<LandingPreviewResult>(`/api/v1/missions/${encodeURIComponent(id)}/landing-preview`);
export const getMissionGitHubPullRequest = (id: string) => get<GitHubPullRequestDetail>(`/api/v1/missions/${encodeURIComponent(id)}/github/pull-request`);
/** One page (1 to 100, GitHub's largest page) of the mission pull request's reviews; newest first unless order is CreatedAscending. */
export const enumerateMissionPullRequestReviews = (id: string, query?: { pageNumber?: number; pageSize?: number; order?: string }) =>
  post<EnumerationResult<GitHubPullRequestReview>>(`/api/v1/missions/${encodeURIComponent(id)}/github/pull-request/reviews/enumerate`, query ?? {});
/** One page of the mission pull request's issue comments. */
export const enumerateMissionPullRequestComments = (id: string, query?: { pageNumber?: number; pageSize?: number; order?: string }) =>
  post<EnumerationResult<GitHubPullRequestComment>>(`/api/v1/missions/${encodeURIComponent(id)}/github/pull-request/comments/enumerate`, query ?? {});
/** One page of the provider checks on the mission pull request's head commit. */
export const enumerateMissionPullRequestChecks = (id: string, query?: { pageNumber?: number; pageSize?: number; order?: string }) =>
  post<EnumerationResult<GitHubPullRequestCheck>>(`/api/v1/missions/${encodeURIComponent(id)}/github/pull-request/checks/enumerate`, query ?? {});
export const createMission = (data: Partial<Mission>) => post<Mission>('/api/v1/missions', data);
export const updateMission = (id: string, data: Partial<Mission>) => put<Mission>(`/api/v1/missions/${id}`, data);
export const deleteMission = (id: string) => del<void>(`/api/v1/missions/${id}`);
export const purgeMission = (id: string) => del<void>(`/api/v1/missions/${id}/purge`);
export const dispatchMission = (data: DispatchRequest) => post<Mission>('/api/v1/missions', data);
export const restartMission = (id: string) => post<Mission>(`/api/v1/missions/${id}/restart`);
export const retryMissionLanding = (id: string) => post<any>(`/api/v1/missions/${id}/retry-landing`, {});
export const transitionMission = (id: string, data: TransitionRequest) => put<Mission>(`/api/v1/missions/${id}/status`, data);
export const approveMissionReview = (id: string, options?: { comment?: string; conditional?: boolean }) =>
  post<Mission>(`/api/v1/missions/${id}/review/approve`, { comment: options?.comment || undefined, conditional: options?.conditional || undefined });
export const denyMissionReview = (id: string, options?: { comment?: string; action?: 'RetryStage' | 'FailPipeline' }) =>
  post<Mission>(`/api/v1/missions/${id}/review/deny`, { comment: options?.comment || undefined, action: options?.action || undefined });
export const getMissionDiff = (id: string) => get<DiffResult>(`/api/v1/missions/${id}/diff`, { timeout: 30000 });
export const getMissionLog = (id: string, lines = 500) => get<LogResult>(`/api/v1/missions/${id}/log?lines=${lines}`);
export const getMissionInstructions = (id: string) => get<InstructionsResult>(`/api/v1/missions/${id}/instructions`);

// Black box (mission and voyage failure records, MISSION_BLACKBOX_PLAN.md 8.1; routes in BlackBoxRoutes.cs). An entry
// the caller cannot see is 404 with data.code BlackBoxNotFound (read codes with apiErrorCode). Bodies are PascalCase for
// the C# server; responses are camelized by `request` (action arguments are kept as written).

const BLACKBOX = '/api/v1/blackbox';
const BLACKBOX_READ: RequestOptions = { dictionaryKeys: ['arguments'] };

function blackBoxFilterBody(query?: BlackBoxEnumerateQuery): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  if (!query) return body;
  if (query.pageNumber !== undefined) body.PageNumber = query.pageNumber;
  if (query.pageSize !== undefined) body.PageSize = query.pageSize;
  if (query.order) body.Order = query.order;
  if (query.tenantId) body.TenantId = query.tenantId;
  if (query.fleetId) body.FleetId = query.fleetId;
  if (query.vesselId) body.VesselId = query.vesselId;
  if (query.voyageId) body.VoyageId = query.voyageId;
  if (query.missionId) body.MissionId = query.missionId;
  if (query.captainId) body.CaptainId = query.captainId;
  if (query.harborId) body.HarborId = query.harborId;
  if (query.subjectType) body.SubjectType = query.subjectType;
  if (query.phase) body.Phase = query.phase;
  if (query.step) body.Step = query.step;
  if (query.kind) body.Kind = query.kind;
  if (query.triage) body.Triage = query.triage;
  if (query.resolution) body.Resolution = query.resolution;
  if (query.state) body.State = query.state;
  if (query.captureMode) body.CaptureMode = query.captureMode;
  if (query.fingerprint) body.Fingerprint = query.fingerprint;
  if (query.pinned !== undefined) body.Pinned = query.pinned;
  if (query.isCurrent !== undefined) body.IsCurrent = query.isCurrent;
  if (query.path) body.Path = query.path;
  if (query.createdAfter) body.CreatedAfter = query.createdAfter;
  if (query.createdBefore) body.CreatedBefore = query.createdBefore;
  return body;
}

function blackBoxQuery(params: Record<string, string | number | boolean | null | undefined>): string {
  const parts: string[] = [];
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue;
    parts.push(`${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`);
  }
  return parts.length ? `?${parts.join('&')}` : '';
}

function blackBoxBriefQuery(opts?: { redactHosts?: boolean }): string {
  return blackBoxQuery({ redactHosts: opts?.redactHosts ? true : undefined });
}

function blackBoxPagedBody(query?: { pageNumber?: number; pageSize?: number; order?: string }): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  if (query?.pageNumber !== undefined) body.PageNumber = query.pageNumber;
  if (query?.pageSize !== undefined) body.PageSize = query.pageSize;
  if (query?.order) body.Order = query.order;
  return body;
}

/** Turns a failed raw black box response (object read, download) into the same ApiError `request` throws. */
async function blackBoxRawError(res: Response): Promise<ApiError> {
  if (res.status === 401) { onUnauthorized?.(); return unauthorizedError(); }
  const text = await res.text();
  let msg = `${res.status}: ${text}`;
  let data: unknown = null;
  try {
    const e = JSON.parse(text);
    msg = e.Message || e.message || e.Error || e.error || msg;
    data = camelizeKeys(e.Data ?? e.data ?? null);
  } catch {
    // use raw text
  }
  return new ApiError(msg, res.status, data);
}

function blackBoxRawHeaders(): Record<string, string> {
  const own: Record<string, string> = {};
  if (authToken) own['X-Token'] = authToken;
  return withExtraHeaders(own);
}

/** The mission's current black box entry with its record (404 BlackBoxNotFound when it has none). */
export const getMissionBlackBox = (missionId: string, includeRecord = true) =>
  get<BlackBoxEntry>(`/api/v1/missions/${encodeURIComponent(missionId)}/blackbox${blackBoxQuery({ includeRecord: includeRecord ? undefined : false })}`, BLACKBOX_READ);
/** The FIX THIS brief (Markdown) of the mission's current entry. */
export const getMissionBlackBoxBrief = (missionId: string, opts?: { redactHosts?: boolean }) =>
  get<string>(`/api/v1/missions/${encodeURIComponent(missionId)}/blackbox/brief${blackBoxBriefQuery(opts)}`, { rawText: true });
/** One page of a mission's black box entries (every failed attempt), newest first. */
export const listMissionBlackBoxEntries = (missionId: string, params?: { pageNumber?: number; pageSize?: number; order?: string }) =>
  get<EnumerationResult<BlackBoxEntrySummary>>(`/api/v1/missions/${encodeURIComponent(missionId)}/blackbox/entries${blackBoxQuery({ pageNumber: params?.pageNumber, pageSize: params?.pageSize, order: params?.order })}`);
/** POST twin of listMissionBlackBoxEntries. */
export const enumerateMissionBlackBoxEntries = (missionId: string, query?: { pageNumber?: number; pageSize?: number; order?: string }) =>
  post<EnumerationResult<BlackBoxEntrySummary>>(`/api/v1/missions/${encodeURIComponent(missionId)}/blackbox/entries/enumerate`, blackBoxPagedBody(query));
/** The voyage's current black box entry with its record. */
export const getVoyageBlackBox = (voyageId: string, includeRecord = true) =>
  get<BlackBoxEntry>(`/api/v1/voyages/${encodeURIComponent(voyageId)}/blackbox${blackBoxQuery({ includeRecord: includeRecord ? undefined : false })}`, BLACKBOX_READ);
/** The FIX THIS brief (Markdown) of the voyage's current entry. */
export const getVoyageBlackBoxBrief = (voyageId: string, opts?: { redactHosts?: boolean }) =>
  get<string>(`/api/v1/voyages/${encodeURIComponent(voyageId)}/blackbox/brief${blackBoxBriefQuery(opts)}`, { rawText: true });

/** One black box entry, with its record unless includeRecord is false. */
export const getBlackBox = (entryId: string, includeRecord = true) =>
  get<BlackBoxEntry>(`${BLACKBOX}/${encodeURIComponent(entryId)}?includeRecord=${includeRecord}`, BLACKBOX_READ);
/** The FIX THIS brief (Markdown) of an entry. */
export const getBlackBoxBrief = (entryId: string, opts?: { redactHosts?: boolean }) =>
  get<string>(`${BLACKBOX}/${encodeURIComponent(entryId)}/brief${blackBoxBriefQuery(opts)}`, { rawText: true });
/** One record section (the returned record carries only that section). 400 for an unknown section name. */
export const getBlackBoxSection = (entryId: string, section: BlackBoxSection) =>
  get<BlackBoxSectionResult>(`${BLACKBOX}/${encodeURIComponent(entryId)}/sections/${encodeURIComponent(section)}`, BLACKBOX_READ);
/** One page of an entry's objects (record.json, briefs and evidence). */
export const listBlackBoxObjects = (entryId: string, params?: { pageNumber?: number; pageSize?: number }) =>
  get<EnumerationResult<BlackBoxObjectSummary>>(`${BLACKBOX}/${encodeURIComponent(entryId)}/objects${blackBoxQuery({ pageNumber: params?.pageNumber, pageSize: params?.pageSize })}`);
/** POST twin of listBlackBoxObjects. */
export const enumerateBlackBoxObjects = (entryId: string, query?: { pageNumber?: number; pageSize?: number; order?: string }) =>
  post<EnumerationResult<BlackBoxObjectSummary>>(`${BLACKBOX}/${encodeURIComponent(entryId)}/objects/enumerate`, blackBoxPagedBody(query));

/**
 * Read (part of) one object: at most 1 MB per read from offset; nextOffset says where the next read starts. The name is
 * sent as one encoded path segment (evidence/git-status.txt becomes evidence%2Fgit-status.txt).
 */
export async function readBlackBoxObject(entryId: string, name: string, range?: { offset?: number; length?: number }): Promise<BlackBoxObjectRead> {
  const query = blackBoxQuery({ offset: range?.offset, length: range?.length }).replace(/^\?/, '');
  const res = await fetchOrNetworkError(`${BASE_URL}/api/v1/blackbox/${encodeURIComponent(entryId)}/objects/${encodeURIComponent(name)}?${query}`, { method: 'GET', headers: blackBoxRawHeaders() });
  if (!res.ok) throw await blackBoxRawError(res);
  const contentType = res.headers.get('Content-Type') || 'application/octet-stream';
  const etag = (res.headers.get('ETag') || '').replace(/"/g, '') || null;
  const offset = Number(res.headers.get('X-BlackBox-Offset') ?? range?.offset ?? 0);
  const totalHeader = res.headers.get('X-BlackBox-Total-Bytes');
  const nextHeader = res.headers.get('X-BlackBox-Next-Offset');
  const body = await res.blob();
  const textual = /^text\/|^application\/json/i.test(contentType);
  return {
    contentType,
    etag,
    offset,
    totalBytes: totalHeader !== null ? Number(totalHeader) : body.size,
    nextOffset: nextHeader !== null ? Number(nextHeader) : null,
    text: textual ? await body.text() : null,
    body,
  };
}

/** Download an entry as a .zip (every object plus manifest.json) through the host's platform adapter. */
export async function downloadBlackBox(entryId: string): Promise<void> {
  const res = await fetchOrNetworkError(`${BASE_URL}/api/v1/blackbox/${encodeURIComponent(entryId)}/download`, { method: 'GET', headers: blackBoxRawHeaders() });
  if (!res.ok) throw await blackBoxRawError(res);
  const blob = await res.blob();
  const disposition = res.headers.get('Content-Disposition') || '';
  const match = disposition.match(/filename="?([^"]+)"?/);
  const filename = match ? match[1] : `${entryId}.zip`;
  if (!platformAdapter) throw new Error('No platform adapter is configured to save downloaded files.');
  await platformAdapter.saveFile({ filename, contentType: res.headers.get('Content-Type') || 'application/zip', body: blob });
}

/** One page of the notes on an entry, newest first. */
export const listBlackBoxNotes = (entryId: string, params?: { pageNumber?: number; pageSize?: number }) =>
  get<EnumerationResult<BlackBoxNote>>(`${BLACKBOX}/${encodeURIComponent(entryId)}/notes${blackBoxQuery({ pageNumber: params?.pageNumber, pageSize: params?.pageSize })}`);
/** POST twin of listBlackBoxNotes. */
export const enumerateBlackBoxNotes = (entryId: string, query?: { pageNumber?: number; pageSize?: number; order?: string }) =>
  post<EnumerationResult<BlackBoxNote>>(`${BLACKBOX}/${encodeURIComponent(entryId)}/notes/enumerate`, blackBoxPagedBody(query));
/** Add a note (1 to 8000 characters; author Operator). */
export const addBlackBoxNote = (entryId: string, body: string) =>
  post<BlackBoxNote>(`${BLACKBOX}/${encodeURIComponent(entryId)}/notes`, { Body: body });

/** One page of the entries visible to the caller (filters on the querystring), newest first. */
export const listBlackBox = (query?: BlackBoxEnumerateQuery) =>
  get<EnumerationResult<BlackBoxEntrySummary>>(`${BLACKBOX}${blackBoxQuery({
    tenantId: query?.tenantId, fleetId: query?.fleetId, vesselId: query?.vesselId, voyageId: query?.voyageId, missionId: query?.missionId,
    captainId: query?.captainId, harborId: query?.harborId, subjectType: query?.subjectType, phase: query?.phase, step: query?.step,
    kind: query?.kind, triage: query?.triage, resolution: query?.resolution, state: query?.state, captureMode: query?.captureMode,
    fingerprint: query?.fingerprint, pinned: query?.pinned, isCurrent: query?.isCurrent, path: query?.path,
    createdAfter: query?.createdAfter, createdBefore: query?.createdBefore, pageNumber: query?.pageNumber, pageSize: query?.pageSize, order: query?.order,
  })}`);
/** POST twin of listBlackBox (filters in the body). */
export const enumerateBlackBox = (query?: BlackBoxEnumerateQuery) =>
  post<EnumerationResult<BlackBoxEntrySummary>>(`${BLACKBOX}/enumerate`, blackBoxFilterBody(query));
/** Full-text search over reason, summary and notes (query 1 to 200 characters) with the same filters. */
export const searchBlackBox = (query: BlackBoxSearchQuery) =>
  post<EnumerationResult<BlackBoxEntrySummary>>(`${BLACKBOX}/search`, { ...blackBoxFilterBody(query), Query: query.query });
/** Maintainer bundle: up to 50 entries grouped by fingerprint, as Markdown (default) or JSON. */
export const getBlackBoxBundle = (query?: BlackBoxBundleQuery) =>
  post<BlackBoxBundleResult>(`${BLACKBOX}/bundle`, {
    ...blackBoxFilterBody(query),
    ...(query?.max !== undefined ? { Max: query.max } : {}),
    ...(query?.format ? { Format: query.format } : {}),
    ...(query?.redactHosts ? { RedactHosts: true } : {}),
  });
/** One page of directories that recur in black box file rows, most entries first. */
export const enumerateBlackBoxHotspots = (query?: BlackBoxHotspotQuery) =>
  post<EnumerationResult<BlackBoxHotspot>>(`${BLACKBOX}/hotspots/enumerate`, {
    ...blackBoxPagedBody(query),
    ...(query?.vesselId ? { VesselId: query.vesselId } : {}),
    ...(query?.role ? { Role: query.role } : {}),
    ...(query?.tenantId ? { TenantId: query.tenantId } : {}),
    ...(query?.createdAfter ? { CreatedAfter: query.createdAfter } : {}),
    ...(query?.createdBefore ? { CreatedBefore: query.createdBefore } : {}),
  });
/** One page of failure modes (entries grouped by fingerprint). */
export const enumerateBlackBoxFingerprints = (query?: BlackBoxFingerprintQuery) =>
  post<EnumerationResult<BlackBoxFingerprintSummary>>(`${BLACKBOX}/fingerprints/enumerate`, {
    ...blackBoxPagedBody(query),
    ...(query?.vesselId ? { VesselId: query.vesselId } : {}),
    ...(query?.resolution ? { Resolution: query.resolution } : {}),
    ...(query?.tenantId ? { TenantId: query.tenantId } : {}),
    ...(query?.createdAfter ? { CreatedAfter: query.createdAfter } : {}),
    ...(query?.createdBefore ? { CreatedBefore: query.createdBefore } : {}),
  });

/** Occurrences of one failure mode per Armada version (to see whether a release fixed it). */
export const getBlackBoxFingerprintTrend = (fingerprint: string) =>
  get<BlackBoxFingerprintTrend>(`${BLACKBOX}/fingerprints/${encodeURIComponent(fingerprint)}/trend`);

/** Export an entry as a sanitized regression fixture (global admin). */
export const exportBlackBoxFixture = (entryId: string, name?: string) =>
  post<BlackBoxFixture>(`${BLACKBOX}/${encodeURIComponent(entryId)}/export-fixture`, name ? { Name: name } : {}, BLACKBOX_READ);

/** Set the resolution (Open, Resolved, Duplicate, WontFix or ArmadaFixed) with an optional note and link. */
export const resolveBlackBox = (entryId: string, request: BlackBoxResolveRequest) =>
  post<BlackBoxEntry>(`${BLACKBOX}/${encodeURIComponent(entryId)}/resolve`, { Resolution: request.resolution, Note: request.note ?? null, Link: request.link ?? null }, BLACKBOX_READ);
/** Pin an entry so garbage collection keeps it. */
export const pinBlackBox = (entryId: string) =>
  post<BlackBoxEntry>(`${BLACKBOX}/${encodeURIComponent(entryId)}/pin`, {}, BLACKBOX_READ);
/** Unpin an entry (its expiry is recomputed). */
export const unpinBlackBox = (entryId: string) =>
  post<BlackBoxEntry>(`${BLACKBOX}/${encodeURIComponent(entryId)}/unpin`, {}, BLACKBOX_READ);
/**
 * Run a recovery path recorded in the entry. dryRun defaults to true and returns the plan and a confirmToken (10
 * minutes); the real run needs that token, and acknowledgeDataLoss for a path marked dataLossRisk. 409
 * BlackBoxConfirmationRequired without them; 503 BlackBoxActionsUnavailable on a host that cannot run actions.
 */
export const runBlackBoxRecovery = (entryId: string, pathId: string, request?: BlackBoxRecoveryRunRequest) =>
  post<BlackBoxRecoveryRunResult>(`${BLACKBOX}/${encodeURIComponent(entryId)}/recovery/${encodeURIComponent(pathId)}`, {
    DryRun: request?.dryRun ?? true,
    ...(request?.confirmToken ? { ConfirmToken: request.confirmToken } : {}),
    ...(request?.acknowledgeDataLoss ? { AcknowledgeDataLoss: true } : {}),
    AuthorKind: request?.authorKind ?? 'Operator',
  }, BLACKBOX_READ);
/** Apply a configuration recommendation's recorded action, with the same dry-run and confirm-token flow. */
export const applyBlackBoxRecommendation = (entryId: string, recommendationId: string, request?: BlackBoxRecommendationApplyRequest) =>
  post<BlackBoxRecommendationApplyResult>(`${BLACKBOX}/${encodeURIComponent(entryId)}/recommendations/${encodeURIComponent(recommendationId)}/apply`, {
    DryRun: request?.dryRun ?? true,
    ...(request?.confirmToken ? { ConfirmToken: request.confirmToken } : {}),
    AuthorKind: request?.authorKind ?? 'Operator',
  }, BLACKBOX_READ);
/** Re-run enrichment and analysis (tenant admin). */
export const reanalyzeBlackBox = (entryId: string) =>
  post<BlackBoxEntry>(`${BLACKBOX}/${encodeURIComponent(entryId)}/reanalyze`, {}, BLACKBOX_READ);
/** Delete one entry (tenant admin). */
export const deleteBlackBox = (entryId: string) =>
  del<BlackBoxDeleteResult>(`${BLACKBOX}/${encodeURIComponent(entryId)}`);
/** Delete up to 1000 entries by id (tenant admin); more is 400 CollectionLimitExceeded before anything is deleted. */
export const deleteBlackBoxEntries = (ids: string[]) =>
  post<BlackBoxDeleteResult>(`${BLACKBOX}/delete`, { Ids: ids });

/** Purge entries by filter (global admin); dryRun defaults to true and only reports what would go. */
export const purgeBlackBox = (query?: BlackBoxPurgeQuery) =>
  post<BlackBoxPurgeResult>(`${BLACKBOX}/purge`, {
    ...blackBoxFilterBody(query),
    DryRun: query?.dryRun ?? true,
    ...(query?.includePinned ? { IncludePinned: true } : {}),
    ...(query?.maxEntries !== undefined ? { MaxEntries: query.maxEntries } : {}),
  });
/** Run one garbage collection pass now (global admin); dryRun lists the candidates. */
export const runBlackBoxGc = (dryRun = false) =>
  post<BlackBoxGcReport>(`${BLACKBOX}/gc`, { DryRun: dryRun });
/** Retention policy, counts, bytes and the last and next garbage collection run (tenant admin). */
export const getBlackBoxRetention = () => get<BlackBoxRetentionStatus>(`${BLACKBOX}/retention`);
/** Storage target, health, fallback and bytes per target (global admin). */
export const getBlackBoxStorage = () => get<BlackBoxStorageStatus>(`${BLACKBOX}/storage`);
/** Start a storage migration job (global admin); 400 for a bad target, 409 while one runs, 503 when the target is unhealthy. */
export const migrateBlackBoxStorage = (request: BlackBoxStorageMigrationRequest) =>
  post<BlackBoxStorageMigrationJob>(`${BLACKBOX}/storage/migrate`, {
    To: request.to,
    ...(request.from ? { From: request.from } : {}),
    ...(request.deleteSource ? { DeleteSource: true } : {}),
    ...(request.batchSize !== undefined ? { BatchSize: request.batchSize } : {}),
  });
/** One storage migration job (global admin). */
export const getBlackBoxStorageMigration = (migrationId: string) =>
  get<BlackBoxStorageMigrationJob>(`${BLACKBOX}/storage/migrations/${encodeURIComponent(migrationId)}`);
/** Capture entries for failed missions and voyages that have none (global admin). */
export const backfillBlackBox = (request?: BlackBoxBackfillRequest) =>
  post<BlackBoxBackfillResult>(`${BLACKBOX}/backfill`, {
    ...(request?.since ? { Since: request.since } : {}),
    ...(request?.maxSubjects !== undefined ? { MaxSubjects: request.maxSubjects } : {}),
  });

// Black box prevention, forensics and operations (BLACKBOX.md section 12; routes in BlackBoxInsightRoutes.cs). A vessel,
// voyage or entry the caller cannot see is 404. Collections are paged (pageSize 1 to 1000, default 10).

/** Preview a mission's overlaps with the vessel's active work and its brief lint findings; nothing is dispatched. */
export const previewMissionOverlap = (vesselId: string, request: MissionOverlapPreviewRequest) =>
  post<MissionOverlapPreview>(`/api/v1/vessels/${encodeURIComponent(vesselId)}/overlap-preview`, {
    Title: request.title ?? null,
    Description: request.description ?? null,
    ...(request.dependsOnMissionId ? { DependsOnMissionId: request.dependsOnMissionId } : {}),
    ...(request.excludeMissionId ? { ExcludeMissionId: request.excludeMissionId } : {}),
  });
/** One page of the vessel's soft directory reservations (active missions in conflict hotspot directories). */
export const listVesselReservations = (vesselId: string, params?: { pageNumber?: number; pageSize?: number }) =>
  get<VesselReservationPage>(`/api/v1/vessels/${encodeURIComponent(vesselId)}/reservations${blackBoxQuery({ pageNumber: params?.pageNumber, pageSize: params?.pageSize })}`);
/** Lint a mission brief (warnings only; dispatch never refuses a brief because of them). */
export const lintMissionBrief = (request: BriefLintRequest) =>
  post<BriefLintResult>('/api/v1/missions/lint-brief', {
    Title: request.title ?? null,
    Description: request.description ?? null,
    ...(request.vesselId ? { VesselId: request.vesselId } : {}),
    ...(request.dependsOnMissionId ? { DependsOnMissionId: request.dependsOnMissionId } : {}),
    ...(request.voyageId ? { VoyageId: request.voyageId } : {}),
  });
/** One page of the configuration recommendations that recur on a vessel (windowDays 1 to 365, default 30). */
export const getVesselBlackBoxDoctor = (vesselId: string, query?: BlackBoxDoctorQuery) =>
  get<BlackBoxDoctorPage>(`/api/v1/vessels/${encodeURIComponent(vesselId)}/blackbox-doctor${blackBoxQuery({ windowDays: query?.windowDays, pageNumber: query?.pageNumber, pageSize: query?.pageSize })}`);
/** One page of recovery path statistics (runs and success rates per rule and path), most runs first. */
export const getBlackBoxRecoveryStats = (query?: BlackBoxRecoveryStatsQuery) =>
  get<EnumerationResult<BlackBoxRecoveryStat>>(`${BLACKBOX}/recovery-stats${blackBoxQuery({ ruleId: query?.ruleId, vesselId: query?.vesselId, pageNumber: query?.pageNumber, pageSize: query?.pageSize })}`);
/** What changed between two entries (two attempts of one mission, or any two the caller can see). */
export const diffBlackBoxEntries = (entryId: string, otherEntryId: string) =>
  get<BlackBoxAttemptDiff>(`${BLACKBOX}/${encodeURIComponent(entryId)}/diff/${encodeURIComponent(otherEntryId)}`);
/** One page of the successful landings near a failure that changed the same files, most overlap first. */
export const listBlackBoxNearbySuccesses = (entryId: string, params?: { pageNumber?: number; pageSize?: number }) =>
  get<EnumerationResult<BlackBoxNearbySuccess>>(`${BLACKBOX}/${encodeURIComponent(entryId)}/nearby-successes${blackBoxQuery({ pageNumber: params?.pageNumber, pageSize: params?.pageSize })}`);
/** One page of the entry's combined timeline (entry, notes, mission, dock, merge entry, captain, Harbor); oldest first by default. */
export const getBlackBoxTimeline = (entryId: string, query?: BlackBoxTimelineQuery) =>
  get<BlackBoxTimelinePage>(`${BLACKBOX}/${encodeURIComponent(entryId)}/timeline${blackBoxQuery({ order: query?.order, pageNumber: query?.pageNumber, pageSize: query?.pageSize })}`);
/** The voyage's cascade graph: its missions and the DependsOn, CancelledBecause, BlockedBy and Sibling edges between them. */
export const getVoyageBlackBoxCascade = (voyageId: string) =>
  get<BlackBoxCascadeGraph>(`/api/v1/voyages/${encodeURIComponent(voyageId)}/blackbox/cascade`);

// ==================== Voyages ====================
export const listVoyages = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Voyage>>(`/api/v1/voyages${buildQuery(params)}`);
/** The voyage with its mission counts (VoyageProgress); the missions are paged with listMissions({ filters: { voyageId } }). */
export const getVoyage = (id: string) => get<VoyageProgress>(`/api/v1/voyages/${id}`);
/** The voyage with its mission counts, as raw JSON for the status view. */
export const getVoyageStatus = (id: string) => get<Record<string, unknown>>(`/api/v1/voyages/${encodeURIComponent(id)}`);
/** One page of voyages with progress; with no status filter, the active voyages (InProgress, then Open). */
export const enumerateVoyageProgress = (query?: { pageNumber?: number; pageSize?: number; status?: string; order?: string }) =>
  post<EnumerationResult<VoyageProgress>>('/api/v1/voyages/progress/enumerate', query ?? {});
export const createVoyage = (data: VoyageCreateRequest) => post<Voyage>('/api/v1/voyages', data);
export const cancelVoyage = (id: string) => del<VoyageCancelResult>(`/api/v1/voyages/${id}`);
export const purgeVoyage = (id: string) => del<void>(`/api/v1/voyages/${id}/purge`);

// ==================== Planning Sessions ====================
/** One page of the planning sessions visible to the caller, most recently updated first (TotalRecords counts them all). */
export const listPlanningSessions = (query?: PagedQuery) => post<EnumerationResult<PlanningSession>>('/api/v1/planning-sessions/enumerate', pagedBody(query));
/** One page of a planning session's transcript; newest first unless `order` is CreatedAscending. */
export const listPlanningSessionMessages = (id: string, query?: PagedQuery) =>
  post<EnumerationResult<PlanningSessionMessage>>(`/api/v1/planning-sessions/${encodeURIComponent(id)}/messages/enumerate`, pagedBody(query));
export const getPlanningSession = (id: string) => get<PlanningSessionDetail>(`/api/v1/planning-sessions/${id}`);
export const createPlanningSession = (data: PlanningSessionCreateRequest) =>
  post<PlanningSessionDetail>('/api/v1/planning-sessions', data, { timeout: PLANNING_CREATE_TIMEOUT_MS });
export const sendPlanningSessionMessage = (id: string, data: PlanningSessionMessageRequest) => post<PlanningSessionDetail>(`/api/v1/planning-sessions/${id}/messages`, data);
export const summarizePlanningSession = (id: string, data: PlanningSessionSummaryRequest) =>
  post<PlanningSessionSummaryResponse>(`/api/v1/planning-sessions/${id}/summarize`, data, { timeout: PLANNING_SUMMARIZE_TIMEOUT_MS });
export const dispatchPlanningSession = (id: string, data: PlanningSessionDispatchRequest) => post<Voyage>(`/api/v1/planning-sessions/${id}/dispatch`, data);
export const stopPlanningSession = (id: string) => post<PlanningSessionDetail>(`/api/v1/planning-sessions/${id}/stop`);
// Abort the in-flight planning turn without ending the session (Stop button parity with Ask Armada).
export const stopPlanningTurn = (id: string) => post<PlanningSessionDetail>(`/api/v1/planning-sessions/${id}/stop-turn`);
export const deletePlanningSession = (id: string) => del<void>(`/api/v1/planning-sessions/${id}`);

// ==================== Events ====================
export const listEvents = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<ArmadaEvent>>(`/api/v1/events${buildQuery(params)}`);
export const getEvent = (id: string) => get<ArmadaEvent>(`/api/v1/events/${id}`);
export const deleteEventsBatch = (ids: string[]) => post<BatchDeleteResult>('/api/v1/events/delete/multiple', { Ids: ids });

// ==================== Merge Queue ====================
export const listMergeQueue = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<MergeEntry>>(`/api/v1/merge-queue${buildQuery(params)}`);
export const getMergeEntry = (id: string) => get<MergeEntry>(`/api/v1/merge-queue/${id}`);
export const enqueueMerge = (data: Partial<MergeEntry>) => post<MergeEntry>('/api/v1/merge-queue', data);
export const deleteMergeEntry = (id: string) => del<void>(`/api/v1/merge-queue/${id}`);
export const processMergeEntry = (id: string) => post<void>(`/api/v1/merge-queue/${id}/process`);
export const processAllMergeQueue = () => post<void>('/api/v1/merge-queue/process');
/** Cancel a queued entry: the server's DELETE route deletes or cancels it. */
export const cancelMergeEntry = (id: string) => del<void>(`/api/v1/merge-queue/${encodeURIComponent(id)}`);

// ==================== Prompt Templates ====================
export const listPromptTemplates = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<PromptTemplate>>(`/api/v1/prompt-templates${buildQuery(params)}`);
export const getPromptTemplate = (name: string) => get<PromptTemplate>(`/api/v1/prompt-templates/${encodeURIComponent(name)}`);
export const createPromptTemplate = (data: { name: string; category: string; content: string; description?: string; active?: boolean; scope?: ScopeEnum }) =>
  post<PromptTemplate>('/api/v1/prompt-templates', data);
export const updatePromptTemplate = (name: string, data: { content: string; description?: string; scope?: ScopeEnum }) => put<PromptTemplate>(`/api/v1/prompt-templates/${encodeURIComponent(name)}`, data);
export const resetPromptTemplate = (name: string) => post<PromptTemplate>(`/api/v1/prompt-templates/${encodeURIComponent(name)}/reset`);

// ==================== Playbooks ====================
export const listPlaybooks = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Playbook>>(`/api/v1/playbooks${buildQuery(params)}`);
export const getPlaybook = (id: string) => get<Playbook>(`/api/v1/playbooks/${id}`);
export const createPlaybook = (data: Partial<Playbook>) => post<Playbook>('/api/v1/playbooks', data);
export const updatePlaybook = (id: string, data: Partial<Playbook>) => put<Playbook>(`/api/v1/playbooks/${id}`, data);
export const deletePlaybook = (id: string) => del<void>(`/api/v1/playbooks/${id}`);

// ==================== Workflow Profiles ====================
export const listWorkflowProfiles = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<WorkflowProfile>>(`/api/v1/workflow-profiles${buildQuery(params)}`);
export const getWorkflowProfile = (id: string) => get<WorkflowProfile>(`/api/v1/workflow-profiles/${encodeURIComponent(id)}`);
export const createWorkflowProfile = (data: Partial<WorkflowProfile>) => post<WorkflowProfile>('/api/v1/workflow-profiles', data);
export const updateWorkflowProfile = (id: string, data: Partial<WorkflowProfile>) => put<WorkflowProfile>(`/api/v1/workflow-profiles/${encodeURIComponent(id)}`, data);
export const deleteWorkflowProfile = (id: string) => del<void>(`/api/v1/workflow-profiles/${encodeURIComponent(id)}`);
export const enumerateWorkflowProfiles = (query?: { pageNumber?: number; pageSize?: number; scope?: string; fleetId?: string; vesselId?: string; search?: string; active?: boolean }) =>
  post<EnumerationResult<WorkflowProfile>>('/api/v1/workflow-profiles/enumerate', query || {});
export const deleteWorkflowProfilesBatch = (ids: string[]) => post<BatchDeleteResult>('/api/v1/workflow-profiles/delete/multiple', { Ids: ids });
export const validateWorkflowProfile = (data: Partial<WorkflowProfile>) => post<WorkflowProfileValidationResult>('/api/v1/workflow-profiles/validate', data);
export const previewWorkflowProfileForVessel = (vesselId: string, workflowProfileId?: string | null) =>
  get<WorkflowProfileResolutionPreviewResult>(`/api/v1/workflow-profiles/preview/vessels/${encodeURIComponent(vesselId)}${workflowProfileId ? `?workflowProfileId=${encodeURIComponent(workflowProfileId)}` : ''}`);
export const resolveWorkflowProfile = (vesselId: string, workflowProfileId?: string | null) =>
  get<WorkflowProfile>(`/api/v1/workflow-profiles/resolve/vessels/${encodeURIComponent(vesselId)}${workflowProfileId ? `?workflowProfileId=${encodeURIComponent(workflowProfileId)}` : ''}`);

// Project profiles
export const listProjectProfiles = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<ProjectProfile>>(`/api/v1/project-profiles${buildQuery(params)}`);
export const getProjectProfile = (id: string) => get<ProjectProfile>(`/api/v1/project-profiles/${encodeURIComponent(id)}`);
export const createProjectProfile = (data: Partial<ProjectProfile>) => post<ProjectProfile>('/api/v1/project-profiles', data);
export const updateProjectProfile = (id: string, data: Partial<ProjectProfile>) => put<ProjectProfile>(`/api/v1/project-profiles/${encodeURIComponent(id)}`, data);
export const deleteProjectProfile = (id: string) => del<void>(`/api/v1/project-profiles/${encodeURIComponent(id)}`);
export const validateProjectProfile = (data: Partial<ProjectProfile>) => post<ProjectProfileValidationResult>('/api/v1/project-profiles/validate', data);
export const resolveProjectProfileForVessel = (vesselId: string, projectProfileId?: string | null) =>
  get<ProjectProfileResolutionResult>(`/api/v1/project-profiles/resolve/vessels/${encodeURIComponent(vesselId)}${projectProfileId ? `?projectProfileId=${encodeURIComponent(projectProfileId)}` : ''}`);
export const previewPersonaPrompt = (profileId: string, persona: string) =>
  get<PersonaPromptPreview>(`/api/v1/project-profiles/${encodeURIComponent(profileId)}/persona-preview/${encodeURIComponent(persona)}`);

// Skills directory
export const listSkills = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Skill>>(`/api/v1/skills${buildQuery(params)}`);
export const getSkill = (id: string) => get<Skill>(`/api/v1/skills/${encodeURIComponent(id)}`);
export const createSkill = (data: Partial<Skill>) => post<Skill>('/api/v1/skills', data);
export const updateSkill = (id: string, data: Partial<Skill>) => put<Skill>(`/api/v1/skills/${encodeURIComponent(id)}`, data);
export const deleteSkill = (id: string) => del<void>(`/api/v1/skills/${encodeURIComponent(id)}`);

// Harbors (host runners)
export const listHarbors = () => enumerateFirstPage<Harbor>('/api/v1/harbors/enumerate');
/** One page of Harbors with true totals (default: the first page of 1000). */
export const enumerateHarbors = (params: { pageNumber?: number; pageSize?: number } = {}) =>
  enumeratePage<Harbor>('/api/v1/harbors/enumerate', params);
export const getHarbor = (id: string) => get<Harbor>(`/api/v1/harbors/${encodeURIComponent(id)}`);
export const createHarbor = (data: Partial<Harbor>) => post<Harbor>('/api/v1/harbors', data);
export const updateHarbor = (id: string, data: Partial<Harbor>) =>
  put<Harbor>(`/api/v1/harbors/${encodeURIComponent(id)}`, data);
export const deleteHarbor = (id: string) => del<void>(`/api/v1/harbors/${encodeURIComponent(id)}`);
export const enableHarbor = (id: string) => post<Harbor>(`/api/v1/harbors/${encodeURIComponent(id)}/enable`, {});
export const disableHarbor = (id: string) => post<Harbor>(`/api/v1/harbors/${encodeURIComponent(id)}/disable`, {});
export const getHarborMetrics = (id: string, range: HarborMetricsRange = '24h') =>
  get<HarborMetrics>(`/api/v1/harbors/${encodeURIComponent(id)}/metrics?range=${encodeURIComponent(range)}`);
/** One page of a Harbor's recorded jobs, newest first; openOnly lists only jobs that have not ended. */
export const listHarborJobs = (id: string, params: { openOnly?: boolean; pageNumber?: number; pageSize?: number } = {}) =>
  get<EnumerationResult<HarborJobRecord>>(`/api/v1/harbors/${encodeURIComponent(id)}/jobs${buildQuery({
    pageNumber: params.pageNumber, pageSize: params.pageSize, filters: params.openOnly ? { open: 'true' } : undefined })}`);
/** Ask a connected Harbor to stop one job (for an agent no mission or captain still points at). */
export const stopHarborJob = (id: string, jobId: string) =>
  post<HarborJobStopResult>(`/api/v1/harbors/${encodeURIComponent(id)}/jobs/${encodeURIComponent(jobId)}/stop`, {});

// Model endpoints (embedding/inference)
export const listModelEndpoints = () => enumerateFirstPage<ModelEndpoint>('/api/v1/model-endpoints/enumerate');
/** One page of model endpoints with true totals (default: the first page of 1000). */
export const enumerateModelEndpoints = (params: { pageNumber?: number; pageSize?: number } = {}) =>
  enumeratePage<ModelEndpoint>('/api/v1/model-endpoints/enumerate', params);
export const duplicateModelEndpoint = (sourceId: string, data: Partial<ModelEndpoint> & { apiKey?: string | null }) =>
  post<ModelEndpoint>(`/api/v1/model-endpoints/${encodeURIComponent(sourceId)}/duplicate`, data);
export const getModelEndpoint = (id: string) => get<ModelEndpoint>(`/api/v1/model-endpoints/${encodeURIComponent(id)}`);
export const createModelEndpoint = (data: Partial<ModelEndpoint> & { apiKey?: string | null }) =>
  post<ModelEndpoint>('/api/v1/model-endpoints', data);
export const updateModelEndpoint = (id: string, data: Partial<ModelEndpoint> & { apiKey?: string | null }) =>
  put<ModelEndpoint>(`/api/v1/model-endpoints/${encodeURIComponent(id)}`, data);
export const deleteModelEndpoint = (id: string) => del<void>(`/api/v1/model-endpoints/${encodeURIComponent(id)}`);
export const validateModelEndpoint = (id: string) =>
  post<ModelEndpointProbeResult>(`/api/v1/model-endpoints/${encodeURIComponent(id)}/validate`, {});
export const healthCheckModelEndpoints = () =>
  post<ModelEndpointHealthSweepResponse>('/api/v1/model-endpoints/health-check', {});

// ==================== Memories ====================
export const listMemories = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Memory>>(`/api/v1/memories${buildQuery(params)}`);
export const enumerateMemories = (query?: MemoryEnumerateQuery) =>
  post<EnumerationResult<Memory>>('/api/v1/memories/enumerate', query || {});
export const getMemory = (id: string) => get<Memory>(`/api/v1/memories/${encodeURIComponent(id)}`);
export const createMemory = (data: Partial<Memory>) => post<Memory>('/api/v1/memories', data);
export const updateMemory = (id: string, data: Partial<Memory>) =>
  put<Memory>(`/api/v1/memories/${encodeURIComponent(id)}`, data);
export const deleteMemory = (id: string) => del<void>(`/api/v1/memories/${encodeURIComponent(id)}`);

// Ask Armada
export const chatWithCaptain = (captainId: string, body: CaptainChatRequest, opts?: { signal?: AbortSignal }) =>
  // A chat turn launches the captain's CLI runtime headlessly, which can take minutes for slower
  // agents (e.g. Codex). Allow more than the default 30s so the reply is not aborted client-side;
  // keep it above the server-side chat timeout so the backend's clean message wins on timeout.
  // An optional signal lets the Stop button abort the request (the server then cancels the runtime).
  post<CaptainChatResponse>('/api/v1/captains/' + captainId + '/chat', body, { timeout: 330000, signal: opts?.signal });

// ==================== Ask Armada threads (docs/ASK_ARMADA_HOME_BASE.md) ====================
// Every Ask endpoint the dashboard uses lives here so integration fixes stay one-line changes. Request bodies
// are PascalCase for the C# server; responses are camelized by `request`.

const ASK = '/api/v1/ask';
const askThreadPath = (id: string) => `${ASK}/threads/${encodeURIComponent(id)}`;

/** Threads ordered pinned first, then most recent message; `search` is matched server-side. */
export const enumerateAskThreads = (query?: AskThreadEnumerateQuery) =>
  post<EnumerationResult<AskThread>>(`${ASK}/threads/enumerate`, {
    PageNumber: query?.pageNumber ?? 1,
    PageSize: query?.pageSize ?? 50,
    ...(query?.search ? { Search: query.search } : {}),
    IncludeArchived: query?.includeArchived ?? false,
  });

export const createAskThread = (data?: { title?: string; captainId?: string | null; autoApprove?: boolean }) =>
  post<AskThread>(`${ASK}/threads`, {
    ...(data?.title ? { Title: data.title } : {}),
    ...(data?.captainId ? { CaptainId: data.captainId } : {}),
    ...(data?.autoApprove !== undefined ? { AutoApprove: data.autoApprove } : {}),
  });

export const getAskThread = (id: string) => get<AskThreadDetail>(askThreadPath(id));

export const updateAskThread = (id: string, data: AskThreadUpdateRequest) => {
  const body: Record<string, unknown> = {};
  if (data.title !== undefined) body.Title = data.title;
  if (data.captainId !== undefined) body.CaptainId = data.captainId;
  if (data.autoApprove !== undefined) body.AutoApprove = data.autoApprove;
  if (data.pinned !== undefined) body.Pinned = data.pinned;
  if (data.archived !== undefined) body.Archived = data.archived;
  return put<AskThread>(askThreadPath(id), body);
};

export const deleteAskThread = (id: string) => del<void>(askThreadPath(id));

/** Bulk delete the caller's conversations; returns the deleted IDs and the skipped IDs with reasons. */
export const deleteAskThreads = (ids: string[]) =>
  post<AskThreadDeleteMultipleResult>('/api/v1/ask/threads/delete/multiple', { Ids: ids });

/** One page of messages, newest page when `beforeSequence` is omitted. */
export const enumerateAskMessages = (id: string, query?: { beforeSequence?: number | null; pageSize?: number }) =>
  post<AskMessagePage>(`${askThreadPath(id)}/messages/enumerate`, {
    ...(query?.beforeSequence != null ? { BeforeSequence: query.beforeSequence } : {}),
    PageSize: query?.pageSize ?? 30,
  });

/** Persist a user message and start a captain turn in the background (202). */
export const sendAskMessage = (id: string, content: string, showThinking?: boolean) =>
  post<AskSendMessageResult>(`${askThreadPath(id)}/messages`, { Content: content, ShowThinking: !!showThinking });

export const cancelAskTurn = (id: string) => post<void>(`${askThreadPath(id)}/cancel`, {});
export const summarizeAskThread = (id: string) => post<void>(`${askThreadPath(id)}/summarize`, {});
export const markAskThreadRead = (id: string) => post<void>(`${askThreadPath(id)}/read`, {});
/** Mark a conversation unread: the server sets its unread count to one when it is zero. */
export const markAskThreadUnread = (id: string) => post<AskThread>(`${askThreadPath(id)}/unread`, {});

/** Run a quick action; the submitted form is the confirmation, so the result is already executed. */
/** Run a quick action, or with `propose` create a Pending proposal the user approves (black box card buttons). */
export const runAskQuickAction = (id: string, toolName: string, args: Record<string, unknown>, options?: { propose?: boolean }) =>
  post<AskActionProposal>(`${askThreadPath(id)}/actions`, options?.propose ? { ToolName: toolName, Arguments: args, Propose: true } : { ToolName: toolName, Arguments: args }, { timeout: 120000 });

export const approveAskProposal = (id: string, proposalId: string) =>
  post<AskActionProposal>(`${askThreadPath(id)}/proposals/${encodeURIComponent(proposalId)}/approve`, {}, { timeout: 120000 });

export const rejectAskProposal = (id: string, proposalId: string) =>
  post<AskActionProposal>(`${askThreadPath(id)}/proposals/${encodeURIComponent(proposalId)}/reject`, {});

export const getAskWorkSnapshot = (id: string, workId: string) =>
  get<AskWorkSnapshot>(`${askThreadPath(id)}/work/${encodeURIComponent(workId)}`);

/** One page of the work a conversation tracks (each with a counts-only snapshot); `status` filters by state. */
export const enumerateAskWork = (id: string, query?: PagedQuery) =>
  post<EnumerationResult<AskTrackedWork>>(`${askThreadPath(id)}/work/enumerate`, pagedBody(query));
/** One page of the mission rows of tracked work (voyage or mission work). */
export const enumerateAskWorkMissions = (id: string, workId: string, query?: PagedQuery) =>
  post<EnumerationResult<AskMissionSnapshot>>(`${askThreadPath(id)}/work/${encodeURIComponent(workId)}/missions/enumerate`, pagedBody(query));
/** One page of the target rows of a tracked fleet action run. */
export const enumerateAskWorkTargets = (id: string, workId: string, query?: PagedQuery) =>
  post<EnumerationResult<AskTargetSnapshot>>(`${askThreadPath(id)}/work/${encodeURIComponent(workId)}/targets/enumerate`, pagedBody(query));
/** One page of a conversation's proposals; `status` filters (for example Pending). */
export const enumerateAskProposals = (id: string, query?: PagedQuery) =>
  post<EnumerationResult<AskActionProposal>>(`${askThreadPath(id)}/proposals/enumerate`, pagedBody(query));
/** One page of one message's tool calls. */
export const enumerateAskToolCalls = (id: string, messageId: string, query?: PagedQuery) =>
  post<EnumerationResult<AskToolCall>>(`${askThreadPath(id)}/messages/${encodeURIComponent(messageId)}/tool-calls/enumerate`, pagedBody(query));
/** One page of the tool calls the running turn has made so far (empty when no turn runs). */
export const enumerateAskTurnToolCalls = (id: string, query?: PagedQuery) =>
  post<EnumerationResult<AskToolCall>>(`${askThreadPath(id)}/turn/tool-calls/enumerate`, pagedBody(query));

/** The quick-action catalog; tolerates a bare array or an `{ QuickActions | Actions | Objects }` wrapper. */
export const getAskQuickActions = async (): Promise<AskQuickAction[]> => {
  const result = await get<unknown>(`${ASK}/quick-actions`);
  if (Array.isArray(result)) return result as AskQuickAction[];
  if (result && typeof result === 'object') {
    const wrapped = result as { quickActions?: AskQuickAction[]; actions?: AskQuickAction[]; objects?: AskQuickAction[] };
    return wrapped.quickActions ?? wrapped.actions ?? wrapped.objects ?? [];
  }
  return [];
};

// Needs-you inbox
export const getInbox = () => enumerateFirstPage<InboxItem>('/api/v1/inbox/enumerate');

// ==================== CLI tool permissions ====================
// Requests are CLI tool calls (shell, edit, fetch) a captain asked Armada to approve; rules allow or deny them by
// pattern. Bodies are PascalCase for the C# server; responses are camelized by `request`.

const CLI_PERMISSIONS = '/api/v1/cli-permissions';

function cliQuery(params: Record<string, string | number | null | undefined>): string {
  const parts = Object.entries(params)
    .filter(([, v]) => v !== undefined && v !== null && v !== '')
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`);
  return parts.length > 0 ? `?${parts.join('&')}` : '';
}

/**
 * The newest `limit` CLI permission requests visible to the caller (default 100, at most 1000): one page of the paged
 * enumerate route, never the whole list. Filter by status, mission, thread, captain, or vessel.
 */
export const listCliPermissionRequests = async (query?: CliPermissionRequestQuery): Promise<CliPermissionRequest[]> =>
  (await post<EnumerationResult<CliPermissionRequest>>(`${CLI_PERMISSIONS}/requests/enumerate`, {
    Status: query?.status ?? undefined, MissionId: query?.missionId ?? undefined, ThreadId: query?.threadId ?? undefined,
    CaptainId: query?.captainId ?? undefined, VesselId: query?.vesselId ?? undefined,
    PageNumber: query?.pageNumber ?? 1, PageSize: query?.limit ?? 100,
  }))?.objects ?? [];

export const getCliPermissionRequest = (id: string) =>
  get<CliPermissionRequest>(`${CLI_PERMISSIONS}/requests/${encodeURIComponent(id)}`);

/** Allow once, allow and remember (creates an allow rule; admins only), or deny a pending request. 409 when no longer pending. */
export const decideCliPermissionRequest = (id: string, body: CliPermissionDecisionRequest) => {
  const payload: Record<string, unknown> = { Decision: body.decision };
  if (body.message) payload.Message = body.message;
  if (body.rulePattern) payload.RulePattern = body.rulePattern;
  if (body.ruleScope) payload.RuleScope = body.ruleScope;
  return post<CliPermissionRequest>(`${CLI_PERMISSIONS}/requests/${encodeURIComponent(id)}/decide`, payload);
};

/** Rules visible to the caller (own tenant plus all-tenant rules). */
export const listCliPermissionRules = (query?: { scope?: CliPermissionRuleScope | null; vesselId?: string | null; captainId?: string | null }) =>
  enumerateFirstPage<CliPermissionRule>(`${CLI_PERMISSIONS}/rules/enumerate${cliQuery({ scope: query?.scope, vesselId: query?.vesselId, captainId: query?.captainId })}`);
/** One page of the rules visible to the caller with true totals (default: the first page of 1000). */
export const enumerateCliPermissionRules = (query?: { scope?: CliPermissionRuleScope | null; vesselId?: string | null; captainId?: string | null }, params: { pageNumber?: number; pageSize?: number } = {}) =>
  enumeratePage<CliPermissionRule>(`${CLI_PERMISSIONS}/rules/enumerate${cliQuery({ scope: query?.scope, vesselId: query?.vesselId, captainId: query?.captainId })}`, params);

export const getCliPermissionRule = (id: string) =>
  get<CliPermissionRule>(`${CLI_PERMISSIONS}/rules/${encodeURIComponent(id)}`);

export const createCliPermissionRule = (rule: CliPermissionRuleCreateRequest) => {
  const payload: Record<string, unknown> = { Pattern: rule.pattern, Action: rule.action, Scope: rule.scope };
  if (rule.vesselId) payload.VesselId = rule.vesselId;
  if (rule.captainId) payload.CaptainId = rule.captainId;
  if (rule.tenantId) payload.TenantId = rule.tenantId;
  if (rule.description) payload.Description = rule.description;
  return post<CliPermissionRule>(`${CLI_PERMISSIONS}/rules`, payload);
};

/** Change a rule's pattern, action, or description (scope and target are fixed). */
export const updateCliPermissionRule = (id: string, data: { pattern?: string; action?: CliPermissionRule['action']; description?: string | null }) => {
  const payload: Record<string, unknown> = {};
  if (data.pattern !== undefined) payload.Pattern = data.pattern;
  if (data.action !== undefined) payload.Action = data.action;
  if (data.description !== undefined) payload.Description = data.description;
  return put<CliPermissionRule>(`${CLI_PERMISSIONS}/rules/${encodeURIComponent(id)}`, payload);
};

export const deleteCliPermissionRule = (id: string) =>
  del<void>(`${CLI_PERMISSIONS}/rules/${encodeURIComponent(id)}`);

/** Set (or clear with null) a captain's CLI tool permission policy. Global admin or tenant admin of the captain's tenant. */
export const setCaptainCliPermissionPolicy = (captainId: string, policy: CliPermissionPolicy | null) =>
  put<Captain>(`/api/v1/captains/${encodeURIComponent(captainId)}/cli-permission-policy`, { Policy: policy });

/** Set (or clear with null) an Ask thread's CLI tool permission override. Owner only; Bypass requires an admin. */
export const setAskThreadCliPermissionPolicy = (threadId: string, policy: CliPermissionPolicy | null) =>
  put<AskThread>(`${askThreadPath(threadId)}/cli-permission-policy`, { Policy: policy });

// ==================== Stage outcomes and failure-cause rules ====================
// One outcome per stage attempt with its suspected cause; rules assign the cause. Bodies are PascalCase for the C#
// server; responses are camelized by `request`.

const STAGE_OUTCOMES = '/api/v1/stage-outcomes';
const FAILURE_CAUSE_RULES = '/api/v1/failure-cause-rules';

function stageOutcomeFilterBody(query?: StageOutcomeQuery): Record<string, unknown> {
  const body: Record<string, unknown> = {};
  if (!query) return body;
  if (query.tenantId) body.TenantId = query.tenantId;
  if (query.missionId) body.MissionId = query.missionId;
  if (query.voyageId) body.VoyageId = query.voyageId;
  if (query.vesselId) body.VesselId = query.vesselId;
  if (query.captainId) body.CaptainId = query.captainId;
  if (query.persona) body.Persona = query.persona;
  if (query.model) body.Model = query.model;
  if (query.suspectedCause) body.SuspectedCause = query.suspectedCause;
  if (query.causeSource) body.CauseSource = query.causeSource;
  if (query.disposition) body.Disposition = query.disposition;
  if (query.isEvaluation !== undefined && query.isEvaluation !== null) body.IsEvaluation = query.isEvaluation;
  if (query.fromUtc) body.FromUtc = query.fromUtc;
  if (query.toUtc) body.ToUtc = query.toUtc;
  return body;
}

function failureCauseRuleBody(rule: FailureCauseRuleUpsertRequest): Record<string, unknown> {
  const match: Partial<FailureCauseRuleMatch> = rule.match || {};
  const payload: Record<string, unknown> = {
    Name: rule.name,
    Description: rule.description ?? null,
    Cause: rule.cause,
    Match: {
      Personas: match.personas ?? [],
      Runtimes: match.runtimes ?? [],
      FailureKinds: match.failureKinds ?? [],
      RuntimeFailureKinds: match.runtimeFailureKinds ?? [],
      StageCheckStatuses: match.stageCheckStatuses ?? [],
      StageCheckReasons: match.stageCheckReasons ?? [],
      Dispositions: match.dispositions ?? [],
      TextPattern: match.textPattern ?? null,
    },
  };
  if (rule.priority !== undefined) payload.Priority = rule.priority;
  if (rule.enabled !== undefined) payload.Enabled = rule.enabled;
  if (rule.tenantId) payload.TenantId = rule.tenantId;
  return payload;
}

/** One page of the stage outcomes visible to the caller (global admins every tenant, others their own), newest first. */
export const listStageOutcomes = (query?: StageOutcomeQuery) =>
  get<EnumerationResult<StageOutcome>>(`${STAGE_OUTCOMES}${cliQuery({
    tenantId: query?.tenantId, missionId: query?.missionId, voyageId: query?.voyageId, vesselId: query?.vesselId,
    captainId: query?.captainId, persona: query?.persona, model: query?.model, suspectedCause: query?.suspectedCause,
    causeSource: query?.causeSource, disposition: query?.disposition,
    isEvaluation: query?.isEvaluation === undefined || query?.isEvaluation === null ? undefined : String(query.isEvaluation),
    fromUtc: query?.fromUtc, toUtc: query?.toUtc, pageNumber: query?.pageNumber, pageSize: query?.pageSize,
  })}`);

export const getStageOutcome = (id: string) =>
  get<StageOutcome>(`${STAGE_OUTCOMES}/${encodeURIComponent(id)}`);

/** Classify an outcome by hand; a null cause hands it back to the rules. 400 for cause None. */
export const classifyStageOutcome = (id: string, cause: FailureCause | null, note?: string | null) =>
  put<StageOutcome>(`${STAGE_OUTCOMES}/${encodeURIComponent(id)}/cause`, { Cause: cause, Note: note ?? null });

/** Re-apply the rules to every outcome matching the filter (manual causes are kept). Global admin or tenant admin. */
export const reclassifyStageOutcomes = (query?: StageOutcomeQuery) =>
  post<StageOutcomeReclassifyResult>(`${STAGE_OUTCOMES}/reclassify`, stageOutcomeFilterBody(query));

/** An unsaved rule drafted from the outcome's cause and signals; edit it and pass it to createFailureCauseRule. */
export const draftFailureCauseRule = (outcomeId: string) =>
  post<FailureCauseRule>(`${STAGE_OUTCOMES}/${encodeURIComponent(outcomeId)}/rule`);

/** One page of the rules visible to the caller (own tenant plus all-tenant rules) in evaluation order. */
export const listFailureCauseRules = (params?: { pageNumber?: number; pageSize?: number }) =>
  get<EnumerationResult<FailureCauseRule>>(`${FAILURE_CAUSE_RULES}${buildQuery(params)}`);

export const getFailureCauseRule = (id: string) =>
  get<FailureCauseRule>(`${FAILURE_CAUSE_RULES}/${encodeURIComponent(id)}`);

/** Create a rule (global admins may set tenantId or omit it for every tenant). 400 when invalid; 409 for a duplicate name. */
export const createFailureCauseRule = (rule: FailureCauseRuleUpsertRequest) =>
  post<FailureCauseRule>(`${FAILURE_CAUSE_RULES}`, failureCauseRuleBody(rule));

/** Replace a rule's fields. Rules for every tenant and built-in rules need a global admin (403). */
export const updateFailureCauseRule = (id: string, rule: FailureCauseRuleUpsertRequest) =>
  put<FailureCauseRule>(`${FAILURE_CAUSE_RULES}/${encodeURIComponent(id)}`, failureCauseRuleBody(rule));

/** Delete a rule. A deleted built-in rule is restored when the Admiral next starts; disable it to keep it off. */
export const deleteFailureCauseRule = (id: string) =>
  del<void>(`${FAILURE_CAUSE_RULES}/${encodeURIComponent(id)}`);

// ==================== Model Probes ====================
// Evaluate a captain or model endpoint with the mechanical probes (Quick depth; Full is rejected until Phase 5). Runs
// are queued and finish in the background; poll getModelProbeRun. Nothing changes routing or captain settings.

const MODEL_PROBE_RUNS = '/api/v1/model-probe-runs';

/** Queue a probe run for a captain through its own runtime. 201 with the queued run; 400 for depth Full. */
export const evaluateCaptain = (captainId: string, depth: ModelProbeDepth = 'Quick') =>
  post<ModelProbeRun>(`/api/v1/captains/${encodeURIComponent(captainId)}/evaluate`, { Depth: depth });

/** Queue a probe run for a model endpoint. 201 with the queued run; 400 for depth Full. */
export const evaluateModelEndpoint = (endpointId: string, depth: ModelProbeDepth = 'Quick') =>
  post<ModelProbeRun>(`/api/v1/model-endpoints/${encodeURIComponent(endpointId)}/evaluate`, { Depth: depth });

/** One run with its per-probe results. */
export const getModelProbeRun = (id: string) =>
  get<ModelProbeRun>(`${MODEL_PROBE_RUNS}/${encodeURIComponent(id)}`);

/** One page of the runs visible to the caller, newest first; list rows carry no results. */
export const listModelProbeRuns = (query?: ModelProbeRunQuery) =>
  get<EnumerationResult<ModelProbeRun>>(`${MODEL_PROBE_RUNS}${cliQuery({
    captainId: query?.captainId, modelEndpointId: query?.modelEndpointId, status: query?.status,
    pageNumber: query?.pageNumber, pageSize: query?.pageSize,
  })}`);

// Workspace terminal
export const execWorkspaceCommand = (vesselId: string, command: string, timeoutSeconds?: number) =>
  post<WorkspaceExecResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/exec`, { command, timeoutSeconds });

// Workspace diff (review)
export const getWorkspaceDiff = (vesselId: string, path?: string) =>
  get<WorkspaceDiffResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/diff${path ? `?path=${encodeWorkspaceQueryPath(path)}` : ''}`);

// ==================== Environments ====================
export const listEnvironments = (params?: DeploymentEnvironmentQuery) =>
  get<EnumerationResult<DeploymentEnvironment>>(`/api/v1/environments${buildEnvironmentQuery(params)}`);
export const enumerateEnvironments = (query?: DeploymentEnvironmentQuery) =>
  post<EnumerationResult<DeploymentEnvironment>>('/api/v1/environments/enumerate', query || {});
export const getEnvironment = (id: string) => get<DeploymentEnvironment>(`/api/v1/environments/${encodeURIComponent(id)}`);
export const createEnvironment = (data: DeploymentEnvironmentUpsertRequest) => post<DeploymentEnvironment>('/api/v1/environments', data);
export const updateEnvironment = (id: string, data: DeploymentEnvironmentUpsertRequest) => put<DeploymentEnvironment>(`/api/v1/environments/${encodeURIComponent(id)}`, data);
export const deleteEnvironment = (id: string) => del<void>(`/api/v1/environments/${encodeURIComponent(id)}`);

// ==================== Deployments ====================
export const listDeployments = (params?: DeploymentQuery) =>
  get<EnumerationResult<Deployment>>(`/api/v1/deployments${buildDeploymentQuery(params)}`);
export const enumerateDeployments = (query?: DeploymentQuery) =>
  post<EnumerationResult<Deployment>>('/api/v1/deployments/enumerate', query || {});
export const getDeployment = (id: string) => get<Deployment>(`/api/v1/deployments/${encodeURIComponent(id)}`);
export const createDeployment = (data: DeploymentUpsertRequest) => post<Deployment>('/api/v1/deployments', data);
export const updateDeployment = (id: string, data: DeploymentUpsertRequest) => put<Deployment>(`/api/v1/deployments/${encodeURIComponent(id)}`, data);
export const syncGitHubActions = (data: GitHubActionsSyncRequest) => post<GitHubActionsSyncResult>('/api/v1/check-runs/sync/github-actions', data);
export const approveDeployment = (id: string, comment?: string | null) => post<Deployment>(`/api/v1/deployments/${encodeURIComponent(id)}/approve`, comment ? { comment } : {});
export const denyDeployment = (id: string, comment?: string | null) => post<Deployment>(`/api/v1/deployments/${encodeURIComponent(id)}/deny`, comment ? { comment } : {});
export const verifyDeployment = (id: string) => post<Deployment>(`/api/v1/deployments/${encodeURIComponent(id)}/verify`, {});
export const rollbackDeployment = (id: string) => post<Deployment>(`/api/v1/deployments/${encodeURIComponent(id)}/rollback`, {});
export const deleteDeployment = (id: string) => del<void>(`/api/v1/deployments/${encodeURIComponent(id)}`);

// ==================== Incidents ====================
export const listIncidents = (params?: IncidentQuery) =>
  get<EnumerationResult<Incident>>(`/api/v1/incidents${buildIncidentQuery(params)}`);
export const enumerateIncidents = (query?: IncidentQuery) =>
  post<EnumerationResult<Incident>>('/api/v1/incidents/enumerate', query || {});
export const getIncident = (id: string) => get<Incident>(`/api/v1/incidents/${encodeURIComponent(id)}`);
export const createIncident = (data: IncidentUpsertRequest) => post<Incident>('/api/v1/incidents', data);
export const updateIncident = (id: string, data: IncidentUpsertRequest) => put<Incident>(`/api/v1/incidents/${encodeURIComponent(id)}`, data);
export const deleteIncident = (id: string) => del<void>(`/api/v1/incidents/${encodeURIComponent(id)}`);

// ==================== Runbooks ====================
export const listRunbooks = (params?: RunbookQuery) =>
  get<EnumerationResult<Runbook>>(`/api/v1/runbooks${buildRunbookQuery(params)}`);
export const enumerateRunbooks = (query?: RunbookQuery) =>
  post<EnumerationResult<Runbook>>('/api/v1/runbooks/enumerate', query || {});
export const getRunbook = (id: string) => get<Runbook>(`/api/v1/runbooks/${encodeURIComponent(id)}`);
export const createRunbook = (data: RunbookUpsertRequest) => post<Runbook>('/api/v1/runbooks', data);
export const updateRunbook = (id: string, data: RunbookUpsertRequest) => put<Runbook>(`/api/v1/runbooks/${encodeURIComponent(id)}`, data);
export const deleteRunbook = (id: string) => del<void>(`/api/v1/runbooks/${encodeURIComponent(id)}`);
export const listRunbookExecutions = (params?: RunbookExecutionQuery) =>
  get<EnumerationResult<RunbookExecution>>(`/api/v1/runbook-executions${buildRunbookExecutionQuery(params)}`);
export const enumerateRunbookExecutions = (query?: RunbookExecutionQuery) =>
  post<EnumerationResult<RunbookExecution>>('/api/v1/runbook-executions/enumerate', query || {});
export const getRunbookExecution = (id: string) => get<RunbookExecution>(`/api/v1/runbook-executions/${encodeURIComponent(id)}`);
export const startRunbookExecution = (runbookId: string, data: RunbookExecutionStartRequest) =>
  post<RunbookExecution>(`/api/v1/runbooks/${encodeURIComponent(runbookId)}/executions`, data);
export const updateRunbookExecution = (id: string, data: RunbookExecutionUpdateRequest) =>
  put<RunbookExecution>(`/api/v1/runbook-executions/${encodeURIComponent(id)}`, data);
export const deleteRunbookExecution = (id: string) => del<void>(`/api/v1/runbook-executions/${encodeURIComponent(id)}`);

// ==================== Releases ====================
export const listReleases = (params?: ReleaseQuery) =>
  get<EnumerationResult<Release>>(`/api/v1/releases${buildReleaseQuery(params)}`);
export const enumerateReleases = (query?: ReleaseQuery) =>
  post<EnumerationResult<Release>>('/api/v1/releases/enumerate', query || {});
export const getRelease = (id: string) => get<Release>(`/api/v1/releases/${encodeURIComponent(id)}`);
export const createRelease = (data: ReleaseUpsertRequest) => post<Release>('/api/v1/releases', data);
export const updateRelease = (id: string, data: ReleaseUpsertRequest) => put<Release>(`/api/v1/releases/${encodeURIComponent(id)}`, data);
export const refreshRelease = (id: string) => post<Release>(`/api/v1/releases/${encodeURIComponent(id)}/refresh`, {});
export const deleteRelease = (id: string) => del<void>(`/api/v1/releases/${encodeURIComponent(id)}`);
/** One page of a release's GitHub pull requests (only that page is read from GitHub), with the total. */
export const getReleaseGitHubPullRequests = (id: string, page?: { pageNumber?: number; pageSize?: number }) =>
  post<EnumerationResult<GitHubPullRequestDetail>>(`/api/v1/releases/${encodeURIComponent(id)}/github/pull-requests/enumerate`, { PageNumber: page?.pageNumber ?? 1, PageSize: page?.pageSize ?? 25 });

// ==================== Check Runs ====================
export const listCheckRuns = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<CheckRun>>(`/api/v1/check-runs${buildQuery(params)}`);
export const getCheckRun = (id: string) => get<CheckRun>(`/api/v1/check-runs/${encodeURIComponent(id)}`);
export const runCheck = (data: CheckRunRequest) => post<CheckRun>('/api/v1/check-runs', data, { timeout: 35 * 60 * 1000 });
export const importCheckRun = (data: CheckRunImportRequest) => post<CheckRun>('/api/v1/check-runs/import', data);
export const retryCheckRun = (id: string) => post<CheckRun>(`/api/v1/check-runs/${encodeURIComponent(id)}/retry`);
export const deleteCheckRun = (id: string) => del<void>(`/api/v1/check-runs/${encodeURIComponent(id)}`);

// ==================== Personas ====================
export const listPersonas = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Persona>>(`/api/v1/personas${buildQuery(params)}`);
export const getPersona = (name: string) => get<Persona>(`/api/v1/personas/${encodeURIComponent(name)}`);
export const createPersona = (data: Partial<Persona>) => post<Persona>('/api/v1/personas', data);
export const updatePersona = (name: string, data: Partial<Persona>) => put<Persona>(`/api/v1/personas/${encodeURIComponent(name)}`, data);
export const deletePersona = (name: string) => del<void>(`/api/v1/personas/${encodeURIComponent(name)}`);

// ==================== Pipelines ====================
export const listPipelines = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Pipeline>>(`/api/v1/pipelines${buildQuery(params)}`);
export const getPipeline = (name: string) => get<Pipeline>(`/api/v1/pipelines/${encodeURIComponent(name)}`);
export const createPipeline = (data: Partial<Pipeline>) => post<Pipeline>('/api/v1/pipelines', data);
export const updatePipeline = (name: string, data: Partial<Pipeline>) => put<Pipeline>(`/api/v1/pipelines/${encodeURIComponent(name)}`, data);
export const deletePipeline = (name: string) => del<void>(`/api/v1/pipelines/${encodeURIComponent(name)}`);

// ==================== Docks ====================
export const listDocks = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Dock>>(`/api/v1/docks${buildQuery(params)}`);
export const getDock = (id: string) => get<Dock>(`/api/v1/docks/${id}`);
export const deleteDock = (id: string) => del<void>(`/api/v1/docks/${id}`);

// ==================== Signals ====================
export const listSignals = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Signal>>(`/api/v1/signals${buildQuery(params)}`);
export const getSignal = (id: string) => get<Signal>(`/api/v1/signals/${id}`);
export const sendSignal = (data: SendSignalRequest) => post<Signal>('/api/v1/signals', data);
export const markSignalRead = (id: string) => put<void>(`/api/v1/signals/${id}/read`);
export const deleteSignalsBatch = (ids: string[]) => post<BatchDeleteResult>('/api/v1/signals/delete/multiple', { Ids: ids });

// ==================== Status / Health ====================
export const getStatus = () => get<StatusSnapshot>('/api/v1/status');
export const getHealth = () => get<Record<string, unknown>>('/api/v1/status/health');
export const getDoctor = () => get<DoctorCheck[]>('/api/v1/doctor');

// ==================== Settings ====================
export const getSettings = () => get<SettingsData>('/api/v1/settings');
export const updateSettings = (data: SettingsData) => put<SettingsData>('/api/v1/settings', data);

// ==================== Server ====================
export const stopServer = () => post<void>('/api/v1/server/stop');

export const restartServer = () => post<void>('/api/v1/server/restart');
export const resetServer = () => post<void>('/api/v1/server/reset');

/** Wire values of the rebuild status. */
export type RebuildState = 'Building' | 'CuttingOver' | 'Succeeded' | 'Failed' | 'RolledBack';

export interface RebuildStatus {
  rebuildId?: string;
  slot?: string | null;
  previousSlot?: string | null;
  sha?: string | null;
  ref?: string | null;
  backupPath?: string | null;
  /** Null (or absent) when no rebuild has run. */
  status?: RebuildState | null;
  startedUtc?: string;
  completedUtc?: string | null;
  error?: string | null;
  log?: string;
}

/** Kick off a server rebuild. Body keys are PascalCase for the C# server. */
export const rebuildServer = (body?: { Ref?: string; SkipDashboard?: boolean; RollbackTimeoutSeconds?: number }) =>
  post<RebuildStatus>('/api/v1/server/rebuild', body ?? {});

/** Poll the latest rebuild status and its accumulated build log. */
export const getRebuildStatus = () => get<RebuildStatus>('/api/v1/server/rebuild/status');

/** Roll back the last rebuild to the previous slot (restores the pre-rebuild DB backup if the schema changed). */
export const rollbackServer = () => post<RebuildStatus>('/api/v1/server/rollback');

// ==================== Backup / Restore ====================
/**
 * Download a backup ZIP and hand it to the host's platform adapter (a browser download in the dashboard, the share
 * sheet on mobile). The server endpoint is GET and returns binary.
 */
export async function downloadBackup(): Promise<void> {
  const own: Record<string, string> = {};
  if (authToken) own['X-Token'] = authToken;
  const res = await fetchOrNetworkError(`${BASE_URL}/api/v1/backup`, { method: 'GET', headers: withExtraHeaders(own) });
  if (res.status === 401) { onUnauthorized?.(); throw unauthorizedError(); }
  if (!res.ok) throw new Error(`Backup failed: ${res.status}`);
  const blob = await res.blob();
  const disposition = res.headers.get('Content-Disposition') || '';
  const match = disposition.match(/filename="?([^"]+)"?/);
  const filename = match ? match[1] : `armada-backup-${new Date().toISOString().slice(0, 10)}.zip`;
  if (!platformAdapter) throw new Error('No platform adapter is configured to save downloaded files.');
  await platformAdapter.saveFile({ filename, contentType: res.headers.get('Content-Type') || 'application/zip', body: blob });
}

/** Upload a backup ZIP file to restore. Sends raw bytes with filename header. */
export async function restoreBackup(file: UploadFile): Promise<Record<string, unknown>> {
  const own: Record<string, string> = { 'Content-Type': 'application/octet-stream' };
  if (authToken) own['X-Token'] = authToken;
  own['X-Original-Filename'] = file.name;
  const bytes = await file.arrayBuffer();
  const res = await fetchOrNetworkError(`${BASE_URL}/api/v1/restore`, { method: 'POST', headers: withExtraHeaders(own), body: bytes });
  if (res.status === 401) { onUnauthorized?.(); throw unauthorizedError(); }
  if (!res.ok) { const text = await res.text(); throw new Error(text || `Restore failed: ${res.status}`); }
  const json = await res.json();
  return camelizeKeys(json) as Record<string, unknown>;
}

// ==================== Generic entity lookup ====================
export function getEntity(type: string, id: string): Promise<unknown> {
  const typeMap: Record<string, string> = {
    fleets: 'fleets',
    vessels: 'vessels',
    captains: 'captains',
    missions: 'missions',
    voyages: 'voyages',
    signals: 'signals',
    events: 'events',
    docks: 'docks',
    'merge-queue': 'merge-queue',
    playbooks: 'playbooks',
    objectives: 'objectives',
    releases: 'releases',
    environments: 'environments',
    deployments: 'deployments',
  };
  const endpoint = typeMap[type];
  if (!endpoint) throw new Error(`Unknown entity type: ${type}`);
  return get<unknown>(`/api/v1/${endpoint}/${id}`);
}

// ---------------------------------------------------------------------------
// Vessel Import
// ---------------------------------------------------------------------------

/** Encode a path as base64url UTF-8, the format the browse endpoint expects. */
export function encodeBrowsePath(path: string): string {
  const bytes = new TextEncoder().encode(path);
  let binary = '';
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/**
 * List one page of browsable subdirectories of a directory on the Admiral host; omit `path` to list the allowed roots.
 * The server default page size is 10; pass up to 1000 and follow totalPages for the rest.
 */
export const browseVesselImport = (path?: string | null, page?: { pageNumber?: number; pageSize?: number }) => {
  const parts: string[] = [];
  if (path) parts.push(`path=${encodeBrowsePath(path)}`);
  if (page?.pageNumber) parts.push(`pageNumber=${page.pageNumber}`);
  if (page?.pageSize) parts.push(`pageSize=${page.pageSize}`);
  return get<VesselBrowseResult>(`/api/v1/vessels/import/browse${parts.length ? `?${parts.join('&')}` : ''}`);
};
/** Every browsable entry of one directory, read in explicit pages of 1000 (at most 100 pages); `truncated` is kept. */
export async function browseAllVesselImport(path?: string | null): Promise<VesselBrowseResult> {
  const first = await browseVesselImport(path, { pageNumber: 1, pageSize: MAX_PAGE_SIZE });
  const entries = [...(first.entries ?? [])];
  const pages = Math.min(first.totalPages ?? 1, 100);
  for (let page = 2; page <= pages; page++) {
    const next = await browseVesselImport(path, { pageNumber: page, pageSize: MAX_PAGE_SIZE });
    if (!next.entries?.length) break;
    entries.push(...next.entries);
  }
  return { ...first, entries, truncated: Boolean(first.truncated) || (first.totalPages ?? 1) > pages };
}

/** Scan directories and roots for git repositories and persist an import batch (creates no vessels). */
export const discoverVesselImport = (data: VesselDiscoveryRequest) =>
  post<VesselImportDiscoverResponse>('/api/v1/vessels/import/discover', data, { timeout: 5 * 60 * 1000 });

/** Create vessels for the selected candidates of a discovered batch (200 inline, 202 background). */
export const importVessels = (data: VesselImportRequest) =>
  post<VesselImportResponse>('/api/v1/vessels/import', data, { timeout: 5 * 60 * 1000 });

/** Paged import history for the caller's tenant, newest first by default. */
export const enumerateVesselImportBatches = (query?: { pageNumber?: number; pageSize?: number; status?: string; order?: string }) =>
  post<EnumerationResult<VesselImportBatch>>('/api/v1/vessels/import/batches/enumerate', {
    PageNumber: query?.pageNumber ?? 1,
    PageSize: query?.pageSize ?? 25,
    ...(query?.status ? { Status: query.status } : {}),
    ...(query?.order ? { Order: query.order } : {}),
  });

/** A batch with all of its items, ordered by path. */
export const getVesselImportBatch = (id: string) =>
  get<VesselImportBatchDetail>(`/api/v1/vessels/import/batches/${encodeURIComponent(id)}`);

/** The default fleet categorization instructions (the import.fleet_categorization prompt template). */
export const getFleetCategorizationDefaultPrompt = () =>
  get<FleetCategorizationDefaultPrompt>('/api/v1/vessels/import/categorization/default-prompt');

/** Run or retry fleet categorization for an imported batch; omitted fields reuse the previous run. */
export const categorizeVesselImport = (id: string, data?: Partial<VesselImportCategorizationRequest>) =>
  post<VesselImportBatch>(`/api/v1/vessels/import/batches/${encodeURIComponent(id)}/categorize`, data ?? {});

/** Apply (possibly edited) fleet recommendations: reuse fleets by name or create them, then assign vessels. */
export const applyFleetRecommendations = (id: string, data: FleetRecommendationApplyRequest) =>
  post<FleetRecommendationApplyResult>(`/api/v1/vessels/import/batches/${encodeURIComponent(id)}/fleet-recommendations/apply`, data);

// ---------------------------------------------------------------------------
// Fleet Actions
// ---------------------------------------------------------------------------

export interface FleetActionEnumerateQuery {
  pageNumber?: number;
  pageSize?: number;
  includeInactive?: boolean;
  order?: 'CreatedAscending' | 'CreatedDescending';
}

export const enumerateFleetActions = (query?: FleetActionEnumerateQuery) =>
  post<EnumerationResult<FleetAction>>('/api/v1/fleet-actions/enumerate', {
    PageNumber: query?.pageNumber ?? 1,
    PageSize: query?.pageSize ?? 25,
    IncludeInactive: query?.includeInactive ?? false,
    ...(query?.order ? { Order: query.order } : {}),
  });

export const getFleetAction = (id: string) => get<FleetAction>(`/api/v1/fleet-actions/${encodeURIComponent(id)}`);
export const createFleetAction = (data: FleetActionUpsertRequest) => post<FleetAction>('/api/v1/fleet-actions', data);
export const updateFleetAction = (id: string, data: FleetActionUpsertRequest) =>
  put<FleetAction>(`/api/v1/fleet-actions/${encodeURIComponent(id)}`, data);
export const deleteFleetAction = (id: string) => del<void>(`/api/v1/fleet-actions/${encodeURIComponent(id)}`);

/** Start a run of a saved action over the given vessels. */
export const runFleetAction = (id: string, data: FleetActionRunRequest) =>
  post<FleetActionRunStartResult>(`/api/v1/fleet-actions/${encodeURIComponent(id)}/run`, data);

/** Start an ad hoc run with an inline `Definition`; the run's action id is null. */
export const runAdHocFleetAction = (data: FleetActionRunRequest) =>
  post<FleetActionRunStartResult>('/api/v1/fleet-actions/run', data);

export interface FleetActionRunEnumerateQuery {
  pageNumber?: number;
  pageSize?: number;
  status?: FleetActionRunStatus | '';
  order?: 'CreatedAscending' | 'CreatedDescending';
}

export const enumerateFleetActionRuns = (query?: FleetActionRunEnumerateQuery) =>
  post<EnumerationResult<FleetActionRun>>('/api/v1/fleet-action-runs/enumerate', {
    PageNumber: query?.pageNumber ?? 1,
    PageSize: query?.pageSize ?? 25,
    ...(query?.status ? { Status: query.status } : {}),
    ...(query?.order ? { Order: query.order } : {}),
  });

/** Run plus summaries of every target (no output text). */
export const getFleetActionRun = (id: string) => get<FleetActionRunDetail>(`/api/v1/fleet-action-runs/${encodeURIComponent(id)}`);

export const enumerateFleetActionRunTargets = (
  runId: string,
  query?: { pageNumber?: number; pageSize?: number; status?: FleetActionTargetStatus | '' },
) =>
  post<EnumerationResult<FleetActionRunTargetSummary>>(`/api/v1/fleet-action-runs/${encodeURIComponent(runId)}/targets/enumerate`, {
    PageNumber: query?.pageNumber ?? 1,
    PageSize: query?.pageSize ?? 25,
    ...(query?.status ? { Status: query.status } : {}),
  });

/** One target including rendered text and captured output; the only endpoint that returns output. */
export const getFleetActionRunTarget = (runId: string, targetId: string) =>
  get<FleetActionRunTarget>(`/api/v1/fleet-action-runs/${encodeURIComponent(runId)}/targets/${encodeURIComponent(targetId)}`);

export const cancelFleetActionRun = (id: string) => post<FleetActionRun>(`/api/v1/fleet-action-runs/${encodeURIComponent(id)}/cancel`);

/** Admin-only screenshot data import; never runs agents or calls inference providers. */
export const importSyntheticTokenUsage = (data: TokenUsageSyntheticRequest) => post<TokenUsageSyntheticResult>('/api/v1/token-usage/synthetic', data);

// Global-administrator storage inventory. Reads use cached snapshots; scans are explicitly requested.
export function getStorage(): Promise<import('../types/storage').StorageSnapshot> {
  return request('GET', '/api/v1/storage', undefined, { dictionaryKeys: ['retention', 'categories', 'hosts', 'tables'] });
}
export function refreshStorage(exactCounts = false): Promise<import('../types/storage').StorageSnapshot> {
  return request('POST', '/api/v1/storage/refresh', { exactCounts }, { dictionaryKeys: ['retention', 'categories', 'hosts', 'tables'] });
}
/** One page of cached storage host reports (the Admiral first, then each Harbor). */
export function listStorageHosts(page?: { pageNumber?: number; pageSize?: number }): Promise<EnumerationResult<import('../types/storage').StorageHostReport>> {
  return post('/api/v1/storage/hosts/enumerate', { PageNumber: page?.pageNumber ?? 1, PageSize: page?.pageSize ?? 10 });
}
/** Every cached storage host report, read in explicit pages of 100 (bounded at 200 pages). */
export async function listAllStorageHosts(): Promise<import('../types/storage').StorageHostReport[]> {
  const all: import('../types/storage').StorageHostReport[] = [];
  for (let page = 1; page <= 200; page++) {
    const result = await listStorageHosts({ pageNumber: page, pageSize: 100 });
    all.push(...(result.objects ?? []));
    if (!result.objects?.length || page >= (result.totalPages ?? 1)) break;
  }
  return all;
}
