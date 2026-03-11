# Gemini CLI as Orchestrator

Connect the Gemini CLI to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** — `dotnet tool install -g armada`
2. **Gemini CLI installed** — See [Google Gemini CLI docs](https://github.com/google-gemini/gemini-cli) for installation
3. **At least one vessel registered** — a git repository for agents to work in

## Setup

```bash
armada mcp install
```

This shows the MCP configuration snippets for all supported tools including Gemini CLI. Add the following to `~/.gemini/settings.json`:

```json
{
  "mcpServers": {
    "armada": {
      "httpUrl": "http://localhost:7891/rpc"
    }
  }
}
```

Use `--dry-run` to preview without writing.

## Default Permission Mode

Armada runs Gemini captains with `--sandbox none` by default, giving them full filesystem access without approval prompts. This is configurable via the captain's `SandboxMode` property.

## Sandbox Modes

Gemini CLI supports three sandbox modes:

| Mode | Description |
|------|-------------|
| `none` | No restrictions — full read/write/execute access |
| `permissive` | Some operations require approval |
| `strict` | All file writes and shell commands require approval |

For orchestration, `none` is recommended since the orchestrator only calls Armada MCP tools:

```bash
gemini --sandbox none -p "Check Armada status and dispatch a test voyage"
```

## Verify It Works

Start the Admiral server (`armada server start`), then:

```bash
gemini --sandbox none -p "Check Armada status and tell me what's running."
```

Gemini will call `armada_status` and report active captains, missions, and voyages.

## Giving Gemini Full Instructions

For Gemini to effectively orchestrate Armada, paste the contents of [`INSTRUCTIONS_FOR_GEMINI.md`](INSTRUCTIONS_FOR_GEMINI.md) into your system prompt or project instructions. That document contains the complete tool reference, workflow patterns, and decision-making guidance Gemini needs to manage fleets, voyages, missions, and captains.

## Quick Start

> "Register this repository as a vessel in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the events and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions and dispatch them."

---

## Appendix: Manual Configuration

If you prefer to configure MCP manually instead of using `armada mcp install`, add to `~/.gemini/settings.json`:

**HTTP Transport (recommended)** — requires Admiral server running (`armada server start`):

```json
{
  "mcpServers": {
    "armada": {
      "httpUrl": "http://localhost:7891/rpc"
    }
  }
}
```

**Stdio Transport** — no server required, Armada runs as a subprocess:

```json
{
  "mcpServers": {
    "armada": {
      "type": "stdio",
      "command": "armada",
      "args": ["mcp", "stdio"]
    }
  }
}
```
