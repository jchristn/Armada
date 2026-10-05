# Changelog

All notable changes to Armada are documented in this file.

---

## Unreleased

### Test runs no longer raise desktop notifications

- `NotificationService` (used by `armada watch`) runs its platform command through `INotificationCommandRunner`; the test suite records the command instead of running it. Before, every full test run sent four real "Test Title" notifications through `osascript`, which macOS shows as coming from Script Editor. The tests now check the exact command and escaping for macOS, Linux, and Windows.

### Structured MCP tool errors

- Every MCP tool error now carries a machine-readable `ErrorCode` (`NotFound`, `InvalidArgument`, `Conflict`, `Forbidden`, `Unavailable`, `Failed`) next to the English `Error`; existing `Code` and `StatusCode` fields are kept. Exceptions map to codes by type, never by message.
- `Mcp.ToolCallsPerSecond` setting (default 100, 0 disables) for the per-client MCP tool call limit.
- `McpToolClient.CallToolResultAsync` returns the deserialized tool result with its `isError` flag.
- Tests: the MCP tenant isolation suite decides from `ErrorCode`, `isError`, and typed counts instead of matching reply text, no longer converts exceptions into sentinel strings, and lifts the rate limit instead of retrying on the words "rate limit". The API surface generator documents data-directory defaults as `~/.armada` wherever it runs.

### Terminal UI: Operations, Delivery, Configuration, Activity, and System screens

- Build: Vessels with branches, build context and bulk fleet actions; the import wizard with background discovery, fleet recommendations and history; Vessel Health with server filters and the health inspector; the vessel page and onboarding; Fleets; the Workspace (file tree, editor, terminal, diff, context, search); Captains with Mux settings, the tools viewer and readable logs; and Docks. Every dashboard route now opens a real TUI screen.
- Fixed: `Armada.Client` treated a 409 from vessel health evaluation (already running) as an error; TUI go-to sequences such as `g s` did not work from a list screen.
- Operations: Home, Needs You, Planning (live streaming transcript, dispatch from a session), Dispatch (pre-fill from Planning, Backlog, Incident, Workspace), Backlog and Backlog item (GitHub import, refinement sessions), Fleet Actions (runs, live run detail, target drawer), Missions and Mission detail (diff, log, review, transition, landing preview, PR panel), Voyages, Voyage detail, Create Voyage, Merge Queue and entry detail, and Jobs.
- Delivery (Deployments, Environments, Releases, Incidents, Checks, Runbooks) and Configuration (Workflow Profiles, Project Profiles, Skills, Playbooks, Endpoints, Harbors, Memory, Personas, Pipelines, Prompts) with list, detail, and form screens.
- Activity (All Activity with saved views and JSON/CSV/Markdown export, API Requests with replay, Events, Signals, Token Usage), API Explorer, Settings for every Server section (backup and restore to files, restart, stop, factory reset, rebuild with live log and rollback), Diagnostics, Tenants, Users, Credentials, and the setup wizard.
- Shared list and detail screens: filters, sorting, paging, bulk actions, typed confirmations, View JSON, `$EDITOR` for long text, live WebSocket updates. Alt+Left/Right history works while a list has focus; the key after a programmatically closed dialog is no longer lost.
- Client: `GetVoyageDetailAsync` and the Data Retention settings group.
- TUI telemetry, off by default: a `Telemetry` section in `tui.json` (the server's fields; defaults `armada-tui`, no scrape endpoint) exports the TUI's `armada_tui_*` metrics (sessions, screen views by route pattern, commands by id/source/outcome, approval decisions and latency, Ask messages), `armada.tui.command` spans, and TUIKit's meter and activity source through the Admiral's telemetry host. See `docs/TUI.md`.
- README: a Terminal UI section with 120x40 text captures (login, Ask Armada, Home, Missions, Approvals) in `docs/tui-screens`, rendered by the `Tui.ReadmeFrames` suite.
- Verified `armada tui` ships in every channel that ships Helm: the `Armada.Helm` .NET tool (also used by the install scripts) and the Linux `.deb`/`.rpm` CLI packages carry `Armada.Tui`, `TUIKit`, and `Armada.Client`. The macOS `.pkg`, Windows `.msi`, Harbor installers, and Docker image do not include the CLI.

### v1.0 readiness: install verification

- Install verification (`scripts/common/install-verify/`, `.github/workflows/install-verify.yml`): Docker compose, the Linux `.deb` (Ubuntu 24.04) and `.rpm` (Fedora 42), the NuGet global tool (Linux, macOS, Windows), and the macOS server `.pkg` are installed on a clean runner or container, log in, load the dashboard, and dispatch one mission on an API-endpoint captain backed by a stub inference server; Windows also runs `--install-service --dry-run`. See "Install verification" in `docs/RELEASING.md`.
- Fixed: `armada server start` from the NuGet global tool reported "Admiral server executable not found" (it looked next to the tool shim, not in the tool store); it now runs `Armada.Server.dll` through the dotnet host, and also finds `armada-server` from the server packages on the PATH.
- Fixed: the `.pkg`, `.deb`/`.rpm`, `.msi`, and NuGet tool installs served the legacy embedded pages at `/dashboard` instead of the React dashboard. The server publish now carries the React build in `dashboard/`, and the single-file server finds it (it looked beside an empty assembly location).
- Fixed: the Linux `.deb`/`.rpm` packages declared no dependencies, so the server aborted on a clean machine ("Couldn't find a valid ICU package") and had no git. They now depend on ICU, OpenSSL, ca-certificates, tzdata, and git; the `.deb` stages subdirectories of the publish.
- Fixed: the Docker server image had no git, so every mission it ran stayed Pending (dock provisioning failed on each retry); it now includes git and serves the React dashboard at `/dashboard`.
- Fixed: the standalone dashboard container was always unhealthy (its healthcheck probed port 80; nginx listens on 8080) and could not work: the build's `/dashboard/` asset paths were served at the root, and API and WebSocket calls went to nginx. It now serves the build under `/dashboard/` and proxies everything else to `ARMADA_SERVER_URL`.
- Fixed: a captain that finished before its launch was recorded (an API-endpoint captain against a fast or failing endpoint) left the mission stuck InProgress. The exit handler now waits for the launch to be recorded, and the launch no longer overwrites a state the exit already reached.

### v1.0 readiness: quality, performance, and service registration

- Added `--install-service`, `--uninstall-service`, and `--run-service` to the Admiral (Windows Service, systemd unit, launchd agent) and `--install-startup`/`--uninstall-startup` to Harbor (Run key, LaunchAgent, XDG autostart; Harbor starts minimized). All support `--dry-run`, are idempotent, and return documented exit codes; installers call them.
- `GET /api/v1/jobs` accepts `status`, `kind`, `pageNumber`, and `pageSize`; the dashboard header polls active jobs only.
- Performance harness (`scripts/common/perf-baseline.sh`, `Armada.PerfSeed`) and `docs/PERFORMANCE.md`.
- Fixed: the Admiral fails startup when the MCP port is taken instead of running without MCP.
- Fixed: agent output lines at process exit could be dropped.
- Fixed: MergeQueue and None landing modes were ignored for vessels with a working directory; merge-queue conflicts and test failures now mark the mission LandingFailed.
- Fixed: an import interrupted by a restart no longer stays Importing; Harbor reconnect races and requests hanging on a disconnected Harbor; the rollback slot deleted at retention 1; a fleet action cancel leaving targets Running; a dashboard relay WebSocket close race; the Deb/Rpm systemd unit and `/usr/bin` link.
- Tests: E2E fixture reserves ports from 20000-31999 and retries startup; waits use a sleep-proof clock.

### Terminal UI: Ask Armada and approvals
- Ask Armada in the TUI: conversation list, conversation header, streaming transcript with confirm cards (approve with `a`, reject with `r`) and live work cards, a composer with `/` quick actions and inline Dispatch and Fleet action forms, the Ask dock (Ctrl+J) on every screen, and "Ask about this" (Alt+A).
- Approvals center (Ctrl+A): Ask proposals, mission reviews, deployment approvals, failed landings, and stalled captains with single-key decisions using the dashboard's calls and confirmations, a header count, and actionable toasts.
- TUI login is prefilled for a localhost Admiral (seeded admin, default password until first sign-in, local API key, settings port) and the Server picker is 50% wider; `e` in the Server picker edits a server's name and URL.
- Fixed: the TUI's auto-created default server follows the local Admiral's port from settings.json, so a profile saved while settings held other values no longer stays on a stale URL; a hand-edited URL is kept.
- Fixed: TUI login Tab order now follows the layout instead of jumping between the field, the buttons, and the pickers.
- The default admin password is flagged, not enforced: the server no longer blocks API calls from a session on the default password, the TUI signs in and shows a header warning, and the dashboard still prompts for a change.
- Fixed: `Armada.Client` serialized raw JSON request values as an object, so quick-action arguments sent from the TUI did not reach the server correctly.

### Terminal UI (foundation)
- Added `Armada.Client`, a typed .NET client covering every dashboard API function, with typed errors (status, code, request id), paging helpers, and a WebSocket client with typed events and automatic reconnect.
- Added `armada tui`, the Armada terminal UI hosted in Helm, with server profiles and tokens stored in the OS keychain (0600 file fallback): email/tenant/password and API key login, a responsive shell with every dashboard route, a command palette that runs commands and jumps to entity IDs, help overlay, menu bar, notification center with actionable toasts, Dark/Light/High contrast/Auto themes, and the dashboard's languages. Screens arrive in later milestones; see `docs/TUI.md` and `TUI_APP_PLAN.md`.
- Added a TUI parity manifest (`src/Armada.Tui/parity.json`) and a test that fails when a dashboard route, tab, API function, WebSocket event, or Server setting has no entry.

### v1.0 readiness: security follow-ups
- MCP tools that read or act on an entity by id are scoped to the caller's tenant and user like REST and answer not-found for other tenants' ids; ids referenced on create and update are checked the same way; `stop_all`, token usage, papercuts, the prompt template list, the model endpoint health sweep, and batch merge purge are scoped. `McpToolClient` now reads every page of `tools/list`.
- **Upgrade note:** passwords are stored as salted PBKDF2-SHA256 (600,000 iterations) and older hashes are upgraded automatically; an upgraded database cannot be used for password login by an older Admiral.
- Login rate limiting (`loginRateLimit` settings; 429 with `Retry-After`); the API key is compared in constant time.
- Ask thread turns run captains without auto-approve unless `Ask.CaptainAutoApprove` (default false) is on; Claude Code still allows Armada's own MCP tools in that mode. An Ask thread token always wins over other credentials, and a request pairing it with a different identity is refused.
- Vessels have an `AutoApprove` override (REST, MCP `autoApprove` / `clearAutoApprove`, dashboard) that wins over the captain setting for that vessel's missions; Harbor launches apply the resolved setting; Harbor ids stay bound to the identity that registered them.
- **Breaking for proxy deployments:** Armada.Proxy refuses to start with a blank or default password unless `AllowDefaultPassword` is set (the proxy compose file requires `ARMADA_PROXY_PASSWORD`); the instance list requires a session; logins are rate limited; forwarded headers are opt-in (`TrustForwardedHeaders`); new `SecureCookie` setting.

### v1.0 readiness: usability
- Ask Armada approval gating now covers Codex, Gemini, Cursor, Mux, and OpenCode captains as well as Claude Code and API endpoints, without changing where each CLI keeps its login; `GET /api/v1/captains/{id}/tools` reports `askApprovalGated`, and the dashboard shows a persistent note for any captain whose actions are not gated.
- Fixed: Codex captains failed to start with codex 0.159+ (which removed `--full-auto`); Armada now passes `--sandbox workspace-write`, plus `--skip-git-repo-check` for chat and planning turns.
- **Behavior change:** a vessel with Landing Mode None or Merge Queue no longer merges finished work into its working directory; None stops at WorkProduced and Merge Queue enqueues.
- Accessibility: every dashboard modal is a labelled dialog that traps focus, closes on Escape, and returns focus (the shared confirm dialog is an alert dialog); row action menus work from the keyboard; skip link and visible focus ring; about 250 unlabelled controls now have names; theme colors meet WCAG AA contrast in light and dark (light-theme accent, status colors, and filled buttons are darker). axe reports no critical or serious issues on any page or modal in either theme.
- Fixed: on phones and tablets the menu button was pushed off screen and the sidebar could not be opened; Vessel Health fits all columns at 1280 px; pipeline stage rows and the workspace toolbar wrap on phones.
- The eight non-English locales are labelled "beta" in the login and top-bar language pickers.
- Setup wizard: the highlighted sidebar no longer covers the wizard or takes its clicks; it defaults to Claude Code, explains each Landing Mode, accepts a local repository path, and refreshes the dispatched mission's status. GETTING_STARTED documents the path to a first landed mission (about 18 seconds after sign-in in testing).

### v1.0 readiness: API freeze
- New `docs/API_SURFACE_1.0.md` and `docs/api-surface-1.0.json`: the frozen 1.0 surface (REST, MCP, WebSocket, CLI, settings), regenerated by `scripts/common/generate-api-surface.sh`; new `E2E.ApiContract` test fails on removed or incompatible changes and allows additions; new `docs/COMPATIBILITY.md` (compatibility promise, deprecation policy, experimental surfaces).
- Harbor split mode and self-rebuild are marked experimental (`[Experimental]` prefix and tag in OpenAPI and MCP descriptions) and excluded from the 1.0 promise.
- **Breaking:** REST errors always use `ApiErrorResponse` with an `Error` code matching the HTTP status (401 `NotAuthorized`, 403 `Forbidden`, 409 `Conflict`); several validation errors that returned 200 now return 400 or 404; cross-tenant access to users, prompt templates, memories, model endpoints, and harbors returns 404 instead of 403; WebSocket commands not in the declared surface are rejected with `Unknown action`.
- Ask work linking no longer lists `create_voyage` or `retry_mission` (no such MCP tools); `armada help` lists `health`, `action`, and `tui`.

### v1.0 readiness: security
- **Breaking:** every REST route and MCP tool declares an explicit authorization requirement in a central registry, checked before the handler runs; undeclared routes and tools fail closed, and a test fails when one is missing. 19 enumerate routes now need only authentication; check-run writes and Harbor probes need a tenant admin.
- **Breaking:** MCP is authenticated by default; unauthenticated calls are accepted only on a localhost-bound listener from localhost (`Mcp.AllowUnauthenticatedLoopback`, default true); `backup`, `restore`, and `stop_server` require an admin credential.
- **Breaking:** `POST /api/v1/server/stop`, `restart`, `rebuild`, and `rollback` always require an admin (`RequireAuthForShutdown` is deprecated and ignored). Previously they needed no login by default, and rebuild could run a build from a caller-chosen path.
- **Breaking:** the first sign-in as `admin@armada` with the default password must set a new password (`PUT /api/v1/account/password`), which also disables the `default` bearer token; the dashboard warns while defaults are in use; the Admiral refuses non-localhost hostnames with default credentials unless `AllowDefaultCredentialsOnNetwork` is set; new `ARMADA_INITIAL_ADMIN_PASSWORD` for headless and Docker installs.
- `AllowSelfRegistration` now defaults to false; the system API-key identity can no longer log in with a password; tenant admins can no longer modify or mint credentials for global admins; Harbor links must present a valid credential unless both ends are on localhost.
- New per-captain `autoApprove` switch runs CLI captains without their auto-approve flags; `audit.command` events are recorded for workspace exec, fleet action commands, check runs, Harbor probes, and merge-queue tests (only global admins can delete audit events).
- Bearer tokens are shown once at creation and masked on reads; remote-tunnel secrets are masked in settings; request history redacts secret-bearing keys, secret-shaped values, and query strings; deleting a vessel only removes directories inside the managed repos and docks directories.
- Containers run as non-root on pinned base images (the dashboard container listens on 8080); new `security.yml` workflow scans NuGet and npm dependencies; new `docs/SECURITY_REVIEW.md` and `SECURITY.md`.
- Removed: the keyword `POST /api/v1/ask` responder and the `armada ask` CLI command (use Ask Armada threads).
- Fixed: the MCP listener now binds when the hostname is `0.0.0.0`.

### v1.0 readiness: upgrades and data safety
- Startup backs up a SQLite database before applying migrations (`{DataDirectory}/backups/pre-migration-*`, newest 5 kept); server providers log the dump command and can be configured to refuse to migrate until a backup is confirmed (`Database.RequireBackupConfirmationForMigrations`).
- New Data Retention settings: Ask threads archive after 90 idle days (deletion optional), finished jobs delete after 30 days, finished import batches after 90 days; editable on the Server settings page.
- Backup and restore now use the configured data directory and settings file, and restore uses SQLite's online backup API instead of overwriting the open database; built-in backup/restore return 400 on server providers.
- Every migration is now safe to re-run on all four providers (fixed: SQLite v15 table rebuild, PostgreSQL column and foreign-key additions, SQL Server initial schema).
- Upgrading no longer appends the memory-recall section to persona templates operators edited.
- On PostgreSQL, MySQL, and SQL Server, data expiry no longer breaks the hourly cleanup, so request-history and fleet-action pruning run again on those providers.
- New `docs/UPGRADING.md` and an upgrade test (`scripts/common/run-upgrade-test.sh`) that upgrades a seeded v0.9.0 database on all four providers.

### v1.0 readiness: CI, packaging, operations
- CI: new `ci.yml` runs the full test suite on Windows, macOS, and Linux (net8.0 and net10.0) on every push and PR, with a warning-free build, the dashboard build and tests, and a check that the committed dashboard `dist/` is current; new nightly provider-parity workflow (SQLite, PostgreSQL, MySQL, SQL Server).
- Packaging: Harbor ships on macOS as `Armada Harbor.app` (with icon) in a `.dmg`; the server ships as a `.pkg` with a LaunchAgent and uninstall script; new WiX `.msi` channel for the server; Developer ID signing and notarization when Apple credentials are present, ad-hoc signing otherwise; every release includes `SHA256SUMS` (new `Armada.Publisher checksums --dir`). Homebrew, Scoop, Chocolatey, winget, and AppImage channels are disabled until implemented. Fixed: the publisher could not find published binaries with dotted names on macOS/Linux, and Windows signing passed the certificate password in the wrong place.
- Docker: healthchecks on every HTTP service, startup ordered on dependency health, `curl` in the proxy image, new `docker/update.sh` / `update.bat`, and `DOCKERHUB_README.md`.
- Docs: `docs/CAPTAINS.md` (captain support matrix), `docs/OPERATIONS.md` (operations guide), `docs/RELEASING.md` (release checklist).

### Ask Armada home base
- **Breaking for custom WebSocket clients:** `/ws` now requires authentication (see the security fix below). The dashboard sends its session token automatically; any other client must pass `?token=<token>` (browsers cannot set headers), and WebSocket commands are global-admin only.
- **Dashboard:** `/ask` is now a two-pane home base: a conversation list (search, pin, unread badges, a live "working" dot, rename, summarize, archive, delete) and the conversation itself, deep-linkable at `/ask/:threadId`. Messages render by kind: Markdown replies with tool-call chips, confirm cards (exact arguments, Approve / Reject, outcome), live work cards that follow every mission (status, captain, pipeline stage, checks, merge queue, landing, failure reason) and update from `ask.work` events, progress updates, summaries, and errors. A "Work in this conversation" strip lists everything the thread started. Typing `/` opens quick actions with inline forms (`/dispatch`, `/fleet-action`, `/status`, `/health`, `/import`). The header has the captain picker, an Auto-approve toggle, and Summarize. The dashboard's socket client sends its token, reconnects with backoff, and refetches the open thread on reconnect.
- Fixed during the end-to-end run: the "not connected to Armada over MCP" banner is no longer shown for Claude Code and ApiEndpoint captains (the server connects them per turn); the captain's reply now reserves its place in the thread when the turn starts, so a confirm card approved while the captain is still writing, and the progress updates that follow, sort after the reply instead of above it.
- **Security fix, WebSocket `/ws`:** upgrades now require authentication (REST headers, the `token` query parameter, or a `Sec-WebSocket-Protocol` entry `armada-token.<base64url(token)>`) and are refused with `401` otherwise. Each socket keeps its identity: entity events (`mission.changed`, `voyage.changed`, `captain.changed`, check runs, objectives, deployments, incidents, runbooks, planning and refinement sessions, generic events) are delivered only to sockets of the entity's tenant (global admins can opt in to every tenant with `{ "Route": "subscribe", "AllTenants": true }`), and `ask.*` events only to the owning user. WebSocket commands now require a global administrator (the command handler is not tenant-scoped; tenant users use REST). The hub exposes `BroadcastToTenant` and `SendToUser`; the proxy relay forwards the browser's token to the deployment's `/ws`. Previously any unauthenticated client could connect, run mutating commands, and receive every tenant's events.
- **Ask Armada threads:** private, persisted conversations under `/api/v1/ask/threads` (enumerate, create, read with tracked work and pending proposals, update, delete, messages with paging, send message (202, background captain turn), cancel, summarize, mark read, quick actions, approve/reject proposals, work snapshots, quick-action catalog). Another user's thread returns 404. Streaming and updates arrive as owner-only `ask.turn`, `ask.chunk`, `ask.thinking`, `ask.tool`, `ask.message`, `ask.proposal`, `ask.work`, and `ask.thread` events.
- **Approvals:** a thread's captain reaches Armada's MCP server with a thread-scoped session token (ApiEndpoint captains via environment, Claude Code via a per-launch strict MCP config with an `X-Token` header). Read-only tools (the `AskToolPolicy` allowlist) run; any other tool becomes a `Pending` proposal with a confirm card unless the thread auto-approves. Approve executes the stored call in-process through the same MCP tool handler as the approving user, posts the result, tracks the created work, and runs a short follow-up turn. Proposals expire after `Ask.ProposalExpiryMinutes`. Quick actions (`/dispatch`, `/fleet-action`, `/status`, `/health`, `/import`) run through the same path as already-approved proposals. Thread-scoped tokens are refused by REST and `/ws`.
- **Live monitoring:** an `AskWorkTracker` follows voyages, missions, fleet action runs, jobs, and import batches started from a thread (change notifications plus a sweep every `Ask.TrackerIntervalSeconds`), pushes work-card snapshots (`ask.work`), and posts `WorkUpdate` milestone messages (started, mission failed, landed, pull request opened, landing failed, finished, failed, cancelled), worded by the thread's captain when it is idle and otherwise deterministic. Unread counts track captain and Armada messages.
- New `Ask` settings: `HistoryTurns` (20), `ProposalExpiryMinutes` (60), `TrackerIntervalSeconds` (5), `NarrateMilestones` (true), `NarrationTimeoutSeconds` (60), `TurnTimeoutMinutes` (15).
- Schema migration v75 on SQLite, PostgreSQL, MySQL, and SQL Server: `ask_threads`, `ask_messages` (unique per-thread sequence), `ask_message_tool_calls`, `ask_action_proposals`, and `ask_tracked_work` (unique per thread and entity), ID prefixes `ath_`, `amg_`, `atc_`, `aap_`, `atw_`. Threads are kept until deleted.
- Claude Code chat turns now surface tool calls (`tool_use` / `tool_result` stream events) as `ask.tool` events. The Postman collection has a new "Ask Threads" folder.

### Vessel import: background discovery and captain fleet recommendations
- Discovery can run in the background: `POST /api/v1/vessels/import/discover` with `RunInBackground: true` validates the request, returns `202` with a `VesselDiscovery` job and a batch in the new `Discovering` status, and moves the batch to `Discovered` (or `Failed` with `ErrorMessage`) when the scan ends. The synchronous path is unchanged; MCP `discover_vessels` gains `runInBackground`.
- Imports can ask a captain to recommend fleets (`Categorization: { Enabled, CaptainId, Prompt, ApplyAutomatically }` on `POST /api/v1/vessels/import`, MCP `import_vessels` `categorize`/`captainId`/`prompt`/`applyFleetsAutomatically`, Helm `armada vessel import --categorize --captain <id> [--prompt-file <path>] [--apply]`). After the vessels exist, a `FleetCategorization` job reserves the idle captain (new `Analyzing` captain state), writes a `REPOSITORIES.md` manifest (paths, remotes, branches, detected languages and manifests, README excerpts) into a scratch directory under the data directory, runs the captain once on the Admiral host with the editable `import.fleet_categorization` prompt plus an Admiral-owned output contract, and validates `fleet-recommendations.json` into structured recommendations (unknown vessel IDs dropped with a warning, unassigned vessels collected in `Uncategorized`). Runs are capped by the new `Import.CategorizationTimeoutMinutes` setting (default 20) and stop on job cancellation.
- New endpoints `GET /api/v1/vessels/import/categorization/default-prompt`, `POST /api/v1/vessels/import/batches/{id}/categorize` (run or retry), and `POST /api/v1/vessels/import/batches/{id}/fleet-recommendations/apply` (create or reuse fleets by name, assign vessels), plus MCP `categorize_vessel_import` and `apply_fleet_recommendations`. `GET /api/v1/vessels/import/batches/{id}` now returns the categorization state, recommendations, and rebuilt discovery hints. Job kinds `VesselDiscovery`, `VesselImport`, and `FleetCategorization` were added (background imports now use `VesselImport` instead of `Generic`).
- Dashboard: discovery and import run in the background from the import wizard, which says so and shows live progress; the Review step adds "Recommend fleets with a captain" with a captain picker (idle captains only), the editable prompt with "Reset to default", and "Apply recommendations automatically"; results show the recommended fleets as editable cards (rename, move repositories, add or remove fleets, apply with confirmation) with the captain's rationale, and a failed run offers "Retry categorization". The header shows a background-activity indicator with a count and a list of running jobs (5-second polling while busy, 30-second otherwise) and toasts when fleet categorization finishes or fails. Batches resume from the import history or `/vessels/import?batch=<id>`.
- Schema migration v74 on SQLite, PostgreSQL, MySQL, and SQL Server: discovery and categorization columns on `vessel_import_batches`, a `selected` flag on `vessel_import_items`, and the `vessel_import_fleet_recommendations` and `vessel_import_fleet_recommendation_vessels` tables (ID prefix `vfr_`). At startup, discovery and categorization interrupted by a restart are marked failed and `Analyzing` captains return to `Idle`.

### CodeHub capabilities: vessel import, fleet actions, vessel health
- Added Vessel Import: discover git repositories under directories or roots on the Admiral host and onboard them as vessels in bulk (`GET /api/v1/vessels/import/browse`, `POST /api/v1/vessels/import/discover`, `POST /api/v1/vessels/import`, import history under `/api/v1/vessels/import/batches`), plus MCP `discover_vessels`/`import_vessels`, the `vessel_import_batch` enumerate type, `armada vessel import`, and `Import` settings (AllowedRoots, MaxDepth, ExcludedDirectoryNames, InlineBatchLimit). Imported vessels set `WorkingDirectory` and never `LocalPath`; worktrees and Armada-managed directories are reported but not preselected; large imports run as background jobs. Vessel creation from REST and MCP now goes through a shared `VesselService` (MCP `add_vessel` now rejects a missing `repoUrl`).
- Added Fleet Actions: apply a shell command (Command) or an AI prompt (Mission, one voyage per vessel) across many vessels as a persisted, cancellable, concurrency-limited run with per-vessel results; five seeded built-ins; template variables including `{{health.summary}}`; REST under `/api/v1/fleet-actions` and `/api/v1/fleet-action-runs`; MCP tools `create_fleet_action`, `update_fleet_action`, `delete_fleet_action`, `run_fleet_action`, `fleet_action_run_status`, `cancel_fleet_action_run`; enumerate types `fleet_action`, `fleet_action_run`, `fleet_action_run_target`; `armada action list|run|status|cancel`; `FleetActions` settings. See `docs/FLEET_ACTIONS.md`.
- Added Vessel Health: scheduled and on-demand grading of every vessel across ten criteria (git divergence, working tree, branches, commit recency, NuGet/npm outdated and vulnerable dependencies, test infrastructure, CI, Armada readiness, mission outcomes), with manual overrides applied everywhere and failed tool runs reported as Unknown, never Pass. REST under `/api/v1/vessel-health` and `/api/v1/vessels/{id}/health`; MCP `vessel_health`, `evaluate_vessel_health`, `set_vessel_health_override`; the `vessel_health` enumerate type; `armada health`; `RepositoryHealth` settings; `armada.health.*` metrics and spans. See `docs/VESSEL_HEALTH.md`.
- Schema migrations v71 (vessel import batches and items), v72 (fleet actions, runs, targets), and v73 (vessel health, findings, dependencies, overrides) on SQLite, PostgreSQL, MySQL, and SQL Server. Deleting a vessel cascades to its health rows; import items and run targets keep their history.
- `IGitService` gains divergence, working-tree, and branch-detail methods used by the health criteria. The Postman collection has new Vessel Import, Fleet Actions, and Vessel Health folders.

### Dependencies
- Upgraded Voltaic from 0.7.1 to 2.0.0 (Armada.Server and Armada.Helm). Behavior changes picked up from Voltaic 1.x/2.x: the MCP servers (HTTP `/mcp` + `/rpc`, and `armada mcp stdio`) now publish only Armada's own tools -- Voltaic's `ping`/`echo`/`getTime`/`getSessions` demo tools are gone (`getSessions` disclosed every client's session id); the protocol `ping` returns `{}` instead of `"pong"`; tools are callable only through `tools/call` (a bare JSON-RPC call to a tool name returns `-32601`); and tool input schemas now enforce `additionalProperties`, so `start_runbook_execution.parameterValues` rejects non-string values with `-32602`. Stateless `2026-07-28` clients (Claude Code 2.1.x) are now served correctly on both `/mcp` and `/rpc`.
- Updated Watson 7.2.0, PolyPrompt 2.6.0, Microsoft.Data.SqlClient 7.1.0, Microsoft.Data.Sqlite 10.0.12, Microsoft.NET.Test.Sdk 18.10.1, and NUnit3TestAdapter 6.3.0. Avalonia stays on 11.3.x (12.x is a separate major migration).
- New MCP E2E cases cover the upgrade in both directions: demo tools absent from `tools/list`, `ping` returns an empty object, bare tool-name calls and `getSessions` are rejected, and `parameterValues` schema enforcement.
- Dependency refresh: Voltaic 2.0.0 -> 2.2.1, PolyPrompt 2.6.0 -> 3.1.0, Watson 7.2.0 -> 7.2.2, SyslogLogging 2.2.2 -> 2.3.1, Spectre.Console.Cli 0.55.0 -> 0.57.2, Microsoft.Data.SqlClient 7.1.0 -> 7.1.1, Avalonia 11.3.20 -> 11.3.22 (still on 11.x), and test packages Touchstone 0.1.12 -> 0.2.0, NUnit 4.6.1 -> 5.0.0, coverlet.collector 10.0.1 -> 10.1.0.
- Voltaic 2.2.1 behavior changes picked up by the MCP servers: sessions are server-assigned only -- a handshake-era client must `initialize`, then send the returned `Mcp-Session-Id` (and `notifications/initialized`) before `tools/list` or `tools/call`; a request without an initialized session is rejected, and client-chosen session ids are never adopted. Tool input-schema violations and handler exceptions are now tool results with `isError: true` instead of JSON-RPC errors (`-32602` and friends). Armada enables `IncludeToolExceptionMessages` on both the HTTP and stdio MCP servers so handler error messages (for example "captain not found") still reach the agent instead of Voltaic's generic "internal error" text. A missing `Accept` header on `/mcp` now counts as `*/*`. Armada's own MCP clients (the API-endpoint runtime's `McpToolClient` and the captain tool-catalog probe) already performed the full handshake and are unaffected.
- PolyPrompt 3.x splits each provider client into one client per capability. `ModelEndpointClientFactory` now exposes `CreateCompletion` (chat/tool-chat, used by the API-endpoint runtime and Inference validation), `CreateEmbedding` (used by Embedding validation), and `Create`, which returns the client matching the endpoint's kind as a `ClientBase` (used by health checks). Per-call tool-chat model overrides move to `ToolChatRequest.Options.Model`.
- Spectre.Console.Cli 0.57 made `AsyncCommand<T>.ExecuteAsync` public; every Helm command override was updated to match.
- Test updates: MCP E2E suites now perform the real `initialize` / `Mcp-Session-Id` / `notifications/initialized` handshake on `/rpc`, assert `isError` tool results for schema violations and handler failures, and probe the `/mcp` Accept check with an explicit non-matching `Accept`. The E2E fixture now waits for the MCP listener (GET `/` health check) as well as REST before running suites, fixing a startup race that surfaced as "connection refused". New factory cases cover kind-based dispatch, the Voyage AI embedding client, and rejection of Anthropic embedding / Voyage AI completion clients. The API-endpoint runtime's fake clients implement the PolyPrompt 3 `*CoreAsync` extension points.
- Added repository-root `build-admiral.sh`, `build-proxy.sh`, and `build-all.sh`, the Linux/macOS counterparts of the existing `.bat` release scripts.

### Captain prompts
- **Commit messages:** the instruction that every commit message must have a summary line plus a per-file description of what changed and why is now sent with every mission, not only when Armada commit trailers are enabled. The trailers are appended only when `MessageTemplates.EnableCommitMetadata` is on, introduced by the new `commit.trailers_preamble` template. Both `commit.instructions_preamble` and `commit.trailers_preamble` now honor edits saved under Configuration > Prompts (previously the built-in text was always used). An untouched built-in `commit.instructions_preamble` is upgraded in place on startup; edited templates are kept.
- **Ask Armada scope:** follow-up questions such as "what's running?" or "any failures?" are now answered for the vessels and voyages the conversation is about, not the whole fleet. The `ask.system` prompt gains a "Conversation scope" section (scope follow-ups to the conversation's focus, answer fleet-wide when asked or when there is no focus, name the scope used, and treat Failed, LandingFailed, and failed merge-queue entries as failures), and each thread turn now includes a "Conversation focus" block listing the work the thread started and the vessels it touched. An untouched built-in `ask.system` prompt is upgraded in place; edited prompts are kept.

### Dashboard
- Fleet Actions moved from DELIVERY to OPERATIONS in the sidebar, right after Dispatch (it dispatches commands and missions across vessels); run detail pages highlight OPERATIONS.
- At 768 px and narrower, every page was squeezed into a 220 px column because the shell's inline desktop grid overrode the mobile rule; the mobile layout now applies.

### Fixes (Linux/macOS)
- Armada Harbor: values (Harbor, Server, MCP URL), the Activity Log, and the subtitle now use a secondary grey that adapts to the color scheme, with labels kept in the primary text color. A new Appearance picker (System, Light, Dark) in the window switches the scheme and is saved as `Appearance` in Harbor's `settings.json`; System follows the operating system as before. The window is 15% wider by default (713 px), the subtitle reads "Host runner for captains, git, and worktrees", a separator line now divides the Harbor/Server/MCP URL details from the connection status, and "Open Dashboard" is now "Dashboard".
- Armada Harbor now shows the Armada icon in the macOS Dock instead of the generic "exec" icon. Harbor runs as a plain executable rather than an .app bundle, and Avalonia does not apply the window icon to the Dock, so Harbor now sets `NSApplication.applicationIconImage` at startup from a new macOS-style asset (`Assets/logo-macos.png`: 1024px canvas, rounded tile on the standard icon grid).
- Generated mission instructions (`CLAUDE.md`, `CODEX.md`, `MUX.md`) can no longer leak into captain commits. In a dock (a linked worktree) the exclude entry was written to the per-worktree git directory, which git never reads, so it is now written to the shared `info/exclude`; and when the repository already tracks its own instruction file, the dock marks it `--skip-worktree` because git ignores exclude rules for tracked files. Two leaked mission briefs were removed from this repository's `CLAUDE.md`.
- `run-local.sh` (macOS/Linux) and `run-local.bat` now build and deploy the dashboard before starting the server, so local runs serve the current dashboard source instead of whatever was last deployed to `~/.armada/dashboard`. Set `ARMADA_SKIP_DASHBOARD=1` to skip it. `deploy-dashboard.sh` also gains the reinstall-and-retry that `deploy-dashboard.bat` already had when a build fails on a stale or foreign `node_modules`.
- `src/Armada.Dashboard/node_modules` is no longer committed (it was a Windows install, so `deploy-dashboard.sh` skipped `npm install` and could not build on Linux/macOS); it is now git-ignored and installed per machine. The committed `dist/` fallback was rebuilt from current source.
- Deployment health checks and HTTP verification definitions no longer treat a rooted path such as `/api/v1/status/health` as an absolute URL. On Linux and macOS `Uri.TryCreate(..., UriKind.Absolute)` accepts it as `file:///api/...`, so the probe ignored the environment's `BaseUrl` and failed with "The 'file' scheme is not supported", leaving deployments and incident-linked deployments in `VerificationFailed` (this affected the Docker images). Only absolute `http`/`https` URLs now bypass `BaseUrl`.
- Worktree registration checks (`GitService.IsWorktreeRegisteredAsync`) and the workspace diff's repository-root guard now compare symlink-resolved paths via the new `PathCanonicalizer`. git reports resolved paths (`/private/var/...` on macOS, where `/var` is a link), so worktrees under the temp directory were reported as unregistered and diffs failed with "Not a git repository rooted at this path".
- `IsRunningAsync` on the CLI agent runtimes returns false for non-positive process ids; on Linux/macOS `Process.GetProcessById(-1)` can succeed because `kill(-1, 0)` addresses every signalable process.
- The dirty-worktree `GitService` test marks its `post-checkout` hook executable on Linux/macOS (git skips non-executable hooks). New `Services.PathCanonicalizer` cases cover separator/dot-segment normalization, symlinked directories, missing segments, and distinct paths.

### Linter persona
- Added a built-in **Linter** persona that evaluates a mission's changed **code and documentation** for style and correctness -- code style (naming, formatting, import ordering, adherence to the project's style guide and idioms), code correctness (typos, obvious defects, unhandled edge paths, mismatched signatures), documentation style (Markdown formatting, headings, spelling/grammar, code-fence tags), and documentation correctness (broken/stale links, examples or commands that no longer match the code). It fixes clear, safe, in-scope violations and reports the rest, staying strictly within the files the mission changed.
- New editable prompt template `persona.linter` (Configuration > Prompts). Like every other working persona it also carries the "Recall Existing Memory" note and reports through `## Code Style`, `## Code Correctness`, `## Documentation`, `## Fixes Applied`, and `## Residual Issues` sections, ending with a standalone `[ARMADA:RESULT] COMPLETE` line.
- Injected into the built-in **FullPipeline** as a gating stage after Test Engineer and before Judge: Product Manager -> Architect -> Worker -> Usability Engineer -> Test Engineer -> **Linter** -> Judge -> Recorder. Existing installs pick up the new stage on the next startup via the in-place FullPipeline upgrade, and the new persona + prompt template are seeded idempotently.

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

### Self-rebuild ("Rebuild Armada")
- Added a dashboard **Rebuild Armada** button (Server page) that rebuilds the Admiral from its own source and cuts over to the new build with near-zero downtime -- so changes landed by captains can be picked up without leaving the dashboard. Designed for the native single-box deployment where Armada's source is itself a vessel.
- A/B slot layout: each rebuild publishes into a fresh versioned slot (`~/.armada/bin/slots/<yyyy-MM-dd_HHmmss_sha>/`) from a detached `git worktree` at an operator-chosen ref while the running server keeps serving, then flips a `current` pointer file and hands over via the existing detached-child restart baton. Publishing to a new slot sidesteps the Windows file lock on the running executable, so the build runs with no downtime and only a successful publish triggers a cutover -- a failed build never disturbs the running server.
- The database is backed up before every cutover. Operator **rollback** reverts to the previous slot; when the rebuild migrated the schema it first restores the pre-rebuild backup (discarding data written since cutover), otherwise it relaunches the previous slot with no restore.
- REST API: `POST /api/v1/server/rebuild`, `GET /api/v1/server/rebuild/status` (live build log), and `POST /api/v1/server/rollback`. New settings: `SelfVesselId` (which vessel holds Armada's source), `RebuildSlotRetentionCount`, and `RebuildSupervisorHarborId`.
- Optional Harbor-supervised cutover: when `RebuildSupervisorHarborId` names a connected on-box Harbor, the Admiral arms it with a one-shot `deferredLaunch` instruction before exiting; the Harbor launches the new slot, health-checks it, and rolls back to the previous slot if it does not come up. Otherwise the in-process baton is used (no automatic rollback). Two new Harbor protocol messages (`deferredLaunch`/`deferredLaunchAck`) documented in `docs/HARBOR_PROTOCOL.md`.
- Dashboard: a build-ref branch picker (from the source vessel's branches) plus a free-text tag/commit box, a live build-log viewer, a Roll Back action, and a Rebuild settings card to designate the self vessel and slot retention.
- Local-clone self vessels are buildable: `add_vessel` records a vessel's `WorkingDirectory` when its `repoUrl` is a local clone (a `file://` URL or an existing local git path), so a vessel pointing at an on-disk checkout can be designated `SelfVesselId` and built from its working tree. The Server page's branch picker also resolves branches from `WorkingDirectory`, so it now populates for such vessels instead of showing only a confirm dialog.
- Cross-platform cutover fix: the replacement-process launcher now uses `UseShellExecute=false` on every platform (Windows, Linux, macOS). It previously used shell-execute on Windows, which is incompatible with passing the predecessor-PID handoff environment variable and made the in-process cutover fail with "the replacement process could not be launched"; a `UseShellExecute=false` child still outlives the parent on all three platforms.
- Pre-rebuild database backups are pruned after each rebuild to the newest `RebuildSlotRetentionCount` (the most recent remains as the rollback safety net) so they no longer accumulate without bound.
- On Windows the dashboard build step during a rebuild runs through `cmd.exe` so `npm` resolves from PATH the same way an interactive shell does.
- Full design, task tracking, and safety rails documented in `docs/SERVER_REBUILD.md`. Status: implemented for the native single-box deployment and unit-tested (slot management and the deferred-launch protocol); the in-process cutover baton is now fixed on Windows, while the Harbor-supervised cutover path is not yet live-verified.

### Agent memory (Recorder persona)
- Added durable agent memory: the new built-in **Recorder** persona reviews a voyage's conversation, classifies what is worth keeping into **episodic** / **semantic** / **procedural** memory (working memory is never stored), reconciles it against what already exists, and persists it to three targets -- the vessel model context, a native Armada memory store, and any external memory MCP tools/skills it discovers at runtime.
- New `Memory` entity (`mem_`) with type, topic, a stable idempotency `key`, one-line `summary`, content, `salience` (used to order recall), monotonic `version`, provenance (source kind + voyage/mission/vessel ids + detail), a vessel association, and tags. Tenant/user ownership scope like other configuration entities. Persisted across SQLite, PostgreSQL, MySQL, and SQL Server (schema migration 70 adds the `memories` and `memory_tags` tables).
- Memory REST API under `/api/v1/memories`: list/search (filter by `type`, `topic`, `vesselId`, `search`; paged; ordered by salience then recency), create/upsert, read, update, and delete.
- Memory MCP tools: `search_memory`, `get_memory`, `create_memory` (idempotent upsert by `key`), `update_memory`, and `delete_memory`, plus a new `memories` entityType on the `enumerate` tool. All are scoped to the authenticated caller.
- Pipelines: the Recorder is appended as a final **non-gating** stage of the built-in `FullPipeline` (existing installs upgrade in place), and a new built-in `Recorded` pipeline (Worker -> Recorder) is seeded.
- Every other built-in persona template (Worker, Architect, Product Manager, Usability Engineer, Test Engineer, Judge) now carries a "Recall Existing Memory" note telling the agent to read the vessel model context and use `search_memory` before acting. Existing deployments pick this up on the next startup via an idempotent template upgrade that never overwrites operator edits.
- Dashboard: a Memory tab under the Configuration hub (list/filter/view/delete memories), and the Pipelines table now renders the Built-in and Active columns as compact checkmark icons.
- The `persona.recorder` prompt (and all persona prompts) remain editable under Configuration > Prompts. Design informed by the Isis agent-memory platform: a stable upsert key to fight duplicate sprawl, salience actually used in recall ordering, and a summary recall hook.

### Ask Armada tool access (ApiEndpoint captains)
- Ask Armada chats backed by an inference endpoint (an `ApiEndpoint` captain, which runs an in-process tool-calling loop instead of a CLI harness) can now use Armada's own MCP tools -- so the assistant can actually create a vessel, dispatch, enumerate, and otherwise act on fleet state instead of describing tools it cannot reach.
- Added a streamable-HTTP MCP client (`Armada.Runtimes/Mcp/McpToolClient`): `initialize` + `Mcp-Session-Id` handshake, `tools/list` discovery, `tools/call` execution, and JSON-or-SSE envelope parsing. The in-process runtime discovers the endpoint's tools and merges them into its tool-calling loop alongside the built-in file/process tools (built-in names win on any collision).
- Per-caller scoping: for each chat turn the server mints a short-lived session token for the asking user and hands the runtime the local `/mcp` URL plus that token, so every tool call authenticates and is scoped to that user exactly as a real per-user MCP client would be. Tool activity surfaces as chat tool cards.
- The default `ask.system` prompt was rewritten to be honest about capability: use only tools actually provided, never claim MCP access it cannot verify, and, when it lacks a tool for a request, say so and point to a MCP-connected captain, the dashboard, or the CLI. Existing installs upgrade the built-in prompt in place without overwriting operator edits.
- Tool activity renders as compact, collapsible cards (status, tool name, runtime, a one-line result preview, and elapsed time) that expand to the formatted arguments and result; the per-turn statistics popover now also reports the tool-call count and total time spent in tools.
- The in-runtime MCP client targets the advertised `http://localhost:<port>/mcp` endpoint (the same URL captain configs use) so the request's Host header matches the listener's bound hostname (a hardcoded loopback IP was rejected by the OS HTTP stack as an invalid hostname). ApiEndpoint diagnostic chatter (`[mcp]`/`[tool]`/`[tool:result]`) no longer leaks into the reply text, the chat window reliably auto-scrolls as tool cards and text stream in, the composer is spaced from the transcript, and a random greeting is shown on the blank Ask Armada screen.

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
