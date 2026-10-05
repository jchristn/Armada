# Cursor as Orchestrator

Connect Cursor to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** - `dotnet tool install -g Armada.Helm`
2. **Cursor installed** - Download from [cursor.com](https://cursor.com); install the `cursor-agent` CLI separately if you want to orchestrate from a terminal (Armada's Cursor captains also run through `cursor-agent`)
3. **At least one vessel registered** - a git repository for agents to work in

## Setup

```bash
armada mcp install
```

This writes the MCP configuration for all supported tools. For Cursor it writes `.cursor/mcp.json` in the current directory (project-scoped, so run it from the project root you open in Cursor) and an Armada-managed block in `AGENTS.md` there. If you prefer to edit manually, use:

```json
{
  "mcpServers": {
    "armada": {
      "url": "http://localhost:7891/mcp",
      "transport": "http"
    }
  }
}
```

The command asks before each change; pass `--yes` to accept them all, or `--dry-run` to preview without writing.

## Default Permission Mode

Armada runs Cursor captains in print mode (`cursor-agent -p`) with `--force` by default, so tool calls proceed without approval prompts. To run a captain without it, untick **Auto-approve agent tool use** when editing the captain in the dashboard (or pass `autoApprove: false` to the `create_captain` / `update_captain` MCP tools); the captain then runs without `--force`.

## Authentication

The Admiral accepts MCP calls without a credential only while it listens on a loopback hostname (`rest.hostname` is `localhost`, `::1`, or a `127.x.x.x` address), the caller connects from the same machine, and `Mcp.AllowUnauthenticatedLoopback` is true (the default). Those calls act as the default tenant's tenant admin. If the Admiral is bound to any other hostname, or the setting is false, every MCP call must carry a credential: `Authorization: Bearer <token>`, `X-Token`, or `X-Api-Key`.

`armada mcp install` writes the MCP URL with the host the listener is bound with (for example `http://127.0.0.1:7891/mcp` when `rest.hostname` is `127.0.0.1`), because the listener answers only that host; the examples below use the default `localhost`.

## Using Cursor Agent Mode

Cursor's Agent mode (the default Composer mode) is the natural fit for orchestration. In Agent mode, Cursor calls MCP tools autonomously, reasons about results, and chains operations together.

### Via Composer (GUI)

1. Ensure the Armada MCP server shows as connected in Settings > MCP
2. Open Composer (Cmd/Ctrl+I)
3. Type your orchestration prompt

### Via CLI

```bash
cursor-agent -p "Check Armada status and dispatch a voyage to add tests"
```

Run it from the project directory that holds `.cursor/mcp.json`; `cursor-agent mcp list` shows the servers it loaded.

## Verify It Works

Start the Admiral server (`armada server start`), then open Composer and type:

> "Check Armada status and tell me what's running."

Cursor will call `status` and report active captains, missions, and voyages.

## Giving Cursor Full Instructions

For Cursor to effectively orchestrate Armada, paste the contents of [`INSTRUCTIONS_FOR_CURSOR.md`](INSTRUCTIONS_FOR_CURSOR.md) into your project rules or system prompt. That document contains the complete tool reference, workflow patterns, and decision-making guidance Cursor needs to manage fleets, voyages, missions, and captains.

## Quick Start

> "Register this repository as a vessel in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the events and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions and dispatch them."

---

## Appendix: Manual Configuration

If you prefer to configure MCP manually instead of using `armada mcp install`, add to `.cursor/mcp.json` (or Cursor Settings > MCP):

**HTTP Transport (recommended)** - requires Admiral server running (`armada server start`):

```json
{
  "mcpServers": {
    "armada": {
      "url": "http://localhost:7891/mcp",
      "transport": "http"
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

The stdio bridge opens the local Armada database directly, so the MCP connection needs no HTTP listener and no credential. It works only on the Admiral host, missions still run only while the Admiral server is running, and it does not register every tool (for example `inbox`, `stop_server`, and the fleet action, playbook, and memory tools are HTTP-only).
