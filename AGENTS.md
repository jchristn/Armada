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
