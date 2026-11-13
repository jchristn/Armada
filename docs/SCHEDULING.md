# Mission Scheduling

This document explains how Armada decides which mission to assign to which captain and in what order.

## Vessel admission limits

`MaxParallelVoyages` limits admitted voyage groups on a vessel (new vessel default 1). `MaxParallelMissionsPerVoyage` limits Assigned/InProgress missions within each group (default 4). Zero removes that cap; negative inputs become 1 and values above 1000 become 1000. Each independent standalone mission counts as a group; its dependent pipeline stages share the group.

A voyage retains admission through verification, review and landing and between its stages, until terminal or all its missions settle. Failed landings and blocked work alone do not retain admission. Post-agent states do not consume a running mission slot. Lowering limits prevents new admissions and leaves existing work running. Global, runtime, endpoint, eligibility, broad-scope and branch-safety gates still apply. Vessels upgraded from v1.2 get 1/1 or 0/0 from the removed `AllowConcurrentMissions` flag (false or true), so they schedule as before; see [UPGRADING.md](UPGRADING.md).

Vessel admission checks serialize per vessel, followed by a shared capacity lock protecting cross-vessel global/runtime/endpoint/captain checks and claims. Locks are always acquired in that order, both waits use `Concurrency.AdmissionLockTimeoutSeconds`, and timed-out requests retry on the next cycle. Idle vessel-lock entries are removed. The check/claim boundary releases both locks before worktree provisioning or agent launch. Admission reads only scheduling metadata, not historical outputs or diffs. Each captain admits up to `MaxConcurrency` missions, bounded by the configured server ceiling. Defaults are 1 for CLI runtimes and 4 for ApiEndpoint/Mux. Each mission holds its own dock and process; cancellation, completion or recovery affects that mission. Planning/refinement/analysis remain exclusive. A mission in verification, review or landing no longer holds a captain slot; its recovery record is kept so a restart can still finish the check. See [end-to-end testing](CONCURRENCY_TESTING.md).

## Capacity

A mission runs only when every limit between it and a captain has room. The number of missions that can run at once is the smallest of:

| Limit | Where it is set | Default | Counts |
|---|---|---|---|
| Voyage slots | Vessel `MaxParallelVoyages` | 1 | Voyages (or standalone missions) admitted on the vessel |
| Missions per voyage | Vessel `MaxParallelMissionsPerVoyage` | 4 | Assigned and InProgress missions of one voyage |
| Captain slots | Captain `MaxConcurrency` (capped by `Concurrency.MaxCaptainConcurrency`, 256) | 1 for CLI runtimes, 4 for ApiEndpoint and Mux | Running missions on that captain |
| Endpoint cap | Model endpoint inference options `MaxConcurrentMissions` | unset (unlimited) | Running missions on every captain that uses the endpoint |
| Runtime cap | `Agents` runtime `MaxConcurrent` | 0 (unlimited) | Running missions of that runtime |
| Global cap | `MaxConcurrentMissions` | 0 (unlimited) | Running missions in the deployment |

Zero means unlimited for every limit except captain slots, which are at least 1. The endpoint, runtime and global caps count missions, not captains, so one endpoint captain with four slots uses four units of an endpoint cap.

**Worked example.** A vessel at the defaults (1 voyage, 4 missions per voyage) runs one voyage with up to four missions at a time. Four Claude Code workers need four Claude Code captains, because a CLI captain runs one harness process and so one mission. One ApiEndpoint or Mux captain at `MaxConcurrency` 4 covers the same four missions. With two Claude Code captains the voyage runs two missions at a time and the others wait with `CaptainConcurrencyLimit`.

**Pipelines.** Each running stage holds a captain slot while its agent runs. Stages after the agent (verification, review, landing) hold the voyage slot but no captain slot and no per-voyage mission slot.

**Hierarchy.** A child mission starts after its parent's last stage finishes (`ParentNotFinished` until then). Siblings at the same level run in parallel within the per-voyage limit. See [PIPELINES.md](PIPELINES.md#mission-hierarchy).

**Gate order.** Admission checks a Pending mission in this order and reports the first gate that holds it: launch backoff and voyage state, parent, dependency, integration barrier, broad scope, vessel voyage slot, per-voyage mission slot, branch in use, global cap, captain selection (eligible captains with a free slot, then the runtime and endpoint caps), and placement. Structural gates come first, so a mission waiting on its parent never reports a capacity reason. Among captains with free slots, the requested captain wins, then routing preferences, then the captain with the fewest active missions, so work spreads across captains.

**Reading the advice.** Every capacity blocker carries advice built from the numbers that held the mission: the holding voyage and what it waits for, and the smallest change that admits this mission (for example "Raise Max parallel voyages on Armada from 1 to 2"). For CLI captains the advice is to add a captain, never to raise `MaxConcurrency`. Callers who cannot change the setting see the text without the action.

**Live view.** `GET /api/v1/vessels/{id}/concurrency` (MCP `vessel_concurrency`) returns the slots in use, the holding voyages, waiting missions and slot wait over 24 hours and 7 days; `GET /api/v1/voyages/{id}/tree` (MCP `voyage_tree`) returns the voyage's logical missions. The dashboard, TUI and Mobile show both on the vessel and voyage pages.

## Ordered captain priorities

Personas can define `captainPriorities`, an ordered list of runtime/model/endpoint/minimum-tier matchers. Every supplied field in an entry must match. `priorityAvailability` chooses `FallThrough` or `WaitForFirstPriority`; `priorityExhaustion` chooses `Wait` or `UseEligibleCaptain`. The first-priority wait policy never uses lower entries or general fallback. Empty lists retain legacy default-captain behavior.

A nonempty hierarchy replaces the persona default captain; explicit dispatch/voyage captain choices retain precedence. Routing is evaluated for every stage and Architect-generated mission, without binding a persona role to a provider. The hierarchy selects configured captains and does not change their model or endpoint. See [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md) for matching, precedence, configuration, and examples.


## Priority

Every mission has a **priority** field -- an integer that defaults to **100**. Lower numbers mean higher priority. When multiple missions are in the `Pending` state, the Admiral picks the one with the lowest priority number first.

| Priority | Typical Use |
|----------|-------------|
| 1-10 | Urgent / jump the queue |
| 50 | High importance |
| 100 | Default |
| 200+ | Low importance / background work |

## FIFO Within Same Priority

When multiple pending missions share the same priority level, they are assigned in **creation order** -- first in, first out. The mission that was created earliest is assigned first. Voyage membership does not affect ordering: a voyage mission and a standalone mission at the same priority are ordered purely by creation time.

### Example

Consider the following pending missions, created in this order:

| Mission | Priority | Voyage |
|---------|----------|--------|
| msn_A | 100 | vyg_sprint1 |
| msn_B | 100 | *(none)* |
| msn_C | 50 | *(none)* |

Assignment order:

1. **msn_C** -- lowest priority number (50), picked first.
2. **msn_A** -- priority 100, created before msn_B.
3. **msn_B** -- priority 100, created last.

Ordering only decides which mission is *tried* first. A mission that cannot be assigned yet (see [Why a Mission Is Waiting](#why-a-mission-is-waiting)) does not block the missions behind it; the Admiral moves on and tries the next pending mission.

## Captain Assignment

Armada tries to assign a mission as soon as it is created. When a mission frees a captain slot, the Admiral immediately offers it to the highest-priority pending mission, and every heartbeat cycle it walks the full pending queue (in priority, then creation order) and tries each mission against the captains with a free slot.

Assignment claims a captain slot atomically, together with the mission's move to Assigned, so two assignments can never take the same last slot. If the claim fails, the mission stays `Pending` and is retried on a later cycle. Admission takes the vessel's lock and then a shared capacity lock, always in that order, and releases both before the dock is provisioned and the agent launched.

### What Happens When All Captains Are Busy

Missions stay `Pending` until a captain slot frees. No missions are lost or dropped; they wait in the queue. Two optional settings can also hold new launches back: `MaxConcurrentMissions` (default 0, unlimited) caps how many missions run at once across the deployment, and `MinAvailableMemoryBytesForLaunch` (default 0, disabled) defers launches while the host is short on memory. How long missions wait, and on which gate, is recorded per mission (`GET /api/v1/missions/{id}/waits`) and in the `armada.concurrency` metrics; see [TELEMETRY.md](TELEMETRY.md).

### Why a Mission Is Waiting

Every `Pending` mission carries a server-computed **assignment blocker** (`AssignmentBlocker` on `GET /api/v1/missions/{id}`), shown as the **Why This Mission Is Waiting** card on the dashboard mission page and the matching section on the TUI mission screen. It names the reason, a summary, when the blocker clears on its own (for example a captain quarantine ending), any blocking or prerequisite missions, and what each captain is currently doing. The reasons are:

| Reason | Meaning |
|--------|---------|
| `AwaitingDispatch` | Nothing blocks the mission; it is assigned on the next dispatch cycle (or when a launch policy, such as a required Harbor connection, allows it). |
| `VesselMissing` | The mission has no vessel, or its vessel no longer exists. |
| `VesselMisconfigured` | The vessel cannot provision docks (`LocalPath` and `WorkingDirectory` are the same directory). |
| `DependencyNotFinished` | The mission depends on another mission that has not finished. |
| `DependencyHandoffPending` | The dependency finished and the handoff to this pipeline stage is still being prepared. |
| `WaitingForVoyageWorkers` | The architect sequenced this mission after the voyage's other implementation missions, which are still running. |
| `VesselBroadScopeMissionActive` | A broad-scope mission is running on the vessel and holds it exclusively. |
| `BroadScopeWaitingForVessel` | This mission is broad scope and waits until the vessel has no active missions. |
| `VesselVoyageLimit` | All vessel voyage slots are occupied. An admitted voyage retains its slot through review and landing; each standalone mission counts as a voyage. |
| `VoyageMissionLimit` | The voyage has reached its running mission limit. Assigned and InProgress consume slots; post-agent states do not. |
| `GlobalMissionLimit` | The deployment-wide running mission cap has been reached. |
| `VesselConcurrencyLimit` | Legacy stored reason from older releases. |
| `NoCaptains` | No captains exist. |
| `NoIdleCaptain` | Every captain is in a state that takes no new work (planning, refining, analyzing, quarantined, stalled, or stopping). |
| `NoEligibleCaptain` | Idle captains exist, but none may take this mission's persona or required tier. |
| `CaptainConcurrencyLimit` | Eligible captains exist, but every one of them is running `MaxConcurrency` missions. |
| `RuntimeMissionLimit` | The runtime's `MaxConcurrent` cap is reached by running missions. |
| `EndpointMissionLimit` | The model endpoint's `MaxConcurrentMissions` cap is reached by running missions. |
| `EndpointUnavailable` | An eligible model endpoint is disabled, unhealthy or temporarily circuit-broken. |
| `ParentNotFinished` | The mission's parent has not finished its last stage. This is not a capacity wait. |
| `BranchInUse` | Another active mission is working on the branch this mission continues. |
| `LaunchFailing` | The captain could not be started on the host the mission was placed on; the next attempt is shown as "Automatic retry". |
| `PlacementWaiting` | A captain is free, but no host can take the mission's dock yet (for example Require Harbor is on and no Harbor is connected). |

## Deadlocks and starvation

- **Mission and captain slots cannot deadlock.** A slot is claimed together with the move to Assigned; Pending missions hold nothing, and a running mission waits on nothing else in Armada.
- **Voyage slots are held, but not in a cycle.** An admitted voyage keeps its slot while its next stage waits for a captain. That wait is only on captain or global capacity, which running missions release, and those missions never wait on the voyage's slot.
- **Starvation is possible and visible.** A voyage waiting for human review keeps its slot. The blocker names the holding voyage and what it waits for, a `mission.starving` event and the `armada.missions.starving` gauge fire after `Concurrency.StarvationWarningMinutes` (60), and LandingFailed or Blocked missions do not keep a voyage's slot, so a stuck landing does not hold the vessel.
- **Fairness considers eligible missions only.** A Pending integration mission or a child waiting for its parent never blocks a sibling that could run, even at one mission per voyage.
- **Broad scope** waits for the vessel to have no running missions; a voyage that holds a slot without running anything does not block it.
- **Lowering a limit** stops new admissions until running work falls under it; nothing is stopped. The invariant monitor reports the temporary overrun with `cause=limit_lowered`.
- **Lock timeouts.** An admission that cannot take a lock within `Concurrency.AdmissionLockTimeoutSeconds` (30) returns and retries on the next cycle, counted in `armada.admission.lock_timeouts`. A mission eligible with every gate open for more than two ticks is counted in `armada.admission.stuck`.

## Heartbeat Cycle

The Admiral runs a health-check loop on a configurable interval controlled by the `heartbeatIntervalSeconds` setting (default: **10 seconds**). It also runs one cycle immediately at startup. On each cycle the Admiral:

1. **Checks captain and mission health** -- including stalled captains that have not reported progress within the `stallThresholdMinutes` window (default: 10 minutes).
2. **Assigns pending missions** -- walks the pending queue and matches missions with idle captains.
3. **Reconciles landings** -- open pull requests that have merged, and Landing Mode `None` missions whose branch was merged by hand.
4. **Runs recovery and background work** -- deployment rollout windows, the merge queue, background job maintenance, the vessel health schedule, mission failure recovery, and fleet action runs.

Every 10 cycles (about every 100 seconds at the default interval) it also rotates logs, maintains planning and backlog refinement sessions, and sweeps model endpoint health. Every 100 cycles (about every 17 minutes at the default interval) it runs data expiry (SQLite only) and retention pruning (request history, Ask threads, finished jobs, import batches, and fleet action runs).

## Manual Priority Override

You can set mission priority at creation time or update it later to reprioritize work.

### At Creation Time

Using the CLI:

```bash
armada mission create "Fix critical login bug" --vessel my-api --priority 1
```

Using the REST API, set `Priority` in the `POST /api/v1/missions` body. The `armada go` command and the MCP `create_mission` and `dispatch` tools do not take a priority; their missions start at the default (100), and you can change it afterward.

### After Creation

Using MCP tools:

- `update_mission` with the `priority` parameter to change the priority of an existing pending mission

## Practical Examples

### Making a Mission Jump the Queue

A critical bug is reported while several missions are already queued. Set the priority to a low number to ensure it is picked up next:

```bash
armada mission create "Fix: users cannot log in after password reset" --vessel my-api --priority 1
```

If the mission already exists, update its priority via MCP:

```
update_mission(missionId: "msn_abc123", priority: 1)
```

The mission will be assigned to the next captain that becomes idle, ahead of all default-priority (100) missions (as long as nothing listed under [Why a Mission Is Waiting](#why-a-mission-is-waiting) blocks it).

### Dispatching Low-Priority Background Work

Queue up non-urgent tasks that should only run when nothing more important is waiting:

```bash
armada mission create "Add XML doc comments to all public methods" --vessel my-api --priority 200
```

These missions will sit in the queue and only be assigned when no higher-priority missions are pending.

## Persona-Aware Routing

When a mission has a `Persona` field set (from a pipeline stage), the Admiral considers captain persona capabilities during assignment:

1. **Filter by AllowedPersonas:** If a captain has `AllowedPersonas` set (JSON array), only assign if the mission's persona is in the list. If `AllowedPersonas` is null, the captain can fill any role.
2. **Prefer PreferredPersona:** Among eligible captains, prefer one whose `PreferredPersona` matches the mission's persona.
3. **Tier routing:** When the mission requires a capability tier, only captains at or above that tier are eligible, and the lowest qualifying tier is preferred so strong captains are not consumed by cheaper work.
4. **No match waits:** `AllowedPersonas` is a hard filter. If no idle captain is allowed to serve the persona, the mission stays `Pending` until one is; `PreferredPersona` only breaks ties among eligible captains.

A mission with a preferred captain (`RequestedCaptainId`, from the dispatch payload, the voyage's per-persona override, or the persona's default captain) goes to that captain whenever it is idle, regardless of `AllowedPersonas`; when it is busy, assignment falls back by capability tier. See [CAPTAIN_ROUTING.md](CAPTAIN_ROUTING.md).

This allows dedicating specific captains to specific roles (e.g., an Opus-backed captain for Architect work, Sonnet-backed captains for Worker tasks).

### Full Scheduling Scenario

Suppose you have two captains, both busy, and dispatch the following work in this order:

| Order | Mission | Priority | Voyage |
|-------|---------|----------|--------|
| 1 | Add unit tests | 100 | vyg_testing |
| 2 | Fix typos in docs | 200 | *(none)* |
| 3 | Add rate limiting | 100 | *(none)* |
| 4 | Fix login crash | 1 | *(none)* |
| 5 | Add integration tests | 100 | vyg_testing |

When the captains free up, assignment proceeds as follows:

1. **Captain 1** gets "Fix login crash" (priority 1 -- lowest number wins).
2. **Captain 2** gets "Add unit tests" (priority 100, created before the other priority-100 missions).
3. When a captain finishes, the next pickup is "Add rate limiting" (priority 100, created before "Add integration tests").
4. Then "Add integration tests" (priority 100, created last of the three).
5. Finally "Fix typos in docs" (priority 200 -- lowest priority, assigned last).

Equivalent captains are selected by occupied slots, then earliest idle/update time and stable ID, after persona/tier preferences. Explicit captain selection and persona priority ordering take precedence. Priority-class ties retain stable ID order after occupied slots.
