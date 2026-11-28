<!-- armada:mcp:begin -->
# Armada Orchestrator

This project is configured to let Codex and Cursor orchestrate Armada through its MCP tools.

## Primary Rules

- When the user asks about Armada state or operations, prefer Armada MCP tools over shell commands or local file inspection.
- Start with `status` for broad "what is happening?" questions.
- Use `enumerate` to browse fleets, vessels, captains, missions, voyages, docks, signals, events, merge queue entries, personas, prompt templates, and pipelines.
- Use `voyage_status` and `mission_status` for status checks.
- Use `get_mission_log`, `get_captain_log`, and `get_mission_diff` when investigating progress or failures.
- Confirm destructive actions before deleting, purging, cancelling, stopping captains, or stopping the server.

## Common Flow

1. Check `status`.
2. Drill into the relevant voyage, mission, captain, or vessel.
3. Summarize the state clearly.
4. Take follow-up actions with MCP tools only when the user has asked for them.

## Useful IDs

- `flt_` fleet
- `vsl_` vessel
- `cpt_` captain
- `msn_` mission
- `vyg_` voyage
- `dck_` dock
- `sig_` signal
- `mrg_` merge queue entry
<!-- armada:mcp:end -->

# Working on the Armada codebase

These notes are kept in sync with `CLAUDE.md`, which has the full build, test and coding standards.

- Build with `dotnet build src/Armada.sln`. Run selected test suites with
  `dotnet run --project src/Test.Automated --framework net10.0 -- --suites <ids>`; `--suites` takes comma-separated
  suite ids (prefix match).
- Coding standards: no `var`, no tuples, no direct `JsonElement` access, XML docs on public members, one class per file,
  `using` statements inside the namespace, and **no partial classes (only Avalonia XAML code-behind)**. No em-dashes in
  anything written.

## Mission and dispatch architecture (1.5.0)
- `MissionService` is a single facade implementing `IMissionService`. Each responsibility lives in its own service
  (`MissionLauncher`, `MissionAdmissionPipeline`, `MissionCaptainRouter`, `StageCompletionHandler`,
  `MissionHandoffService`, `MissionReworkService`, `MissionEscalationService`, `OutputRepairService`,
  `ArchitectFanOutService`, `MissionInstructionWriter`, `StageCheckRunner` and others) that share one
  `MissionServiceContext`. Add new mission behavior to the service that owns it, not to the facade.
- One dispatcher: only `AdmiralService` runs a dispatch pass; other code raises a dispatch wake. Every mission status
  change goes through the checked transition. `Services.SourceGuards` and `Services.MissionTransitions` fail the tests
  if a second dispatcher, a direct status write, control state read from a mission description, exception message
  matching in Core, or a partial class comes back.
- Captains report results through the submit tools (`armada_submit_plan`, `armada_submit_verdict`,
  `armada_submit_result`), checked against `StageOutputContract`; marker text is the fallback.
- The .NET SDK (`ArmadaClient`) is split into sub-clients (`client.Missions.GetAsync(id)`); see
  `docs/SDK_MIGRATION_1.5.md`.
- Open models in mixed pipelines: see `docs/OPEN_MODELS.md`.
