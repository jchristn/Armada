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

This writes the MCP configuration for all supported tools. For Codex it runs `codex mcp remove armada` and then
`codex mcp add armada -- armada mcp stdio`, which registers Armada's stdio bridge in `~/.codex/config.toml`, so the Codex
MCP connection does not need the HTTP listener. (When `armada` itself runs from a source build, the registered command
is `dotnet <path>/Armada.Helm.dll mcp stdio` instead.) It also writes an Armada-managed block into `AGENTS.md` in the
current directory, which Codex reads as project instructions. See the appendix to configure it by hand.

The command asks before each change; pass `--yes` to accept them all, or `--dry-run` to preview without writing.

## Default Permission Mode

Armada runs Codex captains non-interactively (`codex exec`, which never prompts) with `--sandbox workspace-write` by default, so commands run without approval prompts inside the workspace sandbox. On Windows the default is `--dangerously-bypass-approvals-and-sandbox`. To run a captain without it, untick **Auto-approve agent tool use** when editing the captain in the dashboard (or pass `autoApprove: false` to the `create_captain` / `update_captain` MCP tools); the captain then always runs with `--sandbox workspace-write`.

## Authentication

The Admiral accepts MCP calls without a credential only while it listens on a loopback hostname (`rest.hostname` is `localhost`, `::1`, or a `127.x.x.x` address), the caller connects from the same machine, and `Mcp.AllowUnauthenticatedLoopback` is true (the default). Those calls act as the default tenant's tenant admin. If the Admiral is bound to any other hostname, or the setting is false, every MCP call must carry a credential: `Authorization: Bearer <token>`, `X-Token`, or `X-Api-Key`.

`armada mcp install` writes the MCP URL with the host the listener is bound with (for example `http://127.0.0.1:7891/mcp` when `rest.hostname` is `127.0.0.1`), because the listener answers only that host; the examples below use the default `localhost`.

With the default stdio registration none of this applies: the bridge opens the local Armada database directly.

## Verify It Works

Start the Admiral server (`armada server start`), then:

```bash
codex "Check Armada status and tell me what's running."
```

Codex will call `status` and report active captains, missions, and voyages. Run `codex mcp list` to confirm the
`armada` entry if it does not.

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

**Stdio Transport** (what `armada mcp install` uses) - Armada runs as a subprocess instead of over HTTP:

```bash
codex mcp add armada -- armada mcp stdio
```

which writes:

```toml
[mcp_servers.armada]
command = "armada"
args = ["mcp", "stdio"]
```

The stdio bridge opens the local Armada database directly, so the MCP connection needs no HTTP listener and no credential. It works only on the Admiral host, missions still run only while the Admiral server is running, and it does not register every tool (for example `inbox`, `stop_server`, and the fleet action, playbook, and memory tools are HTTP-only).

**HTTP Transport** - requires the Admiral server running (`armada server start`):

```bash
codex mcp add armada --url http://localhost:7891/mcp
```

which writes:

```toml
[mcp_servers.armada]
url = "http://localhost:7891/mcp"
```
