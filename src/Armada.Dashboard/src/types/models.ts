export interface TenantMetadata {
  id: string;
  name: string;
  active: boolean;
  isProtected: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface UserMaster {
  id: string;
  tenantId: string;
  email: string;
  firstName: string | null;
  lastName: string | null;
  isAdmin: boolean;
  isTenantAdmin: boolean;
  isProtected: boolean;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface UserUpsertRequest {
  tenantId?: string;
  email: string;
  password?: string;
  passwordSha256?: string;
  firstName?: string | null;
  lastName?: string | null;
  isAdmin?: boolean;
  isTenantAdmin?: boolean;
  active?: boolean;
}

export interface Credential {
  id: string;
  tenantId: string;
  userId: string;
  name: string | null;
  bearerToken: string;
  isProtected: boolean;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface AuthenticateRequest {
  email: string;
  password: string;
  tenantId: string;
}

export interface AuthenticateResult {
  success: boolean;
  token: string | null;
  expiresUtc: string | null;
}

export interface WhoAmIResult {
  tenant: TenantMetadata | null;
  user: UserMaster | null;
}

export interface TenantLookupResult {
  tenants: TenantListEntry[];
}

export interface TenantListEntry {
  id: string;
  name: string;
}

export interface OnboardingResult {
  success: boolean;
  tenant: TenantMetadata | null;
  user: UserMaster | null;
  credential: Credential | null;
  errorMessage: string | null;
}

export interface EnumerationResult<T> {
  success: boolean;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  totalRecords: number;
  objects: T[];
  totalMs: number;
}

export interface Fleet {
  id: string;
  name: string;
  tenantId: string | null;
  description: string | null;
  defaultPipelineId: string | null;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface Vessel {
  id: string;
  tenantId: string | null;
  fleetId: string | null;
  name: string;
  repoUrl: string | null;
  localPath: string | null;
  workingDirectory: string | null;
  defaultBranch: string;
  projectContext: string | null;
  styleGuide: string | null;
  enableModelContext: boolean;
  modelContext: string | null;
  landingMode: string | null;
  branchCleanupPolicy: string | null;
  allowConcurrentMissions: boolean;
  defaultPipelineId: string | null;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface Captain {
  id: string;
  tenantId: string | null;
  name: string;
  runtime: string;
  systemInstructions: string | null;
  model: string | null;
  allowedPersonas: string | null;
  preferredPersona: string | null;
  state: string;
  currentMissionId: string | null;
  currentDockId: string | null;
  processId: number | null;
  recoveryAttempts: number;
  lastHeartbeatUtc: string | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface Mission {
  id: string;
  tenantId: string | null;
  voyageId: string | null;
  vesselId: string | null;
  captainId: string | null;
  title: string;
  description: string | null;
  status: string;
  priority: number;
  parentMissionId: string | null;
  persona: string | null;
  dependsOnMissionId: string | null;
  branchName: string | null;
  dockId: string | null;
  processId: number | null;
  prUrl: string | null;
  commitHash: string | null;
  failureReason: string | null;
  diffSnapshot: string | null;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  totalRuntimeMs: number | null;
  lastUpdateUtc: string;
  selectedPlaybooks?: SelectedPlaybook[];
  playbookSnapshots?: MissionPlaybookSnapshot[];
}

export interface Voyage {
  id: string;
  tenantId: string | null;
  title: string;
  description: string | null;
  status: string;
  createdUtc: string;
  completedUtc: string | null;
  lastUpdateUtc: string;
  autoPush: boolean | null;
  autoCreatePullRequests: boolean | null;
  autoMergePullRequests: boolean | null;
  landingMode: string | null;
  selectedPlaybooks?: SelectedPlaybook[];
}

export type PlaybookDeliveryMode =
  | 'InlineFullContent'
  | 'InstructionWithReference'
  | 'AttachIntoWorktree';

export interface SelectedPlaybook {
  playbookId: string;
  deliveryMode: PlaybookDeliveryMode;
}

export interface MissionPlaybookSnapshot {
  playbookId: string | null;
  fileName: string;
  description: string | null;
  content: string;
  deliveryMode: PlaybookDeliveryMode;
  resolvedPath: string | null;
  worktreeRelativePath: string | null;
  sourceLastUpdateUtc: string | null;
}

export interface Playbook {
  id: string;
  tenantId: string | null;
  userId: string | null;
  fileName: string;
  description: string | null;
  content: string;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface ArmadaEvent {
  id: string;
  tenantId: string | null;
  eventType: string;
  entityType: string | null;
  entityId: string | null;
  captainId: string | null;
  missionId: string | null;
  vesselId: string | null;
  voyageId: string | null;
  message: string;
  payload: string | null;
  createdUtc: string;
}

export interface MergeEntry {
  id: string;
  tenantId: string | null;
  missionId: string | null;
  vesselId: string | null;
  branchName: string;
  targetBranch: string;
  status: string;
  priority: number;
  batchId: string | null;
  testCommand: string | null;
  testOutput: string | null;
  testExitCode: number | null;
  createdUtc: string;
  lastUpdateUtc: string;
  testStartedUtc: string | null;
  completedUtc: string | null;
}

export interface Signal {
  id: string;
  tenantId: string | null;
  fromCaptainId: string | null;
  toCaptainId: string | null;
  type: string;
  payload: string | null;
  read: boolean;
  createdUtc: string;
}

export interface Dock {
  id: string;
  tenantId: string | null;
  vesselId: string;
  captainId: string | null;
  worktreePath: string | null;
  branchName: string | null;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface HealthResult {
  status: string;
  checks: HealthCheck[];
}

export interface HealthCheck {
  name: string;
  status: string;
  message: string;
}

export interface DoctorCheck {
  name: string;
  status: string;
  message: string;
}

export interface DiffResult {
  diff: string;
  branch?: string;
  error?: string;
}

export interface LogResult {
  log: string;
  lines: number;
  totalLines: number;
}

export interface InstructionsResult {
  fileName: string;
  content: string;
}

export interface DispatchRequest {
  vesselId: string;
  title: string;
  description?: string;
  priority?: number;
}

export interface VoyageCreateRequest {
  title: string;
  description?: string;
  vesselId?: string;
  pipelineId?: string;
  pipeline?: string;
  missions: DispatchRequest[];
  selectedPlaybooks?: SelectedPlaybook[];
}

export interface TransitionRequest {
  status: string;
}

export interface SendSignalRequest {
  toCaptainId?: string;
  type: string;
  payload?: string;
}

export interface SettingsData {
  [key: string]: unknown;
}

export interface BatchDeleteRequest {
  ids: string[];
}

export interface BatchDeleteResult {
  deleted: number;
  skipped: { id: string; reason: string }[];
}

export interface StatusSnapshot {
  health?: string;
  captains?: number;
  activeMissions?: number;
  [key: string]: unknown;
}

export interface WebSocketMessage {
  type: string;
  data?: Record<string, unknown>;
}

export interface PromptTemplate {
  id: string;
  tenantId: string | null;
  name: string;
  description: string | null;
  category: string;
  content: string;
  isBuiltIn: boolean;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface Persona {
  id: string;
  tenantId: string | null;
  name: string;
  description: string | null;
  promptTemplateName: string;
  isBuiltIn: boolean;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface Pipeline {
  id: string;
  tenantId: string | null;
  name: string;
  description: string | null;
  stages: PipelineStage[];
  isBuiltIn: boolean;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface PipelineStage {
  id: string;
  pipelineId: string | null;
  order: number;
  personaName: string;
  isOptional: boolean;
  description: string | null;
}

export type EntityType = 'fleets' | 'vessels' | 'captains' | 'missions' | 'voyages' | 'signals' | 'events' | 'docks' | 'merge-queue' | 'personas' | 'prompt-templates' | 'pipelines' | 'playbooks';
