# Armada C# Refactoring Plan

**Date:** 2026-03-15
**Scope:** Split large files into smaller, focused classes (no partial classes)

---

## Overview

The Armada codebase contains **438 C# files** totaling **67,622 lines**. Ten source files exceed 480 lines and are candidates for refactoring. This plan focuses on the **top 6 production code files** (excluding test files, which naturally grow large).

| Priority | File | Lines | Proposed Split |
|----------|------|-------|----------------|
| P0 | `ArmadaServer.cs` | 3,569 | 5 new classes |
| P0 | `McpToolRegistrar.cs` | 2,369 | 6 new classes |
| P1 | `ArmadaWebSocketHub.cs` | 1,132 | 4 new classes |
| P1 | `AdmiralService.cs` | 988 | 3 new classes |
| P2 | `MergeQueueService.cs` | 651 | 2 new classes |
| P2 | `ArmadaApiClient.cs` | 625 | Base + 6 client classes |
| P2 | `MissionService.cs` | 538 | 3 new classes |
| P2 | `SqliteDatabaseDriver.cs` | 520 | 2 new classes |
| P3 | `GitService.cs` | 488 | 4 new classes |
| P3 | `ArmadaSettings.cs` | 480 | 4 nested settings classes |

---

## P0: ArmadaServer.cs (3,569 lines)

**Problem:** This god-class handles server lifecycle, agent process management, mission completion workflows, merge polling, health checks, and event emission all in one file.

### Proposed Split

#### 1. `ArmadaServer.cs` (~350 lines) — Slim Coordinator
Keep only:
- Fields and constructor
- `StartAsync()` / `Stop()` lifecycle methods
- Service wiring (connecting callbacks to handler classes)

#### 2. `AgentProcessManager.cs` (~300 lines) — NEW
Extract from `HandleLaunchAgentAsync()`, `HandleAgentProcessExitedAsync()`, `HandleStopAgentAsync()`:
```
class AgentProcessManager
    + LaunchAgentAsync(Captain, Mission, Dock) : Task<int>
    + HandleProcessExitedAsync(int processId, int exitCode) : Task
    + StopAgentAsync(Captain) : Task
    - _ProcessToCaptain : Dictionary<int, string>
    - _ProcessToMission : Dictionary<int, string>
```
Dependencies: `AgentRuntimeFactory`, `IAdmiralService`, logging

#### 3. `MissionCompletionHandler.cs` (~450 lines) — NEW
Extract from `HandleCaptureDiffAsync()`, `HandleMissionCompleteAsync()`:
```
class MissionCompletionHandler
    + CaptureDiffAsync(Mission, Dock) : Task
    + HandleMissionCompleteAsync(Mission, Dock) : Task
    - CreatePullRequestAsync(...) : Task
    - CommitAndPushAsync(...) : Task
```
Dependencies: `IGitService`, `DatabaseDriver`, settings

#### 4. `VoyageLandingHandler.cs` (~250 lines) — NEW
Extract from `HandleVoyageCompleteAsync()`, `HandleReconcilePullRequestAsync()`, `PollAndPullAfterMergeAsync()`:
```
class VoyageLandingHandler
    + HandleVoyageCompleteAsync(Voyage) : Task
    + HandleReconcilePullRequestAsync(Mission) : Task<bool>
    + PollAndPullAfterMergeAsync(Mission, MergeEntry) : Task
    - _VesselMergeLocks : ConcurrentDictionary<string, SemaphoreSlim>
```
Dependencies: `IGitService`, `IMergeQueueService`, `LandingService`

#### 5. `ServerHealthMonitor.cs` (~150 lines) — NEW
Extract from `HealthCheckLoopAsync()`:
```
class ServerHealthMonitor
    + StartAsync(CancellationToken) : Task
    - HealthCheckLoopAsync() : Task
    - _HealthCheckCycles : int
    - _StartUtc : DateTime
```
Dependencies: `IAdmiralService`, logging

#### 6. `ServerEventEmitter.cs` (~100 lines) — NEW
Extract from `EmitEventAsync()`:
```
class ServerEventEmitter
    + EmitEventAsync(EventType, string entityId, string detail) : Task
```
Dependencies: `DatabaseDriver`, `ArmadaWebSocketHub`

### Migration Strategy
1. Create new classes with interfaces where appropriate
2. Inject them into `ArmadaServer` constructor
3. Replace inline handler code with delegation calls
4. Wire callbacks in `StartAsync()`

---

## P0: McpToolRegistrar.cs (2,369 lines)

**Problem:** Single static class with 12 `Register*` methods, each containing inline lambda handlers with significant logic. The file is essentially a monolithic tool registry.

### Proposed Split

The `RegisterAll()` orchestrator and `RegisterToolDelegate` stay in `McpToolRegistrar.cs`. Each tool group becomes its own static class:

| New File | Extracted From | ~Lines |
|----------|---------------|--------|
| `McpToolRegistrar.cs` | `RegisterAll()`, delegate | ~80 |
| `Tools/StatusTools.cs` | `RegisterStatusTools()` | ~30 |
| `Tools/EnumerateTools.cs` | `RegisterEnumerateTools()` | ~210 |
| `Tools/FleetTools.cs` | `RegisterFleetTools()` | ~140 |
| `Tools/VesselTools.cs` | `RegisterVesselTools()` | ~190 |
| `Tools/VoyageTools.cs` | `RegisterVoyageTools()` | ~260 |
| `Tools/MissionTools.cs` | `RegisterMissionTools()` | ~440 |
| `Tools/CaptainTools.cs` | `RegisterCaptainTools()` | ~240 |
| `Tools/SignalTools.cs` | `RegisterSignalTools()` | ~70 |
| `Tools/EventTools.cs` | `RegisterEventTools()` | ~70 |
| `Tools/DockTools.cs` | `RegisterDockTools()` | ~110 |
| `Tools/MergeQueueTools.cs` | `RegisterMergeQueueTools()` | ~240 |
| `Tools/BackupTools.cs` | `RegisterBackupTools()` | ~310 |

### Pattern
Each tool file is a static class with one public `Register()` method:
```csharp
// Tools/FleetTools.cs
namespace Armada.Server.Mcp.Tools;

internal static class FleetTools
{
    public static void Register(RegisterToolDelegate register, DatabaseDriver database)
    {
        // ... fleet tool registrations
    }
}
```

`McpToolRegistrar.RegisterAll()` becomes:
```csharp
public static void RegisterAll(RegisterToolDelegate register, ...)
{
    StatusTools.Register(register, admiral, onStop);
    EnumerateTools.Register(register, database, mergeQueue);
    FleetTools.Register(register, database);
    // ...
}
```

### Migration Strategy
1. Create `Mcp/Tools/` directory
2. Move each `Register*` method into its own file as a static class
3. Update `RegisterAll()` to delegate to each tool class
4. No interface changes — callers still use `McpToolRegistrar.RegisterAll()`

---

## P1: ArmadaWebSocketHub.cs (1,132 lines)

**Problem:** The `RegisterRoutes()` method contains a massive switch statement with ~67 command cases, making it a 900-line method.

### Proposed Split

#### 1. `ArmadaWebSocketHub.cs` (~250 lines) — Slim Hub
Keep:
- Constructor, fields, `StartAsync()`
- Broadcast methods (`BroadcastMissionChange`, `BroadcastVoyageChange`, etc.)
- Route registration that dispatches to handler classes
- File reading utilities

#### 2. `Commands/WebSocketCommandRouter.cs` (~100 lines) — NEW
A dispatcher that routes command names to the appropriate handler:
```csharp
class WebSocketCommandRouter
{
    public async Task<object?> HandleCommandAsync(string command, JsonElement? args)
    {
        return command switch
        {
            "list_fleets" or "get_fleet" or ... => await _fleetHandler.HandleAsync(command, args),
            "list_missions" or "get_mission" or ... => await _missionHandler.HandleAsync(command, args),
            // ...
        };
    }
}
```

#### 3. `Commands/FleetVesselCommandHandler.cs` (~200 lines) — NEW
Handles: `list_fleets`, `get_fleet`, `create_fleet`, `update_fleet`, `delete_fleet`, `list_vessels`, `get_vessel`, `create_vessel`, `update_vessel`, `delete_vessel`, `update_vessel_context`

#### 4. `Commands/MissionVoyageCommandHandler.cs` (~300 lines) — NEW
Handles: all mission and voyage commands (19 total)

#### 5. `Commands/CaptainSignalCommandHandler.cs` (~200 lines) — NEW
Handles: captain, signal, event, dock, merge queue, backup, enumerate commands

### Migration Strategy
1. Create `WebSocket/Commands/` directory
2. Define `IWebSocketCommandHandler` interface
3. Extract switch cases into handler classes
4. Hub dispatches via router, keeps broadcast logic

---

## P1: AdmiralService.cs (988 lines)

**Problem:** Orchestration service handling dispatch, status, captain control, and health monitoring. While well-structured internally, it's approaching 1,000 lines.

### Proposed Split

#### 1. `AdmiralService.cs` (~300 lines) — Slim Orchestrator
Keep:
- Constructor, fields, callback properties
- `GetStatusAsync()` — status aggregation
- Top-level delegation to sub-services

#### 2. `DispatchService.cs` (~300 lines) — NEW
Extract:
```
class DispatchService : IDispatchService
    + DispatchVoyageAsync(DispatchRequest) : Task<Voyage>
    + DispatchMissionAsync(MissionTemplate) : Task<Mission>
    - ValidateDispatchAsync(...) : Task
    - BuildMissionsAsync(...) : Task<List<Mission>>
```
Implements sequential dispatch logic, dependency ordering, and mission template expansion.

#### 3. `CaptainControlService.cs` (~400 lines) — NEW
Extract:
```
class CaptainControlService : ICaptainControlService
    + RecallCaptainAsync(string captainId) : Task
    + RecallAllAsync() : Task
    + HealthCheckAsync() : Task
    + HandleProcessExitAsync(int pid, int exitCode) : Task
    + CleanupStaleCaptainsAsync() : Task
```
All captain lifecycle management outside of initial assignment.

### Migration Strategy
1. Define `IDispatchService` and `ICaptainControlService` interfaces
2. `AdmiralService` takes both as constructor dependencies
3. Move private helper methods that support dispatch/control into respective classes
4. Update DI registration in `ArmadaServer`

---

## P2: MergeQueueService.cs (651 lines)

**Problem:** Merge queue processing mixes queue management with git operations and test execution.

### Proposed Split

#### 1. `MergeQueueService.cs` (~350 lines) — Queue Management
Keep:
- `EnqueueAsync()`, `CancelAsync()`, `ListAsync()`, `GetAsync()`, `DeleteAsync()`
- `ProcessQueueAsync()`, `ProcessSingleAsync()`
- `ProcessGroupSafeAsync()`, `ProcessGroupAsync()`, `ProcessEntryAsync()`

#### 2. `MergeLandingExecutor.cs` (~300 lines) — NEW
Extract git and landing operations:
```
class MergeLandingExecutor : IMergeLandingExecutor
    + MergeBranchAsync(MergeEntry, string repoPath) : Task<bool>
    + RunTestsAsync(MergeEntry, string repoPath) : Task<bool>
    + LandEntryAsync(MergeEntry) : Task
    + CleanupWorktreeAsync(MergeEntry) : Task
    - RunGitAsync(string repoPath, string args) : Task<(int, string)>
    - GetRepoPathAsync(MergeEntry) : Task<string>
```

---

## P2: ArmadaApiClient.cs (625 lines)

**Problem:** Single client class with 48 methods covering all entity types.

### Proposed Split

#### 1. `ArmadaApiClient.cs` (~100 lines) — Facade
Keep the public-facing class but delegate to sub-clients:
```csharp
public class ArmadaApiClient : IDisposable
{
    public FleetApiClient Fleets { get; }
    public VesselApiClient Vessels { get; }
    public MissionApiClient Missions { get; }
    public VoyageApiClient Voyages { get; }
    public CaptainApiClient Captains { get; }
    public MergeQueueApiClient MergeQueue { get; }
}
```

#### 2. `ArmadaApiClientBase.cs` (~100 lines) — NEW
Extract shared HTTP helpers:
```
abstract class ArmadaApiClientBase
    # GetAsync<T>(string path) : Task<T>
    # PostAsync<TResp, TBody>(string path, TBody body) : Task<TResp>
    # PutAsync<TResp, TBody>(string path, TBody body) : Task<TResp>
    # DeleteAsync(string path) : Task
```

#### 3–8. Entity-specific clients (~70-90 lines each) — NEW
- `FleetApiClient.cs` — Fleet CRUD
- `VesselApiClient.cs` — Vessel CRUD
- `MissionApiClient.cs` — Mission CRUD + status transitions
- `VoyageApiClient.cs` — Voyage CRUD + dispatch
- `CaptainApiClient.cs` — Captain CRUD + control
- `MergeQueueApiClient.cs` — Queue operations

**Note:** Existing callers using `client.ListFleetsAsync()` would need to change to `client.Fleets.ListAsync()`. If backward compatibility is needed, keep pass-through methods on `ArmadaApiClient` that delegate to sub-clients.

---

## P2: MissionService.cs (538 lines)

**Problem:** Assignment pipeline, completion handling, scope analysis, and documentation generation in one class.

### Proposed Split

#### 1. `MissionService.cs` (~200 lines) — Coordinator
Keep:
- Fields, constructor, callback properties
- `TryAssignAsync()` — but delegate sub-steps to helpers

#### 2. `MissionCompletionHandler.cs` (~200 lines) — NEW
Extract:
```
class MissionCompletionHandler
    + HandleCompletionAsync(Captain, Mission, int exitCode) : Task
    + HandleCompletionAsync(string captainId, string missionId, int exitCode) : Task
```

#### 3. `MissionDocumentGenerator.cs` (~150 lines) — NEW
Extract:
```
class MissionDocumentGenerator
    + GenerateClaudeMdAsync(Mission, Vessel, Dock) : Task<string>
    + IsBroadScope(string prompt) : bool
```

---

## P2: SqliteDatabaseDriver.cs (520 lines)

**Problem:** Driver mixes connection management, schema migration, type conversion utilities, and row mappers.

### Proposed Split

#### 1. `SqliteDatabaseDriver.cs` (~200 lines) — Connection & Lifecycle
Keep:
- Constructor, connection setup
- `InitializeAsync()`, `GetSchemaVersionAsync()`, `Dispose()`
- Entity method collection initialization

#### 2. `SqliteRowMapper.cs` (~220 lines) — NEW
Extract all `*FromReader()` methods:
```
internal static class SqliteRowMapper
    + FleetFromReader(SqliteDataReader) : Fleet
    + VesselFromReader(SqliteDataReader) : Vessel
    + CaptainFromReader(SqliteDataReader) : Captain
    + MissionFromReader(SqliteDataReader) : Mission
    + VoyageFromReader(SqliteDataReader) : Voyage
    + DockFromReader(SqliteDataReader) : Dock
    + SignalFromReader(SqliteDataReader) : Signal
    + EventFromReader(SqliteDataReader) : Event
    + MergeEntryFromReader(SqliteDataReader) : MergeEntry
```

#### 3. `SqliteTypeConverter.cs` (~80 lines) — NEW
Extract conversion helpers:
```
internal static class SqliteTypeConverter
    + ToIso8601(DateTime) : string
    + FromIso8601(string) : DateTime
    + GetNullableString(SqliteDataReader, int) : string?
    + GetNullableDateTime(SqliteDataReader, int) : DateTime?
    + GetNullableInt(SqliteDataReader, int) : int?
```

---

## P3: GitService.cs (488 lines)

**Problem:** All git operations in one class covering worktrees, branches, PRs, merges, and repository inspection.

### Proposed Split

#### 1. `GitService.cs` (~120 lines) — Facade implementing `IGitService`
Delegates to focused managers. Keeps `IGitService` interface intact for callers.

#### 2. `GitWorktreeManager.cs` (~120 lines) — NEW
```
class GitWorktreeManager
    + CreateWorktreeAsync(...) : Task<string>
    + RemoveWorktreeAsync(...) : Task
    + RepairWorktreeAsync(...) : Task
    + PruneWorktreesAsync(...) : Task
    + IsWorktreeRegisteredAsync(...) : Task<bool>
```

#### 3. `GitBranchManager.cs` (~120 lines) — NEW
```
class GitBranchManager
    + PushBranchAsync(...) : Task
    + DeleteLocalBranchAsync(...) : Task
    + DeleteRemoteBranchAsync(...) : Task
    + BranchExistsAsync(...) : Task<bool>
    + MergeBranchLocalAsync(...) : Task<bool>
```

#### 4. `GitPullRequestManager.cs` (~80 lines) — NEW
```
class GitPullRequestManager
    + CreatePullRequestAsync(...) : Task<string>
    + EnableAutoMergeAsync(...) : Task
    + IsPrMergedAsync(...) : Task<bool>
```

#### 5. `GitProcessRunner.cs` (~80 lines) — NEW
```
internal class GitProcessRunner
    + RunGitAsync(string repoPath, string args) : Task<(int, string, string)>
    + RunProcessAsync(string exe, string args, string workDir) : Task<(int, string, string)>
```

All managers receive `GitProcessRunner` via constructor injection.

---

## P3: ArmadaSettings.cs (480 lines)

**Problem:** Flat settings class with 37 properties, validation logic, and mixed concerns.

### Proposed Split

Don't create separate files — instead, group related properties into nested settings classes that already exist in the codebase pattern:

#### 1. `ArmadaSettings.cs` (~200 lines) — Top-level
Keep: paths, ports, API key, runtime, and references to sub-settings.

#### 2. `MonitoringSettings.cs` (~80 lines) — NEW
```
class MonitoringSettings
    + HeartbeatIntervalSeconds : int
    + StallThresholdMinutes : int
    + MaxRecoveryAttempts : int
```

#### 3. `LandingSettings.cs` (~80 lines) — NEW
```
class LandingSettings
    + LandingMode : LandingMode
    + BranchCleanupPolicy : BranchCleanupPolicy
    + AutoPush : bool
    + AutoCreatePullRequests : bool
    + AutoMergePullRequests : bool
    + MaxLandingRetries : int
```

#### 4. `CaptainPoolSettings.cs` (~80 lines) — NEW
```
class CaptainPoolSettings
    + MinIdleCaptains : int
    + MaxCaptains : int
    + IdleCaptainTimeoutSeconds : int
```

#### 5. `LoggingRetentionSettings.cs` (~60 lines) — NEW
```
class LoggingRetentionSettings
    + MaxLogFileSizeBytes : long
    + MaxLogFileCount : int
    + DataRetentionDays : int
```

**Note:** This is a **breaking change** to the settings JSON schema. Requires a migration path (accept both flat and nested formats during a transition period, or bump settings version).

---

## Execution Order

### Phase 1 (P0) — Highest Impact
1. **McpToolRegistrar.cs** — Mechanical extraction, low risk, no interface changes
2. **ArmadaServer.cs** — Requires new interfaces, medium risk

### Phase 2 (P1) — Medium Impact
3. **ArmadaWebSocketHub.cs** — Follows same pattern as McpToolRegistrar
4. **AdmiralService.cs** — Requires new DI interfaces

### Phase 3 (P2) — Lower Impact
5. **MergeQueueService.cs** — Straightforward extraction
6. **ArmadaApiClient.cs** — API surface change, needs backward compat
7. **MissionService.cs** — Small, focused extraction
8. **SqliteDatabaseDriver.cs** — Internal-only change

### Phase 4 (P3) — Nice to Have
9. **GitService.cs** — Facade pattern, medium effort
10. **ArmadaSettings.cs** — Breaking config change, defer or version

---

## Principles

1. **No partial classes** — each class is a complete, single-file unit
2. **Interface-first** — extract interfaces before moving implementations
3. **Constructor injection** — new classes receive dependencies via constructors
4. **One refactor per PR** — each file split is its own pull request for safe review
5. **Test coverage first** — ensure existing tests pass before and after each split
6. **No behavior changes** — pure structural refactoring, no logic changes
