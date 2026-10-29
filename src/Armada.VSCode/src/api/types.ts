/*
 * Armada black box DTOs used by the VS Code extension.
 *
 * Reconciled with the server models (src/Armada.Core/Models/BlackBox*.cs) and routes
 * (src/Armada.Server/Routes/BlackBoxRoutes.cs). REST bodies are PascalCase (docs/REST_API.md); WebSocket payloads are
 * camelCase (docs/WEBSOCKET_API.md). Every field the extension does not strictly need is optional so a server that
 * omits nulls (the Armada default) still parses.
 */

/** Paged result wrapper returned by every Armada enumerate route. */
export interface EnumerationResult<T> {
  Success?: boolean;
  PageNumber: number;
  PageSize: number;
  TotalPages: number;
  TotalRecords: number;
  Objects: T[];
}

/** Machine-readable detail in the Data of a black box error (BlackBoxErrorDetail, or CollectionLimitErrorDetail). */
export interface BlackBoxErrorDetail {
  /** BlackBoxNotFound, BlackBoxDeleting, BlackBoxObjectCorrupt, BlackBoxConfirmationRequired, BlackBoxObjectMissing, BlackBoxStoreUnavailable, InvalidArgument, NotImplemented or CollectionLimitExceeded. */
  Code?: string;
  /** The request field at fault for InvalidArgument (for example audience or section). */
  Field?: string | null;
}

/** Standard Armada REST error body (docs/REST_API.md, Error Responses). */
export interface ApiErrorResponse {
  Error?: string;
  StatusCode?: number;
  Description?: string;
  Message?: string;
  Data?: BlackBoxErrorDetail | null;
}

/** BlackBoxResolutionEnum. */
export type BlackBoxResolution = 'Open' | 'Resolved' | 'Duplicate' | 'WontFix' | 'ArmadaFixed';

/** The resolutions an operator can pick when resolving an entry. */
export const RESOLUTION_CHOICES: BlackBoxResolution[] = ['Resolved', 'Duplicate', 'WontFix', 'ArmadaFixed'];

/** Brief audience (`?audience=`). */
export type BlackBoxAudience = 'agent' | 'user' | 'maintainer';

/** Minimal vessel shape the extension needs (GET /api/v1/vessels). */
export interface Vessel {
  Id: string;
  Name?: string;
  RepoUrl?: string | null;
  FleetId?: string | null;
  DefaultBranch?: string;
}

/** One row of `GET /api/v1/blackbox` and `POST /api/v1/blackbox/enumerate` (BlackBoxEntrySummary). */
export interface BlackBoxEntrySummary {
  Id: string;
  SubjectType?: 'Mission' | 'Voyage' | string;
  MissionId?: string | null;
  VoyageId?: string | null;
  VesselId?: string | null;
  CaptainId?: string | null;
  MissionTitle?: string | null;
  VesselName?: string | null;
  Attempt?: number;
  IsCurrent?: boolean;
  Phase?: string;
  Step?: string;
  FailureKind?: string | null;
  StatusBefore?: string | null;
  StatusAfter?: string | null;
  Triage?: string | null;
  Fingerprint?: string | null;
  CaptureMode?: string;
  State?: string;
  Resolution?: BlackBoxResolution | string;
  Pinned?: boolean;
  ReasonExcerpt?: string | null;
  TotalBytes?: number;
  ObjectCount?: number;
  CreatedUtc: string;
  ExpiresUtc?: string | null;
}

/** Full entry (`GET /api/v1/blackbox/{id}`, `GET /api/v1/missions/{id}/blackbox`). */
export interface BlackBoxEntry extends BlackBoxEntrySummary {
  TenantId?: string | null;
  FleetId?: string | null;
  Summary?: string | null;
  AnalyzedUtc?: string | null;
  ResolvedUtc?: string | null;
  LastUpdateUtc?: string | null;
  ResolutionNote?: string | null;
  ResolutionLink?: string | null;
  ArmadaVersion?: string | null;
  ArmadaCommit?: string | null;
  Record?: BlackBoxRecord | null;
}

/** A directory roll-up of conflicted files (Conflict.ConflictedDirectories; depth 1 to 3). */
export interface BlackBoxDirectoryCount {
  Directory: string;
  Depth?: number;
  FileCount?: number;
}

/** A mission that landed on the target since this one started and touched the same files. */
export interface BlackBoxConflictingMission {
  MissionId?: string | null;
  Title?: string | null;
  CommitHash?: string | null;
  LandedUtc?: string | null;
  OverlappingFiles?: string[];
  OverlappingFileTotal?: number;
  IsOutsideArmada?: boolean;
  Author?: string | null;
}

/** A commit on the target branch (Conflict.TargetCommitsSinceStart). */
export interface BlackBoxCommitRef {
  CommitHash: string;
  Subject?: string | null;
  Author?: string | null;
  CommittedUtc?: string | null;
  MissionId?: string | null;
}

export interface BlackBoxSubject {
  SubjectType?: 'Mission' | 'Voyage' | string;
  MissionId?: string | null;
  VoyageId?: string | null;
  Title?: string | null;
  VoyageTitle?: string | null;
  VesselId?: string | null;
  VesselName?: string | null;
  VesselRepoUrl?: string | null;
  VesselDefaultBranch?: string | null;
  CaptainId?: string | null;
  CaptainName?: string | null;
  CaptainRuntime?: string | null;
  CaptainModel?: string | null;
  Persona?: string | null;
  BranchName?: string | null;
  Attempt?: number;
}

export interface BlackBoxStatusChange {
  Before?: string | null;
  After?: string | null;
  ChangedUtc?: string | null;
}

export interface BlackBoxSite {
  Phase?: string;
  Step?: string;
  HostKind?: 'Admiral' | 'Harbor' | string;
  Host?: string;
  Component?: string;
  Method?: string;
  SourceFile?: string;
  SourceLine?: number;
  ArmadaVersion?: string;
  ArmadaCommit?: string;
}

export interface BlackBoxMechanism {
  ExceptionType?: string | null;
  ExceptionMessage?: string | null;
  StackFrames?: string[];
  ExitCode?: number | null;
  RuntimeFailureKind?: string | null;
  FailingCommand?: string | null;
  CommandExitCode?: number | null;
  CommandOutputTail?: string | null;
  GitOperation?: string | null;
  GitStderrTail?: string | null;
}

export interface BlackBoxClassification {
  Kind?: string | null;
  RawReason?: string | null;
  SuspectedCause?: string | null;
  CauseRuleId?: string | null;
  IsRecoverable?: boolean;
  IsTransient?: boolean;
}

export interface BlackBoxConflict {
  IsConflict?: boolean;
  /** Repository-relative paths (at most 500; ConflictedFileTotal is the true count). */
  ConflictedFiles?: string[];
  ConflictedFileTotal?: number;
  ConflictedFilesTruncated?: boolean;
  ConflictedDirectories?: BlackBoxDirectoryCount[];
  ConflictingMissions?: BlackBoxConflictingMission[];
  TargetCommitsSinceStart?: BlackBoxCommitRef[];
  TargetCommitTotal?: number;
}

/** A finding (BlackBoxFinding). */
export interface BlackBoxWhy {
  RuleId?: string;
  Statement: string;
  Confidence?: 'Low' | 'Medium' | 'High' | string;
  EvidenceRefs?: string[];
}

/** A tool call recorded in the entry (BlackBoxAction); recovery and recommendations run only these. */
export interface BlackBoxRecoveryAction {
  Tool: string;
  Arguments?: Record<string, string>;
}

export interface BlackBoxConfigRecommendation {
  Id?: string;
  Setting: string;
  Scope?: string;
  CurrentValue?: string | null;
  SuggestedValue?: string | null;
  Rationale?: string;
  RuleId?: string;
  HowToApply?: string;
  ApplyAction?: BlackBoxRecoveryAction | null;
}

export interface BlackBoxArmadaImprovement {
  Area?: string;
  Hypothesis: string;
  Evidence?: string;
  SourcePointers?: string[];
  Severity?: string;
  RuleId?: string;
  KnownIssue?: string | null;
}

/** One step of a recovery path (BlackBoxRecoveryStep). */
export interface BlackBoxRecoveryStep {
  Order?: number;
  Description?: string;
  McpCommand?: string | null;
  RestCommand?: string | null;
  CliCommand?: string | null;
  Action?: BlackBoxRecoveryAction | null;
  DataLossRisk?: boolean;
}

export interface BlackBoxRecoveryPath {
  Id: string;
  Rank?: number;
  Title: string;
  Preconditions?: string[];
  Steps?: BlackBoxRecoveryStep[];
  Action?: BlackBoxRecoveryAction;
  Risk?: string;
  DataLossRisk?: boolean;
  ExpectedOutcome?: string;
  RuleId?: string;
}

export interface BlackBoxTriage {
  Verdict?: string;
  Confidence?: 'Low' | 'Medium' | 'High' | string;
}

export interface BlackBoxAnalysis {
  Summary?: string;
  Why?: BlackBoxWhy[];
  ConfigRecommendations?: BlackBoxConfigRecommendation[];
  ArmadaImprovements?: BlackBoxArmadaImprovement[];
  RecoveryPaths?: BlackBoxRecoveryPath[];
  Triage?: BlackBoxTriage;
}

export interface BlackBoxAttempt {
  Attempt?: number;
  EntryId?: string;
  MissionId?: string;
  Kind?: string;
  ReasonExcerpt?: string;
  Runtime?: string;
  Model?: string;
  DurationMs?: number;
  CreatedUtc?: string;
}

/** The parts of `record.json` (BlackBoxRecord, plan 4.3) the extension renders. */
export interface BlackBoxRecord {
  EntryId?: string;
  SchemaVersion?: number;
  CaptureMode?: string;
  State?: string;
  CreatedUtc?: string;
  Subject?: BlackBoxSubject;
  Status?: BlackBoxStatusChange;
  Site?: BlackBoxSite;
  Mechanism?: BlackBoxMechanism;
  Classification?: BlackBoxClassification;
  Conflict?: BlackBoxConflict;
  Analysis?: BlackBoxAnalysis;
  Attempts?: BlackBoxAttempt[];
}

/** Record section names accepted by `GET /api/v1/blackbox/{id}/sections/{section}`. */
export type BlackBoxSectionName =
  | 'Subject' | 'Status' | 'Site' | 'Mechanism' | 'Classification' | 'Workspace' | 'Conflict' | 'Scope'
  | 'Configuration' | 'Attempts' | 'Evidence' | 'Analysis' | 'Related' | 'Voyage' | 'Redaction' | 'Truncation';

/** Response of the section route (BlackBoxSectionResult): a record carrying only the asked section. */
export interface BlackBoxSectionResult {
  EntryId?: string;
  Section?: BlackBoxSectionName | string;
  Record?: BlackBoxRecord;
}

/** One row of `GET /api/v1/blackbox/{id}/objects`. */
export interface BlackBoxObjectSummary {
  Name: string;
  ContentType?: string;
  ContentEncoding?: string;
  SizeBytes?: number;
  UncompressedBytes?: number;
  Sha256?: string;
  IsMissing?: boolean;
  CreatedUtc?: string;
}

/** A note (`blackbox_notes`). */
export interface BlackBoxNote {
  Id?: string;
  EntryId?: string;
  UserId?: string;
  AuthorKind?: 'Operator' | 'Agent' | 'AskArmada' | 'System' | string;
  Body: string;
  CreatedUtc?: string;
}

/** Body of `POST /api/v1/blackbox/{id}/resolve`. */
export interface BlackBoxResolveRequest {
  Resolution: BlackBoxResolution;
  Note?: string;
  Link?: string;
}

/** Body of `POST /api/v1/blackbox/{id}/recovery/{pathId}`. */
export interface BlackBoxRecoveryRequest {
  DryRun: boolean;
  ConfirmToken?: string;
  AcknowledgeDataLoss?: boolean;
  /** Who the recovery note is attributed to; the extension is a human UI, so Operator. */
  AuthorKind?: 'Operator' | 'Agent' | 'AskArmada' | 'System';
}

/** What one step of a real recovery run did (BlackBoxRecoveryStepResult). */
export interface BlackBoxRecoveryStepResult {
  Order?: number;
  Tool?: string | null;
  Succeeded?: boolean;
  Message?: string | null;
}

/** Response of `POST /api/v1/blackbox/{id}/recovery/{pathId}` (BlackBoxRecoveryRunResult). */
export interface BlackBoxRecoveryResult {
  EntryId?: string;
  PathId?: string;
  DryRun?: boolean;
  ConfirmToken?: string | null;
  ConfirmTokenExpiresUtc?: string | null;
  /** The steps a dry run would run. */
  Plan?: BlackBoxRecoveryStep[];
  /** What each step of a real run did. */
  StepResults?: BlackBoxRecoveryStepResult[];
  Succeeded?: boolean;
  /** True when a step's precondition is not met (no token on a dry run; nothing ran on a confirm). */
  Refused?: boolean;
  UnmetPreconditions?: string[];
  Message?: string | null;
}

/** One row of `POST /api/v1/blackbox/hotspots/enumerate` (BlackBoxHotspot). */
export interface BlackBoxHotspot {
  VesselId?: string | null;
  Directory: string;
  FileCount?: number;
  EntryCount?: number;
  LastSeenUtc?: string | null;
}

/** Filters for the entry enumerate route (plan 8.1). */
export interface BlackBoxEnumerateQuery {
  PageNumber?: number;
  PageSize?: number;
  TenantId?: string;
  VesselId?: string;
  VoyageId?: string;
  MissionId?: string;
  CaptainId?: string;
  Fingerprint?: string;
  Triage?: string;
  Resolution?: string;
  IsCurrent?: boolean;
  Pinned?: boolean;
  Kind?: string;
  Phase?: string;
  Step?: string;
  Path?: string;
  CreatedAfter?: string;
  CreatedBefore?: string;
  Order?: 'CreatedDescending' | 'CreatedAscending';
}

/**
 * camelCase payload of the `blackbox.*` WebSocket events (union of BlackBoxCapturedPayload, BlackBoxAnalyzedPayload,
 * BlackBoxResolvedPayload, BlackBoxDeletedPayload and BlackBoxRecoveryRunPayload). Only identifiers are relied on.
 */
export interface BlackBoxSocketEventData {
  entryId?: string;
  missionId?: string | null;
  voyageId?: string | null;
  vesselId?: string | null;
  phase?: string | null;
  step?: string | null;
  kind?: string | null;
  attempt?: number;
  captureMode?: string | null;
  state?: string | null;
  triage?: string | null;
  rulesVersion?: number;
  resolution?: string | null;
  reason?: string | null;
  pathId?: string | null;
  dryRun?: boolean;
  succeeded?: boolean;
}

/** WebSocket server message envelope. */
export interface SocketEnvelope {
  type?: string;
  message?: string;
  data?: unknown;
  timestamp?: string;
}
