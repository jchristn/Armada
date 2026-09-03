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
  supportsPlanningSessions: boolean;
  planningSessionSupportReason: string | null;
  systemInstructions: string | null;
  model: string | null;
  reasoningEffort?: string | null;
  tier?: string | null;
  quarantineUntilUtc?: string | null;
  quarantineReason?: string | null;
  allowedPersonas: string | null;
  preferredPersona: string | null;
  runtimeOptionsJson?: string | null;
  state: string;
  currentMissionId: string | null;
  currentDockId: string | null;
  processId: number | null;
  recoveryAttempts: number;
  lastHeartbeatUtc: string | null;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface CaptainToolSummary {
  name: string;
  description: string;
  inputSchemaJson: string | null;
  registrationSource: string | null;
  sourceKind: string | null;
}

export interface CaptainToolServerSummary {
  name: string;
  sourceKind: string;
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
  name: string;
  description: string | null;
  category: string | null;
  content: string;
  isBuiltIn: boolean;
  active: boolean;
  createdUtc: string;
  lastUpdateUtc: string;
}

export interface AskLink {
  label: string;
  href: string;
}

export type AskResponseKind = 'Answer' | 'Help' | 'Unknown';

export interface AskResponse {
  reply: string;
  kind: AskResponseKind;
  links: AskLink[];
}

export interface CaptainChatMessage {
  role: 'user' | 'assistant';
  content: string;
}

export interface CaptainChatMetrics {
  timeToFirstTokenMs: number | null;
  streamingMs: number | null;
  totalMs: number | null;
  promptTokens: number | null;
  completionTokens: number | null;
  totalTokens: number | null;
  tokensPerSecond: number | null;
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

export interface VesselReadinessResult {
  vesselId: string;
  hasWorkingDirectory: boolean;
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

export interface HistoricalTimelineEntry {
  id: string;
  sourceType: string;
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
  defaultCaptainId?: string | null;
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
  requiresReview: boolean;
  reviewDenyAction: 'RetryStage' | 'FailPipeline';
}

export type EntityType = 'fleets' | 'vessels' | 'captains' | 'missions' | 'voyages' | 'signals' | 'events' | 'docks' | 'merge-queue' | 'personas' | 'prompt-templates' | 'pipelines' | 'playbooks' | 'releases' | 'environments' | 'deployments' | 'incidents' | 'runbooks';
