import type {
  AuthenticateRequest,
  AuthenticateResult,
  WhoAmIResult,
  TenantLookupResult,
  TenantMetadata,
  UserMaster,
  UserUpsertRequest,
  Credential,
  EnumerationResult,
  Job,
  Fleet,
  Vessel,
  Captain,
  CaptainToolAccessResult,
  Mission,
  MissionSummary,
  MissionHistorySummaryResult,
  TokenUsageSummaryResult,
  Voyage,
  Objective,
  GitHubActionsSyncRequest,
  GitHubActionsSyncResult,
  GitHubObjectiveImportRequest,
  GitHubPullRequestDetail,
  ObjectiveQuery,
  ObjectiveReorderRequest,
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
  ModelEndpoint,
  ModelEndpointProbeResult,
  ModelEndpointHealthSweepResponse,
  AskResponse,
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
  LandingPreviewResult,
  MuxEndpointListResult,
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
} from '../types/models';

const BASE_URL = import.meta.env.VITE_ARMADA_SERVER_URL || '';

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
function camelizeKeys(obj: any): any {
  if (Array.isArray(obj)) return obj.map(camelizeKeys);
  if (obj !== null && typeof obj === 'object' && !(obj instanceof Date)) {
    return Object.fromEntries(
      Object.entries(obj).map(([k, v]) => [keyToCamel(k), camelizeKeys(v)])
    );
  }
  return obj;
}

interface RequestOptions {
  timeout?: number;
  rawText?: boolean;
  /** External abort signal; when it fires the in-flight request is cancelled (used by chat Stop). */
  signal?: AbortSignal;
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
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (authToken) headers['X-Token'] = authToken;

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
    const res = await fetch(`${BASE_URL}${path}`, {
      method,
      headers,
      body: body ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    });

    if (res.status === 401) {
      onUnauthorized?.();
      throw new Error('Unauthorized');
    }

    if (!res.ok) {
      const text = await res.text();
      let msg = `${res.status}: ${text}`;
      try {
        const e = JSON.parse(text);
        msg = e.Message || e.message || e.Error || e.error || msg;
      } catch {
        // use raw text
      }
      throw new Error(msg);
    }

    if (res.status === 204) return undefined as T;

    if (opts?.rawText) {
      const text = await res.text();
      return text as unknown as T;
    }

    const json = await res.json();
    return camelizeKeys(json) as T;
  } catch (err) {
    // An external abort is a deliberate cancel (Stop), not a timeout — surface it distinctly so callers
    // can treat it as a clean stop instead of an error.
    if (external?.aborted) {
      const aborted = new Error('Aborted');
      aborted.name = 'AbortError';
      throw aborted;
    }
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw new Error('Request timed out');
    }
    throw err;
  } finally {
    clearTimeout(timeoutId);
    if (external) external.removeEventListener('abort', onExternalAbort);
  }
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

  const res = await fetch(`${BASE_URL}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    credentials: 'same-origin',
  });

  if (res.status === 404 || res.status === 401) {
    throw new Error(`proxy:${res.status}`);
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

// Build query string from pagination/filter params
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

export async function getProxySessionContext(): Promise<ProxySessionContext | null> {
  try {
    return await proxyRequest<ProxySessionContext>('GET', '/proxy-api/v1/session/context');
  } catch (error) {
    if (error instanceof Error && (error.message === 'proxy:404' || error.message === 'proxy:401')) {
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
    if (error instanceof Error && error.message === 'proxy:404') {
      return;
    }
    throw error;
  }
}

export const lookupTenants = (email: string) =>
  post<TenantLookupResult>('/api/v1/tenants/lookup', { Email: email });

// ==================== Tenants (admin) ====================
export const listTenants = () => get<EnumerationResult<TenantMetadata>>('/api/v1/tenants');
export const createTenant = (data: Partial<TenantMetadata>) => post<TenantMetadata>('/api/v1/tenants', data);
export const updateTenant = (id: string, data: Partial<TenantMetadata>) => put<TenantMetadata>(`/api/v1/tenants/${id}`, data);
export const deleteTenant = (id: string) => del<void>(`/api/v1/tenants/${id}`);

// ==================== Users (admin) ====================
export const listUsers = () => get<EnumerationResult<UserMaster>>('/api/v1/users');
export const createUser = (data: UserUpsertRequest) => post<UserMaster>('/api/v1/users', data);
export const updateUser = (id: string, data: UserUpsertRequest) => put<UserMaster>(`/api/v1/users/${id}`, data);
export const deleteUser = (id: string) => del<void>(`/api/v1/users/${id}`);

// ==================== Credentials (admin) ====================
export const listCredentials = () => get<EnumerationResult<Credential>>('/api/v1/credentials');
export const createCredential = (data: Partial<Credential>) => post<Credential>('/api/v1/credentials', data);
export const updateCredential = (id: string, data: Partial<Credential>) => put<Credential>(`/api/v1/credentials/${id}`, data);
export const deleteCredential = (id: string) => del<void>(`/api/v1/credentials/${id}`);

// ==================== Fleets ====================
export const listFleets = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Fleet>>(`/api/v1/fleets${buildQuery(params)}`);
export const getFleet = (id: string) => get<Fleet>(`/api/v1/fleets/${id}`);
export const createFleet = (data: Partial<Fleet>) => post<Fleet>('/api/v1/fleets', data);
export const updateFleet = (id: string, data: Partial<Fleet>) => put<Fleet>(`/api/v1/fleets/${id}`, data);
export const deleteFleet = (id: string) => del<void>(`/api/v1/fleets/${id}`);
export const deleteFleetsBatch = (ids: string[]) => post<BatchDeleteResult>('/api/v1/fleets/delete/multiple', { Ids: ids });

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

export const getVesselBranches = (id: string) =>
  get<{ vesselId: string; defaultBranch?: string; branches: BranchInfo[]; branchCount: number; error?: string }>(`/api/v1/vessels/${id}/branches`);

export const pushVesselBranch = (id: string, branch: string) =>
  post<{ vesselId: string; branch: string; pushed: boolean }>(`/api/v1/vessels/${id}/branches/push`, { Branch: branch });

export const mergeVesselBranch = (id: string, source: string, target: string, push: boolean) =>
  post<{ vesselId: string; source: string; target: string; merged: boolean; pushed: boolean }>(`/api/v1/vessels/${id}/branches/merge`, { Source: source, Target: target, Push: push });

// ==================== Workspace ====================
function encodeWorkspaceQueryPath(path: string) {
  return encodeURIComponent(path).replace(/%2F/g, '/');
}

export const getWorkspaceStatus = (vesselId: string) =>
  get<WorkspaceStatusResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/status`);
export const getWorkspaceTree = (vesselId: string, path?: string) =>
  get<WorkspaceTreeResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/tree${path ? `?path=${encodeWorkspaceQueryPath(path)}` : ''}`);
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
export const getWorkspaceChanges = (vesselId: string) =>
  get<WorkspaceChangesResult>(`/api/v1/workspace/vessels/${encodeURIComponent(vesselId)}/changes`);

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
export const reorderObjectives = (data: ObjectiveReorderRequest) => post<Objective[]>('/api/v1/objectives/reorder', data);
export const getObjective = (id: string) => get<Objective>(`/api/v1/objectives/${encodeURIComponent(id)}`);
export const createObjective = (data: ObjectiveUpsertRequest) => post<Objective>('/api/v1/objectives', data);
export const updateObjective = (id: string, data: ObjectiveUpsertRequest) => put<Objective>(`/api/v1/objectives/${encodeURIComponent(id)}`, data);
export const deleteObjective = (id: string) => del<void>(`/api/v1/objectives/${encodeURIComponent(id)}`);
export const importObjectiveFromGitHub = (data: GitHubObjectiveImportRequest) => post<Objective>('/api/v1/objectives/import/github', data);
export const listBacklog = (params?: ObjectiveQuery) =>
  get<EnumerationResult<Objective>>(`/api/v1/backlog${buildObjectiveQuery(params)}`);
export const enumerateBacklog = (query?: ObjectiveQuery) =>
  post<EnumerationResult<Objective>>('/api/v1/backlog/enumerate', query || {});
export const reorderBacklog = (data: ObjectiveReorderRequest) => post<Objective[]>('/api/v1/backlog/reorder', data);
export const getBacklogItem = (id: string) => get<Objective>(`/api/v1/backlog/${encodeURIComponent(id)}`);
export const createBacklogItem = (data: ObjectiveUpsertRequest) => post<Objective>('/api/v1/backlog', data);
export const updateBacklogItem = (id: string, data: ObjectiveUpsertRequest) => put<Objective>(`/api/v1/backlog/${encodeURIComponent(id)}`, data);
export const deleteBacklogItem = (id: string) => del<void>(`/api/v1/backlog/${encodeURIComponent(id)}`);
export const listObjectiveRefinementSessions = (objectiveId: string) =>
  get<ObjectiveRefinementSession[]>(`/api/v1/objectives/${encodeURIComponent(objectiveId)}/refinement-sessions`);
export const listBacklogRefinementSessions = (objectiveId: string) =>
  get<ObjectiveRefinementSession[]>(`/api/v1/backlog/${encodeURIComponent(objectiveId)}/refinement-sessions`);
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
export const getCaptainTools = (id: string) => get<CaptainToolAccessResult>(`/api/v1/captains/${id}/tools`, { timeout: 120000 });
export const createCaptain = (data: Partial<Captain>) => post<Captain>('/api/v1/captains', data);
export const updateCaptain = (id: string, data: Partial<Captain>) => put<Captain>(`/api/v1/captains/${id}`, data);
export const deleteCaptain = (id: string) => del<void>(`/api/v1/captains/${id}`);
export const getCaptainLog = (id: string, lines = 500, formatted = false) => get<LogResult>(`/api/v1/captains/${id}/log?lines=${lines}${formatted ? '&formatted=true' : ''}`);
export const stopCaptain = (id: string) => post<void>(`/api/v1/captains/${id}/stop`);
export const unquarantineCaptain = (id: string) => post<Captain>(`/api/v1/captains/${id}/unquarantine`);
export const recallCaptain = (id: string) => post<void>(`/api/v1/captains/${id}/recall`);
export const stopAllCaptains = () => post<void>('/api/v1/captains/stop-all');

// Background jobs
export const listJobs = () => get<EnumerationResult<Job>>('/api/v1/jobs');
export const getJob = (id: string) => get<Job>(`/api/v1/jobs/${id}`);
export const cancelJob = (id: string) => post<Job>(`/api/v1/jobs/${id}/cancel`);
export const listMuxEndpoints = (configDirectory?: string | null) =>
  get<MuxEndpointListResult>(`/api/v1/runtimes/mux/endpoints${configDirectory ? `?configDirectory=${encodeURIComponent(configDirectory)}` : ''}`);
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
export const getTokenUsage = (params?: { fromUtc?: string; toUtc?: string; bucketMinutes?: number; model?: string; runtime?: string; source?: string; vesselId?: string; captainId?: string }) =>
  get<TokenUsageSummaryResult>(`/api/v1/token-usage/summary${buildQuery(params ? {
    filters: Object.fromEntries(
      Object.entries(params)
        .filter(([, value]) => value !== undefined && value !== null && value !== '')
        .map(([key, value]) => [key, String(value)]),
    ),
  } : undefined)}`);
export const getMissionLandingPreview = (id: string) => get<LandingPreviewResult>(`/api/v1/missions/${encodeURIComponent(id)}/landing-preview`);
export const getMissionGitHubPullRequest = (id: string) => get<GitHubPullRequestDetail>(`/api/v1/missions/${encodeURIComponent(id)}/github/pull-request`);
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

// ==================== Voyages ====================
export const listVoyages = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<Voyage>>(`/api/v1/voyages${buildQuery(params)}`);
export const getVoyage = (id: string) => get<Voyage>(`/api/v1/voyages/${id}`);
export const getVoyageStatus = (id: string) => get<Record<string, unknown>>(`/api/v1/voyages/${id}/status`);
export const createVoyage = (data: VoyageCreateRequest) => post<Voyage>('/api/v1/voyages', data);
export const cancelVoyage = (id: string) => del<void>(`/api/v1/voyages/${id}`);
export const purgeVoyage = (id: string) => del<void>(`/api/v1/voyages/${id}/purge`);

// ==================== Planning Sessions ====================
export const listPlanningSessions = () => get<PlanningSession[]>('/api/v1/planning-sessions');
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
export const processAllMergeQueue = () => post<void>('/api/v1/merge-queue/process-all');
export const cancelMergeEntry = (id: string) => post<void>(`/api/v1/merge-queue/${id}/cancel`);

// ==================== Prompt Templates ====================
export const listPromptTemplates = (params?: { pageNumber?: number; pageSize?: number; filters?: Record<string, string> }) =>
  get<EnumerationResult<PromptTemplate>>(`/api/v1/prompt-templates${buildQuery(params)}`);
export const getPromptTemplate = (name: string) => get<PromptTemplate>(`/api/v1/prompt-templates/${encodeURIComponent(name)}`);
export const createPromptTemplate = (data: { name: string; category: string; content: string; description?: string; active?: boolean }) =>
  post<PromptTemplate>('/api/v1/prompt-templates', data);
export const updatePromptTemplate = (name: string, data: { content: string; description?: string }) => put<PromptTemplate>(`/api/v1/prompt-templates/${encodeURIComponent(name)}`, data);
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
export const listHarbors = () => get<Harbor[]>('/api/v1/harbors');
export const getHarbor = (id: string) => get<Harbor>(`/api/v1/harbors/${encodeURIComponent(id)}`);
export const createHarbor = (data: Partial<Harbor>) => post<Harbor>('/api/v1/harbors', data);
export const updateHarbor = (id: string, data: Partial<Harbor>) =>
  put<Harbor>(`/api/v1/harbors/${encodeURIComponent(id)}`, data);
export const deleteHarbor = (id: string) => del<void>(`/api/v1/harbors/${encodeURIComponent(id)}`);
export const enableHarbor = (id: string) => post<Harbor>(`/api/v1/harbors/${encodeURIComponent(id)}/enable`, {});
export const disableHarbor = (id: string) => post<Harbor>(`/api/v1/harbors/${encodeURIComponent(id)}/disable`, {});

// Model endpoints (embedding/inference)
export const listModelEndpoints = () => get<ModelEndpoint[]>('/api/v1/model-endpoints');
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

// Ask Armada
export const askArmada = (message: string) => post<AskResponse>('/api/v1/ask', { message });
export const chatWithCaptain = (captainId: string, body: CaptainChatRequest, opts?: { signal?: AbortSignal }) =>
  // A chat turn launches the captain's CLI runtime headlessly, which can take minutes for slower
  // agents (e.g. Codex). Allow more than the default 30s so the reply is not aborted client-side;
  // keep it above the server-side chat timeout so the backend's clean message wins on timeout.
  // An optional signal lets the Stop button abort the request (the server then cancels the runtime).
  post<CaptainChatResponse>('/api/v1/captains/' + captainId + '/chat', body, { timeout: 330000, signal: opts?.signal });

// Needs-you inbox
export const getInbox = () => get<InboxItem[]>('/api/v1/inbox');

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
export const getReleaseGitHubPullRequests = (id: string) => get<GitHubPullRequestDetail[]>(`/api/v1/releases/${encodeURIComponent(id)}/github/pull-requests`);

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

// ==================== Backup / Restore ====================
/** Download backup as a ZIP file blob. The server endpoint is GET and returns binary. */
export async function downloadBackup(): Promise<void> {
  const headers: Record<string, string> = {};
  if (authToken) headers['X-Token'] = authToken;
  const res = await fetch(`${BASE_URL}/api/v1/backup`, { method: 'GET', headers });
  if (res.status === 401) { onUnauthorized?.(); throw new Error('Unauthorized'); }
  if (!res.ok) throw new Error(`Backup failed: ${res.status}`);
  const blob = await res.blob();
  const disposition = res.headers.get('Content-Disposition') || '';
  const match = disposition.match(/filename="?([^"]+)"?/);
  const filename = match ? match[1] : `armada-backup-${new Date().toISOString().slice(0, 10)}.zip`;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

/** Upload a backup ZIP file to restore. Sends raw bytes with filename header. */
export async function restoreBackup(file: File): Promise<Record<string, unknown>> {
  const headers: Record<string, string> = { 'Content-Type': 'application/octet-stream' };
  if (authToken) headers['X-Token'] = authToken;
  headers['X-Original-Filename'] = file.name;
  const bytes = await file.arrayBuffer();
  const res = await fetch(`${BASE_URL}/api/v1/restore`, { method: 'POST', headers, body: bytes });
  if (res.status === 401) { onUnauthorized?.(); throw new Error('Unauthorized'); }
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
