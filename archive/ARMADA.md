# ARMADA - Multi-Agent Orchestration for Scaled Human Development

> **Goal**: Agent/task coordination to scale human developers using AI.
> Cross-platform. C#/.NET. Built on Voltaic (MCP) and SwiftStack (REST/WebSocket).

---

## Status Legend

Each task uses a checkbox for progress tracking:

- `[ ]` Not started
- `[~]` In progress
- `[x]` Complete
- `[!]` Blocked (note reason inline)

Engineers: annotate with initials and dates as needed, e.g. `[x] JC 2026-03-05`

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Core Concepts (Glossary)](#2-core-concepts)
3. [Solution Structure](#3-solution-structure)
4. [Phase 1 - Foundation](#4-phase-1---foundation)
5. [Phase 2 - Agent Runtime](#5-phase-2---agent-runtime)
6. [Phase 3 - Coordination Layer](#6-phase-3---coordination-layer)
7. [Phase 4 - REST API (SwiftStack)](#7-phase-4---rest-api)
8. [Phase 5 - MCP Server (Voltaic)](#8-phase-5---mcp-server)
9. [Phase 6 - CLI (Spectre.Console)](#9-phase-6---cli)
10. [Phase 7 - Merge & Git Coordination](#10-phase-7---merge--git-coordination)
11. [Phase 8 - Monitoring & Observability](#11-phase-8---monitoring--observability)
12. [Phase 9 - Web Dashboard](#12-phase-9---web-dashboard)
13. [Design Decisions](#13-design-decisions)
14. [Coding Standards](#14-coding-standards)
15. [Dependencies](#15-dependencies)

---

## 1. Architecture Overview

Armada uses a **hub-and-spoke model** with a persistent coordinator process (the **Admiral**) managing multiple **Captains** (worker agents) across isolated git worktrees.

```
                         +------------------+
                         |    Human (you)    |
                         +--------+---------+
                                  |
                    CLI (Spectre.Console) / Web Dashboard
                                  |
                         +--------+---------+
                         |     Admiral      |  <-- Coordinator process
                         |  (SwiftStack)    |     REST API + WebSocket
                         |  (Voltaic MCP)   |     MCP Server (stdio/HTTP)
                         +--------+---------+
                                  |
                    +-------------+-------------+
                    |             |             |
              +-----------+ +-----------+ +-----------+
              | Captain 1 | | Captain 2 | | Captain N |
              | (Claude)  | | (Codex)   | | (Agent)   |
              +-----------+ +-----------+ +-----------+
                    |             |             |
              [worktree]    [worktree]    [worktree]
                 repo A        repo A        repo B
```

### Key Architectural Decisions

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Session management | Native process management (`System.Diagnostics.Process`) | Cross-platform (Windows Terminal, no tmux) |
| Database | SQLite via Microsoft.Data.Sqlite | Zero-install, cross-platform, proven at scale |
| CLI | Spectre.Console CLI (`armada`) | C# ecosystem, rich TUI, user's preference |
| RPC | Voltaic MCP server | User's own MCP library, standards-compliant |
| API | SwiftStack REST + WebSocket | User's own framework, OpenAPI built-in |
| Task tracking | Missions (SQLite-backed task graph) | Simpler, queryable |
| Context injection | CLAUDE.md generation + process stdin | Cross-runtime compatible |

### Data Flow

```
User Command (CLI/API/MCP)
    |
    v
Admiral receives command
    |
    +--> Creates/updates Mission in SQLite
    +--> Resolves target Fleet (repo group)
    +--> Allocates Captain (find idle or spawn)
    +--> Provisions worktree (git worktree add)
    +--> Starts agent process with mission context
    +--> Monitors via stdout/stderr + heartbeat
    |
Captain works autonomously
    |
    +--> Reports progress (file-based + process exit)
    +--> Admiral updates Mission status
    +--> On completion: push branch, create PR (optional)
    +--> Captain returns to idle pool or exits
```

---

## 2. Core Concepts

| Concept | Description |
|---------|-------------|
| **Admiral** | The persistent coordinator process. Runs the REST API, MCP server, WebSocket hub, and task scheduler. One per machine. |
| **Captain** | A worker agent instance (Claude Code, Codex, etc.) executing a mission in an isolated worktree. Has identity, sandbox, and session layers. |
| **Fleet** | A named collection of repositories under management. You might have a "NuGet OSS" fleet and a "CompanyX" fleet. |
| **Vessel** | A single git repository registered with Armada. Contains repo URL, default branch, and configuration. |
| **Mission** | An atomic unit of work assigned to a Captain. Has a description, status, priority, parent/child relationships, and belongs to a Voyage. |
| **Voyage** | A batch of related Missions tracked together. "Update FluentValidation to support X, Y, Z" becomes one Voyage with three Missions. |
| **Dock** | A git worktree provisioned for a Captain. Isolated branch, shared object store with the bare repo. Persists across Captain session restarts. |
| **Manifest** | The SQLite database storing all Armada state: Fleets, Vessels, Captains, Missions, Voyages. |
| **Signal** | A message between Admiral and Captains, or between Captains. Persistent (stored in Manifest) or ephemeral (WebSocket). |
| **Helm** | The CLI interface (`armada`). Uses Spectre.Console for rich terminal output. |
| **Bridge** | The REST API layer (SwiftStack). Enables web dashboard and external integrations. |
| **Beacon** | The MCP server layer (Voltaic). Enables AI agents to discover and use Armada as a tool. |

---

## 3. Solution Structure

```
C:\Code\Armada\
|-- Armada.sln
|-- ARMADA.md                              # This file
|-- CLAUDE.md                              # Claude Code instructions for working on Armada
|
|-- src/
|   |-- Armada.Core/                       # Domain models, interfaces, database, services
|   |   |-- Armada.Core.csproj
|   |   |-- Constants.cs
|   |   |-- Models/
|   |   |   |-- Fleet.cs
|   |   |   |-- Vessel.cs
|   |   |   |-- Captain.cs
|   |   |   |-- Mission.cs
|   |   |   |-- Voyage.cs
|   |   |   |-- Dock.cs
|   |   |   |-- Signal.cs
|   |   |   |-- AgentRuntime.cs
|   |   |   |-- ArmadaEvent.cs
|   |   |   +-- Enums/
|   |   |       |-- MissionStatusEnum.cs
|   |   |       |-- CaptainStateEnum.cs
|   |   |       |-- SignalTypeEnum.cs
|   |   |       |-- AgentRuntimeEnum.cs
|   |   |       +-- VoyageStatusEnum.cs
|   |   |-- Database/
|   |   |   |-- Interfaces/
|   |   |   |   |-- IFleetMethods.cs
|   |   |   |   |-- IVesselMethods.cs
|   |   |   |   |-- ICaptainMethods.cs
|   |   |   |   |-- IMissionMethods.cs
|   |   |   |   |-- IVoyageMethods.cs
|   |   |   |   |-- IDockMethods.cs
|   |   |   |   |-- ISignalMethods.cs
|   |   |   |   +-- IEventMethods.cs
|   |   |   |-- DatabaseDriver.cs          # Abstract base
|   |   |   +-- Sqlite/
|   |   |       +-- SqliteDatabaseDriver.cs
|   |   |-- Services/
|   |   |   |-- Interfaces/
|   |   |   |   |-- IAdmiralService.cs
|   |   |   |   |-- ICaptainService.cs
|   |   |   |   |-- IDockService.cs
|   |   |   |   |-- IMissionService.cs
|   |   |   |   |-- IVoyageService.cs
|   |   |   |   |-- IGitService.cs
|   |   |   |   +-- IAgentRuntimeService.cs
|   |   |   |-- AdmiralService.cs
|   |   |   |-- CaptainService.cs
|   |   |   |-- DockService.cs
|   |   |   |-- MissionService.cs
|   |   |   |-- VoyageService.cs
|   |   |   |-- GitService.cs
|   |   |   |-- LogRotationService.cs
|   |   |   |-- ProgressParser.cs
|   |   |   +-- AgentRuntimeService.cs
|   |   +-- Settings/
|   |       |-- ArmadaSettings.cs
|   |       +-- AgentSettings.cs
|   |
|   |-- Armada.Runtimes/                   # Agent runtime adapters (extensible)
|   |   |-- Armada.Runtimes.csproj
|   |   |-- Interfaces/
|   |   |   +-- IAgentRuntime.cs
|   |   |-- BaseAgentRuntime.cs
|   |   |-- ClaudeCodeRuntime.cs
|   |   |-- CodexRuntime.cs
|   |   +-- AgentRuntimeFactory.cs
|   |
|   |-- Armada.Server/                     # Admiral process (REST + MCP + WebSocket)
|   |   |-- Armada.Server.csproj
|   |   |-- Program.cs
|   |   |-- ArmadaServer.cs
|   |   |-- Handlers/
|   |   |   |-- FleetHandler.cs
|   |   |   |-- VesselHandler.cs
|   |   |   |-- CaptainHandler.cs
|   |   |   |-- MissionHandler.cs
|   |   |   |-- VoyageHandler.cs
|   |   |   +-- StatusHandler.cs
|   |   +-- Mcp/
|   |       +-- ArmadaMcpServer.cs
|   |
|   +-- Armada.Helm/                       # CLI (Spectre.Console)
|       |-- Armada.Helm.csproj
|       |-- Program.cs
|       |-- Commands/
|       |   |-- FleetCommands.cs
|       |   |-- VesselCommands.cs
|       |   |-- MissionCommands.cs
|       |   |-- VoyageCommands.cs
|       |   |-- CaptainCommands.cs
|       |   |-- StatusCommands.cs
|       |   |-- ConfigCommands.cs
|       |   |-- LogCommand.cs
|       |   |-- WatchCommand.cs
|       |   +-- McpCommands.cs
|       +-- Rendering/
|           |-- TableRenderer.cs
|           |-- StatusRenderer.cs
|           +-- DashboardRenderer.cs
|
+-- test/
    |-- Armada.Test.Core/                # Unit tests for models, database, services
    |   |-- Armada.Test.Core.csproj
    |   |-- Models/
    |   |-- Database/
    |   |-- Services/
    |   +-- TestHelpers/
    |-- Armada.Test.Runtimes/            # Unit tests for agent runtime adapters
    |   +-- Armada.Test.Runtimes.csproj
    |-- Armada.Test.Server/              # Unit tests for REST API, MCP server
    |   +-- Armada.Test.Server.csproj
    +-- Armada.Test.Integration/         # End-to-end integration tests
        +-- Armada.Test.Integration.csproj
```

---

## 4. Phase 1 - Foundation

> Core models, database, and project scaffolding.

### 4.1 Project Scaffolding

- [x] Create `Armada.sln` solution file
- [x] Create `Armada.Core` class library (net8.0;net10.0)
- [x] Create `Armada.Runtimes` class library (net8.0;net10.0)
- [x] Create `Armada.Server` console app (net8.0;net10.0)
- [x] Create `Armada.Helm` console app (net8.0;net10.0) with `<OutputType>Exe</OutputType>`
- [x] Create test projects
- [x] Add `CLAUDE.md` with project-specific instructions
- [x] Configure `Directory.Build.props` for shared settings (TargetFrameworks, NoWarn, Version, Authors)
- [x] Configure suppressed warnings per coding standards (CS1998, CS8600, CS8602, CS8603, CS8604, CS8618, CS8625, CS0108, CS8601, CS0618)
- [x] Add NuGet package references (see [Dependencies](#15-dependencies))

### 4.2 Domain Models

All models follow the established pattern: PascalCase properties, `_PascalCase` backing fields, XML documentation, `CreatedUtc`/`LastUpdateUtc` timestamps, nullable enabled.

- [x] `Fleet` - Id (string, prefix `flt_`), Name, Description, Active, CreatedUtc
- [x] `Vessel` - Id (`vsl_`), FleetId, Name, RepoUrl, LocalPath, DefaultBranch, Active, CreatedUtc
- [x] `Captain` - Id (`cpt_`), Name, Runtime (enum), State (enum), CurrentMissionId, CurrentDockId, ProcessId, LastHeartbeatUtc, CreatedUtc
- [x] `Mission` - Id (`msn_`), VoyageId, VesselId, CaptainId, Title, Description, Status (enum), Priority, ParentMissionId, BranchName, PrUrl, CreatedUtc, StartedUtc, CompletedUtc
- [x] `Voyage` - Id (`vyg_`), Title, Description, Status (enum), CreatedUtc, CompletedUtc
- [x] `Dock` - Id (`dck_`), VesselId, CaptainId, WorktreePath, BranchName, Active, CreatedUtc
- [x] `Signal` - Id (`sig_`), FromCaptainId, ToCaptainId, Type (enum), Payload (JSON string), Read, CreatedUtc
- [x] `AgentRuntime` - Id (`art_`), Name, Command, Args, PromptMode, ResumeSupported, Active
- [x] `ArmadaStatus` - Aggregate status model with VoyageProgress

### 4.3 Enumerations

- [x] `MissionStatusEnum` - Pending, Assigned, InProgress, Testing, Review, Complete, Failed, Cancelled
- [x] `CaptainStateEnum` - Idle, Working, Stalled, Stopping
- [x] `SignalTypeEnum` - Assignment, Progress, Completion, Error, Heartbeat, Nudge, Mail
- [x] `AgentRuntimeEnum` - ClaudeCode, Codex, Custom
- [x] `VoyageStatusEnum` - Open, InProgress, Complete, Cancelled

### 4.4 Database Layer

SQLite via Microsoft.Data.Sqlite. Follow the interface/implementation pattern from existing repos.

- [x] Define `DatabaseDriver` abstract base class with interface properties
- [x] Define `IFleetMethods` - CreateAsync, ReadAsync, ReadByNameAsync, UpdateAsync, DeleteAsync, EnumerateAsync, ExistsAsync
- [x] Define `IVesselMethods` - CRUD + EnumerateByFleetAsync
- [x] Define `ICaptainMethods` - CRUD + EnumerateByStateAsync, UpdateStateAsync, UpdateHeartbeatAsync
- [x] Define `IMissionMethods` - CRUD + EnumerateByVoyageAsync, EnumerateByVesselAsync, EnumerateByCaptainAsync, EnumerateByStatusAsync
- [x] Define `IVoyageMethods` - CRUD + EnumerateByStatusAsync
- [x] Define `IDockMethods` - CRUD + EnumerateByVesselAsync, FindAvailableAsync
- [x] Define `ISignalMethods` - CreateAsync, ReadAsync, EnumerateByRecipientAsync, EnumerateRecentAsync, MarkReadAsync
- [x] Implement `SqliteDatabaseDriver` with all interface implementations
- [x] Schema migration system (versioned SQL scripts: `SchemaMigration` class, `schema_migrations` tracking table, transactional migration application, `GetSchemaVersionAsync()`, idempotent re-initialization, v1 = initial schema)
- [x] Database indexing: add simple and compound indexes informed by query patterns (status filters, foreign key lookups, time-range queries)
- [x] Data expiry: background task to purge old completed voyages, missions, signals, and events (configurable retention period)
- [x] Seed data for default agent runtimes (Claude Code, Codex) via ArmadaSettings defaults

### 4.5 Settings

- [x] `ArmadaSettings` - DataDirectory (default: `~/.armada/`), DatabasePath, LogDirectory, AdmiralPort (default: 7890), McpPort (default: 7891)
- [x] `AgentSettings` - per-runtime configuration (command, args, environment variables, maxConcurrent)
- [x] Settings loader: JSON file at `~/.armada/settings.json` with LoadAsync/SaveAsync
- [x] Settings validation (all properties validated in setters: ports 1-65535, intervals >= 5s, thresholds >= 1, non-negative counts)

---

## 5. Phase 2 - Agent Runtime

> Extensible agent runtime adapters. Interface + implementation for each supported agent.

### 5.1 Runtime Interface

- [x] `IAgentRuntime` interface:
  ```csharp
  public interface IAgentRuntime
  {
      string Name { get; }
      AgentRuntimeEnum RuntimeType { get; }
      bool SupportsResume { get; }

      Task<int> StartAsync(
          string workingDirectory,
          string prompt,
          Dictionary<string, string> environment,
          CancellationToken token = default);

      Task StopAsync(int processId, CancellationToken token = default);

      Task<bool> IsRunningAsync(int processId, CancellationToken token = default);

      Task<string> GetStatusAsync(int processId, CancellationToken token = default);
  }
  ```

### 5.2 Base Implementation

- [x] `BaseAgentRuntime` abstract class:
  - Process lifecycle management via `System.Diagnostics.Process`
  - stdout/stderr capture and forwarding
  - Heartbeat detection (process alive = heartbeat)
  - Graceful shutdown (stdin close, then SIGTERM/kill after timeout)
  - Cross-platform process management (Windows + Linux + macOS)

### 5.3 Claude Code Runtime

- [x] `ClaudeCodeRuntime : BaseAgentRuntime`
  - Command: `claude` (or configurable path)
  - Args: `--dangerously-skip-permissions` (configurable), `--print` for non-interactive, or interactive with stdin piping
  - Prompt injection: pass mission description via `--prompt` or stdin
  - CLAUDE.md generation: write mission-specific CLAUDE.md into worktree before launch
  - Session resume support: `--resume` flag
  - Environment: `CLAUDE_CODE_DISABLE_NONINTERACTIVE_HINT=1`

### 5.4 Codex Runtime

- [x] `CodexRuntime : BaseAgentRuntime`
  - Command: `codex` (or configurable path)
  - Args: `--approval-mode full-auto` (configurable)
  - Prompt injection: pass via positional arg
  - Non-interactive by default

### 5.5 Runtime Factory

- [x] `AgentRuntimeFactory` - resolves `IAgentRuntime` by `AgentRuntimeEnum` or custom name
- [x] Registration of custom runtimes at startup (from settings)

---

## 6. Phase 3 - Coordination Layer

> The Admiral's brain: services that orchestrate Captains, Missions, and Docks.

### 6.1 Admiral Service

- [x] `IAdmiralService` / `AdmiralService`:
  - `DispatchVoyageAsync(string title, string description, List<MissionRequest> missions)` - create Voyage + Missions, auto-assign
  - `DispatchMissionAsync(MissionRequest request)` - create and assign single Mission
  - `GetStatusAsync()` - aggregate status across all active work
  - `RecallCaptainAsync(string captainId)` - stop a Captain gracefully
  - `RecallAllAsync()` - emergency stop all Captains
  - Periodic health check loop (configurable interval, default 30s)

### 6.2 Captain Service

- [x] `ICaptainService` / `CaptainService` (extracted from AdmiralService):
  - [x] `SpawnAsync(AgentRuntimeEnum runtime, string name)` - register a new Captain identity (via REST API)
  - [x] `AssignAsync(string captainId, string missionId)` - assign Mission, provision Dock, start agent (in TryAssignMissionAsync)
  - [x] `ReleaseAsync(string captainId)` - mark idle after Mission completion (in HandleMissionCompletionAsync)
  - [x] `MonitorAsync(string captainId)` - check process health, update state (in HealthCheckAsync)
  - [x] `EnumerateIdleAsync()` - find available Captains (via database)
  - [x] Captain pool management: MinIdleCaptains/MaxCaptains settings, auto-spawn in HealthCheckAsync
  - [x] Extract into standalone CaptainService class (RecallAsync, TryRecoverAsync, ReleaseAsync)

### 6.3 Dock Service

- [x] `IDockService` / `DockService` (extracted from AdmiralService):
  - [x] `ProvisionAsync(Vessel, Captain, string branchName)` - create git worktree + dock record
  - [x] `ReclaimAsync(string dockId)` - remove worktree
  - [x] `RepairAsync(string dockId)` - fix corrupted worktree (via GitService.RepairWorktreeAsync)
  - [x] `EnumerateByVesselAsync(string vesselId)` - list active worktrees (via database)
  - [x] Worktree directory: `~/.armada/docks/{vesselName}/{captainName}/`

### 6.4 Mission Service

- [x] `IMissionService` / `MissionService` (extracted from AdmiralService):
  - [x] Full CRUD (via database + REST API)
  - [x] State machine: Pending -> Assigned -> InProgress -> Complete/Failed
  - [x] Auto-assignment: TryAssignAsync — find idle Captain, provision Dock, start agent
  - [x] Progress tracking: parse agent output for status signals (ProgressParser + OnOutputReceived)
  - [x] Completion handling: HandleCompletionAsync — push branch, create PR via `gh` CLI
  - [x] Failure handling: mark Failed, reclaim Captain, log error
  - [x] Testing and Review status transitions (PUT /api/v1/missions/{id}/status with state machine validation)
  - [x] Broad-scope mission detection (IsBroadScope)
  - [x] CLAUDE.md generation for worktrees (GenerateClaudeMdAsync)

### 6.5 Voyage Service

- [x] `IVoyageService` / `VoyageService` (extracted from AdmiralService):
  - [x] Create Voyage with missions
  - [x] Track aggregate progress: GetProgressAsync (N of M missions complete)
  - [x] Auto-close when all child Missions are Complete (CheckCompletionsAsync)
  - [x] Status summary for CLI/API consumption

### 6.6 Git Service

- [x] `IGitService` / `GitService`:
  - [x] `CloneBareAsync(string repoUrl, string localPath)` - initial clone as bare repo
  - [x] `CreateWorktreeAsync(string repoPath, string worktreePath, string branchName)` - `git worktree add`
  - [x] `RemoveWorktreeAsync(string worktreePath)` - `git worktree remove`
  - [x] `PushBranchAsync(string worktreePath, string remoteName)` - push Captain's work
  - [x] `CreatePullRequestAsync(string worktreePath, string title, string body)` - via `gh pr create`
  - [x] `FetchAsync(string repoPath)` - update from remote
  - [x] `IsRepositoryAsync(string path)` - check if valid git repo
  - [x] All operations via `System.Diagnostics.Process` calling `git` CLI (cross-platform)

---

## 7. Phase 4 - REST API

> SwiftStack-based REST API for the Admiral. Enables web dashboard and external tool integration.

### 7.1 Server Setup

- [x] `ArmadaServer` class:
  - [x] Initialize `SwiftStackApp` with name "Armada"
  - [x] Configure logging (SyslogLogging)
  - [x] Configure OpenAPI/Swagger (`/swagger`, `/openapi.json`)
  - [x] Authentication: API key via `X-Api-Key` header (configurable, optional for local use)
  - [x] CORS: enabled for dashboard (via post-routing response headers)
  - [x] Start on configurable port (default 7890)

### 7.2 REST Endpoints

All endpoints return JSON. Follow SwiftStack patterns: `AppRequest` -> handler -> object response.

**Fleets**
- [x] `GET    /api/v1/fleets` - list all fleets
- [x] `POST   /api/v1/fleets` - create fleet
- [x] `GET    /api/v1/fleets/{id}` - get fleet details
- [x] `PUT    /api/v1/fleets/{id}` - update fleet
- [x] `DELETE /api/v1/fleets/{id}` - delete fleet

**Vessels**
- [x] `GET    /api/v1/vessels` - list all vessels (optional `?fleetId=` filter)
- [x] `POST   /api/v1/vessels` - register vessel (repo URL + fleet)
- [x] `GET    /api/v1/vessels/{id}` - get vessel details
- [x] `PUT    /api/v1/vessels/{id}` - update vessel
- [x] `DELETE /api/v1/vessels/{id}` - deregister vessel

**Voyages**
- [x] `POST   /api/v1/voyages` - create voyage with missions
- [x] `GET    /api/v1/voyages` - list voyages (optional `?status=` filter)
- [x] `GET    /api/v1/voyages/{id}` - get voyage with mission details
- [x] `DELETE /api/v1/voyages/{id}` - cancel voyage (cancels pending missions too)

**Missions**
- [x] `GET    /api/v1/missions` - list missions (filters: status, vessel, captain, voyage)
- [x] `POST   /api/v1/missions` - create standalone mission (auto-assigns via DispatchMissionAsync)
- [x] `GET    /api/v1/missions/{id}` - get mission details with Captain/Dock info
- [x] `PUT    /api/v1/missions/{id}` - update mission
- [x] `DELETE /api/v1/missions/{id}` - cancel mission

**Captains**
- [x] `GET    /api/v1/captains` - list all captains with state
- [x] `POST   /api/v1/captains` - register captain identity
- [x] `GET    /api/v1/captains/{id}` - get captain details + current mission
- [x] `POST   /api/v1/captains/{id}/stop` - gracefully stop captain
- [x] `DELETE /api/v1/captains/{id}` - deregister captain (recalls if working)

**Status**
- [x] `GET    /api/v1/status` - aggregate dashboard: active voyages, missions by status, captain states, recent signals
- [x] `GET    /api/v1/status/health` - health check (for monitoring)

**Signals**
- [x] `GET    /api/v1/signals` - list recent signals (optional filters)
- [x] `POST   /api/v1/signals` - send a signal

### 7.3 WebSocket Hub

- [x] SwiftStack WebSocket server on configurable port (default 7892, ArmadaSettings.WebSocketPort)
- [x] Routes:
  - `subscribe` - subscribe to real-time events (sends initial status snapshot, receives all broadcasts)
  - `command` - send commands to Admiral (status, stop_captain, stop_all)
- [x] Broadcast mission state changes to all subscribers (via EmitEventAsync -> ArmadaWebSocketHub.BroadcastEvent)
- [x] Broadcast captain state changes to all subscribers (BroadcastCaptainChange/BroadcastMissionChange helpers)

### 7.4 OpenAPI Documentation

- [x] Full OpenAPI 3.0 metadata on all endpoints (tags, summaries, descriptions, parameters, request bodies, responses, security on all 31 routes)
- [x] Swagger UI enabled at `/swagger` (SwiftStack auto-registers `/swagger` and `/openapi.json` via `UseOpenApi()`)
- [x] Tags: Fleets, Vessels, Voyages, Missions, Captains, Status, Signals, Events (8 tags with descriptions)
- [x] Request/response schemas with examples (typed request bodies via `Json<T>()`, response schemas via `Json<T>()`, path/query parameters with descriptions, API key security scheme)

---

## 8. Phase 5 - MCP Server

> Voltaic-based MCP server so AI agents can discover and use Armada as a tool.

### 8.1 MCP Server Setup

- [x] `ArmadaMcpServer` using Voltaic's `McpHttpServer` (integrated into ArmadaServer)
- [x] Register as MCP tool provider
- [x] HTTP transport on configurable port (default 7891)
- [x] Stdio transport for direct Claude Code integration — `armada mcp stdio` command using Voltaic McpServer (v0.1.8), shared McpToolRegistrar, embedded database/services, `mcp install` shows both HTTP and stdio configs

### 8.2 MCP Tools

Each tool is registered via `RegisterTool()` with JSON schema for parameters.

- [x] `armada_status` - Get aggregate status of all active work
- [x] `armada_dispatch` - Dispatch a new Voyage or Mission
  - Input: `{ title, description, vessel, missions: [{ title, description }] }`
- [x] `armada_mission_status` - Get status of a specific Mission
  - Input: `{ missionId }`
- [x] `armada_voyage_status` - Get status of a Voyage with all child Missions
  - Input: `{ voyageId }`
- [x] `armada_list_fleets` - List registered fleets
- [x] `armada_list_captains` - List captains with current state
- [x] `armada_list_vessels` - List registered vessels
- [x] `armada_stop_captain` - Stop a specific Captain
  - Input: `{ captainId }`
- [x] `armada_stop_all` - Emergency stop all Captains
- [x] `armada_send_signal` - Send a message to a Captain
  - Input: `{ captainId, message }`

### 8.3 Claude Code Integration

- [x] Generate `claude_desktop_config.json` snippet for MCP server registration
- [x] Generate `.claude/settings.json` snippet for Claude Code MCP integration
- [x] `armada mcp install` CLI command to auto-configure (with --dry-run)

---

## 9. Phase 6 - CLI

> Spectre.Console-based CLI. The `armada` command (or `ar` alias).

### 9.1 CLI Framework

- [x] Spectre.Console.Cli `CommandApp` with command branches
- [x] `TypeRegistrar` for DI (TypeRegistrar/TypeResolver in Infrastructure/, constructor injection support, instance/lazy/type registration, wired into CommandApp)
- [x] Global options: `--json` (machine-readable output via BaseSettings)
- [x] HTTP client to communicate with Admiral REST API (CLI is a thin client via BaseCommand)
- [x] Fallback: if Admiral not running, start embedded (single-process mode) — EmbeddedServer static manager + BaseCommand auto-detection via health check; Helm references Armada.Server for in-process hosting

### 9.2 Commands

**Fleet Management**
- [x] `armada fleet list` - table of fleets
- [x] `armada fleet add <name>` - create fleet
- [x] `armada fleet remove <name>` - delete fleet

**Vessel Management**
- [x] `armada vessel list [--fleet <name>]` - table of vessels
- [x] `armada vessel add <name> <repo-url> [--fleet <name>] [--branch <b>]` - register vessel
- [x] `armada vessel remove <name>` - deregister vessel

**Voyages**
- [x] `armada voyage create <title> --mission "desc" --mission "desc"` - create voyage
- [x] `armada voyage list [--status <status>]` - list voyages
- [x] `armada voyage show <id>` - detailed view with mission list
- [x] `armada voyage cancel <id>` - cancel voyage

**Missions**
- [x] `armada mission create <title> --vessel <name> [--voyage <id>] [--description <d>] [--priority <p>]` - create mission
- [x] `armada mission list [--status <s>] [--vessel <v>] [--captain <c>] [--voyage <v>]` - list missions
- [x] `armada mission show <id>` - detailed view
- [x] `armada mission cancel <id>` - cancel mission (with confirmation)

**Captain Management**
- [x] `armada captain list` - table with state, current mission, runtime (with emojis + color)
- [x] `armada captain add <name> [--runtime claude|codex|custom]` - register
- [x] `armada captain stop <id>` - graceful recall
- [x] `armada captain stop-all` - emergency recall with confirmation

**Quick Dispatch (the dream UX)**
- [x] `armada go "<prompt>" --vessel <name>` - one-shot dispatch
  - [x] Resolves vessel by name
  - [x] Creates single-mission Voyage
  - [x] Returns immediately with Voyage ID
  - Example: `armada go "Add retry logic to the HTTP client" --vessel FluentValidation`
  - [x] Multi-task detection (split numbered lists and semicolon-separated tasks into multiple Missions)

**Status & Monitoring**
- [x] `armada status` - rich dashboard with figlet banner, emojis, color-coded tables
  - [x] Active Voyages with progress bars (█░ blocks)
  - [x] Captain states (color-coded + emojis)
  - [x] Mission breakdown by status
  - [x] Recent signals
- [x] `armada watch` - live-updating status (Spectre.Console `Live` display, with --interval option)
- [x] `armada log <captain-id>` - tail Captain's output (with --follow and --lines options)

**Configuration**
- [x] `armada config show` - display current settings
- [x] `armada config set <key> <value>` - update setting
- [x] `armada config init` - interactive first-time setup

**Server Management**
- [x] `armada server start` - auto-start Admiral server (finds project, launches dotnet run)
- [x] `armada server status` - check if Admiral is reachable
- [x] `armada server stop` - stop Admiral (via POST /api/v1/server/stop)

**MCP**
- [x] `armada mcp install` - configure MCP integration for Claude Code

### 9.3 Output Rendering

- [x] Rich table rendering with Spectre.Console (inline in each command)
- [x] Color-coded status with emojis throughout
- [x] Figlet banner for help screen and status dashboard
- [x] Dedicated `TableRenderer` helper class (Rendering/TableRenderer.cs: CreateTable factory, color/emoji helpers for CaptainState, MissionStatus, VoyageStatus, SignalType, AgentRuntime; adopted by all 8 command files)
- [x] All commands support `--json` for machine-readable output (via BaseSettings + IsJsonMode/WriteJson helpers)

---

## 10. Phase 7 - Merge & Git Coordination

> Safe multi-agent git operations. Prevents conflicts, manages branches, optionally creates PRs.

### 10.1 Branch Strategy

- [x] Each Captain gets a unique branch: `armada/{captainName}/{missionId}`
- [x] Branches created from latest `origin/{defaultBranch}`
- [x] Fetch before branch creation to minimize drift
- [x] Captain works exclusively on their branch (no cross-branch operations)

### 10.2 Conflict Prevention

- [x] Mission assignment checks: broad-scope mission detection defers conflicting assignments (MissionService.IsBroadScope + TryAssignAsync)
- [x] Vessel-level lock for operations that touch broad paths (e.g., "refactor entire project") — IsBroadScopeMission blocks concurrent assignments
- [x] Warning when multiple Missions target the same Vessel simultaneously (logged in TryAssignMissionAsync)

### 10.3 Completion Flow

- [x] Captain process exit code 0 detected as completion (HealthCheckAsync)
- [x] Push branch to remote (HandleMissionCompleteAsync in ArmadaServer)
- [x] Create PR via `gh pr create` with mission context
- [x] PR body includes: Mission description, mission ID
- [x] Reclaim Dock (remove worktree) after push — in HandleMissionCompleteAsync

### 10.4 Merge Queue

- [x] Bors-style batch merge
- [x] Run tests on merged branch before landing
- [x] Binary bisection on test failure
- [x] Integration branch support for large features
- [x] REST API routes (`/api/v1/merge-queue`)
- [x] MergeEntry model, MergeStatusEnum, IMergeQueueService interface
- [x] MergeQueueTestCommand setting

---

## 11. Phase 8 - Monitoring & Observability

> Know what your fleet is doing at all times.

### 11.1 Health Monitoring

- [x] Captain heartbeat: process alive check on configurable interval (default 30s)
- [x] Stall detection: no heartbeat update for configurable duration (default 10min)
- [x] Zombie detection: Captain process dead but Mission still marked InProgress (exit code check)
- [x] Auto-recovery: restart stalled Captains with context restoration (configurable MaxRecoveryAttempts, worktree repair, CLAUDE.md regeneration)

### 11.2 Logging

- [x] SyslogLogging with `_Header` pattern per class
- [x] Per-Captain log files: `~/.armada/logs/captains/{captainId}.log`
- [x] Admiral log: `~/.armada/logs/admiral.log`
- [x] Log rotation (configurable max size/count via LogRotationService, runs in health check loop)

### 11.3 Events

- [x] Event stream: all state changes emitted as events (ArmadaEvent model, EmitEventAsync in ArmadaServer)
- [x] WebSocket broadcast for real-time consumers (all EmitEventAsync calls broadcast to WebSocket clients via ArmadaWebSocketHub)
- [x] Event log in SQLite for historical queries (events table with IEventMethods, full CRUD)
- [x] Filterable by: event type, captain, mission, vessel, voyage (GET /api/v1/events with query filters)

### 11.4 Escalation

- [x] Configurable escalation rules via EscalationRule settings (trigger, threshold, action, cooldown)
- [x] Triggers: CaptainStalled, MissionOverdue, MissionFailed, RecoveryExhausted, PoolExhausted
- [x] Notification channels: Log (file), Webhook (HTTP POST to Slack/Discord/Teams)
- [x] Cooldown to prevent notification spam (configurable per rule)
- [x] IEscalationService / EscalationService with EvaluateAsync (time-based) and FireAsync (event-based)
- [x] Integrated into AdmiralService.HealthCheckAsync

---

## 12. Phase 9 - Web Dashboard

> Browser-based UI for monitoring and control. Built on the REST API.

### 12.1 Approach

- [x] Static SPA served by SwiftStack (embedded resources in Armada.Server.dll, served via DefaultRoute)
- [x] Framework: htmx + Alpine.js (no build step, no Node.js dependency)
- [x] WebSocket for real-time updates (connects to ArmadaWebSocketHub, auto-reconnect)
- [x] Typed API client (`ArmadaApiClient`) covering all endpoints with full model types

### 12.2 Views

- [x] **Dashboard Home** - aggregate status cards (captains, voyages, missions), voyage progress bars, recent signals
- [x] **Fleet View** - fleets with nested vessel tables (name, repo URL, branch)
- [x] **Voyage Detail** - voyage list with inline mission expansion (status, captain, branch)
- [x] **Captain Detail** - captain table with state, runtime, mission, PID, recall action
- [x] **Dispatch** - form to create new Mission with title, description, vessel selection

### 12.3 Deferred Decisions

- [x] Frontend framework: htmx + Alpine.js (decided)
- [x] Authentication for web dashboard (API key via localStorage, login overlay, logout, 401 detection)
- [x] Mobile responsiveness (CSS media queries for narrow screens)

---

## 13. Design Decisions

### SQLite vs. Git-Backed State

**Decision: SQLite for structured state, git worktrees for code isolation.**

| Factor | SQLite | Git-Backed (Dolt/Beads) |
|--------|--------|------------------------|
| Cross-platform | Excellent | Dolt has Windows issues |
| Zero-install | Yes (embedded) | Requires Dolt server |
| Query flexibility | Full SQL | Limited |
| Schema evolution | Migrations | Complex |
| Concurrent access | WAL mode handles it | Git merge semantics |
| Complexity | Low | High |

Git is still used for what it's best at: code isolation via worktrees. Structured state (missions, voyages, captains) lives in SQLite where it can be queried efficiently.

### Process Management vs. tmux

**Decision: Native `System.Diagnostics.Process` management.**

tmux provides session persistence, multiplexing, and detachment on Unix. On Windows, these concepts map differently:

- **Session persistence**: Armada achieves this through the Admiral process keeping handles to child processes, plus SQLite state that survives Admiral restarts.
- **Multiplexing**: Windows Terminal natively supports multiple tabs/panes.
- **Detachment**: The Admiral runs as a background process; Captains are child processes of the Admiral.

This gives us true cross-platform support without requiring tmux, WSL, or any Unix-specific tooling.

### Context Restoration Strategy

**Decision: CLAUDE.md generation + mission-specific prompt injection.**

Armada takes a straightforward approach:

1. Before launching a Captain, write a `CLAUDE.md` file into the worktree with:
   - Mission description and objectives
   - Relevant Voyage context
   - Repository-specific instructions (from Vessel config)
   - "You are a Captain in the Armada fleet. Your mission ID is {id}."
2. Pass the mission description as the initial prompt.
3. On restart/resume: regenerate CLAUDE.md with current progress state, re-launch.

This is simpler and works with any agent runtime that reads CLAUDE.md or accepts prompts.

### Single-Process vs. Multi-Process

**Decision: Admiral runs as a standalone process; CLI is a thin HTTP client.**

- `armada server start` launches the Admiral (REST + MCP + WebSocket + scheduler).
- `armada <command>` sends HTTP requests to the Admiral.
- If Admiral isn't running, CLI can start it automatically or run in embedded mode for simple operations.
- This enables: web dashboard, MCP integration, and multiple CLI sessions all hitting the same coordinator.

---

## 14. Coding Standards

Follow conventions established across existing repositories (partio, recalldb, verbex, assistanthub, chronos, coachesgpt):

### Naming
- **Namespace**: `Armada.Core`, `Armada.Core.Models`, `Armada.Core.Database.Sqlite`
- **Classes**: PascalCase (`CaptainService`, `SqliteDatabaseDriver`)
- **Interfaces**: `I` prefix (`ICaptainService`, `IMissionMethods`)
- **Private fields**: `_PascalCase` (`_Database`, `_Logging`, `_Settings`)
- **Public properties**: PascalCase with explicit getters/setters where validation needed
- **Methods**: PascalCase, `Async` suffix for async methods
- **Parameters**: camelCase
- **Enums**: PascalCase with `Enum` suffix, `[JsonConverter(typeof(JsonStringEnumConverter))]`
- **ID prefixes**: `flt_`, `vsl_`, `cpt_`, `msn_`, `vyg_`, `dck_`, `sig_`, `art_`

### Structure
- `#region Public-Members` / `#region Private-Members` / `#region Constructors-and-Factories` / `#region Public-Methods` / `#region Private-Methods`
- Using statements: System first, then third-party, then project
- One class per file, filename matches class name
- XML documentation on all public members
- `<Nullable>enable</Nullable>` project-wide

### Async
- All async methods accept `CancellationToken token = default`
- Use `.ConfigureAwait(false)` in library code
- `Async` suffix on all async method names

### Logging
- SyslogLogging with `_Header` pattern: `private string _Header = "[ClassName] ";`
- `_Logging.Info(_Header + "message");`

### Dependencies
- Constructor injection with null validation: `_Database = database ?? throw new ArgumentNullException(nameof(database));`
- No `var` keyword - always explicit types

### Project Files
```xml
<PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

---

## 15. Dependencies

### Armada.Core
| Package | Purpose |
|---------|---------|
| Microsoft.Data.Sqlite | SQLite database access |
| SyslogLogging | Structured logging |
| PrettyId | ID generation with prefixes |
| System.Text.Json | Serialization (built-in) |

### Armada.Runtimes
| Package | Purpose |
|---------|---------|
| (references Armada.Core) | Domain models and interfaces |

### Armada.Server
| Package | Purpose |
|---------|---------|
| SwiftStack | REST API + WebSocket + OpenAPI |
| Voltaic | MCP server (stdio + HTTP) |
| (references Armada.Core, Armada.Runtimes) | |

### Armada.Helm
| Package | Purpose |
|---------|---------|
| Spectre.Console | Rich terminal output |
| Spectre.Console.Cli | Command-line argument parsing |
| System.Net.Http.Json | HTTP client for Admiral API |
| (references Armada.Core) | Models for deserialization |

### Test Projects
| Package | Purpose |
|---------|---------|
| (references target project) | |

---

## Build & Run (Target State)

```bash
# First time setup
dotnet build
armada config init          # Interactive setup wizard
armada server start         # Start Admiral

# Register your fleet
armada fleet add "OSS"
armada vessel add FluentValidation https://github.com/you/FluentValidation --fleet OSS
armada vessel add Serilog https://github.com/you/Serilog --fleet OSS

# Register captains
armada captain add claude-1 --runtime claude
armada captain add claude-2 --runtime claude
armada captain add codex-1 --runtime codex

# Dispatch work
armada go "Add conditional validation groups and update docs" --vessel FluentValidation
armada go "Add retry logic to HTTP sink" --vessel Serilog

# Monitor
armada status               # Rich dashboard
armada watch                # Live updating
armada voyage list          # See all voyages

# Check on specific work
armada voyage show vyg_abc12
armada log claude-1         # Tail captain's output
```

### Testing

```bash
# Run all tests (both net8.0 and net10.0)
dotnet test src/Armada.sln

# Run specific test project
dotnet test test/Armada.Test.Core/Armada.Test.Core.csproj
dotnet test test/Armada.Test.Runtimes/Armada.Test.Runtimes.csproj

# Run with verbose output
dotnet test src/Armada.sln --verbosity normal

# Run a specific test class
dotnet test src/Armada.sln --filter "FullyQualifiedName~MissionDatabaseTests"

# Run a specific test method
dotnet test src/Armada.sln --filter "FullyQualifiedName~TryParse_ProgressSignal_ParsesPercentage"
```

#### Test Structure

```
test/
|-- Directory.Build.props              # Shared xUnit package references
|-- Armada.Test.Core/                  # Core library tests
|   |-- Models/                        # Model construction, defaults, serialization
|   |-- Database/                      # SQLite CRUD, FK constraints, concurrency
|   |-- Services/                      # ProgressParser, LogRotation, DataExpiry, Git, Settings
|   +-- TestHelpers/                   # TestDatabase helper (temp-file SQLite)
|-- Armada.Test.Runtimes/              # Runtime adapter tests
|-- Armada.Test.Server/                # REST API endpoint tests (59 tests)
|   |-- Routes/                        # Fleet, Vessel, Captain, Mission, Voyage, Signal, Event, Status, Auth
|   +-- TestHelpers/                   # TestServerInstance (ephemeral server on random port)
+-- Armada.Test.Integration/           # End-to-end workflow tests (8 tests)
    +-- TestHelpers/                   # IntegrationTestServer with convenience methods
```

#### Conventions

- **Test database**: Use `TestDatabaseHelper.CreateDatabaseAsync()` which creates a temp-file SQLite database. Wrap in `using` — it auto-deletes on dispose.
- **Naming**: `MethodUnderTest_Scenario_ExpectedResult` (e.g., `TryParse_Null_ReturnsNull`)
- **No `var`**: Explicit types in test code, matching production standards
- **Assertions**: Use xUnit `Assert.*` methods. Prefer specific assertions (`Assert.Equal`, `Assert.Single`) over generic ones.

---

## 13a. Testing

> Comprehensive test suites for every component. Run all tests with `dotnet test src/Armada.sln`.

### 13a.1 Test Project Scaffolding

- [x] Create `Armada.Test.Core` xUnit project with references to Armada.Core
- [x] Create `Armada.Test.Runtimes` xUnit project with references to Armada.Runtimes
- [x] Create `Armada.Test.Server` xUnit project with references to Armada.Server
- [x] Create `Armada.Test.Integration` xUnit project with references to all projects
- [x] Add all test projects to `Armada.sln`
- [x] Add shared test utilities (test database helper, fixture classes)

### 13a.2 Core Model Tests (`Armada.Test.Core`)

- [x] Fleet model: ID generation (flt_ prefix), default values, serialization round-trip
- [x] Vessel model: ID generation (vsl_ prefix), default values, serialization round-trip
- [x] Captain model: ID generation (cpt_ prefix), default values, state enum serialization
- [x] Mission model: ID generation (msn_ prefix), default values, status enum serialization
- [x] Voyage model: ID generation (vyg_ prefix), default values, status enum serialization
- [x] Dock model: ID generation (dck_ prefix), default values, serialization round-trip
- [x] Signal model: ID generation (sig_ prefix), default values, type enum serialization
- [x] ArmadaEvent model: ID generation (evt_ prefix), default values, serialization
- [x] ArmadaStatus model: aggregate construction, VoyageProgress calculations
- [x] All enums: JSON string serialization/deserialization for MissionStatusEnum, CaptainStateEnum, SignalTypeEnum, AgentRuntimeEnum, VoyageStatusEnum

### 13a.3 Database Tests (`Armada.Test.Core`)

- [x] SqliteDatabaseDriver: InitializeAsync creates all tables, WAL mode enabled, foreign keys enabled
- [x] Fleet CRUD: Create, Read, ReadByName, Update, Delete, Enumerate, Exists
- [x] Vessel CRUD: Create, Read, ReadByName, Update, Delete, Enumerate, EnumerateByFleet, Exists
- [x] Captain CRUD: Create, Read, ReadByName, Update, Delete, Enumerate, EnumerateByState, UpdateState, UpdateHeartbeat, Exists
- [x] Mission CRUD: Create, Read, Update, Delete, Enumerate, EnumerateByVoyage, EnumerateByVessel, EnumerateByCaptain, EnumerateByStatus, Exists
- [x] Voyage CRUD: Create, Read, Update, Delete, Enumerate, EnumerateByStatus, Exists
- [x] Dock CRUD: Create, Read, Update, Delete, Enumerate, EnumerateByVessel, FindAvailable, Exists
- [x] Signal CRUD: Create, Read, EnumerateByRecipient (unread filter), EnumerateRecent, MarkRead
- [x] Event methods: Create, EnumerateRecent, EnumerateByType, EnumerateByEntity, EnumerateByCaptain, EnumerateByMission, EnumerateByVessel, EnumerateByVoyage
- [x] Foreign key constraints: cascade deletes, set null on delete
- [x] Concurrent access: WAL mode handles simultaneous reads/writes
- [x] Data expiry service: purges old records correctly, respects retention period

### 13a.4 Service Tests (`Armada.Test.Core`)

- [x] ProgressParser: parse PROGRESS signal (percentage clamping 0-100), STATUS signal (enum parsing), MESSAGE signal, invalid/empty input returns null, case insensitivity
- [x] LogRotationService: rotation triggers at size threshold, file shifting (.1->.2->.3), overflow cleanup, no-op when under threshold, directory scan
- [x] GitService: argument validation, null/empty checks, IsRepositoryAsync with nonexistent paths
- [x] AdmiralService: DispatchVoyageAsync, DispatchMissionAsync, GetStatusAsync, RecallCaptainAsync, RecallAllAsync, argument validation, status aggregation
- [x] Settings: ArmadaSettings defaults, AgentSettings defaults, JSON load/save round-trip, validation

### 13a.5 Runtime Tests (`Armada.Test.Runtimes`)

- [x] BaseAgentRuntime: process start/stop lifecycle, IsRunning check, stdout/stderr capture, OnOutputReceived event firing, graceful shutdown sequence
- [x] ClaudeCodeRuntime: correct command/args construction, environment variables, prompt injection, CLAUDE.md generation flag
- [x] CodexRuntime: correct command/args construction, approval mode setting
- [x] AgentRuntimeFactory: resolve by enum (ClaudeCode, Codex), resolve custom runtime, registration/deregistration

### 13a.6 Server Tests (`Armada.Test.Server`)

- [x] REST endpoint tests for all Fleet routes (GET/POST/PUT/DELETE)
- [x] REST endpoint tests for all Vessel routes (GET/POST/PUT/DELETE)
- [x] REST endpoint tests for all Captain routes (GET/POST/DELETE, POST stop)
- [x] REST endpoint tests for all Mission routes (GET/POST/PUT/DELETE)
- [x] REST endpoint tests for all Voyage routes (GET/POST/DELETE)
- [x] REST endpoint tests for Status routes (GET status, GET health)
- [x] REST endpoint tests for Signal routes (GET/POST)
- [x] REST endpoint tests for Event routes (GET with filters)
- [x] Mission status transition validation (PUT /api/v1/missions/{id}/status) — valid and invalid transitions
- [x] API key authentication: requests with valid key, invalid key, missing key
- [x] CORS headers: verify post-routing response headers
- [x] MCP server: tool registration, tool execution for all armada_* tools (12 tests: tools/list verification, description/schema checks, execution of all 10 tools, nonexistent tool error, REST→MCP data consistency)

### 13a.7 Integration Tests (`Armada.Test.Integration`)

- [x] End-to-end: create fleet → register vessel → create captain → dispatch mission → verify mission lifecycle transitions → verify completion → verify events
- [x] Multi-step voyage: create voyage with multiple missions → verify mission linking → cancel voyage → verify cascading cancellation
- [x] Mission status transitions: verify valid transitions succeed, invalid transitions rejected
- [x] Signal flow: send signal to captain, verify receipt in signal list
- [x] Captain lifecycle: create → stop → delete → verify gone
- [x] Fleet/vessel hierarchy: delete fleet → verify vessel survives with null FleetId
- [x] Status dashboard: verify captain counts and mission breakdown with multiple entities
- [x] Event filtering: verify events can be queried by missionId

### 13a.8 Test Documentation

- [x] Document test running instructions in ARMADA.md (Build & Run section)
- [x] Document test structure and conventions
- [x] Document how to add new tests

---

## 13b. Test Completeness Review

> Final pass to ensure all tests are exhaustive and cover edge cases.

- [x] Review all test projects for coverage gaps
- [x] Add edge case tests: empty inputs, null handling, boundary values, concurrent operations
- [x] Add negative tests: invalid IDs, duplicate names, invalid state transitions, malformed requests
- [x] Verify all public API methods have corresponding tests (17/18 → 18/18 after adding HealthCheckAsync tests)
- [x] Verify all database operations have corresponding tests (8/8 entities + init + FK + concurrent)
- [x] Verify all REST endpoints have corresponding tests (31/32 — server/stop excluded as admin-only)
- [x] Run full test suite and verify 100% pass rate (710 tests, 100% pass, net8.0 + net10.0)
- [x] Document any known limitations or untestable areas (git operations test input validation only; MCP tool results use .ToString() via Voltaic — tests verify execution success, not response parsing)

---

## 13c. Getting Started Guide

> User-facing walkthrough demonstrating all Armada capabilities.

- [x] Create GETTING_STARTED.md with:
  - [x] Prerequisites (dotnet SDK, git, claude/codex CLI)
  - [x] Installation and first build
  - [x] Configuration (`armada config init`)
  - [x] Starting the Admiral server (`armada server start`)
  - [x] Registering a fleet and vessels
  - [x] Registering captains with different runtimes
  - [x] Quick dispatch with `armada go`
  - [x] Creating voyages with multiple missions
  - [x] Monitoring with `armada status` and `armada watch`
  - [x] Viewing captain logs (`armada log`)
  - [x] Using the REST API directly (curl examples)
  - [x] Using the MCP server with Claude Code (`armada mcp install`)
  - [x] Signals and inter-captain communication
  - [x] Troubleshooting common issues (escalation rules, health check logging, captain stall detection)

---

## Phase Execution Order

| Phase | Description | Dependencies | Priority |
|-------|-------------|-------------|----------|
| 1 | Foundation (models, DB, scaffolding) | None | **Must have** |
| 2 | Agent Runtime (process management) | Phase 1 | **Must have** |
| 3 | Coordination Layer (Admiral brain) | Phase 1, 2 | **Must have** |
| 4 | REST API (SwiftStack) | Phase 3 | **Must have** |
| 5 | MCP Server (Voltaic) | Phase 3 | **Must have** |
| 6 | CLI (Spectre.Console) | Phase 4 | **Must have** |
| 7 | Merge & Git Coordination | Phase 3 | **Should have** |
| 8 | Monitoring & Observability | Phase 3, 4 | **Should have** |
| 9 | Web Dashboard | Phase 4 | **Nice to have** |

Phases 4, 5, and 6 can be developed in parallel once Phase 3 is complete.

---

*Armada: Scale your fleet. Command your agents. Ship your code.*
