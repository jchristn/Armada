# Harbor Runner Plan

A full-product plan to split agent execution out of the Admiral into a detached, host-side
runner ("Harbor"), so the server can run either standalone (today's single-process model) or
inside Docker as a remote server while agents keep executing on the developer's machine with the
developer's own tools and logins.

Status: IN PROGRESS. Multi-Harbor is in scope. Foundation landed (Phase 0 ids/settings, Phase 1
persistence: Harbor entity + capabilities across all four DB drivers + migration v62 + tests).
Target version: 0.10.0.

---

## How to use this plan

Every task has a stable ID (e.g. `SRV-07`) and a checkbox. A developer annotates progress in place:

- `- [ ]` not started
- `- [~]` in progress -- append `-- <initials> <yyyy-mm-dd> <note>`
- `- [x]` done -- append `-- <initials> <yyyy-mm-dd> <PR or commit>`
- `- [-]` dropped or superseded -- append the reason

Keep the per-phase rollup tables at the top of each phase current as tasks close. Do not delete a
task when it is dropped; mark it `[-]` so the history stays legible. Acceptance criteria live under
the tasks that need them; a task is not done until its acceptance criteria pass and the build is
clean (`dotnet build src/Armada.sln` with zero warnings, `npm run build` for the dashboard, and the
Touchstone suites green).

The plan is written so the phases can start in order, but Phase 1 (server persistence + contracts)
and Phase 3 (the Harbor app) share the wire protocol defined in Phase 0 -- freeze that first.

---

## Why this exists

Armada launches captains by calling `Process.Start` on a bare command (`claude`, `codex`, ...) that
it resolves on `PATH`, in a git worktree on the local filesystem, with the Admiral's own environment
and home directory inherited so the CLI picks up the host login. The captain reaches the Admiral's
MCP server at `http://localhost:<mcpPort>/mcp`, and the Admiral shells out to `git` and `gh` on the
same host. Liveness and termination are PID-based. Every one of those facts assumes the agent runs in
the same process table, filesystem, and network namespace as the Admiral.

That assumption is exactly what breaks when you containerize the Admiral. A captain launched from a
container runs *in* the container, not on your machine, and it cannot see your `~/.claude` session,
your repositories, your SSH keys, or your `gh` auth. Mounting all of that into the container is
fragile and, on a Windows or macOS host running Docker Desktop, the worktree path strings cannot even
match on both sides of the mount.

The runner pattern resolves it cleanly. A small host-side app dials *out* to the Admiral and holds an
authenticated WebSocket open; the Admiral pushes work down that connection; the host app executes it
where the tools and logins already live. Because the connection is client-initiated, nothing has to
reach into the host, so container and remote deployments stop fighting the network boundary. The same
seam that lets the Admiral delegate execution also lets it *not* delegate -- in standalone mode it runs
everything in-process, exactly as it does now. One abstraction, two deployment shapes.

---

## Terminology

**Harbor** is the new host-side app: a tray application plus a background runner. The machine it runs
on is where repositories are cloned, worktrees ("docks") live, and captain CLIs execute. The Admiral
dispatches host work to a Harbor.

**Harbor link** is the authenticated, client-to-server WebSocket the Harbor opens to the Admiral.

**Host operation** is any action that touches the host filesystem, host git/gh, or spawns a captain
process: worktree create/remove, clone, fetch, commit, push, PR creation, agent launch/stdin/kill.

**Local mode** (standalone) is today's single process: the Admiral performs host operations in-process.
**Split mode** (remote server) is the Admiral in Docker or on another host, delegating every host
operation to an attached Harbor.

---

## Architecture

```
  Local mode (standalone, unchanged default)
  +-------------------------------------------------------+
  |  Admiral process                                      |
  |   REST + MCP + dashboard + DB (SQLite)                |
  |   IHostExecutor -> LocalHostExecutor  (Process.Start, |
  |                    GitService, DockService in-process)|
  +-------------------------------------------------------+

  Split mode (server in Docker / remote)
  +---------------------------+          +--------------------------------+
  |  Admiral (container)      |  WSS     |  Harbor (host tray app)        |
  |  REST + MCP + dashboard   |<---------|  dials out, authenticates      |
  |  DB (SQLite or Postgres)  |  link    |  HostExecutorServer:           |
  |  IHostExecutor ->         |=========>|   - Armada.Runtimes (captains) |
  |    RemoteHostExecutor  ---+  RPC     |   - GitService / DockService   |
  |  Harbor connection mgr    |<---------|   - repos + docks on host FS   |
  |  publishes MCP port ------+  events  |   captain -> MCP via published |
  +---------------------------+          |   server URL                   |
                                         +--------------------------------+
```

The load-bearing idea is a single seam, `IHostExecutor`, that covers *all* host operations (process
launch, git, gh, worktree lifecycle). `LocalHostExecutor` is the existing behavior lifted behind the
interface. `RemoteHostExecutor` marshals the same operations over the Harbor link. The Admiral chooses
the implementation at composition time based on deployment mode; nothing above the seam changes.

---

## Locked design decisions

These are decided. Where the last open question was "standalone or remote," the decision is whatever
keeps both working from one codebase.

1. **Both modes ship from one build.** Local mode stays the zero-config default; split mode is opt-in
   via settings. No separate "docker edition."
2. **The Harbor owns all host operations, not just process launch.** Git, gh, and worktree lifecycle
   run wherever the repos physically are. In split mode that is the Harbor; in local mode it is the
   Admiral. There is no shared bind mount and no path-matching requirement across the container
   boundary. Git-only operations (landing, merge queue, clone, live diff) therefore require an attached
   Harbor in split mode; when none is attached they queue and the affected work reports a clear
   "waiting for a runner" state rather than failing. Read-only diff display in the dashboard falls back
   to the persisted diff snapshots Armada already stores.
3. **Multiple Harbors across machines are supported.** One Harbor multiplexes many concurrent jobs over
   one link (keyed by a Harbor-assigned job id), and one Admiral drives many Harbors. The critical
   constraint is **dock affinity**: a dock (repo clone + worktree) physically exists on one Harbor's
   filesystem, so routing is decided once, when a dock is provisioned, and every later host operation for
   that mission (agent process, git, landing, diff) is pinned to the Harbor that owns the dock. A Harbor
   is selected by explicit assignment (vessel-to-Harbor), then by capability match (does it have the
   requested runtime and a live login), then by load and health. If the owning Harbor disconnects
   mid-mission the work stalls with a clear status until it returns; a worktree cannot migrate hosts.
   Repos may be cloned on more than one Harbor, and the router prefers a Harbor that already has the
   vessel's repo.
4. **Auth reuses the existing Credential principal.** A Harbor is a non-interactive machine identity:
   a `Credential` row, tenant- and owner-bound, granted least-privilege `Execute` scope, authenticating
   with the signed-request scheme (preferred for remote) or access-key/secret over TLS, then carrying a
   short-lived revocable session on the link. No new principal type, no `x-api-key` on the data plane.
5. **MCP reachability is made configurable.** `ArmadaMcpConfigBuilder` currently hard-codes
   `http://localhost:<port>`. The Admiral advertises its reachable MCP base URL to the Harbor at
   handshake; the Harbor injects that into the captain's MCP config. In local mode the advertised URL
   stays `127.0.0.1:<port>`.
6. **The Harbor is an Avalonia .NET app.** Avalonia gives a cross-platform tray (Windows, macOS, Linux)
   and lets the runner reference `Armada.Runtimes`, `GitService`, and `DockService` directly instead of
   reimplementing them. This is the single UI-framework choice in the plan; a Windows-only WinForms
   fallback is possible only if cross-platform tray support is later descoped.
7. **`127.0.0.1`, never `localhost`,** for every loopback bind and client call across server, Harbor,
   SDK, tests, and healthchecks (Windows IPv6 `::1` stall).
8. **Kill and liveness move behind the executor.** PID tracking is a `LocalHostExecutor` detail; in
   split mode the Harbor owns the PID and reports liveness via heartbeat, and the Admiral maps a captain
   to a Harbor job id rather than a raw OS PID.

---

## Cross-cutting compliance (applies to every task)

Fold these into each task's definition of done rather than repeating them as tasks.

**C# / backend** (`CODE_STYLE.md`, `BACKEND_ARCHITECTURE.md`): usings inside the namespace, system
first then third-party then project, each alphabetized; XML docs on all public members and none on
private; `_PascalCase` private fields with backing-field validation/clamping; no `var`; no tuples; one
class or enum per file; `.ConfigureAwait(false)` on every await in Core/Server; every cancellable async
method takes `CancellationToken token = default`; specific exception types with contextual messages and
`/// <exception>` tags; no `Console.WriteLine` in library code; nullable reference types on. New entity
work follows the typed-column rule (no BLOB/`*_json` catch-all for structured fields), the per-driver
DB pattern across all four providers, and Watson route registrars with typed request/response DTOs and
per-route OpenAPI metadata. No `JsonElement`/`JsonNode` DOM for any fixed contract, including the
Harbor wire messages -- define typed envelope classes.

**Auth** (`AUTHENTICATION.md`): the Harbor is a `Credential`; every delegated command is statically
mapped to a `(ResourceType, Operation)` pair and re-authorized on the server for the life of the link;
the session dies when the backing credential, user, tenant, or session is disabled; secrets are shown
once, stored encrypted with a per-token random IV, redacted to last 4 in responses, compared in
constant time; signed requests enforce bounded clock skew and nonce uniqueness; auth successes,
failures, session lifecycle, credential use, and authorization denials are mandatory audited events.

**Telemetry** (`TELEMETRY_REQUIREMENTS.md`): both the server component and the Harbor app define their
own `Meter` and `ActivitySource` (product-prefixed, low-cardinality labels, no ids or secrets in
labels), export via OTLP to the same Prometheus/Tempo/Loki backends, and ship structured logs because
both do background work. The trace context (`traceparent`) propagates over the Harbor link so a single
trace spans Admiral -> Harbor -> child process; the delegated command starts a child span with status
set explicitly, and child-process execution is a nested integration span. Instrumentation is
best-effort and must never break execution. Reconcile SyslogLogging (house library) with the
`Activity.Current`-correlated Loki model so trace/span ids reach logs.

**Frontend** (`FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, `I18N.md`): pages in
`src/pages`, shared components in `src/components/shared`, one API client (`src/api/client.ts`), types
as interfaces + string-literal unions in `src/types/models.ts`; tables reuse `useResourceTable`,
`Pagination`, `RefreshButton`/`AutoRefreshSelect`, `ActionMenu`, `StatusBadge`, `ConfirmDialog`; live
updates via `WebSocketContext`; every user-facing string wrapped in `t()`; theming via CSS variables in
both light and dark; responsive QA at 1280/768/390; color never the only status signal; nav registered
in `navConfig.tsx` and `navConfig.test.ts` updated; `dist/` rebuilt and committed.

**Repository** (`REPOSITORY_REQUIREMENTS.md`): source only under `src/`, `test/`, `dashboard/`, `sdk/`;
`REST_API.md` + Postman collection under `assets/postman/` kept in sync; `MCP_API.md` kept in sync;
`CHANGELOG.md` updated; `DOCKERHUB_README.md` updated; Docker uses `.yaml`, per-service curl healthcheck
(`interval: 5s`, `retries: 2`, timeout <= interval, probing `127.0.0.1`), `depends_on` with
`condition: service_healthy`, a `docker/update.bat` helper, and images include `curl`.

**Output hygiene:** ASCII only in source, docs, and commit messages (no em dashes or smart quotes).

---

## Phase 0 -- Contracts and the execution seam

Freeze the wire protocol and the seam before building either side against them.

| ID | Task | Status |
|----|------|--------|
| CON-01..07 | protocol + seam + settings | not started |

- [ ] CON-01 Define the Harbor wire protocol as typed envelope classes in `Armada.Core` (new
  `Core/Harbor/` namespace): a request/response/event envelope with correlation id, message type, and
  `traceparent`; command messages `Launch`, `Stdin`, `Kill`, `GitOp`, `WorktreeOp`, `Probe`; event
  messages `Started{jobId,pid}`, `Stdout`, `Stderr`, `Exited{code}`, `GitOpResult`, `Heartbeat{liveJobIds}`,
  `Error`. Typed classes only, no `JsonElement`. Acceptance: a round-trip serialize/deserialize
  Touchstone test for every message type, positive and negative (malformed rejected).
- [ ] CON-02 Define `IHostExecutor` in `Core/Services/Interfaces` covering process launch
  (launch/stdin/kill/liveness with streamed stdout/stderr), git operations (clone bare, fetch, worktree
  add/remove/prune, branch, rev-parse, commit, push), gh operations (PR create/merge/view), and
  filesystem worktree lifecycle. Return typed result classes, never tuples.
- [ ] CON-03 Split `BaseAgentRuntime` into a *plan* half (command resolution, argument building,
  environment, prompt-via-stdin, MCP config) and an *execute* half. The plan half stays in
  `Armada.Runtimes`; the execute half moves behind `IHostExecutor`. Acceptance: existing runtimes build
  and behave identically under `LocalHostExecutor` (Phase 2 verifies at runtime).
- [ ] CON-04 Define the handshake payload: Harbor id (`hbr_` prefix), advertised capabilities
  (installed runtimes, git/gh availability, OS/arch), protocol version, and the auth material carrier.
  Server replies with the advertised MCP base URL and any runtime settings the Harbor needs.
- [x] CON-05 Add the `hbr_` ID prefix to `Constants.cs` and a `GenerateHarborId()` helper alongside the
  existing generators.
- [~] CON-06 Define `HarborSettings` (backing fields, validation/clamping, defaults) and a
  `DeploymentModeEnum { Local, Split }` (or an `ExecutionMode` setting) on `ArmadaSettings`. Default
  `Local`. Include the link path, required-auth toggle, heartbeat interval, and job concurrency ceiling.
- [ ] CON-07 Document the protocol and seam in a short `docs/HARBOR_PROTOCOL.md` so both sides implement
  against one spec; keep it in sync as messages change.

---

## Phase 1 -- Server: persistence, connection, delegation

| ID | Task | Status |
|----|------|--------|
| SRV-01..20 | entity, auth, link, executor, APIs, telemetry | not started |
| RTR-01..06 | multi-Harbor routing and dock affinity | not started |

### Persistence (the Harbor entity)

- [x] SRV-01 `Core/Models/Harbor.cs`: id (`hbr_`), tenant id, owning user id, name, capabilities
  (typed child collection, not a JSON blob), connection status (`Connected|Disconnected|Degraded|Unknown`
  enum), last-seen UTC, last-connected UTC, protocol version, OS/arch, a max-concurrent-jobs capacity,
  an enabled flag, and created/last-update UTC. Typed columns per structured field.
- [x] SRV-02 `Core/Database/Interfaces/IHarborMethods.cs`: tenant-aware CRUD + enumerate + exists,
  each with `CancellationToken`; tenant-scoped overloads. No generic repository.
- [x] SRV-03 Implement `HarborMethods` for all four drivers (Sqlite, Postgresql, Mysql, SqlServer) with
  handwritten per-dialect SQL; capabilities in a child table. Expose `Harbors` on `DatabaseDriver` and
  register in each driver constructor. (Reuse the parallel-per-driver approach used for
  `model_endpoints`.)
- [x] SRV-04 Add schema migration **v62** ("Add harbors and harbor_capabilities tables") to all four
  `TableQueries.cs`, idempotent, tenant-scoped indexes, `(tenant_id, name)` uniqueness.
- [ ] SRV-04b Add schema migration **v63** for multi-Harbor routing/affinity to all four drivers:
  `docks.harbor_id` (which Harbor owns the dock), `vessels.preferred_harbor_id` and
  `vessels.required_capabilities` (optional routing hints), and `missions.assigned_harbor_id` (resolved at
  dispatch). Additive and idempotent.
- [~] SRV-05 Touchstone DB contract suite for Harbor across all four providers: migration applies, CRUD,
  tenant-scoped enumeration with no cross-tenant leakage, capability child rows, and the routing/affinity
  columns round-trip. Positive and negative.

### Authentication and authorization

- [ ] SRV-06 Harbor credential issuance: reuse the Credential entity; add an admin/tenant-admin flow to
  mint a Harbor credential (access key + secret, secret shown once, stored encrypted with per-token
  random IV, last-4 retained) with least-privilege `Execute` scope on the Harbor resource type. Redact
  in all responses.
- [ ] SRV-07 Static operation-scope map: every Harbor command message type maps to a
  `(ResourceType, Operation)` pair; dispatch/execute maps to `Execute`/`Write`; unclassifiable payloads
  require `Write`, never default to `Read`.
- [ ] SRV-08 Authenticate the WebSocket upgrade: read the credential/signed-request material from
  upgrade headers, resolve tenant, build the `RequestContext` on the HTTP context, normalize to the
  Credential principal, reject with a close frame on failure. Signed-request path enforces bounded clock
  skew and nonce uniqueness.
- [ ] SRV-09 Continuous authorization: re-validate session/credential/user/tenant active-state and
  revocation over the life of the link (periodic + per delegated command), and tear the link down when
  revoked or expired. Re-authorize each command against the credential's effective scope.
- [ ] SRV-10 Audit + counters: persist auth success/failure, session issue/refresh/revoke, credential
  use, and authorization denials for the link; emit the auth/authz observability counters.

### The Harbor link and delegation

- [ ] SRV-11 Enable WebSockets on the existing Watson server (same port, no second listener) and register
  the Harbor link path (e.g. `Server.WebSocket("/v1.0/harbor/connect", ...)`). Bind honoring
  `Rest.Hostname` (set `0.0.0.0` in split-mode config).
- [ ] SRV-12 Harbor connection manager (instance-owned by the server host, not a static global): tracks
  attached Harbors, handshake, capability registration, heartbeat/liveness, reconnection and rebind of
  in-flight jobs, and updates the Harbor entity's connection status. Honors cancellation; failures logged
  and swallowed so a bad link never crashes the host.
- [ ] SRV-13 `RemoteHostExecutor implements IHostExecutor`: marshals every host operation to the attached
  Harbor over the link, correlates responses, streams stdout/stderr back to the existing captain output
  path, and surfaces exit/errors. Job id from the Harbor replaces PID mapping.
- [ ] SRV-14 Dispatch gating for split mode: when no Harbor is attached, host-dependent work (launch,
  landing, merge queue, clone, live diff) queues with a clear "waiting for a runner" status instead of
  failing; resumes on attach. Read-only diff views use persisted snapshots.
- [ ] SRV-15 Composition: choose `LocalHostExecutor` vs `RemoteHostExecutor` at startup from
  `DeploymentMode`; wire the connection manager into the existing health/maintenance loop. `Program.cs`
  stays thin.
- [ ] SRV-16 Make MCP URL configurable: parameterize `ArmadaMcpConfigBuilder` host (not only port); the
  Admiral advertises its reachable MCP base URL at handshake; local mode keeps `127.0.0.1`.

### Routing and affinity (multiple Harbors)

- [ ] RTR-01 `IHarborRouter` + `HarborRouter`: given a mission/vessel and the set of attached, enabled,
  healthy Harbors, select one. Order of precedence: (1) an existing dock's owning Harbor if the mission
  already has a dock (hard affinity); (2) `mission.assigned_harbor_id` / `vessel.preferred_harbor_id` if
  set and eligible; (3) a Harbor that already holds the vessel's repo clone; (4) capability match against
  `vessel.required_capabilities` and the requested runtime; (5) least loaded by in-flight jobs under the
  Harbor's capacity. Returns a typed decision (chosen Harbor or a typed "no eligible Harbor" reason).
- [ ] RTR-02 Decide routing only at dock provisioning. Persist the chosen Harbor on `docks.harbor_id` and
  `missions.assigned_harbor_id`; after that, `RemoteHostExecutor` resolves the Harbor for every operation
  from the dock's `harbor_id` (affinity), never re-routes a live mission.
- [ ] RTR-03 `RemoteHostExecutor` is Harbor-scoped: operations carry a routing context (mission/dock), the
  executor resolves the owning Harbor via the router/affinity, and dispatches over that Harbor's link.
- [ ] RTR-04 Affinity failure handling: if the owning Harbor is disconnected, host operations for that
  mission queue with a "waiting for Harbor <id>" status and resume when it reconnects; new missions still
  route to other eligible Harbors. A capacity-full or capability-missing fleet yields a clear, surfaced
  reason rather than a silent stall.
- [ ] RTR-05 Capacity + load accounting: track in-flight jobs per Harbor; the router respects each
  Harbor's max-concurrent-jobs; expose per-Harbor load for the dashboard.
- [ ] RTR-06 Touchstone tests for routing precedence, dock affinity pinning, capability filtering,
  capacity limits, and disconnect-stall-then-resume. Positive and negative.

### REST + MCP surface

- [ ] SRV-17 `Server/Routes/HarborRoutes.cs`: list/enumerate Harbors (paged `EnumerationResult`), get by
  id, mint/rotate/revoke a Harbor credential, disconnect a Harbor, and read live status. Typed DTOs in
  `Core/Requests` and `Core/Responses`, per-route OpenAPI metadata, tenant scoping from `RequestContext`,
  typed `ErrorResponse`. Secrets returned once on mint only.
- [ ] SRV-18 MCP tools mirroring the read/manage surface (`list_harbors`, `get_harbor`,
  `disconnect_harbor`, `revoke_harbor_credential`) via `McpToolRegistrar` with typed arg classes; add
  `harbors` to the `enumerate` tool's entity types. No secret material through MCP.
- [ ] SRV-19 Server-side telemetry: meters + spans for link lifecycle, per-command delegation
  (integration pattern with `service`/`operation`/`outcome`), and the dispatch queue (with a `queued`
  stage), all product-prefixed and low-cardinality.
- [ ] SRV-20 Runtime settings management for `HarborSettings`: admin-gated read (secrets masked) and
  update (validate, clamp, persist, secrets only written when supplied), with fields annotated
  applied-live vs requires-restart; changes audited without secrets.

---

## Phase 2 -- Refactor existing execution onto the seam

Keep standalone identical while everything routes through the new interface.

| ID | Task | Status |
|----|------|--------|
| LOC-01..05 | LocalHostExecutor + callers | not started |

- [ ] LOC-01 Implement `LocalHostExecutor` by lifting the current in-process behavior: `Process.Start`
  launch/stdin/kill/liveness from `BaseAgentRuntime`, plus `GitService`/`DockService` calls, behind
  `IHostExecutor`. Windows `.cmd` resolution and stdin prompt delivery preserved.
- [ ] LOC-02 Route `AgentLifecycleHandler` (launch/stop/liveness, PID->captain mapping) through
  `IHostExecutor` instead of calling runtimes directly.
- [ ] LOC-03 Route `DockService`/`GitService` git and worktree operations through the executor seam so
  split mode can intercept them; local mode calls straight through.
- [ ] LOC-04 Route landing, merge queue, and diff capture through the seam.
- [ ] LOC-05 Regression: run the full existing Touchstone + dashboard suites and a manual standalone
  smoke (dispatch a real captain, land a branch) to prove local mode is unchanged. Acceptance: no
  behavioral diff versus pre-refactor.

---

## Phase 3 -- The Harbor app (`src/Armada.Harbor`)

| ID | Task | Status |
|----|------|--------|
| HBR-01..12 | app, client, executor, tray, packaging | not started |

- [ ] HBR-01 Create `src/Armada.Harbor` (Avalonia, `net8.0;net10.0`) referencing `Armada.Core` and
  `Armada.Runtimes`. Source under `src/` per repo rules.
- [ ] HBR-02 Harbor link client: dial the Admiral over WSS, perform the handshake (signed-request or
  access-key/secret over TLS), advertise capabilities, maintain heartbeat, auto-reconnect with backoff,
  and rebind in-flight jobs on reconnect. Client-initiated only.
- [ ] HBR-03 `HostExecutorServer`: receive delegated commands and execute them by reusing
  `Armada.Runtimes` (captain launch), `GitService`, and `DockService` on the host; stream stdout/stderr
  and lifecycle events back; multiplex many concurrent jobs keyed by Harbor-assigned job id.
- [ ] HBR-04 Inject the Admiral-advertised MCP URL into launched captains' MCP config; use the host's own
  environment and home so host logins (`~/.claude`, `~/.codex`) and provider keys resolve as they do
  today.
- [ ] HBR-05 Configuration: a Harbor settings file (server URL, credential/access key + secret via env
  or OS secret store, repos/docks directories, concurrency), `127.0.0.1` loopback default, secrets never
  written to logs.
- [ ] HBR-06 Kill/liveness ownership: track child PIDs on the host, honor graceful-stop-then-kill-tree,
  and report liveness via heartbeat.
- [ ] HBR-07 Telemetry: own `Meter` + `ActivitySource`, OTLP export to the same backends, continue the
  propagated trace as a child span per command, nested span for child-process execution, structured logs,
  best-effort (never blocks execution). Decide scrape-vs-push (push/OTLP for a host app) and record the
  choice; consult the Pneuma reference for exact wiring.
- [ ] HBR-08 Tray UI: status (connected/disconnected/degraded), attached server, running captains count,
  start/stop the runner, open the dashboard in the browser, and quit. Theme-aware.
- [ ] HBR-09 First-run/setup: prompt for server URL + credential, validate the connection, and persist.
- [ ] HBR-10 Graceful shutdown: on quit, stop accepting new jobs, optionally drain or cleanly kill
  running captains, and close the link.
- [ ] HBR-11 Touchstone tests for the Harbor executor and link client (connect/auth/reconnect, command
  round-trips, concurrent job multiplexing, kill/liveness), positive and negative, in `Test.Shared`.
- [ ] HBR-12 Packaging targets: self-contained single-file publish for win-x64, osx-arm64/x64, linux-x64
  (see Phase 7 scripts).

---

## Phase 4 -- Dashboard

| ID | Task | Status |
|----|------|--------|
| DASH-01..11 | types, client, page, status, i18n, tests | not started |

- [ ] DASH-01 Types in `src/types/models.ts`: `Harbor`, `HarborCapability`, `HarborConnectionStatus`
  (string-literal union), `HarborQuery`, credential mint/response DTOs.
- [ ] DASH-02 Client functions in `src/api/client.ts`: list/get Harbors, mint/rotate/revoke credential,
  disconnect. Reuse `EnumerationResult`, `buildQuery`.
- [ ] DASH-03 Harbors surface as a tab under the SYSTEM/Server hub (avoids a new top-level nav slot;
  matches Armada's hub-with-tabs consolidation). If a top-level item is chosen instead, update
  `navConfig.tsx`, `navIcons.tsx`, and the count assertion in `navConfig.test.ts`.
- [ ] DASH-04 Harbors table: `useResourceTable`, `Pagination`, refresh + auto-refresh, per-column
  filters, `StatusBadge` for connection status, `ActionMenu` (View, View JSON, mint/rotate credential,
  disconnect), copyable ids, empty/loading/filtered-empty/error states.
- [ ] DASH-05 Harbor detail/health modal: capabilities, connection status, last-seen (relative +
  absolute title), running jobs, OS/arch, protocol version; overlay + ESC + backdrop-close conventions.
- [ ] DASH-06 Credential mint modal: show the secret once with a `CopyButton` and a clear "you will not
  see this again" warning; destructive actions (revoke, disconnect) via `ConfirmDialog` with `btn-danger`.
- [ ] DASH-07 Live updates: subscribe via `WebSocketContext` so connection status and running-job counts
  update without a reload; auto-refresh as fallback.
- [ ] DASH-08 Deployment-mode indicator in the shell: show whether the Admiral is running Local or Split,
  and in Split mode which Harbor(s) are attached. Reuse/extend the existing proxy-context strip and
  top-bar status-dot family rather than inventing a parallel indicator. Show which Harbor is executing a
  given mission on the mission/captain views, and show per-Harbor load (in-flight vs capacity).
- [ ] DASH-08b Routing controls: let an operator set a vessel's preferred Harbor and required
  capabilities, and surface the resolved Harbor per mission. Show a clear state when a mission is stalled
  waiting for its owning Harbor to reconnect, or when no eligible Harbor exists.
- [ ] DASH-09 Onboarding: a setup panel with copyable install commands for the Harbor app per OS and the
  mint-credential flow, following `SetupWizard` conventions; `.mono` + `data-i18n-skip` on command blocks.
- [ ] DASH-10 i18n: wrap every new string in `t()`; add new phrases to the catalog source for
  `armada.json`; add `StatusBadge` tooltips and `.tag.<status>` colors for the new connection statuses;
  format times via `useLocale` helpers.
- [ ] DASH-11 Tests + build: page/table test, `navConfig.test.ts` updated if nav changes, responsive QA
  at 1280/768/390 in both themes, `npm run build` clean, `dist/` rebuilt and committed.

---

## Phase 5 -- Docker and deployment

| ID | Task | Status |
|----|------|--------|
| DKR-01..08 | images, compose, health, MCP exposure | not started |

- [ ] DKR-01 Server image: add `git` and `gh` and `curl` (needed for git-only operations that stay on
  the server in local-in-container edge cases, and for the healthcheck). Set `ENV HOME`/settings path so
  the mounted config is actually read (fixes the current `armada.json`-ignored gap), or add a settings
  path env override.
- [ ] DKR-02 Fix config loading: honor a settings path from env/arg (mirror `ARMADA_PROXY_SETTINGS_FILE`)
  so the compose-mounted config applies and `rest.hostname: 0.0.0.0` takes effect.
- [ ] DKR-03 Publish and document the MCP port so a host Harbor reaches the container's MCP endpoint; the
  advertised MCP URL (SRV-16) resolves from the Harbor host.
- [ ] DKR-04 Split-mode compose profile: Admiral + dashboard (+ optional Postgres + observability),
  server bound `0.0.0.0`, link + MCP ports published, no agent CLIs baked in (they live on the Harbor).
- [ ] DKR-05 Healthchecks on every HTTP service: curl `127.0.0.1` health path, `interval: 5s`,
  `retries: 2`, timeout <= interval; `depends_on` with `condition: service_healthy` (DB and observability
  before the Admiral).
- [ ] DKR-06 `docker/update.bat` helper (pull, down, up detached, `docker ps -a`); keep the destructive
  `docker/factory/reset` helper working.
- [ ] DKR-07 `.dockerignore` and `.yaml` (not `.yml`) with build contexts; pin observability images.
- [ ] DKR-08 Standalone stays first-class: document that no Docker is required for local mode; the
  compose stack is only for the remote-server shape.

---

## Phase 6 -- Documentation

| ID | Task | Status |
|----|------|--------|
| DOC-01..08 | README, CHANGELOG, API docs, deploy guide | not started |

- [ ] DOC-01 `README.md`: add the Harbor runner and the two deployment modes; keep the "zero-install
  standalone" promise accurate; add a "server in Docker + host Harbor" quickstart.
- [ ] DOC-02 `CHANGELOG.md`: new 0.10.0 entry covering the runner, the entity/migration v62, the new
  APIs, and the Harbor app.
- [ ] DOC-03 `docs/REST_API.md`: document every new Harbor route (method, path, params, request/response
  bodies, status codes, auth, examples), including the once-only secret on mint.
- [ ] DOC-04 `docs/MCP_API.md`: document the new Harbor tools and the `harbors` enumerate type.
- [ ] DOC-05 Postman collection under `assets/postman/`: a "Harbors" folder with documented requests,
  variables for base URL/port/token, in sync with REST_API.
- [ ] DOC-06 `DOCKERHUB_README.md`: reflect the split-mode deployment and the Harbor requirement, with
  explicit asset image URLs.
- [ ] DOC-07 `docs/HARBOR.md`: install and run the Harbor app, mint and configure a credential, auth and
  TLS expectations, troubleshooting the link, and the local-vs-split decision guide.
- [ ] DOC-08 `docs/HARBOR_PROTOCOL.md` (from CON-07) kept current with the shipped message set.

---

## Phase 7 -- Scripts

| ID | Task | Status |
|----|------|--------|
| SCR-01..07 | build, install, publish, run-on-startup | not started |

- [ ] SCR-01 `scripts/common/build-harbor.sh` and the win/linux/macos wrappers to build the Harbor app.
- [ ] SCR-02 `scripts/common/publish-harbor.sh`: self-contained single-file publish per RID
  (win-x64, osx-arm64, osx-x64, linux-x64).
- [ ] SCR-03 Extend `scripts/common/install.sh` (and the platform installers) to optionally install the
  Harbor app and register it to run at login.
- [ ] SCR-04 Run-on-startup for the Harbor: systemd user unit (Linux), launchd agent (macOS), scheduled
  task (Windows), mirroring the Admiral's existing approach in `docs/RUN_ON_STARTUP.md`.
- [ ] SCR-05 Docker build/push scripts for the split-mode images (multi-arch), consistent with existing
  server/dashboard/proxy build scripts.
- [ ] SCR-06 Verify the CLI (`Armada.Helm`) story against a remote Admiral: add a base-URL/token option
  or document that the CLI targets a local Admiral only; do not silently break `127.0.0.1` assumptions.
- [ ] SCR-07 Update `install`/uninstall docs and any `.gitignore` entries for Harbor build output.

---

## Phase 8 -- Tests and acceptance

| ID | Task | Status |
|----|------|--------|
| TST-01..07 | suites, E2E, security, perf | not started |

- [ ] TST-01 Backend Touchstone coverage: Harbor model (validation/ID), Harbor service, four-provider DB
  contract, routes + auth, and the wire-protocol round-trips. Positive and negative for each; discovered
  via `Suites.All`.
- [ ] TST-02 Link/executor integration: connect + authenticate, reject bad/expired/revoked credentials,
  reconnect and rebind in-flight jobs, concurrent job multiplexing, kill/liveness, stdout streaming and
  backpressure.
- [ ] TST-03 Split-mode E2E: Admiral in a container + a local Harbor executes a real captain end to end,
  including MCP callback to the published port and a git land through the Harbor.
- [ ] TST-04 Standalone E2E regression: local mode dispatch + land unchanged (LOC-05 extended into CI).
- [ ] TST-05 Security tests: no secret in logs/labels/metrics; owner-ceiling and least-privilege enforced;
  authorization denials audited; tenant isolation on every Harbor-triggered query; TLS required for
  secret-bearing schemes.
- [ ] TST-06 Dashboard tests: Harbors page/table behavior, live status, i18n coverage (no hardcoded
  strings), responsive/theme QA.
- [ ] TST-07 Graceful-degradation tests: split mode with no Harbor attached queues host-dependent work
  and resumes on attach; read-only diffs still render from snapshots.
- [ ] TST-08 Multi-Harbor E2E: two Harbors attached with different capabilities; missions route by
  preference/capability/load; dock affinity pins a running mission to its Harbor; disconnecting the owning
  Harbor stalls only that mission while others keep running; reconnect resumes it; landing runs on the
  owning Harbor.

---

## Definition of done (release gate)

The release ships when standalone mode is provably unchanged, split mode runs a real mission end to end
with the Admiral containerized and the captain executing on the host under the host's own login, and the
whole thing is observable, documented, and covered by tests. Concretely: `dotnet build src/Armada.sln`
and `npm run build` are clean; all Touchstone suites (console, xUnit, NUnit) pass across all four DB
providers; REST_API, MCP_API, the Postman collection, README, CHANGELOG, and DOCKERHUB_README match the
shipped surface; the Harbor app installs and runs on Windows, macOS, and Linux; secrets never appear in
logs or responses; and a trace for one mission spans Admiral, Harbor, and the captain process in one
waterfall.

## Risks

The largest technical risk is reconnection semantics: a link that drops while captains are mid-run must
not orphan work or double-count liveness, which is why the Harbor owns the job id and re-advertises live
jobs on reconnect (SRV-12, HBR-02). The second is the trust boundary -- a Harbor executes whatever the
Admiral sends, so the credential scoping, TLS, and continuous re-authorization in Phase 1 are not
optional polish; they are the feature's safety model.

Multi-Harbor adds a third: dock affinity is a hard physical constraint, not a preference. A worktree
lives on exactly one machine, so a mission cannot fail over to another Harbor once its dock exists.
The router therefore commits the routing decision at dock-provisioning time and pins everything after
(RTR-02), and a disconnected owning Harbor stalls only its own missions while the rest of the fleet keeps
working (RTR-04). Getting that isolation right -- one Harbor going dark must not stall the whole system --
is the multi-host acceptance bar (TST-08).
