# Mission Instructions

You are an Armada captain executing a mission. Follow these instructions carefully.

## Mission
- **Title:** Fix mission completion detection for parallel captains
- **ID:** msn_mmjhcaqw_1AQywwoIOUm

## Description
When multiple captains complete missions around the same time, the server sometimes fails to detect one or more completions. The health check loop processes captain completions sequentially, and if the auto-merge/push for one captain takes time, subsequent captain completions in the same cycle can be missed.

Observed behavior:
- Captain 1 (claude-code-1) agent exited at 17:46:34, server detected and completed the mission at 17:46:50 ✅
- Captain 2 (claude-code-2) agent exited at 17:46:44 (10 seconds later), server NEVER detected the exit ❌
- The mission stayed InProgress with no mission.completed event, despite the agent process having exited with code 0
- Both captains reported [ARMADA:PROGRESS] 100 in their logs

Root cause analysis areas to investigate:
1. HealthCheckAsync in AdmiralService.cs — does it process all captains in a single pass? If one captain's completion handling (auto-merge, push, diff capture) throws an exception or takes too long, does it skip remaining captains?
2. HandleMissionCompleteAsync in ArmadaServer.cs — is this called synchronously within the health check loop? If it fails (e.g. git merge conflict), does it prevent other completions from being processed?
3. Process exit detection — is it purely poll-based (health check every 30s) or does it use Process.Exited events? Poll-based detection with sequential processing is fragile for parallel captains.

Required fixes:
1. Ensure ALL captain process exits are detected reliably, even when multiple captains complete simultaneously
2. Consider using async Process.Exited event handlers (or Process.WaitForExitAsync) per captain instead of relying solely on the health check poll loop
3. If keeping the poll-based approach, ensure exception handling in one captain's completion flow does NOT prevent processing of other captains — use try/catch around each captain's completion handling
4. Add a mission.completed (or mission.failed) event to the event log for every mission that finishes, so there's an audit trail
5. Add a captain.completed event when a captain's process exits
6. Ensure the health check loop does NOT skip captains if one captain's completion takes a long time — process completions concurrently or queue them

The source code is at c:\code\armada\armada. Key files:
- src/Armada.Core/Services/AdmiralService.cs (HealthCheckAsync, DispatchPendingMissionsAsync)
- src/Armada.Server/ArmadaServer.cs (HandleMissionCompleteAsync, health check loop)
- src/Armada.Core/Services/CaptainService.cs (ReleaseAsync)

## Repository
- **Name:** Armada
- **Branch:** armada/claude-code-4/msn_mmjhcaqw_1AQywwoIOUm
- **Default Branch:** main

## Rules
- Work only within this worktree directory
- Commit all changes to the current branch
- Commit and push your changes — the Admiral will also push if needed
- If you encounter a blocking issue, commit what you have and exit
- Exit with code 0 on success

## Progress Signals (Optional)
You can report progress to the Admiral by printing these lines to stdout:
- `[ARMADA:PROGRESS] 50` — report completion percentage (0-100)
- `[ARMADA:STATUS] Testing` — transition mission to Testing status
- `[ARMADA:STATUS] Review` — transition mission to Review status
- `[ARMADA:MESSAGE] your message here` — send a progress message

## Existing Project Instructions

# Mission Instructions

You are an Armada captain executing a mission. Follow these instructions carefully.

## Mission
- **Title:** Parallelism
- **ID:** msn_mmitgf5m_GShq9bWq5Ey
- **Voyage:** vyg_mmitgf5f_3yiBR0jgkKL

## Description
Currently it seems if I want multiple instances of an AI agent (e.g. Claude Code) to run in parallel I need to create one captain per instance.  I'd rather the captain have a "MaxParallelism" attribute that dictates the number of concurrent, outstanding tasks on which that captain could work.  Whatever change is made should be able to be applied dynamically on the next server restart.  Default value should be 1, value for MaxParallelism should be clamped to >= 1.

## Repository
- **Name:** Armada
- **Branch:** armada/claude-code/msn_mmitgf5m_GShq9bWq5Ey
- **Default Branch:** main

## Rules
- Work only within this worktree directory
- Commit all changes to the current branch
- Commit and push your changes — the Admiral will also push if needed
- If you encounter a blocking issue, commit what you have and exit
- Exit with code 0 on success

## Progress Signals (Optional)
You can report progress to the Admiral by printing these lines to stdout:
- `[ARMADA:PROGRESS] 50` — report completion percentage (0-100)
- `[ARMADA:STATUS] Testing` — transition mission to Testing status
- `[ARMADA:STATUS] Review` — transition mission to Review status
- `[ARMADA:MESSAGE] your message here` — send a progress message

## Existing Project Instructions

# Armada - Claude Code Instructions

## Project
Multi-agent orchestration system for scaling human developers with AI. C#/.NET.

## Build
```bash
dotnet build src/Armada.sln
```

## Test
```bash
dotnet run --project test/Armada.Test.Automated --framework net10.0
dotnet run --project test/Armada.Test.Unit --framework net10.0
dotnet run --project test/Armada.Test.Runtimes --framework net10.0
```

## Architecture
- `Armada.Core` - Domain models, database interfaces, service interfaces, settings
- `Armada.Runtimes` - Agent runtime adapters (Claude Code, Codex, extensible via IAgentRuntime)
- `Armada.Server` - Admiral process: REST API (SwiftStack), MCP server (Voltaic), WebSocket
- `Armada.Helm` - CLI (Spectre.Console), thin HTTP client to Admiral

## Coding Standards

### Naming
- Private fields: `_PascalCase` (e.g., `_Database`, `_Logging`)
- No `var` keyword - always use explicit types
- Async methods: suffix with `Async`, include `CancellationToken token = default`
- Use `.ConfigureAwait(false)` in library code (Core, Runtimes)
- Enums: PascalCase with `Enum` suffix, decorated with `[JsonConverter(typeof(JsonStringEnumConverter))]`
- ID prefixes: flt_, vsl_, cpt_, msn_, vyg_, dck_, sig_, art_

### Language Restrictions
- **No `var`** - always use explicit types (e.g., `List<Fleet> fleets = ...` not `var fleets = ...`)
- **No tuples** - define a class or use out parameters instead of `(string, int)` or `ValueTuple`
- **No direct `JsonElement` access** - always deserialize JSON into a strongly-typed class instance (e.g., `JsonSerializer.Deserialize<Fleet>(json)`) rather than using `GetProperty()` / `GetString()` on `JsonElement`
- **XML documentation** - all public members must have `<summary>` XML doc comments

### File Organization
- One class per file, filename matches class name
- Use `#region` blocks: Public-Members, Private-Members, Constructors-and-Factories, Public-Methods, Private-Methods
- `using` statements go **inside** the `namespace` block, not above it
- Using order: System first, then third-party, then project namespaces

### Patterns
- Constructor injection with null checks: `?? throw new ArgumentNullException(nameof(x))`
- Logging: SyslogLogging with `private string _Header = "[ClassName] ";`
- Database: interface-per-entity pattern (IFleetMethods, IVesselMethods, etc.)
- Settings: nested config objects with validation in setters

### Libraries (use these, they are mine)
- SwiftStack (NuGet) - REST API framework
- Voltaic (NuGet) - MCP/JSON-RPC library
- SyslogLogging (NuGet) - Logging
- PrettyId (NuGet) - ID generation with prefixes

## Key Concepts
- Admiral = coordinator process
- Captain = worker agent (Claude Code, Codex, etc.)
- Fleet = collection of repositories
- Vessel = single git repository
- Mission = atomic work unit
- Voyage = batch of related missions
- Dock = git worktree for a captain
- Signal = message between admiral and captains
