/*
 * Armada black box DTOs used by the VS Code extension.
 *
 * Written against MISSION_BLACKBOX_PLAN.md sections 4.2, 4.3 and 8.1, before the server-side routes existed. REST
 * bodies are PascalCase (docs/REST_API.md); WebSocket payloads are camelCase (docs/WEBSOCKET_API.md). Every field
 * the extension does not strictly need is optional so a server that omits nulls (the Armada default) still parses.
 * Reconcile these shapes with the final server models in this file and in blackbox.ts only.
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

/** Standard Armada REST error body (docs/REST_API.md, Error Responses). */
export interface ApiErrorResponse {
  Error?: string;
  StatusCode?: number;
  Description?: string;
  Message?: string;
  Data?: unknown;
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
  TenantId?: string;
  SubjectType?: 'Mission' | 'Voyage' | string;
  MissionId?: string | null;
  VoyageId?: string | null;
  VesselId?: string | null;
  FleetId?: string | null;
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
  TotalBytes?: number;
  ObjectCount?: number;
  CreatedUtc: string;
  AnalyzedUtc?: string | null;
  ResolvedUtc?: string | null;
  LastUpdateUtc?: string | null;
}

/** Full entry (`GET /api/v1/blackbox/{id}`, `GET /api/v1/missions/{id}/blackbox`). */
export interface BlackBoxEntry extends BlackBoxEntrySummary {
  ResolutionNote?: string | null;
  ResolutionLink?: string | null;
  ArmadaVersion?: string | null;
  ArmadaCommit?: string | null;
  Record?: BlackBoxRecord | null;
}

/** A file reference inside a record section; the server may send a bare path string instead. */
export interface BlackBoxFileRef {
  Path: string;
  Role?: string;
  ConflictingMissionId?: string | null;
}

/** A directory with a count (Conflict.ConflictedDirectories). */
export interface BlackBoxDirectoryCount {
  Directory?: string;
  Path?: string;
  Count?: number;
}

export interface BlackBoxConflictingMission {
  MissionId?: string;
  Id?: string;
  Title?: string;
  Commit?: string;
  LandedUtc?: string;
  OverlappingFiles?: Array<string | BlackBoxFileRef>;
}

export interface BlackBoxSubject {
  Type?: string;
  MissionId?: string | null;
  VoyageId?: string | null;
  Title?: string | null;
  VesselId?: string | null;
  VesselName?: string | null;
  RepoUrl?: string | null;
  CaptainName?: string | null;
  Runtime?: string | null;
  Model?: string | null;
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
  ExitCode?: number | null;
  RuntimeFailureKind?: string | null;
  FailingCommand?: string | null;
  CommandExitCode?: number | null;
  CommandOutputTail?: string | null;
  GitOperation?: string | null;
  GitStderrTail?: string | null;
}

export interface BlackBoxClassification {
  Kind?: string;
  RawReason?: string | null;
  SuspectedCause?: string | null;
  CauseRuleId?: string | null;
  IsRecoverable?: boolean;
  IsTransient?: boolean;
}

export interface BlackBoxConflict {
  IsConflict?: boolean;
  ConflictedFiles?: Array<string | BlackBoxFileRef>;
  ConflictedFilesTotalCount?: number;
  ConflictedDirectories?: BlackBoxDirectoryCount[];
  ConflictingMissions?: BlackBoxConflictingMission[];
}

export interface BlackBoxWhy {
  RuleId?: string;
  Statement: string;
  Confidence?: string | number;
  EvidenceRefs?: string[];
}

export interface BlackBoxConfigRecommendation {
  Setting: string;
  Scope?: string;
  CurrentValue?: string | null;
  SuggestedValue?: string | null;
  Rationale?: string;
  RuleId?: string;
  HowToApply?: string;
}

export interface BlackBoxArmadaImprovement {
  Area?: string;
  Hypothesis: string;
  Evidence?: string;
  Severity?: string;
  RuleId?: string;
  KnownIssue?: string | null;
}

export interface BlackBoxRecoveryStep {
  Description?: string;
  Mcp?: string;
  Rest?: string;
  Cli?: string;
}

export interface BlackBoxRecoveryAction {
  Tool: string;
  Arguments?: Record<string, unknown>;
}

export interface BlackBoxRecoveryPath {
  Id: string;
  Title: string;
  Preconditions?: string[];
  Steps?: BlackBoxRecoveryStep[];
  Action?: BlackBoxRecoveryAction;
  Risk?: string;
  DataLossRisk?: boolean;
  ExpectedOutcome?: string;
}

export interface BlackBoxTriage {
  Verdict?: string;
  Confidence?: string | number;
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
  Kind?: string;
  ReasonExcerpt?: string;
  Runtime?: string;
  Model?: string;
  DurationMs?: number;
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
  | 'Configuration' | 'Attempts' | 'Evidence' | 'Analysis' | 'Related' | 'Voyage';

/** One row of `GET /api/v1/blackbox/{id}/objects`. */
export interface BlackBoxObjectSummary {
  Name: string;
  ContentType?: string;
  SizeBytes?: number;
  UncompressedBytes?: number;
  Sha256?: string;
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
}

/** One step of a recovery run or dry run. */
export interface BlackBoxRecoveryStepResult {
  Order?: number;
  Tool?: string;
  Arguments?: Record<string, unknown>;
  Description?: string;
  Status?: string;
  Success?: boolean;
  Result?: string;
  Error?: string | null;
}

/** Response of `POST /api/v1/blackbox/{id}/recovery/{pathId}`. */
export interface BlackBoxRecoveryResult {
  Success?: boolean;
  EntryId?: string;
  PathId?: string;
  DryRun?: boolean;
  ConfirmToken?: string | null;
  ConfirmTokenExpiresUtc?: string | null;
  DataLossRisk?: boolean;
  Summary?: string;
  ExpectedOutcome?: string;
  Steps?: BlackBoxRecoveryStepResult[];
}

/** One row of `POST /api/v1/blackbox/hotspots/enumerate`. */
export interface BlackBoxHotspot {
  VesselId?: string;
  Directory: string;
  ConflictCount?: number;
  EntryCount?: number;
  FileCount?: number;
  LastSeenUtc?: string;
}

/** Filters for the entry enumerate route (plan 8.1). */
export interface BlackBoxEnumerateQuery {
  PageNumber?: number;
  PageSize?: number;
  TenantId?: string;
  VesselId?: string;
  VoyageId?: string;
  MissionId?: string;
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

/** camelCase payload of the `blackbox.*` WebSocket events (plan 8.3). Only identifiers are relied on. */
export interface BlackBoxSocketEventData {
  id?: string;
  entryId?: string;
  missionId?: string | null;
  voyageId?: string | null;
  vesselId?: string | null;
  missionTitle?: string | null;
  kind?: string | null;
  step?: string | null;
  resolution?: string | null;
}

/** WebSocket server message envelope. */
export interface SocketEnvelope {
  type?: string;
  data?: unknown;
  timestamp?: string;
}
