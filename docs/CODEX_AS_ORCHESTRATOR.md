# Codex as Orchestrator

Connect OpenAI Codex CLI to Armada's MCP server and use natural language to orchestrate parallel AI agents across your repositories.

## Prerequisites

1. **Armada installed** - `dotnet tool install -g Armada.Helm`
2. **Codex CLI installed** - `npm install -g @openai/codex`
3. **At least one vessel registered** - a git repository for agents to work in

## Setup

```bash
armada mcp install
```

This writes the MCP configuration for all supported tools. For Codex it runs `codex mcp add armada -- armada mcp stdio`,
which registers Armada's stdio bridge in `~/.codex/config.toml`, so Codex does not need the HTTP server. See the
appendix to configure it by hand.

Use `--dry-run` to preview without writing.

## Default Permission Mode

Armada runs Codex captains non-interactively (`codex exec`, which never prompts) with `--sandbox workspace-write` by default, so commands run without approval prompts inside the workspace sandbox. On Windows the default is `--dangerously-bypass-approvals-and-sandbox`. To run a captain without it, untick **Auto-approve agent tool use** when editing the captain in the dashboard (or pass `autoApprove: false` to the `create_captain` / `update_captain` MCP tools); the captain then always runs with `--sandbox workspace-write`.

## Verify It Works

Start the Admiral server (`armada server start`), then:

```bash
codex "Check Armada status and tell me what's running."
```

Codex will call `status` and report active captains, missions, and voyages.

## Giving Codex Full Instructions

For Codex to effectively orchestrate Armada, paste the contents of [`INSTRUCTIONS_FOR_CODEX.md`](INSTRUCTIONS_FOR_CODEX.md) into your system prompt or project configuration. That document contains the complete tool reference, workflow patterns, and decision-making guidance Codex needs to manage fleets, voyages, missions, and captains.

## Quick Start

> "Register this repository as a vessel in the default fleet, then dispatch a voyage to add input validation to all REST API endpoints."

> "Check on voyage vyg_abc123. If any missions failed, look at the events and redispatch with better prompts."

> "Refactor the authentication system. Decompose into parallel missions and dispatch them."

---

## Appendix: Manual Configuration

If you prefer to configure MCP manually instead of using `armada mcp install`, use `codex mcp add`, which writes
`~/.codex/config.toml`. `armada mcp install` itself registers the stdio bridge.

**Stdio Transport** (what `armada mcp install` uses) - no server required, Armada runs as a subprocess:

```bash
codex mcp add armada -- armada mcp stdio
```

which writes:

```toml
[mcp_servers.armada]
command = "armada"
args = ["mcp", "stdio"]
```

**HTTP Transport** - requires the Admiral server running (`armada server start`):

```bash
codex mcp add armada --url http://localhost:7891/mcp
```

which writes:

```toml
[mcp_servers.armada]
url = "http://localhost:7891/mcp"
```
