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

- [ ] **W1.1 Surface inventory.** List every entry point with its authentication, authorization rule, tenant
  scoping, and input validation: all REST routes (from the route registrars and `/openapi.json`), every MCP tool,
  every WebSocket route and command, the Harbor link protocol, the proxy relay, and every place that touches the host
  file system or runs a process (vessel import browse/discover, Fleet Actions commands, workspace exec, docks, git,
  self-rebuild, backup/restore).
  _Acceptance:_ `docs/SECURITY_REVIEW.md` with one row per entry point and an owner for each gap.
- [ ] **W1.2 Authorization model.** Replace the path-prefix rules with an explicit `(ResourceType, Operation)`
  requirement declared on each route and MCP tool, checked centrally, with a test that fails the build when a route or
  tool has no declared requirement. Keep `PermissionLevel` as the mapping target if that is simpler, but make the
  declaration per route, not per prefix.
  _Acceptance:_ the "undeclared route" test exists and passes; the AUTHENTICATION.md nonconformance note is removed.
- [ ] **W1.3 MCP authentication.** Decide and implement: authenticated by default, with an explicit
  `Mcp.AllowUnauthenticatedLoopback` setting (default true only when bound to loopback) for the local developer
  experience. Non-loopback MCP requires a token.
  _Acceptance:_ an unauthenticated MCP call to a non-loopback binding is refused (test); local Claude Code setup in the
  README still works unchanged.
- [ ] **W1.4 Safe defaults.** Force a password change for `admin@armada` on first dashboard login; disable the
  `default` bearer token once a real credential exists; refuse to bind to a non-loopback address with default
  credentials unless an explicit override is set; show a persistent dashboard warning while defaults are in use.
  _Acceptance:_ a fresh Docker install cannot be driven with `Bearer default` from another host.
- [ ] **W1.5 Captain execution safety.** Document the auto-approve flags in a "Running agents safely" section, add a
  per-vessel and per-captain setting to run CLI captains without auto-approve where the runtime supports it, and make
  Fleet Actions Command runs and workspace exec require TenantAdmin plus an audit record.
  _Acceptance:_ audit records exist for every shell command Armada runs on a user's behalf.
- [ ] **W1.6 Secrets.** Verify no API keys, tokens, or passwords are returned by any read endpoint, written to logs,
  or captured in request history bodies; redact them in captured bodies, not just headers.
  _Acceptance:_ a test seeds known secrets and greps responses, logs, and request history for them.
- [ ] **W1.7 Dependency and container hygiene.** Enable dependency vulnerability scanning (NuGet and npm) in CI, run
  the containers as a non-root user, and pin base images.
- [ ] **W1.8 External review.** One review pass by someone other than the author against the W1.1 inventory.

### W2. API and contract freeze

- [ ] **W2.1 Inventory the public surface.** Generate the REST surface from OpenAPI, the MCP surface from the tool
  registrar, the WebSocket events from the hub, the CLI from Helm, and settings keys from `ArmadaSettings`, and check
  them into `docs/API_SURFACE_1.0.md` as the frozen baseline.
- [ ] **W2.2 Remove or label what is not ready.** Decide each of: the keyword `POST /api/v1/ask` responder (remove
  or keep as documented fallback), `create_voyage` / `retry_mission` references in Ask work linking (remove or add the
  tools), the Harbor split-mode transport (finish or mark experimental), and any other "being rolled out" item in the
  CHANGELOG. Experimental surfaces are marked in docs, OpenAPI, and MCP descriptions and are excluded from the
  compatibility promise.
- [ ] **W2.3 Consistency pass.** One naming and shape review: enumerate request/response shapes, error bodies
  (`ApiErrorResponse` everywhere, stable error codes), status codes (404 vs 403 for cross-tenant), pagination fields,
  PascalCase JSON on REST, camelCase on WebSocket payloads (document the difference or unify it).
- [ ] **W2.4 Compatibility tests.** A contract test that compares the live surface to `API_SURFACE_1.0.md` and fails
  on removals or incompatible changes (additions allowed).
- [ ] **W2.5 Deprecation policy.** `docs/COMPATIBILITY.md`: what is covered, how deprecations are announced (one
  minor release with warnings before removal in the next major), and how experimental surfaces work.

### W3. Upgrades and data safety

- [ ] **W3.1 Upgrade test.** Automated job that installs v0.9.0, seeds representative data (fleets, vessels,
  missions in every status, voyages, merge queue, personas and pipelines with edits, prompt template overrides,
  Ask threads, fleet actions, health, import batches), upgrades to the candidate, and verifies the data and that
  edited templates are preserved. Runs on all four providers.
- [ ] **W3.2 Backup before migrate.** On startup, when pending migrations exist, take an automatic backup (SQLite
  file copy; documented dump command or a refusal-with-instructions for server providers) before applying them, and
  log where it went.
- [ ] **W3.3 Migration hygiene.** Verify every migration is idempotent and additive on all providers; document the
  supported upgrade paths (0.9.x to 1.0 directly).
- [ ] **W3.4 Retention.** Settings and background pruning for Ask threads and messages (archive after N days,
  optional delete), fleet action run output, health findings history, import batches, and jobs; request history
  already has retention.
- [ ] **W3.5 Restore drill.** Documented and tested restore from backup on each provider.

### W4. Quality and test reliability

- [ ] **W4.1 CI on every push and PR.** Workflow that runs `Test.Automated` on Windows, macOS, and Linux (net8.0 and
  net10.0), the dashboard `npm ci && npm run build && npm run test:run`, and fails on new compiler warnings.
- [ ] **W4.2 Fix flakiness.** Root-cause the end-to-end server startup timeouts (readiness waits on both REST and
  MCP listeners; ports; machine load) and the SQL Server timeouts (native amd64 runner instead of emulation). Target:
  20 consecutive green full runs.
- [ ] **W4.3 Provider parity in CI.** Nightly `run-db-parity-tests.sh` against all four providers.
- [ ] **W4.4 Coverage of risky paths.** Landing (local merge, PR, merge queue), recovery of stalled captains, Harbor
  link loss and reconnect, self-rebuild rollback, cancel paths for voyages, fleet action runs, health evaluation, and
  imports.
- [ ] **W4.5 Performance baseline.** Seed 500 vessels, 10,000 missions, 1,000 voyages, 50 captains; measure dashboard
  page loads, enumerate endpoints, the jobs list the header indicator polls (page it), health evaluation throughput,
  and Ask turn latency. Record the numbers in `docs/PERFORMANCE.md` and fix anything over agreed budgets.

### W5. Packaging and distribution

- [ ] **W5.1 Decide the supported install paths for 1.0.** Recommended minimum: Docker (Admiral + dashboard + proxy),
  NuGet/global tool for the CLI, Windows installers (Inno for Harbor, WiX for the server), macOS signed and notarized
  `.app` for Harbor and `.pkg` for the server, Linux Deb/Rpm. Anything else (Homebrew, Scoop, Chocolatey, Winget,
  AppImage) ships only if implemented; otherwise disable those channels in `publisher.json` and remove them from the
  README.
- [ ] **W5.2 Implement the missing chosen channels** in `Armada.Publisher`, with code signing (Windows Authenticode,
  Apple Developer ID plus notarization) and checksums published with each release.
- [ ] **W5.3 Harbor as a real macOS app.** `.app` bundle with `Info.plist`, `.icns` generated from
  `Assets/logo-macos.png`, login-item support; keep the runtime Dock-icon fallback for `dotnet run`.
- [ ] **W5.4 Repository requirements.** Add `DOCKERHUB_README.md` and `docker/update.bat` (and `.sh`) per the
  repository requirements; confirm every service has a Docker healthcheck with `interval: 5s`, `retries: 2`.
- [ ] **W5.5 Install verification.** For each supported path, a clean-machine install test (VM or CI runner) that
  reaches a logged-in dashboard and dispatches one mission.

### W6. Product completeness and usability

- [ ] **W6.1 Simulated user testing.** Run a full session per `SIMULATED_USER_TESTING.md` against each release
  candidate in an isolated `armada-usertest` stack; triage S1/S2 findings before release.
- [ ] **W6.2 Captain support matrix.** `docs/CAPTAINS.md`: supported versions of Claude Code, Codex, Gemini, Cursor,
  Mux, OpenCode, and API endpoints, and which features each supports (missions, planning, Ask threads, Ask approval
  gating -- today only Claude Code and ApiEndpoint are gated -- streaming, thinking, tool display).
- [ ] **W6.3 Ask approval gating for every runtime.** Extend thread-scoped MCP to the remaining CLI runtimes without
  breaking their logins, or document clearly in the UI that a captain's actions are not gated.
- [ ] **W6.4 Visual QA.** One full pass of every page and modal at 1920, 1512, 1280, 768, and 390 px in light and dark;
  fix the remaining Vessel Health all-columns overflow at 1280.
- [ ] **W6.5 Accessibility.** Keyboard-only walkthrough of the main flows, screen-reader labels, focus traps in every
  modal (the shared confirm dialog lacks a `dialog` role), color contrast.
- [ ] **W6.6 Localization.** Native-speaker review of all eight non-English locales, or ship them labeled "beta" in
  the language picker.
- [ ] **W6.7 Onboarding.** First-run path from install to first landed mission in under ten minutes, verified in
  W6.1.

### W7. Documentation

- [ ] **W7.1 Docs audit.** README, GETTING_STARTED, REST_API, MCP_API, WEBSOCKET_API, and the per-runtime orchestrator
  guides checked against the frozen surface; remove em-dashes repo-wide per the writing requirements.
- [ ] **W7.2 Operations guide.** `docs/OPERATIONS.md`: deployment topologies, ports, TLS, backups, upgrades,
  retention, telemetry, troubleshooting.
- [ ] **W7.3 Security guide.** `SECURITY.md` with how to report vulnerabilities and the security model summary from
  W1.

### W8. Release process

- [ ] **W8.1 Release checklist** in `docs/RELEASING.md` (tests, parity, upgrade test, user testing, packaging, signing,
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

## Decisions needed

- **D1.** Which install paths are supported at 1.0 (W5.1).
- **D2.** MCP authentication default for non-loopback bindings (W1.3).
- **D3.** Fate of the keyword `POST /api/v1/ask` responder and the Harbor split-mode transport (W2.2).
- **D4.** Whether non-reviewed locales ship as "beta" or are held back (W6.6).
- **D5.** Whether the TUI client is part of 1.0.

## Progress Log

| Date | Author | Task(s) | Change |
|------|--------|---------|--------|
| 2026-10-04 | (design) | -- | Plan drafted from the 2026-10-04 working session. |
