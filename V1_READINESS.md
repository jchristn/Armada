# Armada v1.0.0 Readiness

> **Type:** implementation plan (work-tracking). Annotate task status and the progress log as you go; keep this
> document in sync with what actually shipped.
>
> **Target:** v1.0.0
> **Current version:** 0.9.0
> **Status:** Not started
> **Last updated:** 2026-10-04

Status values: `[ ]` not started, `[~]` in progress, `[x]` done, `[!]` blocked. Put a one-line note under any task
you touch and add a dated row to the Progress Log.

## What 1.0 means for Armada

The feature set is broad enough. What a 1.0 has to add is a set of promises Armada cannot make today:

1. **It is safe to run where other people can reach it.** Every surface that accepts input (REST, MCP, the
   WebSocket, the Harbor link, the proxy, file-system and shell entry points) authenticates the caller and authorizes
   the action, and the defaults do not leave an install open.
2. **Upgrading does not break you.** A 0.9.x install with real data upgrades to 1.0 on any of the four database
   providers without losing anything, and the server backs itself up before it migrates.
3. **The API holds still.** REST routes and bodies, MCP tool names and arguments, WebSocket events, CLI commands, and
   settings keys are frozen at 1.0 under Semantic Versioning. Anything that is not ready is removed or clearly marked
   experimental before the freeze.
4. **It installs the way the README says it does,** on Windows, macOS, and Linux, with signed artifacts.
5. **It is tested like a product.** A green test suite on every push on all three operating systems, a
   provider-parity run, an upgrade test, and a simulated user testing session per release candidate.

None of these is a new feature. That is the point: 1.0 is a hardening release.

## Evidence from 2026-10-04

These are things observed while working in the code, not the result of a full audit. They are the reason this plan
leads with security and contracts.

- `/ws` accepted unauthenticated connections and ran mutating commands (`stop_server`, `delete_vessel`,
  `create_voyage`) and broadcast every tenant's events to every client. Fixed on 2026-10-04 (reproduced first in
  `E2E.WebSocketSecurity`). One gap that large is a reason to audit every other surface.
- Authorization is decided by URL prefix in `AuthorizationConfig.GetPermissionLevel`. A new route under the wrong
  prefix silently gets the wrong rule. This is a recorded nonconformance with `AUTHENTICATION.md` (resource type plus
  operation).
- The MCP server accepts unauthenticated calls and treats them as authorized ("additive" authentication), and the
  Docker compose file publishes ports 7890 and 7891.
- Default credentials are `admin@armada` / `password` with bearer token `default`; CLI captains run with
  `--dangerously-skip-permissions`, `--full-auto`, or `--yolo` by default.
- 74 schema migrations exist (v1 through v75) across SQLite, PostgreSQL, MySQL, and SQL Server, with no automated
  test that upgrades a populated older release.
- Test reliability: end-to-end runs showed intermittent server-startup timeouts (14 failures in one full run that
  passed on rerun), and SQL Server parity runs hit query timeouts under emulation.
- CI: `.github/workflows/release.yml` builds and publishes; there is no workflow that runs the test suites on push or
  pull request.
- Packaging: of 14 channels in `publisher.json`, only NuGet, Inno, and Deb/Rpm are implemented; WiX, DMG, PKG,
  AppImage, Homebrew, Scoop, Chocolatey, and Winget are `StubChannel`s. Harbor on macOS ships as a bare executable
  (its Dock icon had to be set at runtime because there is no `.app` bundle).
- Repository requirements not met: no `DOCKERHUB_README.md`, no `docker/update.bat`.
- The old keyword-matching `POST /api/v1/ask` responder still ships beside Ask Armada threads; Ask's work-linking
  table references `create_voyage` and `retry_mission`, which are not MCP tools; the CHANGELOG describes the Harbor
  split-mode transport as "still being rolled out".
- Several stores grow without bound: Ask threads and messages, fleet action run output, vessel health findings,
  import batches.
- Eight dashboard locales were translated by agents and have not been reviewed by native speakers.

## Workstreams

The workstreams are ordered by how much they can change the code. Security and the API freeze come first because
everything after them assumes a stable, safe surface.

### W1. Security

The goal is a written threat model and a closed list of findings, not just fixes for what is already known.

- [x] **W1.1 Surface inventory.** List every entry point with its authentication, authorization rule, tenant
  scoping, and input validation: all REST routes (from the route registrars and `/openapi.json`), every MCP tool,
  every WebSocket route and command, the Harbor link protocol, the proxy relay, and every place that touches the host
  file system or runs a process (vessel import browse/discover, Fleet Actions commands, workspace exec, docks, git,
  self-rebuild, backup/restore).
  _Acceptance:_ `docs/SECURITY_REVIEW.md` with one row per entry point and an owner for each gap.
- [x] **W1.2 Authorization model.** Replace the path-prefix rules with an explicit `(ResourceType, Operation)`
  requirement declared on each route and MCP tool, checked centrally, with a test that fails the build when a route or
  tool has no declared requirement. Keep `PermissionLevel` as the mapping target if that is simpler, but make the
  declaration per route, not per prefix.
  _Acceptance:_ the "undeclared route" test exists and passes; the AUTHENTICATION.md nonconformance note is removed.
- [x] **W1.3 MCP authentication.** Decide and implement: authenticated by default, with an explicit
  `Mcp.AllowUnauthenticatedLoopback` setting (default true only when bound to loopback) for the local developer
  experience. Non-loopback MCP requires a token.
  _Acceptance:_ an unauthenticated MCP call to a non-loopback binding is refused (test); local Claude Code setup in the
  README still works unchanged.
- [x] **W1.4 Safe defaults.** Force a password change for `admin@armada` on first dashboard login; disable the
  `default` bearer token once a real credential exists; refuse to bind to a non-loopback address with default
  credentials unless an explicit override is set; show a persistent dashboard warning while defaults are in use.
  _Acceptance:_ a fresh Docker install cannot be driven with `Bearer default` from another host.
- [~] **W1.5 Captain execution safety.** Document the auto-approve flags in a "Running agents safely" section, add a
  per-vessel and per-captain setting to run CLI captains without auto-approve where the runtime supports it, and make
  Fleet Actions Command runs and workspace exec require TenantAdmin plus an audit record.
  _Acceptance:_ audit records exist for every shell command Armada runs on a user's behalf.
- [x] **W1.6 Secrets.** Verify no API keys, tokens, or passwords are returned by any read endpoint, written to logs,
  or captured in request history bodies; redact them in captured bodies, not just headers.
  _Acceptance:_ a test seeds known secrets and greps responses, logs, and request history for them.
- [x] **W1.7 Dependency and container hygiene.** Enable dependency vulnerability scanning (NuGet and npm) in CI, run
  the containers as a non-root user, and pin base images.
- [ ] **W1.9 Security follow-ups from W1.1** (`docs/SECURITY_REVIEW.md`): O-01 tenant checks in the 62 MCP tools that
  act on entities by id; O-02 Ask turns run CLI captains on the host with auto-approve flags (default Ask captains to
  auto-approve off, or require tenant admin); O-04 split-mode captains get no MCP credential; O-05 salted password
  hashing (PBKDF2/Argon2 with transparent rehash) and login rate limiting; O-11 proxy hardening; per-vessel
  auto-approve setting (W1.5 shipped per-captain only).
- [ ] **W1.8 External review.** One review pass by someone other than the author against the W1.1 inventory.

### W2. API and contract freeze

- [x] **W2.1 Inventory the public surface.** Generate the REST surface from OpenAPI, the MCP surface from the tool
  registrar, the WebSocket events from the hub, the CLI from Helm, and settings keys from `ArmadaSettings`, and check
  them into `docs/API_SURFACE_1.0.md` as the frozen baseline.
- [x] **W2.2 Remove or label what is not ready.** Decide each of: the keyword `POST /api/v1/ask` responder (remove
  or keep as documented fallback), `create_voyage` / `retry_mission` references in Ask work linking (remove or add the
  tools), the Harbor split-mode transport (finish or mark experimental), and any other "being rolled out" item in the
  CHANGELOG. Experimental surfaces are marked in docs, OpenAPI, and MCP descriptions and are excluded from the
  compatibility promise.
- [x] **W2.3 Consistency pass.** One naming and shape review: enumerate request/response shapes, error bodies
  (`ApiErrorResponse` everywhere, stable error codes), status codes (404 vs 403 for cross-tenant), pagination fields,
  PascalCase JSON on REST, camelCase on WebSocket payloads (document the difference or unify it).
- [x] **W2.4 Compatibility tests.** A contract test that compares the live surface to `API_SURFACE_1.0.md` and fails
  on removals or incompatible changes (additions allowed).
- [x] **W2.5 Deprecation policy.** `docs/COMPATIBILITY.md`: what is covered, how deprecations are announced (one
  minor release with warnings before removal in the next major), and how experimental surfaces work.

### W3. Upgrades and data safety

- [x] **W3.1 Upgrade test.** Automated job that installs v0.9.0, seeds representative data (fleets, vessels,
  missions in every status, voyages, merge queue, personas and pipelines with edits, prompt template overrides,
  Ask threads, fleet actions, health, import batches), upgrades to the candidate, and verifies the data and that
  edited templates are preserved. Runs on all four providers.
- [x] **W3.2 Backup before migrate.** On startup, when pending migrations exist, take an automatic backup (SQLite
  file copy; documented dump command or a refusal-with-instructions for server providers) before applying them, and
  log where it went.
- [x] **W3.3 Migration hygiene.** Verify every migration is idempotent and additive on all providers; document the
  supported upgrade paths (0.9.x to 1.0 directly).
- [x] **W3.4 Retention.** Settings and background pruning for Ask threads and messages (archive after N days,
  optional delete), fleet action run output, health findings history, import batches, and jobs; request history
  already has retention.
- [x] **W3.5 Restore drill.** Documented and tested restore from backup on each provider.

### W4. Quality and test reliability

- [x] **W4.1 CI on every push and PR.** Workflow that runs `Test.Automated` on Windows, macOS, and Linux (net8.0 and
  net10.0), the dashboard `npm ci && npm run build && npm run test:run`, and fails on new compiler warnings.
  _Notes:_ ci.yml validated with actionlint; not yet run on Actions.
- [ ] **W4.2 Fix flakiness.** Root-cause the end-to-end server startup timeouts (readiness waits on both REST and
  MCP listeners; ports; machine load) and the SQL Server timeouts (native amd64 runner instead of emulation). Target:
  20 consecutive green full runs.
- [x] **W4.3 Provider parity in CI.** Nightly `run-db-parity-tests.sh` against all four providers.
- [ ] **W4.4 Coverage of risky paths.** Landing (local merge, PR, merge queue), recovery of stalled captains, Harbor
  link loss and reconnect, self-rebuild rollback, cancel paths for voyages, fleet action runs, health evaluation, and
  imports.
- [ ] **W4.5 Performance baseline.** Seed 500 vessels, 10,000 missions, 1,000 voyages, 50 captains; measure dashboard
  page loads, enumerate endpoints, the jobs list the header indicator polls (page it), health evaluation throughput,
  and Ask turn latency. Record the numbers in `docs/PERFORMANCE.md` and fix anything over agreed budgets.

### W5. Packaging and distribution

- [x] **W5.1 Decide the supported install paths for 1.0.** Recommended minimum: Docker (Admiral + dashboard + proxy),
  NuGet/global tool for the CLI, Windows installers (Inno for Harbor, WiX for the server), macOS signed and notarized
  `.app` for Harbor and `.pkg` for the server, Linux Deb/Rpm. Anything else (Homebrew, Scoop, Chocolatey, Winget,
  AppImage) ships only if implemented; otherwise disable those channels in `publisher.json` and remove them from the
  README.
- [~] **W5.2 Implement the missing chosen channels** in `Armada.Publisher`, with code signing (Windows Authenticode,
  Apple Developer ID plus notarization) and checksums published with each release.
  _Notes:_ Dmg, Pkg, and WiX channels implemented with signing/notarization gated on secrets and SHA256SUMS per release; still needs certificates and a Windows run of WiX.
- [~] **W5.3 Harbor as a real macOS app.** `.app` bundle with `Info.plist`, `.icns` generated from
  `Assets/logo-macos.png`, login-item support; keep the runtime Dock-icon fallback for `dotnet run`.
  _Notes:_ .app with Info.plist and generated .icns built and verified on macOS; login-item support not done.
- [x] **W5.4 Repository requirements.** Add `DOCKERHUB_README.md` and `docker/update.bat` (and `.sh`) per the
  repository requirements; confirm every service has a Docker healthcheck with `interval: 5s`, `retries: 2`.
- [ ] **W5.5 Install verification.** For each supported path, a clean-machine install test (VM or CI runner) that
  reaches a logged-in dashboard and dispatches one mission.
- [ ] **W5.6 Service and startup registration flags.** The installers pass `--install-service`,
  `--uninstall-service`, `--run-service` (server) and `--install-startup` / `--uninstall-startup` (Harbor) from
  `publisher.json`, but neither program implements them, so the Inno and WiX installers launch the program instead of
  registering it and the systemd unit passes an ignored argument. Implement the flags (Windows service, systemd unit,
  launchd agent; Harbor login item) or remove them before 1.0.

### W6. Product completeness and usability

- [ ] **W6.1 Simulated user testing.** Run a full session per `SIMULATED_USER_TESTING.md` against each release
  candidate in an isolated `armada-usertest` stack; triage S1/S2 findings before release.
- [x] **W6.2 Captain support matrix.** `docs/CAPTAINS.md`: supported versions of Claude Code, Codex, Gemini, Cursor,
  Mux, OpenCode, and API endpoints, and which features each supports (missions, planning, Ask threads, Ask approval
  gating -- today only Claude Code and ApiEndpoint are gated -- streaming, thinking, tool display).
- [x] **W6.3 Ask approval gating for every runtime.** Extend thread-scoped MCP to the remaining CLI runtimes without
  breaking their logins, or document clearly in the UI that a captain's actions are not gated.
- [x] **W6.4 Visual QA.** One full pass of every page and modal at 1920, 1512, 1280, 768, and 390 px in light and dark;
  fix the remaining Vessel Health all-columns overflow at 1280.
- [x] **W6.5 Accessibility.** Keyboard-only walkthrough of the main flows, screen-reader labels, focus traps in every
  modal (the shared confirm dialog lacks a `dialog` role), color contrast.
- [x] **W6.6 Localization.** Native-speaker review of all eight non-English locales, or ship them labeled "beta" in
  the language picker.
- [~] **W6.7 Onboarding.** First-run path from install to first landed mission in under ten minutes, verified in
  W6.1.

### W7. Documentation

- [ ] **W7.1 Docs audit.** README, GETTING_STARTED, REST_API, MCP_API, WEBSOCKET_API, and the per-runtime orchestrator
  guides checked against the frozen surface; remove em-dashes repo-wide per the writing requirements.
- [x] **W7.2 Operations guide.** `docs/OPERATIONS.md`: deployment topologies, ports, TLS, backups, upgrades,
  retention, telemetry, troubleshooting.
- [x] **W7.3 Security guide.** `SECURITY.md` with how to report vulnerabilities and the security model summary from
  W1.

### W8. Release process

- [x] **W8.1 Release checklist** in `docs/RELEASING.md` (tests, parity, upgrade test, user testing, packaging, signing,
  CHANGELOG, tag).
- [ ] **W8.2 Beta.** v0.10.0 (alpha label dropped only on explicit approval) containing W1, W2.2-W2.3, W3.1-W3.2, W4.1.
- [ ] **W8.3 Release candidates.** v1.0.0-rc.N; each RC gets a simulated user testing session and at least a week of
  real use on the maintainer's own repositories.
- [ ] **W8.4 GA.** Tag v1.0.0 after an RC passes two weeks with no breaking change and no open S1/S2 findings.

Agents never change version numbers on their own (see `VERSIONING.md`); every version bump in W8 happens only on the
maintainer's explicit instruction.

## Out of scope for 1.0

The optional Phase D items in `docs/CODEHUB_CAPABILITIES.md` (GitHub signals, more dependency ecosystems, health
history), captain-suggested Ask thread titles, commit-message enforcement at landing, and the terminal client in
`TUI_APP_PLAN.md` are post-1.0 unless a decision below pulls them in.

## Decisions (resolved 2026-10-04: the maintainer approved the plan and its recommendations)

- **D1. Install paths at 1.0:** Docker (Admiral, dashboard, proxy); NuGet global tool for the CLI; Windows (Inno for
  Harbor, WiX for the server); macOS (signed and notarized `.app` in a `.dmg` for Harbor, `.pkg` for the server); Linux
  Deb/Rpm. Homebrew, Scoop, Chocolatey, Winget, and AppImage ship only if implemented; otherwise they are disabled in
  `publisher.json` and removed from the README.
- **D2. MCP authentication:** authenticated by default; unauthenticated calls are allowed only when the MCP listener is
  bound to loopback and `Mcp.AllowUnauthenticatedLoopback` is true (the default).
- **D3. Not-ready surfaces:** remove the keyword `POST /api/v1/ask` responder (the dashboard does not call it; Ask
  threads replace it); remove `create_voyage` / `retry_mission` from Ask work linking; mark the Harbor split-mode
  transport experimental.
- **D4. Locales:** the eight non-English locales ship labeled "beta" in the language pickers until reviewed by native
  speakers.
- **D5. TUI:** built in parallel per `TUI_APP_PLAN.md`; whether it is part of the 1.0 compatibility promise is decided at
  the first release candidate.

## Progress Log

| Date | Author | Task(s) | Change |
|------|--------|---------|--------|
| 2026-10-04 | (design) | -- | Plan drafted from the 2026-10-04 working session. |
| 2026-10-04 | ops agent | W4.1, W4.3, W5.1-W5.4, W6.2, W7.2, W8.1 | CI on push/PR (3 OSes x net8/net10, dashboard dist check), nightly parity, non-D1 channels disabled, DOCKERHUB_README and docker/update scripts, healthchecks on every HTTP service, macOS Harbor .app/.dmg and server .pkg built and verified locally, WiX channel (unverified on Windows), SHA256SUMS, CAPTAINS/OPERATIONS/RELEASING docs. Found W5.6. |
| 2026-10-04 | data agent | W3.1-W3.5 | Upgrade test from v0.9.0 (release commit e456b008 and image commit 574a8a1a) passes on SQLite, PostgreSQL, MySQL, SQL Server; SQLite backup before migrate, server-provider warning/confirmation; all migrations re-runnable (fixed SQLite v15, PostgreSQL guards, SQL Server v1); retention for Ask threads, jobs, import batches; SQLite restore drill; docs/UPGRADING.md. Follow-up: data expiry of voyages/missions/signals/events is SQLite-only. |
| 2026-10-04 | security agent | W1.1-W1.7, W7.3, D3 | Surface inventory (24 findings fixed, 19 open with owners), per-route and per-tool authorization registries checked centrally with a coverage test, MCP auth per D2, forced password change and default-token retirement, non-loopback refusal with defaults, per-captain auto-approve switch, audit.command events, secret redaction, non-root pinned containers, security.yml, SECURITY.md; keyword /api/v1/ask and `armada ask` removed. Critical fixes: unauthenticated server stop/restart/rebuild (RCE), anonymous MCP as tenant admin with host-path backup/restore, shell via check-run command override. Merged; full suite 2954 (2945 passed, 9 skipped, 0 failed). |
| 2026-10-04 | api agent | W2.1-W2.5, D3 | docs/API_SURFACE_1.0.md + api-surface-1.0.json (350 REST routes, 146 MCP tools, 59 WebSocket commands, 65 events, 58 CLI commands, 166 settings; regenerated by scripts/common/generate-api-surface.sh), E2E.ApiContract (fails on breaking changes, allows additions), docs/COMPATIBILITY.md, Harbor split mode and self-rebuild marked experimental, error codes match statuses (~300 sites), cross-tenant 404s. Open consistency items listed in the agent report (unparseable bodies return 500, bare-array list routes, untyped OpenAPI routes). Merged; full suite 3064 (3055 passed, 9 skipped, 0 failed). |
| 2026-10-04 | usability agent | W6.3-W6.7 | Ask approval gating for all six CLI runtimes without breaking their logins (full turns verified with Claude Code, Codex, OpenCode; Gemini, Cursor, Mux to the MCP connection); Codex 0.159+ launch fix; visual QA of 136 scenarios x 5 widths x 2 themes (phone navigation, Vessel Health 1280, pipeline modal fixed); accessibility (dialogs, focus traps, keyboard menus, ~250 labels; axe 0 critical/serious, from 662 critical and 4,350+ serious nodes); locales labeled beta; onboarding 18 s from sign-in to a landed mission; Landing Mode None/MergeQueue no longer merge into the working directory. W6.7 stays open until W6.1. Merged; full suite 3084 with 1 known categorization flake (passes 4/4 alone) and 1 stale Codex assertion fixed. |
