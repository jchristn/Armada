# Plan: Mission and voyage black box

Status: in progress on branch `feature/blackbox` (2026-10-29). Scorecard items "GitHub issue filing with dedupe" and
"Replay" were removed from scope on 2026-10-29. Paths marked (NEW) do not exist; every other path
and line number was read from `main` at commit bd2e7c1ac. Target release: **v1.2.0** (see section 14 for what that
means for the already-built v1.2.0 images).

## Contents

1. Goal
2. What exists today, and what it lacks
3. Design overview
4. Data model and storage targets
5. Capture
6. Enrichment and analysis
7. Retention, expiration and garbage collection
8. Retrieval surfaces: REST, MCP, WebSocket, clients and SDKs
9. MCP consumption: "review the black box for mission X and take action"
10. Ask Armada
11. User interfaces: dashboard, TUI, Helm CLI, VS Code extension, mobile
12. Security and privacy
13. Extensions (ideation)
14. Documentation, README, CHANGELOG and Postman for v1.2.0
15. Implementation phases
16. Test plan (positive and negative)
17. Open questions
18. Element scorecard
19. Effort summary

## 1. Goal

When a mission or voyage fails, for any reason (dispatch, execution, completion gates, review, handoff, landing,
merge queue, recovery, voyage settlement), Armada writes a **black box entry** at the moment of failure. The entry is
a small bundle of **black box objects** (files): a structured record, rendered briefs, and bounded evidence. It
answers, with evidence:

1. Which mission (id, title) and voyage failed.
2. Where it failed: phase, step, host, and code site.
3. How it failed: the mechanism (exception, exit code, failing command, git operation, gate).
4. Status before and after, and the status timeline.
5. Why it failed: classified cause, with evidence and confidence.
6. What configuration changes could have prevented it.
7. Whether a conflicting mission caused it, and which one.
8. Which files caused the conflict.
9. Which directories caused the conflict.
10. What Armada behavior changes could address it.
11. The potential recovery paths, ranked, each with exact commands and a machine-readable action.

Each entry renders as:

- **`record.json`**: the versioned, machine-readable `BlackBoxRecord`.
- **`brief.md`**, the **FIX THIS brief**: a self-contained package an operator pastes into any coding agent and says
  "FIX THIS". It carries the evidence, hypotheses, recovery paths and a triage instruction: *is this the user's
  configuration, the mission brief, a transient, or a defect in Armada itself?*
- **UI panels** in the dashboard, TUI, Helm CLI, VS Code extension and mobile app, each with **Copy FIX THIS**.

Success criteria:

- Every terminal failure (`Failed`, `LandingFailed`, `Blocked`, non-operator `Cancelled`) and every failed or
  cancelled voyage has exactly one current entry. Earlier attempts keep theirs.
- A maintainer agent given only the brief can locate the throw site in the Armada source (file, method, line, build
  commit) without database access.
- A user given only the dashboard panel can tell what happened and what to click or run next.
- An agent connected over MCP can read an entry, choose a recovery path and run it (with confirmation) in one
  conversation: "Review the black box for mission msn_x and fix it."
- In Ask Armada, "show me the pertinent details from the black box for mission msn_x and give me your opinion on why
  it failed and what could have been done differently" produces a grounded answer that separates evidence from
  opinion.

## 2. What exists today, and what it lacks

Armada already has several black-box-like pieces. The plan builds on them rather than replacing them.

| Existing piece | Where | What it gives | Gap |
|---|---|---|---|
| `Mission.FailureKind` / `FailureReason` | `src/Armada.Core/Models/Mission.cs:221,228` | 18-value `MissionFailureKindEnum` plus free text | Text is the only carrier of details (files, SHAs). It is **erased** by `ResetForRestart` (`MissionService.Review.cs:231`), `TryScheduleOutputRepairAsync` (`MissionService.cs:1237`), `TryEscalateToPlannerAsync` (`AdmiralService.cs:2584`), `HandleInterruptedProcessExitAsync` (`:2654`), `LandingReconciler` (`:280`), `MergeQueueService:709`, `MissionLandingHandler:601`, `ManualMissionHandoff:104` and no-op re-dispatch (`MissionService.Completion.cs:259`). Several paths set a reason with **no kind** (Completion.cs:824; AdmiralService.cs:1809, 2017, 2215; CaptainService.cs:109; VesselRoutes.cs:954). |
| `StageOutcome` (`sto_`, `stage_outcomes`) | `StageOutcomeService.RecordAttemptAsync`, called from `AdmiralService.cs:996` | Per-attempt runtime, model, exit code, failure kind/reason (2000 chars), suspected cause from `FailureCauseClassifier` rules | Recorded only on process exit. Landing, review and merge-queue failures after exit never update it. |
| `HandoffArtifact` (`handoff_artifacts`) | `HandoffArtifactBuilder.cs` | Findings, check results, output, diff (1M chars each) per attempt | Built at handoff, not at failure. |
| `Incident` (`inc_`) | `MissionRecoveryCoordinator.OpenIncidentsForNewFailuresAsync` (`:196`) | RootCause, Postmortem, RecoveryNotes, RescueMissionIds | Root cause is "Classified failure kind: X"; the postmortem is empty unless a human writes it. |
| Rescue brief | `MissionRecoveryCoordinator.BuildRescueBrief` (`:354`) | "## Failure To Fix" with kind, reason (1500 chars), prior diff (4000 chars) | Right shape for an agent, but only for rescues, with no git or configuration forensics. |
| Events | `ArmadaEvent`; `mission.failed`, `mission.landing_failed`, `mission.landing_verification`, `mission.landing_deferred`, `mission.stage_check`, `captain.process_exited`, `captain.stalled`, `voyage.stage_failed`, `papercut` | Timeline raw material | Failure events have **no structured payload** (`MissionService.BuildMissionOutcomeEvent`, `:1786`). `mission.landing_target_advanced` has no SHAs. `MissionStatusChangedPayload{Status, PreviousStatus}` is emitted only from a few paths (`ArmadaServer.cs:353,360`). Events expire after 30 days (`DataRetentionDays`). |
| Conflict listing | `GitService.GetConflictedFilesAsync` (`GitService.cs:614`) | `git diff --name-only --diff-filter=U -z` | Results survive only as text in `FailureReason`. `MergeQueueService.MergeBranchAsync` (`:760`) never collects them. `LandingService.RetryLandingAsync` (`:255`) reads them from the owning repository, not the worktree where the merge ran. |
| Start commit | `Dock.GitAnchorsJson` (`GitAnchorsSnapshot.StartCommit`) and `{LogDirectory}/docks/{dockId}.start` | Base for "what moved on the target" | `.start` is deleted on reclaim; `GitAnchorsJson` is written best-effort (`MissionService.Instructions.cs:190-208`). |
| Commit trailers | `Armada-Mission-Id` and friends (`MessageTemplateSettings.cs:62-78`), `IGitService.FindCommitsByTrailerAsync` | Attribution of target-branch commits to missions | Never used for failure attribution. |
| `VesselCleanupService` | `src/Armada.Core/Services/VesselCleanupService.cs` | Host-aware dock, worktree and branch inspection | Helpers are private; reusable once extracted. |
| `MissionArchive` | `src/Armada.Core/Services/MissionArchive.cs` | Evidence snapshot before a mission is deleted | Only on delete. |
| Voyage | `Models/Voyage.cs`, `VoyageSettlement.DecideAsync` (`:67`), `AdmiralService.HaltVoyageAsync` (`:2770`) | Status | **No failure reason or kind at all.** |
| Storage inventory | `src/Armada.Core/Storage/StorageInventoryService.cs`, `Settings/StorageSettings.cs`, `Routes/StorageRoutes.cs` | Disk usage reporting | Reports space; it is not an object store. The black box needs its own storage settings (`BlackBoxStorageSettings`) and must report into this inventory. |

Defects found while researching. They corrupt black box evidence, so Phase 0 fixes them first:

- `GitService.FastForwardVerifiedCommitAsync` (`GitService.cs:101`) passes the sentence "Checkout has changes;
  verified work is preserved." to `GitDirtyCheckoutException` where porcelain output is expected, so
  `ModifiedPaths` parses to `["ckout has changes; verified work is preserved."]`.
- `LandingService.RetryLandingAsync` (`:255-258`) lists conflicts from the wrong repository, so the list is usually
  empty.
- `MergeQueueService.ReconcileMissionStatusAsync` (`:715`) labels every merge-queue failure `LandingConflict`,
  including test and infrastructure failures.
- `MergeQueueService.MergeBranchAsync` (`:760`) returns a bool and loses the conflicted file list.
- `ArmadaSettings.MaxLandingRetries` (`ArmadaSettings.cs:252`) is never read.
- Already fixed on `main` (CHANGELOG "Unreleased"): blank entries in scope-violation reasons. The Garrison mission
  `msn_mvsqzal6_b1BfR4tnZ29` hit it on 2026-10-29 because the live Admiral still ran the older build. Keep it as a test
  fixture (section 16).

## 3. Design overview

```
 failure site (about 40 writes of Status = Failed / LandingFailed / Blocked / Cancelled)
        |
        | 1. BlackBoxCapture.CaptureAsync(context)     synchronous, bounded (2 s), never throws
        v    identity, status before/after, kind, reason, site, mechanism,
   blackbox_entries row + record.json (State=Captured)   volatile facts: target SHA, porcelain, conflicted files,
        |                                               dock/worktree existence, Harbor link state
        | 2. BlackBoxEnricher (background queue)       runs git on the owning host (Admiral or Harbor)
        v    conflict attribution, target movement, configuration snapshot, attempts, evidence objects,
   State=Enriched                                       fingerprint, similar entries
        |
        | 3. BlackBoxAdvisor (pure rules)              deterministic and unit-testable
        v    why (cause + confidence), configuration recommendations, Armada improvement hypotheses,
   State=Analyzed                                       ranked recovery paths with machine-readable actions, triage
        |
        | 4. Renderers and stores
        v    record.json, brief.md (agent / user / maintainer audiences), evidence/*;
             written through IBlackBoxStore (Database BLOBs by default; Disk, S3, Azure, GCS, CIFS, NFS)
        |
        | 5. Readers
        v    REST, MCP, WebSocket, Armada.Client, dashboard, TUI, Helm, VS Code, mobile, Ask Armada
        |
        | 6. BlackBoxGarbageCollector
             expiration, quotas, orphan sweep, pinning
```

Principles:

- **Capture is never lost.** Capture writes the index row and `record.json` before anything can erase evidence.
  Enrichment and analysis can be re-run, so improving the rules improves old entries.
- **Capture never breaks failure handling.** Every capture call is wrapped. A capture failure logs, emits
  `blackbox.capture_failed`, and the sweeper retries.
- **Belt and braces.** Armada has no central status-transition method (`MissionStateMachine.IsValidTransition` is only
  enforced on manual paths). Capture is wired at the known failure writes, and a **sweeper** captures any
  terminal-failure mission or failed voyage that has no current entry (`CaptureMode=Sweeper`, flagged in the brief as
  lower fidelity).
- **Evidence and interpretation are separate.** Capture and enrichment record facts. Interpretation lives only in
  the advisor, and every claim carries a rule id.
- **Bounded everything.** Every collection follows `docs/ENUMERATION.md`: `[CollectionBound]` on each collection
  property, explicit truncation flags and totals, stored sets paged in SQL.
- **Redacted.** Secrets never enter an entry (section 12).

## 4. Data model and storage targets

### 4.1 Naming

| Concept | Name |
|---|---|
| Feature | Black box |
| Index row | `BlackBoxEntry`, table `blackbox_entries`, prefix `bbx_` (add `BlackBoxEntryIdPrefix` to `src/Armada.Core/Constants.cs`) |
| File in an entry | `BlackBoxObject`, table `blackbox_objects` |
| Structured record | `BlackBoxRecord` (the content of `record.json`) |
| Rendered brief | FIX THIS brief (`brief.md`, `brief.user.md`, `brief.maintainer.md`) |

### 4.2 Tables (migration 103, all four providers)

The index is **always** in the database, whatever the storage target, so every query, filter and garbage-collection
decision is a SQL query. Only the object bytes move between targets.

`blackbox_entries`:

| Column | Type | Notes |
|---|---|---|
| id | text pk | `bbx_...` |
| tenant_id, user_id | text | tenant scoping like every other entity |
| subject_type | text | `Mission` or `Voyage` (`BlackBoxSubjectTypeEnum`) |
| mission_id, voyage_id, vessel_id, fleet_id, captain_id, harbor_id, dock_id, merge_entry_id, incident_id, stage_outcome_id, parent_mission_id | text, indexed where useful | every id a reader may filter on; null where not applicable |
| mission_title, vessel_name | text | denormalized for lists and for entries whose mission was purged |
| attempt | int | 1-based per subject |
| is_current | bool | one current entry per subject |
| supersedes_id | text | previous entry for the same subject |
| phase, step | text | `BlackBoxPhaseEnum`, `BlackBoxStepEnum` (section 5.1) |
| failure_kind | text | `MissionFailureKindEnum` (voyages use the root mission's kind) |
| status_before, status_after | text | |
| triage | text | `BlackBoxTriageEnum` |
| fingerprint | text, indexed | section 6.4 |
| capture_mode | text | `Inline`, `Sweeper`, `Backfill` |
| state | text | `Captured`, `Enriched`, `Analyzed`, `EnrichmentFailed`, `Deleting`, `ObjectMissing` |
| resolution | text | `Open`, `Resolved`, `Duplicate`, `WontFix`, `ArmadaFixed` (`BlackBoxResolutionEnum`) |
| resolution_note, resolution_link | text | what fixed it (mission id, commit, PR, issue URL) |
| pinned | bool | pinned entries are never expired or evicted |
| storage_target | text | `Database`, `Disk`, `S3`, `AzureBlob`, `GoogleCloud`, `Cifs`, `Nfs` (`BlackBoxStorageTargetEnum`) |
| storage_prefix | text | object key prefix for external targets |
| total_bytes, object_count | bigint, int | stored (compressed) size |
| schema_version, rules_version | int | |
| armada_version, armada_commit | text | `BuildIdentity.InformationalVersion` and `BuildIdentity.CommitHash` (`src/Armada.Server/BuildIdentity.cs`) |
| expires_utc | datetime, indexed | computed at capture from the retention policy; null when pinned or retention is 0 |
| created_utc, enriched_utc, analyzed_utc, resolved_utc, last_update_utc | datetime | |

`blackbox_objects`:

| Column | Type | Notes |
|---|---|---|
| id | text pk | |
| entry_id, tenant_id | text, indexed | |
| name | text | `record.json`, `brief.md`, `brief.user.md`, `brief.maintainer.md`, `evidence/agent-output.txt`, `evidence/mission.log`, `evidence/diff.patch`, `evidence/diffstat.txt`, `evidence/check-<n>.txt`, `evidence/verification-<n>.txt`, `evidence/merge-entry.txt`, `evidence/git-status.txt`, `evidence/target-commits.txt`, `evidence/events.json` |
| content_type | text | `application/json`, `text/markdown`, `text/plain`, `text/x-diff` |
| content_encoding | text | `gzip` or `identity` |
| size_bytes, uncompressed_bytes | bigint | |
| sha256 | text | integrity check on every read and on storage migration |
| storage_key | text | full key for external targets; null for `Database` |
| data | BLOB | object bytes when the entry's target is `Database`: SQLite `BLOB`, PostgreSQL `bytea`, SQL Server `varbinary(max)`, MySQL `LONGBLOB`. Null for external targets. |
| created_utc | datetime | |

`blackbox_files` (relational rows for cross-entry queries such as hot spots):

| Column | Notes |
|---|---|
| id, entry_id, tenant_id, vessel_id | |
| path | repository-relative, forward slashes |
| directory | parent directory of `path` |
| role | `Conflicted`, `DirtyInCheckout`, `OutOfScope`, `ChangedByMission`, `ChangedByConflictingMission`, `BoundaryViolation` (`BlackBoxFileRoleEnum`) |
| conflicting_mission_id | the mission whose landed commit also touched it, when known |

`blackbox_links`:

| Column | Notes |
|---|---|
| id, entry_id, tenant_id, mission_id | |
| relation | `LandedOnTargetSinceStart`, `OverlappingFiles`, `SharedDock`, `SiblingInVoyage`, `DependencyOf`, `ParentOfRescue`, `RescueOf`, `CascadeRoot` (`BlackBoxLinkRelationEnum`) |
| commit_hash, overlap_file_count | |

`blackbox_notes` (annotations by operators, agents and Ask Armada):

| Column | Notes |
|---|---|
| id, entry_id, tenant_id, user_id | |
| author_kind | `Operator`, `Agent`, `AskArmada`, `System` |
| body | Markdown, 8000 chars max |
| created_utc | |

New mission columns: `missions.last_blackbox_entry_id`, `missions.start_commit`. New voyage column:
`voyages.last_blackbox_entry_id`.

Indexes: `(tenant_id, mission_id, is_current)`, `(tenant_id, voyage_id)`, `(tenant_id, vessel_id, created_utc)`,
`(tenant_id, fingerprint, created_utc)`, `(expires_utc)`, `(tenant_id, resolution, triage)`,
`blackbox_files(tenant_id, vessel_id, directory)`, `blackbox_objects(entry_id, name)` unique.

The record is a forensic snapshot that must keep exactly what was true at capture time across schema versions, so it
is a versioned JSON document deserialized into typed classes (never read through `JsonElement`). Files, links and
notes are relational rows because they are the cross-entry query surface.

### 4.3 The `BlackBoxRecord` document (NEW, `src/Armada.Core/Models/BlackBox*.cs`, one class per file)

```
BlackBoxRecord
  EntryId, SchemaVersion, RulesVersion, CaptureMode, State, CreatedUtc
  Subject        BlackBoxSubject        type, mission/voyage ids, title, vessel (id, name, repo URL, default branch),
                                        captain (id, name, runtime, model, endpoint), persona, pipeline stage,
                                        Harbor (id, name), attempt, isRescue, parentMissionId
  Status         BlackBoxStatusChange   before, after, changedUtc, timeline[] (bounded 100: status, utc, source)
  Site           BlackBoxSite           phase, step, host (Admiral | Harbor:{id}), component, method, sourceFile,
                                        sourceLine, armadaVersion, armadaCommit
  Mechanism      BlackBoxMechanism      exceptionType, exceptionMessage (4000), stackFrames[] (Armada frames only,
                                        bounded 20), exitCode, runtimeFailureKind, failingCommand, commandExitCode,
                                        commandOutputTail (8000), gitOperation, gitStderrTail (4000)
  Classification BlackBoxClassification kind, rawReason (8000), suspectedCause (FailureCauseEnum), causeRuleId,
                                        isRecoverable, isTransient
  Workspace      BlackBoxWorkspace      dockId, dockState, worktreePath, worktreeExists, checkoutPath, branchName,
                                        branchTip, missionCommit, startCommit, startCommitSource, targetBranch,
                                        targetTipAtStart, targetTipAtFailure, commitsBehindTarget,
                                        commitsAheadOfStart, checkoutDirty, dirtyPaths[] (bounded 200),
                                        harborLinkState, harborLastSeenUtc
  Conflict       BlackBoxConflict       isConflict, conflictedFiles[] (bounded 500, TotalCount),
                                        conflictedDirectories[] (bounded 100, with counts),
                                        conflictingMissions[] (bounded 25: id, title, commit, landedUtc,
                                        overlappingFiles[] bounded 50), targetCommitsSinceStart[] (bounded 50)
  Scope          BlackBoxScope          scopedFiles[], outOfScopeFiles[], boundaryFindings[] (each bounded 200)
  Configuration  BlackBoxConfiguration  effective values that shaped this failure, each with its source (Mission,
                                        Voyage, Vessel, WorkflowProfile, Captain, Persona, Global, Default):
                                        landingMode, autoPush, requiresReview, allowConcurrentMissions,
                                        waitForVoyageWorkers, dependsOnMissionId, build/test commands,
                                        stageCheckTimeoutMinutes, stallThresholdMinutes, maxMissionRuntimeMinutes,
                                        maxRecoveryAttempts, protectedPathPatterns ... (bounded 64)
  Attempts       BlackBoxAttempt[]      prior attempts and rescue chain (bounded 20): attempt, entryId,
                                        stageOutcomeId, kind, reason excerpt, runtime/model, durationMs
  Evidence       BlackBoxEvidenceIndex  names, sizes and short excerpts of the evidence objects; checkResults[]
                                        (StageCheckResult, bounded 10); papercuts[] (bounded 10); diff stat
                                        (files, insertions, deletions, top 50 paths); relevantEvents[] (bounded 50)
  Analysis       BlackBoxAnalysis       summary (3-6 sentences); why[] (bounded 10: ruleId, statement, confidence,
                                        evidenceRefs[]); configRecommendations[] (bounded 10: setting, scope,
                                        currentValue, suggestedValue, rationale, ruleId, howToApply, applyAction);
                                        armadaImprovements[] (bounded 10: area, hypothesis, evidence,
                                        sourcePointers[], severity, ruleId, knownIssue); recoveryPaths[] (bounded
                                        10, ranked: id, title, preconditions, steps[] with MCP/REST/CLI commands,
                                        action {tool, arguments}, risk, dataLossRisk, expectedOutcome); triage
                                        (verdict, confidence, votes[])
  Related        BlackBoxRelated        fingerprint, similarEntries[] (bounded 10, with their resolution),
                                        occurrencesLast30Days, cascade (root entry id, downstream mission ids
                                        bounded 100)
  Voyage         BlackBoxVoyageRollup   (voyage entries only) missions by status, failed mission entry ids (bounded
                                        100), cascade root, settlement rule, halt reason
  Redaction      BlackBoxRedaction      rulesApplied[], redactedCount
  Truncation     BlackBoxTruncation     truncated fields and their original sizes
```

Every string has a hard cap and every collection a `[CollectionBound(..., Reason = ...)]`.

### 4.4 Size budget

- `record.json` max 256 KB uncompressed (`BlackBoxLimits.MaxRecordBytes`, NEW).
- Each evidence object max 1 MB uncompressed (`MaxEvidenceObjectBytes`, configurable 64 KB to 16 MB); head and tail
  kept, with a marker line and the original size recorded in `Truncation`.
- Each entry max 8 MB compressed (`MaxEntryBytes`). Over budget, the largest evidence objects are trimmed first.
- `brief.md` targets 12 KB with a 40 KB hard cap.
- Nothing is dropped silently.

### 4.5 Schema and compatibility

The black box is structured for applications to consume, with a documented schema (`docs/BLACKBOX_SCHEMA.md`) and a
JSON Schema generated from the models (`docs/blackbox-record.schema.json`).

- `record.json` carries `SchemaVersion`.
- Additive changes (new optional fields, new enum values) keep the version. Renames, removals, and type or meaning
  changes bump it.
- Readers must ignore unknown fields and treat unknown enum values as `Unknown`; every black box enum has an `Unknown`
  member, and Armada's reader (`BlackBoxJson`, with a tolerant enum converter) does this itself.
- Armada can re-render older records: briefs are rendered live from `record.json`.
- `GET .../blackbox/{id}?schemaVersion=N` is out of scope unless needed.
- Every object has a stable name and content type, listed in the schema document, and downloads include
  `manifest.json` (entry id, schema version, and every object with name, content type, size and SHA-256).

### 4.6 Storage targets

`IBlackBoxStore` (NEW, `src/Armada.Core/BlackBox/Storage/IBlackBoxStore.cs`):

```csharp
Task WriteAsync(BlackBoxObjectWrite write, CancellationToken token = default);
Task<Stream> OpenReadAsync(BlackBoxObjectRef reference, long offset, long? length, CancellationToken token = default);
Task<bool> ExistsAsync(BlackBoxObjectRef reference, CancellationToken token = default);
Task DeleteAsync(BlackBoxObjectRef reference, CancellationToken token = default);
Task<BlackBoxStoreDeleteResult> DeleteManyAsync(BlackBoxObjectRefSet references, CancellationToken token = default);
IAsyncEnumerable<BlackBoxStoredKey> EnumerateAsync(string prefix, CancellationToken token = default);
Task<BlackBoxStoreHealth> CheckHealthAsync(CancellationToken token = default);
```

Implementations:

| Target | Class (NEW) | Backing | Notes |
|---|---|---|---|
| **Database (default)** | `DatabaseBlackBoxStore` | `blackbox_objects.data` BLOB | No extra configuration. Works on all four providers. Included in Armada backups. |
| Disk | `BlobjectBlackBoxStore` + `Blobject.Disk` | directory on the Admiral host | Default directory `{DataDirectory}/blackbox`. |
| S3 and S3-compatible | `Blobject.AmazonS3` | AWS S3, Less3, MinIO, View | endpoint, region, bucket, access key, secret key, path-style, TLS |
| Azure Blob | `Blobject.AzureBlob` | Azure Storage | account, container, key or connection string |
| Google Cloud Storage | `Blobject.GoogleCloud` | GCS | project, bucket, credentials JSON path |
| CIFS | `Blobject.CIFS` | SMB share | host, share, domain, user, password |
| NFS | `Blobject.NFS` | NFSv3 export | host, export path |
| Other | implement `IBlackBoxStore` | | Registered by `StorageTargetEnum` value; the factory throws a clear error for unknown targets. |

Blobject is the repository owner's storage library (`Blobject.Core` `BlobClientBase`, used the same way in
`~/Code/Armor/src/Armor.Core/Storage/StorageRepositoryFactory.cs`). Pin the same versions Armor uses (6.1.x).

Settings (`BlackBoxSettings`, NEW, nested under `ArmadaSettings.BlackBox`, validated in setters, written back with all
properties like every other settings object):

```json
"blackBox": {
  "enabled": true,
  "captureTimeoutMs": 2000,
  "storage": {
    "target": "Database",
    "keyPrefix": "armada/blackbox",
    "compress": true,
    "maxEvidenceObjectBytes": 1048576,
    "maxEntryBytes": 8388608,
    "fallbackToDatabase": true,
    "disk": { "directory": null },
    "s3": { "endpoint": null, "region": "us-east-1", "bucket": null, "accessKey": null, "secretKey": null, "usePathStyle": true, "useSsl": true },
    "azureBlob": { "accountName": null, "container": null, "accessKey": null, "connectionString": null },
    "googleCloud": { "projectId": null, "bucket": null, "credentialsPath": null },
    "cifs": { "hostname": null, "share": null, "domain": null, "username": null, "password": null },
    "nfs": { "hostname": null, "exportPath": null }
  },
  "retention": { "...": "section 7" },
  "sweeper": { "intervalSeconds": 60, "pageSize": 100 },
  "summaryEndpointId": null
}
```

Rules:

- Secret values (`secretKey`, `accessKey`, `connectionString`, `password`, credentials path contents) are marked secret
  in `SettingsData`, masked in every settings read, and never logged.
- Key layout for external targets:
  `{keyPrefix}/{tenantId}/{vesselId or _}/{yyyy}/{MM}/{entryId}/{objectName}`.
- The storage target is recorded **per entry**. Changing the setting affects new entries only; existing entries
  stay readable from where they were written.
- **Fallback.** When an external write fails and `fallbackToDatabase` is true, the entry is written to the database
  target, marked `storage_target=Database`, and a `blackbox.storage_fallback` event and a dashboard warning are raised.
  With fallback off, the index row and `record.json` still go to the database, so capture is never lost; only the
  evidence objects are missing (`state=ObjectMissing` for those objects).
- **Migration between targets.** `POST /api/v1/blackbox/storage/migrate` (Admin) and
  `armada blackbox storage migrate --to S3` start a background job. It copies each object, verifies its SHA-256,
  switches the entry's `storage_target`, then deletes the source copy. The job is resumable, idempotent, paged, and
  reports progress through `GET /api/v1/blackbox/storage/migrations/{id}`.
- **Health.** `CheckHealthAsync` (Blobject `ValidateConnectivity` for external targets) is added as a doctor check
  (`GET /api/v1/doctor` grows from 11 to 12 checks; update `_CodeFixedResponses` in
  `src/Test.Shared/Suites/E2E/ResponseShapeSuite.cs`).
- **Storage inventory.** `StorageInventoryService` reports black box bytes per target as a `StorageLocation`.
- **Harbor split mode.** Evidence that lives on a Harbor (worktree status, logs) is fetched over the Harbor link
  during enrichment. Objects are always written by the Admiral to the Admiral's configured target.
- **Backups.** Database-target entries are included in `backup`/`restore`. External-target entries are not; the
  backup manifest lists them, and the restore report says which entries point at external objects.

## 5. Capture

### 5.1 Where it failed: phase and step taxonomy

`BlackBoxPhaseEnum` and `BlackBoxStepEnum` (NEW). Each failure write passes one step; the phase is derived from it.

| Phase | Steps | Current failure writes |
|---|---|---|
| Dispatch | `VoyageParentNotRunnable`, `AssignmentFailed`, `NoEligibleCaptain`, `HarborUnavailable`, `MemoryPressure` | `MissionService.TryAssignAsync:460`, `FailAssignmentAsync:1340` |
| Provision | `DockProvision`, `WorktreeCreate`, `BranchCreate`, `StartCommitRecord` | `DockService.ProvisionAsync:62`, `ProvisionOnHarborAsync:278` |
| Execution | `ProcessCrash`, `UsageLimit`, `AuthFailure`, `ModelUnavailable`, `Stall`, `MaxRuntime`, `RecoveryExhausted`, `Interrupted`, `OperatorStop` | `AdmiralService.HandleTerminalProcessExitFailureAsync:2677`, `HealthCheckCaptainAsync:1689,1772`, `CleanupStaleCaptainsAsync:843`, `CaptainService.FinalizeRecoveryFailureAsync:522`, `RecordOperatorStopAsync:439` |
| Completion | `NoOp`, `Unfinished`, `CaptainBlocked`, `InvalidOutput`, `ScopeViolation`, `BoundaryViolation`, `DefinitionOfDone`, `StageCheck` | `MissionService.Completion.cs:59,127,279,318`, `MissionService.cs:1582` |
| Review | `JudgeRejected`, `ReviewDenied`, `ReviewTimeout`, `ReviewApprovedDockMissing` | `Completion.cs:657,692`, `Review.cs:82,291` |
| Handoff | `PlanParse`, `StageBranchHarden`, `DependencyCancelled`, `IntegrationBarrier` | `Handoff.cs:141,198,262,309`, `IntegrationBarrier.cs:45,146` |
| Landing | `DockMissing`, `TargetIntegration`, `Verification`, `DirtyCheckout`, `UnpublishedAncestry`, `Push`, `PullRequest`, `FastForward`, `LocalMerge`, `RetryLanding`, `DeferralExpired` | `MissionLandingHandler.cs:240,256,285,308,422,520,627`, `LandingService.cs:134-271`, `LandingIntegrityService.cs:237` |
| MergeQueue | `QueueResolveRepo`, `QueueMergeConflict`, `QueueTests`, `QueueProcessing`, `QueuePush` | `MergeQueueService.cs:508,562,605,655,715` |
| Recovery | `RescueFailed`, `RedispatchExhausted`, `OrphanedExit`, `UnconfirmedStop`, `MissingMergeHandoff` | `AdmiralService.cs:1809,2017,2215`, `MissionRecoveryCoordinator` |
| Voyage | `SettlementFailed`, `Halted`, `Cancelled`, `StageFailed` | `VoyageSettlement.cs:67`, `AdmiralService.HaltVoyageAsync:2770`, `VoyageCancellation.cs:37` |
| External | `VesselDeleted`, `OperatorTransition` | `VesselRoutes.cs:954`, `McpVesselTools.cs:355`, manual `transition_mission_status` |

Both enums include `Unknown`, which the sweeper uses when it cannot infer the step.

### 5.2 Capture API

```csharp
// src/Armada.Core/BlackBox/IBlackBoxCapture.cs (NEW)
Task<string?> CaptureMissionFailureAsync(BlackBoxCaptureContext context, CancellationToken token = default);
Task<string?> CaptureVoyageFailureAsync(BlackBoxVoyageCaptureContext context, CancellationToken token = default);
```

`BlackBoxCaptureContext` (NEW) is filled at the failure site, which knows things nobody can reconstruct later:

- `Mission` (after mutation) and `StatusBefore` (every site already holds the old value).
- `Step`, optional `Exception`, optional `RuntimeExitInfo`, `FailingCommand`, `CommandResult`.
- Optional `BlackBoxGitFacts` (NEW): `ConflictedFiles`, `DirtyPaths`, `TargetTipBefore`, `TargetTipAfter`,
  `WorktreeExisted`, `GitStderr`.
- `[CallerMemberName]`, `[CallerFilePath]`, `[CallerLineNumber]`, trimmed to repository-relative paths, so the entry
  points at the failure site without relying on stack traces.

`BlackBoxCapture` (NEW):

1. Computes `attempt`; marks the previous current entry `is_current=false` and sets `supersedes_id`.
2. Builds Subject, Status, Site, Mechanism, Classification and Configuration from memory (no I/O).
3. Builds Workspace and Conflict from `BlackBoxGitFacts` when present; otherwise enrichment fills them.
4. Redacts, enforces bounds, writes the index row and `record.json`, renders `brief.md` from what it has, and
   enqueues enrichment.
5. Sets `Mission.LastBlackBoxEntryId` and emits `blackbox.captured` with a typed payload
   `{EntryId, MissionId, VoyageId, Phase, Step, Kind}` (WebSocket broadcast included).
6. Has a 2-second budget for everything beyond the database insert; on timeout it writes what it has.

### 5.3 Wiring the capture sites

1. Add `RecordFailureAsync(mission, statusBefore, step, ...)` helpers on the services that fail missions
   (`MissionService`, `AdmiralService`, `MissionLandingHandler`, `MergeQueueService`, `LandingService`,
   `CaptainService`, `LandingIntegrityService`). Each calls capture after the mission row update succeeds.
2. Change each failure write in 5.1 to "update, then `RecordFailureAsync`", passing the step and any git facts it holds.
   One commit per phase row keeps reviews small.
3. Give the kind-less failure writes listed in section 2 a kind.
4. **Preserve, do not erase.** Paths that clear `FailureReason` and `FailureKind` keep clearing the live mission
   fields (correct for a restarted mission). The entry survives, and `Mission.LastBlackBoxEntryId` still points at it.
5. Emit `MissionStatusChangedPayload` with `PreviousStatus` on every capture, so the timeline is complete from now on.

### 5.4 Sweeper and backfill

`BlackBoxSweeper` (NEW) runs every `blackBox.sweeper.intervalSeconds` (default 60) inside the Admiral maintenance loop.
It pages (`EnumerationQuery`, page size 100) through missions in `Failed`, `LandingFailed`, `Blocked` or non-operator
`Cancelled` whose `LastBlackBoxEntryId` is null or older than their `LastUpdateUtc`, plus failed or cancelled voyages
with no current entry, and captures them with `CaptureMode=Sweeper`. It re-queues `EnrichmentFailed` entries with
exponential backoff (at most 5 tries).

Backfill: `POST /api/v1/blackbox/backfill` (Admin) and `armada blackbox backfill [--since 30d]` run the sweeper over
historical missions with `CaptureMode=Backfill`.

### 5.5 Making volatile facts available at the failure site

| Fact | Change |
|---|---|
| Conflicted files, merge queue | `MergeQueueService.MergeBranchAsync` returns a result with `ConflictedFiles`, read from the integration worktree before abort. |
| Conflicted files, retry landing | `LandingService.RetryLandingAsync` reads `GetConflictedFilesAsync` from the worktree where the merge ran. |
| Conflicted files, target integration | `LandingTargetSynchronization.SynchronizeAsync` already throws `GitMergeConflictException.ConflictedFiles`; pass them through. |
| Dirty paths | `GitDirtyCheckoutException` carries real porcelain from `EnsureTrackedFilesCleanAsync` (fix `GitService.cs:101`). |
| Target tip before and after | `LandingTargetSynchronization` and `MergeQueueService` record the SHA they integrated against; `mission.landing_target_advanced` gets a `{Before, After}` payload. |
| Start commit | `DockService` writes `GitAnchorsJson.StartCommit` at every provision, and `Mission.StartCommit` is set at assignment. |
| Worktree existence | `MissionLandingHandler.EnsureLandingDockAsync` passes `WorktreeExisted=false` when it had to recreate, or could not recreate, the worktree. |
| Harbor link state | `HarborConnectionManager.GetLinkState(harborId)` (NEW) is recorded for Harbor-hosted docks. |
| Memory-pressure deferrals | `AdmiralService` emits a per-mission `mission.dispatch_deferred` event with the reason. |

## 6. Enrichment and analysis

### 6.1 Enrichment (`BlackBoxEnricher`, NEW, background queue)

Runs on the owning host through the same host routing `VesselCleanupService` uses. Extract its private helpers
(`DescribeWorktreeRiskAsync`, `DescribeWorkReferenceAsync`, `IsMergedAsync`, per-host `RepoState` loading) into
`VesselGitInspector` (NEW), shared by both services.

1. **Workspace:** worktree existence, branch tip, porcelain status, ahead and behind against the target
   (`IGitService.GetDivergenceAsync`).
2. **Target movement:** commits on the target between `startCommit` and `targetTipAtFailure`. Needs
   `IGitService.GetMergeBaseAsync(repo, a, b)` and `GetCommitRangeAsync(repo, from, to, limit)` (NEW), returning
   commit, date, subject, trailers and changed paths. Harbor protocol twins for remote docks.
3. **Conflict attribution:** for each commit since start, read its `Armada-Mission-Id` trailer and intersect its
   changed paths with the conflicted files and with the failed mission's changed paths. Each intersecting mission
   becomes a `conflictingMissions[]` entry and a `blackbox_links` row. Commits without a trailer are reported as
   "outside Armada" with their author.
4. **Concurrent overlap:** other missions on the vessel that were active during this mission's run window and touched
   overlapping paths (from their diff snapshot or branch diff), even if they had not landed.
5. **Directories:** roll conflicted files up to directories at depths 1 to 3, with counts; report the deepest
   directory holding at least two conflicted files, plus each file's parent.
6. **Attempts:** stage outcomes, handoff artifacts, the rescue chain (`ParentMissionId`, `Incident.RescueMissionIds`),
   redispatch counters.
7. **Evidence objects:** agent output tail, mission log tail, check results, verification events, merge-entry test
   output, papercuts, `git status`, target commits, diff and diff stat, the last 50 relevant events.
8. **Related:** fingerprint matches, similar entries and how they were resolved.

### 6.2 Analysis (`BlackBoxAdvisor`, NEW, pure)

`BlackBoxAdvisor.Analyze(BlackBoxRecord record, BlackBoxRuleCatalog catalog)` returns a `BlackBoxAnalysis`. It is
deterministic and does no I/O, so every rule has golden-file tests. Rules live in `BlackBoxRuleCatalog` (NEW,
versioned code, same style as `FailureCauseRuleCatalog`). Each rule has an id (for example `BBX-LAND-003`), a match
predicate, and can produce why findings, configuration recommendations, Armada improvement hypotheses, recovery
paths and a triage vote. Rule text uses `{placeholders}` filled from the record, so output is concrete.

Starter rules, each taken from a failure observed in this repository:

| Rule | Match | Why | Configuration recommendation | Armada improvement | Recovery paths |
|---|---|---|---|---|---|
| BBX-LAND-001 dirty target checkout | step `DirtyCheckout`, dirty paths present | "The checkout at {path} has uncommitted tracked changes in {n} files, so the landing refused to overwrite them." | Use `LandingMode=PullRequest` or `MergeQueue` for vessels whose checkout is also a human working copy, or point the vessel at a dedicated clone. | Land into a dedicated integration worktree instead of the operator's checkout. | 1) commit or stash in the checkout, then `retry_landing`; 2) switch the vessel to PullRequest, then `retry_landing`. |
| BBX-LAND-002 dock deleted before landing | `worktreeExists=false` at landing | "The dock directory was removed before landing; git could not start in it." | none | Check `DockLeaseState.LeaseStatuses` on every reclaim path; land from the branch rather than the worktree. | `retry_landing` (recreates the worktree from the branch); if the branch is gone, restore it from `missionCommit`. |
| BBX-LAND-003 conflict with a landed mission | conflicted files and conflicting missions present | "{mission} landed {commit} on {target} at {utc}, changing {k} of the same files." | `DependsOnMissionId` between them, `AllowConcurrentMissions=false`, or voyage `WaitForVoyageWorkers=true`. | Detect file overlap at dispatch and serialize overlapping missions (13.1). | Rescue that rebases onto the current target; or resolve on the branch, then `retry_landing`. |
| BBX-LAND-004 conflict only in generated files | every conflicted file matches a generated or append-only pattern (`CHANGELOG.md`, `wwwroot/i18n/armada.json`, `docs/api-surface-1.0.json`, the Postman collection) | "Only generated or append-only files conflicted." | none | Extend `ChangelogLandingConflict` to regenerate the i18n catalog, API surface and Postman collection on conflict. | Regenerate, then `retry_landing`. |
| BBX-LAND-005 verification failed | step `Verification` | "Post-merge verification `{cmd}` failed with exit {code}." | Check the workflow profile commands and `StageCheckTimeoutMinutes`. | Run the command on the target tip too, to separate pre-existing failures from new ones. | Fix forward on the branch and rerun. |
| BBX-MQ-001 merge-queue mislabel | phase MergeQueue, kind `LandingConflict`, no conflicted files | "The merge queue reported a failure that was not a merge conflict." | none | Classify merge-queue failures by step (Phase 0). | Depends on the real step. |
| BBX-SCOPE-001 false scope violation | kind `ScopeViolation` and every out-of-scope entry is blank or matches a scoped file | "The scope check flagged files that were in scope." | none | Triage `ArmadaDefect`, High. Point at `MissionService.FindOutOfScopeFiles`. | Complete the mission manually after review, or restart it. |
| BBX-SCOPE-002 scope check skipped | start commit missing | "The scope check was skipped because the start commit was not recorded." | none | Record the start commit at every provision (5.5). | none |
| BBX-EXEC-001 usage limit | `runtimeFailureKind=UsageLimit` | "The {runtime} account hit its usage limit." | Add a fallback captain through persona `CaptainPriorities`. | none | Wait for the quarantine to expire, or dispatch on another captain. |
| BBX-EXEC-002 stall | kind `StallRecoveryExhausted` | "No activity for {x} minutes; recovery exhausted." | Raise `StallThresholdMinutes` if the step was a long build or test; check `ToolWaitHardLimitMinutes`. | Treat long-running tool calls as activity. | `restart_mission`. |
| BBX-EXEC-003 max runtime | kind `MaxRuntimeExceeded` | "The mission ran longer than {n} minutes." | Raise `MaxMissionRuntimeMinutes` or split the mission. | none | Split into smaller missions. |
| BBX-DEP-001 dependent work dispatched in parallel | the captain reported BLOCKED naming unlanded prerequisites | "Missions that depend on each other were dispatched in parallel." | Chain them with `DependsOnMissionId`. | Detect "depends on mission N" language at dispatch and warn or chain automatically. | Redispatch after the prerequisites land. |
| BBX-INFRA-001 Harbor disconnected | `harborLinkState=Disconnected` | "Harbor {name} was disconnected." | none | none | Reconnect the Harbor, then `retry_landing` or `restart_mission`. |
| BBX-NOOP-001 no-op completion | kind `NoOp` | "The captain finished without producing changes." | none | Flag briefs that ask only for verification. | Restart with a clarified brief. |

Every recovery path carries a machine-readable action, for example
`{ "tool": "retry_landing", "arguments": { "missionId": "msn_..." } }`, so MCP clients and Ask Armada can run it
(section 9).

**Optional model-written summary.** When `blackBox.summaryEndpointId` names a model endpoint, enrichment asks that
model to write `Analysis.Summary` from the record, labeled "model-written". Rule findings stay authoritative.

### 6.3 Triage

Each rule votes `UserConfig`, `MissionBrief`, `Transient`, `ArmadaDefect`, `External` or `Unknown`, with a weight. The
highest total wins; a tie reports `Unknown` and both candidates. `ArmadaDefect` entries feed the maintainer bundle.

### 6.4 Fingerprint

SHA-256 of `phase|step|kind|site.method|normalized reason template`. The template replaces ids, SHAs, paths, numbers
and timestamps with placeholders. Entries with the same fingerprint are the same failure mode.

## 7. Retention, expiration and garbage collection

### 7.1 Policy

`BlackBoxRetentionSettings` (NEW, `blackBox.retention`):

| Setting | Default | Range | Meaning |
|---|---|---|---|
| `retentionDays` | 180 | 0 to 3650 (0 = keep forever) | Age after which an unpinned entry expires. Longer than event retention (30 days) on purpose: the entry outlives the events it summarizes. |
| `resolvedRetentionDays` | 30 | 0 to 3650 | Age after resolution after which a resolved entry expires, if sooner than `retentionDays`. |
| `keepLatestPerMission` | true | | The current entry of a mission is kept until the mission itself is deleted, even past expiry. |
| `maxEntriesPerVessel` | 1000 | 10 to 100000 | Oldest unpinned, non-current entries are evicted above this. |
| `maxTotalBytes` | 1073741824 (1 GB) | 16 MB to 1 TB | Global cap; oldest unpinned, resolved entries go first, then oldest unpinned. |
| `deleteWithMission` | false | | When a mission is purged, delete its entries too (false keeps them, with the denormalized title). |
| `deleteWithVessel` | true | | When a vessel is deleted, delete its entries. |
| `gcIntervalMinutes` | 60 | 5 to 1440 | How often garbage collection runs. |
| `gcBatchSize` | 200 | 10 to 1000 | Entries per batch. |
| `orphanGraceHours` | 24 | 1 to 720 | Minimum age before an unreferenced external object is deleted. |

Per-vessel override: `Vessel.BlackBoxRetentionDays` (nullable). Per-tenant entries count toward the global caps.

`expires_utc` is computed at capture and recomputed when the entry is resolved, pinned, unpinned, or when the policy
changes (a background recompute job runs on settings change).

### 7.2 `BlackBoxGarbageCollector` (NEW)

Runs every `gcIntervalMinutes` and on demand. Each run:

1. **Expire:** entries with `expires_utc < now`, not pinned, and not protected by `keepLatestPerMission`.
2. **Quota:** per-vessel count, then global bytes, evicting in the order in 7.1.
3. **Cascade:** entries whose vessel was deleted (`deleteWithVessel`) or mission purged (`deleteWithMission`).
4. **Delete, in two phases:** mark the entry `state=Deleting`, delete its objects from the store
   (`DeleteManyAsync`), then delete its `blackbox_objects`, `blackbox_files`, `blackbox_links` and `blackbox_notes`
   rows and the entry row. If the store delete fails, the entry stays `Deleting` and is retried next run; it is
   hidden from readers.
5. **Orphan sweep (external targets):** enumerate keys under `keyPrefix`. Keys with no row and older than
   `orphanGraceHours` are deleted. Rows whose object is missing are marked `ObjectMissing` and reported.
6. **Report:** emit `blackbox.gc_completed` with counts (expired, evicted, cascaded, orphans removed, objects missing,
   bytes freed, failures) and update the Prometheus metrics in 13.4.

### 7.3 Manual controls

| Operation | REST | MCP | CLI |
|---|---|---|---|
| Delete one entry | `DELETE /api/v1/blackbox/{id}` | `delete_blackbox_entries` | `armada blackbox delete <id>` |
| Delete many (bounded 1000 ids) | `POST /api/v1/blackbox/delete` | `delete_blackbox_entries` | `armada blackbox delete <id...>` |
| Purge by filter (Admin, dry run by default) | `POST /api/v1/blackbox/purge` | `purge_blackbox` | `armada blackbox purge --vessel vsl_x --before 2026-09-01 [--execute]` |
| Pin / unpin | `POST /api/v1/blackbox/{id}/pin`, `/unpin` | `pin_blackbox` | `armada blackbox pin <id>` |
| Run garbage collection now (Admin; `dryRun` returns what would go) | `POST /api/v1/blackbox/gc` | `run_blackbox_gc` | `armada blackbox gc [--dry-run]` |
| Retention status | `GET /api/v1/blackbox/retention` (policy, counts, bytes, next run, last run report) | `get_blackbox_retention` | `armada blackbox retention` |

Interactions:

- Deleting a mission through `delete_missions` or `purge_mission` follows `deleteWithMission`.
- Tenant deletion deletes all of that tenant's entries and objects.
- `RetentionService` and `DataExpiryService` skip black box tables; the black box has its own policy.
- `MissionArchive` records the current entry id so archives link to the black box.

## 8. Retrieval surfaces: REST, MCP, WebSocket, clients and SDKs

### 8.1 REST (tag `BlackBox`)

| Method | Route | Auth | Response |
|---|---|---|---|
| GET | `/api/v1/missions/{id}/blackbox` | mission read | current `BlackBoxEntry` with `record` (404 when none) |
| GET | `/api/v1/missions/{id}/blackbox/brief` | mission read | `text/markdown`; `?audience=agent\|user\|maintainer` (default agent) |
| GET | `/api/v1/missions/{id}/blackbox/entries` | mission read | `EnumerationResult<BlackBoxEntrySummary>` (all attempts) |
| GET | `/api/v1/voyages/{id}/blackbox` | voyage read | voyage entry |
| GET | `/api/v1/voyages/{id}/blackbox/brief` | voyage read | Markdown |
| GET | `/api/v1/blackbox/{id}` | read | entry with `record` |
| GET | `/api/v1/blackbox/{id}/brief` | read | Markdown |
| GET | `/api/v1/blackbox/{id}/sections/{section}` | read | one record section (Subject, Status, Site, Mechanism, Classification, Workspace, Conflict, Scope, Configuration, Attempts, Evidence, Analysis, Related, Voyage) |
| GET | `/api/v1/blackbox/{id}/objects` | read | `EnumerationResult<BlackBoxObjectSummary>` |
| GET | `/api/v1/blackbox/{id}/objects/{name}` | read | object bytes; `?offset=&length=` (max 1 MB per read), `Content-Type` from the object, `ETag` = SHA-256 |
| GET | `/api/v1/blackbox/{id}/download` | read | `.zip` of every object plus `manifest.json` |
| GET | `/api/v1/blackbox` and POST `/api/v1/blackbox/enumerate` | read | `EnumerationResult<BlackBoxEntrySummary>`; filters: vesselId, voyageId, missionId, captainId, phase, step, kind, triage, resolution, fingerprint, pinned, isCurrent, path (any file role), createdAfter/Before; order CreatedDescending default |
| POST | `/api/v1/blackbox/search` | read | full-text search over reason, summary and notes; paged |
| POST | `/api/v1/blackbox/{id}/notes` | write | add a note |
| GET | `/api/v1/blackbox/{id}/notes` | read | paged notes |
| POST | `/api/v1/blackbox/{id}/resolve` | write | `{resolution, note, link}` |
| POST | `/api/v1/blackbox/{id}/recovery/{pathId}` | write (per the action's own authorization) | runs a recovery path; `dryRun` defaults to true and returns a `confirmToken` (10-minute expiry) that the real run requires |
| POST | `/api/v1/blackbox/{id}/reanalyze` | TenantAdmin | re-runs enrichment and analysis |
| POST | `/api/v1/blackbox/bundle` | read | maintainer bundle (13.2), up to 50 entries, Markdown or JSON |
| POST | `/api/v1/blackbox/hotspots/enumerate` | read | `EnumerationResult<BlackBoxHotspot>` |
| POST | `/api/v1/blackbox/fingerprints/enumerate` | read | `EnumerationResult<BlackBoxFingerprintSummary>` |
| retention and GC routes | section 7.3 | | |
| storage routes | `GET /api/v1/blackbox/storage` (target, health, bytes), `POST .../storage/migrate`, `GET .../storage/migrations/{id}` | Admin | |
| POST | `/api/v1/blackbox/backfill` | Admin | job id |

Implementation pattern: copy `GET /api/v1/missions/{id}/landing-preview` (`MissionRoutes.cs:582`): authorize, read
scoped to the tenant, OpenAPI metadata through `.WithTag/.WithSummary/.WithParameter/.WithResponse(OpenApiJson.For<T>)
/.WithSecurity`. New routes live in `src/Armada.Server/Routes/BlackBoxRoutes.cs` (NEW), registered in `ArmadaServer`
next to `MissionRoutes` (`ArmadaServer.cs:1541`), with entries in `RouteAuthorizationRegistry.cs`. Errors use
`RouteErrorMapper` (404 `BlackBoxNotFound`, 409 `BlackBoxDeleting`, 410 `BlackBoxObjectMissing`, 400
`CollectionLimitExceeded`, 503 `BlackBoxStoreUnavailable`).

`mission_status`, `GET /api/v1/missions/{id}`, `voyage_status` and the voyage read gain `LastBlackBoxEntryId`, so any
client discovers an entry without knowing the routes.

### 8.2 MCP tools (`src/Armada.Server/Mcp/Tools/McpBlackBoxTools.cs`, NEW)

Read tools (read-only; listed in `AskToolPolicy.cs`):

| Tool | Arguments | Returns |
|---|---|---|
| `get_blackbox` | `missionId` \| `voyageId` \| `entryId`; `format` = `summary` (default) \| `brief` \| `json`; `audience` | `summary`: identity, where, how, status before/after, why, top recovery paths (about 2 KB). `brief`: the Markdown brief. `json`: the record. |
| `read_blackbox_section` | `entryId`, `section` | one record section |
| `list_blackbox_objects` | `entryId`, paging | object names, types, sizes |
| `read_blackbox_object` | `entryId`, `name`, `offset`, `length` (max 64 KB per call) | a chunk plus `nextOffset` and `totalBytes`, so agents page through large logs without flooding context |
| `list_blackbox_entries` | paging plus the REST filters | entry summaries |
| `search_blackbox` | `query`, filters, paging | matching summaries |
| `list_blackbox_notes` | `entryId`, paging | notes |
| `get_blackbox_bundle` | filters, `max` (50) | maintainer bundle |
| `list_blackbox_hotspots`, `list_blackbox_fingerprints` | `vesselId`, paging | |
| `get_blackbox_retention`, `get_blackbox_storage` | | policy and storage status |

`blackbox` is also added to the `enumerate` tool's entity types.

Write tools (normal authorization; not Ask read-only):

| Tool | Purpose |
|---|---|
| `annotate_blackbox` | add a note (`authorKind=Agent` for MCP callers) |
| `resolve_blackbox` | set resolution, note and link |
| `run_blackbox_recovery` | `entryId`, `pathId`, `dryRun` (default true), `confirmToken` |
| `apply_blackbox_recommendation` | applies one configuration recommendation's `applyAction` (for example an `update_vessel` patch), with the same dry-run and confirm-token flow |
| `pin_blackbox`, `delete_blackbox_entries`, `purge_blackbox`, `run_blackbox_gc`, `reanalyze_blackbox` | as in section 7.3 |

Registration: `McpToolRegistrar.RegisterAll` (`McpToolRegistrar.cs:130`) calls `McpBlackBoxTools.Register(register,
...)`, where `register` is `RegisterAdaptedTool` (`ArmadaServer.cs:1928`), so authorization, the Ask gate and
`CollectionLimitExceededException` mapping apply automatically. Add every tool to `McpToolAuthorizationRegistry.cs`
(undeclared tools fail closed to AdminOnly) and argument classes under `src/Armada.Server/Mcp/`.

**MCP resources and prompts.** Armada registers only tools today, on Voltaic 2.4.0. Phase 4 checks whether Voltaic
supports `resources/*` and `prompts/*`. If it does, expose:

- Resources `armada://blackbox/{entryId}/brief.md`, `armada://blackbox/{entryId}/record.json`,
  `armada://blackbox/{entryId}/objects/{name}` and `armada://missions/{missionId}/blackbox`.
- Prompts `review_blackbox(missionId, goal)` and `explain_blackbox(missionId)`, which expand to the instructions in
  section 9.

If it does not, the tools above cover the same ground; add the capability to Voltaic later.

### 8.3 WebSocket

- Broadcast `blackbox.captured`, `blackbox.analyzed`, `blackbox.resolved` and `blackbox.deleted` with typed payloads,
  so the dashboard and TUI update live.
- Commands `blackbox.get`, `blackbox.brief` and `blackbox.list` in `WebSocketSurface.cs`, mirroring the read tools.

### 8.4 Clients and SDKs

| Client | File | Methods |
|---|---|---|
| .NET client (`Armada.Client`, NuGet) | `src/Armada.Client/ArmadaClient.BlackBox.cs` (NEW), models in `src/Armada.Client/Models/BlackBox*.cs` | `GetMissionBlackBoxAsync`, `GetMissionBlackBoxBriefAsync`, `EnumerateMissionBlackBoxEntriesAsync`, `GetVoyageBlackBoxAsync`, `GetVoyageBlackBoxBriefAsync`, `GetBlackBoxAsync`, `GetBlackBoxBriefAsync`, `GetBlackBoxSectionAsync`, `EnumerateBlackBoxObjectsAsync`, `ReadBlackBoxObjectAsync` (stream, range), `DownloadBlackBoxAsync`, `EnumerateBlackBoxAsync`, `SearchBlackBoxAsync`, `AddBlackBoxNoteAsync`, `EnumerateBlackBoxNotesAsync`, `ResolveBlackBoxAsync`, `RunBlackBoxRecoveryAsync`, `ReanalyzeBlackBoxAsync`, `GetBlackBoxBundleAsync`, `EnumerateBlackBoxHotspotsAsync`, `EnumerateBlackBoxFingerprintsAsync`, `PinBlackBoxAsync`, `UnpinBlackBoxAsync`, `DeleteBlackBoxAsync`, `DeleteBlackBoxEntriesAsync`, `PurgeBlackBoxAsync`, `RunBlackBoxGcAsync`, `GetBlackBoxRetentionAsync`, `GetBlackBoxStorageAsync`, `MigrateBlackBoxStorageAsync`, `BackfillBlackBoxAsync` |
| Legacy core client | `src/Armada.Core/Client/ArmadaApiClient.cs` | the read methods plus resolve and recovery |
| Dashboard TypeScript client | `src/Armada.Dashboard/src/api/client.ts` and `types` | same set, camelCase |
| Mobile | `src/Armada.Mobile` API layer | read, brief, resolve, recovery |
| VS Code extension | `src/Armada.VSCode/src/api/blackbox.ts` (NEW, section 11.4) | read, brief, objects, list, resolve, notes, recovery |

The surface suites enforce parity: `ClientRouteSurfaceSuite` (every route has a client method), `ClientCoverageSuite`
(every client method has a contract test), `ApiContractSuite` against the regenerated API surface. No SDK for another
language lives in this repository; if out-of-repository SDKs exist, they get the same method set from the OpenAPI
document.

## 9. MCP consumption: "review the black box for mission X and take action"

The target flow for any MCP-connected agent (Claude Code, Codex, an Armada captain, Ask Armada):

```
User:  Review the black box for mission msn_abc and fix it.
Agent: get_blackbox {missionId: "msn_abc", format: "summary"}
       -> where, how, why, triage UserConfig (High), recovery paths [rp1 retry after stash, rp2 switch to PullRequest]
Agent: read_blackbox_section {entryId, section: "Workspace"}       (dirty paths, checkout)
Agent: read_blackbox_object {entryId, name: "evidence/git-status.txt"}
Agent: run_blackbox_recovery {entryId, pathId: "rp2", dryRun: true}
       -> "Would call update_vessel {landingMode: PullRequest} then retry_landing {missionId}"; confirmToken
Agent: (asks the user, or proceeds if told to act) run_blackbox_recovery {entryId, pathId: "rp2", confirmToken}
       -> per-step results; the entry is annotated automatically with what was run and the outcome
Agent: resolve_blackbox {entryId, resolution: "Resolved", note: "...", link: "msn_abc landed at <sha>"}
```

Supported `{take action}` goals and how each maps:

| Goal | Tools |
|---|---|
| Explain it | `get_blackbox` (summary or brief), sections, objects |
| Fix it / recover it | `run_blackbox_recovery` with a ranked path; dry run, then confirm |
| Prevent it | `apply_blackbox_recommendation` (configuration patch, dry run, then confirm) |
| Fix Armada | `get_blackbox {audience: maintainer}`, then dispatch a mission on the Armada vessel with the brief (13.2) |
| Compare | `list_blackbox_entries {fingerprint}`, `search_blackbox` |
| Clean up | `resolve_blackbox`, `pin_blackbox`, `delete_blackbox_entries` |

Safety:

- `run_blackbox_recovery` and `apply_blackbox_recommendation` run only the action recorded in the entry. They never
  take free-form commands.
- Each step is authorized as if the caller had called that tool directly.
- A dry run is required first; the confirm token binds the entry, the path and the current mission version, so a
  mission that changed since the dry run must be re-checked.
- Destructive steps (deleting a branch, forcing a dock reclaim) are marked `dataLossRisk=true`. The tool refuses
  without `acknowledgeDataLoss: true`.
- Every run is annotated on the entry (`blackbox_notes`, `authorKind=Agent`) and emits `blackbox.recovery_run`.

The `review_blackbox` MCP prompt (if Voltaic supports prompts), and the tool descriptions, carry these instructions:

> Read the black box summary first. Quote evidence, not guesses. State the triage verdict and whether you agree. If
> you act, use only recovery paths or recommendations from the entry, do a dry run, tell the user what will happen,
> and confirm before running anything marked dataLossRisk. Annotate the entry with what you did.

## 10. Ask Armada

Goal: in Ask Armada, a user types *"show me the pertinent details from the black box for mission msn_x and give me
your opinion on why it failed and what could have been done differently"* and gets a grounded answer with clearly
separated evidence and opinion, plus one-click follow-ups.

### 10.1 Tools and policy

- Add every black box read tool from 8.2 to the Ask read-only allowlist in `src/Armada.Core/Services/Ask/AskToolPolicy.cs`
  ("Ask Armada readers" block, around line 89), so the thread captain calls them without approval.
- Write tools (`run_blackbox_recovery`, `apply_blackbox_recommendation`, `resolve_blackbox`, `annotate_blackbox`)
  stay behind the Ask gate. Ask turns them into proposals the user approves (`approve_ask_proposal` /
  `reject_ask_proposal`), as it does for other write tools today.

### 10.2 Prompt guidance

Add a "Black box" section to the Ask Armada system prompt template (built-in templates in
`src/Armada.Core/Services/PromptTemplateService.cs`; also editable as a prompt template). It tells the captain:

1. When the user names a failed mission or voyage, or asks why something failed, call `get_blackbox` (summary) first.
   Then read only the sections the question needs (`Conflict`, `Workspace`, `Analysis`), and page evidence objects
   with `read_blackbox_object` instead of loading them whole.
2. Answer in this shape:
   - **What happened** (pertinent details: mission, where, how, status before and after, conflicting missions,
     files and directories), each line from the entry.
   - **Why** (the entry's findings with rule ids and confidence).
   - **My opinion** (labeled as opinion): whether the captain agrees with the triage, what it thinks the real root
     cause is, and what evidence would change its mind.
   - **What could have been done differently**: configuration, mission brief, dependency ordering, and Armada
     behavior, separating what the user can change from what Armada should change.
   - **Next steps**: the ranked recovery paths as buttons (proposals), plus "Copy FIX THIS".
3. Never invent files, commits or missions that are not in the entry. If a fact is missing (for example
   `CaptureMode=Sweeper`), say so.
4. Offer to save the opinion as a note on the entry (`annotate_blackbox`, `authorKind=AskArmada`) through a proposal.

### 10.3 Quick action, cards and entry points

- **Slash command** `/blackbox` in `src/Armada.Core/Services/Ask/AskQuickActionCatalog.cs`: argument `missionId`
  (type `mission`, required, filtered to missions with an entry) and an optional `question` (text), mapping to
  `get_blackbox`. The catalog has 10 actions today (bounded and code-fixed: update its `[CollectionBound]` and the
  `_CodeFixedResponses` entries for `GET .../ask/quick-actions` and MCP `list_ask_quick_actions` in
  `ResponseShapeSuite.cs`).
- **Ask card.** A black box card (`AskWorkResultBuilder` / `AskWorkSnapshotBuilder` pattern) renders the summary with
  status chips, conflicting missions and recovery buttons. Mission ids in a thread that have an entry link to it
  automatically (`AskWorkLinker`).
- **Entry points into Ask.** "Ask Armada about this failure" on the dashboard black box panel, the Inbox failure rows,
  the TUI Failure tab and the VS Code entry view. Each opens a new thread pre-filled with
  "Show me the pertinent details from the black box for mission {id} and give me your opinion on why it failed and
  what could have been done differently."
- **Follow-ups** offered after the answer: "Fix it", "Apply the configuration change", "Draft an Armada fix
  mission", "Compare with similar failures".

### 10.4 Ask tests

See section 16.8.

## 11. User interfaces

### 11.1 Dashboard (`src/Armada.Dashboard/src`)

- `components/blackbox/BlackBoxPanel.tsx` (NEW) on `pages/MissionDetail.tsx` (replaces the bare failure `<pre>` at
  lines 773-797 and keeps the existing Retry Landing action) and on `pages/VoyageDetail.tsx`. Sections follow the
  brief (Summary, Where, How, Why, Conflict, Configuration, What would have prevented it, What Armada could change,
  Recovery, Attempts, Evidence, Notes). It has:
  - **Copy FIX THIS** (`components/shared/CopyButton.tsx`), **Download .md**, **Download .zip**, **View JSON**
    (`JsonViewer.tsx`).
  - An audience toggle (User, Agent, Maintainer).
  - Recovery buttons that dry run, show what will happen, and confirm.
  - **Ask Armada about this failure**, Resolve, Pin, Add note.
  - Links to conflicting missions, the dock, the merge entry, the incident and similar entries.
  - Attempt switcher (attempt 1, 2, ...).
- `pages/BlackBox.tsx` (NEW, nav under Operations): paged list with every filter in 8.1 and saved filters, tabs
  **Entries**, **Fingerprints**, **Hotspots** (per-vessel directory table, later a treemap) and **Retention**
  (policy, usage by target, last and next GC run, a dry-run GC preview, purge by filter for admins).
- `pages/BlackBoxEntry.tsx` (NEW): full-page view for `/blackbox/{id}`, including object viewers with range loading
  for large logs.
- Server settings page: Black Box storage (target, credentials masked, health check, migrate) and retention.
- `Inbox.tsx` failure rows: Copy FIX THIS and Ask Armada actions; `Storage` page: black box usage row.
- Live updates from the WebSocket events in 8.3.
- Every string in every locale (`wwwroot/i18n/armada.json`).

### 11.2 TUI (`src/Armada.Tui`)

- `Screens/Operations/MissionScreen.cs` and the voyage screen: a **Failure** tab showing the user audience, with
  keys shown in the focus hints (focus-dependent keys must be discoverable): `c` copies the brief (OSC 52),
  `x` exports it to a file, `r` lists recovery paths, `a` opens Ask Armada, `j` shows JSON.
- `Screens/Operations/BlackBoxScreen.cs` (NEW): list with filters and fingerprints; entry detail with paged object
  viewer.
- Admin screen sections for retention and storage.
- Regenerate the TUI parity manifest.

### 11.3 Helm CLI (`src/Armada.Helm`)

```
armada blackbox show <missionId|voyageId|entryId> [--brief] [--audience agent|user|maintainer] [--json]
armada blackbox list [--vessel] [--kind] [--triage] [--since] [--page] [--page-size]
armada blackbox objects <entryId>
armada blackbox cat <entryId> <name> [--offset] [--length]
armada blackbox download <entryId> [-o file.zip]
armada blackbox recover <entryId> <pathId> [--execute]
armada blackbox resolve <entryId> --resolution Resolved [--note] [--link]
armada blackbox note <entryId> "<text>"
armada blackbox pin|unpin|delete <entryId...>
armada blackbox purge [filters] [--execute]
armada blackbox gc [--dry-run]
armada blackbox retention
armada blackbox storage [status|migrate --to <target>]
armada blackbox bundle [filters] [-o bundle.md]
armada blackbox backfill [--since 30d]
armada mission failure <id>        (alias of blackbox show for a mission)
armada voyage failure <id>
```

`armada blackbox show msn_x --brief | pbcopy` is the terminal path to "paste into an agent and say FIX THIS".

### 11.4 VS Code extension (NEW project)

There is no VS Code extension in the repository today. This plan adds a small one, `src/Armada.VSCode` (TypeScript,
`vsce` packaging), scoped to the black box first, so it can grow later.

- **Connection:** settings `armada.url` and `armada.tenant`; the API key is kept in VS Code `SecretStorage`.
  Commands `Armada: Connect` and `Armada: Disconnect`.
- **Tree view "Armada Failures"** in the Explorer: recent entries grouped by vessel, with kind, step and age; filter
  to the vessel whose repository is open (matched by remote URL); badge count of open entries.
- **Commands:**
  - `Armada: Open Black Box for Mission...` (quick pick of recent failures, or paste an id)
  - `Armada: Copy FIX THIS`
  - `Armada: Open FIX THIS in Editor` (Markdown preview; save as `.md`)
  - `Armada: Send FIX THIS to Chat` (opens the editor chat with the brief attached as context, where the host
    supports it; otherwise copies)
  - `Armada: Run Recovery Path...` (dry run, then a confirmation dialog)
  - `Armada: Resolve Black Box Entry`
- **File decorations:** for the open repository, files listed as conflicted in open entries get a decoration and
  hover text ("conflicted in msn_x landing, 2026-10-28"); a hotspot decoration marks the vessel's top directories.
- **Live updates** via the WebSocket events.
- **Build and release:** a `build-vscode.sh`/`.bat` script, publisher entry in `publisher.json`, a VSIX artifact in
  `build-all.sh`. Unit tests with `@vscode/test-electron` for the API layer and commands.

### 11.5 Mobile (`src/Armada.Mobile`)

Mission and voyage detail show the user audience summary, recovery buttons (with confirmation), Resolve, and Share
(the brief through the platform share sheet).

### 11.6 Notifications

Existing notification and webhook paths include the entry id, kind, step and a brief URL in failure notifications.

## 12. Security and privacy

- **Redaction** (`BlackBoxRedactor`, NEW) runs on every string and object before storage and again before rendering:
  model endpoint API keys, `Authorization` headers, tokens matching common patterns (GitHub, OpenAI, Anthropic, AWS,
  Azure, Google), `password=`/`secret=`/`token=` pairs, URLs with embedded credentials, private key blocks, and any
  setting value marked secret. It records rule names and counts, never values.
- Entries are tenant-scoped. Reading an entry requires read access to its mission or voyage (or `TenantAdmin` when
  the mission was purged). Notes and resolve need write access; reanalyze needs `TenantAdmin`; purge, GC, backfill,
  storage settings and migration need Admin.
- External-store credentials are secret settings: masked on read, write-only through the settings API, never in
  entries, logs or briefs.
- The `user` and `agent` audiences include absolute paths and host names (they are the operator's own machines).
  Anything that leaves the installation (for example a maintainer bundle shared outside it) can be rendered with
  `redactHosts=true`, which strips absolute paths, host names and user names.
- Object reads verify SHA-256; a mismatch returns 409 `BlackBoxObjectCorrupt` and marks the object.
- The disk target refuses a directory outside the configured root and refuses symlinked object paths.

## 13. Extensions (ideation)

Each extension is independent.

### 13.1 Prevention

1. **Overlap warning at dispatch.** Compare a new mission's likely files (scoped files, paths named in the brief,
   `GitAnchorsSnapshot.RecentPathCommits`) with active missions and with the vessel's hotspots. Warn in dispatch
   results, the dashboard dispatch dialog and MCP `dispatch`; with `Vessel.SerializeOverlappingMissions=true` (NEW),
   chain them with `DependsOnMissionId` automatically.
2. **Soft directory reservations** for hotspot directories.
3. **Brief linter**: flag "after mission N lands" without a dependency, verification-only briefs (NoOp risk), and
   paths outside the vessel.
4. **Configuration doctor**: a per-vessel view of recommendations that recur across entries ("BBX-LAND-001 fired 6
   times this month on vessel Armada").

### 13.2 Maintainer loop

5. **Maintainer bundle**: `get_blackbox_bundle` renders up to 50 entries grouped by fingerprint (count, first and last
   seen, Armada versions affected, one full brief for the newest, one-liners for the rest), with a preamble asking
   the agent to rank Armada improvements and write each as an Armada mission brief.
7. **Self-improvement mission**: dispatch a mission on the Armada vessel from the bundle, with operator approval.
8. **Regression fixtures**: `armada blackbox export-fixture <id>` writes a sanitized record into
   `src/Test.Shared/Fixtures/BlackBox/`; a suite checks that the advisor still produces the expected findings for
   every fixture.
9. **Fingerprint trends** per Armada version, to see whether a release fixed a failure mode.
10. **Rule authoring loop**: when an agent's analysis proposes a new rule, it writes it in rule-catalog format.

### 13.3 Deeper forensics

11. **Attempt diff**: what changed between two attempts (configuration, captain, model, base commit, files).
12. **Success flight recorder (lightweight)**: keep minimal records of successful landings for 7 days to compare
    failures with nearby successes.
14. **Combined timeline** across mission, dock, merge entry, captain and Harbor link.
15. **Voyage cascade graph**: root failure, cancelled dependents and blocked siblings, each linked to its entry.

### 13.4 Operations

16. **Runbook linkage**: recovery paths that map to a runbook start it (`start_runbook_execution`).
17. **Recovery outcome tracking**: record which path was taken and whether it worked; use it to rank paths.
18. **Prometheus metrics**: `armada_blackbox_entries_total{phase,step,kind,triage}`, `armada_blackbox_bytes{target}`,
    `armada_blackbox_gc_deleted_total`, `armada_blackbox_capture_failures_total`.

## 14. Documentation, README, CHANGELOG and Postman for v1.2.0

### 14.1 Release placement

`CHANGELOG.md` has `## Unreleased` above `## v1.2.0 (2026-10-27)`, and v1.2.0 images were built and pushed from
`bd2e7c1ac` on 2026-10-29. To ship the black box as a v1.2.0 feature:

- Fold the black box (and the rest of "Unreleased") into the v1.2.0 section, update its date, and rebuild and push
  the images with `build-all.sh v1.2.0` after the feature lands. Docker tags `v1.2.0` and `latest` are overwritten.
- Alternatively ship it as v1.2.1 or v1.3.0. This is a decision for the release owner (section 17).

### 14.2 CHANGELOG

A lead section at the top of the v1.2.0 notes:

```
### Mission and voyage black box

Every mission or voyage failure, of any kind and including landing failures, now writes a black box entry at the
moment of failure: where it failed (phase, step, host, code site), how (exception, exit code, failing command, git
error), status before and after, why (classified cause with rule ids and confidence), conflicting missions with the
files and directories they collided on, the configuration in effect and what would have prevented the failure, Armada
behavior changes that could address it, and ranked recovery paths with exact commands. Each entry renders a FIX THIS
brief to paste into any coding agent. Entries are stored in the database by default (BLOBs indexed by mission,
voyage, vessel, captain and other ids), or on disk, S3 and S3-compatible stores, Azure Blob, Google Cloud Storage,
CIFS or NFS, with configurable retention and garbage collection. Available in the dashboard, TUI, Helm CLI, the new
VS Code extension, the mobile app, REST, MCP and Ask Armada. Schema migration 103 applies at startup.
```

followed by bullets for: the REST routes and MCP tools; `Armada.Client` methods; `blackBox` settings (storage,
retention, sweeper); Ask Armada `/blackbox`; the VS Code extension; the Phase 0 fixes (each its own bullet); and
breaking changes, if any (none planned: new fields are additive).

The v1.2.0 introductory paragraph gains a sentence naming the black box as a headline feature.

### 14.3 README

- Add the black box to the feature list near the top, as a first-class feature.
- New section **"Black box: why did it fail, and what now?"**: what an entry contains, a screenshot of the dashboard
  panel (light and dark, captured for review, not committed until approved), the three ways to get a brief
  (dashboard Copy FIX THIS, `armada blackbox show <id> --brief | pbcopy`, MCP `get_blackbox`), the Ask Armada prompt
  example, storage targets and retention in one short table, and a link to `docs/BLACKBOX.md`.
- Update the MCP tools summary and the CLI command summary.

### 14.4 Other documentation

- `docs/BLACKBOX_SCHEMA.md` (NEW): the consumer reference for the black box. Every object in an entry (record.json,
  brief*.md, evidence/*, manifest.json in downloads), every BlackBoxRecord section and field with type, nullability,
  bounds (max length, [CollectionBound] limits), enum values, units, and examples; the REST/MCP shapes (BlackBoxEntry,
  BlackBoxEntrySummary, BlackBoxObjectSummary, chunk reads); event payloads; the versioning and compatibility policy.
  `docs/blackbox-record.schema.json` (NEW): JSON Schema (draft 2020-12) for record.json, generated from the C# models,
  plus schemas for the entry and summary shapes.
- `docs/BLACKBOX.md` (NEW): concepts, the record schema with every section, the phase and step taxonomy, the rule
  catalog and how to add a rule, storage targets and configuration examples for each target, retention and GC,
  security and redaction, MCP and Ask usage patterns, troubleshooting.
- `docs/REST_API.md`, `docs/MCP_API.md`, `docs/WEBSOCKET_API.md`: every new route, tool, event and command, with
  examples.
- `docs/ENUMERATION.md`: checklist rows for each new collection.
- `docs/UPGRADING.md`: migration 103, new settings and defaults, storage choices, backfill.
- `docs/OPERATIONS.md`: black box in the recovery section; retention and storage operations.
- `docs/HARBOR.md`: how Harbor-hosted evidence is collected.
- `docs/api-surface-1.0.json` and `docs/API_SURFACE_1.0.md`, regenerated with `scripts/common/generate-api-surface.sh`.
- OpenAPI, generated from the route metadata.
- `GETTING_STARTED.md`: one paragraph on finding out why a mission failed.

### 14.5 Postman

- `Armada.postman_collection.json`: a **Black Box** folder with one request per route in 8.1 (and the retention,
  storage and GC routes), using collection variables `missionId`, `voyageId`, `blackboxEntryId`, `blackboxObjectName`,
  `blackboxPathId`, `blackboxConfirmToken`. Test scripts check status codes, response shape (`EnumerationResult`
  fields, `LastBlackBoxEntryId`), `Content-Type` of brief and object reads, and that a recovery run without a confirm
  token is refused. The dry-run request stores `confirmToken` for the next request.
- Saved example responses for each request (captured, redacted entries), so the collection's mock works.
- `postman/mocks/blackbox/` (NEW): a mock built from those examples, following `postman/mocks/model-providers`, for
  dashboard and VS Code extension development without a live Admiral.
- `postman/README.md`: how to run the Black Box folder against a local Admiral.

## 15. Implementation phases

Each phase ships on its own and leaves the repository green.

### Phase 0: evidence fixes (prerequisite)

1. `GitService.FastForwardVerifiedCommitAsync` (`GitService.cs:101`): pass real porcelain to
   `GitDirtyCheckoutException`.
2. `LandingService.RetryLandingAsync`: list conflicts from the merge worktree.
3. `MergeQueueService.MergeBranchAsync` returns conflicted files; `ReconcileMissionStatusAsync` classifies by step.
4. Record the start commit at every provision; add `Mission.StartCommit`.
5. `mission.landing_target_advanced` payload `{Before, After}`.
6. A kind on every failure write.
7. Remove or wire `MaxLandingRetries`.
8. CHANGELOG bullets.

### Phase 1: core entry, Database storage, capture, brief (MVP)

1. Enums: `BlackBoxPhaseEnum`, `BlackBoxStepEnum`, `BlackBoxSubjectTypeEnum`, `BlackBoxFileRoleEnum`,
   `BlackBoxLinkRelationEnum`, `BlackBoxTriageEnum`, `BlackBoxResolutionEnum`, `BlackBoxStateEnum`,
   `BlackBoxCaptureModeEnum`, `BlackBoxStorageTargetEnum`.
2. Models (4.3), all collections bounded, XML docs on public members.
3. Migration 103 in `Database/Sqlite/Queries/TableQueries.cs`, `Database/Postgresql/Queries/TableQueries.cs`,
   `Database/SqlServer/Queries/TableQueries.cs` and `Database/Mysql/MysqlDatabaseDriver.cs`: the five tables, indexes,
   BLOB column types per provider, mission and voyage columns.
4. `IBlackBoxEntryMethods`, `IBlackBoxObjectMethods`, `IBlackBoxFileMethods`, `IBlackBoxLinkMethods`,
   `IBlackBoxNoteMethods`, with four implementations each (template: `StageOutcomeMethods`), wired in
   `DatabaseDriver.cs`; paging in SQL.
5. `IBlackBoxStore` and `DatabaseBlackBoxStore`; gzip; SHA-256.
6. `BlackBoxSettings` (storage target Database only in this phase), `BlackBoxLimits`, `BlackBoxRedactor`.
7. `BlackBoxCapture`; wire every site in 5.1.
8. `BlackBoxSweeper` and backfill.
9. `BlackBoxBriefRenderer` (agent audience) from captured data.
10. REST: mission and voyage entry and brief, entry by id, enumerate, objects, object read. MCP: `get_blackbox`,
    `read_blackbox_section`, `list_blackbox_objects`, `read_blackbox_object`, `list_blackbox_entries`; add them to
    `AskToolPolicy`.
11. `Armada.Client` and `ArmadaApiClient` read methods.
12. Dashboard `BlackBoxPanel` with Copy FIX THIS on mission and voyage pages.
13. Helm `armada blackbox show <id> --brief`.
14. Basic retention: `retentionDays`, `keepLatestPerMission`, expiry GC on the Database target.
15. Schema: `docs/BLACKBOX_SCHEMA.md` and `docs/blackbox-record.schema.json`, plus a test that regenerates the schema
   from the models and fails on drift (like the API surface check).

End of Phase 1: every failure produces a pasteable FIX THIS brief, stored in the database, readable everywhere that
matters, and expired automatically.

### Phase 2: git forensics and conflict attribution

1. Extract `VesselGitInspector` from `VesselCleanupService`; its tests stay green.
2. `IGitService.GetMergeBaseAsync`, `GetCommitRangeAsync`; Harbor protocol twins.
3. `BlackBoxEnricher` steps 1 to 5 and 7; `blackbox_files` and `blackbox_links` rows.
4. Brief sections Conflict and Workspace; evidence objects.

### Phase 3: advisor, recovery actions, MCP actions, Ask Armada

1. `BlackBoxRuleCatalog`, `BlackBoxAdvisor`, triage, fingerprint, similar entries.
2. `BlackBoxConfigurationResolver` (NEW): effective configuration with sources, sharing the precedence logic used in
   `MissionLandingHandler.cs:183`.
3. Recovery paths with machine-readable actions; `run_blackbox_recovery`, `apply_blackbox_recommendation`, dry run and
   confirm tokens; notes, resolve, pin; REST twins.
4. `user` and `maintainer` audiences.
5. Ask Armada: prompt guidance, `/blackbox` quick action, card, entry points, proposal flow for write tools.
6. `reanalyze` and the rules-version recompute job.

### Phase 4: storage targets, full retention and GC, surfaces, release docs

1. `BlobjectBlackBoxStore` for Disk, S3 (and S3-compatible), Azure Blob, Google Cloud, CIFS, NFS; storage settings,
   secrets masking, fallback, health check (doctor), storage inventory, storage migration job.
2. Full retention policy and `BlackBoxGarbageCollector` (quotas, cascades, two-phase delete, orphan sweep, dry run,
   reports); manual purge, delete, pin.
3. Dashboard `BlackBox` list page (Entries, Fingerprints, Hotspots, Retention), entry page, settings, Inbox and
   Storage page additions, i18n.
4. TUI Failure tab, list screen, admin sections; parity manifest.
5. Helm: the full command set in 11.3.
6. Mobile.
7. WebSocket events and commands.
8. MCP resources and prompts, if Voltaic supports them.
9. Docs, README, CHANGELOG, Postman (collection, examples, mock), OpenAPI, API surface, ENUMERATION checklist.

### Phase 5: VS Code extension

1. `src/Armada.VSCode` scaffold, connection and SecretStorage, API layer.
2. Tree view, commands, Markdown preview, chat hand-off, recovery with confirmation.
3. File decorations and live updates.
4. Build scripts, publisher entry, VSIX in `build-all.sh`; tests.

### Phase 6: maintainer loop and extensions

Maintainer bundle, regression fixture export and suite, fingerprint trends, optional model summary; then the prevention extensions, starting with the overlap warning at dispatch.

## 16. Test plan (positive and negative)

All .NET suites live in `src/Test.Shared` and run through `Test.Automated`, `Test.Xunit` and `Test.Nunit` on net8.0
and net10.0. Dashboard tests use vitest, mobile jest, VS Code `@vscode/test-electron`. P = positive, N = negative.

### 16.1 Capture (`Services.BlackBoxCapture`)

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Each step in 5.1 (one case per step, driven from a table) | Entry with the right phase, step, kind, status before and after, and site file and line |
| 2 | P | Mission fails, restarts, fails again | Attempt 2 is current, attempt 1 kept, `supersedes_id` set |
| 3 | P | `ResetForRestart` clears the mission's `FailureReason` | Entry unchanged; `LastBlackBoxEntryId` still set |
| 4 | P | Landing conflict with `GitMergeConflictException` | Conflicted files recorded, `blackbox_files` rows with role `Conflicted` |
| 5 | P | Dirty checkout | Dirty paths equal the porcelain paths (Phase 0 fix) |
| 6 | P | Voyage fails because one mission failed | Voyage entry with roll-up and cascade root pointing at the mission entry |
| 7 | P | Brief rendered at capture before enrichment | `brief.md` exists and says enrichment is pending |
| 8 | N | Store throws on write (Database target) | Failure handling completes, mission still `Failed`, `blackbox.capture_failed` emitted, sweeper captures later |
| 9 | N | Capture exceeds 2-second budget | Partial entry written, Truncation notes the timeout |
| 10 | N | `blackBox.enabled=false` | No entries, no errors, sweeper idle |
| 11 | N | Operator stop (`OperatorAction`) | Entry with triage `External` (per open question 1), excluded from bundles |
| 12 | N | Reason containing an API key, a token URL and a private key block | Stored text redacted; Redaction lists rules and counts; the values appear nowhere in the objects |
| 13 | N | Reason of 1 MB | Stored capped at 8000 chars with Truncation entry |
| 14 | N | 10,000 conflicted files | 500 stored, `TotalCount=10000`, flagged truncated |
| 15 | N | Two capture calls for the same failure concurrently | Exactly one current entry |

### 16.2 Sweeper and backfill

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Failed mission without an entry | Captured with `CaptureMode=Sweeper` |
| 2 | P | Backfill over historical missions | One entry each, `CaptureMode=Backfill`, idempotent on rerun |
| 3 | N | Mission already has a current entry | Not captured again |
| 4 | N | `EnrichmentFailed` five times | Stops retrying, stays visible with the error |

### 16.3 Enrichment and advisor

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Conflict with a landed Armada mission (temp repo, trailer present) | Conflicting mission, commit and overlapping files identified; link row `LandedOnTargetSinceStart` |
| 2 | P | Conflict with a non-Armada commit | Reported as outside Armada with author |
| 3 | P | Conflicts in three files under one directory | Directory rollup shows that directory with count 3 |
| 4 | P | Harbor-hosted dock (stand-in Harbor host) | Workspace facts from the Harbor |
| 5 | P | Each starter rule (golden files) | Expected findings, recommendations, improvements, recovery paths, triage |
| 6 | P | Same failure twice | Same fingerprint; second lists the first as similar |
| 7 | N | Disconnected Harbor | Workspace marked unknown, rule BBX-INFRA-001 fires, no exception |
| 8 | N | Start commit missing | Target movement omitted with a reason; BBX-SCOPE-002 fires |
| 9 | N | Branch deleted | Workspace says so; recovery offers restore from `missionCommit` |
| 10 | N | Empty record (sweeper, nothing known) | No rule fires except Unknown triage; brief says what is missing |
| 11 | N | Rule placeholder references a missing field | Rule skipped, logged, no broken text |

### 16.4 Storage targets

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Database target on SQLite, PostgreSQL, SQL Server, MySQL | Write, read (whole and ranged), delete; SHA-256 verified (`Database.*` parity suites) |
| 2 | P | Disk target | Objects under the configured directory with the key layout |
| 3 | P | S3 target against Less3 or MinIO in Docker, Azure against Azurite, GCS against the fake-gcs emulator | Same contract suite as Database (shared `BlackBoxStoreContractSuite`) |
| 4 | P | CIFS and NFS against the in-process OpenCIFS and OpenNFS servers | Same contract suite |
| 5 | P | Change target from Database to Disk | New entries on Disk; old entries still readable from Database |
| 6 | P | Storage migration Database to S3 | All objects copied and verified, entries switched, source deleted; resume after interruption |
| 7 | N | External store unreachable, `fallbackToDatabase=true` | Entry written to Database, `blackbox.storage_fallback` emitted |
| 8 | N | External store unreachable, fallback off | Index and `record.json` in Database, evidence objects `ObjectMissing` |
| 9 | N | Object tampered (bytes changed) | Read returns 409 `BlackBoxObjectCorrupt` |
| 10 | N | Object deleted out-of-band | Read returns 410; GC marks `ObjectMissing` |
| 11 | N | Disk directory outside root or symlinked key | Refused |
| 12 | N | Invalid settings (no bucket, bad endpoint, unknown target) | Settings validation error; doctor check fails with the reason |
| 13 | N | Credentials in settings read | Masked |
| 14 | N | Object larger than `maxEvidenceObjectBytes` | Head and tail kept, Truncation set |

### 16.5 Retention and garbage collection

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Entry older than `retentionDays` | Deleted with all objects and child rows; `blackbox.gc_completed` counts it |
| 2 | P | Resolved entry older than `resolvedRetentionDays` | Deleted |
| 3 | P | Over `maxEntriesPerVessel` | Oldest unpinned, non-current evicted |
| 4 | P | Over `maxTotalBytes` | Resolved first, then oldest |
| 5 | P | Vessel deleted with `deleteWithVessel=true` | Entries deleted |
| 6 | P | GC dry run | Reports what would be deleted, deletes nothing |
| 7 | P | Orphaned external object older than grace | Deleted |
| 8 | P | Policy change | `expires_utc` recomputed |
| 9 | N | Pinned entry past expiry | Kept |
| 10 | N | Current entry of an existing mission past expiry with `keepLatestPerMission` | Kept |
| 11 | N | `retentionDays=0` | Nothing expires by age |
| 12 | N | Store delete fails mid-run | Entry stays `Deleting`, hidden, retried next run |
| 13 | N | Orphan younger than grace | Kept |
| 14 | N | Mission purged with `deleteWithMission=false` | Entry kept, readable by TenantAdmin with denormalized title |
| 15 | N | Purge by filter without `--execute` | Dry run only |

### 16.6 REST and MCP

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Every route and tool, happy path | Documented shape; enumerate defaults to page size 10 |
| 2 | P | Brief `audience=user|agent|maintainer` | Different sections; user has no stack frames |
| 3 | P | `read_blackbox_object` paging through a 3 MB log | Chunks of at most 64 KB, `nextOffset` until end |
| 4 | P | `run_blackbox_recovery` dry run then confirm | Underlying tools called once; note added; `blackbox.recovery_run` emitted |
| 5 | P | `mission_status` includes `LastBlackBoxEntryId` | Present for failed missions |
| 6 | N | Unknown mission, mission without an entry, unknown entry | 404 `BlackBoxNotFound` (REST), MCP `NotFound` |
| 7 | N | Other tenant's entry | 404, not 403 (no existence leak); `McpTenantIsolationSuite` covers every tool |
| 8 | N | Page size 0 or 1001, 1001 ids to delete | 400 `CollectionLimitExceeded` / MCP `InvalidArgument` |
| 9 | N | Invalid section name, invalid audience, negative offset, length over 1 MB | 400 with field |
| 10 | N | Recovery without dry run, with expired token, with a token for another path, after the mission changed | Refused with a specific code |
| 11 | N | `dataLossRisk` path without `acknowledgeDataLoss` | Refused |
| 12 | N | Viewer calls purge, GC, storage migrate, reanalyze | 403; MCP authorization error |
| 13 | N | Entry in `Deleting` state | 409 `BlackBoxDeleting` |
| 14 | P/N | API contract, authorization registry, route surface, client coverage, collection surface and response shape suites | All pass with the new routes and models |

### 16.7 Clients, SDK and Postman

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Every `Armada.Client` method against a live test server | Contract tests pass (`ClientCoverageSuite`) |
| 2 | N | Client 404, 400, 409, 410, 503 handling | Typed exceptions with codes |
| 3 | P | Postman collection run (`postman collection run` or Newman) against a seeded Admiral | All Black Box tests pass |
| 4 | N | Postman recovery request without token | Test asserts refusal |

### 16.8 Ask Armada

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Policy: black box read tools are on the read-only allowlist | No approval prompt for reads |
| 2 | P | Scripted thread captain asks for the pertinent details and an opinion on msn_x | Answer has What happened, Why, My opinion (labeled), What could have been done differently, Next steps; every file, commit and mission named exists in the entry |
| 3 | P | `/blackbox` quick action | Calls `get_blackbox`, renders the card |
| 4 | P | "Fix it" follow-up | Creates a proposal; approval runs `run_blackbox_recovery` with dry run then confirm |
| 5 | N | Mission without an entry | Captain says so and offers to show `FailureReason` |
| 6 | N | Write tool called directly in Ask | Turned into a proposal, not executed |
| 7 | N | Captain output names a file not in the entry | Evaluation fixture flags it (grounding check in the Ask evaluation script) |

### 16.9 User interfaces

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Dashboard panel sections, copy, download, audience toggle, recovery dialog, Ask button, attempt switcher | vitest |
| 2 | P | Black Box page filters, fingerprints, hotspots, retention tab, live update on WebSocket event | vitest |
| 3 | N | Empty, loading, error, 404 and `ObjectMissing` states | Rendered correctly |
| 4 | P | TUI Failure tab, keys visible in focus hints, copy and export | TUI suites; parity manifest regenerated |
| 5 | P | Helm commands, including `--brief` output to stdout and non-zero exit on 404 | CLI tests |
| 6 | P | VS Code tree view, commands, decorations | `@vscode/test-electron` |
| 7 | N | VS Code without connection or with a bad key | Clear error, no crash |
| 8 | P | Mobile detail and share | jest |

### 16.10 Fixtures from real failures

Sanitized records built from:

- `msn_mvskboi0_vJGaqsNlGqU`: LandingFailed, dirty checkout with 52 staged paths. Expect BBX-LAND-001, triage
  `UserConfig`.
- Its parent `msn_mvsjeweh_tmCfX52gt3z`: dock directory deleted before landing. Expect BBX-LAND-002, `ArmadaDefect`.
- Garrison `msn_mvsqzal6_b1BfR4tnZ29`: false ScopeViolation with a blank entry. Expect BBX-SCOPE-001,
  `ArmadaDefect`.
- The bounded-enumeration voyage's missions 2 to 5: BLOCKED because dependent missions were dispatched in parallel
  (signals 2026-10-29T02:26Z). Expect BBX-DEP-001, `MissionBrief`.

These missions were deleted on 2026-10-29, so the fixtures are reconstructed from this plan's descriptions and the
conversation record, not exported.

### 16.11 Schema (`Models.BlackBoxSchema`)

| # | Type | Case | Expected |
|---|---|---|---|
| 1 | P | Schema generated from the models | Equals the committed `docs/blackbox-record.schema.json` |
| 2 | P | A captured record | Validates against the schema |
| 3 | P | A record with an extra unknown field | Still deserializes |
| 4 | N | A record missing a required field | Fails validation |
| 5 | N | An unknown enum value | Deserializes to `Unknown` instead of throwing |

## 17. Open questions

1. Should operator cancellations produce entries? Proposal: yes, triage `External`, excluded from bundles by default.
2. Should `Blocked` missions that later continue get an entry? Proposal: yes, non-terminal (`StatusAfter=Blocked`),
   superseded if the mission later fails.
3. Ship in v1.2.0 (rebuild and re-push the v1.2.0 images) or in v1.2.1 / v1.3.0?
4. Should the VS Code extension be published to the Marketplace, or distributed as a VSIX from releases only?
5. Default `maxTotalBytes` of 1 GB: right for SQLite installs; larger for server databases?

## 18. Element scorecard

Each element of the output is scored 1 to 5 for simplicity of implementation (5 = easiest), value to the user, and
value to Armada maintainers. Total = simplicity + user value + maintainer value (maximum 15). Ordered by total, then
user value.

| # | Element | Description | Simplicity | User value | Maintainer value | Total |
|---|---|---|---|---|---|---|
| 1 | Identity | Mission or voyage id and title, vessel, captain, runtime, model, persona, Harbor, attempt | 5 | 5 | 5 | 15 |
| 2 | FIX THIS brief | Agent-ready Markdown with the triage preamble; copy and download | 4 | 5 | 5 | 14 |
| 3 | Kind and raw reason, preserved | Failure kind (never null) and full reason, kept across restarts | 5 | 4 | 5 | 14 |
| 4 | Database BLOB storage (default) | Entries and objects in the database, indexed by every id | 4 | 4 | 5 | 13 |
| 5 | MCP read tools | `get_blackbox` summary, sections, paged object reads | 4 | 4 | 5 | 13 |
| 6 | Status before and after | Statuses at the failure write and the change time | 4 | 4 | 5 | 13 |
| 7 | Failure site | Phase, step, host, file, method, line, build commit | 4 | 4 | 5 | 13 |
| 8 | Mechanism | Exception, exit code, failing command, git operation and stderr | 4 | 4 | 5 | 13 |
| 9 | Documented, versioned schema | BLACKBOX_SCHEMA.md, JSON Schema, drift test, compatibility policy | 4 | 4 | 5 | 13 |
| 10 | Ask Armada explain | "Show me the pertinent details and your opinion" grounded in the entry | 3 | 5 | 4 | 12 |
| 11 | Conflicted files | Files git reported as conflicted, bounded, with total | 3 | 5 | 4 | 12 |
| 12 | Ranked recovery paths | Paths with preconditions, risk and exact commands | 3 | 5 | 4 | 12 |
| 13 | Voyage roll-up | Missions by status, failed entries, cascade root, settlement rule | 3 | 5 | 4 | 12 |
| 14 | Dashboard panel | Mission and voyage panels with copy, audience toggle, actions | 4 | 5 | 3 | 12 |
| 15 | Conflicted directories | Directory rollup of conflicted files | 4 | 4 | 4 | 12 |
| 16 | Evidence objects | Agent output, logs, check and verification output, git status, diff | 4 | 4 | 4 | 12 |
| 17 | Basic retention and expiry | `retentionDays`, keep-latest, pinning | 4 | 4 | 4 | 12 |
| 18 | REST retrieval routes | Entry, brief, sections, objects, enumerate, download | 4 | 3 | 5 | 12 |
| 19 | Workspace state | Dock and worktree existence, branch tip, dirty paths, Harbor link | 3 | 4 | 5 | 12 |
| 20 | Target movement | Start commit, target tips, commits landed since start | 3 | 4 | 5 | 12 |
| 21 | Attempt history | Prior attempts, stage outcomes, rescue chain | 3 | 4 | 5 | 12 |
| 22 | Build and environment identity | Armada version and commit, Harbor, runtime and git versions | 5 | 2 | 5 | 12 |
| 23 | .NET client (SDK) methods | `Armada.Client` black box methods with contract tests | 4 | 3 | 5 | 12 |
| 24 | Helm CLI | `armada blackbox show --brief` and the full command set | 4 | 4 | 3 | 11 |
| 25 | Conflicting mission attribution | Which missions landed overlapping commits (trailers) or overlapped concurrently | 2 | 5 | 4 | 11 |
| 26 | Plain-language summary | 3 to 6 sentences on what happened and what to do | 3 | 5 | 3 | 11 |
| 27 | Preventive configuration recommendations | Setting, scope, current, suggested, how to apply | 2 | 5 | 4 | 11 |
| 28 | MCP act: recovery and recommendations | Dry run, confirm token, annotation | 2 | 5 | 4 | 11 |
| 29 | Triage verdict | UserConfig, MissionBrief, Transient, ArmadaDefect, External | 3 | 4 | 4 | 11 |
| 30 | Effective configuration with sources | Settings that shaped the failure and where each came from | 3 | 3 | 5 | 11 |
| 31 | Fingerprint and similar failures | Failure-mode hash, occurrences, similar entries and resolutions | 3 | 3 | 5 | 11 |
| 32 | Maintainer bundle | Up to 50 entries grouped by fingerprint | 4 | 2 | 5 | 11 |
| 33 | Postman collection and mock | Black Box folder, tests, examples, mock | 4 | 2 | 5 | 11 |
| 34 | Notes and resolution tracking | Notes by operators and agents; resolution and link | 4 | 3 | 4 | 11 |
| 35 | Full GC (quotas, cascades, orphans, dry run) | Two-phase delete and reports | 3 | 3 | 4 | 10 |
| 36 | Redaction report | Which redaction rules fired and how often | 3 | 4 | 3 | 10 |
| 37 | TUI Failure tab and list | Copy, export, recovery, Ask | 3 | 4 | 3 | 10 |
| 38 | Status timeline | Every status change in the attempt | 3 | 3 | 4 | 10 |
| 39 | External storage targets | Disk, S3 and compatible, Azure, GCS, CIFS, NFS through Blobject | 2 | 3 | 4 | 9 |
| 40 | Armada behavior-change hypotheses | Improvement ideas with source pointers and severity | 2 | 2 | 5 | 9 |
| 41 | Scope and boundary detail | Scoped files, out-of-scope files, boundary findings | 4 | 2 | 3 | 9 |
| 42 | Conflict hotspots | Directories and files that conflict most per vessel | 3 | 3 | 3 | 9 |
| 43 | Overlap warning at dispatch (extension) | Warn or chain missions touching hot or overlapping paths | 2 | 4 | 3 | 9 |
| 44 | Regression fixture export (extension) | Real failures become advisor fixtures | 3 | 1 | 5 | 9 |
| 45 | MCP resources and prompts | `armada://blackbox/...` resources, `review_blackbox` prompt (if Voltaic supports them) | 3 | 2 | 3 | 8 |
| 46 | Storage migration between targets | Copy, verify, switch, delete; resumable | 2 | 2 | 4 | 8 |
| 47 | Model-written summary (extension) | Labeled model summary over the entry | 3 | 3 | 2 | 8 |
| 48 | VS Code extension | New extension: tree view, brief, recovery, file decorations | 1 | 4 | 2 | 7 |
| 49 | Mobile | Detail, recovery, share | 3 | 2 | 2 | 7 |
| 50 | Self-improvement mission (extension) | Dispatch an Armada mission from the maintainer bundle | 2 | 1 | 4 | 7 |

## 19. Effort summary

| Phase | Scope | Estimate (one developer) |
|---|---|---|
| 0 | Evidence fixes | 3 days |
| 1 | MVP: entries, Database BLOB storage, migration 103 on four providers, capture at every failure site, sweeper and backfill, agent brief, core REST and MCP read tools, client, dashboard panel, Helm `show`, basic retention | 10 days |
| 2 | Git forensics and conflict attribution | 5 days |
| 3 | Advisor, triage, configuration with sources, recovery actions with dry run and confirm, notes and resolution, Ask Armada | 7 days |
| 4 | External storage targets and migration, full retention and GC, dashboard list and entry pages, TUI, Helm, mobile, WebSocket, MCP resources and prompts, docs, README, CHANGELOG, Postman | 10 days |
| 5 | VS Code extension | 5 days |
| 6 | Maintainer loop (bundle, fixtures, trends, model summary) | 4 days |
| | **Total (without the 13.1 prevention and 13.3 forensics extensions)** | **about 44 days (9 weeks)** |
| | **MVP: Phases 0 and 1** | **about 13 days** |
| | **"Ask Armada can explain it, and agents can act on it": Phases 0 to 3** | **about 25 days (5 weeks)** |

Testing is included in each phase's estimate (roughly a third of each). The storage contract suite against Docker
emulators (Less3 or MinIO, Azurite, fake-gcs) and the VS Code extension's test harness are the main new test
infrastructure.
