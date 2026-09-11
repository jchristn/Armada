# Rebuild / Restart the Admiral from the Dashboard

> **Type:** implementation plan (work-tracking). Annotate task status and the
> progress log as you go; keep this doc in sync with what actually shipped.
>
> **Status:** Phase 1 + Phase 2 implemented (compiles; Harbor cutover path needs live verification)
> **Owner:** _unassigned_
> **Target deployment:** native single-box Windows (self-contained Admiral + on-box Harbor)
> **Last updated:** 2026-09-11

### Implementation notes / deviations from the original plan

- `ServerRebuildService` lives in `Armada.Server` (not `Armada.Core`) so it can reuse the Server-side
  `McpToolHelpers.PerformBackupAsync`/`PerformRestoreAsync`; `SlotManager` stays in `Armada.Core` (pure,
  unit-tested).
- The build streams into a persisted `ServerRebuildStatus` (`<dataDir>/rebuild-status.json`) polled via
  `GET /api/v1/server/rebuild/status`, rather than reusing the CheckRun WebSocket channel. The dashboard tails
  it with the existing `LogViewer` (poll-based).
- The dashboard build is **best-effort and non-fatal**: only a failed *server* publish blocks cutover. The
  dashboard is rebuilt from the live working tree into the shared `<dataDir>/dashboard` (served by whichever
  slot is active), not per-slot, so a non-HEAD ref still ships the working-tree dashboard.
- The build-ref control is a branch **dropdown** (populated from `getVesselBranches` for the self vessel,
  default first option = current HEAD) alongside a text box for an arbitrary tag/commit; both bind the same
  ref.
- Harbor supervision is opt-in via `settings.RebuildSupervisorHarborId`; when set and connected the cutover is
  delegated for health-gated rollback, otherwise the in-process baton runs. The Harbor-side handler
  (launch + health poll + rollback) compiles and has protocol round-trip tests, but the live cutover path has
  not been exercised against a running Harbor.
- Launcher slot-awareness needed only `start-armada-server.ps1` (the HKCU Run key already invokes it);
  `install-windows-task.bat` was left unchanged.

Status values used throughout: `[ ]` not started, `[~]` in progress, `[x]` done,
`[!]` blocked. Put a one-line note under any task you touch, and add a dated row
to the Progress Log at the bottom.

## Goal

Give the operator a "Rebuild Armada" button (and a plain "Restart") in the
dashboard. After missions land code changes into the local source tree, one click
rebuilds the Admiral from the freshly-landed source and cuts over to the new
binary with near-zero downtime, rolling back automatically if the new build fails
to come up.

Scope is the **native, single-box Windows deployment**: the Admiral runs
self-contained at `%USERPROFILE%\.armada\bin\...`, a Harbor runs on the same box
connected over the link, the Armada source repo IS the vessel being rebuilt, and
everything is driven from the dashboard. Docker / container redeploy is out of
scope.

## Non-goals

Crash recovery for an unplanned Admiral death stays out of scope -- that belongs
to the OS (Task Scheduler / Run-key relaunch / `Restart=on-failure`), and folding
it in here would distort the design. Every flow below is a *planned* operation, so
the Admiral is always alive to hand off an instruction before it exits. Harbor is
never made a supervisor or a parent of the Admiral; it stays a peer.

## The core primitive: a deferred, one-shot relaunch

The only hard part of a process restarting itself is that whatever relaunches the
server cannot be the server -- it is gone by then. Armada already solves this for
the same-bits case. `POST /api/v1/server/restart`
(`src/Armada.Server/Routes/StatusRoutes.cs:320`) calls `LaunchReplacementProcess()`
(`StatusRoutes.cs:483`), which starts a detached child from `Environment.ProcessPath`,
sets `ARMADA_RESTART_WAIT_PID = Environment.ProcessId`, then stops the current
instance. The child blocks in `Program.WaitForPredecessorExitAsync()`
(`src/Armada.Server/Program.cs:66,112`) until the old PID exits and the port frees,
then binds.

That baton -- a detached child that outlives its parent, gated on the old PID -- is
the whole trick. The rest of this plan does two things to it: point it at *new
bits*, and add a peer that can perform the *health-gated rollback* a dead Admiral
cannot perform itself.

## Deployment layout: A/B slots

Publishing over the running exe is impossible on Windows -- it is file-locked,
which is exactly why `scripts/windows/update-windows-task.bat` stops the server
before republishing and eats full-build downtime. Publish into a **new versioned
slot** while the old server keeps running instead:

```
%USERPROFILE%\.armada\bin\
   slots\
      2026-09-11_a1b2c3d\      <- new publish target (no lock conflict)
         Armada.Server.exe
         wwwroot\ (dashboard)
      2026-09-08_f9e8d7c\      <- previous slot, retained for rollback
   current                     <- pointer file: contains the active slot name
```

Slot names are `<yyyy-MM-dd>_<short-git-sha>` of the built source HEAD. `current`
is a plain text file (not a junction, to avoid privilege and reparse-point
quirks); both the launcher and the rebuild flow read and write it. Retain the last
N slots (default 3, configurable) and prune older ones only after a successful
cutover.

Because the build target differs from the running exe's directory, the build runs
with the Admiral still up and serving the dashboard, so downtime collapses to the
few seconds of the port handoff.

## Where Harbor fits (and where it does not)

Harbor earns its place at exactly one point: the rollback watchdog. The Admiral can
build, flip the pointer, and even launch the replacement itself -- but it cannot
notice that the *new* slot failed to boot and bring the *old* one back, because by
then it is dead and the broken new process is the one that was meant to take over.
Harbor holds a deferred, health-gated relaunch instruction for that window.

Building into the new slot is a separate, optional Harbor use: `dotnet publish` can
run in-process on the Admiral (backgrounded) or be dispatched to the on-box Harbor
via `IHostCommandExecutor` / `RemoteHostCommandExecutor`, selected the way
`CaptainRuntimeToolCatalogService` already picks its executor by `HarborId`. Nice
for keeping build load off the Admiral, not load-bearing for correctness. Skip the
rollback and you can skip Harbor entirely -- the built-in baton pointed at the new
slot delivers the whole button.

## Flows

### Restart (same bits)

The existing behavior, with one change: target the `current` slot's exe rather than
`Environment.ProcessPath` (identical today, but slot-aware for the future).

1. Dashboard -> `POST /api/v1/server/restart`.
2. Admiral launches a detached child from the `current` slot exe with
   `ARMADA_RESTART_WAIT_PID = <own pid>`, then stops.
3. Child waits for the old PID, binds. No Harbor involved.

### Rebuild (new bits, Harbor-assisted rollback)

```
Dashboard "Rebuild Armada"
        |
        v
POST /api/v1/server/rebuild   (returns immediately with a rebuild id)
        |
        |-- 1. Resolve SelfVesselId -> vessel LocalPath; git worktree add a throwaway
        |         detached worktree at the operator-chosen ref (default main HEAD);
        |         snapshot its sha; compute new slot name.
        |-- 2. Auto-backup DB (reuse McpBackupTools online backup); record backup path.
        |-- 3. Build from the detached worktree: dotnet publish server + vite build
        |         dashboard -> slots\<new>\  (in-process bg job OR dispatched to Harbor);
        |         stream build log to dashboard (reuse CheckRun log streaming);
        |         remove the throwaway worktree when the build finishes.
        |
        |   [build fails] -> leave `current` untouched, surface errors, DO NOT restart. STOP.
        |
        |-- 4. Flip `current` -> new slot (atomic write).
        |-- 5. Send Harbor the deferred relaunch instruction (protocol below),
        |         while the Admiral is still alive and the link is up. Await ACK.
        |-- 6. Admiral stops (frees port + DB + file locks).
        v
Harbor executes the armed instruction:
        |-- launch slots\<new>\Armada.Server.exe with ARMADA_RESTART_WAIT_PID=<old pid>
        |-- poll GET /api/v1/status/health (pattern: ServerRestartCommand.cs:40-55)
        |-- healthy within timeout   -> done; prune slots beyond retention.
        |-- NOT healthy within timeout:
              |-- rewrite `current` -> previous slot
              |-- launch slots\<prev>\Armada.Server.exe
              |-- record a "rebuild rolled back" event for the dashboard.
```

The `ARMADA_RESTART_WAIT_PID` handshake already stops the new process from binding
until the old one is gone, so Harbor can fire the launch as soon as it receives the
instruction. Any "wait N seconds" is a coarse floor, not the mechanism preventing a
double-bind -- do not rely on a fixed sleep for correctness.

---

## Tasks

### Phase 1 -- MVP (no Harbor, no auto-rollback)

Ships a working button: build to a new slot, flip the pointer, restart via the
existing baton, with an auto DB backup and live build log. A failed *boot* (rarer
than a failed build, which is already safe) needs a manual relaunch of the previous
slot until Phase 2 lands.

- [x] **T1 -- Slot layout + `current` pointer helper.**
  New `SlotManager` (`src/Armada.Core/Services/`, interface `ISlotManager` in
  `.../Interfaces/`): resolve slot dir, read/write `current` atomically, enumerate
  and prune slots to the retention count. Retention count is a configurable public
  member with a backing field defaulting to 3 (not a constant). Include an async
  variant with `CancellationToken` for any `IEnumerable`-returning method.
  _Acceptance:_ unit test proves atomic pointer swap and prune-keeps-newest-N.
  _Notes:_

- [x] **T2 -- `ServerRebuildRequest` model.**
  One class per file in `src/Armada.Server/`. Fields (all optional): `SourcePath`
  (override; when null, resolve via `SelfVesselId` -> vessel `LocalPath`, see T13),
  `Ref` (branch/commit to build; defaults to `main` HEAD), `SkipDashboard`,
  `RollbackTimeoutSeconds` (clamp on set to a sane range, e.g. 10-600). Public
  properties with backing fields and range/null validation in the setters; XML docs
  stating defaults/min/max.
  _Acceptance:_ deserializes from the route body; out-of-range timeout clamps.
  _Notes:_

- [x] **T13 -- `SelfVesselId` designation.**
  Add `ArmadaSettings.SelfVesselId` (nullable, `vsl_` prefix, default null) plus a
  dashboard field to set it (Server/Settings page). Rebuild resolves it to the
  Armada vessel's `LocalPath`; throw a specific, clear error if unset or if the
  referenced vessel has no `LocalPath`. Chosen over an `IsSelf` flag on `Vessel` so
  two vessels can never both claim to be Armada. Adding a settings field means the
  settings REST surface and REST_API.md must reflect it (see Compliance).
  _Acceptance:_ unset -> actionable error; set -> rebuild resolves the right path.
  _Notes:_

- [x] **T3 -- `IServerRebuildService` + `ServerRebuildService`.**
  `src/Armada.Core/Services/` (+ interface in `.../Interfaces/`). Resolves the
  vessel `LocalPath` (T13), runs `git worktree add --detach <tmp> <ref>` at the
  chosen ref so the operator's working tree and any captain activity in `LocalPath`
  are never disturbed, snapshots the sha, then runs
  `dotnet publish src/Armada.Server -c Release -o slots\<new>` and the Vite
  dashboard build from that worktree via `IHostCommandExecutor` (Local for MVP);
  streams output to the existing CheckRun log channel; removes the throwaway
  worktree in a `finally`; on success writes `current` (via `ISlotManager`) then
  hands back to the caller to trigger the restart; on failure marks the rebuild
  `Failed` and leaves `current` untouched. All async methods take `CancellationToken`
  and check it around the long publish step. Throw specific exceptions (e.g. a
  domain `RebuildException`) with contextual messages, not bare `Exception`. No
  `Console.WriteLine` -- use the injected logger.
  _Acceptance:_ build-failure path never flips `current` and never stops the server;
  the operator's `LocalPath` checkout is unchanged after a rebuild; the throwaway
  worktree is removed even on failure.
  _Notes:_

- [x] **T4 -- `POST /api/v1/server/rebuild` route.**
  Add to `src/Armada.Server/Routes/StatusRoutes.cs` beside `server/restart`. Same
  auth guard (`RequireAuthForShutdown` -> `authz.IsAuthorized`). Returns
  `{ RebuildId, Slot, Sha, Status = "building" }` immediately; the build runs as a
  tracked background job so the request never blocks on `dotnet publish`. On build
  success, reuse the baton (T5) pointed at the new slot.
  _Acceptance:_ returns promptly; unauthorized returns 401/403 like `server/restart`.
  _Notes:_

- [x] **T5 -- Slot-aware baton.**
  Generalize `LaunchReplacementProcess()` (`StatusRoutes.cs:483`) to launch a
  caller-supplied slot exe (default: `current`) instead of only
  `Environment.ProcessPath`. `server/restart` now targets `current`.
  _Acceptance:_ restart still works with a single slot; rebuild launches the new slot.
  _Notes:_

- [x] **T6 -- Auto DB backup before cutover.**
  Call the existing `McpBackupTools` / SQLite online-backup path at rebuild step 2;
  record the backup path on the rebuild record.
  _Acceptance:_ a backup file exists before any cutover is attempted.
  _Notes:_

- [x] **T7 -- Launcher `current`-awareness.**
  `scripts/windows/start-armada-server.ps1` and `install-windows-task.bat` launch
  the exe named by `current` (`slots\<name>\Armada.Server.exe`), keeping the
  existing duplicate-instance guard (match by `ExecutablePath`).
  _Acceptance:_ a fresh reboot comes up on the slot named by `current`.
  _Notes:_

- [x] **T8 -- Dashboard button + progress panel.**
  `client.ts`: `rebuildServer(body?)` beside `restartServer` (~line 1030).
  `Server.tsx`: "Rebuild Armada" button beside "Restart Server" (~line 1300),
  confirm -> call -> toast -> health-poll pattern, `disabled={remoteProxyMode}`.
  Confirm dialog includes a **build-ref picker** (default `main`, plus other
  branches / paste-a-sha; drives `ServerRebuildRequest.Ref`) and shows the resolved
  sha to be built; optionally warn on a dirty tree or non-empty merge queue. A log
  panel tails the rebuild log and shows `rebuilding -> cutting over -> up on <sha>`.
  Follow FRONTEND_ARCHITECTURE and DASHBOARD_STYLE_AND_USABILITY; route all strings
  through i18n (see I18N.md), no hard-coded copy.
  _Acceptance:_ ref picker defaults to main and drives the build; button gated in
  remote-proxy mode; resolved sha visible pre-confirm; strings localized.
  _Notes:_

### Phase 2 -- Harbor-assisted health-gated rollback

- [x] **T9 -- `HarborDeferredLaunchRequest` protocol message.**
  Add to `src/Armada.Core/Harbor/` (one class per file) and document in
  `docs/HARBOR_PROTOCOL.md`. Fields: `LaunchExePath`, `WaitForPid`,
  `WorkingDirectory`, `HealthUrl`, `HealthTimeoutSeconds`, `FallbackExePath`,
  `FallbackSlot`, `CurrentPointerPath`. It is a fire-before-death, one-shot
  instruction the Admiral does not block on beyond the ACK.
  _Acceptance:_ round-trips through the protocol; documented in HARBOR_PROTOCOL.md.
  _Notes:_

- [x] **T10 -- Harbor-side handler.**
  On receipt: ACK immediately (so the Admiral knows it is armed before exiting);
  launch `LaunchExePath` with `ARMADA_RESTART_WAIT_PID = WaitForPid`; poll
  `HealthUrl` up to `HealthTimeoutSeconds`; on healthy report success over the link
  (the new Admiral is up, so the link is back); on timeout rewrite
  `CurrentPointerPath` to `FallbackSlot`, launch `FallbackExePath`, and report
  rollback. Because the instruction is ACKed while the Admiral lives, the link being
  dead during the cutover is irrelevant. Async, `CancellationToken`, specific
  exceptions, logger not `Console`.
  _Acceptance:_ a deliberately-broken new slot triggers rollback to the previous slot.
  _Notes:_

- [x] **T11 -- Wire rebuild step 5 to the handoff; optional Harbor build.**
  Rebuild sends `HarborDeferredLaunchRequest` when a supervising on-box Harbor is
  connected; otherwise fall back to the Phase 1 baton. Optionally dispatch the T3
  build through `RemoteHostCommandExecutor` when a Harbor is present.
  _Acceptance:_ rebuild works with Harbor (auto-rollback) and without (baton fallback).
  _Notes:_

- [x] **T14 -- Operator-initiated rollback (post-successful-boot).**
  Distinct from the automatic health-gate in T10 (which covers the crash-loop case,
  where the new slot never migrated and a bare relaunch of the old slot is safe).
  This is a dashboard "Roll back to previous slot" action taken *after* the new
  build booted cleanly and may already have migrated the DB (e.g. v69 -> v70). It
  restores the pre-rebuild DB backup recorded in T6, flips `current` to the previous
  slot, and relaunches it. Because the restore discards everything written on the new
  build since cutover (missions, signals, merge activity), the UI must warn loudly,
  name the backup and the cutover time, and require an explicit confirm. Gate this
  action so it only offers the DB-restoring path when a migration actually occurred;
  if the schema version is unchanged, fall back to a plain slot relaunch with no
  restore.
  _Acceptance:_ post-migration rollback restores the backup + old slot behind a loud
  confirm; same-schema rollback skips the restore; data-loss window is stated in the UI.
  _Notes:_

### Optional

- [ ] **T12 -- `rebuild_server` MCP tool.** (deferred -- not implemented)
  Only if parity with the `stop_server` MCP tool is wanted. If added, it obligates
  an `MCP_API.md` update (see Compliance). REST-only for now.
  _Notes:_ Deliberately skipped; the feature is REST + dashboard only.

## Compliance checklist (per c:\code\agents\requirements)

- [x] **REST_API.md** updated for `POST /api/v1/server/rebuild`,
  `GET /api/v1/server/rebuild/status`, and `POST /api/v1/server/rollback`, plus the
  new settings fields (`SelfVesselId`, `RebuildSlotRetentionCount`,
  `RebuildSupervisorHarborId`). Postman collection updated (Rebuild Armada, Rebuild
  Status, Rollback Rebuild requests + settings example).
- [ ] **MCP_API.md** -- not applicable; T12 (the MCP tool) was deliberately skipped.
- [x] **HARBOR_PROTOCOL.md** updated for `HarborDeferredLaunchRequest` /
  `HarborDeferredLaunchAck` (T9).
- [x] **CODE_STYLE.md** conformance across all new C#: usings inside the namespace and
  ordered; XML docs on public members only; `_PascalCase` private fields; no `var`; no
  tuples; configurable values as members with backing fields; `.ConfigureAwait(false)`
  and `CancellationToken` on async; a specific exception type (`RebuildException`) with
  `<exception>` tags; guard clauses; one class/enum per file; no `Console.WriteLine` in
  library code. Verified by a clean 0-warning build.
- [x] **Tests** added per BACKEND_TEST_ARCHITECTURE -- `SlotManagerSuite` (pointer
  swap, prune-keeps-active, enumerate) and `HarborDeferredLaunchProtocolSuite` (message
  round-trips); **6/6 passing** (`dotnet run --project src/Test.Automated`, suite
  filter `Services.SlotManager,Services.HarborDeferredLaunchProtocol`). A
  build-failure and a live Harbor-rollback E2E remain to add.
- [x] **i18n / dashboard style** for T8: all new strings route through `t()`, no
  hard-coded copy; typechecks clean (`tsc --noEmit`).
- [x] **README/CHANGELOG** -- README "Rebuilding Armada from the Dashboard" section
  and a CHANGELOG "Self-rebuild" entry under Unreleased.

## Decisions (resolved 2026-09-11)

- **Canonical source path.** Build from the **Armada vessel's `LocalPath`**,
  designated by a new `ArmadaSettings.SelfVesselId` (T13) rather than an `IsSelf`
  flag on `Vessel`. Resolves T2/T3.
- **Trigger point.** The operator **picks the ref at click** (T8 ref picker,
  default `main` HEAD; branch or pasted sha allowed). To build an arbitrary ref
  without touching the operator's checkout or in-flight captain work, the build runs
  from a **throwaway detached `git worktree`** at that ref (T3), not an in-place
  `git checkout`.
- **Post-migration rollback.** **Auto-restore the pre-rebuild DB backup** and
  relaunch the old slot (T14), behind a loud warning and explicit confirm, accepting
  that data written on the new build since cutover is discarded. The automatic
  health-gated rollback (T10) still covers the crash-loop case with no restore
  needed.

## Progress Log

Append a dated row whenever you advance a task. Keep newest at the bottom.

| Date | Author | Task(s) | Change |
|------|--------|---------|--------|
| 2026-09-11 | (design) | -- | Initial plan drafted. |
| 2026-09-11 | (design) | T2,T3,T8,T13,T14 | Resolved the three open questions: source = Armada vessel LocalPath via SelfVesselId; operator-picked ref built from a detached worktree; auto-restore DB backup on post-migration rollback. Added T13, T14. |
| 2026-09-11 | (impl) | T8 | Branch dropdown (getVesselBranches) added to the rebuild ref control; Postman collection updated with the 3 server endpoints + settings fields; ran the new suites with the server stopped -- 6/6 passing; server relaunched. |
| 2026-09-11 | (impl) | T1-T14 | Implemented Phase 1 + Phase 2 end to end. Backend (SlotManager, ServerRebuildService, ReplacementProcessLauncher, rebuild/status/rollback routes, SelfVesselId + slot settings), Harbor deferred-launch protocol + handler + admiral delegation, slot-aware start script, dashboard button/ref input/LogViewer/rollback + self-vessel settings, SlotManager + Harbor-protocol test suites, REST_API.md + HARBOR_PROTOCOL.md. Core/Server/Harbor/Test.Shared build clean (0 warnings); dashboard tsc clean. Harbor cutover path not yet live-verified; T12 (MCP tool) skipped; Postman + build-failure/rollback E2E tests pending. |
