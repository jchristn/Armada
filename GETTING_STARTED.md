# Getting Started with Armada

Go from zero to three AI agents working in parallel in under five minutes.

> **Security Note:** Armada runs AI agent captains with permission-bypassing flags enabled by default (e.g. `--dangerously-skip-permissions` for Claude Code, `--approval-mode yolo` for Gemini, `--force` for Cursor; Codex runs non-interactively in its `workspace-write` sandbox). Agents can read, write, and execute code without user confirmation. Be aware of this before proceeding; each captain's **Auto-approve agent tool use** switch (`autoApprove`) turns the flags off.

## Install

```bash
# Requires: .NET 8.0 or 10.0 SDK (https://dot.net/download)
# Requires: Claude Code on your PATH (https://docs.anthropic.com/en/docs/claude-code)

dotnet tool install -g Armada.Helm
```

Verify:

```bash
armada doctor
```

Helper scripts are available under `scripts/windows/`, `scripts/linux/`, and `scripts/macos/` if you are working from source. Shared shell implementations live under `scripts/common/`.

If you are installing from a source checkout instead of a published tool package, use the platform install script:

Linux: `./scripts/linux/install.sh`

macOS: `./scripts/macos/install.sh`

Windows: `scripts\windows\install.bat`

Those scripts build the solution, deploy dashboard assets, and install `Armada.Helm` as a global tool from the repo.

## Connect Claude Code

```bash
armada mcp install
```

This configures Armada MCP for Claude Code, Codex, Gemini, and Cursor (plus Mux and OpenCode when they are installed), and installs the Claude Code orchestrator agent. Use `armada mcp remove` to remove those entries later.

## Start the server

```bash
armada server start
```

This launches the Admiral in the background and returns once it answers its health check. `armada server status`
shows it and `armada server stop` stops it.

If you want Armada managed as a local deployment on your machine instead of a foreground terminal process, use the source-deployment scripts:

| Task | Linux | macOS | Windows |
|------|-------|-------|---------|
| Publish server and dashboard only | `./scripts/linux/publish-server.sh` | `./scripts/macos/publish-server.sh` | `scripts\windows\publish-server.bat` |
| Install and register a user-scoped local deployment | `./scripts/linux/install-systemd-user.sh` | `./scripts/macos/install-launchd-agent.sh` | `scripts\windows\install-windows-task.bat` |
| Update the deployed server from the current checkout | `./scripts/linux/update-systemd-user.sh` | `./scripts/macos/update-launchd-agent.sh` | `scripts\windows\update-windows-task.bat` |
| Verify the running deployment | `./scripts/linux/healthcheck-server.sh` | `./scripts/macos/healthcheck-server.sh` | `scripts\windows\healthcheck-server.bat` |
| Remove the startup-managed deployment | `./scripts/linux/remove-systemd-user.sh` | `./scripts/macos/remove-launchd-agent.sh` | `scripts\windows\remove-windows-task.bat` |

On Windows, the install and update wrappers also accept a framework override when the machine only has one SDK, for example `scripts\windows\install-windows-task.bat net8.0` or `scripts\windows\update-windows-task.bat --framework net8.0`.

These scripts publish `Armada.Server` into `~/.armada/bin` on Linux and macOS, or `%USERPROFILE%\.armada\bin` on Windows, and deploy dashboard assets into `~/.armada/dashboard` or `%USERPROFILE%\.armada\dashboard`.

The remove scripts unregister the user-scoped startup entry or service, but they do not delete the published files under `~/.armada` or `%USERPROFILE%\.armada`.

Repo-relative deployment script paths:

- Linux: `scripts/linux/install-systemd-user.sh`, `scripts/linux/update-systemd-user.sh`, `scripts/linux/healthcheck-server.sh`
- macOS: `scripts/macos/install-launchd-agent.sh`, `scripts/macos/update-launchd-agent.sh`, `scripts/macos/healthcheck-server.sh`
- Windows: `scripts/windows/install-windows-task.bat`, `scripts/windows/update-windows-task.bat`, `scripts/windows/healthcheck-server.bat`

## First mission with the setup wizard

The fastest way to see Armada land real work is the setup wizard in the dashboard. It opens on its own the first
time you sign in to an empty Armada (and later from Dashboard, Setup Wizard).

1. Open `http://localhost:7890/dashboard` and sign in with `admin@armada` / `password`. The first sign-in with the
   default password asks you to choose a new one (8+ characters); doing so also disables the `default` bearer token.
2. **Objective:** select Start Setup.
3. **Fleet:** keep "Armada Starter Fleet" and select Create Fleet.
4. **Vessel:** enter a name, the repository (a clone URL or a local path such as `~/code/hello`), and, to have
   finished work merged for you, the path of your local checkout as Working Directory with Landing Mode set to
   Local Merge. Local Merge merges into that checkout and pushes it to its `origin` remote, so the checkout needs
   one. Leave Landing Mode on None if you would rather review the branch yourself; the mission then stops at
   WorkProduced with its branch kept for you. Select Register Vessel.
5. **Captain:** the runtime defaults to Claude Code. Pick another runtime if that is the CLI you have installed and
   signed in, then select Create Captain.
6. **Dispatch:** the default mission only inspects the repository. For a first landed change, replace it with
   something small, for example "Append the line 'Armada was here.' to README.md and commit the change." Select
   Dispatch Mission.
7. **Handoff:** the status updates on its own every few seconds. With Local Merge it reaches Complete once the
   commit is merged into your checkout. Open Mission shows the diff and the captain's log.

Measured on macOS (2026-10-04) from a fresh data directory with Claude Code 2.1.289 and a one-commit repository:
the server was serving the dashboard 2 seconds after start, the wizard took about 3 seconds of clicking when driven
by a script (budget one to two minutes by hand to read and type), and the mission went from dispatch to Complete in
about 15 seconds. Installing the .NET SDK and the agent CLI and signing in to the CLI are not included; with those
done, the whole path fits comfortably in ten minutes.

---

## Planning Workflow

If you want to work out the plan with a captain before dispatching anything, use the dashboard planning screen:

```text
Dashboard Planning UI
    |
    +--> Reserve captain + dock/worktree
    +--> Chat with the captain inside the UI
    +--> Keep the transcript as the source of truth
    +--> Select the reply you want to use
    +--> Summarize it, open it in Dispatch, or dispatch directly
```

1. Start Armada and open `http://localhost:7890/dashboard`
2. Go to `Planning`
3. Pick a captain, vessel, optional pipeline, and playbooks
4. Chat until you have a plan you trust
5. Select the assistant response you want
6. Either summarize it into a cleaner draft, open that draft in the main `Dispatch` page, or dispatch it directly from the planning page
7. Delete the session when you no longer need the transcript, or let Armada clean it up through retention settings

Current planning-session constraints:

- Planning supports the CLI runtimes `ClaudeCode`, `Codex`, `Gemini`, `Cursor`, `Mux`, and `OpenCode`. `Custom` and `ApiEndpoint` captains and Harbor-hosted captains are blocked there.
- A planning session reserves the selected captain and a dock/worktree for the selected vessel until you stop the session.
- The captain can inspect and modify the repository while planning.
- Planning is transcript-backed today. Each turn relaunches the runtime with the preserved transcript and repo context instead of keeping a persistent interactive stdin session alive.
- Planning-session persistence is SQLite-first. Non-SQLite backends currently return an explicit unsupported response for planning-session endpoints.
- Armada can summarize a selected planning reply into a dispatch-ready draft before launch.
- You can open that draft in the main `Dispatch` page without copy/paste or dispatch directly from the planning screen.
- Optional cleanup controls are available through `PlanningSessionInactivityTimeoutMinutes` and `PlanningSessionRetentionDays`.

---

## Backlog Workflow

If you want to capture and refine future work before repository-aware planning starts, use the backlog surface:

```text
Dashboard Backlog UI
    |
    +--> Capture title, priority, rank, kind, and vessel links
    +--> Start captain-backed refinement (optional vessel)
    +--> Summarize and apply refinement output
    +--> Start repository-aware planning when vessel/captain are chosen
    +--> Dispatch implementation or draft a release from the same backlog item
```

1. Start Armada and open `http://localhost:7890/dashboard`
2. Go to `Backlog`
3. Create a backlog item with the title, description, and any known metadata
4. Optionally start `Refinement` from the backlog detail page and explicitly choose the captain that should refine the work
5. Send refinement messages until you have a scoped summary you want to keep
6. Apply the refinement summary back to the backlog item to update acceptance criteria, non-goals, rollout constraints, and backlog readiness
7. Start `Planning` once you are ready to choose the repository-aware vessel, captain, pipeline, and playbooks
8. Dispatch from planning or draft a release from the same backlog item without copy/paste

Important backlog rules:

- Backlog refinement is lighter than planning: it records a captain-backed transcript but does not provision a dock or mutate a repository by default.
- A backlog item does not need a vessel to start refinement, but it does need a vessel before repository-aware planning or dispatch can begin.
- The same backlog item stays linked to refinement sessions, planning sessions, voyages, releases, deployments, incidents, and history entries.
- The legacy `/api/v1/objectives/...` routes still work, but the dashboard and user-facing docs prefer `Backlog`.

CLI examples:

```bash
armada backlog list
armada backlog create --title "Stabilize release rollout" --priority P1 --backlog-state Inbox
armada backlog update obj_abc123 --kind Feature --target-version 1.1.0
armada backlog reorder obj_abc123 --rank 10
armada backlog show obj_abc123
```

See `docs/BACKLOG.md` for the full backlog, refinement, planning, REST, MCP, and Helm guide.

---

## Create a project

We'll create an empty repo and let Armada's agents build the whole thing.

```bash
mkdir ~/code/bookshelf && cd ~/code/bookshelf
git init
git commit --allow-empty -m "Initial commit"
```

If you want agents to push branches, add a remote:

```bash
# Create a repo on GitHub first, then:
git remote add origin https://github.com/you/bookshelf.git
git push -u origin main
```

A local-only repo works fine too - agents work in local worktrees.

## Launch the orchestrator

```bash
claude --agent armada
```

Everything below happens inside this Claude session.

---

## Register the project

> Create a fleet called "demo" and add a vessel named "bookshelf" pointing to ~/code/bookshelf.

Claude calls `create_fleet` and `add_vessel`. You'll see IDs like `flt_...` and `vsl_...` in the response.

## Scaffold the project

> Create a mission on bookshelf: "Initialize a Python project. Create pyproject.toml with FastAPI, uvicorn, and pytest as dependencies. Create src/main.py with a FastAPI app that has a GET /health endpoint returning {"status": "ok"}. Create a README.md with the project name and a one-line description. Run no tests yet."

One captain spins up, creates a worktree, builds the scaffold, and completes. This gives the parallel missions a foundation to build on.

> Check mission status.

Wait until it shows Complete.

## Dispatch a parallel voyage

Now three agents work simultaneously on non-overlapping parts of the codebase:

> Dispatch a voyage called "Core Features" to bookshelf with these missions:
>
> 1. "Book CRUD endpoints. Create src/models.py with a Book dataclass (id, title, author, year, isbn). Create src/books.py with an in-memory store and FastAPI router mounted at /books with GET (list all), GET /{id}, POST (create), PUT /{id} (update), DELETE /{id}. Return 404 for missing books. Import and include the router in src/main.py."
>
> 2. "Search endpoint. Create src/search.py with a FastAPI router mounted at /search. Add GET /search?q=term that searches books by title or author (case-insensitive substring match). Import the book store from src/books.py. Include the router in src/main.py."
>
> 3. "Test suite. Create tests/test_books.py with pytest tests using FastAPI TestClient. Test: create a book, get it by ID, list all books, update a book, delete a book, get a missing book returns 404. Create tests/test_search.py testing search by title, search by author, and empty results. Import the app from src/main.py."

Three captains spin up in isolated worktrees, each working on their own files.

## Monitor progress

> Check voyage status.

You'll see each mission's status - Pending, InProgress, or Complete.

> Show the diff for the book CRUD mission.

Review the code changes. You can do this while other missions are still running.

> Show the captain log for the search mission.

See what the agent is doing in real time.

## Review and land

Once all three missions show Complete:

> Show the diff for each completed mission.

Review the changes. When you're satisfied:

> Enqueue all completed mission branches to the merge queue, then process it.

Armada tests and merges each branch into main in order. Your project is built.

---

## Without the orchestrator

Everything above works from the CLI. No Claude Code required.

```bash
# Register
armada fleet add demo
armada vessel add bookshelf ~/code/bookshelf --fleet demo

# Quick dispatch from inside the repo
cd ~/code/bookshelf
armada go "Initialize a Python FastAPI project with a /health endpoint"

# Parallel voyage
armada voyage create "Core Features" --vessel bookshelf \
  --mission "Book CRUD endpoints..." \
  --mission "Search endpoint..." \
  --mission "Test suite..."

# Monitor
armada watch

# Review
armada diff msn_abc123
armada log captain-1
```

---

## Next Steps

**CLI reference**

```
armada go <prompt>             Dispatch a task (infers repo from CWD; repeat --task for several missions)
armada watch                   Live dashboard
armada diff [mission]          Review changes
armada log <captain|mission>   Tail agent output
armada status                  System overview
armada inbox                   Items awaiting your attention
armada health                  Vessel health
armada doctor                  Health check
armada tui                     Terminal UI

armada mission  list|create|show|cancel|restart|retry
armada voyage   list|create|show|cancel|retry
armada vessel   list|add|import|remove
armada captain  list|add|update|stop|stop-all|remove
armada fleet    list|add|remove
armada action   list|run|status|cancel
armada playbook list|add|show|remove
armada backlog  list|create|show|update|delete|reorder
armada server   start|status|stop|restart
armada config   show|set|init
armada mcp      install|remove|stdio
```

**Configuration** - `armada config show` to see all settings. Key ones: `MaxCaptains` (concurrent agents), `StallThresholdMinutes` (stall detection), `AutoPush`, `AutoCreatePullRequests`, `DefaultRuntime`.

**Web dashboard** - Built-in web UI with live dashboards, diff viewer, log viewer, and settings editor. Served by the Admiral server at `http://localhost:7890/dashboard/`.

**REST API** - Full CRUD on port 7890 under `/api/v1/`. See `docs/REST_API.md`.

**MCP tools** - 146 tools for fleets, vessels, voyages, missions, captains, signals, events, docks, the merge queue, backlog, fleet actions, and more. Any MCP client can orchestrate Armada. See `docs/MCP_API.md` and `docs/CLAUDE_CODE_AS_ORCHESTRATOR.md`.

---

## Running with Docker

If you prefer Docker over a local .NET SDK install:

```bash
cd docker/armada
ARMADA_INITIAL_ADMIN_PASSWORD='choose-a-password' docker compose up -d
```

This starts the Armada server on port 7890 (REST and the built-in dashboard; MCP on 7891), a standalone React dashboard container on port 3000, and the bundled observability stack. Open `http://localhost:7890/dashboard` or `http://localhost:3000` in your browser.

Set `ARMADA_INITIAL_ADMIN_PASSWORD` (8+ characters, not `password`) before `docker compose up`, in your shell or in a `.env` file next to `compose.yaml`: the Admiral listens on all interfaces in the container and refuses to start while the default password is in use. Sign in with `admin@armada` and that password; the `default` bearer token is disabled. Create a credential for scripts under Server > Credentials (the token is shown once).

Data is persisted in `docker/armada/db/`. To stop: `docker compose down`. To reset all data: run `docker/armada/factory/reset.sh` (or `reset.bat` on Windows).

See the [README](README.md#running-locally-with-docker) for full Docker details including volume layout, configuration, and building images from source.

---

## Authentication

All REST API endpoints except the health check (`GET /api/v1/status/health`) require authentication. Until the default admin password is changed, the default bearer token (`default`) provides backward-compatible access:

```bash
curl -H "Authorization: Bearer default" http://localhost:7890/api/v1/status
```

The dashboard login screen accepts the default email (`admin@armada`) and password (`password`); the first sign-in asks for a new password (`PUT /api/v1/account/password`), which also disables the `default` bearer token. After login, the dashboard uses encrypted session tokens automatically. While default credentials are in use the Admiral only listens on localhost (see [SECURITY.md](SECURITY.md)).

Creating a tenant through the admin UI/API also seeds a protected `admin@armada` user and default credential for that tenant.

`IsAdmin` is the global admin flag. `IsTenantAdmin` is the tenant-scoped admin flag. Tenant-created seeded admins are created with `IsAdmin = false` and `IsTenantAdmin = true`.

The effective access tiers are:

- `IsAdmin = true`: full system-wide access.
- `IsAdmin = false`, `IsTenantAdmin = true`: full access within the user's tenant, including user and credential management in that tenant.
- `IsAdmin = false`, `IsTenantAdmin = false`: regular-user access limited to tenant-scoped visibility plus self-service on that user's own account and credentials.

Operational records are owned by both tenant and user. Armada persists and indexes those ownership columns consistently across SQLite, PostgreSQL, SQL Server, and MySQL.

`IsProtected` is server-controlled for tenants, users, and credentials. Protected objects cannot be deleted directly, and immutable fields such as IDs, ownership columns, and creation timestamps are preserved by the API on update.

User creation and user updates accept a plaintext `Password` field. Armada hashes the password server-side before storing it. If `Password` is omitted on update, the existing password is preserved. The dashboard Users modal supports both admin-managed password resets and self-service password changes.

Server stop, restart, rebuild, and rollback (`POST /api/v1/server/...`) always require a global admin (`IsAdmin = true`); tenant admins and regular users cannot shut the server down through the REST API. The old `requireAuthForShutdown` setting is deprecated and ignored.

For production use, create additional users and credentials via the admin API or dashboard. See `docs/REST_API.md` for details.
