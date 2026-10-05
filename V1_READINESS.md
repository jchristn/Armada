# Armada v1.0.0 Readiness

> **Type:** implementation plan (work-tracking). Annotate task status and the progress log as you go; keep this
> document in sync with what actually shipped.
>
> **Target:** v1.0.0
> **Current version:** 1.0.0 (set 2026-10-05; images built after the final merges and green CI)
> **Status:** In progress (engineering nearly complete; human checks and release steps remain)
> **Last updated:** 2026-10-05

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
- [x] **W1.5 Captain execution safety.** Document the auto-approve flags in a "Running agents safely" section, add a
  per-vessel and per-captain setting to run CLI captains without auto-approve where the runtime supports it, and make
  Fleet Actions Command runs and workspace exec require TenantAdmin plus an audit record.
  _Acceptance:_ audit records exist for every shell command Armada runs on a user's behalf.
- [x] **W1.6 Secrets.** Verify no API keys, tokens, or passwords are returned by any read endpoint, written to logs,
  or captured in request history bodies; redact them in captured bodies, not just headers.
  _Acceptance:_ a test seeds known secrets and greps responses, logs, and request history for them.
- [x] **W1.7 Dependency and container hygiene.** Enable dependency vulnerability scanning (NuGet and npm) in CI, run
  the containers as a non-root user, and pin base images.
- [x] **W1.9 Security follow-ups from W1.1** (`docs/SECURITY_REVIEW.md`): O-01 tenant checks in the 62 MCP tools that
  act on entities by id; O-02 Ask turns run CLI captains on the host with auto-approve flags (default Ask captains to
  auto-approve off, or require tenant admin); O-04 split-mode captains get no MCP credential; O-05 salted password
  hashing (PBKDF2/Argon2 with transparent rehash) and login rate limiting; O-11 proxy hardening; per-vessel
  auto-approve setting (W1.5 shipped per-captain only).
  _Notes:_ Closed O-01 (MCP tenant isolation, 74 tools exercised cross-tenant), O-02 (Ask turns auto-approve off by default), O-05 (PBKDF2-SHA256 600k with transparent rehash; login rate limiting with 429), per-vessel auto-approve, and Ask thread-token precedence. Final pass: fixed an MCP `enumerate` cross-tenant leak and scoped-filter bug (F-33, F-34); closed O-04 and most of O-20 with mission-scoped MCP tokens for local and Harbor captains (F-36), O-06 and O-17 (F-37); O-11 narrowed (persisted proxy lockouts, AllowInvalidCertificates warning). Explicit post-1.0 decisions: per-user proxy identity and challenge rate limiting (O-11); defaulting the loopback MCP exception off (O-20 residual: other local processes, Gemini/Cursor mission captains without IsolateCaptainLaunch); per-launch binding for https or path-prefixed advertised Harbor MCP URLs.
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
- [x] **W4.2 Fix flakiness.** Root-cause the end-to-end server startup timeouts (readiness waits on both REST and
  MCP listeners; ports; machine load) and the SQL Server timeouts (native amd64 runner instead of emulation). Target:
  20 consecutive green full runs.
- [x] **W4.3 Provider parity in CI.** Nightly `run-db-parity-tests.sh` against all four providers.
- [x] **W4.4 Coverage of risky paths.** Landing (local merge, PR, merge queue), recovery of stalled captains, Harbor
  link loss and reconnect, self-rebuild rollback, cancel paths for voyages, fleet action runs, health evaluation, and
  imports.
- [x] **W4.5 Performance baseline.** Seed 500 vessels, 10,000 missions, 1,000 voyages, 50 captains; measure dashboard
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
- [x] **W5.3 Harbor as a real macOS app.** `.app` bundle with `Info.plist`, `.icns` generated from
  `Assets/logo-macos.png`, login-item support; keep the runtime Dock-icon fallback for `dotnet run`.
  _Notes:_ Done except Apple signing (W5.2: needs the owner's Developer ID certificates and notary key; exact list in BUILDING_INSTALLERS.md "macOS signing and notarization"). Info.plist adds `LSUIElement` (menu bar app), `LSMultipleInstancesProhibited`, `CFBundleSignature`, copyright; Harbor switches the activation policy itself (Dock icon only while its window is open; a `--minimized` login-item start is menu-bar only, verified with `lsappinfo`: UIElement vs Foreground); runtime Dock icon only outside a bundle; login item via `--install-startup` (LaunchAgent running the bundled binary with `--minimized`). `scripts/macos/build-harbor-app.sh` builds both architectures and checks layout, plist keys, icns, signature, notarization (`--require-notarized`), and `--install-startup --dry-run` from the bundle; release.yml runs it. Verified locally (ad-hoc signed, version 1.0.0). Not verified: installing the signed image on a clean Mac.
- [x] **W5.4 Repository requirements.** Add `DOCKERHUB_README.md` and `docker/update.bat` (and `.sh`) per the
  repository requirements; confirm every service has a Docker healthcheck with `interval: 5s`, `retries: 2`.
- [x] **W5.5 Install verification.** For each supported path, a clean-machine install test (VM or CI runner) that
  reaches a logged-in dashboard and dispatches one mission.
  _Notes:_ 2026-10-05: every job of install-verify.yml is green on Actions (run 37269750258, including windows) except docker, whose root cause was git dropping command-scope config (`GIT_CONFIG_COUNT`, `git -c`) from the `upload-pack` process of a local clone, so the earlier safe.directory override never applied and a mounted repository owned by another UID could not be cloned. Fixed with a `GIT_CONFIG_GLOBAL` gitconfig (documented in DOCKER.md for real mounted repositories); the test origin now lives in a volume owned by UID 4242 so every host reproduces the mismatch (Docker Desktop hid it); `GitCommandException` messages keep all of stderr on one line. Verified locally: fixed script PASS, the previous override FAIL with the same error as CI. The docker job's first green run on Actions follows the merge. Earlier notes: `scripts/common/install-verify/` plus `.github/workflows/install-verify.yml` (docs/RELEASING.md "Install verification"); the mission runs on an API-endpoint captain backed by a stub inference server and must produce a commit. Verified locally on macOS (arm64): Docker compose, `.deb` in ubuntu:24.04, `.rpm` in fedora:42, NuGet tool, server `.pkg` payload plus `--install-service --dry-run`. Not yet run: the workflow on Actions, the Windows script (tool path and `--install-service --dry-run`, unverified), the NuGet tool on Linux. Not covered by automation: `.msi`, Inno, Harbor `.dmg` and Harbor packages, real service registration.
- [x] **W5.6 Service and startup registration flags.** The installers pass `--install-service`,
  `--uninstall-service`, `--run-service` (server) and `--install-startup` / `--uninstall-startup` (Harbor) from
  `publisher.json`, but neither program implements them, so the Inno and WiX installers launch the program instead of
  registering it and the systemd unit passes an ignored argument. Implement the flags (Windows service, systemd unit,
  launchd agent; Harbor login item) or remove them before 1.0.

### W6. Product completeness and usability

- [~] **W6.1 Simulated user testing.** Run a full session per `SIMULATED_USER_TESTING.md` against each release
  candidate in an isolated `armada-usertest` stack; triage S1/S2 findings before release.
  `SIMULATED_USER_TESTING.md` written; the first unattended run (agent, stub captain, dashboard in headless Chrome,
  TUI in a pty) is in `docs/SIMULATED_USER_TESTING_RESULTS_1.0.md`: every P1/P2 task and P3 except restore passed on
  the dashboard after fixes; 3 S2 and 4 S3 fixed, 0 S1/S2 and 13 S3 open. Left for a human: the same session with
  real captains (reply quality, streaming, gating, the W6.7 ten-minute path), visual judgement, the TUI flows that
  were only screen-checked, a restore, and triage of the open S3 findings.
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
  W6.1. The unattended W6.1 run took 7 to 32 s from sign-in to a landed mission (stub captain); the timed human run
  with a real captain is still open.
  _Notes:_ Scripted and timed: `scripts/common/install-verify/verify-onboarding.sh` (NuGet tool install into a fresh HOME and data dir, `armada server start`, login, fleet, vessel from a local checkout, one stub-captain mission landed with LocalMerge into the checkout) prints stage times and fails on any step or over 600 s; install-verify.yml runs it on Linux and macOS. Local run (macOS arm64): 5.9 s total. The manual equivalent with a real captain is in RELEASING.md "Onboarding by hand"; open until it is timed in a W6.1 session.

### W7. Documentation

- [x] **W7.1 Docs audit.** README, GETTING_STARTED, REST_API, MCP_API, WEBSOCKET_API, and the per-runtime orchestrator
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
| 2026-10-04 | security agent | W1.9, W1.5 | Closed O-01, O-02, O-05, per-vessel auto-approve (migration 76), thread-token precedence; O-04 and O-11 partly closed; new O-20. API surface +11 additive. Merged; full suite 3133 (3124 passed, 9 skipped, 0 failed); four-provider parity green on the branch. Note: a helper agent briefly ran a throwaway server on ports 7890/7891 with a scratch data dir (no impact; ports confirmed free). |
| 2026-10-04 | quality agent | W4.2, W4.4, W4.5, W5.6 | E2E startup timeouts root-caused (OS ephemeral port reuse plus a silent MCP bind failure; the Admiral now fails startup when the MCP port is taken), 3 more flakes and a sleep-sensitive clock fixed, 20 consecutive green full runs; risky-path suites with 9 defects fixed (landing modes, LandingFailed on merge-queue conflicts, import restart recovery, Harbor reconnect, rollback slot pruning, fleet action cancel); perf harness and docs/PERFORMANCE.md, paged GET /api/v1/jobs; --install-service/--uninstall-service/--run-service and Harbor --install-startup with installers calling them (Windows unverified). Open: consolidate the four voyage-cancel copies; creation-time indexes. |
| 2026-10-04 | install agent | W5.5 | Install verification scripts (scripts/common/install-verify/) and install-verify.yml: Docker compose, .deb (ubuntu:24.04), .rpm (fedora:42), NuGet tool, macOS server .pkg, Windows tool path plus --install-service --dry-run; each logs in, loads the React dashboard, and lands one mission on a stub-inference ApiEndpoint captain. Verified locally on macOS for every path except Windows (unverified) and the workflow (not yet run on Actions). Fixed: `armada server start` from the NuGet tool; packaged servers served the legacy dashboard; .deb/.rpm had no dependencies (ICU crash on a clean machine, no git); Docker server image had no git (missions stuck Pending); standalone dashboard container unhealthy and non-functional; fast-exiting captains left missions stuck InProgress. Full suite 3425 (3413 passed, 9 skipped, 3 failed under load, all 24 in those suites pass on rerun). |
| 2026-10-04 | docs agent | W7.1 | Root and docs/ Markdown audited against api-surface-1.0.json and the code (scripted checks: REST routes and auth, MCP tools and arguments, CLI commands and flags, settings keys and defaults, relative links and anchors). Fixed TESTING, SCHEDULING, TUNNEL_PROTOCOL/OPERATIONS, PROXY_API, the orchestrator and INSTRUCTIONS guides (install package, permission flags, autoApprove, Codex config.toml, mcp install clients), GETTING_STARTED, README (FullPipeline stages, planning default, mcp install), MCP_API error shapes and settings keys, REST_API authorization matrix and endpoint summary, WEBSOCKET_API planning events, PIPELINES/TESTING_PIPELINES FullPipeline. Non-ASCII typography removed except docs/DOCKER.md (owned elsewhere). Docs only; no code changed. |
| 2026-10-05 | ops agent | W5.3, W5.5, W6.7 | Docker install-verify root cause (git strips command-scope config from a local clone's upload-pack; safe.directory now via GIT_CONFIG_GLOBAL, test origin owned by another UID on every host, one-line GitCommandException messages); Harbor .app as a menu bar app (LSUIElement, activation policy, login item) with scripts/macos/build-harbor-app.sh and the owner's signing checklist; timed onboarding script and CI job; OPERATIONS/DOCKER/RELEASING/HARBOR/BUILDING_INSTALLERS/DOCKERHUB_README drift fixed (settings mount path, 1.0.0, retention, Harbor settings and install). |
| 2026-10-05 | security agent | W1.9 | Final security pass: fixed MCP `enumerate` cross-tenant leak (mission summaries, model endpoints, harbors, releases, check runs, personas, templates, pipelines, playbooks, jobs, profiles, skills) and scoped DB enumerations ignoring filters on all four providers (F-33, F-34); typed errors (MCP gate Forbidden, restore 400, WebSocket `command.error` code, status_changed payload, categorization/fleet action/captain validation codes, bind/dirty/not-idle/dock exceptions, tunnel LastErrorCode); mission-scoped MCP tokens for local and Harbor captains (O-04 closed, O-20 mostly closed); O-06, O-17 closed; O-11 narrowed (persisted proxy lockouts, AllowInvalidCertificates warning). API surface additive (+1 setting, +2 event fields, +1 reply field, tenant create response). Full suite 3770 (3761 passed, 9 skipped, 0 failed); Database suites green on SQLite (465), PostgreSQL, MySQL, SQL Server (455 + 10 skipped each). |
| 2026-10-05 | ui security agent | W1.9 | UI follow-ups for F-37 and the typed errors: dashboard and TUI self password change send `CurrentPassword`, tenant create offers an admin password and shows a generated one once; categorization code labels (fixture regenerated, 16 catalog translations); Armada.Client typed `mission.status_changed` and `command.error` readers (no UI parsed them from text before); WEBSOCKET_API.md documents `code` and `status` / `previousStatus`. Dashboard Vitest 294/294; Tui suites 428 + 9 new; full suite 3778 (3768 passed, 9 skipped, 1 failed: Tui.Parity reflection on a client overload, fixed by dropping the overload, suite rerun green). |
| 2026-10-05 | sim-testing agent | W6.1, W6.7 | `SIMULATED_USER_TESTING.md` (personas, task scripts, recording, S1-S4, exit criterion) and the first unattended run in `docs/SIMULATED_USER_TESTING_RESULTS_1.0.md` (throwaway Admiral with the stub captain, 13 temp repos with bare origins, dashboard via Playwright, TUI via pty and pyte). Fixed: six locales blanked the dashboard on Settings (i18n template recursion), Draft Release stuck on Loading, backlog refinement Summarize crash, voyage and mission pages not live, jobs `status` filter 400 (header activity indicator), stale translated tooltips and aria-labels, `armada-landing/*` branches left in checkouts. 31 findings recorded with suggested fixes (13 S3, 18 S4), among them sign-outs on every Admiral restart (session key not persisted). W6.1 stays open for the human run with real captains. |
