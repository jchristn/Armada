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
