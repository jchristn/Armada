# Claude Code as Orchestrator

Connect Claude Code to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** - `dotnet tool install -g Armada.Helm`
2. **Claude Code installed** - `npm install -g @anthropic-ai/claude-code`
3. **At least one vessel registered** - a git repository for agents to work in

## Setup

```bash
armada mcp install
```

For Claude Code this does two things:

1. **Adds the Armada MCP server** to `~/.claude.json` (an `armada` entry under `mcpServers` with `"type": "http"`; user-scoped, available from any directory)
2. **Installs the Armada agent** to `~/.claude/agents/armada.md` (a custom agent with full Armada context)

It also configures Codex and Gemini CLI (through their own `mcp add` commands), Cursor (`.cursor/mcp.json` in the current directory), Mux and OpenCode when they are installed, and writes an Armada-managed block into `AGENTS.md` and `GEMINI.md` in the current directory.

The command asks before each change; pass `--yes` to accept them all, or `--dry-run` to preview without writing. `armada mcp remove` undoes it.

## Launch

Start the Admiral server, then launch the Armada agent from any directory:

```bash
armada server start
claude --agent armada
```

The `armada` agent is a standalone Claude Code instance that:
- Knows Armada's domain model, workflows, and conventions
- Has access to all `mcp__armada__*` tools
- Is instructed to work only through Armada's MCP tools - no file editing, no bash
- Works from any directory, not tied to a project

## Default Permission Mode

Armada runs Claude Code captains with `--dangerously-skip-permissions` by default, so all tool calls are auto-approved without user prompts. To run a captain without it, untick **Auto-approve agent tool use** when editing the captain in the dashboard (or pass `autoApprove: false` to the `create_captain` / `update_captain` MCP tools); the captain then runs with `--permission-mode acceptEdits --allowedTools mcp__armada`: edits are accepted, Armada's own MCP tools stay usable, and shell commands need an allow rule in the project's Claude Code settings. A vessel's own `autoApprove` setting, when set, overrides the captain's for missions on that vessel.

Each mission captain also gets a mission-scoped MCP token (`Mcp.MissionScopedTokens`, default true) and is launched with `--setting-sources project,local --strict-mcp-config --mcp-config <per-launch file>`. It therefore reaches only Armada's MCP server, acting as the mission's owner, and ignores your user-level Claude Code settings and the other MCP servers in your configuration. Set `Mcp.MissionScopedTokens` to false to launch missions without a token.

## Authentication

The Admiral accepts MCP calls without a credential only while it listens on a loopback hostname (`rest.hostname` is `localhost`, `::1`, or a `127.x.x.x` address), the caller connects from the same machine, and `Mcp.AllowUnauthenticatedLoopback` is true (the default). Those calls act as the default tenant's tenant admin. If the Admiral is bound to any other hostname, or the setting is false, every MCP call must carry a credential: `Authorization: Bearer <token>`, `X-Token`, or `X-Api-Key`.

`armada mcp install` writes the MCP URL with the host the listener is bound with (for example `http://127.0.0.1:7891/mcp` when `rest.hostname` is `127.0.0.1`), because the listener answers only that host; the examples below use the default `localhost`.

## Verify It Works

In the agent session, type:

> "Check Armada status."

Claude will call `status` and report active captains, missions, and voyages. If it doesn't recognize the tool, verify the Admiral is running and check `claude mcp list` for the armada entry.

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
1. Use `enumerate({ entityType: "vessels" })` to find this repository's vessel ID
2. Decompose work into missions that touch **non-overlapping files** - never assign the same file to two missions
3. For monolithic/shared files, combine all changes into a single mission or chain sequential voyages
4. Use `dispatch` to create a voyage with missions (include explicit file paths in descriptions)
5. Monitor with `voyage_status` until complete
6. Do NOT dispatch a second voyage that touches the same files while a prior voyage is still running
7. Review results and redispatch failures if needed

Vessel ID: vsl_xxxxxxxx
Fleet ID: flt_xxxxxxxx
```

For full tool reference and decision-making guidance, see [`INSTRUCTIONS_FOR_CLAUDE_CODE.md`](INSTRUCTIONS_FOR_CLAUDE_CODE.md).

---

## Appendix: Manual Configuration

If you prefer to configure MCP manually instead of using `armada mcp install`:

**Add MCP server** (user-scoped, works from any directory):

```bash
claude mcp add --transport http --scope user armada http://localhost:7891/mcp
```

Or add directly to `~/.claude.json`:

```json
{
  "mcpServers": {
    "armada": {
      "type": "http",
      "url": "http://localhost:7891/mcp"
    }
  }
}
```

**Stdio Transport** - Armada runs as a subprocess instead of over HTTP:

```bash
claude mcp add --scope user armada -- armada mcp stdio
```

The stdio bridge opens the local Armada database directly, so the MCP connection needs no HTTP listener and no credential. It works only on the Admiral host, missions still run only while the Admiral server is running, and it does not register every tool (for example `inbox`, `stop_server`, and the fleet action, playbook, and memory tools are HTTP-only).

**Install the agent manually** - create `~/.claude/agents/armada.md` with the agent definition. `armada mcp install --dry-run` shows where it goes; the content is generated by `GenerateAgentDefinition` in `src/Armada.Helm/Commands/McpConfigHelper.cs`.
