# Claude Code as Orchestrator

Connect Claude Code to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** — `dotnet tool install -g armada`
2. **Claude Code installed** — `npm install -g @anthropic-ai/claude-code`
3. **At least one vessel registered** — a git repository for agents to work in

## Setup

```bash
armada mcp install
```

This does two things automatically:

1. **Adds the Armada MCP server** to `~/.claude.json` (user-scoped, available from any directory)
2. **Installs the Armada agent** to `~/.claude/agents/armada.md` (a custom agent with full Armada context)

Use `--dry-run` to preview without writing.

## Launch

Start the Admiral server, then launch the Armada agent from any directory:

```bash
armada server start
claude --agent armada
```

The `armada` agent is a standalone Claude Code instance that:
- Knows Armada's domain model, workflows, and conventions
- Has access to all `mcp__armada__*` tools
- Is restricted to Armada tools only — no file editing, no bash
- Works from any directory, not tied to a project

## Default Permission Mode

Armada runs Claude Code captains with `--dangerously-skip-permissions` by default, so all tool calls are auto-approved without user prompts. This is configurable via the captain's `SkipPermissions` property.

## Verify It Works

In the agent session, type:

> "Check Armada status."

Claude will call `armada_status` and report active captains, missions, and voyages. If it doesn't recognize the tool, verify the Admiral is running and check `claude mcp list` for the armada entry.

## Quick Start

> "Show me all fleets and vessels."

> "Register a new vessel for https://github.com/org/repo in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the logs and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions and dispatch them."

## Project-Scoped Orchestration

If you want Claude Code to orchestrate Armada from within a specific project (not as a standalone agent), add to your project's `CLAUDE.md`:

```markdown
## Armada Integration

This project is managed by Armada. When asked to perform large tasks:
1. Use `armada_enumerate({ entityType: "vessels" })` to find this repository's vessel ID
2. Decompose work into missions that touch non-overlapping files
3. Use `armada_dispatch` to create a voyage with parallel missions
4. Monitor with `armada_voyage_status` until complete
5. Review results and redispatch failures if needed

Vessel ID: vsl_xxxxxxxx
Fleet ID: flt_xxxxxxxx
```

For full tool reference and decision-making guidance, see [`INSTRUCTIONS_FOR_CLAUDE_CODE.md`](INSTRUCTIONS_FOR_CLAUDE_CODE.md).

---

## Appendix: Manual Configuration

If you prefer to configure MCP manually instead of using `armada mcp install`:

**Add MCP server** (user-scoped, works from any directory):

```bash
claude mcp add --transport http --scope user armada http://localhost:7891/rpc
```

Or add directly to `~/.claude.json`:

```json
{
  "mcpServers": {
    "armada": {
      "type": "http",
      "url": "http://localhost:7891/rpc"
    }
  }
}
```

**Stdio Transport** — no server required, Armada runs as a subprocess:

```bash
claude mcp add --scope user armada -- armada mcp stdio
```

**Install the agent manually** — create `~/.claude/agents/armada.md` with the agent definition. See `armada mcp install` source for the full content.
