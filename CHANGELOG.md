# Changelog

All notable changes to Armada are documented in this file.

---

## Unreleased

Focus: Harbors -- detaching the Admiral from the developer's machine so it can run standalone (Local mode, today's default) or containerized/remote (Split mode) while agent CLIs, git, and worktrees keep executing on the host where the repositories and tool logins live.

### Harbors (host runners)
- Added the `Harbor` entity (`hbr_`): a registered host-side runner with advertised capabilities, connection status, capacity, and handshake-reported platform/architecture/protocol metadata. Persisted across SQLite, PostgreSQL, MySQL, and SQL Server (schema migration 62 adds the `harbors` and `harbor_capabilities` tables; schema migration 63 adds the `harbor_id`, `assigned_harbor_id`, `preferred_harbor_id`, and `required_capabilities` routing/affinity columns to docks, missions, and vessels).
- Harbor management REST API under `/api/v1/harbors`: list (plain array), register, read, update (`name`, `maxConcurrentJobs`, `enabled` only), delete, and enable/disable. Runtime state reported by the link is preserved server-side and is not operator-editable.
- Harbor management MCP tools: `get_harbor`, `create_harbor`, `update_harbor`, `delete_harbor`, and `set_harbor_enabled`, plus a new `harbors` entityType on the `enumerate` tool.
- Multi-Harbor router (`HarborRouter`) with dock affinity: the routing decision is made once when a dock is provisioned and pinned afterward, so a mission's later host operations stay on the Harbor that owns its dock. Selection precedence is affinity, then an eligible preferred Harbor (`vessel.preferredHarborId`), then capability (`vessel.requiredCapabilities` and the requested runtime), then least load under `maxConcurrentJobs`.
- Documented the Harbor link wire protocol in `docs/HARBOR_PROTOCOL.md`: an authenticated, client-to-server WebSocket the Harbor dials out to the Admiral, versioned by `HarborProtocol.Version` (1.0), carrying polymorphic `HarborMessage` frames for launch/stdin/kill/git and handshake/started/output/exited/gitResult/heartbeat/error.
- Added the `Armada.Harbor` host-runner app (Avalonia), configured from `~/.armada-harbor/settings.json` (`serverLinkUrl`, `dashboardUrl`, `harborId`, `name`, `capabilities`, `maxConcurrentJobs`, `accessKey`/`secret`).
- New guide `docs/HARBOR.md` covering Local vs Split mode, the link, installing and running the app, the management surfaces, and dock-affinity routing.
- Status: the Harbor entity, management REST + MCP APIs, wire-protocol contract, and host-runner app exist today; the live split-mode link transport (the server-side WebSocket endpoint that accepts Harbor links, credential auth on the upgrade, and remote captain-process delegation) is still being rolled out.

---

## v0.9.0

Focus: stickiness and reliability -- making Armada a daily driver through per-project customization, while eliminating the stuck-dock and dangling-handoff failure modes and hardening the orchestrator for multi-instance operation.

### Inbox MCP tool + broader "needs you" coverage
- Added an `inbox` MCP tool so agent harnesses can answer "is there anything waiting on me / that needs my attention / any action items from Armada?". It returns the same consolidated attention list as the dashboard's Needs You and the `armada inbox` CLI (REST: `GET /api/v1/inbox`), with counts and per-item kind/severity/title/detail/entity/href.
- Broadened the inbox definitions beyond missions + stalled captains to cover the full human-in-the-loop / human-out-of-the-loop set: missions in Review, landing-failed and failed missions, **failed merges**, **deployments pending approval**, **failed/verification-failed deployments**, and stalled captains. Purely informational events (completions, normal progress) are excluded.
- Documented in MCP_API.md (with the kind/severity table) and added the tool to every `INSTRUCTIONS_FOR_*` orchestrator reference.

### Dashboard navigation consolidation
- Regrouped the dashboard from ~35 nav destinations across 6 sections to ~13 workflow-grouped destinations, without removing any capability: every folded page is reachable as a tab, a filter, the notification bell, or the command palette, and every old route redirects.
- Ask Armada is now a standalone top-level nav item directly under Dashboard (the primary workflow interface), also reachable via a new Cmd/Ctrl+K command palette.
- New shared primitives: a URL-synced `Tabs` component, a top-bar `NotificationBell` (replacing the standalone Notifications page, which now redirects to Needs You), and the command palette.
- Consolidated surfaces: `Configuration` (Workflow Profiles, Project Profiles, Skills, Personas, Pipelines, Prompts, Playbooks), `Activity` (History, Requests, Events, Signals via a source filter, preserving the request inspector), `Delivery` (Deployments, Environments, Releases, Incidents, Checks, Runbooks), `Vessels` (Fleets and Workspace folded in), `Captains` (Docks tab), `Missions` (Voyages and Merge Queue tabs), and `Dispatch` (Backlog intake tab). Doctor moved to a Server > Diagnostics tab. Sidebar widened to 220px.

### Project profiles (foundation)
- Added the `ProjectProfile` entity (`ppf_`): a scoped aggregate (Global -> Fleet -> Vessel) that binds a project's pipeline, workflow profile, per-persona prompt overrides (`PersonaOverride`), and skills in one place, resolved with the same vessel/fleet/global precedence as workflow profiles
- `ProjectProfileService` validation and layered resolution; full REST CRUD under `/api/v1/project-profiles` (plus `/enumerate`, `/validate`, `/resolve/vessels/{vesselId}`) and MCP `enumerate` support for `project_profiles`
- Persisted across SQLite, PostgreSQL, MySQL, and SQL Server (schema migration 45)

### Layered persona resolution + diff preview
- Per-project persona overrides now take effect at dispatch: `MissionService` resolves the vessel's project profile and applies the matching `PersonaOverride` (swap the persona's prompt template and/or append per-project instructions) when building mission instructions -- best-effort, so a profile lookup never blocks dispatch
- Added `GET /api/v1/project-profiles/{id}/persona-preview/{persona}` returning the base and effective (override-applied) persona prompt, so the dashboard can render a live before/after diff (`PersonaPromptPreview`)
- Dashboard: Project Profiles list + detail pages, including the persona-override editor and the live base-vs-effective persona prompt diff

### Skills directory
- Added the `Skill` entity (`skl_`): a tenant-scoped directory of reusable, categorized, editable capability snippets, persisted across all four database providers (schema migration 46)
- Project profiles attach skills by id or name; `MissionService` injects the resolved skill content into mission prompts as a Skills section (best-effort)
- REST CRUD under `/api/v1/skills` (+ `/enumerate`) and MCP `enumerate` support for `skills`; dashboard Skills list + detail pages
- Editable expectations: persona output contracts remain editable via prompt templates, and per-project expectations are expressible through `PersonaOverride` additional instructions

### Visual pipeline builder + live run-mode
- Pipeline detail now shows a visual left-to-right stage flow (persona cards with review-gate and optional badges) alongside the existing low-code stage editor
- Live run-mode: dispatch a voyage that runs a pipeline against a chosen vessel directly from the pipeline page, then jump to the voyage to watch it

### Ask Armada (captain-backed conversational control)
- Ask Armada is now a real captain-backed chat, not a fixed intent layer: it dispatches each turn to a live captain over that captain's CLI runtime (Claude Code, Codex, Gemini, Cursor, Mux, or OpenCode), so the assistant can actually reason about and act on fleet state through the captain's Armada MCP tools rather than pattern-matching a fixed question set
- Per-turn telemetry in an `(i)` popover: time-to-first-token, streaming duration, tokens/sec, and completion/total token counts, sourced from real captain output via a shared `ChatTurnMetricsBuilder` (replacing the earlier wildly-inflated whole-context token estimates)
- Real streaming: Claude Code turns stream token-by-token via `stream-json` output; Mux protocol events are parsed and stripped from the transcript; Codex (which cannot token-stream from `exec`) shows an explicit non-streaming notice instead of appearing hung
- Replies stream to the browser over the Watson WebSocket, render Markdown, surface live tool-call activity, and show rotating waiting messages instead of a static "Thinking..."; optional show-thinking, an editable Ask Armada system prompt, and a Stop button to abort a turn
- Reliability: correctly detects a missing Armada MCP connection, and loads MCP servers for headless Mux so Ask Armada can call tools; a Clear-conversation control (trash icon beside Send) with a confirmation modal
- REST `POST /api/v1/ask`; Ask Armada is a standalone top-level nav destination

### Planning sessions unified with Ask Armada
- The planning Current Session chat now mirrors the full Ask Armada experience: the same reusable chat component, per-turn `(i)` metrics, Markdown rendering, tool-call activity, Stop button, and streaming (Claude Code planning turns stream token-by-token; Mux protocol events are stripped from the transcript)
- Recent Sessions is collapsible with a per-row action menu and a Delete All control (confirmation modal); Clear conversation moved to a trash icon beside Send with its own confirmation; the whole Current Session card is pinned so the transcript no longer scrolls the page
- Mission execution is explicitly non-streaming again: token streaming is used only in Ask Armada and Planning when the user opts in, never during mission runs

### Agent runtimes
- Added the OpenCode runtime (`opencode`) as a first-class captain, wired through `AgentRuntimeFactory`, with its failure-handling, admission, and auto-land cores brought to parity with the other runtimes
- Per-captain reasoning effort: an effort level stored on the captain is translated per runtime (Claude thinking tokens, Codex `model_reasoning_effort`, Mux `--effort`, OpenCode `--variant`)
- Model-tier routing for dispatch: missions can be routed to captains by model tier
- Prompt delivery hardened on Windows: the five CLI runtimes (Claude Code, Codex, Gemini, Cursor, OpenCode) now deliver the prompt on stdin instead of as a command-line argument, fixing multi-line prompts being truncated at the first newline by the npm `.cmd` wrappers
- `armada mcp install` now detects and configures Mux alongside Claude Code, Codex, Gemini, and Cursor

### Vessel context building
- Vessels gained a Build Context / Refine Context action: launch a chosen captain to write (or refine) the vessel's Model Context from a seeded, editable `vessel.build_context` prompt template plus optional operator notes, provisioning and reclaiming a dock for the run; the result is saved to the vessel's Model Context field
- Vessel row-click now opens the full Edit Vessel modal (the previous read-only detail modal was removed for that path)

### Background jobs
- Added the background-jobs feature end to end (fork-parity): a durable job entity with its own state machine, persisted across all four database providers, surfaced through a dashboard Jobs page

### Fork-parity reliability and delivery edges
- Vessel auto-land predicate + UI and captain quarantine types (backend + four-driver persistence), including MCP auto-land arguments and an un-quarantine path
- Resource-admission wiring and cross-runtime node reuse for launch scheduling
- Readable runtime logs and git anchors surfaced in the mission brief
- Objective-link parity for MCP dispatch so dispatched work stays tied to its objective
- Captured merge-conflict file lists on landing retry so the operator sees exactly what to fix

### Readable mission logs
- Mux writes per-token JSONL during a run; the mission log endpoint now renders that stream into a readable transcript instead of returning raw one-token-per-line JSON

### Operator experience
- Cross-platform `factory-reset` scripts for Windows, Linux, and macOS that stop the running server (escalating to a forced kill and verifying it exited) before wiping state, so a reset can no longer delete the database out from under a live server
- Setup wizard: Vessel and Captain steps sized to the viewport with pinned step actions (no scrolling to reach the register button), the wizard now reappears on an empty deployment even after a prior setup completed, and finishing the wizard lands on the Missions page
- A shared loading indicator is shown while lazy-loaded pages resolve, replacing the transient blank screen
- Mission History chart renders finished captain-work bars (work produced, PR open, testing, review, complete) in green

### Merge-queue cleanup tools
- Added `delete_merge` (delete a single terminal merge-queue entry) and `purge_merge_queue` / `purge_merge_entry` / `purge_merge_entries` (bulk-purge terminal entries, optionally filtered by vessel and status), leveraging the existing branch-cleanup path -- closing the gap with the mission and voyage purge tools

### In-browser dock terminal
- `WorkspaceService.ExecAsync` runs a shell command in a vessel's working tree (cross-platform: cmd.exe on Windows, /bin/sh elsewhere), bounded by a timeout that kills the whole process tree, with captured stdout/stderr and output caps
- REST `POST /api/v1/workspace/vessels/{vesselId}/exec` (tenant administrators only); dashboard Terminal panel on the Workspace page with command history; one-click open into a vessel workspace via the existing picker

### In-app review + diff
- `WorkspaceService.GetDiffAsync` returns a unified git diff of the working tree against HEAD (optionally scoped to one path); REST `GET /api/v1/workspace/vessels/{vesselId}/diff`
- Dashboard: a Review Diff panel on the Workspace page (line-colored unified diff) that, together with the existing file browser and changes list, completes in-app review
- Hardening: every workspace git invocation is now bounded by a 30s timeout that kills the process tree, and disables the pager and credential prompts, so a wedged git can no longer hang the diff/changes/status endpoints

### Needs-you inbox
- Added `InboxService`, a consolidated "needs you" inbox aggregating everything awaiting a human decision -- missions in review (overdue ones flagged critical), failed landings, failed missions, and stalled captains -- ordered most-urgent first with deep links
- REST `GET /api/v1/inbox`; dashboard "Needs You" page under Operations with severity counts and one-click navigation
- Monitoring and the flight recorder are served by the existing mission-history chart and event feed plus the Prometheus/Grafana telemetry stack added earlier in this release

### SDK and CLI propagation
- `ArmadaApiClient` (C# SDK) gained typed methods for project profiles, skills, the Ask assistant, the needs-you inbox, and the workspace terminal/diff endpoints
- Helm CLI gained `armada inbox` (with `--critical`) and `armada ask "<question>"` commands

### Landing retry conflict capture
- Added `IGitService.GetConflictedFilesAsync` (git diff --name-only --diff-filter=U) to list unmerged paths
- When `RetryLandingAsync` fails, the mission's failure reason now records the exact conflicting file list so the operator knows what to fix

### Maintainability
- Centralized four scattered inline mission-status checks in AdmiralService/CaptainService onto `MissionStateMachine.IsTerminalOrPostWork`, fixing a divergence where a recovery failure could fail a mission whose work already existed

### Per-step captain selection
- A persona now carries a default (preferred) captain (`Persona.DefaultCaptainId`). At dispatch, each pipeline step is pre-filled with that captain and an optional fallback tier; the choice applies to every mission of the persona in the voyage, fan-out included, via a per-voyage override (`Voyage.CaptainOverridesJson`) plus per-mission resolution (`Mission.RequestedCaptainId`)
- Assignment honors the preferred captain when it is idle (bypassing the `AllowedPersonas` fence), falls back to an idle captain at or above the fallback tier when it is busy (lowest eligible tier wins), routes normally when the preferred captain was deleted, and leaves the mission Pending when nothing satisfies the tier -- reusing the existing capability-tier routing
- Startup migration 55 adds `personas.default_captain_id`, `missions.requested_captain_id`, and `voyages.captain_overrides_json` across SQLite, PostgreSQL, MySQL, and SQL Server
- REST persona create/update and dispatch, and the MCP `create_persona` / `update_persona` / `dispatch` tools, accept `defaultCaptainId` and per-persona `captainAssignments` (with invalid-captain validation); mission reads expose both `requestedCaptainId` and the actual `captainId`
- Dashboard: a Default Captain picker on persona detail, per-step preferred-captain and fallback-tier pickers on Dispatch, a Preferred vs. Actual captain (with a "fell back to tier" indicator) on mission detail, capability-tier badges on the Captains table, and a capability-tier step in the setup wizard for out-of-the-box routing. See [docs/CAPTAIN_ROUTING.md](docs/CAPTAIN_ROUTING.md)

### Reliability
- Fixed stall detection: the process-liveness loop now refreshes a separate liveness timestamp instead of the output heartbeat, so a live-but-silent agent is still detected as stalled; added a configurable max-mission-runtime backstop for runaways
- Cross-platform process supervision: agent subprocesses are killed on Admiral shutdown, and PID-identity verification (via process start time) prevents a recycled PID from leaving a captain stuck Working
- Dangling pipeline handoffs (WorkProduced with an unprepared downstream stage) are re-driven automatically each health cycle
- Review-timeout watchdog releases the captain a forgotten review was pinning (mission and dock preserved for the reviewer); enforced global MaxConcurrentMissions ceiling
- Non-destructive dock repair and unstick operator tools (REST + MCP)
- Merge queue: background driver so entries land without a manual trigger, hard timeouts on git/test subprocesses (no more queue freeze), and multi-instance-safe processing via a durable coordination lease
- Centralized, tested mission state machine (single authoritative transition table + classifiers)

### Data
- Schema migration 44 across SQLite, PostgreSQL, MySQL, and SQL Server: dock state/lease, captain process-liveness, mission review deadline, merge-entry retry/lease, and a durable coordination-lease table; deterministic SQLite foreign-key enforcement

### Testing
- Migrated the entire test suite (~2,100 cases) to the runner-agnostic Touchstone framework: a shared descriptor library run by a console/CLI runner, an xUnit adapter, and an NUnit adapter, with reflection-based discovery and per-suite server isolation for end-to-end tests

### Cross-provider database parity
- The database test suite now runs against every supported provider from one configurable harness (`--db-type/--db-host/--db-port/--db-user/--db-pass/--db-name`, or the matching `ARMADA_TEST_DB_*` variables): SQLite in-process, and PostgreSQL/MySQL/SQL Server against a throwaway database. To keep the identical suite fast on the servers, the migrate-and-seed is paid once per run and each case resets a shared database (truncate + re-seed) instead of re-migrating. Added `scripts/{common,linux,macos,windows}/run-db-parity-tests` to run the whole matrix with one command
- Running the full suite against the server providers for the first time surfaced and fixed three real driver bugs the SQLite-only tests never exercised: UTC timestamps were read back shifted by the host's timezone offset on PostgreSQL and MySQL (interpreted as local instead of UTC); MySQL additionally lost sub-second precision on every timestamp read (round-tripped through a fractional-second-less string); and deleting a captain referenced by a signal threw a foreign-key error on SQL Server (the null-on-delete that PostgreSQL does at the DB level was missing). The full Database suite now passes on SQLite, PostgreSQL, MySQL, and SQL Server

### Observability
- OpenTelemetry telemetry export (opt-in via `telemetry` settings): the Admiral hosts an OTel pipeline that exports reliability metrics to an OTLP collector, an in-process Prometheus scrape endpoint, and/or Loki; the core libraries emit through the base class library and take no telemetry-framework dependency
- Reliability counters under the `Armada` meter (stalls, recoveries, mission failures, runaway force-fails, overdue reviews, handoff re-drives, dock provision/reclaim, merge-queue processing)
- Docker stack ships Prometheus, Loki, and Grafana services with pre-provisioned datasources and an "Armada Reliability" dashboard; see [docs/TELEMETRY.md](docs/TELEMETRY.md)

### Dependencies
- Updated all dependencies, including the breaking Voltaic 0.6.0 MCP API (RpcParameters-based tool registration) and Watson 7.1.0

## v0.8.0

Focus: backlog-first delivery management.

### Backlog and Objectives
- Added normalized first-class objective storage with ranked backlog metadata, lifecycle fields, source lineage, and continued `objective.snapshot` event emission
- Added backlog alias REST routes, ranked reorder support, dashboard/.NET client request models, and MCP backlog CRUD plus reorder aliases
- Added objective refinement sessions and transcript messages with explicit captain selection, captain availability checks, summary generation, apply-to-objective support, and server startup wiring

### Delivery Lineage
- Added automatic objective linkage through deployment and incident create/update flows, including inference from linked release, mission, voyage, and deployment context
- Added deployment and incident objective-link helpers so the same objective remains the record of truth as work moves from release into rollout and response

### Release and Migration
- Bumped shared product/package metadata to `0.8.0` across .NET, Helm, dashboard, Postman, and current-version API/documentation surfaces
- Added versioned `v0.7.0 -> v0.8.0` migration handoff scripts with backlog/objective and refinement table guidance for all supported backends
- Updated schema/version verification coverage for the new backlog schema baseline

---

## v0.7.0

Focus: remote access.

### Remote Access
- Added an experimental outbound remote-control tunnel foundation in `Armada.Server`
- New `RemoteControl` settings are persisted in `settings.json` and exposed through `GET/PUT /api/v1/settings`
- Health and status responses now expose `RemoteTunnel` telemetry including state, instance ID, latency, and last error
- React dashboard, legacy dashboard, and `armada status` now surface remote tunnel configuration and live state
- Added request/response handling and server event forwarding on the tunnel contract
- Added `Armada.Proxy` with websocket tunnel termination, instance summaries, recent-event inspection, and live `armada.status.snapshot` / `armada.status.health` forwarding
- Added focused tunnel-backed remote inspection routes for recent activity, missions, voyages, captains, logs, and diffs
- Added bounded tunnel-backed management routes for fleets, vessels, voyages, missions, and captain stop
- Added a proxy-hosted remote operations shell at `/` for mobile-first remote triage, fleet and vessel management, voyage dispatch, mission editing, and captain control
- Added `docs/TUNNEL_PROTOCOL.md`, `docs/PROXY_API.md`, and `docs/TUNNEL_OPERATIONS.md` for the shipped tunnel and proxy contract

### Runtime and Hosting
- Updated the embedded server stack to Watson Webserver 7 for both HTTP and WebSocket handling
- Removed the standalone `WatsonWebsocket` dependency in favor of Watson 7's built-in WebSocket capability
- Fixed interactive server startup so `update.bat` and normal foreground launches no longer hang on startup handoff

### Dashboard and UX
- Reworked the setup wizard into a contained first-run workflow that uses dispatch directly instead of sending users into separate dashboard pages
- Expanded server settings with remote tunnel controls, MCP client references, system path inspection, database backup actions, and clearer hover guidance
- Added press-and-hold reveal controls for remote-control secrets and other protected login/setup inputs
- Added a full playbook management surface in the dashboard with list, detail, editing, delete, and ordered selection UX on voyage dispatch flows
- Added explicit success and warning toast feedback across dashboard mutation flows so save, delete, cancel, stop, and update actions acknowledge completion visibly
- Added a first-class `Workspace` experience with vessel-aware file browsing, editing, search, git status, context curation, and direct planning/dispatch handoff
- Added `System > Requests` and `System > API Explorer` so captured REST traffic, OpenAPI-backed live execution, and replay all live inside the Armada dashboard
- Added a first-class `Delivery` section in the dashboard for workflow-profile management, structured check-run inspection, and release drafting/detail flows
- Added first-class `Operations > Objectives`, `Delivery > Environments`, `Delivery > Deployments`, `Activity > Incidents`, and `System > Runbooks` dashboard surfaces for scoping, rollout, incident response, and guided operational execution
- Added `Activity > History` with saved views and export so cross-entity delivery memory spans objectives, planning, dispatch, checks, releases, deployments, incidents, events, merge activity, and request history

### Playbooks
- Added tenant-scoped markdown playbooks with CRUD across REST, MCP, proxy remote management, dashboard, CLI, SDK, and Postman
- Voyages and standalone missions can now carry ordered playbook selections with per-selection delivery mode: `InlineFullContent`, `InstructionWithReference`, or `AttachIntoWorktree`
- Mission dispatch now snapshots selected playbooks and injects them into mission instructions with resolved path metadata when file-based delivery is requested
- Added playbook persistence tables and schema migration support for SQLite, PostgreSQL, SQL Server, and MySQL
- Added reproducible mission-time storage of playbook filename, markdown content, selection order, and resolved delivery mode so later playbook edits do not rewrite historical execution context
- Added dashboard selection tooling that scales to larger playbook libraries through filtering, batch add/remove, and explicit ordering controls instead of one-card-per-playbook dispatch UI

### Internationalization
- Added a dashboard locale runtime, translation catalog, and persistent language selection available from login and the authenticated shell
- Added initial translations for English, Spanish, Simplified Chinese, Traditional Chinese, Cantonese, Japanese, German, French, and Italian
- Localized shared shell surfaces including login, pagination, notifications, setup wizard flows, and server/settings management views
- Expanded route-level coverage across list, detail, admin, and setup flows so Spanish no longer falls back to English on common table headers, filters, actions, and confirmations
- Routed legacy dashboard confirms, alerts, toasts, pagination affordances, and key static view copy through the shared i18n runtime so non-React surfaces honor the selected locale
- Added locale-aware date, time, and number formatting for dashboard runtime data
- Extended localization coverage to newer operational pages and shared controls so the playbook, dispatch, and administrative flows follow the same runtime and persistence model as the rest of the dashboard

### Planning, Runtimes, and API Tooling
- Added planning-session REST endpoints for list, create, transcript detail, message turns, summarize-to-dispatch, direct dispatch, stop, and delete flows
- Added persistent request-history capture, summaries, scoped delete flows, and replay metadata across SQLite, PostgreSQL, SQL Server, and MySQL
- Added Mux captain runtime integration, Mux endpoint/config support on captains, and runtime helper APIs for saved endpoint discovery
- Added live OpenAPI publishing at `/openapi.json` and `/swagger` to back the dashboard API Explorer and external tooling

### Delivery Workflows
- Added workflow profiles as first-class vessel/fleet delivery recipes for lint, build, unit test, integration test, e2e test, package, publish artifact, release versioning, changelog, deploy, rollback, smoke-test, and health-check flows
- Added workflow-profile validation, scope-aware default resolution, and required secret/config reference declarations across SQLite, PostgreSQL, SQL Server, and MySQL
- Added workflow-profile CRUD, validation, resolve, and enumerate APIs plus `ArmadaApiClient` support and dashboard list/detail/edit flows
- Added vessel readiness, setup-checklist onboarding, and typed workflow-input preflight across Workspace, vessel detail, Planning, Dispatch, and Checks
- Added structured check runs with durable status, timings, logs, artifacts, parsed test/coverage summaries, compare-to-previous-run analysis, retry, branch/commit metadata, and mission/voyage/release linkage
- Added check-run execute/import/read/retry/delete/enumerate APIs, dashboard list/detail flows, and launch hooks from Workspace, vessel detail, mission detail, voyage detail, and release detail
- Added first-class release records with version inference, draft/candidate/shipped state, artifact aggregation, linked work, and refreshable derived notes
- Added first-class objective records with linked vessels, planning sessions, voyages, checks, releases, deployments, incidents, and acceptance criteria
- Added first-class environments and deployments with approval, verification, rollback, request-history evidence, and default-environment seeding on startup
- Added first-class incidents, hotfix handoff, and playbook-backed runbooks with execution history
- Added optional server-global `GitHubToken` configuration plus per-vessel `GitHubTokenOverride` fallback with write-only update semantics, request-history redaction, and `hasGitHubTokenOverride` read models across REST, MCP, WebSocket, and dashboard surfaces
- Added pull-based GitHub delivery integration for objective import from issues or PR scope, GitHub Actions sync into structured checks, and GitHub PR review/check evidence on mission and release detail surfaces
- Added MCP enumeration support for `workflow_profiles`, `check_runs`, `releases`, `objectives`, `deployments`, `incidents`, `runbooks`, and `runbook_executions`
- Added MCP delivery and operations tools for `run_check`, `get_check_run`, `retry_check_run`, `create_release`, `get_release`, `create_objective`, `get_objective`, `create_deployment`, `get_deployment`, `approve_deployment`, `verify_deployment`, `rollback_deployment`, `get_runbook`, `get_runbook_execution`, and `start_runbook_execution`
- Added WebSocket delivery and operations events for `check-run.changed`, `objective.changed`, `deployment.changed`, `deployment.progress`, `environment.health`, and `approval-needed`

### Release and Docs
- Updated shared release metadata, docker tags, Postman examples, REST docs, MCP docs, and WebSocket docs to reflect the shipped objective, environment, deployment, incident, runbook, and history surfaces
- Promoted the shipped remote-management guide into `docs/REMOTE_MGMT.md` and archived the earlier planning doc
- Added no-op `v0.6.0 -> v0.7.0` migration scripts to reflect the release even though no database schema change is required
- Updated README and operator docs for the new playbook lifecycle, delivery modes, workflow profiles, readiness/onboarding, structured checks, releases, history, remote playbook management flows, workspace, request-history, planning-session, Mux, and internationalized dashboard behavior
- Expanded database, automated, MCP, WebSocket, request-history, and dashboard Vitest coverage around workflow profiles, checks, releases, and history

---

## v0.5.0

Focus: dispatch and pipeline stability.

### Dispatch and Pipeline Stability
- Hardened architect-to-worker handoff behavior, mission status freshness, branch cleanup, worktree cleanup, and landing paths
- Improved mission and voyage telemetry so active work reports current state more reliably
- Tightened dock/worktree safety to prevent dirty fresh docks and stale branch leakage

### Captains and Runtime Selection
- Added optional `Model` on captains across SQLite, MySQL, PostgreSQL, and SQL Server
- Captain model selection is exposed through dashboard, REST, MCP, and Postman examples
- Runtime launches now pass the configured model where supported, otherwise the runtime chooses its default
- Captain create/update now validates configured models before saving and returns a user-facing error when the model is invalid or unavailable

### Missions and Pipeline Reliability
- Added `TotalRuntimeMs` on missions, surfaced in API responses and the mission detail dashboard
- Mission create/update now touch parent voyage `LastUpdateUtc` so active voyages report fresh status
- Architect handoff text now strips trailing `[ARMADA:*]` control markers before passing instructions downstream
- Worktree creation now fails fast if a fresh dock is dirty, preventing unrelated tracked-file contamination
- Dock and mission branch cleanup was hardened across no-op landing and published-server worktree reclamation paths

### Git and Landing
- Worktree branch creation now creates the branch ref before attaching the worktree and keeps existing-branch docks on the named branch
- Merge handling now retries with `--allow-unrelated-histories` when needed
- Diff capture now falls back cleanly when there is no merge base instead of producing an empty snapshot
- Architect-only branches are cleaned up after successful fan-out instead of lingering indefinitely

### Dashboard and Docs
- Captain detail now supports editing and displaying the configured model
- Mission detail now uses a four-column layout and shows total runtime
- Login secret inputs now support a press-and-hold reveal control
- Dispatch page no longer shows the redundant detected-task UI or stale task-splitting guidance
- README, REST API, MCP API, compose.yaml, and release metadata are updated for `v0.5.0`

---

## v0.4.0

### Personas and Pipelines
- Added personas: named agent roles (Worker, Architect, Judge, TestEngineer) with custom persona support
- Added pipelines: ordered sequences of persona stages (WorkerOnly, Reviewed, Tested, FullPipeline) with custom pipeline support
- Pipeline resolution: dispatch param > vessel default > fleet default > WorkerOnly
- Architect stage special handling: parses [ARMADA:MISSION] markers to create multiple Worker missions
- Stage handoff: injects prior stage output (agent stdout + diff) into next stage description
- Persona-aware captain routing: AllowedPersonas and PreferredPersona on captains
- Mission dependency chain: DependsOnMissionId gates assignment until predecessor completes

### Prompt Templates
- Every prompt is now template-driven and user-editable (18 built-in templates)
- Categories: mission, persona, structure, commit, landing, agent
- Dashboard two-column editor with parameter reference panel
- MCP tools: get/update/reset_prompt_template
- REST endpoints: /api/v1/prompt-templates CRUD

### Dashboard
- Personas, Pipelines, Prompt Templates pages
- Pipeline dropdown on Dispatch, Voyage Create, Vessel, Fleet
- Mission detail: persona badge, depends-on link, failure reason display
- Captain detail: AllowedPersonas, PreferredPersona fields
- Vessel edit: 95% width, 3-column layout
- Log viewer: LIVE/DONE indicators, follow mode
- Toast notifications instead of layout-shifting banners
- Consistent CopyButton component across all pages
- Version display on login and sidebar

### Infrastructure
- Schema migrations 19-23 across SQLite, MySQL, PostgreSQL, SQL Server
- FailureReason field on missions (surfaced in dashboard)
- Vessel deletion cleanup: cancels missions, deletes docks, bare repo
- Empty repo auto-seed: creates README.md on first dispatch to empty GitHub repo
- Process.Dispose on agent exit to release Windows directory handles
- Crash logging: AppDomain.UnhandledException + TaskScheduler.UnobservedTaskException
- Case-insensitive email login
- CLAUDE.md auto-gitignored in worktrees

### API
- 11 new MCP tools (persona, pipeline, prompt template CRUD)
- 17 new REST endpoints
- 12 new WebSocket commands
- enumerate supports personas, prompt_templates, pipelines
- dispatch accepts pipelineId and pipeline (name) parameters
- Voyage status considers LandingFailed, WorkProduced, PullRequestOpen as terminal

### Documentation
- PIPELINES.md: complete implementation reference
- PERSONAS_GUIDE.md: user-facing guide
- TESTING_PIPELINES.md: 6 end-to-end test examples
- OLLAMA_AS_CAPTAIN.md: implementation plan for Ollama runtime
- VLLM_AS_CAPTAIN.md: implementation plan for vLLM runtime

---

## v0.3.0

### Added

- **Multi-tenant support** -- all operational data (fleets, vessels, captains, missions, voyages, docks, signals, events, merge entries) is scoped by tenant
- **Tenant, user, and credential models** -- `TenantMetadata` (`ten_` prefix), `UserMaster` (`usr_` prefix), `Credential` (`crd_` prefix)
- **Bearer token authentication** -- 64-character random alphanumeric tokens linked to a specific tenant and user, sent via `Authorization: Bearer <token>` header
- **Encrypted session tokens** -- AES-256-CBC self-contained tokens with 24-hour lifetime, sent via `X-Token` header. No server-side session storage required
- **Authentication endpoints** -- `POST /api/v1/authenticate`, `GET /api/v1/whoami`, `POST /api/v1/tenants/lookup`
- **Onboarding endpoint** -- `POST /api/v1/onboarding` for self-registration (gated by `AllowSelfRegistration` setting)
- **Tenant CRUD endpoints** -- `GET/POST/PUT/DELETE /api/v1/tenants` (admin only, with self-read for non-admins)
- **User CRUD endpoints** -- `GET/POST/PUT/DELETE /api/v1/users` (admin only, with self-read for non-admins)
- **Credential CRUD endpoints** -- `GET/POST/PUT/DELETE /api/v1/credentials` (admins: all; non-admins: own credentials)
- **Admin vs non-admin access patterns** -- three-tier authorization: `NoAuthRequired`, `Authenticated`, `AdminOnly`
- **Default data seeding** -- on first boot, creates default tenant, user (`admin@armada` / `password`), and credential (bearer token `default`)
- **React dashboard** -- standalone React dashboard (`Armada.Dashboard`) as a separate deployment option for Docker/production
- **Docker Compose with dashboard** -- `compose.yaml` runs `armada-server` and `armada-dashboard` containers together
- **SQL Server support** -- added SQL Server as a database backend option alongside SQLite, PostgreSQL, and MySQL
- **`AllowSelfRegistration` setting** -- controls whether `POST /api/v1/onboarding` is enabled (default: `true`)
- **`SessionTokenEncryptionKey` setting** -- AES-256 key for session token encryption (auto-generated if not provided)

### Changed

- All REST API endpoints now require authentication (except health check, authenticate, tenant lookup, onboarding, and dashboard routes)
- All operational database queries are tenant-scoped for non-admin users
- Admin users see all data across all tenants
- CORS headers now include `Authorization` and `X-Token` in `Access-Control-Allow-Headers`

### Deprecated

- **`X-Api-Key` header** -- retained for backward compatibility but deprecated. When configured, the server creates a synthetic admin tenant (`ten_system`) and user (`usr_system`). Migrate to bearer tokens for new integrations

---

## v0.2.0

### Added

- Multi-database support (SQLite, PostgreSQL, MySQL) with connection pooling
- Structured `database` object in `settings.json` replacing flat `databasePath`
- Migration scripts for v0.1.0 to v0.2.0 settings conversion
- Merge queue system for automated branch merging
- WebSocket hub for real-time event streaming
- Embedded dashboard at `/dashboard`
- MCP server with full API parity (18 tools)
- Batch delete operations for all entity types
- Enumeration (POST) endpoints with JSON body filtering
- Mission diff and log retrieval endpoints
- Captain log streaming
- Dock (worktree) management endpoints
- Signal system for Admiral-captain communication
- Event audit trail

### Changed

- Settings format: `databasePath` string replaced with `database` object (breaking change)

---

## v0.1.0

### Added

- Initial release
- Core orchestration: fleets, vessels, captains, missions, voyages
- Git worktree isolation for parallel agent work
- Multi-runtime support: Claude Code, Codex, Gemini, Cursor
- Auto-recovery for crashed agents
- REST API on port 7890
- CLI (`armada`) with Spectre.Console
- SQLite database backend
- Zero-config startup with auto-detection
