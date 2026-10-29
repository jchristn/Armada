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
  `Capture`, `Pipeline`, `Queue`, `Sweeper`, `GarbageCollector`, `Service`, `Background`.
- The Admiral (`src/Armada.Server/ArmadaServer.cs`) builds one runtime right after `MissionService`, with
  `BuildIdentityBlackBoxVersionProvider` (BuildIdentity version and commit) and a `DatabaseBlackBoxEventSink` whose
  `OnEmitted` broadcasts each event to the tenant's WebSocket clients. It sets `MissionService.BlackBox`, starts
  `Background` (enrichment worker, sweeper every `blackBox.sweeper.intervalSeconds`, garbage collection every
  `blackBox.retention.gcIntervalMinutes`; both check `blackBox.enabled` on every tick) and stops it before the database
  is disposed. `ArmadaServer.BlackBox` exposes the runtime to routes and tests.
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
BoundaryFindings, CheckResults, MergeEntryId, IncidentId, StageOutcomeId, SuspectedCause, CauseRuleId, Note, Vessel,
Captain, Voyage, Dock, CallerMember, CallerFile, CallerLine, Component. `BlackBoxGitFacts`: ConflictedFiles, DirtyPaths,
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
the record (so renderer improvements apply to old entries) and include the ten newest notes.

Other public classes:

| Class | Role |
|---|---|
| `BlackBoxCapture` | `IBlackBoxCapture`; `Enrichment` property; `static TrimSourcePath(string?)` |
| `BlackBoxCaptureExtensions` | `RecordMissionFailureAsync`, `RecordVoyageFailureAsync` (one-line helpers) |
| `BlackBoxService` | `IBlackBoxService`; `static Summarize(BlackBoxEntry)`, `static SummarizeObject(BlackBoxObject)` |
| `BlackBoxPipeline` | `AddEnricher(IBlackBoxEnricher)`, `Advisor` (default `NoOpBlackBoxAdvisor`), `ProcessAsync(entryId)`, `LoadRecordAsync(entry)` |
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
- `BlackBoxStoreFactory(DatabaseDriver, BlackBoxSettings)`: `Get(target)`, `GetConfigured()`, `Register(target, builder)`
  (test doubles and external targets), `Reset()`. Adding a target is one `case` in `CreateBuiltIn`; unimplemented
  targets throw `NotSupportedException("The black box <Target> storage target is not yet implemented; use Database.")`,
  which the writer treats like a store failure (fallback to the database).
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
```

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
```

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

Implementation: the SQL is shared in `Armada.Core.Database.BlackBox` (`BlackBoxEntryMethodsBase`,
`BlackBoxObjectMethodsBase`, `BlackBoxFileMethodsBase`, `BlackBoxLinkMethodsBase`, `BlackBoxNoteMethodsBase`,
`BlackBoxSqlPaging`) over a per-provider `BlackBoxSqlDialect` (`SqliteBlackBoxSqlDialect`, `PostgresqlBlackBoxSqlDialect`,
`SqlServerBlackBoxSqlDialect`, `MysqlBlackBoxSqlDialect`: connection, transactional write under the provider's write
gate, timestamps, BLOB binding, boolean literals, paging, BLOB slicing). Each provider has thin `BlackBox*Methods`
classes in its `Implementations` folder. Every list pages in SQL with a COUNT over the same filter, ordered with the
id as the tie-break. A null `tenantId` means any tenant. Entries in the Deleting state are hidden unless
`includeDeleting` or `State = Deleting` is asked for. A full-row mission or voyage update never clears
`last_blackbox_entry_id` (COALESCE); `DeleteCascadeAsync` clears it.

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

Each `BlackBoxStepEnum` value belongs to one phase (`BlackBoxTaxonomy.PhaseOf`); the table is in the plan, section 5.1.

### 3.8 Settings

`ArmadaSettings.BlackBox` (`BlackBoxSettings`, JSON `blackBox`): `Enabled` (true), `CaptureTimeoutMs` (2000, 100 to
30000), `Storage` (`BlackBoxStorageSettings`: `Target` (Database), `KeyPrefix` (armada/blackbox), `Compress` (true),
`MaxEvidenceObjectBytes` (1 MB, 64 KB to 16 MB), `MaxEntryBytes` (8 MB, 1 MB to 64 MB), `FallbackToDatabase` (true),
`Disk`, `S3`, `AzureBlob`, `GoogleCloud`, `Cifs`, `Nfs`), `Retention` (`BlackBoxRetentionSettings`: `RetentionDays` 180
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
| `evidence/mission.log`, `evidence/diffstat.txt`, `evidence/check-<n>.txt`, `evidence/verification-<n>.txt`, `evidence/merge-entry.txt`, `evidence/git-status.txt`, `evidence/target-commits.txt` | text/plain | enrichers (3.13) |
| `evidence/events.json` | application/json | enrichers (3.13) |
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
| `blackbox.recovery_run` | `BlackBoxRecoveryRunPayload` (entryId, pathId, dryRun, succeeded); emitted by wave two |

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
`Services.BlackBoxFingerprint`, `Services.BlackBoxBriefRenderer`, `Models.BlackBoxSchema` (schema drift and
validation). Test helpers: `BlackBoxTestKit`, `ScriptedBlackBoxStore`, `RecordingBlackBoxEventSink`,
`BlackBoxSchemaValidator`. After changing a model, run `scripts/common/generate-blackbox-schema.sh`.

### 3.13 Enrichment (built-in enrichers)

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

## 4. TODO(blackbox-wave2) list

Grep for `TODO(blackbox-wave2` to find each placeholder in code.

| Owner | Item | Where |
|---|---|---|
| capture sites | Wire the remaining failure writes in plan section 5.1 with `RecordMissionFailureAsync` / `RecordVoyageFailureAsync` | services listed in plan 5.3 |
| enrichment | Optional model-written summary (`blackBox.summaryEndpointId`) | `BlackBoxPipeline` |
| advisor | `BlackBoxRuleCatalog` and `BlackBoxAdvisor` (starter rules, triage) | `IBlackBoxAdvisor`, `BlackBoxPipeline.Advisor` |
| recovery | `RunRecoveryAsync` and `ApplyRecommendationAsync` (dry run, confirm token, data-loss acknowledgement, annotation, `blackbox.recovery_run`) | `BlackBoxService` |
| storage | Blobject targets (Disk, S3, AzureBlob, GoogleCloud, Cifs, Nfs), storage migration job, doctor check, storage inventory | `BlackBoxStoreFactory.CreateBuiltIn`, `BlackBoxService.StartStorageMigrationAsync`, `GetStorageMigrationAsync` |
| gc | Quotas, vessel and mission cascades, orphan sweep, expiry recompute on policy change | `BlackBoxGarbageCollector.EnforceQuotasAsync`, `CascadeAsync`, `SweepOrphansAsync` |
| surfaces | REST routes, MCP tools, WebSocket commands, clients, dashboard, TUI, Helm, VS Code, mobile | call `IBlackBoxService` |
