# Mission and voyage black box

When a mission or voyage fails, Armada writes a black box entry at the moment of failure: an index row plus a small
bundle of objects (the versioned `record.json`, the FIX THIS briefs and bounded evidence). This document is the
developer contract the black box surfaces build on. The consumer schema (every object, section and field with types
and bounds) is in [BLACKBOX_SCHEMA.md](BLACKBOX_SCHEMA.md) and the generated JSON Schema in
[blackbox-record.schema.json](blackbox-record.schema.json). The design and phases are in `MISSION_BLACKBOX_PLAN.md`.

User-facing sections (what an entry contains, dashboard and CLI usage, storage targets and retention for operators,
MCP and Ask Armada patterns, troubleshooting) are stubs below; the documentation wave completes them.

## Contents

1. Concepts (stub)
2. Using the black box (stub)
3. Developer contract
4. TODO(blackbox-wave2) list

## 1. Concepts

Stub. See `MISSION_BLACKBOX_PLAN.md` sections 1 to 5.

## 2. Using the black box

Stub. REST routes, MCP tools, the dashboard panel, the TUI Failure tab and Helm commands are built in wave two.

## 3. Developer contract

Everything below exists on `feature/blackbox` and is covered by tests. Surfaces (REST, MCP, WebSocket commands,
clients, UIs) call only `IBlackBoxService`; failure sites call only `IBlackBoxCapture` (through
`BlackBoxCaptureExtensions`); enrichment and analysis plug in through `IBlackBoxEnricher` and `IBlackBoxAdvisor`; storage
targets plug in through `IBlackBoxStore` and `BlackBoxStoreFactory`.

### 3.1 Composition and wiring

- `BlackBoxRuntime.Create(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging, IBlackBoxVersionProvider? version = null, IBlackBoxEventSink? events = null)`
  builds every component once and exposes them: `Redactor`, `Stores`, `Objects`, `Renderer`, `Writer`, `Events`,
  `Capture`, `Pipeline`, `Advisor`, `Reanalyzer`, `Queue`, `Sweeper`, `GarbageCollector`, `Migrator`, `Hooks`, `Service`,
  `Background`. It sets `Pipeline.Advisor` to a `BlackBoxAdvisor` over `BlackBoxRuleCatalog.Default`,
  `Pipeline.SummaryWriter` to a `ModelEndpointBlackBoxSummaryWriter`, registers `BlackBoxConfigurationEnricher` as the
  first enricher, and gives `Background` the `Reanalyzer` and the `Migrator`. `Events` wraps the given sink in a
  `MeteringBlackBoxEventSink` (section 3.19). `ApplySettingsChanged(previousRetention)` drops cached stores,
  registers new secrets with the redactor and, when `retentionDays` or `resolvedRetentionDays` changed, starts the
  background expiry recompute; `PUT /api/v1/settings` copies `blackBox` into the live settings object
  (`BlackBoxSettings.CopyFrom`, so every component sees the change) and then calls it.
- The Admiral (`src/Armada.Server/ArmadaServer.cs`) builds one runtime right after `MissionService`, with
  `BuildIdentityBlackBoxVersionProvider` (BuildIdentity version and commit) and a `DatabaseBlackBoxEventSink` whose
  `OnEmitted` broadcasts each event to the tenant's WebSocket clients. It sets `MissionService.BlackBox`, starts
  `Background` (enrichment worker, sweeper every `blackBox.sweeper.intervalSeconds`, garbage collection every
  `blackBox.retention.gcIntervalMinutes`; both check `blackBox.enabled` on every tick) and stops it before the database
  is disposed; `Background.Start()` also resumes unfinished storage migrations. `ArmadaServer.BlackBox` exposes the
  runtime to routes and tests. After registering the MCP tools the Admiral sets `Service.ActionExecutor` to a
  `BlackBoxMcpActionExecutor` (recovery runs and recommendation applies run through the registered MCP tool handlers,
  as the caller). The Admiral passes `runtime.Hooks` (`IBlackBoxLifecycleHooks`) to `MissionRoutes`, `VesselRoutes`,
  `TenantRoutes` (`BlackBoxHooks` property) and `McpToolRegistrar.RegisterAll(..., blackBoxHooks)`, and the runtime to
  `StatusRoutes.BlackBox` (doctor check and settings changes).
- Namespaces: services in `Armada.Core.BlackBox`, storage in `Armada.Core.BlackBox.Storage`, models in
  `Armada.Core.Models`, enums in `Armada.Core.Enums`, settings in `Armada.Core.Settings`, database interfaces in
  `Armada.Core.Database.Interfaces`, shared SQL in `Armada.Core.Database.BlackBox`.

### 3.2 Wiring a failure site (one line)

Update the failing row first, then record it. The capture never throws, records the caller's member, file and line as
the failure site, and is a no-op when the black box is not wired (`null`):

```csharp
MissionStatusEnum statusBefore = mission.Status;
mission.Status = MissionStatusEnum.Failed;
mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
mission.FailureReason = "...";
await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
await BlackBox.RecordMissionFailureAsync(mission, statusBefore, BlackBoxStepEnum.ScopeViolation, context =>
{
    context.Dock = dock;                                  // rows you already hold save reads
    context.OutOfScopeFiles.AddRange(outOfScopeFiles);    // facts only the failure site knows
    context.GitFacts = new BlackBoxGitFacts { ConflictedFiles = conflicted, TargetTipAfter = tip };
    context.Exception = ex;
}, token).ConfigureAwait(false);
```

The reference site is `MissionService.TryFailMissionForScopeViolationAsync` (`src/Armada.Core/Services/MissionService.cs`).
Services that fail missions expose `public IBlackBoxCapture? BlackBox { get; set; }` and the Admiral assigns
`runtime.Capture`. Voyages use `RecordVoyageFailureAsync(voyage, statusBefore, step, reason, configure, token)`.
`BlackBoxCaptureContext` fields: Mission (required), StatusBefore, Step, CaptureMode, Exception, ExitCode,
RuntimeFailureKind, FailingCommand, CommandExitCode, CommandOutput, GitFacts, ScopedFiles, OutOfScopeFiles,
BoundaryFindings, CheckResults, MergeEntryId, IncidentId, StageOutcomeId, SuspectedCause, CauseRuleId, Reason (used when
the site writes no Mission.FailureReason, such as a non-terminal entry), Note, Vessel, Captain, CaptainId (when the site
cleared Mission.CaptainId before its write), Voyage, Dock, CallerMember, CallerFile, CallerLine, Component. `BlackBoxGitFacts`: ConflictedFiles, DirtyPaths,
TargetTipBefore, TargetTipAfter, WorktreeExisted, CheckoutPath, BranchTip, GitOperation, GitStderr.
`BlackBoxVoyageCaptureContext`: Voyage (required), StatusBefore, Step, CaptureMode, Reason, Exception, RootMissionId,
SettlementRule, HaltReason, Note, CallerMember, CallerFile, CallerLine, Component.

What a capture does: numbers the attempt and supersedes the previous current entry (`CreateCurrentAsync`), builds
Subject, Status, Site, Mechanism, Classification and Configuration (from the mission, vessel, voyage, captain, dock,
Harbor and global settings), Workspace, Conflict (with a depth 1 to 3 directory roll-up) and Scope from the context,
adds the previous attempt, computes the fingerprint and expiry, redacts and bounds the record, writes `record.json`,
the three briefs and in-memory evidence (`evidence/agent-output.txt`, `evidence/diff.patch`), writes `blackbox_files`
rows (Conflicted, DirtyInCheckout, OutOfScope), sets `missions.last_blackbox_entry_id` with a narrow update (no
`last_update_utc` change), emits `blackbox.captured` and queues enrichment. Everything beyond the index insert runs
under `blackBox.captureTimeoutMs` (default 2000); on timeout record.json is rewritten with `truncation.timedOut` and
the rest is left to enrichment.

Site conventions (every failure write in plan 5.1 is wired; `Services.BlackBoxCaptureSites` checks each step):

- Read `StatusBefore` before the mutation; capture only after the row update succeeded (for a compare-and-set write, only
  when it wrote). Pass the rows you hold (`Dock`, `Captain`, `Vessel`, `Voyage`) so the entry keeps ids the site cleared.
- `BlackBoxSiteFacts.ApplyException(context, ex)` records the exception and the git facts it carries
  (`GitMergeConflictException.ConflictedFiles`, `GitDirtyCheckoutException.ModifiedPaths` and checkout, `GitCommandException`
  command and stderr, `LandingVerificationException` command and exit code). `LandingStepOf`, `ExecutionStepOf` and
  `MergeQueueStepOf` map an exception, a `RuntimeFailureKindEnum` or a `MergeQueueFailure` to a step.
- Non-terminal entries: a provisioning failure that returns the mission to Pending (once per mission and cause), a
  dependent held as Blocked, an unconfirmed stop (once per episode), an orphaned exit held as WorkProduced, a landing not
  attempted, an expired landing deferral, a used-up rescue budget. `StatusAfter` is the status the mission kept.
- Triage: operator stops and cancellations (`OperatorStop`, the External phase, kind `OperatorAction`, a voyage
  `Cancelled`) are `External`.
- Every mission capture whose status changed emits `mission.status_changed` with `MissionStatusChangedPayload`
  (`PreviousStatus`), through `BlackBoxCapture.MissionStatusChanged` (the Admiral's `EmitMissionStatusChangedAsync`) or as
  an event row, whether or not the black box is enabled.
- Volatile facts: `DockService` writes the start commit into `Dock.GitAnchorsJson` on every provision (`DockStartCommit`)
  and `Mission.StartCommit` is set when the dock is recorded; checks read the `.start` file, then the anchors, then the
  mission. `BlackBoxCapture.Harbors` records `HarborConnectionManager.GetLinkState(harborId)` for Harbor-hosted docks.
  `MissionLandingHandler` passes `WorktreeExisted=false` when it had to recreate, or could not recreate, the worktree,
  and the dock head and target tip read by `LandingTargetSynchronization`. A memory-pressure deferral records one typed
  `mission.dispatch_deferred` event (`MissionDispatchDeferredPayload`) per Pending mission and reason.
- Not captured inline: `NoEligibleCaptain` and `MemoryPressure` (waits, not failure writes) and `VesselDeleted` (written by
  the vessel routes and MCP tools; their missions are recorded as `OperatorTransition` by `CancelMissionAsync`, and the
  sweeper covers the rest).

### 3.3 Services
### IBlackBoxCapture

`src/Armada.Core/BlackBox/IBlackBoxCapture.cs`

```csharp
Task<string?> CaptureMissionFailureAsync(BlackBoxCaptureContext context, CancellationToken token = default);
Task<string?> CaptureVoyageFailureAsync(BlackBoxVoyageCaptureContext context, CancellationToken token = default);
```

### IBlackBoxService (the facade every surface calls)

`src/Armada.Core/BlackBox/IBlackBoxService.cs`

```csharp
Task<BlackBoxEntry> GetMissionEntryAsync(AuthContext caller, string missionId, bool includeRecord = true, CancellationToken token = default);
Task<BlackBoxEntry> GetVoyageEntryAsync(AuthContext caller, string voyageId, bool includeRecord = true, CancellationToken token = default);
Task<BlackBoxEntry> GetEntryAsync(AuthContext caller, string entryId, bool includeRecord = true, CancellationToken token = default);
Task<string> ResolveEntryIdAsync(AuthContext caller, string? missionId, string? voyageId, string? entryId, CancellationToken token = default);
Task<BlackBoxDigest> GetDigestAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxRecord> GetRecordAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxSectionResult> GetSectionAsync(AuthContext caller, string entryId, BlackBoxSectionEnum section, CancellationToken token = default);
Task<string> GetBriefAsync(AuthContext caller, string entryId, BlackBoxAudienceEnum audience = BlackBoxAudienceEnum.Agent, bool redactHosts = false, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntrySummary>> EnumerateMissionEntriesAsync(AuthContext caller, string missionId, EnumerationQuery? query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntrySummary>> EnumerateAsync(AuthContext caller, BlackBoxEnumerateRequest? query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntrySummary>> SearchAsync(AuthContext caller, BlackBoxSearchRequest request, CancellationToken token = default);
Task<EnumerationResult<BlackBoxObjectSummary>> EnumerateObjectsAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default);
Task<Armada.Core.BlackBox.Storage.BlackBoxObjectReadResult> ReadObjectAsync(AuthContext caller, string entryId, string name, long offset = 0, long? length = null, CancellationToken token = default);
Task<BlackBoxObjectChunk> ReadObjectChunkAsync(AuthContext caller, string entryId, string name, long offset = 0, int? length = null, CancellationToken token = default);
Task<byte[]> DownloadAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxNote> AddNoteAsync(AuthContext caller, string entryId, BlackBoxNoteAddRequest request, BlackBoxNoteAuthorKindEnum defaultAuthor = BlackBoxNoteAuthorKindEnum.Operator, CancellationToken token = default);
Task<EnumerationResult<BlackBoxNote>> EnumerateNotesAsync(AuthContext caller, string entryId, EnumerationQuery? query, CancellationToken token = default);
Task<BlackBoxEntry> ResolveAsync(AuthContext caller, string entryId, BlackBoxResolveRequest request, CancellationToken token = default);
Task<BlackBoxEntry> PinAsync(AuthContext caller, string entryId, bool pinned, CancellationToken token = default);
Task<BlackBoxDeleteResult> DeleteAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxDeleteResult> DeleteManyAsync(AuthContext caller, BlackBoxDeleteManyRequest request, CancellationToken token = default);
Task<BlackBoxPurgeResult> PurgeAsync(AuthContext caller, BlackBoxPurgeRequest request, CancellationToken token = default);
Task<BlackBoxGcReport> RunGcAsync(AuthContext caller, BlackBoxGcRunRequest? request, CancellationToken token = default);
Task<BlackBoxRetentionStatus> GetRetentionStatusAsync(AuthContext caller, CancellationToken token = default);
Task<BlackBoxStorageStatus> GetStorageStatusAsync(AuthContext caller, CancellationToken token = default);
Task<BlackBoxStorageMigrationJob> StartStorageMigrationAsync(AuthContext caller, BlackBoxStorageMigrationRequest request, CancellationToken token = default);
Task<BlackBoxStorageMigrationJob> GetStorageMigrationAsync(AuthContext caller, string migrationId, CancellationToken token = default);
Task<BlackBoxBackfillResult> BackfillAsync(AuthContext caller, BlackBoxBackfillRequest? request, CancellationToken token = default);
Task<BlackBoxEntry> ReanalyzeAsync(AuthContext caller, string entryId, CancellationToken token = default);
Task<BlackBoxRecoveryRunResult> RunRecoveryAsync(AuthContext caller, string entryId, string pathId, BlackBoxRecoveryRunRequest request, CancellationToken token = default);
Task<BlackBoxRecommendationApplyResult> ApplyRecommendationAsync(AuthContext caller, string entryId, string recommendationId, BlackBoxRecommendationApplyRequest request, CancellationToken token = default);
Task<BlackBoxBundleResult> GetBundleAsync(AuthContext caller, BlackBoxBundleRequest? request, CancellationToken token = default);
Task<EnumerationResult<BlackBoxHotspot>> EnumerateHotspotsAsync(AuthContext caller, BlackBoxHotspotEnumerateRequest? request, CancellationToken token = default);
Task<EnumerationResult<BlackBoxFingerprintSummary>> EnumerateFingerprintsAsync(AuthContext caller, BlackBoxFingerprintEnumerateRequest? request, CancellationToken token = default);
Task<BlackBoxFingerprintTrend> GetFingerprintTrendAsync(AuthContext caller, string fingerprint, CancellationToken token = default);
Task<BlackBoxFixture> ExportFixtureAsync(AuthContext caller, string entryId, string? name = null, CancellationToken token = default);
```

### IBlackBoxEnricher

`src/Armada.Core/BlackBox/IBlackBoxEnricher.cs`

```csharp
string Name { get; }
Task EnrichAsync(BlackBoxEnrichmentContext context, CancellationToken token = default);
```

### IBlackBoxAdvisor

`src/Armada.Core/BlackBox/IBlackBoxAdvisor.cs`

```csharp
int RulesVersion { get; }
BlackBoxAnalysis? Analyze(BlackBoxRecord record);
```

Implemented by `BlackBoxAdvisor` (section 3.13).

### IBlackBoxActionExecutor

`src/Armada.Core/BlackBox/IBlackBoxActionExecutor.cs`

```csharp
Task<BlackBoxRecoveryStepResult> ExecuteAsync(AuthContext caller, BlackBoxAction action, CancellationToken token = default);
```

### IBlackBoxSummaryWriter

`src/Armada.Core/BlackBox/IBlackBoxSummaryWriter.cs`

```csharp
Task<string?> WriteSummaryAsync(string endpointId, BlackBoxRecord record, BlackBoxAnalysis analysis, CancellationToken token = default);
```

### IBlackBoxEnrichmentQueue

`src/Armada.Core/BlackBox/IBlackBoxEnrichmentQueue.cs`

```csharp
int Pending { get; }
bool Enqueue(string entryId);
```

### IBlackBoxEventSink

`src/Armada.Core/BlackBox/IBlackBoxEventSink.cs`

```csharp
Task EmitAsync(string eventType, string message, string? tenantId, string? entryId, string? missionId, string? voyageId, string? vesselId, object payload, CancellationToken token = default);
```

### IBlackBoxVersionProvider

`src/Armada.Core/BlackBox/IBlackBoxVersionProvider.cs`

```csharp
string Version { get; }
string? Commit { get; }
```

Service rules: a caller that is not authenticated, or not a global admin and without a tenant, gets
`UnauthorizedAccessException`. Visibility: global admin all tenants, tenant admin their tenant, user their own entries
(`BlackBoxEntry.UserId`). Invisible entries read as `BlackBoxNotFoundException` (no existence leak); an entry in the
Deleting state reads as `BlackBoxDeletingException`. Request validation errors are `ArgumentException` or
`ArgumentOutOfRangeException` (bounds: notes 1 to 8000 characters, search query 1 to 200, REST reads at most 1 MB,
MCP chunks at most 64 KB, delete at most 1000 ids as `CollectionLimitExceededException`). Briefs are rendered live from
the record (so renderer improvements apply to old entries) and include the ten newest notes. Storage migration
(`StartStorageMigrationAsync`, `GetStorageMigrationAsync`) needs a global admin (`UnauthorizedAccessException`
otherwise); Start throws `ArgumentException` for an Unknown, equal-to-From or misconfigured target (REST 400),
`InvalidOperationException` while a different migration is unfinished (409) and `BlackBoxStoreUnavailableException` when
the target fails its health check (503); Get throws `BlackBoxNotFoundException` for an unknown job (404). Starting the
move that is already unfinished returns that job. `GetStorageStatusAsync` fills `ActiveMigrationId` and reports a
misconfigured target as unhealthy with the reason.

Other public classes:

| Class | Role |
|---|---|
| `BlackBoxCapture` | `IBlackBoxCapture`; `Enrichment` property; `static TrimSourcePath(string?)` |
| `BlackBoxCaptureExtensions` | `RecordMissionFailureAsync`, `RecordVoyageFailureAsync` (one-line helpers) |
| `BlackBoxService` | `IBlackBoxService`; `static Summarize(BlackBoxEntry)`, `static SummarizeObject(BlackBoxObject)` |
| `BlackBoxPipeline` | `AddEnricher(IBlackBoxEnricher)`, `Advisor` (default `NoOpBlackBoxAdvisor`; the runtime sets `BlackBoxAdvisor`), `SummaryWriter`, `SummaryTimeoutMs` (30000), `ProcessAsync(entryId)`, `ReanalyzeAsync(entryId)` (advisor only), `LoadRecordAsync(entry)` |
| `BlackBoxAdvisor` | `IBlackBoxAdvisor`; `Catalog`; `static Analyze(record, catalog, diagnostics)`, `static Tally(votes)` |
| `BlackBoxRuleCatalog` | `CurrentRulesVersion`, `Default`, `Rules`, `Find(id)`, `static BuildRules()`; rule building blocks `BlackBoxRule`, `BlackBoxRuleRecommendation`, `BlackBoxRuleImprovement`, `BlackBoxRulePath`, `BlackBoxRuleStep` |
| `BlackBoxRuleTemplate`, `BlackBoxRulePlaceholders`, `BlackBoxRuleFacts` | Placeholder rendering, placeholder values, derived facts shared by rule predicates |
| `BlackBoxActionCommands` | `SupportedTools`, `DataLossTools`, `IsSupported`, `Rest(action)`, `Cli(action)` |
| `BlackBoxReanalyzer` | `RunOnceAsync()`: re-analyzes entries with an older rules version (`PageSize` 100, `MaxPagesPerRun` 5, `MaxPerRun` 100) |
| `BlackBoxConfigurationResolver` | `Resolve(inputs)`, `LoadAsync(...)` (persona, workflow profile), `Merge(target, extra)`; `BlackBoxConfigurationInputs`; `BlackBoxConfigurationEnricher` |
| `BlackBoxConfirmationStore` | `Issue`, `Consume`, `Lifetime` (10 minutes), `MaxPending` (10000), `Clock`; `BlackBoxPendingConfirmation` |
| `BlackBoxFixtureExporter` | `Sanitize(record)`, `Build(record, advisor, ...)`, `Serialize`, `Parse`, `WriteAsync(fixture, directory)` |
| `ModelEndpointBlackBoxSummaryWriter` | `IBlackBoxSummaryWriter` over a model endpoint (`MaxTokens` 400, `MaxPromptChars` 12000), `BuildPrompt` |
| `BlackBoxMcpActionExecutor` (Armada.Server) | `IBlackBoxActionExecutor` through `AskActionService.ExecuteAsync` |
| `BlackBoxEnrichmentQueue` | `IBlackBoxEnrichmentQueue`; `Start()`, `StopAsync()`, `DrainAsync()`; capacity 10000 |
| `BlackBoxEnrichmentContext` | Entry, Record, Mission, Voyage, Vessel, and collectors Files, Links, Objects (`BlackBoxPendingObject`: Name, ContentType, Data, IsText) |
| `BlackBoxEntryWriter` | Writes record.json, briefs and evidence with redaction, bounds, fallback and the time budget; saves totals |
| `BlackBoxSweeper` | `SweepAsync()`, `BackfillAsync(since, maxSubjects)`, `static InferStep(Mission)`, `StaleCapturedAfter` (10 minutes) |
| `BlackBoxGarbageCollector` | `RunAsync(dryRun)`, `DeleteEntryAsync(entry, reason)` (two-phase), `LastReport`, `NextRunUtc` |
| `BlackBoxBackgroundService` | `Start()`, `StopAsync()`: queue worker, sweeper loop, GC loop |
| `BlackBoxBriefRenderer` | `Render(entry, record, audience, redactHosts)`, `static ObjectName(audience)`, `TargetBytes` (12 KB), `MaxBytes` (40 KB), `CutMarker` |
| `BlackBoxRedactor` | `Redact(text, report)`, `RedactUtf8(bytes, report)`, `RedactRecord(record)`, `RegisterSecrets(values)`, `Rules`, `Marker` |
| `BlackBoxFingerprint` | `Compute(phase, step, kind, method, reason)`, `NormalizeReason(reason)` |
| `BlackBoxTaxonomy` | `PhaseOf(step)`, `StepsOf(phase)` |
| `BlackBoxRecordBounder` | `Bound(record)`, `AddNote`, `AddField`, `StringCap(propertyName)` |
| `BlackBoxRetentionPolicy` | `ComputeExpiry(entry, policy, vesselRetentionDays)` |
| `BlackBoxLimits` | Every limit (collection sizes, string caps, record 256 KB, brief 12 KB target and 40 KB cap, chunk sizes) |
| `BlackBoxJson` | `Options` (camelCase, tolerant enums, unknown fields skipped), `SerializeRecord`, `DeserializeRecord` |
| `BlackBoxSchemaGenerator` | `Generate()`, `GenerateJson()`; source of `docs/blackbox-record.schema.json` |
| `BlackBoxSettingsSecrets` | `Mask(settings, marker)`, `KeepMasked(incoming, current, marker)` (used by `GET`/`PUT /api/v1/settings`) |
| `BlackBoxObjectNames`, `BlackBoxEventTypes` | Stable object names and event types |
| `DatabaseBlackBoxEventSink`, `AssemblyBlackBoxVersionProvider`, `NoOpBlackBoxAdvisor`, `NoOpBlackBoxEnricher` | Defaults |

Exceptions (`Armada.Core.BlackBox`, base `BlackBoxException` with `Code`): `BlackBoxNotFoundException`
(BlackBoxNotFound, REST 404), `BlackBoxDeletingException` (BlackBoxDeleting, 409), `BlackBoxObjectMissingException`
(BlackBoxObjectMissing, 410), `BlackBoxObjectCorruptException` (BlackBoxObjectCorrupt, 409),
`BlackBoxStoreUnavailableException` (BlackBoxStoreUnavailable, 503), `BlackBoxConfirmationException`
(BlackBoxConfirmationRequired, 409, for recovery runs).

### 3.4 Storage
### IBlackBoxStore

`src/Armada.Core/BlackBox/Storage/IBlackBoxStore.cs`

```csharp
BlackBoxStorageTargetEnum Target { get; }
Task WriteAsync(BlackBoxObjectWrite write, CancellationToken token = default);
Task<Stream> OpenReadAsync(BlackBoxObjectRef reference, long offset, long? length, CancellationToken token = default);
Task<bool> ExistsAsync(BlackBoxObjectRef reference, CancellationToken token = default);
Task DeleteAsync(BlackBoxObjectRef reference, CancellationToken token = default);
Task<BlackBoxStoreDeleteResult> DeleteManyAsync(BlackBoxObjectRefSet references, CancellationToken token = default);
IAsyncEnumerable<BlackBoxStoredKey> EnumerateAsync(string prefix, CancellationToken token = default);
Task<BlackBoxStoreHealth> CheckHealthAsync(CancellationToken token = default);
```

- `DatabaseBlackBoxStore` (default): bytes in `blackbox_objects.data` (SQLite BLOB, PostgreSQL bytea, SQL Server
  varbinary(max), MySQL LONGBLOB); ranged reads are cut in SQL; the metadata row must exist before its bytes are written.
- `BlobjectBlackBoxStore(target, BlobClientBase client, diskRoot)` (`IDisposable`): every external target over its
  Blobject 6.1.2 client (`Blobject.Disk`, `Blobject.AmazonS3` for S3 and S3-compatible stores such as MinIO and Less3,
  `Blobject.AzureBlob`, `Blobject.GoogleCloud`, `Blobject.CIFS`, `Blobject.NFS`). Objects live at their `StorageKey`.
  Reads fetch the whole object (`GetAsync`) and slice it (objects are bounded by `maxEvidenceObjectBytes`); the Disk
  target reads ranges straight from the file. Not-found errors of every SDK (FileNotFound, KeyNotFound, S3 404, Azure
  404, Google 404) become `BlackBoxObjectMissingException`; other failures become `BlackBoxStoreUnavailableException`
  (no secrets in messages). Deleting a missing key succeeds; `DeleteManyAsync` maps Blobject's per-key results back to
  object names. `EnumerateAsync(prefix)` skips folder entries and reports `LastUpdateUtc` in UTC (the Disk target walks
  the file system itself and skips reparse points). `CheckHealthAsync` is Blobject `ValidateConnectivity`. The Disk
  target (`ResolveDiskPath(key)`) refuses keys with `.`/`..` segments, rooted keys, keys with `:` and keys that resolve
  outside its root, and refuses any path below the root that crosses a symbolic link or other reparse point (write,
  read, exists, delete); the root itself may sit under a link (for example macOS `/var`).
- `BlackBoxStoreFactory(DatabaseDriver, BlackBoxSettings, string? dataDirectory = null)`: `Get(target)`,
  `GetConfigured()`, `Register(target, builder)` (test doubles and other stores), `Reset()` (disposes cached built-in
  stores), `DiskDirectory`. `CreateBuiltIn` has one `case` per target (Database, Disk, S3, AzureBlob, GoogleCloud, Cifs,
  Nfs); a target whose settings are invalid or whose client cannot be built throws `BlackBoxStoreUnavailableException`
  ("The black box S3 storage target is misconfigured: blackBox.storage.s3.bucket is required ..."), which the writer
  treats like a store failure (fallback to the database); `Unknown` throws `NotSupportedException`.
- `BlackBoxBlobClientFactory`: `Create(target, storageSettings, dataDirectory)` builds the Blobject client from validated
  settings; `ResolveDiskDirectory(settings, dataDirectory)` is the configured directory (relative paths resolve against
  the data directory) or `{DataDirectory}/blackbox`. S3 with a custom endpoint uses path-style requests (Blobject forces
  them; `usePathStyle` only shapes generated URLs); without an endpoint it is AWS S3 (no keys means the SDK's default
  credential chain). Azure takes account name and key, or a connection string (AccountName, AccountKey, optional
  BlobEndpoint), and `endpoint` (default `https://{account}.blob.core.windows.net/`). Google Cloud reads the service
  account JSON from `credentialsPath` and takes an optional `endpoint` (an emulator). CIFS resolves the host name when
  the client is built, so an unknown host is reported as misconfigured.
- `BlackBoxStorageSettingsValidator`: `Validate(storageSettings, target = null)` returns errors that name the setting
  (`blackBox.storage.s3.bucket is required for the S3 target.`) and never contain a secret: S3 bucket, region, both or
  neither keys, http/https endpoint with a host whose scheme agrees with `useSsl` (keys required with an endpoint);
  Azure container and account name plus key or a connection string with AccountName and AccountKey, http/https
  endpoint; Google project, bucket and an existing credentials file, http/https endpoint; CIFS host (a valid host name
  or address), share, user and password; NFS host and an absolute export path; Disk a valid path; Unknown is refused.
  `Require` throws `ArgumentException` with every error. `PUT /api/v1/settings` validates `blackBox.storage` before
  applying anything (400 "Invalid black box storage settings: ...").
- `BlackBoxStorageDoctor.CheckAsync(settings, stores)` returns `BlackBoxStorageDoctorResult` (Status, Message): Pass when
  the configured target validates and its health check passes, Fail with the validation errors or the health message
  (plus what fallback will do), Warn when the black box is disabled or the runtime is not running. It is the twelfth
  `GET /api/v1/doctor` check, named "Black Box Storage".
- `BlackBoxStorageMigrator`: see section 3.20.
- Storage inventory: `StorageInventoryService` adds one `StorageLocation` per target that holds entries to the
  Admiral's host report (Category `BlackBox`, EntityId the target, Path `database:blackbox_objects`, the Disk directory,
  `s3://bucket/prefix`, `azure://account/container/prefix`, `gs://bucket/prefix`, `smb://host/share/prefix` or
  `nfs://host/export/prefix`, Bytes from the index, FileCount the entry count; the Database figure is also inside the
  database files).
- `BlackBoxBackupInventoryBuilder.BuildAsync(database, maxListed)` returns `BlackBoxBackupInventory` (DatabaseEntries,
  DatabaseBytes, ExternalEntries, ExternalTargets, ExternalEntryRefs (EntryId, Target, StoragePrefix, ObjectCount,
  TotalBytes; at most 10000), ExternalEntryRefsTruncated, Message). Backups (SQLite) contain Database-target entries
  inside `armada.db`; `manifest.json` gains a `blackBox` section with this inventory, the backup result a `BlackBox`
  summary, and the restore result a `BlackBox` section (counts, targets, up to 100 external entry ids) plus a sentence
  in its message when restored entries point at objects outside the database.
- `BlackBoxObjectRepository`: `WriteAsync(entry, name, contentType, raw, compress, forceDatabase)` and
  `ReadAsync(entry, name, offset, length)`; `static RefOf(entry, obj)`. An object whose `StorageKey` is null lives in the
  database whatever the entry's target (record.json is forced there when an external store fails).
- `BlackBoxObjectCodec`: `Encode(raw, compress)` (gzip or identity), `Decode(stored, encoding)`, `Sha256Hex(bytes)`. The
  SHA-256 is over the stored (encoded) bytes and is checked on every whole read and every read of a gzip object
  (always read whole); a mismatch throws `BlackBoxObjectCorruptException`.
- `BlackBoxKeyLayout`: `BuildEntryPrefix(keyPrefix, tenantId, vesselId, createdUtc, entryId)` gives
  `{keyPrefix}/{tenantId}/{vesselId or _}/{yyyy}/{MM}/{entryId}`; `BuildKey(prefix, objectName)`; `Segment(value)`
  sanitizes so a key never escapes its prefix.
- Store DTOs: `BlackBoxObjectRef` (Target, EntryId, ObjectId, Name, StorageKey), `BlackBoxObjectWrite` (Reference,
  ContentType, ContentEncoding, Data, Sha256), `BlackBoxObjectRefSet` (References, at most 1000),
  `BlackBoxStoreDeleteResult` (Requested, Deleted, Failed, FailedNames, FirstError), `BlackBoxStoredKey` (Key,
  SizeBytes, LastModifiedUtc), `BlackBoxEncodedObject`, `BlackBoxObjectReadResult` (Object, Data, Offset, TotalBytes,
  Verified).
- Fallback: when an external write of record.json fails and `blackBox.storage.fallbackToDatabase` is true, the entry
  moves to the Database target and `blackbox.storage_fallback` is emitted; with fallback off, record.json is written to
  the database, the other objects are marked missing and the entry state is ObjectMissing.

### 3.5 Database
### IBlackBoxEntryMethods (DatabaseDriver.BlackBoxEntries)

`src/Armada.Core/Database/Interfaces/IBlackBoxEntryMethods.cs`

```csharp
Task<BlackBoxEntry> CreateAsync(BlackBoxEntry entry, CancellationToken token = default);
Task<BlackBoxEntry> CreateCurrentAsync(BlackBoxEntry entry, CancellationToken token = default);
Task<BlackBoxEntry?> ReadAsync(string id, string? tenantId = null, bool includeDeleting = false, CancellationToken token = default);
Task<BlackBoxEntry?> ReadCurrentByMissionAsync(string missionId, string? tenantId = null, CancellationToken token = default);
Task<BlackBoxEntry?> ReadCurrentByVoyageAsync(string voyageId, string? tenantId = null, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateAsync(BlackBoxEnumerateRequest query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateByMissionAsync(string missionId, string? tenantId, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateByFingerprintAsync(string fingerprint, string? tenantId, EnumerationQuery query, CancellationToken token = default);
Task<long> CountAsync(BlackBoxEnumerateRequest query, CancellationToken token = default);
Task<BlackBoxEntry> UpdateAsync(BlackBoxEntry entry, CancellationToken token = default);
Task<bool> MarkSupersededAsync(string id, CancellationToken token = default);
Task<bool> UpdateStateAsync(string id, BlackBoxStateEnum state, string? lastError = null, int? enrichmentAttempts = null, DateTime? nextAttemptUtc = null, CancellationToken token = default);
Task<bool> UpdateResolutionAsync(string id, BlackBoxResolutionEnum resolution, string? note, string? link, DateTime? resolvedUtc, DateTime? expiresUtc, CancellationToken token = default);
Task<bool> UpdatePinnedAsync(string id, bool pinned, DateTime? expiresUtc, CancellationToken token = default);
Task<bool> UpdateExpiresAsync(string id, DateTime? expiresUtc, CancellationToken token = default);
Task<bool> DeleteCascadeAsync(string id, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateExpiredAsync(DateTime nowUtc, bool keepLatestPerMission, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateDeletingAsync(EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateRetryableAsync(DateTime nowUtc, int maxAttempts, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxFingerprintSummary>> EnumerateFingerprintsAsync(BlackBoxFingerprintEnumerateRequest query, CancellationToken token = default);
Task<long> SumBytesAsync(string? tenantId = null, string? vesselId = null, CancellationToken token = default);
Task<List<BlackBoxStorageTargetUsage>> SumBytesByTargetAsync(string? tenantId = null, CancellationToken token = default);
Task<long> CountByVesselAsync(string vesselId, string? tenantId = null, CancellationToken token = default);
Task<EnumerationResult<string>> EnumerateUncapturedMissionIdsAsync(DateTime? updatedSince, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<string>> EnumerateUncapturedVoyageIdsAsync(DateTime? updatedSince, EnumerationQuery query, CancellationToken token = default);
Task<bool> SetMissionLastEntryAsync(string missionId, string? entryId, CancellationToken token = default);
Task<bool> SetVoyageLastEntryAsync(string voyageId, string? entryId, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateEvictionCandidatesAsync(BlackBoxEvictionQuery query, CancellationToken token = default);
Task<EnumerationResult<string>> EnumerateVesselsOverQuotaAsync(int maxEntries, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> EnumerateCascadeCandidatesAsync(BlackBoxCascadeReasonEnum reason, string? subjectId, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxEntry>> ScanAsync(BlackBoxScanQuery query, CancellationToken token = default);
Task<bool> UpdateStorageAsync(string id, BlackBoxStorageTargetEnum target, string? storagePrefix, CancellationToken token = default);
```

Eviction candidates are unpinned and not Deleting, oldest first, filtered by `BlackBoxEvictionQuery` (VesselId,
ResolvedOnly, ExcludeCurrent, KeepLatestPerMission, PageNumber, PageSize). Vessels over quota count non-Deleting
entries. Cascade candidates: MissionDeleted (Mission entries whose mission row is gone), VesselDeleted (entries whose
vessel row is gone), both without pinned entries, and TenantDeleted (every entry of the tenant, pinned included).
`ScanAsync` is a keyset scan in id order (`BlackBoxScanQuery`: AfterId, StorageTarget, ExcludeStorageTarget, VesselId,
NeedsMigrationTo, PageSize; TotalRecords counts what remains after the cursor); `NeedsMigrationTo` selects entries
whose target differs or, for an external target, that still hold a database-resident object that is not missing.
`UpdateStorageAsync` changes only `storage_target` and `storage_prefix`.

### IBlackBoxObjectMethods (DatabaseDriver.BlackBoxObjects)

`src/Armada.Core/Database/Interfaces/IBlackBoxObjectMethods.cs`

```csharp
Task<BlackBoxObject> UpsertAsync(BlackBoxObject obj, CancellationToken token = default);
Task<BlackBoxObject?> ReadAsync(string entryId, string name, bool includeData = false, CancellationToken token = default);
Task<BlackBoxObject?> ReadByIdAsync(string id, bool includeData = false, CancellationToken token = default);
Task<byte[]?> ReadDataRangeAsync(string id, long offset, int length, CancellationToken token = default);
Task<bool> WriteDataAsync(string id, byte[]? data, CancellationToken token = default);
Task<bool> SetMissingAsync(string id, bool missing, CancellationToken token = default);
Task<EnumerationResult<BlackBoxObject>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);
Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
Task<List<string>> FilterReferencedStorageKeysAsync(List<string> storageKeys, CancellationToken token = default);
Task<bool> SetLocationAsync(string id, string? storageKey, byte[]? data, CancellationToken token = default);
```

`FilterReferencedStorageKeysAsync` (at most 1000 keys, queried 100 at a time) returns the keys an object row
references; `SetLocationAsync` sets `storage_key` and `data` together and clears `missing` (storage migration).

### IBlackBoxFileMethods (DatabaseDriver.BlackBoxFiles)

`src/Armada.Core/Database/Interfaces/IBlackBoxFileMethods.cs`

```csharp
Task<int> CreateManyAsync(List<BlackBoxFile> files, CancellationToken token = default);
Task<EnumerationResult<BlackBoxFile>> EnumerateAsync(string entryId, BlackBoxFileRoleEnum? role, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxHotspot>> EnumerateHotspotsAsync(BlackBoxHotspotEnumerateRequest query, CancellationToken token = default);
Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
```

### IBlackBoxLinkMethods (DatabaseDriver.BlackBoxLinks)

`src/Armada.Core/Database/Interfaces/IBlackBoxLinkMethods.cs`

```csharp
Task<int> CreateManyAsync(List<BlackBoxLink> links, CancellationToken token = default);
Task<EnumerationResult<BlackBoxLink>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxLink>> EnumerateByMissionAsync(string missionId, string? tenantId, EnumerationQuery query, CancellationToken token = default);
Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
```

### IBlackBoxNoteMethods (DatabaseDriver.BlackBoxNotes)

`src/Armada.Core/Database/Interfaces/IBlackBoxNoteMethods.cs`

```csharp
Task<BlackBoxNote> CreateAsync(BlackBoxNote note, CancellationToken token = default);
Task<BlackBoxNote?> ReadAsync(string id, string? tenantId = null, CancellationToken token = default);
Task<EnumerationResult<BlackBoxNote>> EnumerateAsync(string entryId, EnumerationQuery query, CancellationToken token = default);
Task<long> CountByEntryAsync(string entryId, CancellationToken token = default);
Task<bool> DeleteAsync(string id, CancellationToken token = default);
Task<int> DeleteByEntryAsync(string entryId, CancellationToken token = default);
```

### IBlackBoxMigrationMethods (DatabaseDriver.BlackBoxMigrations)

`src/Armada.Core/Database/Interfaces/IBlackBoxMigrationMethods.cs`

```csharp
Task<BlackBoxStorageMigrationJob> CreateAsync(BlackBoxStorageMigrationJob job, CancellationToken token = default);
Task<BlackBoxStorageMigrationJob?> ReadAsync(string id, CancellationToken token = default);
Task<bool> UpdateAsync(BlackBoxStorageMigrationJob job, CancellationToken token = default);
Task<EnumerationResult<BlackBoxStorageMigrationJob>> EnumerateActiveAsync(EnumerationQuery query, CancellationToken token = default);
Task<EnumerationResult<BlackBoxStorageMigrationJob>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default);
```

Implementation: the SQL is shared in `Armada.Core.Database.BlackBox` (`BlackBoxEntryMethodsBase`,
`BlackBoxObjectMethodsBase`, `BlackBoxFileMethodsBase`, `BlackBoxLinkMethodsBase`, `BlackBoxNoteMethodsBase`,
`BlackBoxSqlPaging`) over a per-provider `BlackBoxSqlDialect` (`SqliteBlackBoxSqlDialect`, `PostgresqlBlackBoxSqlDialect`,
`SqlServerBlackBoxSqlDialect`, `MysqlBlackBoxSqlDialect`: connection, transactional write under the provider's write
gate, timestamps, BLOB binding, boolean literals, paging, BLOB slicing). Each provider has thin `BlackBox*Methods`
classes in its `Implementations` folder. Every list pages in SQL with a COUNT over the same filter, ordered with the
id as the tie-break. A null `tenantId` means any tenant. Entries in the Deleting state are hidden unless
`includeDeleting` or `State = Deleting` is asked for. A full-row mission or voyage update never clears
`last_blackbox_entry_id` (COALESCE); `DeleteCascadeAsync` clears it.

Migration 104 (all four providers) adds `blackbox_migrations` (id `bbm_...`, state, from_target, to_target,
delete_source, batch_size, total_entries, migrated_entries, failed_entries, bytes_copied, last_entry_id, last_error,
created_utc, started_utc, finished_utc, last_update_utc; index on state and created_utc), shared SQL in
`BlackBoxMigrationMethodsBase`. Active jobs are Pending or Running, oldest first.

Schema (migration 103, all four providers): `blackbox_entries`, `blackbox_objects` (unique `entry_id, name`; `data`
BLOB), `blackbox_files`, `blackbox_links`, `blackbox_notes`; indexes from the plan (tenant and mission and current,
voyage, vessel and created, fingerprint and created, expires, resolution and triage, files by tenant, vessel and
directory); new columns `missions.last_blackbox_entry_id`, `missions.start_commit`, `voyages.last_blackbox_entry_id`,
`vessels.blackbox_retention_days`. Model properties: `Mission.LastBlackBoxEntryId`, `Mission.StartCommit`,
`Voyage.LastBlackBoxEntryId`, `Vessel.BlackBoxRetentionDays` (clamped 0 to 3650, null uses the global policy).

### 3.6 Models

All in `Armada.Core.Models`, one class per file, every collection with `[CollectionBound]`:

`BlackBoxAction`, `BlackBoxAnalysis`, `BlackBoxAnalyzedPayload`, `BlackBoxAttempt`, `BlackBoxBackfillRequest`, `BlackBoxBackfillResult`, `BlackBoxBundleRequest`, `BlackBoxBundleResult`, `BlackBoxCaptureFailedPayload`, `BlackBoxCapturedPayload`, `BlackBoxClassification`, `BlackBoxCommitRef`, `BlackBoxConfigRecommendation`, `BlackBoxConfigValue`, `BlackBoxConfiguration`, `BlackBoxConflict`, `BlackBoxConflictingMission`, `BlackBoxDeleteManyRequest`, `BlackBoxDeleteResult`, `BlackBoxDeletedPayload`, `BlackBoxDiffStat`, `BlackBoxDigest`, `BlackBoxDirectoryCount`, `BlackBoxDownloadManifest`, `BlackBoxEntry`, `BlackBoxEntrySummary`, `BlackBoxEnumerateRequest`, `BlackBoxEventRef`, `BlackBoxEvidenceIndex`, `BlackBoxEvidenceItem`, `BlackBoxFile`, `BlackBoxFinding`, `BlackBoxFingerprintEnumerateRequest`, `BlackBoxFingerprintSummary`, `BlackBoxGcReport`, `BlackBoxGcRunRequest`, `BlackBoxHotspot`, `BlackBoxHotspotEnumerateRequest`, `BlackBoxImprovement`, `BlackBoxLink`, `BlackBoxMechanism`, `BlackBoxNote`, `BlackBoxNoteAddRequest`, `BlackBoxObject`, `BlackBoxObjectChunk`, `BlackBoxObjectSummary`, `BlackBoxPurgeRequest`, `BlackBoxPurgeResult`, `BlackBoxRecommendationApplyRequest`, `BlackBoxRecommendationApplyResult`, `BlackBoxRecord`, `BlackBoxRecoveryPath`, `BlackBoxRecoveryRunPayload`, `BlackBoxRecoveryRunRequest`, `BlackBoxRecoveryRunResult`, `BlackBoxRecoveryStep`, `BlackBoxRecoveryStepResult`, `BlackBoxRedaction`, `BlackBoxRedactionRuleCount`, `BlackBoxRelated`, `BlackBoxResolveRequest`, `BlackBoxResolvedPayload`, `BlackBoxRetentionStatus`, `BlackBoxScope`, `BlackBoxSearchRequest`, `BlackBoxSectionResult`, `BlackBoxSimilarEntry`, `BlackBoxSite`, `BlackBoxStatusChange`, `BlackBoxStatusCount`, `BlackBoxStatusTimelineEntry`, `BlackBoxStorageFallbackPayload`, `BlackBoxStorageMigrationJob`, `BlackBoxStorageMigrationRequest`, `BlackBoxStorageStatus`, `BlackBoxStorageTargetUsage`, `BlackBoxStoreHealth`, `BlackBoxSubject`, `BlackBoxTriageVerdict`, `BlackBoxTriageVote`, `BlackBoxTruncatedField`, `BlackBoxTruncation`, `BlackBoxVoyageRollup`, `BlackBoxWorkspace`.

Record sections (`BlackBoxRecord`): Subject, Status, Site, Mechanism, Classification, Workspace, Conflict, Scope,
Configuration, Attempts, Evidence, Analysis, Related, Voyage, Redaction, Truncation. A section that was not captured is
null (Attempts is an empty list; Redaction and Truncation are always present). Field-level detail is in
[BLACKBOX_SCHEMA.md](BLACKBOX_SCHEMA.md).

### 3.7 Enums

All with `[JsonConverter(typeof(JsonStringEnumConverter))]`; every enum has an `Unknown` member (tolerant readers map
values from a newer Armada to it):

- `BlackBoxAudienceEnum`: Agent, User, Maintainer, Unknown
- `BlackBoxBundleFormatEnum`: Markdown, Json, Unknown
- `BlackBoxCaptureModeEnum`: Inline, Sweeper, Backfill, Unknown
- `BlackBoxConfidenceEnum`: Low, Medium, High, Unknown
- `BlackBoxConfigSourceEnum`: Mission, Voyage, Vessel, WorkflowProfile, Captain, Persona, Global, Default, Unknown
- `BlackBoxFileRoleEnum`: Conflicted, DirtyInCheckout, OutOfScope, ChangedByMission, ChangedByConflictingMission, BoundaryViolation, Unknown
- `BlackBoxHostKindEnum`: Admiral, Harbor, Unknown
- `BlackBoxJobStateEnum`: Pending, Running, Completed, Failed, Cancelled, Unknown
- `BlackBoxLinkRelationEnum`: LandedOnTargetSinceStart, OverlappingFiles, SharedDock, SiblingInVoyage, DependencyOf, ParentOfRescue, RescueOf, CascadeRoot, Unknown
- `BlackBoxNoteAuthorKindEnum`: Operator, Agent, AskArmada, System, Unknown
- `BlackBoxPhaseEnum`: Unknown, Dispatch, Provision, Execution, Completion, Review, Handoff, Landing, MergeQueue, Recovery, Voyage, External
- `BlackBoxResolutionEnum`: Open, Resolved, Duplicate, WontFix, ArmadaFixed, Unknown
- `BlackBoxSectionEnum`: Subject, Status, Site, Mechanism, Classification, Workspace, Conflict, Scope, Configuration, Attempts, Evidence, Analysis, Related, Voyage, Redaction, Truncation, Unknown
- `BlackBoxSeverityEnum`: Low, Medium, High, Critical, Unknown
- `BlackBoxStateEnum`: Captured, Enriched, Analyzed, EnrichmentFailed, Deleting, ObjectMissing, Unknown
- `BlackBoxStepEnum`: Unknown, VoyageParentNotRunnable, AssignmentFailed, NoEligibleCaptain, HarborUnavailable, MemoryPressure, DockProvision, WorktreeCreate, BranchCreate, StartCommitRecord, ProcessCrash, UsageLimit, AuthFailure, ModelUnavailable, Stall, MaxRuntime, RecoveryExhausted, Interrupted, OperatorStop, NoOp, Unfinished, CaptainBlocked, InvalidOutput, ScopeViolation, BoundaryViolation, DefinitionOfDone, StageCheck, JudgeRejected, ReviewDenied, ReviewTimeout, ReviewApprovedDockMissing, PlanParse, StageBranchHarden, DependencyCancelled, IntegrationBarrier, DockMissing, TargetIntegration, Verification, DirtyCheckout, UnpublishedAncestry, Push, PullRequest, FastForward, LocalMerge, RetryLanding, DeferralExpired, QueueResolveRepo, QueueMergeConflict, QueueTests, QueueProcessing, QueuePush, RescueFailed, RedispatchExhausted, OrphanedExit, UnconfirmedStop, MissingMergeHandoff, SettlementFailed, Halted, Cancelled, StageFailed, VesselDeleted, OperatorTransition
- `BlackBoxStorageTargetEnum`: Database, Disk, AzureBlob, GoogleCloud, Cifs, Nfs, Unknown
- `BlackBoxSubjectTypeEnum`: Mission, Voyage, Unknown
- `BlackBoxTriageEnum`: Unknown, UserConfig, MissionBrief, Transient, ArmadaDefect, External

- `BlackBoxCascadeReasonEnum`: MissionDeleted, VesselDeleted, TenantDeleted, Unknown

Each `BlackBoxStepEnum` value belongs to one phase (`BlackBoxTaxonomy.PhaseOf`); the table is in the plan, section 5.1.

### 3.8 Settings

`ArmadaSettings.BlackBox` (`BlackBoxSettings`, JSON `blackBox`): `Enabled` (true), `CaptureTimeoutMs` (2000, 100 to
30000), `Storage` (`BlackBoxStorageSettings`: `Target` (Database), `KeyPrefix` (armada/blackbox), `Compress` (true),
`MaxEvidenceObjectBytes` (1 MB, 64 KB to 16 MB), `MaxEntryBytes` (8 MB, 1 MB to 64 MB), `FallbackToDatabase` (true),
`Disk` (Directory, null for `{DataDirectory}/blackbox`), `S3` (Endpoint, Region us-east-1, Bucket, AccessKey,
SecretKey, UsePathStyle true, UseSsl true), `AzureBlob` (AccountName, Container, AccessKey, ConnectionString, Endpoint),
`GoogleCloud` (ProjectId, Bucket, CredentialsPath, Endpoint), `Cifs` (Hostname, Port 445, Share, Domain, Username,
Password, PreferEncryption true), `Nfs` (Hostname, ExportPath, Version 3 (2 to 4), Port and MountPort (null: the
portmapper), UserId 0, GroupId 0)), `Retention` (`BlackBoxRetentionSettings`: `RetentionDays` 180
(0 to 3650, 0 keeps forever), `ResolvedRetentionDays` 30, `KeepLatestPerMission` true, `MaxEntriesPerVessel` 1000,
`MaxTotalBytes` 1 GB, `DeleteWithMission` false, `DeleteWithVessel` true, `GcIntervalMinutes` 60, `GcBatchSize` 200,
`OrphanGraceHours` 24), `Sweeper` (`BlackBoxSweeperSettings`: `IntervalSeconds` 60, `PageSize` 100,
`MaxEnrichmentAttempts` 5), `SummaryEndpointId` (null). Out-of-range values are clamped in the setters. Secrets
(S3 AccessKey and SecretKey, Azure AccessKey and ConnectionString, Google CredentialsPath, CIFS Password) are masked as
`********` in `GET /api/v1/settings` and kept when written back masked through `PUT /api/v1/settings` (`blackBox`);
`BlackBoxRuntime` registers them with the redactor so they never reach an entry.

### 3.9 Objects

| Name | Content type | Written by |
|---|---|---|
| `record.json` | application/json | capture, rewritten by the pipeline |
| `brief.md` | text/markdown | agent audience, capture and pipeline |
| `brief.user.md` | text/markdown | user audience |
| `brief.maintainer.md` | text/markdown | maintainer audience |
| `evidence/agent-output.txt` | text/plain | capture (Mission.AgentOutput) |
| `evidence/diff.patch` | text/x-diff | capture (Mission.DiffSnapshot) |
| `evidence/mission.log`, `evidence/diffstat.txt`, `evidence/check-<n>.txt`, `evidence/verification-<n>.txt`, `evidence/merge-entry.txt`, `evidence/git-status.txt`, `evidence/target-commits.txt` | text/plain | enrichers (3.17) |
| `evidence/events.json` | application/json | enrichers (3.17) |
| `manifest.json` | application/json | only inside a `.zip` download (`BlackBoxDownloadManifest`) |

Names are constants in `BlackBoxObjectNames`. Text evidence is redacted and capped at `maxEvidenceObjectBytes` with the
head and tail kept around a marker line; the original size is recorded in `truncation.fields`.

### 3.10 Events

Recorded as `ArmadaEvent` rows (EntityType `blackbox`, EntityId the entry id, Payload the camelCase JSON of the typed
payload) and broadcast to the tenant's WebSocket clients (declared in `WebSocketSurface`):

| Event | Payload |
|---|---|
| `blackbox.captured` | `BlackBoxCapturedPayload` (entryId, missionId, voyageId, vesselId, phase, step, kind, attempt, captureMode) |
| `blackbox.capture_failed` | `BlackBoxCaptureFailedPayload` (missionId, voyageId, step, error) |
| `blackbox.analyzed` | `BlackBoxAnalyzedPayload` (entryId, missionId, voyageId, state, triage, rulesVersion) |
| `blackbox.resolved` | `BlackBoxResolvedPayload` (entryId, missionId, voyageId, resolution) |
| `blackbox.deleted` | `BlackBoxDeletedPayload` (entryId, missionId, voyageId, reason) |
| `blackbox.storage_fallback` | `BlackBoxStorageFallbackPayload` (entryId, from, to, error) |
| `blackbox.gc_completed` | `BlackBoxGcReport` |
| `blackbox.recovery_run` | `BlackBoxRecoveryRunPayload` (entryId, pathId, recommendationId, dryRun, succeeded, stepsRun, stepsFailed, userId); every recovery dry run and run, and every recommendation dry run and apply |

### 3.11 Redaction

`BlackBoxRedactor` rules, in order: KnownSecretValue (registered values), PrivateKeyBlock, AuthorizationHeader,
UrlCredentials, GitHubToken, AnthropicKey, OpenAiKey, AwsAccessKeyId, AwsSecretAccessKey, AzureKey, GoogleApiKey,
SlackToken, JsonWebToken, SecretPair (password, secret, token, api key, access key pairs). Matches become
`[REDACTED]` (key names and URL schemes are kept); the record's `redaction` section lists rule names and counts, never
values. Redaction runs on the record, every text evidence object and every rendered brief.

### 3.12 Tests

`Database.BlackBoxEntries`, `Database.BlackBoxObjects`, `Database.BlackBoxFiles`, `Database.BlackBoxLinksNotes` (all
four providers through `scripts/common/run-db-parity-tests.sh`), `Services.BlackBoxCapture`, `Services.BlackBoxSweeper`,
`Services.BlackBoxGarbageCollector`, `Services.BlackBoxStore`, `Services.BlackBoxService`, `Services.BlackBoxRedactor`,
`Services.BlackBoxFingerprint`, `Services.BlackBoxBriefRenderer`, `Services.BlackBoxAdvisor` (a golden case per rule,
coverage, ties, placeholders, empty record, ranking, re-analysis, audiences), `Services.BlackBoxRecovery`,
`Services.BlackBoxFixtures`, `Services.BlackBoxMaintainer` (bundle, trends), `Services.BlackBoxSummary`,
`Services.BlackBoxConfiguration`, `Models.BlackBoxSchema` (schema drift and validation). Test helpers: `BlackBoxTestKit`,
`ScriptedBlackBoxStore`, `RecordingBlackBoxEventSink`, `BlackBoxSchemaValidator`, `BlackBoxGoldenCase`,
`BlackBoxRecoveryFixture`, `RecordingBlackBoxActionExecutor`, `ScriptedBlackBoxSummaryWriter`, `FakeChatEndpoint`. After changing a model, run `scripts/common/generate-blackbox-schema.sh`.

### 3.13 Advisor and rule catalog

`BlackBoxAdvisor.Analyze(record, catalog)` is pure and deterministic. It evaluates every rule's match predicate (a
throwing predicate is no match), renders the matched rules, lets generic rules step aside for specific ones that
rendered (`YieldsTo`), and falls back to `BBX-UNKNOWN-001` when nothing rendered. Findings, recommendations and paths are
ordered by vote weight, then catalog order; recovery paths are deduplicated by their actions, ranked 1 to n and capped at
10. Triage sums the weights per verdict; the highest total wins and a tie reports `Unknown` with `tiedWith` naming the
candidates (confidence: High when the winner has at least 4 and leads by 2, Medium from 2, else Low). The summary (at
most 6 sentences) is assembled from the subject, the top two findings, the triage, the top recovery path and the first
recommendation.

Rule text is a template: `{name}` is filled from the record (`BlackBoxRulePlaceholders.Names`, plus `{config:Setting}`
for an effective configuration value) and `{name|fallback}` uses the fallback when the value is missing. A why statement
with a missing placeholder skips the rule; a recommendation, improvement or recovery path with a missing placeholder is
left out (both logged at debug). Stable ids: a recovery path is `rule id lower case + "." + key` (for example
`bbx-land-001.stash-retry`), a recommendation the same (for example `bbx-land-001.landing-mode`).

`BlackBoxRuleCatalog.CurrentRulesVersion` (1) is stored on every analyzed entry and record. Bump it when a rule's match,
text, vote or recovery changes: `BlackBoxReanalyzer` (run after each sweep) re-analyzes Analyzed entries with an older
version through `BlackBoxPipeline.ReanalyzeAsync` (advisor and summary only, no enrichers).

| Rule | Phase | Matches | Vote |
|---|---|---|---|
| BBX-LAND-001 | Landing | step DirtyCheckout with dirty paths | UserConfig 3 |
| BBX-LAND-002 | Landing | worktree missing, step DockMissing, or "could not start process ... working directory" | ArmadaDefect 3 |
| BBX-LAND-003 | Landing | conflicted files and a conflicting mission that landed with overlapping files | UserConfig 2 |
| BBX-LAND-004 | Landing | every conflicted file is generated or append-only (CHANGELOG, i18n, API surface, Postman, lock files, schemas) | ArmadaDefect 3 |
| BBX-LAND-005 | Landing | step Verification | MissionBrief 2 |
| BBX-LAND-006 | Landing | step Push, PullRequest or UnpublishedAncestry | External 2 |
| BBX-LAND-007 | Landing | any other landing failure | none |
| BBX-MQ-001 | MergeQueue | kind LandingConflict with no conflicted files | ArmadaDefect 2 |
| BBX-MQ-002 | MergeQueue | step QueueTests | MissionBrief 1 |
| BBX-MQ-003 | MergeQueue | any other merge-queue failure | none |
| BBX-SCOPE-001 | Completion | scope violation whose out-of-scope entries are blank or scoped | ArmadaDefect 4 |
| BBX-SCOPE-002 | Completion | start commit missing on a scope failure | none |
| BBX-SCOPE-003 | Completion | a real scope violation | MissionBrief 2 |
| BBX-NOOP-001 | Completion | kind or step NoOp | MissionBrief 2 |
| BBX-DEP-001 | Completion | captain BLOCKED naming unlanded prerequisites dispatched in parallel | MissionBrief 4 |
| BBX-BLOCK-001 | Completion | any other BLOCKED report | MissionBrief 2 |
| BBX-OUT-001 | Completion | invalid output | UserConfig 1 |
| BBX-BOUND-001 | Completion | boundary violation | MissionBrief 2 |
| BBX-CHECK-001 | Completion | definition of done, stage check, compile or test failure | MissionBrief 1 |
| BBX-UNFIN-001 | Completion | step Unfinished | MissionBrief 1 |
| BBX-EXEC-001 | Execution | usage limit | Transient 3 |
| BBX-EXEC-002 | Execution | stall recovery exhausted | Transient 2 |
| BBX-EXEC-003 | Execution | max runtime exceeded | MissionBrief 2 |
| BBX-EXEC-004 | Execution | authentication failure | UserConfig 3 |
| BBX-EXEC-005 | Execution | model unavailable | Transient 2 |
| BBX-EXEC-006 | Execution | process crash | Transient 1 |
| BBX-EXEC-007 | Execution | operator stop or interruption | External 3 |
| BBX-EXEC-008 | Execution | recovery exhausted | Transient 1 |
| BBX-INFRA-001 | Dispatch | Harbor disconnected or unavailable | External 3 |
| BBX-DISP-001 | Dispatch | no eligible captain, assignment failed | UserConfig 2 |
| BBX-DISP-002 | Dispatch | any other dispatch failure | Transient 1 |
| BBX-PROV-001 | Provision | any provisioning failure | Transient 1 |
| BBX-REV-001 | Review | review denied | External 2 |
| BBX-REV-002 | Review | judge rejected | MissionBrief 2 |
| BBX-REV-003 | Review | review approved but the dock is gone | ArmadaDefect 3 |
| BBX-REV-004 | Review | review timed out | External 2 |
| BBX-DEP-002 | Handoff | dependency failed or cancelled | External 1 |
| BBX-HAND-001 | Handoff | any other handoff failure | ArmadaDefect 1 |
| BBX-RECOV-001 | Recovery | Armada's recovery failed | ArmadaDefect 2 |
| BBX-VOY-001 | Voyage | a voyage entry | none |
| BBX-EXT-001 | External | vessel deleted, operator transition | External 3 |
| BBX-UNKNOWN-001 | (fallback) | nothing else rendered | none |

Recorded actions name only `BlackBoxActionCommands.SupportedTools`: `retry_landing`, `restart_mission`, `cancel_mission`,
`unstick_dock`, `repair_dock`, `update_vessel` (which now takes `landingMode`, or `Default` to clear it),
`update_mission`. `cancel_mission` and `unstick_dock` steps are data-loss steps. Each step carries the MCP call, the
REST request and, where Helm has one, the CLI command.

The optional model-written summary: when `blackBox.summaryEndpointId` is set and `Pipeline.SummaryWriter` is wired,
the pipeline asks that model endpoint (through the same PolyPrompt clients as the ApiEndpoint runtime) for 3 to 6
sentences from the record facts and the rule findings, within `SummaryTimeoutMs`, and sets `summaryIsModelWritten`.
Any failure, timeout or empty reply keeps the rules summary.

### 3.14 Recovery contract

`RunRecoveryAsync(caller, entryId, pathId, request)` and `ApplyRecommendationAsync(caller, entryId, recommendationId,
request)` run only what the entry recorded:

1. The entry must be visible to the caller; the path (or recommendation, which needs an `applyAction`) must be in the
   entry's analysis (else `BlackBoxNotFoundException`; an advice-only recommendation is `ArgumentException`).
2. Every recorded action must name a supported tool and target the entry's own mission, vessel and dock
   (`ArgumentException`).
3. Every action is authorized as if the caller called the tool directly, against its `McpToolAuthorizationRegistry`
   requirement (`UnauthorizedAccessException`, before anything runs). The executor then runs the tool through its MCP
   handler as the caller, which repeats the tool's own authorization and tenant scoping.
4. `dryRun` (the default) returns the plan (or the action), runs nothing, and issues a confirm token (`bbc_...`) bound to
   the entry, the path or recommendation, the kind, the caller, the plan's SHA-256 and the subject's version (mission or
   voyage status and last update; plus the vessel's last update for recommendations). Tokens expire after 10 minutes,
   are single use, and live in memory.
5. A confirmed call (`dryRun: false`) needs the token (`BlackBoxConfirmationException` when missing). A path with
   `dataLossRisk` also needs `acknowledgeDataLoss: true` (checked before the token is used). The token is then consumed
   whatever happens next and refused when unknown, used, expired, for another entry, path, kind or caller, when the
   subject changed since the dry run, or when the plan changed (re-analysis); each refusal asks for a new dry run.
6. Steps run in order; a manual step is reported, not run; the first failed step stops the run and the rest report
   "Not run". Tools that return a result with `success: false` (such as `retry_landing`) count as failed.
7. The entry is annotated (`blackbox_notes`, `authorKind` from the request, default `Agent`; human UIs pass `Operator`)
   and `blackbox.recovery_run` is emitted for dry runs and runs. The result carries per-step results.
8. Where no `IBlackBoxActionExecutor` is wired, confirmed calls throw `NotImplementedException`.

### 3.15 Maintainer loop: bundle, trends, fixtures

- `GetBundleAsync`: up to 50 entries (filters of `BlackBoxEnumerateRequest`, `max` clamped to 1..50), grouped by
  fingerprint, most frequent first. Markdown: the preamble asking the agent to rank Armada improvements, check them
  against the source and write each as an Armada mission brief (with a fixture test, and new rules in catalog format),
  then per group the fingerprint, first and last seen, Armada versions, triage and rule ids, the newest entry's full
  maintainer brief and one-liners for the rest (cut at 1 MB). JSON: a `BlackBoxBundleDocument` with
  `BlackBoxBundleGroup`s. `redactHosts` strips host paths; `truncated` is set when more entries matched than were
  bundled.
- `GetFingerprintTrendAsync`: counts of one fingerprint per Armada version (`BlackBoxFingerprintTrend`, versions ordered
  by first sighting, each with open and ArmadaFixed counts; at most 1000 entries and 100 versions), scoped to the caller.
- `ExportFixtureAsync`: a `BlackBoxFixture` (sanitized record plus the rule ids and triage the advisor produces now);
  `BlackBoxFixtureExporter.WriteAsync` writes `{name}.json`. Fixtures live in `src/Test.Shared/Fixtures/BlackBox/` and
  `Services.BlackBoxFixtures` checks each one still produces its expected rule ids (the first one leading) and triage,
  is sanitized, and validates against the record schema. Current fixtures: `land-001-dirty-checkout`
  (msn_mvskboi0_vJGaqsNlGqU), `land-002-dock-deleted` (msn_mvsjeweh_tmCfX52gt3z), `scope-001-false-violation` (Garrison
  msn_mvsqzal6_b1BfR4tnZ29), `dep-001-parallel-dependency-mission-2` and `-mission-4` (the bounded-enumeration voyage).

### 3.16 Effective configuration

`BlackBoxConfigurationResolver.Resolve` records, with sources: LandingMode (voyage, vessel, global, MergeAndPush),
AutoPush, AutoCreatePullRequests, AutoMergePullRequests (voyage, global), BranchCleanupPolicy (vessel, global), the
mission's RequiresReview, WaitForVoyageWorkers, DependsOnMissionId, Persona, MissionMode and Tier, the vessel's
AllowConcurrentMissions, AutoApprove, RequirePassingChecksToLand, DefinitionOfDone settings, AutoLandEnabled,
ProtectedPathPatterns and DefaultPipelineId, the workflow profile's name, BuildCommand and UnitTestCommand, TestCommand
and LintCommand (vessel override, else profile), MergeQueueTestCommand, the captain's runtime, model, endpoint,
reasoning effort and permission policy, the persona's CaptainPriorities, PriorityAvailability, PriorityExhaustion and
DefaultCaptainId, the global thresholds and attempt limits, and BlackBoxRetentionDays (vessel, global); at most 64
values. Capture resolves from the rows it holds; `BlackBoxConfigurationEnricher` adds the persona and workflow profile
values without replacing what capture recorded.

### 3.17 Enrichment (built-in enrichers)

`BlackBoxRuntime.Create` registers the built-in enrichers (`Armada.Core.BlackBox.Enrichment`,
`BlackBoxEnricherCatalog.CreateDefaults`) on the pipeline, in this order. Each derives from `BlackBoxEnricherBase`,
which catches everything but cancellation and records `"<Name> enrichment did not complete (...)"` in
`truncation.notes`, so a failing host or git command never marks the entry EnrichmentFailed.

| Enricher | Records |
|---|---|
| `Workspace` | Worktree existence, `branchExists`, branch tip, ahead and behind the target (`GetDivergenceAsync`), commits ahead of start, porcelain of the worktree and of the target checkout (`evidence/git-status.txt`, dirty paths when capture had none), conflicted files of an unfinished merge, Harbor link state and last seen, `inspectedHost`, `inspectionNote`; ChangedByMission file rows (from the diff snapshot, the worktree, or start..tip) |
| `TargetMovement` | Commits on the target between the start commit and the target tip at the failure (or the current tip, noted), up to 500 read, newest 50 in `conflict.targetCommitsSinceStart` with `targetCommitTotal` and a truncation entry; `evidence/target-commits.txt`; `workspace.targetMovementNote` says why it was omitted (no start commit, unreachable host, start commit not in the repository) |
| `ConflictAttribution` | Per target commit: `Armada-Mission-Id` trailer, changed paths intersected with the conflicted files and the mission's changed paths; `conflict.conflictingMissions` (missions first, then commits outside Armada with their author), LandedOnTargetSinceStart links, ChangedByConflictingMission rows |
| `ConcurrentOverlap` | Other missions on the vessel active during the run window that touched the same files (diff snapshot, or their branch on the owning host, at most 10 branch reads), `conflict.concurrentMissions`, OverlappingFiles links |
| `DirectoryRollup` | `conflictedDirectories` (depth 1 to 3), `conflictedParentDirectories`, `deepestConflictedDirectory` (at least two files) |
| `Attempts` | Earlier entries, stage outcomes and handoff artifacts as `attempts` (bounded 20, oldest first); subject counters (redispatch, output repair, no-op); `subject.rescueChain`; RescueOf and ParentOfRescue (`Incident.RescueMissionIds`) links |
| `Evidence` | `evidence/agent-output.txt` (tail, when capture did not store it), `evidence/mission.log` (tail), `evidence/check-<n>.txt`, `evidence/verification-<n>.txt`, `evidence/merge-entry.txt`, `evidence/diff.patch` (worktree diff when no snapshot), `evidence/diffstat.txt` and `evidence.diffStat`, `evidence/events.json` and `evidence.relevantEvents` |
| `Related` | DependencyOf and CascadeRoot links up `DependsOnMissionId`, `related.cascadeRootEntryId`, `related.downstreamMissionIds`, SiblingInVoyage and SharedDock links (fingerprint matches and similar entries are added by the pipeline) |
| `Voyage` | Voyage entries: roll-up by status, failed missions and their entries, cascade root, downstream missions, CascadeRoot and SiblingInVoyage links |
| `FileRows` | Last: re-emits the captured roles (Conflicted, DirtyInCheckout, OutOfScope, BoundaryViolation for findings that name a path) with the rows above and removes duplicates; the pipeline replaces the entry's file and link rows with these sets, so reanalyze never duplicates rows |

Host routing: enrichers inspect docks through `VesselGitInspector` (`src/Armada.Core/Services/VesselGitInspector.cs`,
shared with `VesselCleanupService`): a Harbor-hosted dock is read on its Harbor through the Harbor git request path
(`GitService` over `RemoteHostCommandExecutor`); a Harbor that is not connected resolves to no host and its facts are
recorded as unknown. `BlackBoxRuntime.Inspector` starts with the Admiral host only; the Admiral sets
`Inspector.Hosts` to its Harbor-aware `DockHostResolver`. Tests use `Inspector.HarborHostOverride` with a stand-in
host (`RecordingHostCommandExecutor`).

Git reads added for forensics (`IGitService`, default implementations answer null): `GetMergeBaseAsync(repo, a, b)`
and `GetCommitRangeAsync(repo, from, to, limit)` (`GitCommitRange`: commits newest first with author, dates, subject,
`GitTrailer` list, `MissionId` from the `Armada-Mission-Id` trailer, changed paths capped at 200 per commit,
`TotalCount`, `Truncated`; limit 1 to 500). Trailers are parsed by `GitTrailerParser`.

Bounds: tails keep the last 256 KB (`BlackBoxForensics.TailBytes`) starting at a line; per-role file rows at most 200;
every cut is a `truncation.fields` entry; the writer then redacts every text object and caps it at
`maxEvidenceObjectBytes`. Suites: `Services.BlackBoxEnrichment`, `Services.GitForensics`.

Storage and retention suites: `Database.BlackBoxStorageQueries` and `Database.BlackBoxStoreContract` (the shared
contract on the Database target, all four providers, migrating to Disk), `Services.BlackBoxStoreContract.Disk`,
`.S3` (a throwaway MinIO container), `.AzureBlob` (Azurite), `.GoogleCloud` (fake-gcs-server), `.Cifs` (in-process
OpenCIFS.Server 0.1.1) and `.Nfs` (in-process OpenNFS.Server 0.2.1, NFSv3), all built from
`BlackBoxStoreContractSuiteBase`; `Services.BlackBoxStorageTargets` (validation, masking, Disk guards, unreachable and
misconfigured stores with and without fallback, oversized evidence, doctor, migration validation, failure and restart,
backup inventory, metrics, settings changes) and `Services.BlackBoxRetention` (plan section 16.5).
`BlackBoxStorageEmulators` (`Test.Shared/Infrastructure/BlackBoxStorage`) starts each target once per process
(containers labeled `armada.test=blackbox-storage`, removed at exit) and reports a skip reason when Docker or an
image is missing, so those suites skip instead of failing; `ARMADA_TEST_NO_DOCKER=1` skips them on purpose and
`ARMADA_TEST_MINIO_IMAGE`, `ARMADA_TEST_AZURITE_IMAGE` and `ARMADA_TEST_FAKEGCS_IMAGE` pick images (defaults:
`minio/minio:latest` or `pgsty/minio`, `mcr.microsoft.com/azure-storage/azurite:3.35.0`, `fsouza/fake-gcs-server:1.56.1`).
Against fake-gcs-server the enumerate and orphan-sweep cases are skipped: the emulator writes timestamps with
trailing-zero microseconds trimmed, which Google.Apis cannot always parse, so listing fails intermittently there.

### 3.18 Retention and garbage collection

`BlackBoxRetentionPolicy.ComputeExpiry(entry, policy, vesselRetentionDays)` sets `expires_utc` at capture, on resolve
and on pin and unpin (service), on a retention policy change (`ApplySettingsChanged` starts
`GarbageCollector.StartRecompute()`), and for one vessel's entries when the vessel changes
(`IBlackBoxLifecycleHooks.OnVesselUpdatedAsync`; `PUT /api/v1/vessels/{id}` calls it when `blackBoxRetentionDays`
changed, MCP `update_vessel` on every update). `RecomputeExpiryAsync(vesselId)` scans in id order and writes only
changed expiries (idempotent).

`BlackBoxGarbageCollector.RunAsync(dryRun)` (every `gcIntervalMinutes` and on demand) runs, in order:

1. Finish deletes left Deleting (`RetriedDeletes`).
2. Expire: `expires_utc` before `Clock()`, unpinned, not the current entry of a mission or voyage that still exists when
   `keepLatestPerMission` (`Expired`).
3. Quotas (`Evicted`): for each vessel above `maxEntriesPerVessel`, the oldest unpinned non-current entries down to the
   limit; then, while the sum of `total_bytes` is above `maxTotalBytes`, the oldest unpinned resolved entries, then the
   oldest unpinned (current entries of live subjects stay with `keepLatestPerMission`).
4. Cascades (`Cascaded`): entries whose vessel row is gone (`deleteWithVessel`, default true) and Mission entries whose
   mission row is gone (`deleteWithMission`, default false). Pinned entries are kept.
5. Orphan sweep (`OrphansRemoved`): for the configured external target and every external target that holds entries,
   keys under `{keyPrefix}/` with no object row and a modification time older than `orphanGraceHours` are deleted
   (keys of unknown age and keys outside the prefix are never touched; at most `MaxOrphanKeysPerRun`, 100000).
6. Missing check (`ObjectsMissing`): a rotating page of `gcBatchSize` entries; every object whose bytes are gone from
   its store is marked missing and the entry becomes ObjectMissing.
7. Report: `LastReport`, the bytes metric, and `blackbox.gc_completed` with the counts, `BytesFreed`, `Failures`,
   `Errors` and up to 100 `CandidateEntryIds`.

Deletes are two-phase (`DeleteEntryAsync`): mark Deleting (hidden), delete external bytes with `DeleteManyAsync` in
batches of 1000, then the rows; a store failure leaves the entry Deleting for the next run. A dry run counts and samples
candidates and deletes nothing (no event). `CascadeAsync(reason, subjectId)` and the hooks run a cascade at once:
`OnMissionDeletedAsync` (REST purge and delete-multiple, MCP `purge_mission` and `delete_missions`; follows
`deleteWithMission`), `OnVesselDeletedAsync` (REST and MCP vessel deletes; follows `deleteWithVessel`, then the mission
cascade for the vessel's deleted missions), `OnTenantDeletedAsync` (tenant deletion: every entry and object, pinned
ones too). Hooks never throw; the next run catches anything they miss (WebSocket deletes, voyage purges, data expiry).
`RetentionService` and `DataExpiryService` do not touch black box tables. `MissionArchive` snapshots record the
mission's current entry as `BlackBoxEntryId`.

### 3.19 Metrics

`BlackBoxMetrics` instruments on Armada's meter, exported by the Admiral's telemetry host (Prometheus names):
`armada_blackbox_entries_total{phase,step,kind,triage}` (each capture; triage as captured),
`armada_blackbox_bytes{target}` (stored bytes per target, refreshed after each garbage collection run and each admin
storage status read), `armada_blackbox_gc_deleted_total{reason}` (expired, evicted, cascaded, retried, orphan) and
`armada_blackbox_capture_failures_total`. `MeteringBlackBoxEventSink` records them from the events it forwards.

### 3.20 Storage migration

`BlackBoxStorageMigrator` (`runtime.Migrator`): `StartAsync(request, runInBackground)`, `RunAsync(jobId)`,
`ResumePendingAsync()`, `GetAsync(id)`, `StopAsync()`, `ActiveJobId`, `OnEntryProcessedAsync` (test hook). A job
(`BlackBoxStorageMigrationJob`, `bbm_` id, persisted in `blackbox_migrations`) pages through
`ScanAsync(NeedsMigrationTo = To, StorageTarget = From)` in id order, `BatchSize` (1 to 1000) at a time, and saves its
progress and cursor (`LastEntryId`) after every entry, so it resumes after a restart (`Background.Start()`) and running
it again skips entries already moved; a job with nothing to move is created Completed. Per entry: every external
object is read, checked against its SHA-256, written to the same key on the target and read back and checked again;
database-resident objects are read, checked, copied to `{prefix}/{name}` and checked; only then is the entry switched
(`UpdateStorageAsync`), the database rows pointed at their keys (`SetLocationAsync`; their bytes cleared, or kept when
`DeleteSource` is false) and the source copies deleted. Moving to Database writes each verified object into its row
before switching the entry. A corrupt or unreadable object fails only its entry (`FailedEntries`, `LastError`), which
stays where it was; the job ends Completed, or Failed when any entry failed. Every step leaves every object readable.


## 4. TODO(blackbox-wave2) list

Grep for `TODO(blackbox-wave2` to find each placeholder in code.

| Owner | Item | Where |
|---|---|---|
| capture sites | Done: every failure write in plan 5.1 is wired (see 3.2); `VesselDeleted` waits on the vessel routes and MCP tools | `VesselRoutes`, `McpVesselTools` |
| enrichment | Done: git forensics, conflict attribution, attempts, evidence objects, configuration with sources, optional model-written summary | `BlackBoxPipeline` |
| advisor and recovery | Done: rule catalog, advisor, triage, recovery runs and recommendation applies (see 3.13 to 3.16) | `BlackBoxService` |
| CLI | `armada blackbox export-fixture <id>`: call `ExportFixtureAsync` and write it with `BlackBoxFixtureExporter.WriteAsync` into `src/Test.Shared/Fixtures/BlackBox` | Helm |
| surfaces | `GetFingerprintTrendAsync` (REST/MCP route), the recovery and recommendation tools (`run_blackbox_recovery`, `apply_blackbox_recommendation`); pass `authorKind` Operator from human UIs | REST, MCP, dashboard |
| storage and gc | Done: Blobject targets, storage migration, doctor check, storage inventory, backups, quotas, cascades, orphan sweep, expiry recompute, metrics (see 3.4 and 3.18 to 3.20) | `BlackBoxStoreFactory`, `BlackBoxStorageMigrator`, `BlackBoxGarbageCollector` |
| surfaces | REST routes, MCP tools, WebSocket commands, clients, dashboard, TUI, Helm, VS Code, mobile | call `IBlackBoxService` |
