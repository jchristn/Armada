# Mux as Orchestrator

Connect [Mux](https://github.com/jchristn/mux) to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** - `dotnet tool install -g Armada.Helm`
2. **Mux installed** - from the [mux repo](https://github.com/jchristn/mux), run `./install-tool.sh` (Linux/macOS) or `install-tool.bat` (Windows) to build and install the `mux` CLI as a global tool. Configure at least one model endpoint with `mux endpoint add`.
3. **At least one vessel registered** - a git repository for agents to work in.

## Setup

```bash
armada mcp install
```

When Mux is detected (its config directory, `MUX_CONFIG_DIR` or `~/.mux`, exists, or a `mux` executable is on PATH), `armada mcp install` adds an HTTP `armada` entry to that directory's `mcp-servers.json`, alongside Claude Code, Codex, Gemini CLI, and Cursor:

```json
{
  "servers": [
    { "name": "armada", "transport": "http", "url": "http://localhost:7891", "mcpPath": "/mcp" }
  ]
}
```

The command asks before each change; pass `--yes` to accept them all, or `--dry-run` to preview without writing. To configure Mux yourself instead, see the appendix: run `/mcp` inside a Mux session, or save the entry above to a file such as `armada.mcp.json` and pass it with `--mcp-config`.

## Launch

Start the Admiral server, then launch Mux with the Armada MCP config:

```bash
armada server start

# Interactive shell (loads mcp-servers.json automatically):
mux

# Headless, one-shot orchestration (headless runs load MCP servers only from --mcp-config):
mux print --yolo --mcp-config ./armada.mcp.json "show me all fleets and vessels"
```

With the config loaded, Mux can call all of Armada's `armada` MCP tools (`status`, `enumerate`, `dispatch`, `voyage_status`, and the rest).

## Default Permission Mode

Armada runs Mux captains headless with `mux print --yolo` by default, so all tool calls are auto-approved without prompts. Keep destructive operations inside the worktree. To run a captain without it, untick **Auto-approve agent tool use** when editing the captain in the dashboard (or pass `autoApprove: false` to the `create_captain` / `update_captain` MCP tools); with no explicit `--mux-approval-policy`, the captain then runs with `--approval-policy deny`.

## Authentication

The Admiral accepts MCP calls without a credential only while it listens on a loopback hostname (`rest.hostname` is `localhost`, `::1`, or a `127.x.x.x` address), the caller connects from the same machine, and `Mcp.AllowUnauthenticatedLoopback` is true (the default). Those calls act as the default tenant's tenant admin. If the Admiral is bound to any other hostname, or the setting is false, every MCP call must carry a credential: `Authorization: Bearer <token>`, `X-Token`, or `X-Api-Key`. Mux sends one through an `auth` block on the server entry, for example `"auth": { "type": "apikey", "apiKeyHeader": "X-Api-Key", "apiKeyValue": "<key>" }`.

`armada mcp install` writes the MCP URL with the host the listener is bound with (for example `http://127.0.0.1:7891/mcp` when `rest.hostname` is `127.0.0.1`), because the listener answers only that host; the examples below use the default `localhost`.

## Verify It Works

```bash
mux probe --output-format json --require-tools
```

The `armada` server should appear with a non-zero tool count. In an interactive session, ask:

> "Check Armada status."

Mux will call `status` and report active captains, missions, and voyages. If it does not recognize the tool, verify the Admiral is running and that `--mcp-config` (or `mcp-servers.json`) points at the `armada` server.

## Quick Start

> "Show me all fleets and vessels."

> "Register a new vessel for https://github.com/org/repo in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the logs and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions that touch non-overlapping files and dispatch them."

## Project-Scoped Orchestration

To orchestrate Armada from within a specific project rather than as a standalone session, add Armada guidance to the project's Mux context (system prompt, skill, or a `MUX.md`-style file):

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

For the full tool reference and decision-making guidance, see [`INSTRUCTIONS_FOR_MUX.md`](INSTRUCTIONS_FOR_MUX.md).

---

## Appendix: Manual Configuration

**HTTP transport (recommended)** - the Admiral serves MCP over Streamable HTTP at `http://localhost:7891/mcp` (MCP port 7891; REST is on 7890). In an interactive session, run `/mcp`, choose **+ Add MCP server...**, and enter name `armada`, transport `http`, url `http://localhost:7891`, mcp path `/mcp` (default), auth `none`. For headless runs, point Mux's HTTP MCP transport at that path:

```json
{
  "servers": [
    { "name": "armada", "transport": "http", "url": "http://localhost:7891", "mcpPath": "/mcp" }
  ]
}
```

Pass it inline instead of a file if you prefer:

```bash
mux print --yolo --mcp-config '{"servers":[{"name":"armada","transport":"http","url":"http://localhost:7891","mcpPath":"/mcp"}]}' "what is the status of the fleet?"
```

Use `--strict-mcp-config` to load only the servers from the flag and ignore the config directory's `mcp-servers.json`.

**Stdio transport (fallback)** - Mux launches Armada as a subprocess instead of connecting over HTTP:

```json
{
  "servers": [
    { "name": "armada", "transport": "stdio", "command": "armada", "args": ["mcp", "stdio"] }
  ]
}
```

The stdio bridge opens the local Armada database directly, so the MCP connection needs no HTTP listener and no credential. It works only on the Admiral host, missions still run only while the Admiral server is running, and it lists the same tools as the HTTP MCP server, but `stop_server` and the fleet action tools (`create_fleet_action`, `update_fleet_action`, `delete_fleet_action`, `run_fleet_action`, `fleet_action_run_status`, `cancel_fleet_action_run`) need the Admiral process and answer an `Unavailable` error over stdio; use the HTTP endpoint for them.
