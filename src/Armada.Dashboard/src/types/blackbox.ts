// Mission and voyage black box DTOs (src/Armada.Core/Models/BlackBox*.cs), camelized by api/client.ts. The REST
// routes are in src/Armada.Server/Routes/BlackBoxRoutes.cs; the record schema is docs/BLACKBOX_SCHEMA.md.

export type BlackBoxSubjectType = 'Mission' | 'Voyage' | 'Unknown';
export type BlackBoxResolution = 'Open' | 'Resolved' | 'Duplicate' | 'WontFix' | 'ArmadaFixed' | 'Unknown';
export type BlackBoxTriage = 'Unknown' | 'UserConfig' | 'MissionBrief' | 'Transient' | 'ArmadaDefect' | 'External';
export type BlackBoxState = 'Captured' | 'Enriched' | 'Analyzed' | 'EnrichmentFailed' | 'Deleting' | 'ObjectMissing' | 'Unknown';
export type BlackBoxCaptureMode = 'Inline' | 'Sweeper' | 'Backfill' | 'Unknown';
export type BlackBoxConfidence = 'Low' | 'Medium' | 'High' | 'Unknown';
export type BlackBoxSeverity = 'Low' | 'Medium' | 'High' | 'Critical' | 'Unknown';
export type BlackBoxFileRole = 'Conflicted' | 'DirtyInCheckout' | 'OutOfScope' | 'ChangedByMission' | 'ChangedByConflictingMission' | 'BoundaryViolation' | 'Unknown';
export type BlackBoxNoteAuthorKind = 'Operator' | 'Agent' | 'AskArmada' | 'System' | 'Unknown';
export type BlackBoxStorageTarget = 'Database' | 'Disk' | 'S3' | 'AzureBlob' | 'GoogleCloud' | 'Cifs' | 'Nfs' | 'Unknown';
export type BlackBoxJobState = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled' | 'Unknown';
export type BlackBoxBundleFormat = 'Markdown' | 'Json';
export type BlackBoxSection =
  | 'Subject' | 'Status' | 'Site' | 'Mechanism' | 'Classification' | 'Workspace' | 'Conflict' | 'Scope'
  | 'Configuration' | 'Attempts' | 'Evidence' | 'Analysis' | 'Related' | 'Voyage' | 'Redaction' | 'Truncation';

/** One row of the entry lists (BlackBoxEntrySummary). */
export interface BlackBoxEntrySummary {
  id: string;
  subjectType: BlackBoxSubjectType;
  missionId?: string | null;
  voyageId?: string | null;
  vesselId?: string | null;
  captainId?: string | null;
  missionTitle?: string | null;
  vesselName?: string | null;
  attempt: number;
  isCurrent: boolean;
  phase: string;
  step: string;
  failureKind?: string | null;
  statusBefore?: string | null;
  statusAfter?: string | null;
  triage: BlackBoxTriage;
  fingerprint?: string | null;
  captureMode: BlackBoxCaptureMode;
  state: BlackBoxState;
  resolution: BlackBoxResolution;
  pinned: boolean;
  reasonExcerpt?: string | null;
  totalBytes: number;
  objectCount: number;
  createdUtc: string;
  expiresUtc?: string | null;
}

/** A full entry (BlackBoxEntry), with the record unless includeRecord was false. */
export interface BlackBoxEntry extends BlackBoxEntrySummary {
  tenantId?: string | null;
  userId?: string | null;
  fleetId?: string | null;
  harborId?: string | null;
  dockId?: string | null;
  mergeEntryId?: string | null;
  incidentId?: string | null;
  stageOutcomeId?: string | null;
  parentMissionId?: string | null;
  supersededById?: string | null;
  resolutionNote?: string | null;
  resolutionLink?: string | null;
  storageTarget?: BlackBoxStorageTarget;
  schemaVersion?: number;
  rulesVersion?: number;
  armadaVersion?: string | null;
  armadaCommit?: string | null;
  summary?: string | null;
  enrichmentAttempts?: number;
  lastError?: string | null;
  enrichedUtc?: string | null;
  analyzedUtc?: string | null;
  resolvedUtc?: string | null;
  lastUpdateUtc?: string;
  record?: BlackBoxRecord | null;
}

export interface BlackBoxSubject {
  subjectType?: BlackBoxSubjectType;
  missionId?: string | null;
  voyageId?: string | null;
  title?: string | null;
  voyageTitle?: string | null;
  vesselId?: string | null;
  vesselName?: string | null;
  vesselRepoUrl?: string | null;
  vesselDefaultBranch?: string | null;
  fleetId?: string | null;
  captainId?: string | null;
  captainName?: string | null;
  captainRuntime?: string | null;
  captainModel?: string | null;
  persona?: string | null;
  pipelineStage?: string | null;
  harborId?: string | null;
  harborName?: string | null;
  dockId?: string | null;
  attempt?: number;
  isRescue?: boolean;
  parentMissionId?: string | null;
  branchName?: string | null;
}

export interface BlackBoxStatusTimelineEntry { status?: string | null; utc?: string | null; source?: string | null }

export interface BlackBoxStatusChange {
  before?: string | null;
  after?: string | null;
  changedUtc?: string | null;
  timeline?: BlackBoxStatusTimelineEntry[];
}

export interface BlackBoxSite {
  phase?: string;
  step?: string;
  hostKind?: 'Admiral' | 'Harbor' | 'Unknown';
  host?: string | null;
  component?: string | null;
  method?: string | null;
  sourceFile?: string | null;
  sourceLine?: number | null;
  armadaVersion?: string | null;
  armadaCommit?: string | null;
}

export interface BlackBoxMechanism {
  exceptionType?: string | null;
  exceptionMessage?: string | null;
  stackFrames?: string[];
  exitCode?: number | null;
  runtimeFailureKind?: string | null;
  failingCommand?: string | null;
  commandExitCode?: number | null;
  commandOutputTail?: string | null;
  gitOperation?: string | null;
  gitStderrTail?: string | null;
}

export interface BlackBoxClassification {
  kind?: string | null;
  rawReason?: string | null;
  suspectedCause?: string;
  causeRuleId?: string | null;
  isRecoverable?: boolean | null;
  isTransient?: boolean | null;
}

export interface BlackBoxWorkspace {
  dockId?: string | null;
  dockState?: string | null;
  worktreePath?: string | null;
  worktreeExists?: boolean | null;
  checkoutPath?: string | null;
  branchName?: string | null;
  branchTip?: string | null;
  missionCommit?: string | null;
  startCommit?: string | null;
  startCommitSource?: string | null;
  targetBranch?: string | null;
  targetTipAtStart?: string | null;
  targetTipAtFailure?: string | null;
  commitsBehindTarget?: number | null;
  commitsAheadOfStart?: number | null;
  checkoutDirty?: boolean | null;
  dirtyPaths?: string[];
  dirtyPathTotal?: number;
  harborLinkState?: string | null;
  harborLastSeenUtc?: string | null;
}

export interface BlackBoxDirectoryCount { directory: string; depth: number; fileCount: number }

export interface BlackBoxCommitRef {
  commitHash: string;
  subject?: string | null;
  author?: string | null;
  committedUtc?: string | null;
  missionId?: string | null;
}

export interface BlackBoxConflictingMission {
  missionId?: string | null;
  title?: string | null;
  commitHash?: string | null;
  landedUtc?: string | null;
  overlappingFiles?: string[];
  overlappingFileTotal?: number;
  isOutsideArmada?: boolean;
  author?: string | null;
}

export interface BlackBoxConflict {
  isConflict?: boolean;
  conflictedFiles?: string[];
  conflictedFileTotal?: number;
  conflictedFilesTruncated?: boolean;
  conflictedDirectories?: BlackBoxDirectoryCount[];
  conflictingMissions?: BlackBoxConflictingMission[];
  targetCommitsSinceStart?: BlackBoxCommitRef[];
  targetCommitTotal?: number;
}

export interface BlackBoxScope {
  scopedFiles?: string[];
  outOfScopeFiles?: string[];
  outOfScopeFileTotal?: number;
  boundaryFindings?: string[];
}

export interface BlackBoxConfigValue { setting: string; value?: string | null; source?: string }

export interface BlackBoxConfiguration { values?: BlackBoxConfigValue[] }

export interface BlackBoxAttempt {
  attempt?: number;
  entryId?: string | null;
  missionId?: string | null;
  stageOutcomeId?: string | null;
  kind?: string | null;
  reasonExcerpt?: string | null;
  runtime?: string | null;
  model?: string | null;
  durationMs?: number | null;
  createdUtc?: string | null;
}

export interface BlackBoxEvidenceItem { name: string; contentType?: string; sizeBytes?: number; uncompressedBytes?: number; excerpt?: string | null; truncated?: boolean; originalBytes?: number | null }

export interface BlackBoxEvidenceIndex { objects?: BlackBoxEvidenceItem[]; checkResults?: unknown[]; papercuts?: unknown[]; diffStat?: unknown; relevantEvents?: unknown[] }

/** A tool call recorded in the entry; recovery paths and recommendations run only these. */
export interface BlackBoxAction { tool: string; arguments?: Record<string, string> }

export interface BlackBoxFinding { ruleId: string; statement: string; confidence: BlackBoxConfidence; evidenceRefs?: string[] }

export interface BlackBoxConfigRecommendation {
  id: string;
  setting: string;
  scope?: string;
  currentValue?: string | null;
  suggestedValue?: string | null;
  rationale?: string | null;
  ruleId?: string | null;
  howToApply?: string | null;
  applyAction?: BlackBoxAction | null;
}

export interface BlackBoxImprovement {
  area?: string | null;
  hypothesis: string;
  evidence?: string | null;
  sourcePointers?: string[];
  severity?: BlackBoxSeverity;
  ruleId?: string | null;
  knownIssue?: string | null;
}

export interface BlackBoxRecoveryStep {
  order: number;
  description: string;
  mcpCommand?: string | null;
  restCommand?: string | null;
  cliCommand?: string | null;
  action?: BlackBoxAction | null;
  dataLossRisk?: boolean;
}

export interface BlackBoxRecoveryPath {
  id: string;
  rank: number;
  title: string;
  preconditions?: string[];
  steps?: BlackBoxRecoveryStep[];
  action?: BlackBoxAction | null;
  risk?: BlackBoxSeverity;
  dataLossRisk?: boolean;
  expectedOutcome?: string | null;
  ruleId?: string | null;
  /** The active runbook this path starts instead of its own steps (black box runbook linkage). */
  runbookId?: string | null;
  runbookTitle?: string | null;
  /** Share of settled confirmed runs of this path that recovered the mission; null until three have settled. */
  successRate?: number | null;
  trackedRuns?: number;
}

export interface BlackBoxTriageVote { ruleId: string; verdict: BlackBoxTriage; weight: number }

export interface BlackBoxTriageVerdict {
  verdict: BlackBoxTriage;
  confidence: BlackBoxConfidence;
  votes?: BlackBoxTriageVote[];
  tiedWith?: BlackBoxTriage[];
}

export interface BlackBoxAnalysis {
  summary?: string | null;
  summaryIsModelWritten?: boolean;
  why?: BlackBoxFinding[];
  configRecommendations?: BlackBoxConfigRecommendation[];
  armadaImprovements?: BlackBoxImprovement[];
  recoveryPaths?: BlackBoxRecoveryPath[];
  triage?: BlackBoxTriageVerdict | null;
}

export interface BlackBoxRedactionRuleCount { rule?: string; count?: number }

export interface BlackBoxRedaction { rulesApplied?: BlackBoxRedactionRuleCount[]; redactedCount?: number }

export interface BlackBoxTruncatedField { field: string; originalSize?: number; keptSize?: number; unit?: string }

export interface BlackBoxTruncation { fields?: BlackBoxTruncatedField[]; fieldTotal?: number; timedOut?: boolean; notes?: string[] }

/** An earlier entry with the same fingerprint (BlackBoxSimilarEntry). */
export interface BlackBoxSimilarEntry {
  entryId: string;
  missionId?: string | null;
  missionTitle?: string | null;
  createdUtc?: string;
  resolution?: BlackBoxResolution;
  resolutionNote?: string | null;
}

/** Related failures (BlackBoxRelated): the fingerprint, similar entries and the cascade. */
export interface BlackBoxRelated {
  fingerprint?: string | null;
  similarEntries?: BlackBoxSimilarEntry[];
  occurrencesLast30Days?: number;
  cascadeRootEntryId?: string | null;
  downstreamMissionIds?: string[];
  /** The first ten successful landings near the failure that touched the same files. */
  nearbySuccesses?: BlackBoxNearbySuccess[];
  nearbySuccessTotal?: number;
}

/** The versioned record (record.json); a section that was not captured is null. */
export interface BlackBoxRecord {
  entryId: string;
  schemaVersion: number;
  rulesVersion?: number;
  captureMode?: BlackBoxCaptureMode;
  state?: BlackBoxState;
  createdUtc?: string;
  subject?: BlackBoxSubject | null;
  status?: BlackBoxStatusChange | null;
  site?: BlackBoxSite | null;
  mechanism?: BlackBoxMechanism | null;
  classification?: BlackBoxClassification | null;
  workspace?: BlackBoxWorkspace | null;
  conflict?: BlackBoxConflict | null;
  scope?: BlackBoxScope | null;
  configuration?: BlackBoxConfiguration | null;
  attempts?: BlackBoxAttempt[];
  evidence?: BlackBoxEvidenceIndex | null;
  analysis?: BlackBoxAnalysis | null;
  related?: BlackBoxRelated | null;
  voyage?: Record<string, unknown> | null;
  redaction?: BlackBoxRedaction;
  truncation?: BlackBoxTruncation;
}

/** GET /api/v1/blackbox/{id}/sections/{section}: a record carrying only that section. */
export interface BlackBoxSectionResult { entryId: string; section: BlackBoxSection; record: BlackBoxRecord }

export interface BlackBoxObjectSummary {
  name: string;
  contentType: string;
  contentEncoding: string;
  sizeBytes: number;
  uncompressedBytes: number;
  sha256?: string | null;
  isMissing: boolean;
  createdUtc: string;
}

/** One read of GET /api/v1/blackbox/{id}/objects/{name} (at most 1 MB). */
export interface BlackBoxObjectRead {
  contentType: string;
  /** The object's SHA-256 (the ETag without quotes). */
  etag: string | null;
  offset: number;
  totalBytes: number;
  /** Where the next read starts, or null at the end. */
  nextOffset: number | null;
  /** UTF-8 text for text and JSON objects, otherwise null (read body). */
  text: string | null;
  body: Blob;
}

export interface BlackBoxNote {
  id: string;
  entryId: string;
  tenantId?: string | null;
  userId?: string | null;
  authorKind: BlackBoxNoteAuthorKind;
  body: string;
  createdUtc: string;
}

/** Filters of the entry lists (BlackBoxEnumerateRequest); every field optional. */
export interface BlackBoxEnumerateQuery {
  pageNumber?: number;
  pageSize?: number;
  order?: 'CreatedDescending' | 'CreatedAscending';
  tenantId?: string;
  fleetId?: string;
  vesselId?: string;
  voyageId?: string;
  missionId?: string;
  captainId?: string;
  harborId?: string;
  subjectType?: BlackBoxSubjectType;
  phase?: string;
  step?: string;
  kind?: string;
  triage?: BlackBoxTriage;
  resolution?: BlackBoxResolution;
  state?: BlackBoxState;
  captureMode?: BlackBoxCaptureMode;
  fingerprint?: string;
  pinned?: boolean;
  isCurrent?: boolean;
  path?: string;
  createdAfter?: string;
  createdBefore?: string;
}

export interface BlackBoxSearchQuery extends BlackBoxEnumerateQuery { query: string }

export interface BlackBoxBundleQuery extends BlackBoxEnumerateQuery { max?: number; format?: BlackBoxBundleFormat; redactHosts?: boolean }

export interface BlackBoxBundleResult { format: BlackBoxBundleFormat; entryCount: number; fingerprintCount: number; content: string; truncated: boolean }

export interface BlackBoxHotspotQuery { pageNumber?: number; pageSize?: number; vesselId?: string; role?: BlackBoxFileRole; createdAfter?: string; createdBefore?: string; tenantId?: string }

export interface BlackBoxHotspot { vesselId?: string | null; directory: string; fileCount: number; entryCount: number; lastSeenUtc?: string | null }

export interface BlackBoxFingerprintQuery { pageNumber?: number; pageSize?: number; vesselId?: string; resolution?: BlackBoxResolution; createdAfter?: string; createdBefore?: string; tenantId?: string }

export interface BlackBoxFingerprintSummary {
  fingerprint: string;
  count: number;
  openCount: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
  latestEntryId?: string | null;
  phase: string;
  step: string;
  failureKind?: string | null;
}

export interface BlackBoxResolveRequest { resolution: BlackBoxResolution; note?: string | null; link?: string | null }

/** Body of the recovery route. dryRun defaults to true on the server. */
export interface BlackBoxRecoveryRunRequest { dryRun?: boolean; confirmToken?: string | null; acknowledgeDataLoss?: boolean; authorKind?: BlackBoxNoteAuthorKind }

export interface BlackBoxRecoveryStepResult { order: number; tool?: string | null; succeeded: boolean; message?: string | null }

export interface BlackBoxRecoveryRunResult {
  entryId: string;
  pathId: string;
  dryRun: boolean;
  confirmToken?: string | null;
  confirmTokenExpiresUtc?: string | null;
  plan?: BlackBoxRecoveryStep[];
  stepResults?: BlackBoxRecoveryStepResult[];
  succeeded: boolean;
  /** True when a step's precondition is not met (no token on a dry run; nothing ran on a confirm). */
  refused?: boolean;
  unmetPreconditions?: string[];
  message?: string | null;
}

export interface BlackBoxRecommendationApplyRequest { dryRun?: boolean; confirmToken?: string | null; authorKind?: BlackBoxNoteAuthorKind }

export interface BlackBoxRecommendationApplyResult {
  entryId: string;
  recommendationId: string;
  dryRun: boolean;
  confirmToken?: string | null;
  confirmTokenExpiresUtc?: string | null;
  action?: BlackBoxAction | null;
  succeeded: boolean;
  message?: string | null;
}

export interface BlackBoxDeleteResult { requested: number; deleted: number; pending: number; bytesFreed: number; skipped?: string[] }

export interface BlackBoxPurgeQuery extends BlackBoxEnumerateQuery { dryRun?: boolean; includePinned?: boolean; maxEntries?: number }

export interface BlackBoxPurgeResult {
  dryRun: boolean;
  matched: number;
  deleted: number;
  pending: number;
  skippedPinned: number;
  bytesFreed: number;
  sampleEntryIds?: string[];
}

export interface BlackBoxGcReport {
  dryRun: boolean;
  startedUtc: string;
  finishedUtc?: string | null;
  durationMs?: number;
  expired: number;
  evicted: number;
  cascaded: number;
  retriedDeletes: number;
  orphansRemoved: number;
  objectsMissing: number;
  bytesFreed: number;
  failures: number;
  candidateEntryIds?: string[];
  errors?: string[];
}

export interface BlackBoxRetentionPolicy {
  retentionDays: number;
  resolvedRetentionDays: number;
  keepLatestPerMission: boolean;
  maxEntriesPerVessel: number;
  maxTotalBytes: number;
  deleteWithMission: boolean;
  deleteWithVessel: boolean;
  gcIntervalMinutes: number;
  gcBatchSize: number;
  orphanGraceHours: number;
}

export interface BlackBoxRetentionStatus {
  policy: BlackBoxRetentionPolicy;
  totalEntries: number;
  pinnedEntries: number;
  deletingEntries: number;
  expiredEntries: number;
  totalBytes: number;
  nextRunUtc?: string | null;
  lastRun?: BlackBoxGcReport | null;
}

export interface BlackBoxStoreHealth { target: BlackBoxStorageTarget; healthy: boolean; message?: string | null; latencyMs?: number | null; checkedUtc: string }

export interface BlackBoxStorageTargetUsage { target: BlackBoxStorageTarget; entries: number; bytes: number }

export interface BlackBoxStorageStatus {
  target: BlackBoxStorageTarget;
  fallbackToDatabase: boolean;
  health?: BlackBoxStoreHealth | null;
  usage?: BlackBoxStorageTargetUsage[];
  activeMigrationId?: string | null;
}

export interface BlackBoxStorageMigrationRequest { to: BlackBoxStorageTarget; from?: BlackBoxStorageTarget | null; deleteSource?: boolean; batchSize?: number }

export interface BlackBoxStorageMigrationJob {
  id: string;
  state: BlackBoxJobState;
  from?: BlackBoxStorageTarget | null;
  to: BlackBoxStorageTarget;
  deleteSource: boolean;
  totalEntries: number;
  migratedEntries: number;
  failedEntries: number;
  bytesCopied: number;
  lastEntryId?: string | null;
  lastError?: string | null;
  createdUtc: string;
  startedUtc?: string | null;
  finishedUtc?: string | null;
}

export interface BlackBoxBackfillRequest { since?: string | null; maxSubjects?: number }

export interface BlackBoxBackfillResult {
  mode: BlackBoxCaptureMode;
  scanned: number;
  captured: number;
  skipped: number;
  failed: number;
  enrichmentRequeued: number;
  startedUtc: string;
  finishedUtc?: string | null;
}

/** Data of a black box REST error (BlackBoxErrorDetail): read it with apiErrorCode. */
export interface BlackBoxErrorDetail { code: string; field?: string | null }

/** Occurrences of one failure mode in one Armada version (BlackBoxVersionCount). */
export interface BlackBoxVersionCount {
  armadaVersion: string;
  count: number;
  openCount: number;
  armadaFixedCount: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
}

/** GET /api/v1/blackbox/fingerprints/{fingerprint}/trend (BlackBoxFingerprintTrend). */
export interface BlackBoxFingerprintTrend {
  fingerprint: string;
  phase: string;
  step: string;
  total: number;
  truncated: boolean;
  versions: BlackBoxVersionCount[];
}

/** POST /api/v1/blackbox/{id}/export-fixture (BlackBoxFixture): a sanitized record with the expected rules and triage. */
export interface BlackBoxFixture {
  name: string;
  description?: string | null;
  source?: string | null;
  rulesVersion: number;
  expectedRuleIds: string[];
  expectedTriage: BlackBoxTriage;
  record: BlackBoxRecord;
}

/** One storage target's settings (blackBox.storage.disk, .s3, ...): strings, numbers and flags; secrets read back masked. */
export type BlackBoxTargetSettings = Record<string, string | number | boolean | null | undefined>;

/** blackBox.storage in GET/PUT /api/v1/settings (BlackBoxStorageSettings). */
export interface BlackBoxStorageSettingsData {
  target: BlackBoxStorageTarget;
  keyPrefix?: string;
  compress?: boolean;
  maxEvidenceObjectBytes?: number;
  maxEntryBytes?: number;
  fallbackToDatabase?: boolean;
  disk?: BlackBoxTargetSettings;
  s3?: BlackBoxTargetSettings;
  azureBlob?: BlackBoxTargetSettings;
  googleCloud?: BlackBoxTargetSettings;
  cifs?: BlackBoxTargetSettings;
  nfs?: BlackBoxTargetSettings;
  [key: string]: unknown;
}

/** blackBox in GET/PUT /api/v1/settings (BlackBoxSettings); the PUT replaces the whole group. */
export interface BlackBoxSettingsData {
  enabled?: boolean;
  captureTimeoutMs?: number;
  storage: BlackBoxStorageSettingsData;
  retention: BlackBoxRetentionPolicy;
  [key: string]: unknown;
}

// ==================== Prevention, forensics and operations (BLACKBOX.md section 12, routes in BlackBoxInsightRoutes.cs)

export type MissionOverlapKind = 'ActiveMission' | 'Hotspot' | 'Reservation' | 'Unknown';
export type BriefLintSeverity = 'Info' | 'Warning' | 'Unknown';
export type BlackBoxTimelineSource = 'Entry' | 'Note' | 'Mission' | 'Voyage' | 'Dock' | 'MergeEntry' | 'Captain' | 'Harbor' | 'Unknown';
export type BlackBoxCascadeEdgeKind = 'DependsOn' | 'CancelledBecause' | 'BlockedBy' | 'Sibling' | 'Unknown';

/** One overlap a mission has with the vessel's active work (MissionOverlapWarning). */
export interface MissionOverlapWarning {
  kind: MissionOverlapKind;
  missionId?: string | null;
  overlappingMissionId?: string | null;
  overlappingMissionTitle?: string | null;
  overlappingMissionStatus?: string | null;
  directory?: string | null;
  hotspotEntryCount?: number;
  files?: string[];
  fileCount?: number;
  /** True when dispatch chained the mission after the overlapping one (vessel serializeOverlappingMissions). */
  serialized?: boolean;
  message: string;
}

/** One brief lint finding (BriefLintFinding): BRIEF-DEP-001, BRIEF-NOOP-001, BRIEF-PATH-001, BRIEF-PATH-002. */
export interface BriefLintFinding {
  ruleId: string;
  severity: BriefLintSeverity;
  message: string;
  excerpt?: string | null;
  path?: string | null;
  suggestion?: string | null;
}

export interface BriefLintRequest {
  title?: string | null;
  description?: string | null;
  vesselId?: string | null;
  dependsOnMissionId?: string | null;
  voyageId?: string | null;
}

export interface BriefLintResult {
  findings: BriefLintFinding[];
  findingCount: number;
  clean: boolean;
  pathsChecked: boolean;
}

export interface MissionOverlapPreviewRequest {
  title?: string | null;
  description?: string | null;
  dependsOnMissionId?: string | null;
  excludeMissionId?: string | null;
}

export interface MissionOverlapPreview {
  vesselId: string;
  serializeOverlappingMissions: boolean;
  likelyFiles: string[];
  likelyFileCount: number;
  scopedFileCount: number;
  briefPathCount: number;
  trackedFilesKnown: boolean;
  warnings: MissionOverlapWarning[];
  warningCount: number;
  suggestedDependsOnMissionId?: string | null;
  activeMissionsCompared: number;
  activeMissionsTruncated: boolean;
  briefLint?: BriefLintResult | null;
}

/** A soft directory reservation: an active mission working in a conflict hotspot directory. */
export interface VesselReservation {
  vesselId: string;
  directory: string;
  missionId: string;
  missionTitle?: string | null;
  missionStatus?: string | null;
  hotspotEntryCount: number;
  fileCount: number;
  sinceUtc: string;
}

export interface VesselReservationPage {
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  totalRecords: number;
  objects: VesselReservation[];
  vesselId: string;
  threshold: number;
  hotDirectoryCount: number;
  activeMissionsScanned: number;
  scanTruncated: boolean;
}

/** A configuration recommendation that recurs across a vessel's entries (BlackBoxDoctorFinding). */
export interface BlackBoxDoctorFinding {
  key: string;
  ruleId?: string | null;
  recommendationId?: string | null;
  setting: string;
  scope?: string;
  currentValue?: string | null;
  suggestedValue?: string | null;
  rationale?: string | null;
  count: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
  exampleEntryId: string;
  exampleMissionId?: string | null;
  /** True when the example entry's recommendation has an apply action. */
  applicable: boolean;
}

export interface BlackBoxDoctorPage {
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  totalRecords: number;
  objects: BlackBoxDoctorFinding[];
  vesselId: string;
  windowDays: number;
  entriesInWindow: number;
  entriesScanned: number;
  scanTruncated: boolean;
}

export interface BlackBoxDoctorQuery { windowDays?: number; pageNumber?: number; pageSize?: number }

/** One value that differs between two entries (BlackBoxDiffField). */
export interface BlackBoxDiffField { section: string; field: string; value?: string | null; otherValue?: string | null }

export interface BlackBoxAttemptDiff {
  entryId: string;
  otherEntryId: string;
  sameMission: boolean;
  attempt: number;
  otherAttempt: number;
  sameFingerprint: boolean;
  differences: BlackBoxDiffField[];
  differenceCount: number;
  filesOnlyInEntry: string[];
  filesOnlyInEntryCount: number;
  filesOnlyInOther: string[];
  filesOnlyInOtherCount: number;
  filesInBothCount: number;
  changedFilesKnown: boolean;
}

/** A successful landing near a failure that touched the same files (BlackBoxNearbySuccess). */
export interface BlackBoxNearbySuccess {
  successId: string;
  missionId: string;
  missionTitle?: string | null;
  landedUtc: string;
  hoursFromFailure: number;
  baseCommit?: string | null;
  targetCommit?: string | null;
  runMs?: number | null;
  overlappingFiles: string[];
  overlappingFileCount: number;
}

export interface BlackBoxTimelineItem {
  timestampUtc: string;
  source: BlackBoxTimelineSource;
  kind: string;
  message: string;
  entityId?: string | null;
}

export interface BlackBoxTimelineSourceCount { source: BlackBoxTimelineSource; count: number; truncated: boolean }

export interface BlackBoxTimelinePage {
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  totalRecords: number;
  objects: BlackBoxTimelineItem[];
  entryId: string;
  windowStartUtc?: string | null;
  windowEndUtc?: string | null;
  sources: BlackBoxTimelineSourceCount[];
  truncated: boolean;
}

export interface BlackBoxTimelineQuery { pageNumber?: number; pageSize?: number; order?: 'CreatedAscending' | 'CreatedDescending' }

export interface BlackBoxCascadeNode {
  missionId: string;
  title?: string | null;
  status: string;
  failureKind?: string | null;
  entryId?: string | null;
  dependsOnMissionId?: string | null;
  isRoot: boolean;
}

export interface BlackBoxCascadeEdge { fromMissionId: string; toMissionId: string; kind: BlackBoxCascadeEdgeKind }

export interface BlackBoxCascadeGraph {
  voyageId: string;
  voyageTitle?: string | null;
  voyageStatus: string;
  entryId?: string | null;
  rootMissionId?: string | null;
  rootEntryId?: string | null;
  nodes: BlackBoxCascadeNode[];
  edges: BlackBoxCascadeEdge[];
  missionCount: number;
  truncated: boolean;
}

/** Outcomes of one recovery path's confirmed runs (BlackBoxRecoveryStat). */
export interface BlackBoxRecoveryStat {
  ruleId?: string | null;
  pathId: string;
  runs: number;
  succeeded: number;
  failed: number;
  pending: number;
  expired: number;
  successRate?: number | null;
  lastRunUtc?: string | null;
}

export interface BlackBoxRecoveryStatsQuery { ruleId?: string; vesselId?: string; pageNumber?: number; pageSize?: number }
