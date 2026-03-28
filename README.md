<p align="center">
  <img src="assets/logo.png" alt="Armada Logo" width="200" />
</p>

<h1 align="center">Armada</h1>

<p align="center">
  <strong>Multi-agent orchestration for scaling human developers with AI</strong>
  <br />
  <strong>⚠️ ALPHA v0.3.0 - APIs, schemas, and behavior may change without notice</strong>
</p>

<p align="center">
  <a href="#quick-start">Quick Start</a> |
  <a href="#how-it-works">How It Works</a> |
  <a href="#architecture">Architecture</a> |
  <a href="#cli-reference">CLI Reference</a> |
  <a href="#upgrading">Upgrading</a> |
  <a href="#rest-api">REST API</a> |
  <a href="#mcp-integration">MCP Integration</a> |
  <a href="#contributing">Contributing</a>
</p>

---

Armada coordinates multiple AI coding agents working in parallel on your codebase. Each agent operates in an isolated git worktree, producing clean branches and pull requests. One command gets you from zero to dispatching work -- no configuration required.

```bash
cd your-project
armada go "Add input validation to the signup form"
```

That's it. Armada auto-initializes, detects your installed agent runtime (Claude Code, Codex, Gemini, Cursor), infers the repo from your current directory, provisions a worker agent, and dispatches your task.

> **⚠️ Security Note:** Armada runs AI agents with auto-approve flags enabled by default — Claude Code uses `--dangerously-skip-permissions`, Codex uses `--approval-mode full-auto`, and Gemini uses `--sandbox none`. This means agents can read, write, and execute code in their worktrees without user confirmation. Review the [configuration](#configuration) options and understand the implications before running Armada in sensitive environments.

## New in v0.3.0

- **Multi-tenant support** -- tenant isolation, user management, bearer token and session token authentication with role-based access (admin, tenant admin, user)
- **Per-captain system instructions** -- customize each captain's behavior with persistent instructions injected into every mission prompt (e.g., "You are a testing specialist")
- **Model context accumulation** -- agents discover and record key information about repositories during missions, building a shared knowledge base for future agents (enabled by default)
- **Mission history chart** -- SVG bar chart on the Dashboard tab showing missions over time with fleet/vessel filters and time range tabs
- **Dashboard alert banners** -- proactive warnings when captains are stalled, missions have failed, or dispatch is blocked
- **Vessel git sync status** -- GitHub-style "ahead/behind" badges on the Vessels page showing commits that need to be pushed or pulled
- **Captain no longer stalls on failure** -- recovery exhaustion releases captains to Idle instead of Stalled, so they pick up new work immediately
- **WorkProduced missions no longer block dispatch** -- post-agent states don't prevent new missions on the same vessel
- **Dock preservation on crash** -- when a captain's process dies, the worktree and branch are preserved so the next captain can continue from partial work
- **Error modals** -- all data loading errors are shown in dismissable modals instead of inline text
- **Improved deserialization** -- all REST routes use explicit JSON deserialization with case-insensitive options, fixing camelCase request bodies from the dashboard
- **Default vessel settings** -- new vessels default to LocalMerge landing mode and LocalAndRemote branch cleanup
- **Dependency updates** -- SqlClient 7.0.0, Sqlite 10.0.5, MySqlConnector 2.5.0, Npgsql 10.0.2, SwiftStack 0.4.8, Spectre.Console 0.54.0
- **Copy button fix** -- clipboard icons use CSS pseudo-elements with green checkmark on copy instead of "Copied!" text
- **Status tooltips with guidance** -- every status badge tooltip now includes a "Next:" action telling users what to do

## Features

- **Zero-config startup** -- sensible defaults, auto-detection of runtimes and repositories
- **Parallel agents** -- dispatch multiple tasks across multiple AI agents simultaneously
- **Git worktree isolation** -- each agent works on its own branch, no interference between agents
- **Multi-runtime support** -- Claude Code, Codex, Gemini, Cursor, and extensible to other agent runtimes via `IAgentRuntime`
- **Auto-recovery** -- crashed agents are automatically detected, repaired, and relaunched
- **Broad-scope detection** -- prevents concurrent mutations to the same files across agents
- **Multi-tenant** -- tenant isolation, user management, bearer token and session token authentication
- **REST API + WebSocket** -- programmatic access and real-time status updates
- **MCP server** -- 18 tools let Claude Code, Codex, or any MCP client orchestrate Armada (see [AI-powered orchestration](#ai-powered-orchestration))
- **React dashboard** -- optional standalone React dashboard for Docker/production deployments
- **Model context** -- agents accumulate key knowledge about repositories across missions, so future agents start with institutional memory
- **Persona-based specialization** -- assign agent roles (Worker, Architect, Judge, TestEngineer) to shape what each captain does, with support for custom personas
- **Pipeline workflows** -- define ordered sequences of persona stages (e.g. Architect -> Worker -> TestEngineer -> Judge) for multi-stage quality gates, configured at fleet/vessel level or per-dispatch
- **Prompt template configurability** -- every instruction given to agents is driven by user-editable templates with `{Placeholder}` parameters, giving full control over agent behavior
- **Cross-platform** -- Windows, macOS, Linux (C#/.NET)

## Benefits

- **Single pane of glass** -- Monitor and manage all AI agent work across every project from one unified dashboard, eliminating the need to juggle multiple terminals and windows.
- **Reduced context-switching** -- Full mission history, logs, diffs, and signals are preserved, so you can pick up exactly where you left off without losing your train of thought.
- **Scale across more projects** -- Dispatch parallel missions across multiple repositories simultaneously, letting you take on more work than a single developer normally could.
- **Project management meets conversational AI** -- Integrate task tracking, prioritization, and workflow orchestration directly into AI coding agents like Claude Code, bridging the gap between planning and execution.
- **Safe isolated worktrees** -- Every agent operates in its own git worktree, so parallel work never collides and your main branch stays clean until you're ready to merge.
- **Model context accumulation** -- Agents discover and record key information about each repository during missions, building a shared knowledge base that makes future missions faster and more effective.
- **Automated merge queues** -- Completed missions are queued for merge automatically, reducing manual branch management and keeping your integration pipeline flowing.
- **Auditable event trails** -- Every mission dispatch, status transition, completion, and failure is recorded in a structured event log you can query at any time.
- **Reproducible workflows** -- Voyages capture a batch of missions as a reusable unit; retry a failed voyage or re-dispatch it against a new branch with a single command.
- **Team visibility** -- Real-time WebSocket updates and a REST API keep everyone informed of agent progress without polling or asking around.

## Screenshots

<details>
<summary>Click to expand screenshots</summary>

<br />

![Screenshot 1](assets/screenshot-1.png)

![Screenshot 2](assets/screenshot-2.png)

![Screenshot 3](assets/screenshot-3.png)

![Screenshot 4](assets/screenshot-4.png)

</details>

## Quick Start

### Prerequisites

- [.NET 8.0+ SDK](https://dot.net/download)
- At least one AI agent runtime on your PATH:
  - [Claude Code](https://docs.anthropic.com/en/docs/claude-code) (`claude`)
  - [Codex](https://github.com/openai/codex) (`codex`)
  - [Gemini CLI](https://github.com/google-gemini/gemini-cli) (`gemini`)
  - [Cursor](https://docs.cursor.com/cli) (`cursor`)

### Install

```bash
# Prerequisites: .NET 8.0+ SDK (https://dot.net/download)
git clone https://github.com/jchristn/armada.git
cd armada/src
dotnet build Armada.sln

# Install as a global dotnet tool
dotnet pack Armada.Helm -o ./nupkg
dotnet tool install --global --add-source ./nupkg Armada.Helm

# If you later need to remove (perhaps to update)
dotnet tool uninstall --global Armada.Helm
```

Helper scripts are in the project root directory: `install.bat/.sh`, `remove.bat/.sh`, `reinstall.bat/.sh`, and `update.bat/.sh`.

### First Mission

```bash
cd your-project
armada go "Add input validation to the signup form"
armada watch   # monitor progress
```

### Default Credentials

On first boot, Armada seeds a default tenant, user, and credential:

| Item | Value |
|------|-------|
| Email | `admin@armada` |
| Password | `password` |
| Bearer Token | `default` |

The dashboard login screen accepts the email and password above. For API access, use `Authorization: Bearer default`.

> **Important:** Change the default password in production environments.

For a deeper walkthrough, see the [Getting Started Guide](GETTING_STARTED.md).

## How It Works

```
You (Human)
    |
    v
 armada CLI ---embedded---> Admiral (in-process, auto-starts)
                  or
               ----HTTP----> Admiral Server (if started separately)
                               |
                               +-- SQLite Database (state)
                               |
                               +-- Captain 1 (Claude Code) --> git worktree 1
                               +-- Captain 2 (Claude Code) --> git worktree 2
                               +-- Captain 3 (Codex)       --> git worktree 3
                               +-- Captain 4 (Gemini)      --> git worktree 4
                               +-- Captain 5 (Cursor)      --> git worktree 5
```

When you run `armada go`, the **Admiral** (coordinator process) receives your prompt, creates one or more **Missions**, and assigns each to a **Captain** (AI agent). Each captain works in its own **git worktree** branched from the default branch. This gives you:

- **Full isolation** -- agents cannot interfere with each other
- **Clean diffs** -- each branch contains only one agent's changes
- **Easy review** -- one PR per mission

### Parallel Tasks

Semicolons or numbered lists split a prompt into separate missions, each assigned to a different agent:

```bash
armada go "Add rate limiting; Add request logging; Add input validation"

armada go "1. Add auth middleware 2. Add login endpoint 3. Add token validation"
```

### Named Voyages (Batches)

```bash
armada voyage create "API Hardening" --vessel my-project \
  --mission "Add rate limiting middleware" \
  --mission "Add input validation to all POST endpoints" \
  --mission "Add request logging with correlation IDs"
```

### Auto-Recovery

If a captain crashes, the Admiral automatically detects it, repairs the worktree, and relaunches the agent (up to `MaxRecoveryAttempts` times, default: 3).

## Architecture

Armada is a C#/.NET solution with five projects:

| Project | Description |
|---------|-------------|
| **Armada.Core** | Domain models (including tenants, users, credentials), database interfaces, service interfaces, settings |
| **Armada.Runtimes** | Agent runtime adapters (Claude Code, Codex, Gemini, Cursor, extensible via `IAgentRuntime`) |
| **Armada.Server** | Admiral process: REST API ([SwiftStack](https://github.com/jchristn/swiftstack)), MCP server ([Voltaic](https://github.com/jchristn/voltaic)), WebSocket hub, embedded dashboard |
| **Armada.Dashboard** | Standalone React dashboard for Docker/production deployments |
| **Armada.Helm** | CLI ([Spectre.Console](https://spectreconsole.net/)), thin HTTP client to Admiral |

All operational data (fleets, vessels, captains, missions, voyages, docks, signals, events, merge entries) is tenant-scoped. Users within a tenant collaborate on shared operational data. Admin users can access data across all tenants.

Each operational table persists both `TenantId` and `UserId`. Armada maintains foreign-key integrity for those ownership columns across all supported databases: SQLite, PostgreSQL, SQL Server, and MySQL.

The authorization model is:

- `IsAdmin = true`: global system admin with access to every tenant and object.
- `IsAdmin = false`, `IsTenantAdmin = true`: tenant admin with management access inside that tenant, including users and credentials.
- `IsAdmin = false`, `IsTenantAdmin = false`: regular user with tenant-scoped visibility plus self-service on their own user account and credentials.

### Key Concepts

| Term | Plain Language | Description |
|------|---------------|-------------|
| **Admiral** | Coordinator | The server process that orchestrates everything. Auto-starts when needed. |
| **Captain** | Agent/worker | An AI agent instance (Claude Code, Codex, etc.). Auto-created on demand. |
| **Fleet** | Group of repos | Collection of repositories. A default fleet is auto-created. |
| **Vessel** | Repository | A git repository registered with Armada. Auto-registered from your current directory. |
| **Mission** | Task | An atomic work unit assigned to a captain. |
| **Voyage** | Batch | A group of related missions dispatched together. |
| **Dock** | Worktree | A git worktree provisioned for a captain's isolated work. |
| **Signal** | Message | Communication between the Admiral and captains. |
| **Persona** | Agent role | A named agent role (Worker, Architect, Judge, TestEngineer) that determines what a captain does during a mission. Personas are extensible -- users can create custom personas with custom prompt templates. |
| **Pipeline** | Workflow | An ordered sequence of persona stages that a dispatch goes through (e.g. Architect -> Worker -> TestEngineer -> Judge). Configured at fleet/vessel level with per-dispatch override. |
| **Prompt Template** | Instructions | A user-editable template that controls the instructions given to agents. Every prompt in the system is template-driven with `{Placeholder}` parameters. |

For details on how the Admiral decides which mission to assign to which captain and in what order, see [Mission Scheduling](docs/SCHEDULING.md).

### Data Model

```
┌─────────────────────────────────────────────────────────────────┐
│                            ADMIRAL                              │
│                     (coordinator process)                       │
└────────┬──────────────┬──────────────┬──────────────┬───────────┘
         │              │              │              │
         ▼              ▼              ▼              ▼
    ┌─────────┐   ┌──────────┐  ┌──────────┐   ┌──────────┐
    │  Fleet  │   │ Captain  │  │  Voyage  │   │  Signal  │
    │ (flt_)  │   │  (cpt_)  │  │  (vyg_)  │   │  (sig_)  │
    │         │   │          │  │          │   │          │
    │ group   │   │ AI agent │  │ batch of │   │ message  │
    │ of repos│   │ worker   │  │ missions │   │ between  │
    └────┬────┘   └────┬─────┘  └────┬─────┘   │ admiral  │
         │             │             │         │ & agents │
         ▼             │             ▼         └──────────┘
    ┌──────────┐       │       ┌──────────┐
    │ Vessel   │◄──────┼───────│ Mission  │
    │ (vsl_)   │       │       │  (msn_)  │
    │          │       │       │          │
    │ git repo │       └──────►│ one task │
    └────┬─────┘       assigns │ for one  │
         │             captain │ agent    │
         ▼                     └──────────┘
    ┌──────────┐
    │   Dock   │
    │  (dck_)  │
    │          │
    │   git    │
    │ worktree │
    └──────────┘

    Relationships:
    Fleet  1──*  Vessel       A fleet contains many vessels (repos)
    Vessel 1──*  Dock         A vessel has many docks (worktrees)
    Voyage 1──*  Mission      A voyage groups many missions
    Mission *──1 Vessel       Each mission targets one vessel
    Mission *──1 Captain      Each mission is assigned to one captain
    Captain 1──1 Dock         A captain works in one dock at a time
```

### Data Flow

```
User Command (CLI / API / MCP)
    |
    v
Admiral receives command
    |
    +--> Creates/updates Mission in SQLite
    +--> Resolves target Vessel (repository)
    +--> Allocates Captain (find idle or spawn new)
    +--> Provisions worktree (git worktree add)
    +--> Starts agent process with mission context
    +--> Monitors via stdout/stderr + heartbeat
    |
Captain works autonomously
    |
    +--> Reports progress via signals
    +--> Admiral updates Mission status
    +--> On completion: push branch, create PR (optional)
    +--> Captain returns to idle pool
```

### Technology Stack

| Component | Technology | Notes |
|-----------|-----------|-------|
| Language | C# / .NET 8+ | Cross-platform |
| Database | SQLite, PostgreSQL, SQL Server, MySQL | SQLite default; zero-install, embedded |
| REST API | [SwiftStack](https://github.com/jchristn/swiftstack) | OpenAPI built-in |
| MCP/JSON-RPC | [Voltaic](https://github.com/jchristn/voltaic) | Standards-compliant MCP server |
| CLI | [Spectre.Console](https://spectreconsole.net/) | Rich terminal UI |
| Logging | [SyslogLogging](https://github.com/jchristn/sysloglogging) | Structured logging |
| ID Generation | [PrettyId](https://github.com/jchristn/prettyid) | Prefixed IDs (flt_, vsl_, cpt_, msn_, etc.) |

## CLI Reference

### Common Commands

```
armada go <prompt>           Quick dispatch (infers repo from current directory)
armada status                Dashboard (scoped to current repo)
armada status --all          Global view across all repos
armada watch                 Live dashboard with notifications
armada log <captain>         Tail a specific agent's output
armada log <captain> -f      Follow mode (like tail -f)
armada doctor                System health check
```

### Missions and Voyages

```
armada mission list|create|show|cancel|retry
armada voyage list|create|show|cancel|retry
```

### Entity Management

All commands accept names or IDs:

```
armada vessel list|add|remove
armada captain list|add|stop|stop-all
armada fleet list|add|remove
```

### Infrastructure

```
armada server start|status|stop
armada config show|set|init
armada mcp install|remove|stdio
```

### Examples

```bash
# Dispatch a single task in your current repo
armada go "Fix the null reference in UserService.cs"

# Dispatch three tasks in parallel
armada go "Add rate limiting; Add request logging; Add input validation"

# Work with a specific repo
armada go "Fix the login bug" --vessel my-api

# Register additional repos
armada vessel add my-api https://github.com/you/my-api
armada vessel add my-frontend https://github.com/you/my-frontend

# Add more agents (supports claude, codex, gemini, cursor)
armada captain add claude-2 --runtime claude
armada captain add codex-1 --runtime codex
armada captain add gemini-1 --runtime gemini

# Emergency stop all agents
armada captain stop-all

# Retry a failed mission
armada mission retry msn_abc123

# Retry all failed missions in a voyage
armada voyage retry "API Hardening"
```

## Configuration

Settings live in `~/.armada/settings.json` and are auto-created with sensible defaults on first use.

```bash
armada config show              # View current settings
armada config set MaxCaptains 8 # Change a setting
armada config init              # Interactive setup (optional)
```

| Setting | Default | Description |
|---------|---------|-------------|
| `AdmiralPort` | 7890 | REST API port |
| `MaxCaptains` | 0 (auto, defaults to 5) | Maximum total captains |
| `StallThresholdMinutes` | 10 | Minutes before a captain is considered stalled |
| `MaxRecoveryAttempts` | 3 | Auto-recovery attempts before giving up |
| `AutoPush` | true | Push branches to remote on mission completion |
| `AutoCreatePullRequests` | false | Create PRs on mission completion |
| `AutoMergePullRequests` | false | Auto-merge PRs after creation |
| `LandingMode` | null | Landing policy: `LocalMerge`, `PullRequest`, `MergeQueue`, or `None` |
| `BranchCleanupPolicy` | `LocalOnly` | Branch cleanup after landing: `LocalOnly`, `LocalAndRemote`, or `None` |
| `RequireAuthForShutdown` | false | Require authentication for `POST /api/v1/server/stop` |
| `TerminalBell` | true | Ring terminal bell during `armada watch` |
| `DefaultRuntime` | null (auto-detect) | Default agent runtime |

## Upgrading

### v0.1.0 to v0.2.0

**Breaking change:** The `settings.json` format has changed. Armada v0.2.0 will fail to start if you use a v0.1.0 `settings.json` without updating it.

The `databasePath` string property has been replaced with a `database` object that supports multiple database backends (SQLite, PostgreSQL, SQL Server, MySQL).

#### Before (v0.1.0)

```json
{
  "databasePath": "armada.db",
  "admiralPort": 7890,
  "maxCaptains": 5
}
```

#### After (v0.2.0)

```json
{
  "database": {
    "type": "Sqlite",
    "filename": "armada.db"
  },
  "admiralPort": 7890,
  "maxCaptains": 5
}
```

#### Minimal change for SQLite users

Replace:

```json
"databasePath": "path/to/armada.db"
```

With:

```json
"database": {
  "type": "Sqlite",
  "filename": "path/to/armada.db"
}
```

No other changes are required -- all other settings remain the same.

#### Switching to PostgreSQL

```json
"database": {
  "type": "Postgresql",
  "hostname": "localhost",
  "port": 5432,
  "username": "armada",
  "password": "your-password",
  "databaseName": "armada",
  "schema": "public",
  "minPoolSize": 1,
  "maxPoolSize": 25,
  "connectionLifetimeSeconds": 300,
  "connectionIdleTimeoutSeconds": 60
}
```

#### Switching to SQL Server

```json
"database": {
  "type": "SqlServer",
  "hostname": "localhost",
  "port": 1433,
  "username": "armada",
  "password": "your-password",
  "databaseName": "armada",
  "minPoolSize": 1,
  "maxPoolSize": 25,
  "connectionLifetimeSeconds": 300,
  "connectionIdleTimeoutSeconds": 60
}
```

#### Switching to MySQL

```json
"database": {
  "type": "Mysql",
  "hostname": "localhost",
  "port": 3306,
  "username": "armada",
  "password": "your-password",
  "databaseName": "armada",
  "minPoolSize": 1,
  "maxPoolSize": 25,
  "connectionLifetimeSeconds": 300,
  "connectionIdleTimeoutSeconds": 60
}
```

#### Additional notes

- **Port auto-detection:** Setting `port` to `0` (or omitting it) auto-detects the default port for each database type (PostgreSQL: 5432, SQL Server: 1433, MySQL: 3306).
- **Connection pooling:** All non-SQLite backends support connection pooling via `minPoolSize` (0-100), `maxPoolSize` (1-200), `connectionLifetimeSeconds` (minimum 30), and `connectionIdleTimeoutSeconds` (minimum 10).
- **Encryption:** Set `requireEncryption` to `true` to require encrypted connections for PostgreSQL, SQL Server, or MySQL.
- **Backup/restore:** The `armada_backup` and `armada_restore` MCP tools are only available when using SQLite. If you switch to PostgreSQL, SQL Server, or MySQL, use your database's native backup tools instead.

## Authentication

As of v0.3.0, Armada supports multi-tenant authentication with three methods:

| Method | Header | Description |
|--------|--------|-------------|
| **Bearer Token** (recommended) | `Authorization: Bearer <token>` | 64-character tokens linked to a tenant and user. Default token: `default` |
| **Session Token** | `X-Token: <token>` | AES-256-CBC encrypted, 24-hour lifetime. Returned by `POST /api/v1/authenticate` |
| **API Key** (deprecated) | `X-Api-Key: <key>` | Legacy. Maps to a synthetic admin identity. Migrate to bearer tokens |

The default installation works with `Authorization: Bearer default` -- no additional setup needed for single-user use.

For full details, see [docs/REST_API.md](docs/REST_API.md#authentication).

## REST API

The Admiral exposes a REST API on port 7890. All endpoints are under `/api/v1/` and require authentication (see above). All error responses use a standard format with `Error`, `Description`, `Message`, and `Data` fields -- see [REST_API.md](docs/REST_API.md#error-responses) for the full error code reference.

```bash
API="http://localhost:7890/api/v1"
AUTH="Authorization: Bearer default"

curl -H "$AUTH" $API/status              # System status
curl -H "$AUTH" $API/fleets              # List fleets
curl -H "$AUTH" $API/vessels             # List vessels
curl -H "$AUTH" $API/missions            # List missions
curl -H "$AUTH" $API/captains            # List captains
curl $API/status/health                  # Health check (no auth required)
```

Full CRUD endpoints are available for fleets, vessels, missions, voyages, captains, signals, events, tenants, users, and credentials.

Start the Admiral as a standalone server:

```bash
armada server start
```

## MCP Integration

Armada runs an MCP (Model Context Protocol) server with 18 tools, allowing Claude Code and other MCP-compatible clients to use Armada tools directly.

```bash
armada mcp install    # Configure Claude Code, Codex, Gemini, and Cursor for Armada MCP
armada mcp remove     # Remove those Armada MCP entries again
```

If you are working from source, repo-root helpers are also available:
`install-mcp.bat/.sh` and `remove-mcp.bat/.sh`.

Once installed, your MCP client can call tools like `armada_status`, `armada_dispatch`, `armada_enumerate`, `armada_voyage_status`, `armada_cancel_voyage`, and more. Additional tool categories include persona management, pipeline management, and prompt template management.

### AI-Powered Orchestration

Connect Claude Code, Codex, or any MCP-capable AI to Armada's MCP server and it becomes an AI-powered orchestrator -- the AI reasons about objectives, decomposes work into missions, dispatches voyages, monitors progress, and adapts dynamically, while Armada handles the infrastructure (worktrees, state machines, merge queues, health checks).

```
Claude Code (orchestrator) --MCP--> Armada Server --spawns--> Captain agents (workers)
```

This gives you the equivalent of an AI "Mayor" pattern without coupling AI reasoning into the infrastructure:

```
> "Refactor the authentication system. Decompose it into parallel missions
   and dispatch them via Armada. Monitor progress and redispatch failures."
```

Claude Code will research the codebase, identify components, design non-overlapping missions, call `armada_dispatch`, poll `armada_voyage_status`, and handle failures -- all autonomously.

For detailed setup and examples, see:
- [Claude Code as Orchestrator](docs/CLAUDE_CODE_AS_ORCHESTRATOR.md)
- [Codex as Orchestrator](docs/CODEX_AS_ORCHESTRATOR.md)

## Use Cases

### Solo Developer Multiplier

You're working on a feature and realize three preparatory refactors are needed. Instead of doing them sequentially:

```bash
armada go "1. Extract UserRepository from UserService 2. Add ILogger to all controllers 3. Migrate config to Options pattern"
```

Three agents work in parallel while you continue on your feature branch.

### Code Review Prep

Batch mechanical changes across a codebase before a review:

```bash
armada voyage create "Pre-review cleanup" --vessel my-api \
  --mission "Add XML documentation to all public methods in Controllers/" \
  --mission "Replace magic strings with constants in Services/" \
  --mission "Add input validation to all POST endpoints"
```

### Multi-Repo Coordination

Dispatch related work across multiple repositories:

```bash
armada go "Update the shared DTOs to include CreatedAt field" --vessel shared-models
armada go "Add CreatedAt to the API response serialization" --vessel backend-api
armada go "Display CreatedAt in the user profile component" --vessel frontend-app
```

### Prototyping and Exploration

Explore multiple approaches to a problem simultaneously:

```bash
armada voyage create "Auth approach comparison" --vessel my-api \
  --mission "Implement JWT-based authentication with refresh tokens" \
  --mission "Implement session-based authentication with Redis store" \
  --mission "Implement OAuth2 with Google and GitHub providers"
```

Review each branch, pick the winner, discard the rest.

### Bug Triage

Fan out investigation and fixes across reported issues:

```bash
armada go "Fix: login fails when email contains a plus sign" --vessel auth-service
armada go "Fix: pagination returns duplicate results on page 2" --vessel search-api
armada go "Fix: file upload silently fails for files over 10MB" --vessel upload-service
```

## Running Locally (without Docker)

### Prerequisites

- [.NET 8.0+ SDK](https://dot.net/download)
- At least one AI agent runtime on your PATH (Claude Code, Codex, Gemini, or Cursor)

### Build and Run

```bash
git clone https://github.com/jchristn/armada.git
cd armada

# Build the solution
dotnet build src/Armada.sln

# Run the server directly
dotnet run --project src/Armada.Server
```

The server starts on the following ports:

| Port | Protocol | Description |
|------|----------|-------------|
| 7890 | HTTP | REST API + embedded dashboard |
| 7891 | JSON-RPC | MCP server |
| 7892 | WebSocket | Real-time event hub |

Open `http://localhost:7890/dashboard` in your browser. Log in with the default credentials:

| Field | Value |
|-------|-------|
| Email | `admin@armada` |
| Password | `password` |

For API access, use `Authorization: Bearer default` in your requests.

Configuration is stored in `armada.json` in the working directory. On first run, Armada creates the SQLite database, applies migrations, and seeds default data automatically. No manual setup is required.

### Install the CLI (optional)

```bash
dotnet pack src/Armada.Helm -o ./nupkg
dotnet tool install --global --add-source ./nupkg Armada.Helm

# Then use the CLI from any directory
armada doctor
armada go "your task here"
```

### Run Tests

```bash
dotnet run --project test/Armada.Test.Unit
```

## Running Locally (with Docker)

Docker Compose runs the server and optional React dashboard in containers. No .NET SDK required on the host.

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) with Docker Compose v2

### Start

```bash
cd docker
docker compose up -d
```

### Services

| Service | Port | URL | Description |
|---------|------|-----|-------------|
| `armada-server` | 7890 | `http://localhost:7890/dashboard` | REST API, MCP, WebSocket, embedded dashboard |
| `armada-dashboard` | 3000 | `http://localhost:3000` | Standalone React dashboard |

Both dashboards connect to the same server. The embedded dashboard at port 7890 is always available. The React dashboard at port 3000 is an additional option for production deployments.

### Default Credentials

Same as the non-Docker setup:

| Field | Value |
|-------|-------|
| Email | `admin@armada` |
| Password | `password` |
| Bearer Token | `default` |

### Data Persistence

Docker volumes are mapped to `docker/armada/`:

```
docker/
├── armada/
│   ├── db/          # SQLite database (persistent across restarts)
│   └── logs/        # Server logs
├── server/
│   └── armada.json  # Server configuration
└── compose.yaml
```

To change settings, edit `docker/server/armada.json` and restart:

```bash
docker compose restart armada-server
```

### Factory Reset

To delete all data and start fresh (preserves configuration):

```bash
cd docker/factory

# Linux/macOS
./reset.sh

# Windows
reset.bat
```

### Stop

```bash
cd docker
docker compose down
```

### Build Images Locally

If you want to build the Docker images from source instead of pulling from Docker Hub:

```bash
# Build server image
docker build -f src/Armada.Server/Dockerfile -t armada-server:local .

# Build dashboard image
docker build -f src/Armada.Dashboard/Dockerfile -t armada-dashboard:local .
```

Build scripts for multi-platform images are also provided: `build-server.bat/.sh` and `build-dashboard.bat/.sh`.

## Upgrading / Migration

When upgrading between major versions of Armada, your `settings.json` configuration file may need to be updated to match the new format.

### v0.1.0 to v0.2.0

In v0.2.0, the flat `databasePath` string property was replaced with a structured `database` object that supports multiple database types, connection pooling, and additional configuration options.

#### Manual Migration

1. Back up your existing `settings.json`
2. Remove the `"databasePath"` property
3. Add a `"database"` object with your SQLite path and default pooling settings (see `settings.json.sample` for the full schema)

#### Automated Migration

For existing v0.1.0 deployments, run the migration script to automatically convert your `settings.json`:

**Windows:**
```
migrations\migrate_v0.1.0_to_v0.2.0.bat
# or with a custom path:
migrations\migrate_v0.1.0_to_v0.2.0.bat C:\path\to\settings.json
```

**Linux/macOS:**
```
./migrations/migrate_v0.1.0_to_v0.2.0.sh
# or with a custom path:
./migrations/migrate_v0.1.0_to_v0.2.0.sh /path/to/settings.json
```

The script backs up your original file to `settings.json.v0.1.0.bak` before making changes.

**Requires:** jq (Linux/macOS) -- install via `apt install jq`, `brew install jq`, etc.

### v0.2.0 to v0.3.0

v0.3.0 introduces multi-tenant support. The database schema is automatically migrated on first startup. Key changes:

- **New tables:** `TenantMetadata`, `UserMaster`, `Credential` are created automatically
- **Default data seeded:** A default tenant (`default`), user (`admin@armada` / `password`), and credential (bearer token `default`) are created if no tenants exist
- **All operational tables gain `TenantId`:** Existing rows are assigned to the `default` tenant during migration
- **All operational tables gain `UserId`:** Existing rows are assigned to the earliest user in their tenant during migration
- **Ownership integrity:** Operational `TenantId` and `UserId` columns are indexed and protected by database foreign keys across all supported backends
- **Protected auth resources:** The default tenant, its default user/credential, and the synthetic system records are seeded as protected and cannot be deleted directly
- **Role model:** `IsAdmin` now means global system admin. `IsTenantAdmin` means tenant-scoped admin. Regular users are limited to their own tenant, own account, and own credentials
- **Password management:** User create/update APIs accept plaintext `Password`; the server hashes it before persistence. Leaving `Password` blank on update preserves the existing password. The dashboard exposes this through the Users edit modal for both admin-managed and self-service password changes
- **Protected resources:** `IsProtected` is server-controlled on tenants, users, and credentials. Protected objects cannot be deleted directly, and immutable identifiers/timestamps/ownership fields are preserved on update
- **Tenant-created seed admin:** Creating a tenant also creates `admin@armada` with password `password` plus a default credential inside that tenant; that seeded user is tenant admin only (`IsAdmin = false`, `IsTenantAdmin = true`) and those child resources are protected from direct delete
- **Authentication required:** All REST API endpoints now require authentication. Use `Authorization: Bearer default` for backward-compatible access
- **`X-Api-Key` deprecated:** The `X-Api-Key` header still works but is deprecated. If configured, it maps to a synthetic admin identity. Migrate to bearer tokens
- **New settings:** `AllowSelfRegistration` (default: `true`), `RequireAuthForShutdown` (default: `false`), `SessionTokenEncryptionKey` (auto-generated)

No manual changes to `settings.json` are required. Existing `ApiKey` settings continue to work.

## Issues and Discussions

- **Bug reports and feature requests**: [Open an issue](https://github.com/jchristn/armada/issues) on GitHub. Please include your OS, .NET version, agent runtime, and steps to reproduce.
- **Questions and discussions**: [Start a discussion](https://github.com/jchristn/armada/discussions) on GitHub for general questions, ideas, or feedback.

When filing an issue, include:

1. What you expected to happen
2. What actually happened
3. Output of `armada doctor`
4. Relevant log output (`armada log <captain>`)

## License

Armada is released under the [MIT License](LICENSE.md). See the LICENSE.md file for details.
