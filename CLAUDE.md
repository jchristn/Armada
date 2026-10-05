# Armada - Claude Code Instructions

## Project
Multi-agent orchestration system for scaling human developers with AI. C#/.NET.

## Using Armada's MCP Tools
When using Armada MCP tools, use `enumerate` with a small pageSize (10-25) to conserve context. Use filters (vesselId, status, date ranges) to narrow results. Only set include flags (includeDescription, includeContext, includeTestOutput, includePayload, includeMessage, includeOutput) to true when you specifically need that data; by default, large fields are excluded and length hints are returned instead.

## Build
```bash
dotnet build src/Armada.sln
```

## Test
```bash
# Console runner (all suites; net8.0 or net10.0)
dotnet run --project src/Test.Automated --framework net10.0

# Same suites via the xUnit / NUnit adapters
dotnet test src/Test.Xunit --framework net10.0
dotnet test src/Test.Nunit --framework net10.0

# Dashboard (src/Armada.Dashboard): node_modules is not committed; install it per machine
npm ci && npm run build && npm run test:run
```

The dashboard's `dist/` is committed on purpose (deploy scripts fall back to it when Node is not
installed). Rebuild and commit it whenever dashboard source changes.

## Architecture
- `Armada.Core` - Domain models, database interfaces and providers, service interfaces, settings
- `Armada.Runtimes` - Agent runtime adapters (Claude Code, Codex, Gemini, Cursor, Mux, OpenCode, ApiEndpoint; extensible via IAgentRuntime)
- `Armada.Server` - Admiral process: REST API (Watson), MCP server (Voltaic), WebSocket, Harbor link, serves the dashboard
- `Armada.Helm` - CLI (Spectre.Console), thin HTTP client to Admiral; hosts `armada tui`
- `Armada.Tui` - Terminal UI (TUIKit), the dashboard in a terminal
- `Armada.Client` - Typed .NET client for the REST API and WebSocket (used by the TUI)
- `Armada.Dashboard` - React dashboard (Vite); `dist/` is committed and served by the Admiral at `/dashboard`
- `Armada.Harbor` - Avalonia tray app that runs captains, git, and worktrees on the developer's machine (split mode)
- `Armada.Proxy` - Remote-access portal and tunnel relay
- `Armada.Publisher` - Packaging orchestrator for installers (reads `publisher.json`)
- `Armada.PerfSeed` - Seeds a throwaway data directory for the performance baseline
- `Test.Shared` (suites), `Test.Automated` (console runner), `Test.Xunit` / `Test.Nunit` (adapters)

## Coding Standards

### Naming
- Private fields: `_PascalCase` (e.g., `_Database`, `_Logging`)
- No `var` keyword - always use explicit types
- Async methods: suffix with `Async`, include `CancellationToken token = default`
- Use `.ConfigureAwait(false)` in library code (Core, Runtimes)
- Enums: PascalCase with `Enum` suffix, decorated with `[JsonConverter(typeof(JsonStringEnumConverter))]`
- ID prefixes: flt_, vsl_, cpt_, msn_, vyg_, dck_, sig_, mrg_, evt_, usr_, ten_, crd_, hbr_, ath_, and more;
  every prefix is defined in `src/Armada.Core/Constants.cs` (`*IdPrefix`) or on the model's `_Id` initializer

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
- Watson (NuGet) - REST API + WebSocket framework
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
- Harbor = host-side runner that executes captains on a developer machine (split mode)
