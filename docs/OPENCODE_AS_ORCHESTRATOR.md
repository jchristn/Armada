# OpenCode as Orchestrator

Connect [OpenCode](https://opencode.ai) to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** -- `dotnet tool install -g armada`
2. **OpenCode installed** -- install the `opencode` CLI and confirm it is on your PATH with `opencode --version`. Authenticate at least one provider with `opencode providers login` so runs can reach a model.
3. **At least one vessel registered** -- a git repository for agents to work in.

## Setup

`armada mcp install` wires up Claude Code, Codex, Gemini, Cursor, Mux, and -- when OpenCode is present on the machine -- OpenCode automatically. It writes a remote MCP server entry (`{ "type": "remote", "url": "http://localhost:7891/mcp", "enabled": true }`) under the `mcp` block of `~/.config/opencode/opencode.json`. To configure OpenCode yourself instead, point it at Armada's Streamable HTTP endpoint at `http://localhost:7891/mcp` as a remote MCP server with one command:

```bash
opencode mcp add armada --url http://localhost:7891/mcp
```

That writes an `mcp` entry into your OpenCode config (`~/.config/opencode/opencode.json`, or the equivalent user config directory on Windows). If you prefer to manage the config by hand, add the server yourself:

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

Armada runs OpenCode captains headless with `opencode run --auto` by default, so permissions that are not explicitly denied are auto-approved without prompts. Keep destructive operations inside the worktree.

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

**HTTP transport (recommended)** -- the Admiral serves MCP over Streamable HTTP at `http://localhost:7891/mcp` (MCP port 7891; REST is on 7890). Add it with `opencode mcp add armada --url http://localhost:7891/mcp`, or write the `mcp` block directly:

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

**Stdio transport (fallback)** -- no MCP port required; OpenCode launches Armada as a subprocess. Useful when a proxy in front of the MCP port requires an auth header, since the stdio bridge reuses the local `armada` CLI's credentials:

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
