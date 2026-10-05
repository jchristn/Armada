# OpenCode as Orchestrator

Connect [OpenCode](https://opencode.ai) to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** -- `dotnet tool install -g Armada.Helm`
2. **OpenCode installed** -- install the `opencode` CLI and confirm it is on your PATH with `opencode --version`. Authenticate at least one provider with `opencode providers login` so runs can reach a model.
3. **At least one vessel registered** -- a git repository for agents to work in.

## Setup

```bash
armada mcp install
```

`armada mcp install` wires up Claude Code, Codex, Gemini CLI, and Cursor, plus Mux and OpenCode when they are present. OpenCode counts as present when `~/.config/opencode` exists or `opencode` is on PATH. The command writes a remote MCP server entry (`{ "type": "remote", "url": "http://localhost:7891/mcp", "enabled": true }`) under the `mcp` block of `~/.config/opencode/opencode.jsonc` if that file exists, otherwise `~/.config/opencode/opencode.json` (the same path on every OS). It asks before each change; pass `--yes` to accept them all, or `--dry-run` to preview without writing.

To configure OpenCode yourself instead, run `opencode mcp add` and register a remote server named `armada` with URL `http://localhost:7891/mcp`, or add the server to the config by hand:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "armada": {
      "type": "remote",
      "url": "http://localhost:7891/mcp",
      "enabled": true
    }
  }
}
```

## Launch

Start the Admiral server, then launch OpenCode with the Armada MCP config loaded:

```bash
armada server start

# Interactive TUI (loads opencode.json automatically):
opencode

# Headless, one-shot orchestration:
opencode run --auto "show me all fleets and vessels"
```

With the config loaded, OpenCode can call all of Armada's `armada` MCP tools (`status`, `enumerate`, `dispatch`, `voyage_status`, and the rest).

## Default Permission Mode

Armada runs OpenCode captains headless with `opencode run --auto` by default, so permissions that are not explicitly denied are auto-approved without prompts. Keep destructive operations inside the worktree. To run a captain without it, untick **Auto-approve agent tool use** when editing the captain in the dashboard (or pass `autoApprove: false` to the `create_captain` / `update_captain` MCP tools); the captain then runs without `--auto`.

## Authentication

The Admiral accepts MCP calls without a credential only while it listens on a loopback hostname (`rest.hostname` is `localhost`, `::1`, or a `127.x.x.x` address), the caller connects from the same machine, and `Mcp.AllowUnauthenticatedLoopback` is true (the default). Those calls act as the default tenant's tenant admin. If the Admiral is bound to any other hostname, or the setting is false, every MCP call must carry a credential: `Authorization: Bearer <token>`, `X-Token`, or `X-Api-Key`. OpenCode sends one through a `headers` object on the remote entry.

`armada mcp install` writes the MCP URL with the host the listener is bound with (for example `http://127.0.0.1:7891/mcp` when `rest.hostname` is `127.0.0.1`), because the listener answers only that host; the examples below use the default `localhost`.

## Verify It Works

```bash
opencode mcp list
```

The `armada` server should appear online with a non-zero tool count. In an interactive session, ask:

> "Check Armada status."

OpenCode will call `status` and report active captains, missions, and voyages. If it does not recognize the tool, verify the Admiral is running and that the `armada` server is present and enabled in the OpenCode config.

## Quick Start

> "Show me all fleets and vessels."

> "Register a new vessel for https://github.com/org/repo in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the logs and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions that touch non-overlapping files and dispatch them."

## Project-Scoped Orchestration

To orchestrate Armada from within a specific project rather than as a standalone session, add Armada guidance to the project's OpenCode context (system prompt, a project `AGENTS.md`, or a similar context file):

```markdown
## Armada Integration

This project is managed by Armada. When asked to perform large tasks:
1. Use `enumerate({ entityType: "vessels" })` to find this repository's vessel ID
2. Decompose work into missions that touch **non-overlapping files** -- never assign the same file to two missions
3. For monolithic/shared files, combine all changes into a single mission or chain sequential voyages
4. Use `dispatch` to create a voyage with missions (include explicit file paths in descriptions)
5. Monitor with `voyage_status` until complete
6. Do NOT dispatch a second voyage that touches the same files while a prior voyage is still running
7. Review results and redispatch failures if needed

Vessel ID: vsl_xxxxxxxx
Fleet ID: flt_xxxxxxxx
```

For the full tool reference and decision-making guidance, see [`INSTRUCTIONS_FOR_OPENCODE.md`](INSTRUCTIONS_FOR_OPENCODE.md).

---

## Appendix: Manual Configuration

**HTTP transport (recommended)** -- the Admiral serves MCP over Streamable HTTP at `http://localhost:7891/mcp` (MCP port 7891; REST is on 7890). Add it with `opencode mcp add`, or write the `mcp` block directly:

```json
{
  "mcp": {
    "armada": {
      "type": "remote",
      "url": "http://localhost:7891/mcp",
      "enabled": true
    }
  }
}
```

**Stdio transport (fallback)** -- OpenCode launches Armada as a subprocess instead of connecting over HTTP. The stdio bridge opens the local Armada database directly, so the MCP connection needs no HTTP listener and no credential. It works only on the Admiral host, missions still run only while the Admiral server is running, and it does not register every tool (for example `inbox`, `stop_server`, and the fleet action, playbook, and memory tools are HTTP-only).

```json
{
  "mcp": {
    "armada": {
      "type": "local",
      "command": ["armada", "mcp", "stdio"],
      "enabled": true
    }
  }
}
```
