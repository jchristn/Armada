# Mission Scheduling

This document explains how Armada decides which mission to assign to which captain and in what order.

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

Armada tries to assign a mission as soon as it is created. When a captain finishes a mission, the Admiral immediately offers the freed captain the highest-priority pending mission, and every heartbeat cycle it walks the full pending queue (in priority, then creation order) and tries each mission against the idle captains.

Assignment uses an atomic **TryClaim** operation on the captain to prevent race conditions when two assignments find the same idle captain at once. Only one assignment can claim a given captain; if the claim fails, the mission is reverted to `Pending` and retried on a later cycle.

### What Happens When All Captains Are Busy

Missions stay in the `Pending` state until a captain finishes its current work and becomes idle. No missions are lost or dropped -- they simply wait in the queue. Two optional settings can also hold new launches back: `MaxConcurrentMissions` (default 0, unlimited) caps how many captains may be working at once, and `MinAvailableMemoryBytesForLaunch` (default 0, disabled) defers launches while the host is short on memory.

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
| `VesselConcurrencyLimit` | The vessel runs one mission at a time (`AllowConcurrentMissions` is off) and another mission is active. |
| `NoCaptains` | No captains exist. |
| `NoIdleCaptain` | Every captain is busy (working, planning, refining, quarantined, stalled, or stopping). |
| `NoEligibleCaptain` | Idle captains exist, but none may take this mission's persona or required tier. |

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
