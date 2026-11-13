# pi as Orchestrator

Connect [pi](https://pi.dev) to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** -- `dotnet tool install -g Armada.Helm`
2. **pi installed** -- `curl -fsSL https://pi.dev/install.sh | sh` (macOS/Linux), `powershell -c "irm https://pi.dev/install.ps1 | iex"` (Windows), or `npm install -g --ignore-scripts @earendil-works/pi-coding-agent` (Node.js 22.19 or later). Confirm it is on your PATH with `pi --help` (the first line reads `pi - AI coding assistant ...`). Authenticate at least one provider (an API key environment variable such as `ANTHROPIC_API_KEY`, or a stored login) and check it with `pi auth check --provider <provider> --no-refresh`.
3. **At least one vessel registered** -- a git repository for agents to work in.

## Setup

```bash
armada mcp install
```

`armada mcp install` wires up Claude Code, Codex, Gemini CLI, and Cursor, plus Mux, OpenCode, and pi when they are present. pi counts as present when `pi` is on PATH. The command writes an `armada` entry under `mcpServers` in `~/.pi/agent/mcp.json` (or `$PI_CODING_AGENT_DIR/mcp.json` when that variable is set), keeping every other entry in the file. When it adds a token, the token is written to that file in plain text, so protect the file accordingly. It asks before each change; pass `--yes` to accept them all, or `--dry-run` to preview without writing.

To configure pi yourself instead, run:

```bash
pi mcp add armada --url http://localhost:7891/mcp
```

or add the server to `~/.pi/agent/mcp.json` by hand:

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

## Launch

Start the Admiral server, then launch pi with the Armada MCP config loaded:

```bash
armada server start

# Interactive TUI (loads ~/.pi/agent/mcp.json automatically):
pi

# Headless, one-shot orchestration:
pi -p "show me all fleets and vessels"
```

With the config loaded, pi can call all of Armada's MCP tools (`mcp__armada__status`, `mcp__armada__enumerate`, `mcp__armada__dispatch`, `mcp__armada__voyage_status`, and the rest).

## Default Permission Mode

pi itself never prompts for permission. Armada runs pi captains headless with `pi -p --mode json`; under the Bypass policy every built-in tool is available. Under Refuse (or with the captain's **Auto-approve agent tool use** switch off, or `autoApprove: false` on the `create_captain` / `update_captain` MCP tools) Armada adds `--exclude-tools bash,powershell`, so the captain can read and edit files but not run shell commands. Under Approve in Armada Armada loads its permission gate extension (`--extension <armada bin>/Pi/armada-permission-gate.ts`), so each shell command waits for an approver in Armada; a denial is returned to the model, and an unreachable Admiral or a timeout blocks the command. Keep destructive operations inside the worktree.

Armada also passes `--approve` so a headless captain trusts the dock and loads the per-run `.pi/mcp.json` Armada writes there. That flag also loads the repository's own `.pi/extensions` and `.pi/settings.json`; this is the same trust a captain already has through `bash`, but only dispatch pi captains to repositories whose `.pi/` content you trust. See [`INSTRUCTIONS_FOR_PI.md`](INSTRUCTIONS_FOR_PI.md) for the full launch and MCP binding details.

## Authentication

The Admiral accepts MCP calls without a credential only while it listens on a loopback hostname (`rest.hostname` is `localhost`, `::1`, or a `127.x.x.x` address), the caller connects from the same machine, and `Mcp.AllowUnauthenticatedLoopback` is true (the default). Those calls act as the default tenant's tenant admin. If the Admiral is bound to any other hostname, or the setting is false, every MCP call must carry a credential: `Authorization: Bearer <token>`, `X-Token`, or `X-Api-Key`. pi sends one through a `headers` object on the server entry (for example `pi mcp add armada --url http://localhost:7891/mcp --header "X-Token=..."`). Header values support `${ENV}` expansion, so `"X-Token": "${ARMADA_MCP_TOKEN}"` keeps the token out of the file.

`armada mcp install` writes the MCP URL with the host the listener is bound with (for example `http://127.0.0.1:7891/mcp` when `rest.hostname` is `127.0.0.1`), because the listener answers only that host; the examples below use the default `localhost`.

## Verify It Works

```bash
pi mcp list
```

The `armada` server should appear connected with a non-zero tool count (`pi mcp list` exits 1 when a server fails). In an interactive session, ask:

> "Check Armada status."

pi will call `status` and report active captains, missions, and voyages. If it does not recognize the tool, verify the Admiral is running and that the `armada` server is present in `~/.pi/agent/mcp.json`.

## Dispatching a Mission Tree

When one mission needs another's code, dispatch both in one `dispatch` call: give the first a `key` and the dependent one a matching `parentKey`. The child starts after its parent finishes and builds on its branch; missions without a `parentKey` run in parallel. Give each mission `ownedPaths` (the files or directories it may change), and never let two missions that can run at the same time own the same file. Before adding more work, call `vessel_concurrency` to see the vessel's free voyage slots and waiting missions, and `voyage_tree` to see a voyage's missions as a tree. A CLI captain runs one mission at a time, so N parallel missions need N captains. See [`INSTRUCTIONS_FOR_PI.md`](INSTRUCTIONS_FOR_PI.md#dispatching-a-mission-tree) and [SCHEDULING.md](SCHEDULING.md#capacity).

## Quick Start

> "Show me all fleets and vessels."

> "Register a new vessel for https://github.com/org/repo in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the logs and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions that touch non-overlapping files and dispatch them."

## Project-Scoped Orchestration

To orchestrate Armada from within a specific project rather than as a standalone session, add Armada guidance to the project's pi context (a project `AGENTS.md`, or `.pi/APPEND_SYSTEM.md`):

```markdown
## Armada Integration

This project is managed by Armada. When asked to perform large tasks:
1. Use `enumerate({ entityType: "vessels" })` to find this repository's vessel ID
2. Decompose work into missions that touch **non-overlapping files** -- never assign the same file to two missions
3. For monolithic/shared files, combine all changes into a single mission, make the dependent mission a child (`key` and `parentKey` in one dispatch), or chain sequential voyages
4. Use `dispatch` to create a voyage with missions (include explicit file paths in descriptions and `ownedPaths`)
5. Check `vessel_concurrency` before adding more work; monitor with `voyage_status` and `voyage_tree` until complete
6. Do NOT dispatch a second voyage that touches the same files while a prior voyage is still running
7. Review results and redispatch failures if needed

Vessel ID: vsl_xxxxxxxx
Fleet ID: flt_xxxxxxxx
```

pi loads one context file per directory: `AGENTS.override.md`, else `AGENTS.md`, else `CLAUDE.md`. A `CLAUDE.md` next to an `AGENTS.md` is not loaded, so put this guidance in the file pi actually reads.

For the full tool reference and decision-making guidance, see [`INSTRUCTIONS_FOR_PI.md`](INSTRUCTIONS_FOR_PI.md).

---

## Appendix: Manual Configuration

**HTTP transport (recommended)** -- the Admiral serves MCP over Streamable HTTP at `http://localhost:7891/mcp` (MCP port 7891; REST is on 7890). Add it with `pi mcp add armada --url http://localhost:7891/mcp`, or write the `mcpServers` block directly:

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

**Stdio transport (fallback)** -- pi launches Armada as a subprocess instead of connecting over HTTP. The stdio bridge opens the local Armada database directly, so the MCP connection needs no HTTP listener and no credential. It works only on the Admiral host, missions still run only while the Admiral server is running, and it lists the same tools as the HTTP MCP server, but `stop_server` and the fleet action tools (`create_fleet_action`, `update_fleet_action`, `delete_fleet_action`, `run_fleet_action`, `fleet_action_run_status`, `cancel_fleet_action_run`) need the Admiral process and answer an `Unavailable` error over stdio; use the HTTP endpoint for them.

```bash
pi mcp add armada -- armada mcp stdio
```

Windows behavior of the pi runtime has not been verified yet.
