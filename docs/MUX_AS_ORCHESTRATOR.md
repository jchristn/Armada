# Mux as Orchestrator

Connect [Mux](https://github.com/jchristn/mux) to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** - `dotnet tool install -g Armada.Helm`
2. **Mux installed** - from the [mux repo](https://github.com/jchristn/mux), run `./install-tool.sh` (Linux/macOS) or `install-tool.bat` (Windows) to build and install the `mux` CLI as a global tool. Configure at least one model endpoint with `mux endpoint add`.
3. **At least one vessel registered** - a git repository for agents to work in.

## Setup

When Mux is installed, `armada mcp install` adds an HTTP `armada` entry (`http://localhost:7891/mcp`) to the `mcp-servers.json` in Mux's config directory, alongside Claude Code, Codex, Gemini, and Cursor. To configure Mux yourself instead, point it at Armada's MCP server with a small config file. The recommended transport is Armada's stdio bridge (`armada mcp stdio`), which reuses the local `armada` CLI's credentials - Mux's HTTP MCP transport does not currently expose per-server auth headers.

Create `armada.mcp.json`:

```json
{
  "servers": [
    { "name": "armada", "transport": "stdio", "command": "armada", "args": ["mcp", "stdio"] }
  ]
}
```

For interactive use, save the same server into the Mux config directory's `mcp-servers.json` (or run `/mcp add` inside the Mux shell) so it loads automatically.

## Launch

Start the Admiral server, then launch Mux with the Armada MCP config:

```bash
armada server start

# Interactive shell (loads mcp-servers.json automatically):
mux

# Headless, one-shot orchestration:
mux print --yolo --mcp-config ./armada.mcp.json "show me all fleets and vessels"
```

With the config loaded, Mux can call all of Armada's `armada` MCP tools (`status`, `enumerate`, `dispatch`, `voyage_status`, and the rest).

## Default Permission Mode

Armada runs Mux captains headless with `mux print --yolo` by default, so all tool calls are auto-approved without prompts. Keep destructive operations inside the worktree. To run a captain without it, untick **Auto-approve agent tool use** when editing the captain in the dashboard (or pass `autoApprove: false` to the `create_captain` / `update_captain` MCP tools); with no explicit `--mux-approval-policy`, the captain then runs with `--approval-policy deny`.

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

**Stdio transport (fallback)** - no MCP port required; Mux launches Armada as a subprocess. Useful when a proxy in front of the MCP port requires an auth header:

```json
{
  "servers": [
    { "name": "armada", "transport": "stdio", "command": "armada", "args": ["mcp", "stdio"] }
  ]
}
```
