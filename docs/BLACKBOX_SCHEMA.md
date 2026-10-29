# Black box schema (consumer reference)

This is the reference for applications that consume Armada black box entries: every object in an entry, every
`record.json` section and field with its type, nullability, bounds and enum values, the REST and MCP shapes, the event
payloads, and the versioning and compatibility policy. The machine-readable form is the JSON Schema (draft 2020-12)
[blackbox-record.schema.json](blackbox-record.schema.json), generated from the C# models by
`BlackBoxSchemaGenerator`; the field tables below are rendered from the same models. The developer contract (services,
interfaces, storage, database) is in [BLACKBOX.md](BLACKBOX.md).

## Contents

1. Conventions
2. Versioning and compatibility
3. Objects in an entry
4. record.json sections and fields
5. REST and MCP shapes
6. Event payloads
7. Example

## 1. Conventions

- JSON property names are camelCase. Enums are written by name (strings). Timestamps are ISO 8601 UTC strings.
- Every field is written, including nulls. A section that was not captured is `null`; lists are never null (empty
  when there is nothing).
- "Required" in the tables means the field is non-nullable and always present in output Armada writes. A consumer
  must still tolerate it being absent in a record written by another version.
- Bounds are hard limits enforced when the record is written: lists by `[CollectionBound]` (`maxItems`), strings by
  `BlackBoxRecordBounder` (`maxLength`; strings inside lists are capped at 1024 characters). Anything cut is listed in
  `truncation.fields` with its original size, and totals such as `conflictedFileTotal` keep the real count. Nothing is
  dropped silently.
- Units: sizes are bytes, durations milliseconds, `*Utc` fields UTC, `*Chars` caps characters.
- Secrets never appear: they are replaced by `[REDACTED]` and counted in `redaction` (rule names and counts only).
- Ids carry prefixes: `bbx_` entry, `bbo_` object, `bbf_` file row, `bbl_` link row, `bbn_` note, `bbm_` storage
  migration job, plus Armada's `msn_`, `vyg_`, `vsl_`, `cpt_`, `dck_`, `hbr_`, `mrg_`, `inc_`, `sto_`.

## 2. Versioning and compatibility

- `record.json` carries `schemaVersion` (currently 1; the schema file carries it as `x-armada-schemaVersion`).
- Additive changes keep the version: new optional fields, new sections, new enum values, larger limits.
- Renames, removals, type changes and meaning changes bump `schemaVersion`.
- Readers must ignore unknown fields and treat unknown enum values as `Unknown`. Armada's own reader
  (`BlackBoxJson`) does exactly that: unknown properties are skipped, an unknown enum name or number reads as the
  enum's `Unknown` member (every black box enum has one), and a nullable Armada enum without an `Unknown` member (for
  example `runtimeFailureKind`) reads as null.
- Armada can re-render older records: briefs are rendered live from `record.json`, so an entry written by an older
  version still renders with the current renderer, and re-analysis (`reanalyze`) rewrites `record.json` at the current
  version.
- Reading an older schema version through `GET .../blackbox/{id}?schemaVersion=N` is out of scope unless a consumer
  needs it.
- The drift test `Models.BlackBoxSchema/schema_matches_models` fails when the models change without regenerating the
  schema (`scripts/common/generate-blackbox-schema.sh`).

## 3. Objects in an entry

Every object has a stable name and content type. Objects are stored gzip-compressed when `blackBox.storage.compress`
is on; reads always return the decoded bytes. `sha256` is over the stored (encoded) bytes.

| Name | Content type | Content |
|---|---|---|
| `record.json` | application/json | The `BlackBoxRecord` (section 4). At most 256 KB. |
| `brief.md` | text/markdown | FIX THIS brief, agent audience: triage preamble, evidence, recovery paths, answer instructions. Targets 12 KB, at most 40 KB. |
| `brief.user.md` | text/markdown | Operator audience: plain language, no stack frames or source pointers. |
| `brief.maintainer.md` | text/markdown | Maintainer audience: build identity, source pointers, Armada improvement hypotheses. |
| `evidence/agent-output.txt` | text/plain | Tail of the captain's output. |
| `evidence/mission.log` | text/plain | Tail of the mission log. |
| `evidence/diff.patch` | text/x-diff | The mission's diff. |
| `evidence/diffstat.txt` | text/plain | Diff stat. |
| `evidence/check-<n>.txt` | text/plain | Output of stage check n. |
| `evidence/verification-<n>.txt` | text/plain | Output of landing verification n. |
| `evidence/merge-entry.txt` | text/plain | Merge-queue entry test output. |
| `evidence/git-status.txt` | text/plain | git status of the worktree or checkout. |
| `evidence/target-commits.txt` | text/plain | Target commits since the mission started. |
| `evidence/events.json` | application/json | The relevant events. |
| `manifest.json` | application/json | Only inside a `.zip` download: `BlackBoxDownloadManifest` (entry id, schema version, objects with name, content type, size and SHA-256). |

Evidence objects are at most `blackBox.storage.maxEvidenceObjectBytes` (default 1 MB) uncompressed; larger content
keeps its head and tail around a marker line `[... cut by Armada: N bytes originally, head and tail kept ...]`. The
set of evidence objects grows with enrichment; consumers list objects instead of assuming names.

## 4. record.json sections and fields

The root is `BlackBoxRecord`. Section order in the brief: Summary, Identity, Where, How, Status timeline, Why,
Conflict, Workspace, Scope, Configuration, Prevention, Improvements, Recovery, Attempts, Evidence, Related.

### BlackBoxRecord

The content of a black box entry's record.json: a versioned forensic snapshot of one mission or voyage failure, deserialized into typed classes (never read through JsonElement). A section that was not captured is null; readers render whatever sections exist. Every string is capped and every collection bounded (BlackBoxLimits).

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes | at most 2000 chars | Entry identifier (bbx_ prefix). |
| `schemaVersion` | integer | no | yes |  | Record schema version (BlackBoxLimits.SchemaVersion for this build). |
| `rulesVersion` | integer | no | yes |  | Advisor rules version that produced the Analysis section (0 when not analyzed). |
| `captureMode` | enum: `Inline`, `Sweeper`, `Backfill`, `Unknown` | no | yes |  | How the entry was captured. |
| `state` | enum: `Captured`, `Enriched`, `Analyzed`, `EnrichmentFailed`, `Deleting`, `ObjectMissing`, `Unknown` | no | yes |  | Processing state when the record was last written. |
| `createdUtc` | string (date-time, UTC) | no | yes |  | When the failure was captured (UTC). |
| `subject` | [BlackBoxSubject](#blackboxsubject) or null | yes | no |  | Identity of the failed mission or voyage. |
| `status` | [BlackBoxStatusChange](#blackboxstatuschange) or null | yes | no |  | Status before and after, and the timeline. |
| `site` | [BlackBoxSite](#blackboxsite) or null | yes | no |  | Where it failed. |
| `mechanism` | [BlackBoxMechanism](#blackboxmechanism) or null | yes | no |  | How it failed. |
| `classification` | [BlackBoxClassification](#blackboxclassification) or null | yes | no |  | Kind, raw reason and suspected cause. |
| `workspace` | [BlackBoxWorkspace](#blackboxworkspace) or null | yes | no |  | Dock, worktree, branch and target state; null until captured or enriched. |
| `conflict` | [BlackBoxConflict](#blackboxconflict) or null | yes | no |  | Conflicted files, directories and conflicting missions; null when not a conflict or not yet enriched. |
| `scope` | [BlackBoxScope](#blackboxscope) or null | yes | no |  | Scoped, out-of-scope and boundary files; null when not applicable. |
| `configuration` | [BlackBoxConfiguration](#blackboxconfiguration) or null | yes | no |  | Effective configuration that shaped the failure. |
| `attempts` | array of [BlackBoxAttempt](#blackboxattempt) | no | yes | at most 20 items | Prior attempts and the rescue chain, oldest first. |
| `evidence` | [BlackBoxEvidenceIndex](#blackboxevidenceindex) or null | yes | no |  | Index of the evidence objects. |
| `analysis` | [BlackBoxAnalysis](#blackboxanalysis) or null | yes | no |  | Advisor output; null until analyzed. |
| `related` | [BlackBoxRelated](#blackboxrelated) or null | yes | no |  | Fingerprint and similar entries. |
| `voyage` | [BlackBoxVoyageRollup](#blackboxvoyagerollup) or null | yes | no |  | Voyage roll-up (voyage entries only). |
| `redaction` | [BlackBoxRedaction](#blackboxredaction) | no | yes |  | Which redaction rules fired and how often (never the values). |
| `truncation` | [BlackBoxTruncation](#blackboxtruncation) | no | yes |  | Fields that were cut, with their original sizes, and capture notes. |

### BlackBoxSubject

Identity section of a black box record: the failed mission or voyage and everything it ran with.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `subjectType` | enum: `Mission`, `Voyage`, `Unknown` | no | yes |  | Mission or voyage. |
| `tenantId` | string | yes | no | at most 2000 chars | Tenant. |
| `userId` | string | yes | no | at most 2000 chars | Owner. |
| `missionId` | string | yes | no | at most 2000 chars | Mission (msn_ prefix), or null for a voyage entry. |
| `voyageId` | string | yes | no | at most 2000 chars | Voyage (vyg_ prefix), or null. |
| `title` | string | yes | no | at most 500 chars | Mission title (or voyage title for a voyage entry). |
| `voyageTitle` | string | yes | no | at most 500 chars | Voyage title, or null. |
| `vesselId` | string | yes | no | at most 2000 chars | Vessel, or null. |
| `vesselName` | string | yes | no | at most 2000 chars | Vessel name, or null. |
| `vesselRepoUrl` | string | yes | no | at most 2000 chars | Vessel repository URL (credentials redacted), or null. |
| `vesselDefaultBranch` | string | yes | no | at most 2000 chars | Vessel default (target) branch, or null. |
| `fleetId` | string | yes | no | at most 2000 chars | Fleet, or null. |
| `captainId` | string | yes | no | at most 2000 chars | Captain, or null. |
| `captainName` | string | yes | no | at most 2000 chars | Captain name, or null. |
| `captainRuntime` | string | yes | no | at most 2000 chars | Captain runtime (AgentRuntimeEnum name), or null. |
| `captainModel` | string | yes | no | at most 2000 chars | Model the captain ran, or null. |
| `modelEndpointId` | string | yes | no | at most 2000 chars | Model endpoint, or null. |
| `persona` | string | yes | no | at most 2000 chars | Persona the mission routed as, or null. |
| `pipelineStage` | string | yes | no | at most 2000 chars | Pipeline stage name, or null. |
| `harborId` | string | yes | no | at most 2000 chars | Harbor the dock ran on, or null for the Admiral host. |
| `harborName` | string | yes | no | at most 2000 chars | Harbor name, or null. |
| `dockId` | string | yes | no | at most 2000 chars | Dock, or null. |
| `mergeEntryId` | string | yes | no | at most 2000 chars | Merge-queue entry, or null. |
| `incidentId` | string | yes | no | at most 2000 chars | Incident, or null. |
| `stageOutcomeId` | string | yes | no | at most 2000 chars | Stage outcome of the attempt, or null. |
| `attempt` | integer | no | yes |  | Attempt number of this entry for the subject, starting at 1. |
| `isRescue` | boolean | no | yes |  | Whether the mission is a rescue. |
| `parentMissionId` | string | yes | no | at most 2000 chars | Parent mission, or null. |
| `branchName` | string | yes | no | at most 2000 chars | Mission branch, or null. |

### BlackBoxStatusChange

Status section: the subject's status before and after the failure write, and its timeline.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `before` | string | yes | no | at most 2000 chars | Status before the failure write (MissionStatusEnum or VoyageStatusEnum name), or null when unknown. |
| `after` | string | yes | no | at most 2000 chars | Status after the failure write. |
| `changedUtc` | string (date-time, UTC) | yes | no |  | When the status changed (UTC). |
| `timeline` | array of [BlackBoxStatusTimelineEntry](#blackboxstatustimelineentry) | no | yes | at most 100 items | Status changes in the attempt, oldest first. |

### BlackBoxStatusTimelineEntry

One status change in a black box status timeline.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `status` | string | no | yes | at most 2000 chars | Status name. |
| `utc` | string (date-time, UTC) | no | yes |  | When (UTC). |
| `source` | string | yes | no | at most 2000 chars | What set it (event type or component), or null. |

### BlackBoxSite

Site section: where a failure happened, down to the Armada source line.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `phase` | enum: `Unknown`, `Dispatch`, `Provision`, `Execution`, `Completion`, `Review`, `Handoff`, `Landing`, `MergeQueue`, `Recovery`, `Voyage`, `External` | no | yes |  | Phase (derived from Step). |
| `step` | enum: `Unknown`, `VoyageParentNotRunnable`, `AssignmentFailed`, `NoEligibleCaptain`, `HarborUnavailable`, `MemoryPressure`, `DockProvision`, `WorktreeCreate`, `BranchCreate`, `StartCommitRecord`, `ProcessCrash`, `UsageLimit`, `AuthFailure`, `ModelUnavailable`, `Stall`, `MaxRuntime`, `RecoveryExhausted`, `Interrupted`, `OperatorStop`, `NoOp`, `Unfinished`, `CaptainBlocked`, `InvalidOutput`, `ScopeViolation`, `BoundaryViolation`, `DefinitionOfDone`, `StageCheck`, `JudgeRejected`, `ReviewDenied`, `ReviewTimeout`, `ReviewApprovedDockMissing`, `PlanParse`, `StageBranchHarden`, `DependencyCancelled`, `IntegrationBarrier`, `DockMissing`, `TargetIntegration`, `Verification`, `DirtyCheckout`, `UnpublishedAncestry`, `Push`, `PullRequest`, `FastForward`, `LocalMerge`, `RetryLanding`, `DeferralExpired`, `QueueResolveRepo`, `QueueMergeConflict`, `QueueTests`, `QueueProcessing`, `QueuePush`, `RescueFailed`, `RedispatchExhausted`, `OrphanedExit`, `UnconfirmedStop`, `MissingMergeHandoff`, `SettlementFailed`, `Halted`, `Cancelled`, `StageFailed`, `VesselDeleted`, `OperatorTransition` | no | yes |  | Step the capture site reported. |
| `hostKind` | enum: `Admiral`, `Harbor`, `Unknown` | no | yes |  | Which host the failure happened on. |
| `host` | string | yes | no | at most 2000 chars | Host label: Admiral, or Harbor:{harborId}. |
| `component` | string | yes | no | at most 2000 chars | Component (class) that captured the failure. |
| `method` | string | yes | no | at most 2000 chars | Method that captured the failure ([CallerMemberName]). |
| `sourceFile` | string | yes | no | at most 2000 chars | Repository-relative source file ([CallerFilePath], trimmed). |
| `sourceLine` | integer | yes | no |  | Source line ([CallerLineNumber]). |
| `armadaVersion` | string | yes | no | at most 2000 chars | Armada build version. |
| `armadaCommit` | string | yes | no | at most 2000 chars | Armada build commit, or null when unknown. |

### BlackBoxMechanism

Mechanism section: how a failure happened.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `exceptionType` | string | yes | no | at most 2000 chars | Exception type, or null. |
| `exceptionMessage` | string | yes | no | at most 4000 chars | Exception message (redacted, at most 4000 characters). |
| `stackFrames` | array of string | no | yes | at most 20 items; each at most 1024 chars | Armada stack frames only. |
| `exitCode` | integer | yes | no |  | Agent exit code, or null. |
| `runtimeFailureKind` | enum: `Clean`, `UsageLimit`, `AuthFailure`, `Crash`, `ModelUnavailable`, `TaskFailure`, `Transient` | yes | no |  | How the runtime classified the exit, or null. |
| `failingCommand` | string | yes | no | at most 2000 chars | Failing command, or null. |
| `commandExitCode` | integer | yes | no |  | Failing command's exit code, or null. |
| `commandOutputTail` | string | yes | no | at most 8000 chars | Tail of the failing command's output (at most 8000 characters). |
| `gitOperation` | string | yes | no | at most 2000 chars | Git operation that failed, or null. |
| `gitStderrTail` | string | yes | no | at most 4000 chars | Tail of git stderr (at most 4000 characters). |

### BlackBoxClassification

Classification section: the failure kind, the raw reason and the suspected cause.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `kind` | enum: `Compile`, `TestFail`, `Timeout`, `LandingConflict`, `Crash`, `NoOp`, `Boundary`, `ScopeViolation`, `JudgeRejected`, `Infra`, `Unknown`, `ReviewDenied`, `DependencyFailed`, `MaxRuntimeExceeded`, `StallRecoveryExhausted`, `OperatorAction`, `InvalidOutput`, `Blocked` | yes | no |  | Failure kind (voyages use the root mission's kind). |
| `rawReason` | string | yes | no | at most 8000 chars | Raw failure reason (redacted, at most 8000 characters). |
| `suspectedCause` | enum: `None`, `Protocol`, `InstructionFollowing`, `ToolUse`, `Context`, `Capability`, `NotTheModel`, `Unknown`, `Mixed` | no | yes |  | Suspected cause from the failure-cause rules. |
| `causeRuleId` | string | yes | no | at most 2000 chars | Rule that assigned the cause, or null. |
| `isRecoverable` | boolean | yes | no |  | Whether the failure is recoverable, or null when unknown. |
| `isTransient` | boolean | yes | no |  | Whether the failure looks transient, or null when unknown. |

### BlackBoxWorkspace

Workspace section: dock, worktree, branch and target state at the failure.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `dockId` | string | yes | no | at most 2000 chars | Dock, or null. |
| `dockState` | string | yes | no | at most 2000 chars | Dock state name, or null. |
| `worktreePath` | string | yes | no | at most 2000 chars | Worktree path, or null. |
| `worktreeExists` | boolean | yes | no |  | Whether the worktree existed, or null when unknown. |
| `checkoutPath` | string | yes | no | at most 2000 chars | Target checkout path, or null. |
| `branchName` | string | yes | no | at most 2000 chars | Mission branch, or null. |
| `branchTip` | string | yes | no | at most 2000 chars | Mission branch tip, or null. |
| `missionCommit` | string | yes | no | at most 2000 chars | Mission commit, or null. |
| `startCommit` | string | yes | no | at most 2000 chars | Commit the mission started from, or null. |
| `startCommitSource` | string | yes | no | at most 2000 chars | Where the start commit came from (Mission, DockAnchors, DockStartFile), or null. |
| `targetBranch` | string | yes | no | at most 2000 chars | Target branch, or null. |
| `targetTipAtStart` | string | yes | no | at most 2000 chars | Target tip when the mission started, or null. |
| `targetTipAtFailure` | string | yes | no | at most 2000 chars | Target tip at the failure, or null. |
| `commitsBehindTarget` | integer | yes | no |  | Commits the branch is behind the target, or null. |
| `commitsAheadOfStart` | integer | yes | no |  | Commits the branch is ahead of its start, or null. |
| `checkoutDirty` | boolean | yes | no |  | Whether the target checkout had uncommitted tracked changes, or null. |
| `dirtyPaths` | array of string | no | yes | at most 200 items; each at most 1024 chars | Dirty paths in the checkout. |
| `dirtyPathTotal` | integer | no | yes |  | Total dirty paths before capping. |
| `harborLinkState` | string | yes | no | at most 2000 chars | Harbor link state for Harbor-hosted docks, or null. |
| `harborLastSeenUtc` | string (date-time, UTC) | yes | no |  | When the Harbor was last seen (UTC), or null. |

### BlackBoxConflict

Conflict section: conflicted files and directories and the missions that collided.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `isConflict` | boolean | no | yes |  | Whether the failure is a merge conflict. |
| `conflictedFiles` | array of string | no | yes | at most 500 items; each at most 1024 chars | Conflicted files (repository-relative, forward slashes). |
| `conflictedFileTotal` | integer | no | yes |  | Total conflicted files before capping. |
| `conflictedFilesTruncated` | boolean | no | yes |  | Whether ConflictedFiles was cut. |
| `conflictedDirectories` | array of [BlackBoxDirectoryCount](#blackboxdirectorycount) | no | yes | at most 100 items | Conflicted files rolled up to directories, with counts. |
| `conflictingMissions` | array of [BlackBoxConflictingMission](#blackboxconflictingmission) | no | yes | at most 25 items | Missions whose landed commits touched the same files. |
| `targetCommitsSinceStart` | array of [BlackBoxCommitRef](#blackboxcommitref) | no | yes | at most 50 items | Commits on the target since the mission started. |
| `targetCommitTotal` | integer | no | yes |  | Total target commits since start before capping. |

### BlackBoxDirectoryCount

A directory and how many conflicted files it holds.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `directory` | string | no | yes | at most 2000 chars | Directory (repository-relative, forward slashes; empty for the root). |
| `depth` | integer | no | yes |  | Depth (1 to 3). |
| `fileCount` | integer | no | yes |  | Conflicted files under it. |

### BlackBoxConflictingMission

A mission whose work collided with the failed mission.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `missionId` | string | yes | no | at most 2000 chars | Mission, or null for a commit made outside Armada. |
| `title` | string | yes | no | at most 500 chars | Mission title, or null. |
| `commitHash` | string | yes | no | at most 2000 chars | Landed commit, or null. |
| `landedUtc` | string (date-time, UTC) | yes | no |  | When the commit landed (UTC), or null. |
| `overlappingFiles` | array of string | no | yes | at most 50 items; each at most 1024 chars | Files both changed. |
| `overlappingFileTotal` | integer | no | yes |  | Total overlapping files before capping. |
| `isOutsideArmada` | boolean | no | yes |  | Whether the commit has no Armada-Mission-Id trailer. |
| `author` | string | yes | no | at most 2000 chars | Commit author (for commits outside Armada), or null. |

### BlackBoxCommitRef

A commit referenced by a black box record.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `commitHash` | string | no | yes | at most 2000 chars | Commit hash. |
| `subject` | string | yes | no | at most 2000 chars | Commit subject, or null. |
| `author` | string | yes | no | at most 2000 chars | Author, or null. |
| `committedUtc` | string (date-time, UTC) | yes | no |  | Commit time (UTC), or null. |
| `missionId` | string | yes | no | at most 2000 chars | Armada-Mission-Id trailer value, or null. |

### BlackBoxScope

Scope section: the mission's scoped files and what fell outside them.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `scopedFiles` | array of string | no | yes | at most 200 items; each at most 1024 chars | Scoped files from the mission brief. |
| `outOfScopeFiles` | array of string | no | yes | at most 200 items; each at most 1024 chars | Changed files outside the scope. |
| `outOfScopeFileTotal` | integer | no | yes |  | Total out-of-scope files before capping. |
| `boundaryFindings` | array of string | no | yes | at most 200 items; each at most 1024 chars | Protected-path findings. |

### BlackBoxConfiguration

Configuration section: effective values that shaped the failure, each with its source.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `values` | array of [BlackBoxConfigValue](#blackboxconfigvalue) | no | yes | at most 64 items | Effective values. |

### BlackBoxConfigValue

One effective configuration value and where it came from.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `setting` | string | no | yes | at most 2000 chars | Setting name (for example LandingMode). |
| `value` | string | yes | no | at most 2000 chars | Value as text (redacted when secret), or null when unset. |
| `source` | enum: `Mission`, `Voyage`, `Vessel`, `WorkflowProfile`, `Captain`, `Persona`, `Global`, `Default`, `Unknown` | no | yes |  | Where the value came from. |

### BlackBoxAttempt

A prior attempt of the subject, or a link in its rescue chain.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `attempt` | integer | no | yes |  | Attempt number. |
| `entryId` | string | yes | no | at most 2000 chars | Black box entry of the attempt, or null. |
| `missionId` | string | yes | no | at most 2000 chars | Mission of the attempt (differs for a rescue). |
| `stageOutcomeId` | string | yes | no | at most 2000 chars | Stage outcome, or null. |
| `kind` | enum: `Compile`, `TestFail`, `Timeout`, `LandingConflict`, `Crash`, `NoOp`, `Boundary`, `ScopeViolation`, `JudgeRejected`, `Infra`, `Unknown`, `ReviewDenied`, `DependencyFailed`, `MaxRuntimeExceeded`, `StallRecoveryExhausted`, `OperatorAction`, `InvalidOutput`, `Blocked` | yes | no |  | Failure kind, or null. |
| `reasonExcerpt` | string | yes | no | at most 500 chars | Reason excerpt (at most 500 characters). |
| `runtime` | string | yes | no | at most 2000 chars | Runtime name, or null. |
| `model` | string | yes | no | at most 2000 chars | Model, or null. |
| `durationMs` | integer | yes | no |  | Duration in milliseconds, or null. |
| `createdUtc` | string (date-time, UTC) | yes | no |  | When (UTC), or null. |

### BlackBoxEvidenceIndex

Evidence section: names, sizes and excerpts of the evidence objects, plus small structured evidence.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `objects` | array of [BlackBoxEvidenceItem](#blackboxevidenceitem) | no | yes | at most 64 items | Evidence objects. |
| `checkResults` | array of [StageCheckResult](#stagecheckresult) | no | yes | at most 10 items | Stage check results. |
| `papercuts` | array of string | no | yes | at most 10 items; each at most 1024 chars | Papercuts reported during the attempt. |
| `diffStat` | [BlackBoxDiffStat](#blackboxdiffstat) or null | yes | no |  | Diff stat, or null. |
| `relevantEvents` | array of [BlackBoxEventRef](#blackboxeventref) | no | yes | at most 50 items | Relevant events, newest first. |

### BlackBoxEvidenceItem

One evidence object listed in a record.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `name` | string | no | yes | at most 2000 chars | Object name (for example evidence/mission.log). |
| `contentType` | string | yes | no | at most 2000 chars | Content type. |
| `sizeBytes` | integer | no | yes |  | Stored size in bytes. |
| `uncompressedBytes` | integer | no | yes |  | Uncompressed size in bytes. |
| `excerpt` | string | yes | no | at most 500 chars | Short excerpt (at most 500 characters). |
| `truncated` | boolean | no | yes |  | Whether the object was cut (head and tail kept). |
| `originalBytes` | integer | yes | no |  | Original size before cutting, or null. |

### StageCheckResult

The result of a stage check: whether the stage's work was verified, and when it was not, why.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `status` | enum: `Passed`, `Failed`, `Skipped`, `CouldNotRun` | no | yes |  | What the check established. |
| `reason` | enum: `None`, `RuntimeNotChecked`, `ReviewPersona`, `VerifierReportedBlocker`, `NoDock`, `NoWorkflowProfile`, `NoCommands`, `NoNewCommit`, `HarborDisconnected`, `TimedOut`, `Error` | no | yes |  | Why the check ended with this status. |
| `commands` | array of string | no | yes | at most 2 items; each at most 1024 chars | The commands the check ran or would have run, in order. |
| `unitTestScope` | enum: `Full`, `Abbreviated` | yes | no |  | Which unit-test command the check chose (full or abbreviated), or null when the profile had no unit-test command. |
| `failedCommand` | string | yes | no | at most 2000 chars | The command that failed or timed out, or null. |
| `exitCode` | integer | yes | no |  | Exit code of the failed command, or null. |
| `preExistingFailures` | array of string | no | yes | at most 2 items; each at most 1024 chars | Commands that failed on the stage's starting commit too, so they did not block the stage. |
| `failureMessage` | string | yes | no | at most 2000 chars | The note sent back to the captain describing a failure or an unverified check, or null. |
| `durationMs` | integer | no | yes |  | How long the check took, in milliseconds. |
| `checkedUtc` | string (date-time, UTC) | no | yes |  | When the check ended (UTC). |
| `isBlocking` | boolean | no | yes |  | Whether the check blocks the stage. Only a confirmed Failed result blocks, even without a failure message; CouldNotRun and TimedOut are unverified warnings that never count as passing. |

### BlackBoxDiffStat

Diff stat of the mission's change.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `files` | integer | no | yes |  | Files changed. |
| `insertions` | integer | no | yes |  | Lines inserted. |
| `deletions` | integer | no | yes |  | Lines deleted. |
| `topPaths` | array of string | no | yes | at most 50 items; each at most 1024 chars | Top paths by change size. |

### BlackBoxEventRef

An event referenced by a record.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `eventId` | string | yes | no | at most 2000 chars | Event identifier. |
| `eventType` | string | no | yes | at most 2000 chars | Event type. |
| `message` | string | yes | no | at most 500 chars | Message (at most 500 characters). |
| `createdUtc` | string (date-time, UTC) | no | yes |  | When (UTC). |

### BlackBoxAnalysis

Analysis section: advisor findings. Every claim carries a rule id.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `summary` | string | yes | no | at most 2000 chars | Plain-language summary (3 to 6 sentences), or null. |
| `summaryIsModelWritten` | boolean | no | yes |  | Whether Summary was written by a model (labeled model-written). |
| `why` | array of [BlackBoxFinding](#blackboxfinding) | no | yes | at most 10 items | Why findings. |
| `configRecommendations` | array of [BlackBoxConfigRecommendation](#blackboxconfigrecommendation) | no | yes | at most 10 items | Configuration recommendations. |
| `armadaImprovements` | array of [BlackBoxImprovement](#blackboximprovement) | no | yes | at most 10 items | Armada improvement hypotheses. |
| `recoveryPaths` | array of [BlackBoxRecoveryPath](#blackboxrecoverypath) | no | yes | at most 10 items | Ranked recovery paths. |
| `triage` | [BlackBoxTriageVerdict](#blackboxtriageverdict) or null | yes | no |  | Triage verdict, or null. |

### BlackBoxFinding

One why finding.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `ruleId` | string | no | yes | at most 2000 chars | Rule that produced it (for example BBX-LAND-003). |
| `statement` | string | no | yes | at most 2000 chars | Statement. |
| `confidence` | enum: `Low`, `Medium`, `High`, `Unknown` | no | yes |  | Confidence. |
| `evidenceRefs` | array of string | no | yes | at most 10 items; each at most 1024 chars | References to evidence (section paths or object names). |

### BlackBoxConfigRecommendation

A configuration change that could have prevented the failure.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `id` | string | no | yes | at most 2000 chars | Recommendation id within the entry (for example cr1). |
| `setting` | string | no | yes | at most 2000 chars | Setting. |
| `scope` | enum: `Mission`, `Voyage`, `Vessel`, `WorkflowProfile`, `Captain`, `Persona`, `Global`, `Default`, `Unknown` | no | yes |  | Where to change it. |
| `currentValue` | string | yes | no | at most 2000 chars | Current value, or null. |
| `suggestedValue` | string | yes | no | at most 2000 chars | Suggested value, or null. |
| `rationale` | string | yes | no | at most 2000 chars | Why. |
| `ruleId` | string | yes | no | at most 2000 chars | Rule that produced it. |
| `howToApply` | string | yes | no | at most 2000 chars | Human instructions. |
| `applyAction` | [BlackBoxAction](#blackboxaction) or null | yes | no |  | Machine-readable action, or null. |

### BlackBoxAction

A machine-readable action: an MCP tool name and its arguments.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `tool` | string | no | yes | at most 2000 chars | MCP tool name (for example retry_landing). |
| `arguments` | map of string to string | no | yes | at most 32 entries | Tool arguments as text. |

### BlackBoxImprovement

An Armada behavior change that could address the failure.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `area` | string | yes | no | at most 2000 chars | Area of Armada. |
| `hypothesis` | string | no | yes | at most 2000 chars | Hypothesis. |
| `evidence` | string | yes | no | at most 2000 chars | Evidence. |
| `sourcePointers` | array of string | no | yes | at most 10 items; each at most 1024 chars | Source pointers (file, method). |
| `severity` | enum: `Low`, `Medium`, `High`, `Critical`, `Unknown` | no | yes |  | Severity. |
| `ruleId` | string | yes | no | at most 2000 chars | Rule that produced it. |
| `knownIssue` | string | yes | no | at most 2000 chars | Known issue link, or null. |

### BlackBoxRecoveryPath

A ranked recovery path with exact commands and a machine-readable action.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `id` | string | no | yes | at most 2000 chars | Path id within the entry (for example rp1). |
| `rank` | integer | no | yes |  | Rank, 1 is best. |
| `title` | string | no | yes | at most 500 chars | Title. |
| `preconditions` | array of string | no | yes | at most 10 items; each at most 1024 chars | Preconditions. |
| `steps` | array of [BlackBoxRecoveryStep](#blackboxrecoverystep) | no | yes | at most 10 items | Steps. |
| `action` | [BlackBoxAction](#blackboxaction) or null | yes | no |  | Primary action, or null. |
| `risk` | enum: `Low`, `Medium`, `High`, `Critical`, `Unknown` | no | yes |  | Risk. |
| `dataLossRisk` | boolean | no | yes |  | Whether any step can lose data (requires acknowledgeDataLoss). |
| `expectedOutcome` | string | yes | no | at most 2000 chars | Expected outcome. |
| `ruleId` | string | yes | no | at most 2000 chars | Rule that produced it. |

### BlackBoxRecoveryStep

One step of a recovery path.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `order` | integer | no | yes |  | Order, starting at 1. |
| `description` | string | no | yes | at most 2000 chars | Description. |
| `mcpCommand` | string | yes | no | at most 2000 chars | MCP command text. |
| `restCommand` | string | yes | no | at most 2000 chars | REST command text. |
| `cliCommand` | string | yes | no | at most 2000 chars | CLI command text. |
| `action` | [BlackBoxAction](#blackboxaction) or null | yes | no |  | Machine-readable action, or null for a manual step. |
| `dataLossRisk` | boolean | no | yes |  | Whether the step can lose data. |

### BlackBoxTriageVerdict

Triage verdict with the rule votes behind it.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `verdict` | enum: `Unknown`, `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` | no | yes |  | Verdict. |
| `confidence` | enum: `Low`, `Medium`, `High`, `Unknown` | no | yes |  | Confidence. |
| `votes` | array of [BlackBoxTriageVote](#blackboxtriagevote) | no | yes | at most 20 items | Votes. |
| `tiedWith` | array of enum: `Unknown`, `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` | no | yes | at most 6 items | Candidates on a tie. |

### BlackBoxTriageVote

One rule's triage vote.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `ruleId` | string | no | yes | at most 2000 chars | Rule. |
| `verdict` | enum: `Unknown`, `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` | no | yes |  | Vote. |
| `weight` | integer | no | yes |  | Weight. |

### BlackBoxRelated

Related section: fingerprint, similar entries and cascade.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `fingerprint` | string | yes | no | at most 2000 chars | Failure-mode fingerprint (BlackBoxFingerprint). |
| `similarEntries` | array of [BlackBoxSimilarEntry](#blackboxsimilarentry) | no | yes | at most 10 items | Similar entries, newest first. |
| `occurrencesLast30Days` | integer | no | yes |  | Entries with the same fingerprint in the last 30 days. |
| `cascadeRootEntryId` | string | yes | no | at most 2000 chars | Cascade root entry, or null. |
| `downstreamMissionIds` | array of string | no | yes | at most 100 items; each at most 1024 chars | Missions downstream of the cascade. |

### BlackBoxSimilarEntry

An earlier entry with the same failure mode.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes | at most 2000 chars | Entry. |
| `missionId` | string | yes | no | at most 2000 chars | Mission. |
| `missionTitle` | string | yes | no | at most 500 chars | Mission title. |
| `createdUtc` | string (date-time, UTC) | no | yes |  | When (UTC). |
| `resolution` | enum: `Open`, `Resolved`, `Duplicate`, `WontFix`, `ArmadaFixed`, `Unknown` | no | yes |  | How it was resolved. |
| `resolutionNote` | string | yes | no | at most 2000 chars | Resolution note excerpt. |

### BlackBoxVoyageRollup

Voyage section (voyage entries only): missions by status, failed entries, cascade root and settlement.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `missionTotal` | integer | no | yes |  | Missions in the voyage. |
| `missionsByStatus` | array of [BlackBoxStatusCount](#blackboxstatuscount) | no | yes | at most 16 items | Missions by status. |
| `failedMissionIds` | array of string | no | yes | at most 100 items; each at most 1024 chars | Failed missions. |
| `failedMissionEntryIds` | array of string | no | yes | at most 100 items; each at most 1024 chars | Current black box entries of the failed missions. |
| `cascadeRootMissionId` | string | yes | no | at most 2000 chars | Root failed mission, or null. |
| `cascadeRootEntryId` | string | yes | no | at most 2000 chars | Root failed mission's entry, or null. |
| `settlementRule` | string | yes | no | at most 2000 chars | Settlement rule that decided the voyage, or null. |
| `haltReason` | string | yes | no | at most 2000 chars | Halt reason, or null. |

### BlackBoxStatusCount

A status and a count.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `status` | string | no | yes | at most 2000 chars | Status name. |
| `count` | integer | no | yes |  | Count. |

### BlackBoxRedaction

Redaction report: rule names and counts, never values.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `rulesApplied` | array of [BlackBoxRedactionRuleCount](#blackboxredactionrulecount) | no | yes | at most 32 items | Rules that fired. |
| `redactedCount` | integer | no | yes |  | Total redactions. |

### BlackBoxRedactionRuleCount

A redaction rule and how often it fired.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `rule` | string | no | yes | at most 2000 chars | Rule name. |
| `count` | integer | no | yes |  | Count. |

### BlackBoxTruncation

Truncation report: what was cut and why. Nothing is dropped silently.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `fields` | array of [BlackBoxTruncatedField](#blackboxtruncatedfield) | no | yes | at most 64 items | Fields that were cut. |
| `fieldTotal` | integer | no | yes |  | Total truncated fields before capping. |
| `timedOut` | boolean | no | yes |  | Whether capture ran out of its time budget and wrote a partial entry. |
| `notes` | array of string | no | yes | at most 20 items; each at most 1024 chars | Capture notes (timeouts, lower-fidelity capture, missing facts). |

### BlackBoxTruncatedField

A field that was cut.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `field` | string | no | yes | at most 2000 chars | Field path (for example Conflict.ConflictedFiles). |
| `originalSize` | integer | no | yes |  | Original size. |
| `keptSize` | integer | no | yes |  | Kept size. |
| `unit` | string | no | yes | at most 2000 chars | Unit: chars, items or bytes. |

## 5. REST and MCP shapes

`BlackBoxEntry` is the index row; reads that ask for it populate `record`. Lists return `EnumerationResult` pages
(`pageNumber`, `pageSize`, `totalPages`, `totalRecords`, `objects`; page size 1 to 1000, default 10) of
`BlackBoxEntrySummary`, `BlackBoxObjectSummary` or `BlackBoxNote`. Object reads return bytes over REST (at most 1 MB
per read, `offset` and `length`) and `BlackBoxObjectChunk` over MCP (at most 64 KB, `nextOffset` until null). Section
reads return `BlackBoxSectionResult` with only the requested section populated. `BlackBoxDigest` is the compact MCP
summary.

### BlackBoxEntry

A black box entry: the index row (blackbox_entries) for one captured mission or voyage failure. The index is always in the database whatever the storage target; only object bytes move between targets. Record is populated only by reads that ask for it.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `id` | string | no | yes |  | Unique identifier (bbx_ prefix). |
| `tenantId` | string | yes | no |  | Tenant. |
| `userId` | string | yes | no |  | Owner of the subject. |
| `subjectType` | enum: `Mission`, `Voyage`, `Unknown` | no | yes |  | Mission or voyage. |
| `missionId` | string | yes | no |  | Mission, or null for a voyage entry. |
| `voyageId` | string | yes | no |  | Voyage, or null. |
| `vesselId` | string | yes | no |  | Vessel, or null. |
| `fleetId` | string | yes | no |  | Fleet, or null. |
| `captainId` | string | yes | no |  | Captain, or null. |
| `harborId` | string | yes | no |  | Harbor, or null. |
| `dockId` | string | yes | no |  | Dock, or null. |
| `mergeEntryId` | string | yes | no |  | Merge-queue entry, or null. |
| `incidentId` | string | yes | no |  | Incident, or null. |
| `stageOutcomeId` | string | yes | no |  | Stage outcome, or null. |
| `parentMissionId` | string | yes | no |  | Parent mission, or null. |
| `missionTitle` | string | yes | no |  | Denormalized mission (or voyage) title, kept after the mission is purged. |
| `vesselName` | string | yes | no |  | Denormalized vessel name. |
| `attempt` | integer | no | yes |  | Attempt number for the subject, starting at 1. |
| `isCurrent` | boolean | no | yes |  | Whether this is the subject's current entry (one per subject). |
| `supersedesId` | string | yes | no |  | The previous entry for the same subject, or null. |
| `phase` | enum: `Unknown`, `Dispatch`, `Provision`, `Execution`, `Completion`, `Review`, `Handoff`, `Landing`, `MergeQueue`, `Recovery`, `Voyage`, `External` | no | yes |  | Phase. |
| `step` | enum: `Unknown`, `VoyageParentNotRunnable`, `AssignmentFailed`, `NoEligibleCaptain`, `HarborUnavailable`, `MemoryPressure`, `DockProvision`, `WorktreeCreate`, `BranchCreate`, `StartCommitRecord`, `ProcessCrash`, `UsageLimit`, `AuthFailure`, `ModelUnavailable`, `Stall`, `MaxRuntime`, `RecoveryExhausted`, `Interrupted`, `OperatorStop`, `NoOp`, `Unfinished`, `CaptainBlocked`, `InvalidOutput`, `ScopeViolation`, `BoundaryViolation`, `DefinitionOfDone`, `StageCheck`, `JudgeRejected`, `ReviewDenied`, `ReviewTimeout`, `ReviewApprovedDockMissing`, `PlanParse`, `StageBranchHarden`, `DependencyCancelled`, `IntegrationBarrier`, `DockMissing`, `TargetIntegration`, `Verification`, `DirtyCheckout`, `UnpublishedAncestry`, `Push`, `PullRequest`, `FastForward`, `LocalMerge`, `RetryLanding`, `DeferralExpired`, `QueueResolveRepo`, `QueueMergeConflict`, `QueueTests`, `QueueProcessing`, `QueuePush`, `RescueFailed`, `RedispatchExhausted`, `OrphanedExit`, `UnconfirmedStop`, `MissingMergeHandoff`, `SettlementFailed`, `Halted`, `Cancelled`, `StageFailed`, `VesselDeleted`, `OperatorTransition` | no | yes |  | Step. |
| `failureKind` | enum: `Compile`, `TestFail`, `Timeout`, `LandingConflict`, `Crash`, `NoOp`, `Boundary`, `ScopeViolation`, `JudgeRejected`, `Infra`, `Unknown`, `ReviewDenied`, `DependencyFailed`, `MaxRuntimeExceeded`, `StallRecoveryExhausted`, `OperatorAction`, `InvalidOutput`, `Blocked` | yes | no |  | Failure kind, or null. |
| `statusBefore` | string | yes | no |  | Status before the failure write. |
| `statusAfter` | string | yes | no |  | Status after the failure write. |
| `triage` | enum: `Unknown`, `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` | no | yes |  | Triage verdict. |
| `fingerprint` | string | yes | no |  | Failure-mode fingerprint (64 hex characters), or null. |
| `captureMode` | enum: `Inline`, `Sweeper`, `Backfill`, `Unknown` | no | yes |  | How it was captured. |
| `state` | enum: `Captured`, `Enriched`, `Analyzed`, `EnrichmentFailed`, `Deleting`, `ObjectMissing`, `Unknown` | no | yes |  | Processing state. |
| `resolution` | enum: `Open`, `Resolved`, `Duplicate`, `WontFix`, `ArmadaFixed`, `Unknown` | no | yes |  | Resolution. |
| `resolutionNote` | string | yes | no |  | What fixed it (at most 2000 characters). |
| `resolutionLink` | string | yes | no |  | Link to what fixed it: mission id, commit, PR or issue URL (at most 1000 characters). |
| `pinned` | boolean | no | yes |  | Pinned entries are never expired or evicted. |
| `storageTarget` | enum: `Database`, `Disk`, `S3`, `AzureBlob`, `GoogleCloud`, `Cifs`, `Nfs`, `Unknown` | no | yes |  | Where the entry's object bytes are stored. |
| `storagePrefix` | string | yes | no |  | Object key prefix for external targets, or null. |
| `totalBytes` | integer | no | yes |  | Stored (compressed) size of the entry's objects. |
| `objectCount` | integer | no | yes |  | Number of objects. |
| `schemaVersion` | integer | no | yes |  | Record schema version. |
| `rulesVersion` | integer | no | yes |  | Advisor rules version. |
| `armadaVersion` | string | yes | no |  | Armada version that captured it. |
| `armadaCommit` | string | yes | no |  | Armada commit that captured it, or null. |
| `reasonExcerpt` | string | yes | no |  | Redacted reason excerpt for lists and search (at most 1000 characters). |
| `summary` | string | yes | no |  | Analysis summary excerpt for lists and search (at most 2000 characters), or null. |
| `enrichmentAttempts` | integer | no | yes |  | Enrichment attempts so far. |
| `lastError` | string | yes | no |  | Last enrichment or storage error (redacted), or null. |
| `nextAttemptUtc` | string (date-time, UTC) | yes | no |  | When the sweeper may retry enrichment (UTC), or null. |
| `expiresUtc` | string (date-time, UTC) | yes | no |  | When the entry expires (UTC); null when pinned or kept forever. |
| `createdUtc` | string (date-time, UTC) | no | yes |  | Created (UTC). |
| `enrichedUtc` | string (date-time, UTC) | yes | no |  | Enriched (UTC), or null. |
| `analyzedUtc` | string (date-time, UTC) | yes | no |  | Analyzed (UTC), or null. |
| `resolvedUtc` | string (date-time, UTC) | yes | no |  | Resolved (UTC), or null. |
| `lastUpdateUtc` | string (date-time, UTC) | no | yes |  | Last update (UTC). |
| `record` | [BlackBoxRecord](#blackboxrecord) or null | yes | no |  | The record (record.json), populated only when a read asks for it; never stored in the row. |

### BlackBoxEntrySummary

Short form of a black box entry for lists.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `id` | string | no | yes |  | Entry identifier. |
| `subjectType` | enum: `Mission`, `Voyage`, `Unknown` | no | yes |  | Mission or voyage. |
| `missionId` | string | yes | no |  | Mission. |
| `voyageId` | string | yes | no |  | Voyage. |
| `vesselId` | string | yes | no |  | Vessel. |
| `captainId` | string | yes | no |  | Captain. |
| `missionTitle` | string | yes | no |  | Mission (or voyage) title. |
| `vesselName` | string | yes | no |  | Vessel name. |
| `attempt` | integer | no | yes |  | Attempt. |
| `isCurrent` | boolean | no | yes |  | Whether current. |
| `phase` | enum: `Unknown`, `Dispatch`, `Provision`, `Execution`, `Completion`, `Review`, `Handoff`, `Landing`, `MergeQueue`, `Recovery`, `Voyage`, `External` | no | yes |  | Phase. |
| `step` | enum: `Unknown`, `VoyageParentNotRunnable`, `AssignmentFailed`, `NoEligibleCaptain`, `HarborUnavailable`, `MemoryPressure`, `DockProvision`, `WorktreeCreate`, `BranchCreate`, `StartCommitRecord`, `ProcessCrash`, `UsageLimit`, `AuthFailure`, `ModelUnavailable`, `Stall`, `MaxRuntime`, `RecoveryExhausted`, `Interrupted`, `OperatorStop`, `NoOp`, `Unfinished`, `CaptainBlocked`, `InvalidOutput`, `ScopeViolation`, `BoundaryViolation`, `DefinitionOfDone`, `StageCheck`, `JudgeRejected`, `ReviewDenied`, `ReviewTimeout`, `ReviewApprovedDockMissing`, `PlanParse`, `StageBranchHarden`, `DependencyCancelled`, `IntegrationBarrier`, `DockMissing`, `TargetIntegration`, `Verification`, `DirtyCheckout`, `UnpublishedAncestry`, `Push`, `PullRequest`, `FastForward`, `LocalMerge`, `RetryLanding`, `DeferralExpired`, `QueueResolveRepo`, `QueueMergeConflict`, `QueueTests`, `QueueProcessing`, `QueuePush`, `RescueFailed`, `RedispatchExhausted`, `OrphanedExit`, `UnconfirmedStop`, `MissingMergeHandoff`, `SettlementFailed`, `Halted`, `Cancelled`, `StageFailed`, `VesselDeleted`, `OperatorTransition` | no | yes |  | Step. |
| `failureKind` | enum: `Compile`, `TestFail`, `Timeout`, `LandingConflict`, `Crash`, `NoOp`, `Boundary`, `ScopeViolation`, `JudgeRejected`, `Infra`, `Unknown`, `ReviewDenied`, `DependencyFailed`, `MaxRuntimeExceeded`, `StallRecoveryExhausted`, `OperatorAction`, `InvalidOutput`, `Blocked` | yes | no |  | Failure kind. |
| `statusBefore` | string | yes | no |  | Status before. |
| `statusAfter` | string | yes | no |  | Status after. |
| `triage` | enum: `Unknown`, `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` | no | yes |  | Triage. |
| `fingerprint` | string | yes | no |  | Fingerprint. |
| `captureMode` | enum: `Inline`, `Sweeper`, `Backfill`, `Unknown` | no | yes |  | Capture mode. |
| `state` | enum: `Captured`, `Enriched`, `Analyzed`, `EnrichmentFailed`, `Deleting`, `ObjectMissing`, `Unknown` | no | yes |  | State. |
| `resolution` | enum: `Open`, `Resolved`, `Duplicate`, `WontFix`, `ArmadaFixed`, `Unknown` | no | yes |  | Resolution. |
| `pinned` | boolean | no | yes |  | Pinned. |
| `reasonExcerpt` | string | yes | no |  | Reason excerpt (at most 300 characters). |
| `totalBytes` | integer | no | yes |  | Stored bytes. |
| `objectCount` | integer | no | yes |  | Objects. |
| `createdUtc` | string (date-time, UTC) | no | yes |  | Created (UTC). |
| `expiresUtc` | string (date-time, UTC) | yes | no |  | Expires (UTC). |

### BlackBoxObjectSummary

Short form of a black box object for lists.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `name` | string | no | yes |  | Object name. |
| `contentType` | string | no | yes |  | Content type. |
| `contentEncoding` | string | no | yes |  | Stored encoding. |
| `sizeBytes` | integer | no | yes |  | Stored size. |
| `uncompressedBytes` | integer | no | yes |  | Uncompressed size. |
| `sha256` | string | yes | no |  | SHA-256 of the stored bytes. |
| `isMissing` | boolean | no | yes |  | Whether the bytes are missing. |
| `createdUtc` | string (date-time, UTC) | no | yes |  | Created (UTC). |

### BlackBoxObjectChunk

A chunk of a black box object, for paging through large objects (MCP read_blackbox_object: at most 64 KB per call).

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `name` | string | no | yes |  | Object name. |
| `contentType` | string | no | yes |  | Content type. |
| `offset` | integer | no | yes |  | Offset of this chunk in the uncompressed object. |
| `length` | integer | no | yes |  | Bytes in this chunk. |
| `totalBytes` | integer | no | yes |  | Uncompressed size of the object. |
| `nextOffset` | integer | yes | no |  | Offset of the next chunk, or null at the end. |
| `encoding` | string | no | yes |  | utf-8 for text objects, base64 otherwise. |
| `content` | string | no | yes |  | Chunk content (text, or base64). |
| `sha256` | string | yes | no |  | SHA-256 of the stored object. |

### BlackBoxSectionResult

One section of an entry's record. Record carries only the requested section (other sections are null).

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `section` | enum: `Subject`, `Status`, `Site`, `Mechanism`, `Classification`, `Workspace`, `Conflict`, `Scope`, `Configuration`, `Attempts`, `Evidence`, `Analysis`, `Related`, `Voyage`, `Redaction`, `Truncation`, `Unknown` | no | yes |  | Section. |
| `record` | [BlackBoxRecord](#blackboxrecord) | no | yes |  | Record with only the requested section populated. |

### BlackBoxDigest

Compact view of a black box entry (about 2 KB) for MCP get_blackbox format=summary: identity, where, how, status before and after, why, and the top recovery paths.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `subjectType` | enum: `Mission`, `Voyage`, `Unknown` | no | yes |  | Mission or voyage. |
| `missionId` | string | yes | no |  | Mission. |
| `voyageId` | string | yes | no |  | Voyage. |
| `title` | string | yes | no |  | Mission (or voyage) title. |
| `vesselName` | string | yes | no |  | Vessel name. |
| `attempt` | integer | no | yes |  | Attempt. |
| `captureMode` | enum: `Inline`, `Sweeper`, `Backfill`, `Unknown` | no | yes |  | Capture mode (Sweeper and Backfill are lower fidelity). |
| `state` | enum: `Captured`, `Enriched`, `Analyzed`, `EnrichmentFailed`, `Deleting`, `ObjectMissing`, `Unknown` | no | yes |  | State. |
| `phase` | enum: `Unknown`, `Dispatch`, `Provision`, `Execution`, `Completion`, `Review`, `Handoff`, `Landing`, `MergeQueue`, `Recovery`, `Voyage`, `External` | no | yes |  | Phase. |
| `step` | enum: `Unknown`, `VoyageParentNotRunnable`, `AssignmentFailed`, `NoEligibleCaptain`, `HarborUnavailable`, `MemoryPressure`, `DockProvision`, `WorktreeCreate`, `BranchCreate`, `StartCommitRecord`, `ProcessCrash`, `UsageLimit`, `AuthFailure`, `ModelUnavailable`, `Stall`, `MaxRuntime`, `RecoveryExhausted`, `Interrupted`, `OperatorStop`, `NoOp`, `Unfinished`, `CaptainBlocked`, `InvalidOutput`, `ScopeViolation`, `BoundaryViolation`, `DefinitionOfDone`, `StageCheck`, `JudgeRejected`, `ReviewDenied`, `ReviewTimeout`, `ReviewApprovedDockMissing`, `PlanParse`, `StageBranchHarden`, `DependencyCancelled`, `IntegrationBarrier`, `DockMissing`, `TargetIntegration`, `Verification`, `DirtyCheckout`, `UnpublishedAncestry`, `Push`, `PullRequest`, `FastForward`, `LocalMerge`, `RetryLanding`, `DeferralExpired`, `QueueResolveRepo`, `QueueMergeConflict`, `QueueTests`, `QueueProcessing`, `QueuePush`, `RescueFailed`, `RedispatchExhausted`, `OrphanedExit`, `UnconfirmedStop`, `MissingMergeHandoff`, `SettlementFailed`, `Halted`, `Cancelled`, `StageFailed`, `VesselDeleted`, `OperatorTransition` | no | yes |  | Step. |
| `host` | string | yes | no |  | Host label. |
| `sourceFile` | string | yes | no |  | Source file of the failure site. |
| `sourceLine` | integer | yes | no |  | Source line. |
| `method` | string | yes | no |  | Method. |
| `kind` | enum: `Compile`, `TestFail`, `Timeout`, `LandingConflict`, `Crash`, `NoOp`, `Boundary`, `ScopeViolation`, `JudgeRejected`, `Infra`, `Unknown`, `ReviewDenied`, `DependencyFailed`, `MaxRuntimeExceeded`, `StallRecoveryExhausted`, `OperatorAction`, `InvalidOutput`, `Blocked` | yes | no |  | Failure kind. |
| `reason` | string | yes | no |  | Reason excerpt (at most 500 characters). |
| `mechanism` | string | yes | no |  | One-line mechanism (exception, exit code, failing command or git operation). |
| `statusBefore` | string | yes | no |  | Status before. |
| `statusAfter` | string | yes | no |  | Status after. |
| `triage` | enum: `Unknown`, `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` | no | yes |  | Triage verdict. |
| `triageConfidence` | enum: `Low`, `Medium`, `High`, `Unknown` | yes | no |  | Triage confidence, or null. |
| `summary` | string | yes | no |  | Analysis summary, or null. |
| `why` | array of [BlackBoxFinding](#blackboxfinding) | no | yes | at most 3 items | Top why findings. |
| `recoveryPaths` | array of [BlackBoxRecoveryPath](#blackboxrecoverypath) | no | yes | at most 3 items | Top recovery paths. |
| `conflictedFileTotal` | integer | no | yes |  | Conflicted files. |
| `objectCount` | integer | no | yes |  | Objects. |
| `resolution` | enum: `Open`, `Resolved`, `Duplicate`, `WontFix`, `ArmadaFixed`, `Unknown` | no | yes |  | Resolution. |
| `createdUtc` | string (date-time, UTC) | no | yes |  | Created (UTC). |

### BlackBoxNote

A note on a black box entry by an operator, an agent, Ask Armada or Armada (blackbox_notes).

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `id` | string | no | yes |  | Unique identifier (bbn_ prefix). |
| `entryId` | string | no | yes |  | Entry. |
| `tenantId` | string | yes | no |  | Tenant. |
| `userId` | string | yes | no |  | Author user, or null. |
| `authorKind` | enum: `Operator`, `Agent`, `AskArmada`, `System`, `Unknown` | no | yes |  | Who wrote it. |
| `body` | string | no | yes |  | Markdown body (at most 8000 characters). |
| `createdUtc` | string (date-time, UTC) | no | yes |  | Created (UTC). |

### BlackBoxDownloadManifest

manifest.json inside a black box .zip download: the entry, its schema version and every object with name, content type, size and SHA-256.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `schemaVersion` | integer | no | yes |  | Record schema version. |
| `missionId` | string | yes | no |  | Mission. |
| `voyageId` | string | yes | no |  | Voyage. |
| `armadaVersion` | string | yes | no |  | Armada version that captured the entry. |
| `createdUtc` | string (date-time, UTC) | no | yes |  | Entry creation (UTC). |
| `generatedUtc` | string (date-time, UTC) | no | yes |  | When the download was built (UTC). |
| `objects` | array of [BlackBoxObjectSummary](#blackboxobjectsummary) | no | yes | at most 128 items | Objects in the archive (stored SHA-256 of the encoded bytes; the archive holds the decoded bytes). |
| `missingObjects` | array of string | no | yes | at most 128 items | Objects whose bytes could not be read. |

## 6. Event payloads

Event types and when they fire are listed in [BLACKBOX.md](BLACKBOX.md), section 3.10.

### BlackBoxCapturedPayload

Payload of the blackbox.captured event.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `missionId` | string | yes | no |  | Mission. |
| `voyageId` | string | yes | no |  | Voyage. |
| `vesselId` | string | yes | no |  | Vessel. |
| `phase` | enum: `Unknown`, `Dispatch`, `Provision`, `Execution`, `Completion`, `Review`, `Handoff`, `Landing`, `MergeQueue`, `Recovery`, `Voyage`, `External` | no | yes |  | Phase. |
| `step` | enum: `Unknown`, `VoyageParentNotRunnable`, `AssignmentFailed`, `NoEligibleCaptain`, `HarborUnavailable`, `MemoryPressure`, `DockProvision`, `WorktreeCreate`, `BranchCreate`, `StartCommitRecord`, `ProcessCrash`, `UsageLimit`, `AuthFailure`, `ModelUnavailable`, `Stall`, `MaxRuntime`, `RecoveryExhausted`, `Interrupted`, `OperatorStop`, `NoOp`, `Unfinished`, `CaptainBlocked`, `InvalidOutput`, `ScopeViolation`, `BoundaryViolation`, `DefinitionOfDone`, `StageCheck`, `JudgeRejected`, `ReviewDenied`, `ReviewTimeout`, `ReviewApprovedDockMissing`, `PlanParse`, `StageBranchHarden`, `DependencyCancelled`, `IntegrationBarrier`, `DockMissing`, `TargetIntegration`, `Verification`, `DirtyCheckout`, `UnpublishedAncestry`, `Push`, `PullRequest`, `FastForward`, `LocalMerge`, `RetryLanding`, `DeferralExpired`, `QueueResolveRepo`, `QueueMergeConflict`, `QueueTests`, `QueueProcessing`, `QueuePush`, `RescueFailed`, `RedispatchExhausted`, `OrphanedExit`, `UnconfirmedStop`, `MissingMergeHandoff`, `SettlementFailed`, `Halted`, `Cancelled`, `StageFailed`, `VesselDeleted`, `OperatorTransition` | no | yes |  | Step. |
| `kind` | enum: `Compile`, `TestFail`, `Timeout`, `LandingConflict`, `Crash`, `NoOp`, `Boundary`, `ScopeViolation`, `JudgeRejected`, `Infra`, `Unknown`, `ReviewDenied`, `DependencyFailed`, `MaxRuntimeExceeded`, `StallRecoveryExhausted`, `OperatorAction`, `InvalidOutput`, `Blocked` | yes | no |  | Failure kind. |
| `attempt` | integer | no | yes |  | Attempt. |
| `captureMode` | enum: `Inline`, `Sweeper`, `Backfill`, `Unknown` | no | yes |  | Capture mode. |

### BlackBoxCaptureFailedPayload

Payload of the blackbox.capture_failed event.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `missionId` | string | yes | no |  | Mission. |
| `voyageId` | string | yes | no |  | Voyage. |
| `step` | enum: `Unknown`, `VoyageParentNotRunnable`, `AssignmentFailed`, `NoEligibleCaptain`, `HarborUnavailable`, `MemoryPressure`, `DockProvision`, `WorktreeCreate`, `BranchCreate`, `StartCommitRecord`, `ProcessCrash`, `UsageLimit`, `AuthFailure`, `ModelUnavailable`, `Stall`, `MaxRuntime`, `RecoveryExhausted`, `Interrupted`, `OperatorStop`, `NoOp`, `Unfinished`, `CaptainBlocked`, `InvalidOutput`, `ScopeViolation`, `BoundaryViolation`, `DefinitionOfDone`, `StageCheck`, `JudgeRejected`, `ReviewDenied`, `ReviewTimeout`, `ReviewApprovedDockMissing`, `PlanParse`, `StageBranchHarden`, `DependencyCancelled`, `IntegrationBarrier`, `DockMissing`, `TargetIntegration`, `Verification`, `DirtyCheckout`, `UnpublishedAncestry`, `Push`, `PullRequest`, `FastForward`, `LocalMerge`, `RetryLanding`, `DeferralExpired`, `QueueResolveRepo`, `QueueMergeConflict`, `QueueTests`, `QueueProcessing`, `QueuePush`, `RescueFailed`, `RedispatchExhausted`, `OrphanedExit`, `UnconfirmedStop`, `MissingMergeHandoff`, `SettlementFailed`, `Halted`, `Cancelled`, `StageFailed`, `VesselDeleted`, `OperatorTransition` | no | yes |  | Step. |
| `error` | string | yes | no |  | Error (redacted). |

### BlackBoxAnalyzedPayload

Payload of the blackbox.analyzed event.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `missionId` | string | yes | no |  | Mission. |
| `voyageId` | string | yes | no |  | Voyage. |
| `state` | enum: `Captured`, `Enriched`, `Analyzed`, `EnrichmentFailed`, `Deleting`, `ObjectMissing`, `Unknown` | no | yes |  | State after the pipeline. |
| `triage` | enum: `Unknown`, `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` | no | yes |  | Triage. |
| `rulesVersion` | integer | no | yes |  | Rules version. |

### BlackBoxResolvedPayload

Payload of the blackbox.resolved event.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `missionId` | string | yes | no |  | Mission. |
| `voyageId` | string | yes | no |  | Voyage. |
| `resolution` | enum: `Open`, `Resolved`, `Duplicate`, `WontFix`, `ArmadaFixed`, `Unknown` | no | yes |  | Resolution. |

### BlackBoxDeletedPayload

Payload of the blackbox.deleted event.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `missionId` | string | yes | no |  | Mission. |
| `voyageId` | string | yes | no |  | Voyage. |
| `reason` | string | no | yes |  | Why: Manual, Purge, Expired, Evicted, Cascade. |

### BlackBoxStorageFallbackPayload

Payload of the blackbox.storage_fallback event.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `from` | enum: `Database`, `Disk`, `S3`, `AzureBlob`, `GoogleCloud`, `Cifs`, `Nfs`, `Unknown` | no | yes |  | Target that failed. |
| `to` | enum: `Database`, `Disk`, `S3`, `AzureBlob`, `GoogleCloud`, `Cifs`, `Nfs`, `Unknown` | no | yes |  | Target written instead. |
| `error` | string | yes | no |  | Error (no secrets). |

### BlackBoxRecoveryRunPayload

Payload of the blackbox.recovery_run event.

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `entryId` | string | no | yes |  | Entry. |
| `pathId` | string | no | yes |  | Recovery path. |
| `dryRun` | boolean | no | yes |  | Whether this was a dry run. |
| `succeeded` | boolean | no | yes |  | Whether it succeeded. |

### BlackBoxGcReport

Report of one black box garbage collection run (also the blackbox.gc_completed event payload).

| Field | Type | Null | Required | Bounds | Description |
|---|---|---|---|---|---|
| `dryRun` | boolean | no | yes |  | Whether this was a dry run. |
| `startedUtc` | string (date-time, UTC) | no | yes |  | Started (UTC). |
| `finishedUtc` | string (date-time, UTC) | yes | no |  | Finished (UTC). |
| `durationMs` | integer | no | yes |  | Duration in milliseconds. |
| `expired` | integer | no | yes |  | Entries expired by age. |
| `evicted` | integer | no | yes |  | Entries evicted by quota. |
| `cascaded` | integer | no | yes |  | Entries deleted with their vessel or mission. |
| `retriedDeletes` | integer | no | yes |  | Entries left Deleting by an earlier run and finished now. |
| `orphansRemoved` | integer | no | yes |  | Orphaned external objects removed. |
| `objectsMissing` | integer | no | yes |  | Objects found missing. |
| `bytesFreed` | integer | no | yes |  | Stored bytes freed. |
| `failures` | integer | no | yes |  | Entries whose delete failed (left Deleting, retried next run). |
| `candidateEntryIds` | array of string | no | yes | at most 100 items | Up to 100 entry ids deleted (or that a dry run would delete). |
| `errors` | array of string | no | yes | at most 20 items | Up to 20 error messages. |

## 7. Example

An abbreviated `record.json` (`"...": "..."` marks omitted fields):

```json
{
  "entryId": "bbx_mvtw00ab_Example0000",
  "schemaVersion": 1,
  "rulesVersion": 0,
  "captureMode": "Inline",
  "state": "Captured",
  "createdUtc": "2026-10-29T02:26:00Z",
  "subject": {
    "subjectType": "Mission",
    "missionId": "msn_mvtw0000_Example0000",
    "title": "Add the parser",
    "vesselId": "vsl_mvtw0000_Example0000",
    "vesselName": "Armada",
    "attempt": 2,
    "isRescue": false,
    "...": "..."
  },
  "status": {
    "before": "WorkProduced",
    "after": "LandingFailed",
    "changedUtc": "2026-10-29T02:26:00Z",
    "timeline": [
      {
        "status": "LandingFailed",
        "utc": "2026-10-29T02:26:00Z",
        "source": "failure write"
      }
    ]
  },
  "site": {
    "phase": "Landing",
    "step": "TargetIntegration",
    "hostKind": "Admiral",
    "host": "Admiral",
    "component": "MissionLandingHandler",
    "method": "LandAsync",
    "sourceFile": "src/Armada.Core/Services/MissionLandingHandler.cs",
    "sourceLine": 285,
    "armadaVersion": "1.2.0+abc1234",
    "armadaCommit": "abc1234"
  },
  "classification": {
    "kind": "LandingConflict",
    "rawReason": "Merge conflict in 2 files",
    "suspectedCause": "None",
    "causeRuleId": null,
    "isRecoverable": null,
    "isTransient": null
  },
  "conflict": {
    "isConflict": true,
    "conflictedFiles": [
      "src/core/a.cs",
      "src/core/b.cs"
    ],
    "conflictedFileTotal": 2,
    "conflictedFilesTruncated": false,
    "conflictedDirectories": [
      {
        "directory": "src/core",
        "depth": 2,
        "fileCount": 2
      }
    ],
    "conflictingMissions": [],
    "targetCommitsSinceStart": [],
    "targetCommitTotal": 0
  },
  "attempts": [
    {
      "attempt": 1,
      "entryId": "bbx_mvtv00ab_Example0000",
      "kind": "LandingConflict",
      "reasonExcerpt": "Merge conflict in 1 file"
    }
  ],
  "analysis": null,
  "redaction": {
    "rulesApplied": [
      {
        "rule": "UrlCredentials",
        "count": 1
      }
    ],
    "redactedCount": 1
  },
  "truncation": {
    "fields": [],
    "fieldTotal": 0,
    "timedOut": false,
    "notes": []
  }
}
```
