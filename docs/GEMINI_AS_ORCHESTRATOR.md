# Gemini CLI as Orchestrator

Connect the Gemini CLI to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** - `dotnet tool install -g Armada.Helm`
2. **Gemini CLI installed** - See [Google Gemini CLI docs](https://github.com/google-gemini/gemini-cli) for installation
3. **At least one vessel registered** - a git repository for agents to work in

## Setup

```bash
armada mcp install
```

This writes the MCP configuration for all supported tools. For Gemini CLI it runs `gemini mcp add --scope user --transport http armada http://localhost:7891/mcp`, which writes `~/.gemini/settings.json`. It also writes an Armada-managed block into `GEMINI.md` in the current directory, which Gemini CLI reads as project context. If you prefer to edit manually, use:

```json
{
  "mcpServers": {
    "armada": {
      "httpUrl": "http://localhost:7891/mcp"
    }
  }
}
```

The command asks before each change; pass `--yes` to accept them all, or `--dry-run` to preview without writing.

## Default Permission Mode

Armada runs Gemini captains with `--approval-mode yolo` by default, so tool calls are auto-approved without prompts. To run a captain without it, untick **Auto-approve agent tool use** when editing the captain in the dashboard (or pass `autoApprove: false` to the `create_captain` / `update_captain` MCP tools); the captain then runs with `--approval-mode auto_edit`.

## Approval Modes for the Orchestrator

When you run Gemini yourself as the orchestrator, its `--approval-mode` decides whether each tool call (including
Armada's MCP tools) needs your confirmation: `default` asks every time, `auto_edit` approves edits only, and `yolo`
approves everything. Because the orchestrator only calls Armada MCP tools, `yolo` is convenient; keep `default` if
you want to confirm each dispatch:

```bash
gemini --approval-mode yolo -p "Check Armada status and dispatch a test voyage"
```

## Authentication

The Admiral accepts MCP calls without a credential only while it listens on a loopback hostname (`rest.hostname` is `localhost`, `::1`, or a `127.x.x.x` address), the caller connects from the same machine, and `Mcp.AllowUnauthenticatedLoopback` is true (the default). Those calls act as the default tenant's tenant admin. If the Admiral is bound to any other hostname, or the setting is false, every MCP call must carry a credential: `Authorization: Bearer <token>`, `X-Token`, or `X-Api-Key`.

`armada mcp install` writes the MCP URL with the host the listener is bound with (for example `http://127.0.0.1:7891/mcp` when `rest.hostname` is `127.0.0.1`), because the listener answers only that host; the examples below use the default `localhost`.

## Verify It Works

Start the Admiral server (`armada server start`), then:

```bash
gemini -p "Check Armada status and tell me what's running."
```

Gemini will call `status` and report active captains, missions, and voyages.

## Giving Gemini Full Instructions

For Gemini to effectively orchestrate Armada, paste the contents of [`INSTRUCTIONS_FOR_GEMINI.md`](INSTRUCTIONS_FOR_GEMINI.md) into your system prompt or project instructions. That document contains the complete tool reference, workflow patterns, and decision-making guidance Gemini needs to manage fleets, voyages, missions, and captains.

## Quick Start

> "Register this repository as a vessel in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the events and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions and dispatch them."

---

## Appendix: Manual Configuration

If you prefer to configure MCP manually instead of using `armada mcp install`, add to `~/.gemini/settings.json`:

**HTTP Transport (recommended)** - requires Admiral server running (`armada server start`):

```json
{
  "mcpServers": {
    "armada": {
      "httpUrl": "http://localhost:7891/mcp"
    }
  }
}
```

**Stdio Transport** - Armada runs as a subprocess instead of over HTTP:

```json
{
  "mcpServers": {
    "armada": {
      "command": "armada",
      "args": ["mcp", "stdio"]
    }
  }
}
```

The stdio bridge opens the local Armada database directly, so the MCP connection needs no HTTP listener and no credential. It works only on the Admiral host, missions still run only while the Admiral server is running, and it lists the same tools as the HTTP MCP server, but `stop_server` and the fleet action tools (`create_fleet_action`, `update_fleet_action`, `delete_fleet_action`, `run_fleet_action`, `fleet_action_run_status`, `cancel_fleet_action_run`) need the Admiral process and answer an `Unavailable` error over stdio; use the HTTP endpoint for them.
