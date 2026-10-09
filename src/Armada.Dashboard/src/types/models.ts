export interface TenantMetadata {
  id: string;
  name: string;
  active: boolean;
  isProtected: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

/** Body of POST /api/v1/tenants: the tenant plus an optional password for its seeded admin. */
export interface TenantCreateRequest {
  name: string;
  active?: boolean;
  /** Password for the seeded admin@armada account; omit to have the server generate one. */
  adminPassword?: string;
}

/**
 * Response of POST /api/v1/tenants. `adminPassword` is the generated password of the seeded tenant admin, returned
 * only in this response (null when the creator supplied one). Show it once; never store it.
 */
export interface TenantCreateResult extends TenantMetadata {
  adminEmail: string | null;
  adminPassword: string | null;
}

/**
 * Ownership scope for Category B configuration entities. Tenant-wide objects are visible to everyone
 * in the tenant but editable only by tenant/global admins; user-specific objects are owned by a user.
 * Workflow profiles and project profiles carry this as `ownershipScope` (their `scope` field is the
 * application scope: Global/Fleet/Vessel).
 */
export type ScopeEnum = 'TenantWide' | 'UserSpecific';

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
  /** The caller's current password; required by the server only when changing your own password. */
  currentPassword?: string;
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
  passwordChangeRequired?: boolean;
}

export interface WhoAmIResult {
  tenant: TenantMetadata | null;
  user: UserMaster | null;
  /** The signed-in seeded admin still uses the default password; the API is limited until it changes. */
  passwordChangeRequired?: boolean;
  /** Default credentials are still in use on this server (admins and tenant admins only). */
  defaultCredentialsInUse?: boolean;
}

export interface PasswordChangeRequest {
  CurrentPassword: string;
  NewPassword: string;
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
  gitHubTokenOverride?: string | null;
  hasGitHubTokenOverride: boolean;
  landingMode: string | null;
  branchCleanupPolicy: string | null;
  requirePassingChecksToLand: boolean;
  protectedBranchPatterns: string[];
  releaseBranchPrefix: string;
  hotfixBranchPrefix: string;
  requirePullRequestForProtectedBranches: boolean;
  requireMergeQueueForReleaseBranches: boolean;
  secretScanEnabled?: boolean;
  protectedPathPatterns?: string[];
  privateIdentifierDenylist?: string[];
  autoLandEnabled?: boolean;
  autoLandMaxFiles?: number;
  autoLandMaxLines?: number;
  autoLandPathAllowGlobs?: string[];
  autoLandPathDenyGlobs?: string[];
  definitionOfDoneEnabled?: boolean;
  definitionOfDoneBuildCommand?: string | null;
  definitionOfDoneTestCommand?: string | null;
  definitionOfDoneTimeoutSeconds?: number;
  allowConcurrentMissions: boolean;
  /** Per-vessel auto-approve override for missions; null or absent uses the captain setting. */
  autoApprove?: boolean | null;
  defaultPipelineId: string | null;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

/** Wire values of MissionStatusEnum. */
export type MissionStatus =
  | 'Pending'
  | 'Assigned'
  | 'InProgress'
  | 'WorkProduced'
  | 'PullRequestOpen'
  | 'Testing'
  | 'Review'
  | 'Complete'
  | 'Failed'
  | 'LandingFailed'
  | 'Cancelled';

/** Wire values of VoyageStatusEnum. */
export type VoyageStatus = 'Open' | 'InProgress' | 'Complete' | 'Failed' | 'Cancelled';

/** Wire values of CaptainStateEnum. */
export type CaptainState = 'Idle' | 'Working' | 'Planning' | 'Refining' | 'Stalled' | 'Stopping' | 'Quarantined' | 'Analyzing';

export interface Captain {
  id: string;
  tenantId: string | null;
  name: string;
  runtime: string;
  supportsPlanningSessions: boolean;
  planningSessionSupportReason: string | null;
  systemInstructions: string | null;
  model: string | null;
  modelEndpointId?: string | null;
  reasoningEffort?: string | null;
  tier?: string | null;
  quarantineUntilUtc?: string | null;
  quarantineReason?: string | null;
  allowedPersonas: string | null;
  preferredPersona: string | null;
  runtimeOptionsJson?: string | null;
  /** CLI tool permission policy for this captain's CLI sessions; null or absent inherits (see CliPermissionPolicy). */
  cliPermissionPolicy?: CliPermissionPolicy | null;
  state: string;
  currentMissionId: string | null;
  currentDockId: string | null;
  processId: number | null;
  recoveryAttempts: number;
  lastHeartbeatUtc: string | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

/** Wire values of CaptainToolSourceKindEnum. */
export type CaptainToolSourceKind = 'Internal' | 'McpServer' | 'RuntimeBuiltIn';

export interface CaptainToolSummary {
  name: string;
  description: string;
  inputSchemaJson: string | null;
  registrationSource: string | null;
  sourceKind: CaptainToolSourceKind;
}

export interface CaptainToolServerSummary {
  name: string;
  sourceKind: CaptainToolSourceKind;
  transport: string;
  target: string;
  url: string | null;
  command: string | null;
  workingDirectory: string | null;
  enabled: boolean;
  reachable: boolean;
  toolCount: number;
  headerCount: number;
  environmentVariableCount: number;
  enabledToolFilterCount: number;
  disabledToolFilterCount: number;
  startupTimeoutSeconds: number;
  toolTimeoutSeconds: number;
  status: string;
  errorMessage: string | null;
}

export interface CaptainToolAccessResult {
  captainId: string;
  captainName: string;
  runtime: string;
  toolsAccessible: boolean;
  availabilityVerified: boolean;
  availabilitySource: string;
  summary: string;
  endpointName: string | null;
  toolsEnabled: boolean | null;
  effectiveToolCount: number | null;
  armadaToolCount: number;
  configuredServerCount: number;
  reachableServerCount: number;
  /** True when Ask thread turns of this captain hold mutating Armada tool calls as approval cards. */
  askApprovalGated?: boolean;
  servers: CaptainToolServerSummary[];
  tools: CaptainToolSummary[];
}

/** Execution mode of a mission. Audit and Research are read-only modes that produce a report, not a commit. */
export type MissionMode = 'Implementation' | 'Audit' | 'Research';

export interface Mission {
  id: string;
  tenantId: string | null;
  userId?: string | null;
  voyageId: string | null;
  vesselId: string | null;
  captainId: string | null;
  requestedCaptainId?: string | null;
  title: string;
  description: string | null;
  status: string;
  mode: MissionMode;
  priority: number;
  parentMissionId: string | null;
  persona: string | null;
  dependsOnMissionId: string | null;
  requiresReview: boolean;
  reviewDenyAction: 'RetryStage' | 'FailPipeline';
  reviewComment: string | null;
  reviewedByUserId: string | null;
  reviewRequestedUtc: string | null;
  reviewedUtc: string | null;
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
  /** Server-computed: why a Pending mission is still waiting for a captain (single mission and voyage reads only). */
  assignmentBlocker?: MissionAssignmentBlocker | null;
}

export type MissionAssignmentBlockerReason =
  | 'AwaitingDispatch'
  | 'VesselMissing'
  | 'VesselMisconfigured'
  | 'DependencyNotFinished'
  | 'DependencyHandoffPending'
  | 'WaitingForVoyageWorkers'
  | 'VesselBroadScopeMissionActive'
  | 'BroadScopeWaitingForVessel'
  | 'VesselConcurrencyLimit'
  | 'NoCaptains'
  | 'NoIdleCaptain'
  | 'NoEligibleCaptain';

export interface MissionAssignmentCaptainStatus {
  captainId: string;
  captainName: string | null;
  state: string;
  detail: string;
  missionId: string | null;
  planningSessionId: string | null;
  refinementSessionId: string | null;
  objectiveId: string | null;
  quarantineUntilUtc: string | null;
  quarantineReason: string | null;
}

export interface MissionAssignmentBlocker {
  reason: MissionAssignmentBlockerReason;
  summary: string;
  untilUtc: string | null;
  dependsOnMissionId: string | null;
  blockingMissionIds: string[];
  captains: MissionAssignmentCaptainStatus[];
  computedUtc: string;
}

export interface MissionSummary {
  id: string;
  tenantId: string | null;
  userId: string | null;
  voyageId: string | null;
  vesselId: string | null;
  captainId: string | null;
  title: string;
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
  requiresReview: boolean;
  reviewDenyAction: 'RetryStage' | 'FailPipeline';
  reviewComment: string | null;
  reviewedByUserId: string | null;
  reviewRequestedUtc: string | null;
  reviewedUtc: string | null;
  descriptionLength: number;
  diffSnapshotLength: number;
  agentOutputLength: number;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  totalRuntimeMs: number | null;
  lastUpdateUtc: string;
}

export interface MissionHistoryBucket {
  startUtc: string;
  completeCount: number;
  failedCount: number;
  otherCount: number;
  totalCount: number;
}

export interface MissionHistorySummaryResult {
  totalCount: number;
  completeCount: number;
  failedCount: number;
  otherCount: number;
  fromUtc: string;
  toUtc: string;
  bucketMinutes: number;
  buckets: MissionHistoryBucket[];
}

export interface TokenUsageModelBreakdown {
  model: string;
  inputTokens: number;
  outputTokens: number;
  cachedTokens: number;
  totalTokens: number;
}

export interface TokenUsageBucket {
  bucketStartUtc: string;
  bucketEndUtc: string;
  inputTokens: number;
  outputTokens: number;
  cachedTokens: number;
  totalTokens: number;
  models: TokenUsageModelBreakdown[];
}

export interface TokenUsageSummaryResult {
  fromUtc: string | null;
  toUtc: string | null;
  bucketMinutes: number;
  recordCount: number;
  estimatedCount: number;
  inputTokens: number;
  outputTokens: number;
  cachedTokens: number;
  totalTokens: number;
  buckets: TokenUsageBucket[];
  byModel: TokenUsageModelBreakdown[];
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
  sourcePlanningSessionId?: string | null;
  sourcePlanningMessageId?: string | null;
  selectedPlaybooks?: SelectedPlaybook[];
  captainOverridesJson?: string | null;
}

/** Capability tier used for fallback routing when a preferred captain is busy. */
export type CaptainTier = 'Economy' | 'Standard' | 'Premium';

/** Per-persona captain override selected at dispatch (preferred captain + fallback tier). */
export interface CaptainAssignmentOverride {
  persona: string;
  captainId?: string | null;
  fallbackTier?: CaptainTier | null;
}

export type ObjectiveStatus =
  | 'Draft'
  | 'Scoped'
  | 'Planned'
  | 'InProgress'
  | 'Released'
  | 'Deployed'
  | 'Completed'
  | 'Blocked'
  | 'Cancelled';

export type ObjectiveKind = 'Feature' | 'Bug' | 'Refactor' | 'Research' | 'Chore' | 'Initiative';
export type ObjectivePriority = 'P0' | 'P1' | 'P2' | 'P3';
export type ObjectiveBacklogState = 'Inbox' | 'Triaged' | 'Refining' | 'ReadyForPlanning' | 'ReadyForDispatch' | 'Dispatched';
export type ObjectiveEffort = 'XS' | 'S' | 'M' | 'L' | 'XL';

export interface Objective {
  id: string;
  tenantId: string | null;
  userId: string | null;
  title: string;
  description: string | null;
  status: ObjectiveStatus;
  kind: ObjectiveKind;
  category: string | null;
  priority: ObjectivePriority;
  rank: number;
  backlogState: ObjectiveBacklogState;
  effort: ObjectiveEffort;
  owner: string | null;
  targetVersion: string | null;
  dueUtc: string | null;
  parentObjectiveId: string | null;
  blockedByObjectiveIds: string[];
  refinementSummary: string | null;
  suggestedPipelineId: string | null;
  suggestedPlaybooks: SelectedPlaybook[];
  refinementSessionIds: string[];
  sourceProvider: string | null;
  sourceType: string | null;
  sourceId: string | null;
  /** GitHub issue or pull request number of the source, when the objective was imported from GitHub. */
  sourceNumber?: number | null;
  sourceUrl: string | null;
  sourceUpdatedUtc: string | null;
  tags: string[];
  acceptanceCriteria: string[];
  nonGoals: string[];
  rolloutConstraints: string[];
  evidenceLinks: string[];
  fleetIds: string[];
  vesselIds: string[];
  planningSessionIds: string[];
  voyageIds: string[];
  missionIds: string[];
  checkRunIds: string[];
  releaseIds: string[];
  deploymentIds: string[];
  incidentIds: string[];
  createdUtc: string;
  lastUpdateUtc: string;
  completedUtc: string | null;
}

export interface ObjectiveQuery {
  tenantId?: string | null;
  userId?: string | null;
  owner?: string | null;
  category?: string | null;
  parentObjectiveId?: string | null;
  vesselId?: string | null;
  fleetId?: string | null;
  planningSessionId?: string | null;
  voyageId?: string | null;
  missionId?: string | null;
  checkRunId?: string | null;
  releaseId?: string | null;
  deploymentId?: string | null;
  incidentId?: string | null;
  tag?: string | null;
  status?: ObjectiveStatus | null;
  backlogState?: ObjectiveBacklogState | null;
  kind?: ObjectiveKind | null;
  priority?: ObjectivePriority | null;
  effort?: ObjectiveEffort | null;
  targetVersion?: string | null;
  search?: string | null;
  fromUtc?: string | null;
  toUtc?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface ObjectiveUpsertRequest {
  title?: string | null;
  description?: string | null;
  status?: ObjectiveStatus | null;
  kind?: ObjectiveKind | null;
  category?: string | null;
  priority?: ObjectivePriority | null;
  rank?: number | null;
  backlogState?: ObjectiveBacklogState | null;
  effort?: ObjectiveEffort | null;
  owner?: string | null;
  targetVersion?: string | null;
  dueUtc?: string | null;
  parentObjectiveId?: string | null;
  blockedByObjectiveIds?: string[] | null;
  refinementSummary?: string | null;
  suggestedPipelineId?: string | null;
  suggestedPlaybooks?: SelectedPlaybook[] | null;
  tags?: string[] | null;
  acceptanceCriteria?: string[] | null;
  nonGoals?: string[] | null;
  rolloutConstraints?: string[] | null;
  evidenceLinks?: string[] | null;
  fleetIds?: string[] | null;
  vesselIds?: string[] | null;
  planningSessionIds?: string[] | null;
  refinementSessionIds?: string[] | null;
  voyageIds?: string[] | null;
  missionIds?: string[] | null;
  checkRunIds?: string[] | null;
  releaseIds?: string[] | null;
  deploymentIds?: string[] | null;
  incidentIds?: string[] | null;
}

export interface ObjectiveReorderItem {
  objectiveId: string;
  rank: number;
}

export interface ObjectiveReorderRequest {
  items: ObjectiveReorderItem[];
}

export type GitHubObjectiveSourceType = 'Issue' | 'PullRequest';

export interface GitHubObjectiveImportRequest {
  vesselId?: string | null;
  objectiveId?: string | null;
  sourceType: GitHubObjectiveSourceType;
  number: number;
  statusOverride?: ObjectiveStatus | null;
}

export interface GitHubActionsSyncRequest {
  vesselId?: string | null;
  workflowProfileId?: string | null;
  deploymentId?: string | null;
  environmentName?: string | null;
  branchName?: string | null;
  commitHash?: string | null;
  workflowName?: string | null;
  runStatus?: string | null;
  typeOverride?: CheckRunType | null;
  runCount?: number;
}

export interface GitHubActionsSyncResult {
  providerName: string;
  vesselId: string | null;
  deploymentId: string | null;
  createdCount: number;
  updatedCount: number;
  checkRuns: CheckRun[];
}

export interface GitHubPullRequestReview {
  reviewerLogin: string | null;
  state: string;
  body: string | null;
  submittedUtc: string | null;
}

export interface GitHubPullRequestComment {
  authorLogin: string | null;
  body: string | null;
  url: string | null;
  createdUtc: string | null;
}

export interface GitHubPullRequestCheck {
  name: string;
  status: string;
  conclusion: string | null;
  detailsUrl: string | null;
}

export interface GitHubPullRequestDetail {
  repository: string;
  number: number;
  missionId: string | null;
  url: string;
  title: string;
  body: string | null;
  state: string;
  isDraft: boolean;
  isMerged: boolean;
  mergeableState: string | null;
  reviewStatus: string;
  baseRefName: string;
  headRefName: string;
  headSha: string | null;
  authorLogin: string | null;
  mergedByLogin: string | null;
  requestedReviewers: string[];
  labels: string[];
  changedFiles: number;
  additions: number;
  deletions: number;
  commitCount: number;
  reviews: GitHubPullRequestReview[];
  comments: GitHubPullRequestComment[];
  checks: GitHubPullRequestCheck[];
  createdUtc: string | null;
  updatedUtc: string | null;
  mergedUtc: string | null;
}

export interface PlanningSession {
  id: string;
  tenantId: string | null;
  userId: string | null;
  captainId: string;
  vesselId: string;
  fleetId: string | null;
  dockId: string | null;
  branchName: string | null;
  title: string;
  status: string;
  pipelineId: string | null;
  processId: number | null;
  failureReason: string | null;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  lastUpdateUtc: string;
  selectedPlaybooks?: SelectedPlaybook[];
}

export interface PlanningSessionMessage {
  id: string;
  planningSessionId: string;
  tenantId: string | null;
  userId: string | null;
  role: string;
  sequence: number;
  content: string;
  isSelectedForDispatch: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
  metrics?: CaptainChatMetrics | null;
}

export interface PlanningSessionDetail {
  session: PlanningSession;
  messages: PlanningSessionMessage[];
  captain: Captain | null;
  vessel: Vessel | null;
}

export type ObjectiveRefinementSessionStatus =
  | 'Created'
  | 'Active'
  | 'Responding'
  | 'Stopping'
  | 'Stopped'
  | 'Completed'
  | 'Failed';

export interface ObjectiveRefinementSession {
  id: string;
  objectiveId: string;
  tenantId: string | null;
  userId: string | null;
  captainId: string;
  fleetId: string | null;
  vesselId: string | null;
  title: string;
  status: ObjectiveRefinementSessionStatus;
  processId: number | null;
  failureReason: string | null;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  lastUpdateUtc: string;
}

export interface ObjectiveRefinementMessage {
  id: string;
  objectiveRefinementSessionId: string;
  objectiveId: string;
  tenantId: string | null;
  userId: string | null;
  role: string;
  sequence: number;
  content: string;
  isSelected: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface ObjectiveRefinementSessionDetail {
  session: ObjectiveRefinementSession;
  messages: ObjectiveRefinementMessage[];
  captain: Captain | null;
  vessel: Vessel | null;
  objective: Objective | null;
}

export interface ObjectiveRefinementSessionCreateRequest {
  captainId: string;
  fleetId?: string | null;
  vesselId?: string | null;
  title?: string | null;
  initialMessage?: string | null;
}

export interface ObjectiveRefinementMessageRequest {
  content: string;
}

export interface ObjectiveRefinementSummaryRequest {
  messageId?: string | null;
}

export interface ObjectiveRefinementSummaryResponse {
  sessionId: string;
  messageId: string | null;
  summary: string;
  acceptanceCriteria: string[];
  nonGoals: string[];
  rolloutConstraints: string[];
  suggestedPipelineId: string | null;
  method: string;
}

export interface ObjectiveRefinementApplyRequest {
  messageId?: string | null;
  markMessageSelected?: boolean;
  promoteBacklogState?: boolean;
}

export interface ObjectiveRefinementApplyResponse {
  summary: ObjectiveRefinementSummaryResponse;
  objective: Objective;
}

export interface PlanningSessionSummaryRequest {
  messageId?: string;
  title?: string;
}

export interface PlanningSessionSummaryResponse {
  sessionId: string;
  messageId: string;
  title: string;
  description: string;
  method: string;
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
  scope: ScopeEnum;
  fileName: string;
  description: string | null;
  content: string;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export type WorkflowProfileScope = 'Global' | 'Fleet' | 'Vessel';
export type WorkflowInputReferenceProvider =
  | 'EnvironmentVariable'
  | 'FilePath'
  | 'DirectoryPath'
  | 'AwsSecretsManager'
  | 'AzureKeyVaultSecret'
  | 'HashiCorpVault'
  | 'OnePassword';

export interface WorkflowInputReference {
  provider: WorkflowInputReferenceProvider;
  key: string;
  environmentName?: string | null;
  description?: string | null;
}

export interface WorkflowEnvironmentProfile {
  environmentName: string;
  deployCommand: string | null;
  rollbackCommand: string | null;
  smokeTestCommand: string | null;
  healthCheckCommand: string | null;
  deploymentVerificationCommand: string | null;
  rollbackVerificationCommand: string | null;
}

export interface WorkflowProfile {
  id: string;
  tenantId: string | null;
  userId: string | null;
  ownershipScope: ScopeEnum;
  name: string;
  description: string | null;
  scope: WorkflowProfileScope;
  fleetId: string | null;
  vesselId: string | null;
  isDefault: boolean;
  active: boolean;
  languageHints: string[];
  lintCommand: string | null;
  buildCommand: string | null;
  unitTestCommand: string | null;
  integrationTestCommand: string | null;
  e2eTestCommand: string | null;
  migrationCommand: string | null;
  securityScanCommand: string | null;
  performanceCommand: string | null;
  packageCommand: string | null;
  deploymentVerificationCommand: string | null;
  rollbackVerificationCommand: string | null;
  publishArtifactCommand: string | null;
  releaseVersioningCommand: string | null;
  changelogGenerationCommand: string | null;
  requiredSecrets: string[];
  requiredInputs: WorkflowInputReference[];
  expectedArtifacts: string[];
  environments: WorkflowEnvironmentProfile[];
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface WorkflowProfileValidationResult {
  isValid: boolean;
  errors: string[];
  warnings: string[];
  availableCheckTypes: string[];
  commandPreviews: WorkflowProfileCommandPreview[];
}

export type ProjectProfileScope = 'Global' | 'Fleet' | 'Vessel';
export type ProjectProfileResolutionMode = 'Explicit' | 'Vessel' | 'Fleet' | 'Global' | 'None';

export interface PersonaOverride {
  personaName: string;
  promptTemplateName: string | null;
  additionalInstructions: string | null;
  enabled: boolean;
}

export interface ProjectProfile {
  id: string;
  tenantId: string | null;
  userId: string | null;
  ownershipScope: ScopeEnum;
  name: string;
  description: string | null;
  scope: ProjectProfileScope;
  fleetId: string | null;
  vesselId: string | null;
  isDefault: boolean;
  active: boolean;
  defaultPipelineId: string | null;
  workflowProfileId: string | null;
  personaOverrides: PersonaOverride[];
  skills: string[];
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface ProjectProfileValidationResult {
  isValid: boolean;
  errors: string[];
  warnings: string[];
}

export interface ProjectProfileResolutionResult {
  profile: ProjectProfile | null;
  mode: ProjectProfileResolutionMode;
}

export interface PersonaPromptPreview {
  personaName: string;
  baseTemplateName: string;
  effectiveTemplateName: string;
  basePrompt: string;
  effectivePrompt: string;
  additionalInstructions: string | null;
  isOverridden: boolean;
}

export interface Skill {
  id: string;
  tenantId: string | null;
  userId: string | null;
  scope: ScopeEnum;
  name: string;
  description: string | null;
  category: string | null;
  content: string;
  isBuiltIn: boolean;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface CaptainChatMessage {
  role: 'user' | 'assistant';
  content: string;
}

export interface CaptainChatMetrics {
  /** Time to the first output of any kind (reasoning, a tool call, or reply text). */
  timeToFirstTokenMs: number | null;
  streamingMs: number | null;
  totalMs: number | null;
  /** Input tokens (cached included), when the runtime reports usage. */
  promptTokens: number | null;
  completionTokens: number | null;
  totalTokens: number | null;
  tokensPerSecond: number | null;
  /** Time to the first visible reply text, when it differs from the first output. */
  timeToFirstTextMs?: number | null;
  /** Cache-read input tokens (a subset of promptTokens), when reported. */
  cachedTokens?: number | null;
  /** Cost of the turn in US dollars, when the runtime reports it (Claude Code, OpenCode). */
  costUsd?: number | null;
  /** True when completionTokens is an estimate from the reply length rather than the runtime's own count. */
  tokensEstimated?: boolean | null;
  /** Tool calls completed during the turn (Ask turns). */
  toolCallCount?: number | null;
  /** Total time spent in the turn's tool calls, in milliseconds (Ask turns). */
  toolTimeMs?: number | null;
}

export interface Job {
  id: string;
  tenantId: string | null;
  userId: string | null;
  name: string;
  kind: string;
  status: string;
  progress: number;
  resultJson: string | null;
  errorReason: string | null;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  lastUpdateUtc: string;
}

// ==================== Vessel Health (begin) ====================

/** Status of one vessel health criterion or of the vessel overall. */
export type VesselHealthStatus = 'Pass' | 'Warn' | 'Fail' | 'NotApplicable' | 'Unknown';

/** Stable criterion codes; `Overall` is valid only for overrides. */
export type VesselHealthCriterion =
  | 'GitDivergence'
  | 'WorkingTree'
  | 'Branches'
  | 'CommitRecency'
  | 'Dependencies'
  | 'Vulnerabilities'
  | 'TestInfrastructure'
  | 'ContinuousIntegration'
  | 'ArmadaReadiness'
  | 'MissionOutcomes'
  | 'Overall';

export type VulnerabilitySeverity = 'None' | 'Low' | 'Moderate' | 'High' | 'Critical';
export type DependencyDrift = 'None' | 'Patch' | 'Minor' | 'Major';
export type VesselDivergenceFilter = 'Ahead' | 'Behind' | 'Diverged' | 'Even';

export type VesselHealthSortField =
  | 'VesselName'
  | 'FleetName'
  | 'OverallStatus'
  | 'Divergence'
  | 'AheadOfDefault'
  | 'BehindDefault'
  | 'IsDirty'
  | 'BranchCount'
  | 'StaleBranchCount'
  | 'OutdatedCount'
  | 'OutdatedMajorCount'
  | 'VulnerableCount'
  | 'DependencyStatus'
  | 'TestInfraStatus'
  | 'CiStatus'
  | 'LastCommitUtc'
  | 'EvaluatedUtc';

/**
 * One health row (effective statuses, overrides applied). A never-evaluated vessel has no `id`,
 * Unknown statuses, and no measurements. Null properties are omitted by the server.
 */
export interface VesselHealth {
  id?: string;
  tenantId?: string | null;
  vesselId: string;
  overallStatus: VesselHealthStatus;
  evaluatedUtc?: string | null;
  evaluationDurationMs?: number | null;
  errorCode?: string | null;
  evaluatedPath?: string | null;
  currentBranch?: string | null;
  isDirty?: boolean | null;
  untrackedCount?: number | null;
  aheadOfDefault?: number | null;
  behindDefault?: number | null;
  aheadOfUpstream?: number | null;
  behindUpstream?: number | null;
  lastCommitUtc?: string | null;
  branchCount?: number | null;
  staleBranchCount?: number | null;
  armadaBranchCount?: number | null;
  primaryLanguage?: string | null;
  projectCount?: number | null;
  outdatedCount?: number | null;
  outdatedMajorCount?: number | null;
  vulnerableCount?: number | null;
  maxVulnerabilitySeverity?: VulnerabilitySeverity;
  dependencyStatus: VesselHealthStatus;
  vulnerabilityStatus: VesselHealthStatus;
  testInfraStatus: VesselHealthStatus;
  ciStatus: VesselHealthStatus;
  divergenceStatus: VesselHealthStatus;
  workingTreeStatus: VesselHealthStatus;
  branchStatus: VesselHealthStatus;
  readinessStatus: VesselHealthStatus;
  missionOutcomeStatus: VesselHealthStatus;
  lastCheckRunStatus?: string | null;
  hasCiConfig?: boolean | null;
  hasLicense?: boolean | null;
  hasReadme?: boolean | null;
  readinessErrorCount?: number | null;
  recentMissionFailureCount?: number | null;
  manifestHash?: string | null;
  dependenciesEvaluatedUtc?: string | null;
  createdUtc?: string;
  lastUpdateUtc?: string;
  vesselName?: string | null;
  fleetId?: string | null;
  fleetName?: string | null;
}

/** Raw evaluated finding for one criterion. */
export interface VesselHealthFinding {
  id?: string;
  tenantId?: string | null;
  vesselId: string;
  criterion: VesselHealthCriterion;
  status: VesselHealthStatus;
  detailCode?: string | null;
  valueA?: number | null;
  valueB?: number | null;
  evaluatedUtc?: string;
  createdUtc?: string;
  lastUpdateUtc?: string;
}

/** One outdated and/or vulnerable package. */
export interface VesselDependency {
  id?: string;
  tenantId?: string | null;
  vesselId: string;
  ecosystem: string;
  projectPath?: string | null;
  packageName: string;
  currentVersion?: string | null;
  latestVersion?: string | null;
  drift?: DependencyDrift;
  isVulnerable?: boolean;
  severity?: VulnerabilitySeverity;
  advisoryUrl?: string | null;
  createdUtc?: string;
  lastUpdateUtc?: string;
}

/** Manual status for one criterion (or Overall). */
export interface VesselHealthOverride {
  id?: string;
  tenantId?: string | null;
  vesselId: string;
  userId?: string | null;
  criterion: VesselHealthCriterion;
  status: VesselHealthStatus;
  note?: string | null;
  createdUtc?: string;
  lastUpdateUtc?: string;
}

/** GET /api/v1/vessels/{id}/health response. */
export interface VesselHealthDetail {
  health: VesselHealth;
  findings: VesselHealthFinding[];
  dependencies: VesselDependency[];
  overrides: VesselHealthOverride[];
}

/** GET /api/v1/vessel-health/summary response. */
export interface VesselHealthSummary {
  totalVessels: number;
  pass: number;
  warn: number;
  fail: number;
  unknown: number;
  notApplicable: number;
  notEvaluated: number;
  outdatedMajorVessels: number;
  highOrCriticalVulnerabilityVessels: number;
}

/** POST /api/v1/vessel-health/enumerate body (sent with PascalCase keys, every field optional). */
export interface VesselHealthEnumerateRequest {
  PageNumber?: number;
  PageSize?: number;
  SortBy?: VesselHealthSortField;
  SortDescending?: boolean;
  NameContains?: string;
  FleetId?: string;
  PrimaryLanguage?: string;
  CurrentBranchContains?: string;
  OverallStatus?: VesselHealthStatus[];
  DependencyStatus?: VesselHealthStatus[];
  TestInfraStatus?: VesselHealthStatus[];
  IsDirty?: boolean;
  HasCiConfig?: boolean;
  Divergence?: VesselDivergenceFilter;
  MinBranchCount?: number;
  MaxBranchCount?: number;
  LastCommitAfterUtc?: string;
  LastCommitBeforeUtc?: string;
  IncludeInactive?: boolean;
}

/** POST /api/v1/vessel-health/evaluate body. */
export interface VesselHealthEvaluateRequest {
  VesselIds?: string[];
  FleetId?: string;
  Force?: boolean;
}

/** 202 (started) or 409 (already running) response of the evaluate route. */
export interface VesselHealthEvaluationStart {
  jobId: string;
  alreadyRunning: boolean;
  vesselCount: number;
}

/** RepositoryHealth.Thresholds in server settings. */
export interface RepositoryHealthThresholds {
  behindWarn: number;
  behindFail: number;
  staleBranchWarn: number;
  staleBranchFail: number;
  missionFailureWarn: number;
  missionFailureFail: number;
}

/** RepositoryHealth section of server settings. */
export interface RepositoryHealthSettings {
  intervalMinutes: number;
  maxConcurrency: number;
  fetchBeforeEvaluate: boolean;
  dependencyMaxAgeHours: number;
  dependencyCommandTimeoutSeconds: number;
  staleBranchDays: number;
  missionWindowDays: number;
  scoredCriteria: VesselHealthCriterion[];
  thresholds: RepositoryHealthThresholds;
}

// ==================== Vessel Health (end) ====================

export interface CaptainChatRequest {
  message: string;
  history: CaptainChatMessage[];
  turnId?: string;
  showThinking?: boolean;
}

export interface CaptainChatResponse {
  success: boolean;
  reply: string;
  model: string | null;
  metrics: CaptainChatMetrics;
  error: string | null;
  thinking?: string | null;
}

export interface WorkspaceExecRequest {
  command: string;
  timeoutSeconds?: number;
}

export interface WorkspaceExecResult {
  command: string;
  workingDirectory: string;
  exitCode: number;
  stdout: string;
  stderr: string;
  timedOut: boolean;
  durationMs: number;
}

export interface WorkspaceDiffResult {
  path: string | null;
  diff: string;
  error: string | null;
}

export type InboxSeverity = 'Info' | 'Warning' | 'Critical';

export interface InboxItem {
  kind: string;
  severity: InboxSeverity;
  title: string;
  detail: string;
  entityType: string | null;
  entityId: string | null;
  /** Display name of the entity (mission title, captain name, deployment environment, merge target branch). */
  entityName?: string | null;
  /** Deployment items: the environment name (builds the "Deploy to {environment}: {title}" approval label). */
  environmentName?: string | null;
  /** Deployment items: the deployment title. */
  deploymentTitle?: string | null;
  /** cli_permission items: the pending CLI permission request (with the caller's canDecide / canRemember). */
  cliPermission?: CliPermissionRequest | null;
  /** cli_permission items: when the pending request expires (UTC). */
  expiresUtc?: string | null;
  href: string;
}

export type WorkflowProfileResolutionMode = 'Explicit' | 'Vessel' | 'Fleet' | 'Global';

export interface WorkflowProfileCommandPreview {
  checkType: CheckRunType;
  environmentName: string | null;
  command: string;
}

export interface WorkflowProfileResolutionPreviewResult {
  resolvedProfile: WorkflowProfile | null;
  resolutionMode: WorkflowProfileResolutionMode;
  availableCheckTypes: string[];
  commandPreviews: WorkflowProfileCommandPreview[];
}

export type CheckRunType =
  | 'Lint'
  | 'Build'
  | 'UnitTest'
  | 'IntegrationTest'
  | 'E2ETest'
  | 'Migration'
  | 'SecurityScan'
  | 'Performance'
  | 'Package'
  | 'DeploymentVerification'
  | 'RollbackVerification'
  | 'PublishArtifact'
  | 'ReleaseVersioning'
  | 'Changelog'
  | 'Deploy'
  | 'Rollback'
  | 'SmokeTest'
  | 'HealthCheck'
  | 'Custom';

export type CheckRunStatus = 'Pending' | 'Running' | 'Passed' | 'Failed' | 'Canceled';
export type CheckRunSource = 'Armada' | 'External';

export interface CheckRunArtifact {
  path: string;
  sizeBytes: number;
  lastWriteUtc: string;
}

export interface CheckRunTestSummary {
  format: string | null;
  total: number | null;
  passed: number | null;
  failed: number | null;
  skipped: number | null;
  durationMs: number | null;
}

export interface CheckRunCoverageMetric {
  covered: number | null;
  total: number | null;
  percentage: number | null;
}

export interface CheckRunCoverageSummary {
  format: string | null;
  sourcePath: string | null;
  lines: CheckRunCoverageMetric | null;
  branches: CheckRunCoverageMetric | null;
  functions: CheckRunCoverageMetric | null;
  statements: CheckRunCoverageMetric | null;
}

export interface CheckRun {
  id: string;
  tenantId: string | null;
  userId: string | null;
  workflowProfileId: string | null;
  vesselId: string | null;
  missionId: string | null;
  voyageId: string | null;
  deploymentId: string | null;
  label: string | null;
  type: CheckRunType;
  source: CheckRunSource;
  status: CheckRunStatus;
  providerName: string | null;
  externalId: string | null;
  externalUrl: string | null;
  environmentName: string | null;
  command: string;
  workingDirectory: string | null;
  branchName: string | null;
  commitHash: string | null;
  exitCode: number | null;
  output: string | null;
  summary: string | null;
  testSummary: CheckRunTestSummary | null;
  coverageSummary: CheckRunCoverageSummary | null;
  artifacts: CheckRunArtifact[];
  durationMs: number | null;
  startedUtc: string | null;
  completedUtc: string | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface CheckRunRequest {
  vesselId: string;
  workflowProfileId?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
  deploymentId?: string | null;
  type: CheckRunType;
  environmentName?: string | null;
  label?: string | null;
  branchName?: string | null;
  commitHash?: string | null;
  commandOverride?: string | null;
}

export interface CheckRunImportRequest {
  vesselId: string;
  workflowProfileId?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
  deploymentId?: string | null;
  type: CheckRunType;
  status: CheckRunStatus;
  providerName?: string | null;
  externalId?: string | null;
  externalUrl?: string | null;
  environmentName?: string | null;
  label?: string | null;
  branchName?: string | null;
  commitHash?: string | null;
  command?: string | null;
  summary?: string | null;
  output?: string | null;
  exitCode?: number | null;
  testSummary?: CheckRunTestSummary | null;
  coverageSummary?: CheckRunCoverageSummary | null;
  artifacts?: CheckRunArtifact[];
  durationMs?: number | null;
  startedUtc?: string | null;
  completedUtc?: string | null;
}

export type ReleaseStatus = 'Draft' | 'Candidate' | 'Shipped' | 'Failed' | 'RolledBack';

export type EnvironmentKind = 'Development' | 'Test' | 'Staging' | 'Production' | 'CustomerHosted' | 'Custom';

export interface DeploymentVerificationDefinition {
  id: string;
  name: string;
  method: string;
  path: string;
  requestBody: string | null;
  headers: Record<string, string>;
  expectedStatusCode: number | null;
  mustContainText: string | null;
  active: boolean;
}

export interface DeploymentEnvironment {
  id: string;
  tenantId: string | null;
  userId: string | null;
  vesselId: string | null;
  name: string;
  description: string | null;
  kind: EnvironmentKind;
  configurationSource: string | null;
  baseUrl: string | null;
  healthEndpoint: string | null;
  accessNotes: string | null;
  deploymentRules: string | null;
  verificationDefinitions: DeploymentVerificationDefinition[];
  rolloutMonitoringWindowMinutes: number;
  rolloutMonitoringIntervalSeconds: number;
  alertOnRegression: boolean;
  requiresApproval: boolean;
  isDefault: boolean;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface DeploymentEnvironmentQuery {
  tenantId?: string | null;
  userId?: string | null;
  vesselId?: string | null;
  kind?: EnvironmentKind | null;
  isDefault?: boolean | null;
  active?: boolean | null;
  search?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface DeploymentEnvironmentUpsertRequest {
  vesselId?: string | null;
  name?: string | null;
  description?: string | null;
  kind?: EnvironmentKind | null;
  configurationSource?: string | null;
  baseUrl?: string | null;
  healthEndpoint?: string | null;
  accessNotes?: string | null;
  deploymentRules?: string | null;
  verificationDefinitions?: DeploymentVerificationDefinition[] | null;
  rolloutMonitoringWindowMinutes?: number | null;
  rolloutMonitoringIntervalSeconds?: number | null;
  alertOnRegression?: boolean | null;
  requiresApproval?: boolean | null;
  isDefault?: boolean | null;
  active?: boolean | null;
}

export type DeploymentStatus =
  | 'PendingApproval'
  | 'Running'
  | 'Succeeded'
  | 'VerificationFailed'
  | 'Failed'
  | 'Denied'
  | 'RollingBack'
  | 'RolledBack';

export type DeploymentVerificationStatus =
  | 'NotRun'
  | 'Running'
  | 'Passed'
  | 'Failed'
  | 'Partial'
  | 'Skipped';

export interface Deployment {
  id: string;
  tenantId: string | null;
  userId: string | null;
  vesselId: string | null;
  workflowProfileId: string | null;
  environmentId: string | null;
  environmentName: string | null;
  releaseId: string | null;
  missionId: string | null;
  voyageId: string | null;
  title: string;
  sourceRef: string | null;
  summary: string | null;
  notes: string | null;
  status: DeploymentStatus;
  verificationStatus: DeploymentVerificationStatus;
  approvalRequired: boolean;
  approvedByUserId: string | null;
  approvedUtc: string | null;
  approvalComment: string | null;
  deployCheckRunId: string | null;
  smokeTestCheckRunId: string | null;
  healthCheckRunId: string | null;
  deploymentVerificationCheckRunId: string | null;
  rollbackCheckRunId: string | null;
  rollbackVerificationCheckRunId: string | null;
  checkRunIds: string[];
  requestHistorySummary: RequestHistorySummaryResult | null;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  verifiedUtc: string | null;
  rolledBackUtc: string | null;
  monitoringWindowEndsUtc: string | null;
  lastMonitoredUtc: string | null;
  lastRegressionAlertUtc: string | null;
  latestMonitoringSummary: string | null;
  monitoringFailureCount: number;
  lastUpdateUtc: string;
}

export interface DeploymentQuery {
  tenantId?: string | null;
  userId?: string | null;
  vesselId?: string | null;
  workflowProfileId?: string | null;
  environmentId?: string | null;
  environmentName?: string | null;
  releaseId?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
  checkRunId?: string | null;
  status?: DeploymentStatus | null;
  verificationStatus?: DeploymentVerificationStatus | null;
  search?: string | null;
  fromUtc?: string | null;
  toUtc?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface DeploymentUpsertRequest {
  vesselId?: string | null;
  workflowProfileId?: string | null;
  environmentId?: string | null;
  environmentName?: string | null;
  releaseId?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
  objectiveIds?: string[];
  title?: string | null;
  sourceRef?: string | null;
  summary?: string | null;
  notes?: string | null;
  autoExecute?: boolean | null;
}

export interface ReleaseArtifact {
  sourceType: string;
  sourceId: string | null;
  path: string;
  sizeBytes: number;
  lastWriteUtc: string | null;
}

export interface Release {
  id: string;
  tenantId: string | null;
  userId: string | null;
  vesselId: string | null;
  workflowProfileId: string | null;
  title: string;
  version: string | null;
  tagName: string | null;
  summary: string | null;
  notes: string | null;
  status: ReleaseStatus;
  voyageIds: string[];
  missionIds: string[];
  checkRunIds: string[];
  artifacts: ReleaseArtifact[];
  createdUtc: string;
  lastUpdateUtc: string;
  publishedUtc: string | null;
}

export interface ReleaseQuery {
  tenantId?: string | null;
  userId?: string | null;
  vesselId?: string | null;
  workflowProfileId?: string | null;
  voyageId?: string | null;
  missionId?: string | null;
  checkRunId?: string | null;
  status?: ReleaseStatus | null;
  search?: string | null;
  fromUtc?: string | null;
  toUtc?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface ReleaseUpsertRequest {
  vesselId?: string | null;
  workflowProfileId?: string | null;
  title?: string | null;
  version?: string | null;
  tagName?: string | null;
  summary?: string | null;
  notes?: string | null;
  status?: ReleaseStatus | null;
  voyageIds?: string[];
  missionIds?: string[];
  checkRunIds?: string[];
  objectiveIds?: string[];
}

export type IncidentStatus = 'Open' | 'Monitoring' | 'Mitigated' | 'RolledBack' | 'Closed';
export type IncidentSeverity = 'Critical' | 'High' | 'Medium' | 'Low';

export interface Incident {
  id: string;
  tenantId: string | null;
  userId: string | null;
  title: string;
  summary: string | null;
  status: IncidentStatus;
  severity: IncidentSeverity;
  environmentId: string | null;
  environmentName: string | null;
  deploymentId: string | null;
  releaseId: string | null;
  vesselId: string | null;
  missionId: string | null;
  voyageId: string | null;
  rollbackDeploymentId: string | null;
  impact: string | null;
  rootCause: string | null;
  recoveryNotes: string | null;
  postmortem: string | null;
  failureKind: string | null;
  recoveryAttempts: number;
  rescueMissionIds: string[];
  detectedUtc: string;
  mitigatedUtc: string | null;
  closedUtc: string | null;
  lastUpdateUtc: string;
}

export interface IncidentQuery {
  tenantId?: string | null;
  userId?: string | null;
  vesselId?: string | null;
  environmentId?: string | null;
  deploymentId?: string | null;
  releaseId?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
  status?: IncidentStatus | null;
  severity?: IncidentSeverity | null;
  search?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface IncidentUpsertRequest {
  title?: string | null;
  summary?: string | null;
  status?: IncidentStatus | null;
  severity?: IncidentSeverity | null;
  environmentId?: string | null;
  environmentName?: string | null;
  deploymentId?: string | null;
  releaseId?: string | null;
  vesselId?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
  rollbackDeploymentId?: string | null;
  objectiveIds?: string[];
  impact?: string | null;
  rootCause?: string | null;
  recoveryNotes?: string | null;
  postmortem?: string | null;
  detectedUtc?: string | null;
  mitigatedUtc?: string | null;
  closedUtc?: string | null;
}

export type RunbookExecutionStatus = 'Running' | 'Completed' | 'Cancelled';

export interface RunbookParameter {
  name: string;
  label: string | null;
  description: string | null;
  defaultValue: string | null;
  required: boolean;
}

export interface RunbookStep {
  id: string;
  title: string;
  instructions: string;
}

export interface Runbook {
  id: string;
  playbookId: string;
  tenantId: string | null;
  userId: string | null;
  scope: ScopeEnum;
  fileName: string;
  title: string;
  description: string | null;
  workflowProfileId: string | null;
  environmentId: string | null;
  environmentName: string | null;
  defaultCheckType: CheckRunType | null;
  parameters: RunbookParameter[];
  steps: RunbookStep[];
  overviewMarkdown: string;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface RunbookQuery {
  workflowProfileId?: string | null;
  environmentId?: string | null;
  defaultCheckType?: CheckRunType | null;
  active?: boolean | null;
  search?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface RunbookUpsertRequest {
  fileName?: string | null;
  title?: string | null;
  description?: string | null;
  workflowProfileId?: string | null;
  environmentId?: string | null;
  environmentName?: string | null;
  defaultCheckType?: CheckRunType | null;
  parameters?: RunbookParameter[] | null;
  steps?: RunbookStep[] | null;
  overviewMarkdown?: string | null;
  active?: boolean | null;
  scope?: ScopeEnum | null;
}

export interface RunbookExecution {
  id: string;
  runbookId: string;
  playbookId: string;
  tenantId: string | null;
  userId: string | null;
  title: string;
  status: RunbookExecutionStatus;
  workflowProfileId: string | null;
  environmentId: string | null;
  environmentName: string | null;
  checkType: CheckRunType | null;
  deploymentId: string | null;
  incidentId: string | null;
  parameterValues: Record<string, string>;
  completedStepIds: string[];
  stepNotes: Record<string, string>;
  notes: string | null;
  startedUtc: string;
  completedUtc: string | null;
  lastUpdateUtc: string;
}

export interface RunbookExecutionQuery {
  runbookId?: string | null;
  deploymentId?: string | null;
  incidentId?: string | null;
  status?: RunbookExecutionStatus | null;
  search?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface RunbookExecutionStartRequest {
  title?: string | null;
  workflowProfileId?: string | null;
  environmentId?: string | null;
  environmentName?: string | null;
  checkType?: CheckRunType | null;
  parameterValues?: Record<string, string> | null;
  deploymentId?: string | null;
  incidentId?: string | null;
  notes?: string | null;
}

export interface RunbookExecutionUpdateRequest {
  status?: RunbookExecutionStatus | null;
  completedStepIds?: string[] | null;
  stepNotes?: Record<string, string> | null;
  notes?: string | null;
}

export type ReadinessSeverity = 'Info' | 'Warning' | 'Error';

export interface VesselReadinessIssue {
  code: string;
  severity: ReadinessSeverity;
  title: string;
  message: string;
  relatedValue: string | null;
  /** Provider of the workflow input the issue is about; null (or absent from older servers) otherwise. */
  inputProvider?: WorkflowInputReferenceProvider | null;
}

export interface VesselToolchainProbe {
  name: string;
  command: string;
  version: string | null;
  available: boolean;
  expected: boolean;
  evidence: string | null;
}

export interface VesselDeploymentMetadata {
  environmentCount: number;
  hasDeployCommand: boolean;
  hasRollbackCommand: boolean;
  hasSmokeTestCommand: boolean;
  hasHealthCheckCommand: boolean;
  hasDeploymentVerificationCommand: boolean;
  hasRollbackVerificationCommand: boolean;
}

/** Why no checkout of a vessel is available (no working directory on the Admiral, and no connected Harbor serves it). */
export type VesselCheckoutErrorCode = 'NoHarborConnected' | 'NoHarborCheckout';

export interface VesselReadinessResult {
  vesselId: string;
  hasWorkingDirectory: boolean;
  /** Where checks, Workspace, and readiness probes run: the Admiral's working directory or the Harbor's checkout. */
  checkoutPath: string | null;
  /** The Harbor that has the checkout, or null for the Admiral host (or when there is none). */
  harborId: string | null;
  /** That Harbor's name, or null. */
  harborName: string | null;
  /** Why there is no checkout, or null when there is one (or Harbors were not considered). */
  checkoutErrorCode: VesselCheckoutErrorCode | null;
  hasRepositoryContext: boolean;
  workflowProfileId: string | null;
  workflowProfileName: string | null;
  workflowProfileScope: WorkflowProfileScope | null;
  requestedCheckType: CheckRunType | null;
  requestedEnvironmentName: string | null;
  availableCheckTypes: string[];
  currentBranch: string | null;
  hasUncommittedChanges: boolean | null;
  isDetachedHead: boolean | null;
  commitsAhead: number | null;
  commitsBehind: number | null;
  detectedToolchains: string[];
  toolchainProbes: VesselToolchainProbe[];
  deploymentEnvironments: string[];
  deploymentMetadata: VesselDeploymentMetadata | null;
  setupChecklist: VesselSetupChecklistItem[];
  issues: VesselReadinessIssue[];
  setupChecklistSatisfiedCount: number;
  setupChecklistTotalCount: number;
  errorCount: number;
  warningCount: number;
  isReady: boolean;
}

export interface VesselSetupChecklistItem {
  code: string;
  severity: ReadinessSeverity;
  title: string;
  message: string;
  isSatisfied: boolean;
  actionLabel: string | null;
  actionRoute: string | null;
}

export interface LandingPreviewIssue {
  code: string;
  severity: ReadinessSeverity;
  title: string;
  message: string;
}

export interface LandingPreviewResult {
  vesselId: string;
  missionId: string | null;
  sourceBranch: string | null;
  targetBranch: string;
  branchCategory: string;
  targetBranchProtected: boolean;
  protectedBranchMatch: string | null;
  landingMode: string | null;
  branchCleanupPolicy: string | null;
  requirePassingChecksToLand: boolean;
  requirePullRequestForProtectedBranches: boolean;
  requireMergeQueueForReleaseBranches: boolean;
  expectedLandingAction: string | null;
  hasPassingChecks: boolean;
  latestCheckRunId: string | null;
  latestCheckStatus: CheckRunStatus | null;
  latestCheckSummary: string | null;
  isReadyToLand: boolean;
  issues: LandingPreviewIssue[];
  /** Mission previews: the landing mode that applies (voyage, then vessel, then Admiral default). */
  effectiveLandingMode?: string | null;
  /** Mission previews: true when the effective landing mode is None (merge by hand; Land cannot succeed). */
  manualLandingOnly?: boolean;
  /** Mission previews: the mission's status when the preview was computed. */
  missionStatus?: string | null;
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
  gitAnchorsJson?: string | null;
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

export interface FormattedLogEntry {
  text: string;
  isToolCall: boolean;
  toolName: string | null;
  redacted: boolean;
  truncated: boolean;
}

export interface LogResult {
  log: string;
  lines: number;
  totalLines: number;
  entries?: FormattedLogEntry[];
}

export interface InstructionsResult {
  fileName: string;
  content: string;
}

export interface MuxCaptainOptions {
  schemaVersion: number;
  configDirectory: string | null;
  endpoint: string | null;
  baseUrl: string | null;
  adapterType: string | null;
  temperature: number | null;
  maxTokens: number | null;
  systemPromptPath: string | null;
  approvalPolicy: string | null;
}

export interface MuxEndpointInfo {
  name: string;
  adapterType: string;
  baseUrl: string;
  model: string;
  isDefault: boolean;
  maxTokens: number;
  temperature: number;
  contextWindow: number;
  timeoutMs: number;
  toolsEnabled: boolean;
  headerNames: string[];
  headers: Record<string, string>;
}

export interface MuxEndpointListResult {
  contractVersion: number;
  success: boolean;
  configDirectory: string;
  errorCode: string;
  errorMessage: string;
  endpoints: MuxEndpointInfo[];
}

export interface MuxEndpointShowResult {
  contractVersion: number;
  success: boolean;
  configDirectory: string;
  errorCode: string;
  errorMessage: string;
  endpoint: MuxEndpointInfo | null;
}

export interface WorkspaceTreeEntry {
  name: string;
  relativePath: string;
  isDirectory: boolean;
  isEditable: boolean;
  sizeBytes: number | null;
  lastWriteUtc: string;
}

export interface WorkspaceTreeResult {
  vesselId: string;
  rootPath: string;
  currentPath: string;
  parentPath: string | null;
  entries: WorkspaceTreeEntry[];
}

export interface WorkspaceFileResponse {
  vesselId: string;
  path: string;
  name: string;
  content: string;
  contentHash: string;
  isEditable: boolean;
  isBinary: boolean;
  isLarge: boolean;
  previewTruncated: boolean;
  sizeBytes: number;
  lastWriteUtc: string;
  language: string;
}

export interface WorkspaceSaveRequest {
  path: string;
  content: string;
  expectedHash?: string | null;
}

export interface WorkspaceSaveResult {
  path: string;
  contentHash: string;
  sizeBytes: number;
  lastWriteUtc: string;
  created: boolean;
}

export interface WorkspaceCreateDirectoryRequest {
  path: string;
}

export interface WorkspaceRenameRequest {
  path: string;
  newPath: string;
}

export interface WorkspaceOperationResult {
  path: string;
  newPath?: string | null;
  status: string;
}

export interface WorkspaceSearchMatch {
  path: string;
  lineNumber: number;
  preview: string;
}

export interface WorkspaceSearchResult {
  query: string;
  totalMatches: number;
  truncated: boolean;
  matches: WorkspaceSearchMatch[];
}

export interface WorkspaceChangeEntry {
  path: string;
  status: string;
  originalPath?: string | null;
}

export interface WorkspaceChangesResult {
  branchName: string;
  isDirty: boolean;
  commitsAhead: number;
  commitsBehind: number;
  changes: WorkspaceChangeEntry[];
  error?: string | null;
}

export interface WorkspaceActiveMission {
  missionId: string;
  title: string;
  status: string;
  scopedFiles: string[];
}

export interface WorkspaceStatusResult {
  vesselId: string;
  hasWorkingDirectory: boolean;
  rootPath?: string | null;
  branchName?: string | null;
  isDirty: boolean;
  commitsAhead?: number | null;
  commitsBehind?: number | null;
  activeMissionCount: number;
  activeMissions: WorkspaceActiveMission[];
  error?: string | null;
}

export interface RequestHistoryEntry {
  id: string;
  tenantId: string | null;
  userId: string | null;
  credentialId: string | null;
  principalDisplay: string | null;
  authMethod: string | null;
  method: string;
  route: string;
  routeTemplate: string | null;
  queryString: string | null;
  statusCode: number;
  durationMs: number;
  requestSizeBytes: number;
  responseSizeBytes: number;
  requestContentType: string | null;
  responseContentType: string | null;
  isSuccess: boolean;
  clientIp: string | null;
  correlationId: string | null;
  createdUtc: string;
}

export interface RequestHistoryDetail {
  requestHistoryId: string;
  pathParamsJson: string | null;
  queryParamsJson: string | null;
  requestHeadersJson: string | null;
  responseHeadersJson: string | null;
  requestBodyText: string | null;
  responseBodyText: string | null;
  requestBodyTruncated: boolean;
  responseBodyTruncated: boolean;
}

export interface RequestHistoryRecord {
  entry: RequestHistoryEntry;
  detail: RequestHistoryDetail | null;
}

export interface RequestHistorySummaryBucket {
  bucketStartUtc: string;
  bucketEndUtc: string;
  totalCount: number;
  successCount: number;
  failureCount: number;
  averageDurationMs: number;
}

export interface RequestHistorySummaryResult {
  totalCount: number;
  successCount: number;
  failureCount: number;
  successRate: number;
  averageDurationMs: number;
  fromUtc: string | null;
  toUtc: string | null;
  bucketMinutes: number;
  buckets: RequestHistorySummaryBucket[];
}

export interface RequestHistoryQuery {
  tenantId?: string | null;
  userId?: string | null;
  credentialId?: string | null;
  principal?: string | null;
  method?: string | null;
  route?: string | null;
  statusCode?: number | null;
  isSuccess?: boolean | null;
  fromUtc?: string | null;
  toUtc?: string | null;
  pageNumber?: number;
  pageSize?: number;
  bucketMinutes?: number;
}

/** SourceType values written by the server's HistoricalTimelineService. */
export type HistoricalTimelineSourceType =
  | 'Objective'
  | 'ObjectiveRefinementSession'
  | 'Mission'
  | 'Voyage'
  | 'Planning'
  | 'MergeEntry'
  | 'CheckRun'
  | 'Release'
  | 'Deployment'
  | 'Incident'
  | 'RunbookExecution'
  | 'Event'
  | 'Request';

export interface HistoricalTimelineEntry {
  id: string;
  sourceType: HistoricalTimelineSourceType;
  sourceId: string;
  entityType: string | null;
  entityId: string | null;
  objectiveId: string | null;
  vesselId: string | null;
  environmentId: string | null;
  deploymentId: string | null;
  incidentId: string | null;
  missionId: string | null;
  voyageId: string | null;
  actorId: string | null;
  actorDisplay: string | null;
  title: string;
  description: string | null;
  status: string | null;
  severity: string | null;
  route: string | null;
  occurredUtc: string;
  metadataJson: string | null;
}

export interface HistoricalTimelineQuery {
  tenantId?: string | null;
  userId?: string | null;
  objectiveId?: string | null;
  vesselId?: string | null;
  environmentId?: string | null;
  deploymentId?: string | null;
  incidentId?: string | null;
  postmortemOnly?: boolean;
  /** When true, GET/HEAD/OPTIONS request-history entries are left out (dashboard polling). */
  excludeReadRequests?: boolean;
  missionId?: string | null;
  voyageId?: string | null;
  actor?: string | null;
  text?: string | null;
  sourceTypes?: string[];
  fromUtc?: string | null;
  toUtc?: string | null;
  pageNumber?: number;
  pageSize?: number;
}

export interface DispatchRequest {
  vesselId: string;
  title: string;
  description?: string;
  priority?: number;
}

export interface PlanningSessionCreateRequest {
  title?: string;
  captainId: string;
  vesselId: string;
  fleetId?: string;
  pipelineId?: string;
  selectedPlaybooks?: SelectedPlaybook[];
  objectiveId?: string;
}

export interface PlanningSessionMessageRequest {
  content: string;
  showThinking?: boolean;
  stream?: boolean;
}

export interface PlanningSessionDispatchRequest {
  messageId?: string;
  title?: string;
  description?: string;
}

export interface VoyageCreateRequest {
  title: string;
  description?: string;
  vesselId?: string;
  pipelineId?: string;
  pipeline?: string;
  missions: DispatchRequest[];
  selectedPlaybooks?: SelectedPlaybook[];
  objectiveId?: string;
  captainAssignments?: CaptainAssignmentOverride[];
  /** Landing mode for the voyage's missions; omitted to inherit the vessel's mode, then the global default. */
  landingMode?: string;
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
  data?: unknown;
  message?: string;
  timestamp?: string;
}

export interface PromptTemplate {
  id: string;
  tenantId: string | null;
  userId: string | null;
  scope: ScopeEnum;
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
  userId: string | null;
  scope: ScopeEnum;
  name: string;
  description: string | null;
  promptTemplateName: string;
  isBuiltIn: boolean;
  active: boolean;
  defaultCaptainId?: string | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface Pipeline {
  id: string;
  tenantId: string | null;
  userId: string | null;
  scope: ScopeEnum;
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
  requiresReview: boolean;
  reviewDenyAction: 'RetryStage' | 'FailPipeline';
}

export type EntityType = 'fleets' | 'vessels' | 'captains' | 'missions' | 'voyages' | 'signals' | 'events' | 'docks' | 'merge-queue' | 'personas' | 'prompt-templates' | 'pipelines' | 'playbooks' | 'releases' | 'environments' | 'deployments' | 'incidents' | 'runbooks';

export type HarborConnectionStatus = 'Unknown' | 'Connected' | 'Degraded' | 'Disconnected';

export interface HarborCapability {
  name: string;
  available: boolean;
  detail: string | null;
}

export interface Harbor {
  id: string;
  tenantId: string | null;
  userId: string | null;
  name: string;
  capabilities: HarborCapability[];
  connectionStatus: HarborConnectionStatus;
  maxConcurrentJobs: number;
  enabled: boolean;
  protocolVersion: string | null;
  osPlatform: string | null;
  architecture: string | null;
  lastSeenUtc: string | null;
  lastConnectedUtc: string | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export type HarborMetricsRange = '1h' | '24h' | '7d';
export type HarborLinkSegmentState = 'Unknown' | 'Connected' | 'Reconnecting' | 'Down';

export interface HarborJobBucket {
  bucketStartUtc: string;
  missionsFinished: number;
  missionsFailed: number;
  interactiveFinished: number;
  interactiveFailed: number;
}

export interface HarborJobMetrics {
  buckets: HarborJobBucket[];
  missionsFinished: number;
  missionsFailed: number;
  interactiveFinished: number;
  interactiveFailed: number;
  running: number;
}

export interface HarborConcurrencyBucket {
  bucketStartUtc: string;
  peak: number;
  average: number;
}

export interface HarborSlotMetrics {
  maxConcurrentJobs: number;
  buckets: HarborConcurrencyBucket[];
  peak: number;
  average: number;
}

export interface HarborLaunchSpeed {
  runtime: string;
  jobCount: number;
  firstOutputCount: number;
  firstOutputMedianMs: number | null;
  firstOutputP95Ms: number | null;
  durationCount: number;
  durationMedianMs: number | null;
  durationP95Ms: number | null;
  firstOutputMedianMsByBucket: (number | null)[];
}

export interface HarborLinkSegment {
  state: HarborLinkSegmentState;
  startUtc: string;
  endUtc: string;
}

export interface HarborRoundTripBucket {
  bucketStartUtc: string;
  heartbeatCount: number;
  sampleCount: number;
  averageMs: number | null;
  maxMs: number | null;
}

export interface HarborLinkMetrics {
  segments: HarborLinkSegment[];
  roundTrip: HarborRoundTripBucket[];
  connectedPercent: number | null;
  disconnects: number;
  reconnectCount: number | null;
  lastReconnectUtc: string | null;
  roundTripMedianMs: number | null;
}

export interface HarborTokenSeries {
  runtime: string;
  model: string;
  inputTokens: number;
  outputTokens: number;
  cachedTokens: number;
  totalTokens: number;
}

export interface HarborTokenBucket {
  bucketStartUtc: string;
  inputTokens: number;
  outputTokens: number;
  cachedTokens: number;
  totalTokens: number;
  series: HarborTokenSeries[];
}

export interface HarborTokenMetrics {
  buckets: HarborTokenBucket[];
  series: HarborTokenSeries[];
  inputTokens: number;
  outputTokens: number;
  cachedTokens: number;
  totalTokens: number;
  recordCount: number;
  estimatedCount: number;
}

/** GET /api/v1/harbors/{id}/metrics: every series shares the same buckets (fromUtc, bucketMinutes, bucketCount). */
export interface HarborMetrics {
  harborId: string;
  harborName: string;
  range: HarborMetricsRange;
  fromUtc: string;
  toUtc: string;
  bucketMinutes: number;
  bucketCount: number;
  generatedUtc: string;
  connectionStatus: HarborConnectionStatus;
  jobs: HarborJobMetrics;
  slots: HarborSlotMetrics;
  launchSpeed: HarborLaunchSpeed[];
  link: HarborLinkMetrics;
  tokens: HarborTokenMetrics;
}

export type ModelEndpointKind = 'Embedding' | 'Inference';
export type ModelProvider = 'Ollama' | 'OpenAI' | 'OpenAICompatible' | 'Anthropic' | 'Gemini' | 'VoyageAI' | 'AzureOpenAI' | 'VertexAI' | 'Bedrock';
export type EndpointHealthStatus = 'Unknown' | 'Healthy' | 'Unhealthy';

export interface ModelEndpointHealthRecord {
  timestampUtc: string;
  success: boolean;
}

export interface ModelEndpoint {
  id: string;
  tenantId: string | null;
  userId: string | null;
  scope: ScopeEnum;
  name: string;
  kind: ModelEndpointKind;
  provider: ModelProvider;
  baseUrl: string;
  model: string | null;
  region: string | null;
  project: string | null;
  apiVersion: string | null;
  accessKeyId: string | null;
  dimensionality: number;
  timeoutMs: number;
  enabled: boolean;
  hasApiKey: boolean;
  healthStatus: EndpointHealthStatus;
  lastHealthCheckUtc: string | null;
  lastHealthError: string | null;
  lastLatencyMs: number | null;
  healthHistory: ModelEndpointHealthRecord[];
  uptimePercentage: number;
  consecutiveSuccesses: number;
  consecutiveFailures: number;
  firstHealthCheckUtc: string | null;
  lastHealthyUtc: string | null;
  lastUnhealthyUtc: string | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface ModelEndpointProbeResult {
  success: boolean;
  baseUrl: string | null;
  latencyMs: number;
  statusCode: number | null;
  error: string | null;
  embeddingDimensions: number | null;
  sampleText: string | null;
  timestampUtc: string;
}

export interface ModelEndpointHealthSweepResponse {
  distinctBaseUrlsProbed: number;
}

export type MemoryType = 'Episodic' | 'Semantic' | 'Procedural';

export type MemorySourceKind = 'Voyage' | 'Mission' | 'Vessel' | 'Conversation' | 'Manual' | 'Other';

export interface Memory {
  id: string;
  tenantId?: string | null;
  userId?: string | null;
  scope: ScopeEnum;
  type: MemoryType;
  topic?: string | null;
  key?: string | null;
  summary?: string | null;
  content: string;
  salience: number;
  version: number;
  sourceKind: MemorySourceKind;
  sourceVoyageId?: string | null;
  sourceMissionId?: string | null;
  sourceVesselId?: string | null;
  sourceDetail?: string | null;
  vesselId?: string | null;
  tags: string[];
  createdUtc: string;
  lastUpdateUtc: string;
}

// ---------------------------------------------------------------------------
// Vessel Import (bulk onboarding of existing local repositories)
// ---------------------------------------------------------------------------

export type VesselImportBatchStatus = 'Discovering' | 'Discovered' | 'Importing' | 'Completed' | 'CompletedWithFailures' | 'Failed';
export type VesselImportCategorizationStatus = 'None' | 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Applied';
export type VesselImportCandidateStatus = 'New' | 'AlreadyOnboarded' | 'Worktree' | 'ArmadaManaged' | 'NotFound' | 'NotGit' | 'AccessDenied';
export type VesselImportOutcome = 'Pending' | 'Created' | 'SkippedExisting' | 'SkippedNotSelected' | 'Failed';

export interface VesselBrowseEntry {
  name: string;
  path: string;
  isGitRepository: boolean;
  isWorktree: boolean;
  hasSubdirectories: boolean;
}

export interface VesselBrowseResult {
  path: string | null;
  parent: string | null;
  entries: VesselBrowseEntry[];
}

export interface VesselImportBatch {
  id: string;
  tenantId: string | null;
  userId: string | null;
  status: VesselImportBatchStatus;
  harborId: string | null;
  fleetId: string | null;
  jobId: string | null;
  requestedPathCount: number;
  candidateCount: number;
  createdCount: number;
  skippedCount: number;
  failedCount: number;
  createdUtc: string;
  lastUpdateUtc: string;
  completedUtc: string | null;
  discoveryJobId?: string | null;
  truncated?: boolean;
  errorMessage?: string | null;
  categorizationStatus?: VesselImportCategorizationStatus;
  categorizationCaptainId?: string | null;
  categorizationJobId?: string | null;
  categorizationPrompt?: string | null;
  categorizationApplyAutomatically?: boolean;
  categorizationError?: string | null;
  categorizationStartedUtc?: string | null;
  categorizationCompletedUtc?: string | null;
}

export interface VesselImportItem {
  id: string;
  tenantId: string | null;
  batchId: string | null;
  path: string;
  proposedName: string;
  remoteUrl: string | null;
  defaultBranch: string | null;
  candidateStatus: VesselImportCandidateStatus;
  existingVesselId: string | null;
  outcome: VesselImportOutcome;
  outcomeReason: string | null;
  outcomeMessage: string | null;
  vesselId: string | null;
  selected?: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface VesselImportHint {
  code: string;
  message: string;
}

export interface VesselDiscoveryRequest {
  Directories?: string[];
  Roots?: string[];
  MaxDepth?: number;
  RunInBackground?: boolean;
}

export interface VesselImportDiscoverResponse {
  batchId: string;
  jobId?: string | null;
  runsInBackground?: boolean;
  batch: VesselImportBatch;
  candidates: VesselImportItem[];
  truncated: boolean;
  hints: VesselImportHint[];
}

export interface VesselImportRequest {
  BatchId: string;
  Paths: string[];
  FleetId?: string | null;
  Defaults?: {
    DefaultPipelineId?: string | null;
    LandingMode?: string | null;
  } | null;
  Categorization?: VesselImportCategorizationRequest | null;
}

/** Optional captain-driven fleet categorization requested with an import. */
export interface VesselImportCategorizationRequest {
  Enabled: boolean;
  CaptainId?: string | null;
  Prompt?: string | null;
  ApplyAutomatically?: boolean;
}

/** One fleet a captain recommended for an import batch. */
export interface VesselImportFleetRecommendation {
  id: string;
  tenantId: string | null;
  batchId: string;
  name: string;
  description: string | null;
  rationale: string | null;
  sortOrder: number;
  appliedFleetId: string | null;
  vesselIds: string[];
  createdUtc: string;
  lastUpdateUtc: string;
}

/** Body of POST /api/v1/vessels/import/batches/{id}/fleet-recommendations/apply. */
export interface FleetRecommendationApplyRequest {
  Fleets: Array<{ Name: string; Description?: string | null; VesselIds: string[] }>;
}

export interface FleetRecommendationAssignment {
  vesselId: string;
  vesselName: string | null;
  fleetId: string;
  fleetName: string;
  previousFleetId: string | null;
}

export interface FleetRecommendationApplyResult {
  batchId: string;
  fleets: Fleet[];
  createdFleetIds: string[];
  assignments: FleetRecommendationAssignment[];
  batch: VesselImportBatch | null;
}

export interface FleetCategorizationDefaultPrompt {
  templateName: string;
  prompt: string;
  timeoutMinutes: number;
}

export interface VesselImportResponse {
  batchId: string;
  jobId: string | null;
  runsInBackground: boolean;
  batch: VesselImportBatch;
  items: VesselImportItem[];
}

export interface VesselImportBatchDetail {
  batch: VesselImportBatch;
  items: VesselImportItem[];
  hints?: VesselImportHint[];
  fleetRecommendations?: VesselImportFleetRecommendation[];
}

/** Machine-readable error detail carried in `data` of vessel import error responses. */
export interface VesselImportErrorDetail {
  code: string;
  path: string | null;
}

// ---------------------------------------------------------------------------
// Fleet Actions
// ---------------------------------------------------------------------------

export type FleetActionKind = 'Command' | 'Mission';
export type FleetActionRunStatus = 'Pending' | 'Running' | 'Completed' | 'CompletedWithFailures' | 'Cancelled' | 'Failed';
export type FleetActionTargetStatus = 'Pending' | 'Skipped' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled' | 'TimedOut';

export interface FleetAction {
  id: string;
  tenantId: string | null;
  userId: string | null;
  name: string;
  description: string | null;
  kind: FleetActionKind;
  commandText: string | null;
  promptTemplate: string | null;
  pipelineId: string | null;
  persona: string | null;
  timeoutSeconds: number;
  defaultConcurrency: number;
  requiresCleanWorkingTree: boolean;
  isBuiltIn: boolean;
  builtInKey: string | null;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

/** Create/update body. PascalCase keys because the server binds them as-is. */
export interface FleetActionUpsertRequest {
  Name?: string;
  Description?: string | null;
  Kind?: FleetActionKind;
  CommandText?: string | null;
  PromptTemplate?: string | null;
  PipelineId?: string | null;
  Persona?: string | null;
  TimeoutSeconds?: number | null;
  DefaultConcurrency?: number | null;
  RequiresCleanWorkingTree?: boolean | null;
}

export interface FleetActionRunOverrides {
  TimeoutSeconds?: number | null;
  RequiresCleanWorkingTree?: boolean | null;
  PipelineId?: string | null;
}

export interface FleetActionRunRequest {
  VesselIds: string[];
  Concurrency?: number | null;
  Overrides?: FleetActionRunOverrides | null;
  Definition?: FleetActionUpsertRequest | null;
}

export interface FleetActionRunStartResult {
  runId: string;
  actionId: string | null;
  kind: FleetActionKind;
  status: FleetActionRunStatus;
  targetCount: number;
  concurrency: number;
}

export interface FleetActionRun {
  id: string;
  tenantId: string | null;
  userId: string | null;
  actionId: string | null;
  actionName: string;
  kind: FleetActionKind;
  commandText: string | null;
  promptTemplate: string | null;
  pipelineId: string | null;
  persona: string | null;
  timeoutSeconds: number;
  requiresCleanWorkingTree: boolean;
  concurrency: number;
  status: FleetActionRunStatus;
  targetCount: number;
  succeededCount: number;
  failedCount: number;
  skippedCount: number;
  cancelledCount: number;
  startedUtc: string | null;
  completedUtc: string | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface FleetActionRunTargetSummary {
  id: string;
  runId: string | null;
  vesselId: string;
  vesselName: string;
  status: FleetActionTargetStatus;
  skipReason: string | null;
  failureReason: string | null;
  exitCode: number | null;
  outputTruncated: boolean;
  outputLength: number;
  errorLength: number;
  renderedLength: number;
  voyageId: string | null;
  startedUtc: string | null;
  completedUtc: string | null;
  durationMs: number | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface FleetActionRunTarget {
  id: string;
  tenantId: string | null;
  runId: string | null;
  vesselId: string;
  vesselName: string;
  status: FleetActionTargetStatus;
  skipReason: string | null;
  failureReason: string | null;
  renderedText: string | null;
  exitCode: number | null;
  outputText: string | null;
  errorText: string | null;
  outputTruncated: boolean;
  voyageId: string | null;
  startedUtc: string | null;
  completedUtc: string | null;
  durationMs: number | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface FleetActionRunDetail {
  run: FleetActionRun;
  targets: FleetActionRunTargetSummary[];
}

// ---------------------------------------------------------------------------
// Settings groups added for import and fleet actions
// ---------------------------------------------------------------------------

export interface VesselImportSettingsData {
  allowedRoots: string[];
  maxDepth: number;
  excludedDirectoryNames: string[];
  inlineBatchLimit: number;
  categorizationTimeoutMinutes?: number;
}

export interface FleetActionSettingsData {
  maxConcurrency: number;
  defaultTimeoutSeconds: number;
  maxOutputBytes: number;
  runRetentionDays: number;
}

/** Server `Retention` settings group: days to keep each kind of data; 0 means never (0-3650). */
export interface RetentionSettingsData {
  askThreadArchiveAfterDays: number;
  askThreadDeleteAfterDays: number;
  jobRetentionDays: number;
  importBatchRetentionDays: number;
  cliPermissionRequestRetentionDays: number;
}

/**
 * Server `Ask` settings group (Ask Armada conversations). PUT replaces the whole group, so clients send every field
 * and round-trip captainAutoApprove unchanged (a security setting governed by the CLI permission policy, not edited
 * in the UI).
 */
export interface AskSettingsData {
  /** Recent messages replayed to the captain each turn (2-200, default 20). */
  historyTurns: number;
  /** Minutes a pending action proposal waits before it expires (1-1440, default 60). */
  proposalExpiryMinutes: number;
  /** Seconds between work-tracker sweeps (2-300, default 5). */
  trackerIntervalSeconds: number;
  /** The captain narrates milestones when idle (default true). */
  narrateMilestones: boolean;
  /** The captain posts a WorkReport when tracked work finishes (default true). */
  reportResultsOnCompletion: boolean;
  /** Ask turns run CLI captains with their auto-approve flags (default false). Preserved, never edited. */
  captainAutoApprove: boolean;
  /** Seconds a milestone narration may take (10-600, default 60). */
  narrationTimeoutSeconds: number;
  /** Minutes a captain turn may run (1-120, default 15). */
  turnTimeoutMinutes: number;
}

// ---------------------------------------------------------------------------
// Ask Armada threads (docs/ASK_ARMADA_HOME_BASE.md). The server sends PascalCase; the API client camelizes.
// Every field the plan does not pin down is optional so the UI tolerates a slightly different server shape.
// ---------------------------------------------------------------------------

export type AskMessageRole = 'User' | 'Assistant' | 'System';
export type AskMessageKind = 'Text' | 'ActionProposal' | 'ActionResult' | 'WorkUpdate' | 'Summary' | 'Error' | 'CliPermission' | 'WorkReport';
export type AskProposalStatus = 'Pending' | 'Approved' | 'Rejected' | 'Expired' | 'Executed' | 'Failed';
export type AskProposalSource = 'Captain' | 'QuickAction';
export type AskTrackedEntityType = 'Voyage' | 'Mission' | 'FleetActionRun' | 'Job' | 'VesselImportBatch';
export type AskWorkState = 'Active' | 'Succeeded' | 'Failed' | 'Cancelled';
export type AskTurnState = 'started' | 'completed' | 'failed' | 'cancelled';

export interface AskThread {
  id: string;
  tenantId?: string | null;
  userId?: string | null;
  title: string;
  captainId: string | null;
  autoApprove: boolean;
  summaryText?: string | null;
  summaryUtc?: string | null;
  pinned: boolean;
  archived: boolean;
  lastMessageUtc?: string | null;
  messageCount?: number;
  unreadCount?: number;
  /** UI assumption: number of tracked items still Active (drives the "working" dot). */
  activeWorkCount?: number;
  /** UI assumption: id of the captain turn currently running, when one is. */
  activeTurnId?: string | null;
  /** The thread's CLI tool permission policy override; null inherits from the captain and the server default. */
  cliPermissionPolicy?: CliPermissionPolicy | null;
  /** The resolved CLI tool permission policy for this thread's captain turns (computed by the server). */
  cliPermission?: CliPermissionResolution | null;
  createdUtc?: string;
  lastUpdateUtc?: string;
}

export interface AskToolCall {
  id?: string;
  messageId?: string;
  threadId?: string;
  callId: string;
  toolName: string;
  argumentsText?: string | null;
  resultText?: string | null;
  ok?: boolean | null;
  elapsedMs?: number | null;
  /** True when the CLI refused the call because the turn's CLI tool permission policy did not grant it. */
  permissionDenied?: boolean | null;
}

export interface AskActionProposal {
  id: string;
  threadId: string;
  messageId?: string | null;
  toolName: string;
  argumentsText?: string | null;
  summaryText?: string | null;
  source: AskProposalSource | string;
  status: AskProposalStatus | string;
  resultText?: string | null;
  errorText?: string | null;
  decidedByUserId?: string | null;
  decidedUtc?: string | null;
  executedUtc?: string | null;
  /** UI assumption: when a pending proposal expires. */
  expiresUtc?: string | null;
  createdUtc?: string;
}

/** One mission row on a voyage (or mission) work card. */
export interface AskMissionSnapshot {
  id: string;
  title?: string | null;
  status: string;
  vesselId?: string | null;
  persona?: string | null;
  pipelineStage?: string | null;
  captainId?: string | null;
  captainName?: string | null;
  branchName?: string | null;
  checkRunId?: string | null;
  checkRunStatus?: string | null;
  mergeEntryId?: string | null;
  mergeQueueStatus?: string | null;
  prUrl?: string | null;
  landingOutcome?: string | null;
  failureReason?: string | null;
  startedUtc?: string | null;
  completedUtc?: string | null;
}

/** One target row on a fleet action run work card. */
export interface AskTargetSnapshot {
  id: string;
  vesselId?: string | null;
  vesselName?: string | null;
  status: string;
  reason?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
}

/**
 * The live card data for one tracked item. Voyage and Mission snapshots fill `missions`; FleetActionRun fills
 * `targets`; Job and VesselImportBatch use `completedCount` / `totalCount` and `errorText`.
 */
export interface AskWorkSnapshot {
  trackedWorkId?: string;
  entityType: AskTrackedEntityType | string;
  entityId: string;
  title?: string | null;
  status: string;
  state?: AskWorkState | string;
  /** Count of children by status (missions or targets); keys are status names. */
  counts?: Record<string, number> | null;
  totalCount?: number | null;
  completedCount?: number | null;
  failedCount?: number | null;
  missions?: AskMissionSnapshot[] | null;
  targets?: AskTargetSnapshot[] | null;
  errorText?: string | null;
  startedUtc?: string | null;
  completedUtc?: string | null;
  capturedUtc?: string | null;
}

export interface AskTrackedWork {
  id: string;
  threadId: string;
  entityType: AskTrackedEntityType | string;
  entityId: string;
  title?: string | null;
  status?: string | null;
  state: AskWorkState | string;
  lastChangeUtc?: string | null;
  completedUtc?: string | null;
  createdUtc?: string;
  /** UI assumption: the latest snapshot, when the server includes it. */
  snapshot?: AskWorkSnapshot | null;
}

export interface AskMessage {
  id: string;
  threadId: string;
  sequence: number;
  role: AskMessageRole | string;
  kind: AskMessageKind | string;
  contentText?: string | null;
  thinkingText?: string | null;
  proposalId?: string | null;
  trackedWorkId?: string | null;
  captainId?: string | null;
  durationMs?: number | null;
  /** Telemetry of the captain turn that wrote this reply; null for user messages, cards, and older replies. */
  metrics?: CaptainChatMetrics | null;
  createdUtc?: string;
  toolCalls?: AskToolCall[] | null;
  proposal?: AskActionProposal | null;
  trackedWork?: AskTrackedWork | null;
  /** CliPermission cards: the linked CLI permission request. */
  cliPermissionRequest?: CliPermissionRequest | null;
  /** Client-only: true for an optimistic user message not yet persisted by the server. Never sent by the server. */
  isLocal?: boolean;
}

export interface AskThreadDetail {
  thread: AskThread;
  trackedWork?: AskTrackedWork[] | null;
  pendingProposals?: AskActionProposal[] | null;
  /** Pending CLI permission requests of this thread's captain turns. */
  pendingCliPermissions?: CliPermissionRequest[] | null;
}

export interface AskMessagePage {
  messages: AskMessage[];
  hasMore: boolean;
}

export interface AskSendMessageResult {
  messageId: string;
  turnId: string;
}

export interface AskThreadEnumerateQuery {
  pageNumber?: number;
  pageSize?: number;
  search?: string;
  includeArchived?: boolean;
}

export interface AskThreadUpdateRequest {
  title?: string;
  captainId?: string | null;
  autoApprove?: boolean;
  pinned?: boolean;
  archived?: boolean;
}

/** One entry of `GET /ask/quick-actions`. */
export interface AskQuickAction {
  /** Stable name, e.g. `dispatch`, `fleet-action`, `status`, `health`, `import`. */
  name: string;
  /** The slash command, e.g. `/dispatch`. */
  command?: string;
  title?: string;
  description?: string;
  /** The MCP tool the action runs; null for client-only actions such as `/import`. */
  toolName?: string | null;
  /** JSON schema of the tool arguments (object or JSON text). */
  argumentsSchema?: unknown;
}

// ---------------------------------------------------------------------------
// CLI tool permissions: how CLI captains (Claude Code, Codex, ...) handle shell, edit, and fetch tool calls that need
// permission. The server sends PascalCase; the API client camelizes. Enums travel as names.
// ---------------------------------------------------------------------------

/** Wire values of CliPermissionPolicyEnum. */
export type CliPermissionPolicy = 'Refuse' | 'ApproveInArmada' | 'Bypass';
/** Wire values of CliPermissionPolicySourceEnum: where the effective policy came from. */
export type CliPermissionPolicySource = 'AskThread' | 'VesselAutoApprove' | 'Captain' | 'CaptainAutoApprove' | 'ServerDefault';
/** Wire values of CliPermissionFallbackReasonEnum: why ApproveInArmada fell back to Refuse. */
export type CliPermissionFallbackReason = 'RuntimeUnsupported' | 'NoSessionToken' | 'RemoteHarbor';
/** Wire values of CliPermissionRequestStatusEnum. */
export type CliPermissionRequestStatus = 'Pending' | 'Allowed' | 'Denied' | 'Expired' | 'Cancelled';
/** Wire values of CliPermissionDecisionSourceEnum. */
export type CliPermissionDecisionSource = 'Approver' | 'AllowRule' | 'DenyRule' | 'Timeout' | 'Cancelled' | 'DeliveryFailed';
/** Wire values of CliPermissionDecisionEnum. */
export type CliPermissionDecision = 'AllowOnce' | 'AllowAndRemember' | 'Deny';
/** Wire values of CliPermissionRuleScopeEnum. */
export type CliPermissionRuleScope = 'Global' | 'Vessel' | 'Captain';
/** Wire values of CliPermissionRuleActionEnum. */
export type CliPermissionRuleAction = 'Allow' | 'Deny';

/** The resolved CLI tool permission policy of an Ask thread (or mission). */
export interface CliPermissionResolution {
  requested: CliPermissionPolicy;
  effective: CliPermissionPolicy;
  source: CliPermissionPolicySource;
  fallbackReason?: CliPermissionFallbackReason | null;
  /** One-line English explanation from the server (display only; the dashboard builds its own from the typed fields). */
  note?: string | null;
}

/** A CLI tool call waiting on (or decided by) an approver in Armada. */
export interface CliPermissionRequest {
  id: string;
  tenantId?: string | null;
  userId?: string | null;
  captainId?: string | null;
  missionId?: string | null;
  voyageId?: string | null;
  vesselId?: string | null;
  threadId?: string | null;
  messageId?: string | null;
  runtime?: string | null;
  toolName: string;
  /** The tool input as (redacted) JSON text. */
  inputText?: string | null;
  /** The command, URL, or path the tool acts on. */
  summaryText?: string | null;
  /** A rule pattern that would allow this call, e.g. Bash(git status:*). */
  suggestedRule?: string | null;
  status: CliPermissionRequestStatus | string;
  decisionSource?: CliPermissionDecisionSource | string | null;
  ruleId?: string | null;
  decidedByUserId?: string | null;
  decisionMessage?: string | null;
  expiresUtc?: string | null;
  decidedUtc?: string | null;
  createdUtc?: string;
  lastUpdateUtc?: string;
  captainName?: string | null;
  vesselName?: string | null;
  missionTitle?: string | null;
  threadTitle?: string | null;
  /** True when the caller may allow or deny this request now. */
  canDecide?: boolean;
  /** True when the caller may also create an allow rule from it (Allow and remember). */
  canRemember?: boolean;
}

/** Body of POST /api/v1/cli-permissions/requests/{id}/decide. */
export interface CliPermissionDecisionRequest {
  decision: CliPermissionDecision;
  message?: string | null;
  rulePattern?: string | null;
  ruleScope?: CliPermissionRuleScope;
}

/** Query of GET /api/v1/cli-permissions/requests. */
export interface CliPermissionRequestQuery {
  status?: CliPermissionRequestStatus | null;
  missionId?: string | null;
  threadId?: string | null;
  captainId?: string | null;
  vesselId?: string | null;
  limit?: number | null;
}

/** An allow or deny rule in Claude Code permission rule syntax, e.g. Bash(git status:*). */
export interface CliPermissionRule {
  id: string;
  tenantId?: string | null;
  scope: CliPermissionRuleScope;
  vesselId?: string | null;
  captainId?: string | null;
  pattern: string;
  action: CliPermissionRuleAction;
  description?: string | null;
  createdByUserId?: string | null;
  createdUtc?: string;
  lastUpdateUtc?: string;
}

/** Body of POST /api/v1/cli-permissions/rules. */
export interface CliPermissionRuleCreateRequest {
  pattern: string;
  action: CliPermissionRuleAction;
  scope: CliPermissionRuleScope;
  vesselId?: string | null;
  captainId?: string | null;
  tenantId?: string | null;
  description?: string | null;
}

/** Server `Permissions` settings group (global admin only). */
export interface CliPermissionSettingsData {
  askDefaultPolicy: CliPermissionPolicy;
  missionDefaultPolicy: CliPermissionPolicy;
  allowOwnerApproval: boolean;
  /** Seconds a request waits for a decision (10-3600, default 600). */
  promptTimeoutSeconds: number;
}

// ==================== Vessel History ====================

/** Kind of change git reports for one path (server `GitChangeKindEnum`). */
export type GitChangeKind = 'Modified' | 'Added' | 'Deleted' | 'Renamed' | 'Copied' | 'TypeChanged' | 'Unmerged' | 'Unknown';

/** One changed path in a commit (server `GitChangedFile`). */
export interface GitChangedFile {
  kind: GitChangeKind;
  /** Repository-relative path; for a rename or copy, the new path. */
  path: string;
  /** Source path for a rename or copy; null otherwise. */
  oldPath: string | null;
  /** Added lines; null for binary files or when not measured. */
  addedLines: number | null;
  /** Deleted lines; null for binary files or when not measured. */
  deletedLines: number | null;
  isBinary: boolean;
}

/** Commit count for one calendar day (one heatmap cell). */
export interface VesselCommitActivityDay {
  /** yyyy-MM-dd in the requested UTC offset. */
  date: string;
  count: number;
}

/** Per-day commit counts for a vessel branch (GET /api/v1/vessels/{id}/history/activity). */
export interface VesselCommitActivity {
  vesselId: string;
  branch: string;
  /** First day, yyyy-MM-dd, inclusive. */
  from: string;
  /** Last day, yyyy-MM-dd, inclusive. */
  to: string;
  utcOffsetMinutes: number;
  /** One entry per day from..to inclusive, zero-filled, in date order. */
  days: VesselCommitActivityDay[];
  totalCommits: number;
  maxDayCount: number;
  /** Oldest commit date on the branch (whole history), or null when the branch has no commits. */
  firstCommitUtc: string | null;
  /** Newest commit date on the branch, or null when the branch has no commits. */
  lastCommitUtc: string | null;
  /** Error reading the repository (returned with HTTP 200), or null. */
  error: string | null;
}

/** One commit with what it changed (server `VesselCommit`). */
export interface VesselCommit {
  sha: string;
  shortSha: string;
  subject: string;
  body: string;
  authorName: string;
  authorEmail: string;
  authoredUtc: string;
  committerName: string;
  committerEmail: string;
  /** Commit date; history is ordered and filtered by it. */
  committedUtc: string;
  parentShas: string[];
  isMerge: boolean;
  /** All changed files, even when `files` is truncated. */
  filesChanged: number;
  addedLines: number;
  deletedLines: number;
  /** At most 200 entries. */
  files: GitChangedFile[];
  filesTruncated: boolean;
}

/** One page of commit history, newest first (GET /api/v1/vessels/{id}/history/commits). */
export interface VesselCommitPage {
  vesselId: string;
  branch: string;
  commits: VesselCommit[];
  /** Cursor for the next (older) page, or null on the last page. */
  nextCursor: string | null;
  /** Error reading the repository (returned with HTTP 200), or null. */
  error: string | null;
}

/** Query for `getVesselCommitActivity`. */
export interface VesselCommitActivityQuery {
  /** Branch; omitted for the vessel's default branch. */
  branch?: string | null;
  /** First day, yyyy-MM-dd; omitted for 364 days before `to`. */
  from?: string | null;
  /** Last day, yyyy-MM-dd; omitted for today in the offset. */
  to?: string | null;
  /** Offset to bucket days in (-840..840); omitted for UTC. */
  utcOffsetMinutes?: number | null;
}

/** Query for `getVesselCommits`. */
export interface VesselCommitQuery {
  /** Branch; omitted for the vessel's default branch. Ignored with `cursor`. */
  branch?: string | null;
  /** Exclusive upper bound on commit date (ISO 8601). Ignored with `cursor`. */
  before?: string | null;
  /** `nextCursor` from the previous page. */
  cursor?: string | null;
  /** Page size 1..200 (server default 50). */
  limit?: number | null;
}
