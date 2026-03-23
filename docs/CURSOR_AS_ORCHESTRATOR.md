# Cursor as Orchestrator

Connect Cursor to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** — `dotnet tool install -g armada`
2. **Cursor installed** — Download from [cursor.com](https://cursor.com) or install the CLI separately
3. **At least one vessel registered** — a git repository for agents to work in

## Setup

```bash
armada mcp install
```

This now writes the MCP configuration for all supported tools automatically. For Cursor specifically, it writes `.cursor/mcp.json` in the current project. If you prefer to edit manually, use:

```json
{
  "mcpServers": {
    "armada": {
      "url": "http://localhost:7891/rpc"
    }
  }
}
```

Use `--dry-run` to preview without writing.

## Default Permission Mode

Armada runs Cursor in `--agent` mode with no explicit permission gate — all tool calls proceed without approval prompts. There is no configurable permission property for Cursor.

## Using Cursor Agent Mode

Cursor's Agent mode (the default Composer mode) is the natural fit for orchestration. In Agent mode, Cursor calls MCP tools autonomously, reasons about results, and chains operations together.

### Via Composer (GUI)

1. Ensure the Armada MCP server shows as connected in Settings > MCP
2. Open Composer (Cmd/Ctrl+I)
3. Type your orchestration prompt

### Via CLI

```bash
cursor --agent --prompt "Check Armada status and dispatch a voyage to add tests"
```

## Verify It Works

Start the Admiral server (`armada server start`), then open Composer and type:

> "Check Armada status and tell me what's running."

Cursor will call `armada_status` and report active captains, missions, and voyages.

## Giving Cursor Full Instructions

For Cursor to effectively orchestrate Armada, paste the contents of [`INSTRUCTIONS_FOR_CURSOR.md`](INSTRUCTIONS_FOR_CURSOR.md) into your project rules or system prompt. That document contains the complete tool reference, workflow patterns, and decision-making guidance Cursor needs to manage fleets, voyages, missions, and captains.

## Quick Start

> "Register this repository as a vessel in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the events and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions and dispatch them."

---

## Appendix: Manual Configuration

If you prefer to configure MCP manually instead of using `armada mcp install`, add to `.cursor/mcp.json` (or Cursor Settings > MCP):

**HTTP Transport (recommended)** — requires Admiral server running (`armada server start`):

```json
{
  "mcpServers": {
    "armada": {
      "url": "http://localhost:7891/rpc"
    }
  }
}
```

**Stdio Transport** — no server required, Armada runs as a subprocess:

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
