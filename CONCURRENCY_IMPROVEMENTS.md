# Concurrency improvements: captain slots, vessel voyage/mission limits, mission hierarchy

Status: implementation started for v1.3.0 on `feature/v1.3.0`. It is based on repository commit `27042d923f921e6ce63059502af06d213345263b`, inspected 2026-10-27. The original baseline below is historical; the progress section records what is implemented. Line numbers are as of that commit; re-check them before you edit. The owner's answers to the open questions were folded in on 2026-10-27 (section 17), so every decision below is final unless marked otherwise.

The v1.3.0 implementation starts from `b21fc9ef40b85b89d6a9b2437373c13ac4d1de48` (2026-11-02).

Captain usage management and the mobile applications shipped in v1.2.0. This plan now builds on that release, including its existing usage scheduling and schema migrations through 116. Do not renumber or rewrite shipped migrations; section 12 describes the integration with usage management.

## Implementation progress (v1.3.0)

- [x] Create `feature/v1.3.0` from the completed v1.2.0 baseline and align product/package versions.
- [x] Start M1 with shared bounds, `ConcurrencySettings`, runtime-specific captain default helpers, settings-file normalization, and boundary/round-trip tests.
- [x] Vessel vertical slice: migration 117 across all providers, model/storage mapping, numeric editors in Dashboard/Mobile/TUI, REST/MCP operations, shared admission explanations and serialized check/claim. Tested with 50 simultaneous dispatch requests and a live HTTP/git/process scenario; see [test guide](docs/CONCURRENCY_TESTING.md).
- [ ] Finish M1: model/DTO changes, new migrations 117-121 and all four database providers' atomic slot operations and mapping.
- [x] Captain vertical slice: migration 118 and atomic slots across all providers; per-mission lifecycle, heartbeat/recovery budgets, cancellation, recall and Harbor reconnection; REST/MCP/WebSocket fields and Dashboard/Mobile/TUI capacity editors. HTTP/git/process tests exercise three missions on one captain, vacancy refill, crash/restart and lowering limits without preemption.
- [x] M3 admission locking/load distribution: bounded per-vessel locks followed by a shared capacity lock; remove unused lock entries; preserve routing preferences and spread equivalent work by occupied slots. Add timeout metrics and 200-mission/mixed-limit progress coverage.
- [x] M3 waiting diagnostics: share broad/branch/vessel safety decisions and runtime/endpoint capacity filters with dispatch; distinguish saturation from endpoint unavailability; add bounded, role-filtered advice in REST and MCP mission status, compact Dashboard next steps and native Mobile/TUI explanations. Keep remaining dependency/eligibility unification and invariant monitoring open.
- [ ] Finish M2-M3: remove legacy captain pointers in migration 121, unify admission evaluation and invariants, release captain reservations during verification, and complete the remaining stress/invariant-monitor matrix. Vessel/global gates and captain capacity admission are active.
- [ ] M4-M5: hierarchy, branch ownership, wait telemetry and Grafana.
- [ ] M6-M10: public operations and all interactive clients. The new `Concurrency` group is currently a settings-file foundation, not an editable REST/MCP settings group.
- [ ] M11: final integration, release documentation, migration/upgrade validation, and the complete concurrency stress matrix.

Migration 117 preserves existing vessel admission behavior. Migration 118 changes existing ApiEndpoint/Mux captains to concurrency 4 and CLI captains to 1; set endpoint-backed captains to 1 to retain serial operation. The existing global `MaxConcurrentMissions` settings-file guard now clamps negative input to 1 (not unlimited) and values above 10000 to 10000.

Waiting diagnostics have [UX/layout validation evidence](docs/concurrency/WAITING_DIAGNOSTICS_VALIDATION.md), including light/dark responsive checks and actual editor navigation.

The admission segment has [UX/layout validation evidence](docs/concurrency/ADMISSION_VALIDATION.md) for the actual Dashboard at desktop, tablet and mobile widths.

### Segment completion requirements

Every implementation segment ends with a UX/layout/aesthetic validation of affected surfaces (including backend-driven state), a commit/push on `feature/v1.3.0`, and `./build-all.sh v1.3.0`. Keep README/CHANGELOG and affected REST/MCP contracts current. Docker builds update both `v1.3.0` and `latest` and include the VSIX. Record actual checks and build results; do not mark later milestones complete based on a partial slice.

## 1. Goals

1. A **captain** can run up to `MaxConcurrency` missions at once. This is the innermost gate. Defaults:
   - CLI harness captains: 1. That covers ClaudeCode, Codex, Gemini, Cursor, OpenCode, pi and Custom.
   - Endpoint-backed captains: 4. That covers ApiEndpoint and Mux.
2. A **vessel** has two limits, replacing the bool `AllowConcurrentMissions`:
   - `MaxParallelVoyages`: how many voyages can be active on the vessel at once. Default 1.
   - `MaxParallelMissionsPerVoyage`: how many missions of one voyage can be active at once. Default 4.
3. **Enforcement** never exceeds any limit, even when assignment calls race. It never deadlocks. When a mission waits, Armada says which gate it is waiting on.
4. **Telemetry** records how long a mission waits for a slot or lock, broken down by gate. The new metrics are exported to Prometheus and shown on a Grafana dashboard and in the Armada UI.
5. **Mission hierarchy**: `ParentMissionId` defines a tree inside a voyage.
   - A child starts after its parent finishes.
   - Siblings are independent and can run in parallel when the limits allow.
   - Example: Voyage A contains A -> B -> C (chained) and D (sibling of A). A and D can run at the same time.
6. **Mission-generating prompts**:
   - Produce smaller missions, and nest them with parent/child relationships only where that is appropriate.
   - Give missions that can run in parallel clearly separate file ownership.
7. **Everything that exposes these fields is updated**: REST, MCP, WebSocket, the .NET SDK, the Helm CLI, Postman, the API surface baseline, the Dashboard, the TUI, Mobile and the docs.

Non-goals:
- Preempting or killing running missions when a limit is lowered.
- Coordinating several Admirals. Armada runs one Admiral process, and Harbors only execute work.
- Per-vessel Prometheus labels. See section 8.3.

## 2. Verified baseline (what exists today)

| Area | Today | Location |
|---|---|---|
| Vessel concurrency | `bool AllowConcurrentMissions`, default false | `src/Armada.Core/Models/Vessel.cs:179` |
| Vessel gate | Defers when `concurrentCount > 0` and the flag is false; warns when it is true | `MissionService.cs:554`, `:570` |
| Blocker explanation | `VesselConcurrencyLimit` and the other reasons | `MissionService.cs:221-380` (`:328`), `MissionAssignmentBlockerReasonEnum` |
| Captain capacity | One mission per captain. `CurrentMissionId`, `CurrentDockId` and `ProcessId` are single fields | `Captain.cs:146-156` |
| Captain claim | `UPDATE ... WHERE id=@id AND state='Idle'` | `Database/*/Implementations/CaptainMethods.cs` (`TryClaimAsync`, Sqlite `:432`), `ICaptainMethods.cs:90-113` |
| Captain release | `CaptainService.ReleaseAsync` sets Idle and nulls the fields; called from about 9 AdmiralService sites | `CaptainService.cs:371`; AdmiralService `:827, 1458, 1472, 1646, 1730, 1899, 2450, 2504, 2606` |
| Idle-only routing | `FindAvailableCaptainAsync` lists Idle captains only | `MissionService.CaptainRouting.cs:199` |
| Endpoint cap | `InferenceOptions.MaxConcurrentMissions` (1-64), counts **Working captains** per base URL | `InferenceOptions.cs:33`, `MissionService.ApplyCapacityLimitsAsync` `:1049-1149` |
| Runtime cap | `AgentSettings.MaxConcurrent`, counts captains | `AgentSettings.cs:49` |
| Global cap | `ArmadaSettings.MaxConcurrentMissions` (0 = unlimited), counts **Working captains**; false if no captain is Idle | `ArmadaSettings.cs:583`, `AdmiralService.HasAvailableCapacityAsync` `:2209-2236` |
| Assignment serialization | **None.** There is a per-mission in-flight dictionary plus atomic SQL claims. `TryAssignAsync` is called from the health tick and directly from dispatch paths, so calls can run concurrently | `MissionService.cs:119, 404, 409`; `AdmiralService.cs:274, 411, 460, 738, 2153` |
| Scheduler tick | `HealthCheckLoopAsync` every `HeartbeatIntervalSeconds` -> `DispatchPendingMissionsAsync` | `ArmadaServer.cs:892, 2165-2179`; `AdmiralService.cs:644, 738` |
| Per-captain files | `logs/captains/{captainId}.current` holds a single pointer | `AgentLifecycleHandler.cs:1003` |
| Dormant column | `captains.max_parallelism INTEGER NOT NULL DEFAULT 1` (migration 4), never mapped | `Sqlite/Queries/TableQueries.cs:168` |
| `ParentMissionId` | **Already exists** and is stored by all 4 drivers, but is used only for rescue linkage (`rescue.ParentMissionId = original.Id`, plus "no rescue of a mission with a parent") | `Mission.cs:164`; `MissionRecoveryCoordinator.cs:254, 270, 287` |
| Dependencies | `DependsOnMissionId` is a single string. It chains pipeline stages, and is satisfied by `Complete`, or by `WorkProduced` with the handoff prepared | `Mission.cs:214`; `AdmiralService.cs:370-391`; `MissionService.cs:485-512, 1965-1980` |
| Branch continuation | After a stage completes, dependents inherit `BranchName` | `MissionService.Handoff.cs:217` |
| Architect plan | `ArchitectPlanMission.dependsOn` (a 1-based int) and `waitForOtherMissions`; contract version 3 | `Models/ArchitectPlanMission.cs:46, 54`; `ArchitectPlanParser.cs`; `MissionService.ArchitectPlan.cs`; `MissionService.Architect.cs` |
| Mission timestamps | `CreatedUtc`, `StartedUtc` and `CompletedUtc` only. No assigned or queued time | `Mission.cs:305-323` |
| Metrics | Static `Meter("Armada")` with counters and histograms. Nothing covers queueing | `src/Armada.Core/ArmadaMetrics.cs`; `src/Armada.Server/ArmadaTelemetryHost.cs:116` |
| Grafana | `armada-reliability.json`, `armada-fleet-operations.json`, `armada-storage.json` | `docker/armada/observability/grafana/dashboards/` |
| Migrations | The latest on the v1.2.0 starting point is **116** in every driver. MySQL keeps its migrations in `MysqlDatabaseDriver.cs`, not in `TableQueries.cs`. SQLite rebuilds the vessels table with an explicit column list | `Sqlite/Queries/TableQueries.cs:2269`, `:567` |

The single-mission assumption on captains is the largest piece of work. Non-test code has 232 references to `CurrentMissionId` and 149 to `CurrentDockId`, in AdmiralService, CaptainService, AgentLifecycleHandler, MissionService, PlanningSessionCoordinator, ObjectiveRefinementCoordinator, CaptainRouting, AdmiralFleetActionMissionDispatcher, CaptainRuntimeToolCatalogService, the DB drivers, the TUI, Helm and the Dashboard.

## 3. Decisions

| # | Decision | Choice and reason |
|---|---|---|
| D1 | Source of truth for what a captain is running | The **missions table**: missions where `captain_id = X AND status IN (Assigned, InProgress)`. Missions already carry `CaptainId`, `DockId` and `ProcessId`. The captain also gets a denormalized `ActiveMissionCount` so slots can be claimed atomically, and the health tick reconciles that count against the missions table (D8). |
| D2 | Captain state | `Idle` when ActiveMissionCount is 0. `Working` when 1 or more. `Planning`, `Refining` and `Analyzing` stay **exclusive**: a captain in those states takes no missions, and a captain with active missions cannot enter them. `Stalled`, `Quarantined` and `Stopping` block new admissions, but missions already running on the captain continue. Stall detection moves to per-mission (section 6.4). |
| D3 | `Captain.CurrentMissionId` and `CurrentDockId` | **Removed in v1.3.0**, with no compatibility shim. New `ActiveMissionIds` (a computed, read-only list) replaces them on the captain DTOs. Every internal reader moves to a mission id in M2; planning and refinement move to a new `Captain.CurrentSessionId` (section 6.4). The columns are dropped in migration 121. Listed as a breaking change in `CHANGELOG.md` and `UPGRADING.md`. |
| D4 | Captain concurrency column | New column `max_concurrency`. The dormant `max_parallelism` is **not** reused: it defaults to 1 for every row, so it cannot carry the per-runtime defaults, and it is not reliably present in every driver. Leave it unused and note it in `archive/DECISIONS.md`. |
| D5 | Captain defaults | Set by runtime **at creation**: ApiEndpoint and Mux get 4; ClaudeCode, Codex, Gemini, Cursor, OpenCode and Custom get 1. Allowed range 1..`Concurrency.MaxCaptainConcurrency` (default 256). Changing a captain's runtime does not change its value unless the client sends `MaxConcurrency`; the Dashboard, TUI and Mobile forms suggest the new runtime's default. **Upgrade backfill**: existing ApiEndpoint and Mux captains get 4, everything else gets 1, as requested. This is called out in `UPGRADING.md`, because existing Mux and ApiEndpoint captains will start running up to 4 missions at once. Their endpoint's `MaxConcurrentMissions` still caps them. |
| D6 | Vessel limits | `MaxParallelVoyages` and `MaxParallelMissionsPerVoyage` are ints; **0 means unlimited** (the same convention as the global `MaxConcurrentMissions`). New vessels default to 1 and 4. **Upgrade backfill preserves behavior**: `allow_concurrent_missions = 0` becomes 1/1 (serial, as today), and `= 1` becomes 0/0 (unlimited, as today). |
| D7 | `AllowConcurrentMissions` | **Removed in v1.3.0**, with no shim. Migration 98 backfills the new limits from the column (D6) and migration 121 drops it. A REST or MCP request that still sends `AllowConcurrentMissions` gets a 400 validation error naming `MaxParallelVoyages` and `MaxParallelMissionsPerVoyage`, so an old client fails loudly instead of silently getting different behavior. Helm `--allow-concurrent` fails the same way. Listed as a breaking change. |
| D8 | Race-free enforcement | (a) a per-vessel async admission lock around "evaluate gates -> claim captain slot -> mark mission Assigned"; (b) an atomic conditional captain slot claim in SQL; (c) an invariant monitor on every tick. Details in section 6. |
| D9 | What "active voyage" means for `MaxParallelVoyages` | A voyage holds a voyage slot from the moment its first mission is assigned until the voyage reaches a terminal status (Complete, Failed or Cancelled), **or** until it has no missions left in a non-terminal status. The slot is sticky: it is kept between pipeline stages, while waiting for review, and while landing. Otherwise a second voyage could slip in whenever a stage hands off, and with a limit of 1 two voyages would interleave on the same vessel. |
| D10 | Standalone missions (no voyage) | Each one counts as a voyage of one mission. It takes a voyage slot while it is Assigned, InProgress or in a post-run status (WorkProduced, Testing, Review, PullRequestOpen, Verifying), and gives it up when it reaches a terminal status. Rescue missions follow the same rule. |
| D11 | What "active mission" means for `MaxParallelMissionsPerVoyage` and captain slots | Status Assigned or InProgress, meaning a process is running or about to run. Pending, WorkProduced, Review and the other post-run statuses **do not** hold mission or captain slots. That keeps the mission gate free of hold-and-wait (section 6.5). |
| D12 | What existing caps count | The endpoint `MaxConcurrentMissions`, runtime `AgentSettings.MaxConcurrent` and global `MaxConcurrentMissions` caps all change from counting Working **captains** to counting active **missions**. Their names already say "missions", and this is the only way they stay meaningful once one captain can run several. |
| D13 | Hierarchy field | `ParentMissionId` becomes the hierarchy link. Rescue linkage moves to a new `RescueOfMissionId`, with a data migration for rows where `is_rescue = 1`. Without this change, `MissionRecoveryCoordinator.cs:254` ("no rescue when ParentMissionId is set") would turn off rescue for every child mission. |
| D14 | Hierarchy versus pipeline stages | The hierarchy is between **logical missions**. A logical mission is a root mission (usually the Worker) plus its pipeline stage chain (Test Engineer, Judge, ...), still linked by `DependsOnMissionId`. `ParentMissionId` always refers to a logical mission's **root**. Stage missions copy their root's `ParentMissionId` so the tree view can show it, and `IsStageOf` (the root id) is derived from the DependsOn chain. If a request points `ParentMissionId` at a stage mission, it is changed to point at that stage's root. |
| D15 | When a child starts | A child becomes eligible when its parent's **terminal stage** has reached `Complete`, or `WorkProduced` with the handoff prepared. This is the same rule `ApplyArchitectMissionDependenciesAsync` and `FindTerminalPipelineStage` use today. A child root mission never has both `ParentMissionId` and `DependsOnMissionId` set; the parent link implies the dependency. |
| D16 | Branches in a hierarchy | **A series of missions may reuse one branch.** Each logical mission gets a `BranchStrategy` (new `MissionBranchStrategyEnum`): `ContinueParent` works on the parent's branch, `ForkFromParent` creates a new branch from the parent's final branch head, and `New` (roots) branches from the vessel default. Default rule: a child that is its parent's **only** child continues the parent's branch, so A -> B -> C is one branch. That matches how a `dependsOn` slice continues its dependency's branch today. When a parent has **several** children, the first one in plan order continues the branch and the rest fork from the parent's head, so siblings that can run in parallel never share a branch. A plan or dispatch may set `branchStrategy` explicitly; the server downgrades `ContinueParent` to `ForkFromParent` (and logs why) if another child already continues that branch. The fork start point is stored in a new `Mission.BaseBranchName`. The existing `BranchLock` gate (`BranchHolders`) still guarantees that two missions never run on one branch at once. Landing order is in section 7.4. |
| D17 | Hierarchy scope and validation | The parent must be in the same voyage (or, for standalone missions, on the same vessel with no voyage). No cycles. Maximum depth is `Concurrency.MaxHierarchyDepth` (default 4). A parent cannot be deleted or purged while it has non-terminal descendants, unless the request cascades. |
| D18 | Failure and cancellation propagation | When a parent fails, is cancelled or lands with `LandingFailed`, all of its **descendants** are cancelled, with reason `ParentFailed` and the parent's id. Today only direct DependsOn dependents are handled (`Handoff.cs:22, 285`). Siblings are not affected. **Restarting a parent always puts its descendants that were cancelled with `ParentFailed` back to Pending**, on every surface (UI, REST, MCP, Helm). Descendants cancelled for another reason, for example by an operator, stay cancelled. |
| D19 | Fairness | Voyage slots go to the waiting voyages in order of their earliest assignable mission's `(Priority, CreatedUtc)`. Mission slots within a voyage go to that voyage's missions in `(Priority, CreatedUtc)` order. A mission waiting longer than `Concurrency.StarvationWarningMinutes` (default 60) raises a `mission.starving` event and is counted in a metric. |
| D20 | Lowering a limit while missions run | Nothing running is stopped. New admissions wait until the count is under the new limit. The UI shows "4 running / limit 2". |
| D21 | Value clamping | **Every configurable concurrency value is clamped to a documented range at every entry point** (model setters, settings load, DB read, REST, MCP, Helm, Dashboard, TUI, Mobile). Out-of-range input is clamped, not rejected, and the response always returns the **effective** (clamped) value so the caller sees what was stored. A clamp is logged at Debug for API input and at Warning for settings files and DB rows. Two rules matter: (1) where 0 means unlimited, a **negative** value clamps to **1** (the most restrictive finite value), never to 0, so a typo like `-1` can never open a vessel up; (2) cross-field bounds (a captain's `MaxConcurrency` against `Concurrency.MaxCaptainConcurrency`) are applied in the service layer and again at admission time, because a model setter cannot see settings. Structural input (cycles, hierarchy depth in a plan, a missing parent) is still **rejected**, not clamped. Full table in section 4.7. |

## 4. Data model

### 4.1 Settings: `src/Armada.Core/Settings/ConcurrencySettings.cs` (new), at `ArmadaSettings.Concurrency`

| Property | Default | Validation |
|---|---|---|
| `DefaultVesselMaxParallelVoyages` | 1 | 0..1000 (0 = unlimited) |
| `DefaultVesselMaxParallelMissionsPerVoyage` | 4 | 0..1000 |
| `DefaultCliCaptainMaxConcurrency` | 1 | 1..MaxCaptainConcurrency |
| `DefaultEndpointCaptainMaxConcurrency` | 4 | 1..MaxCaptainConcurrency |
| `MaxCaptainConcurrency` | 256 | 1..256 |
| `MaxHierarchyDepth` | 4 | 1..16 |
| `StarvationWarningMinutes` | 60 | 1..10080 |
| `AdmissionLockTimeoutSeconds` | 30 | 1..600 (section 6.2) |
| `WaitIntervalRetentionDays` | 90 | 1..3650 |

Clamp in the setters with `Math.Clamp` (the pattern in `Models/VesselDiscoveryRequest.cs:40`), **not** by throwing like the older `ArmadaSettings` setters: a bad value in `settings.json` must not stop the server from starting. Cross-field bounds (the two `Default*CaptainMaxConcurrency` values must not exceed `MaxCaptainConcurrency`) cannot be done in a setter because JSON property order is not guaranteed; add `ConcurrencySettings.Normalize()` and call it after settings load and after every settings update over the API. Section 4.7 has the full rules. Add the defaults and the hard ceilings to `Constants.cs`. Add the settings to `docs/api-surface-1.0.json` under `settings` and regenerate it.

### 4.2 Vessel (`Vessel.cs`)
- Add `int MaxParallelVoyages` (default from settings, 1) and `int MaxParallelMissionsPerVoyage` (default 4), clamped in the setters as described in section 4.7 (0..1000, 0 = unlimited, negative becomes 1).
- Delete `AllowConcurrentMissions` (D7).
- Callers to update: the MCP vessel tools and args, the TUI `VesselForm` and `SetupWizardScreen`, `BatteryFixtureService`, and the Dashboard and Mobile forms.

### 4.3 Captain (`Captain.cs`)
- `int MaxConcurrency`. Its default comes from `CaptainConcurrencyDefaults.For(AgentRuntimeEnum)` (new static helper in `Armada.Core/Services/`). The setter clamps to the hard range 1..`Constants.CaptainConcurrencyCeiling` (256); `CaptainService` then clamps to the configured `Concurrency.MaxCaptainConcurrency` on create and update (section 4.7).
- `int ActiveMissionCount`: read-only in the API, maintained by the claim and release SQL.
- `List<string> ActiveMissionIds`: computed when the captain is read for the API, not stored.
- Delete `CurrentMissionId` and `CurrentDockId` (D3). Add `string? CurrentSessionId` for the exclusive Planning, Refining and Analyzing states.

### 4.4 Mission (`Mission.cs`)
- `ParentMissionId`: same field, new meaning (D13-D17). Update the XML doc.
- `string? RescueOfMissionId` (new). `IsRescue` stays.
- `MissionBranchStrategyEnum BranchStrategy` (new, D16): `New`, `ContinueParent` or `ForkFromParent`.
- `string? BaseBranchName` (new): the start point of the mission's branch when it forks from a parent instead of starting from the vessel's default branch.
- `DateTime? ReadyUtc` (new): the first time the mission's only blockers were capacity gates (section 8.1).
- `DateTime? AssignedUtc` (new): when the slot was claimed.
- `int Depth` (new, stored): 0 for a root, otherwise the parent's depth plus 1. Stored so tree queries and the depth check stay cheap.

### 4.5 New table `mission_wait_intervals`

| Column | Type | Notes |
|---|---|---|
| `id` | text pk | prefix `mwi_`, added to `Constants.cs` |
| `tenant_id`, `mission_id`, `vessel_id`, `voyage_id`, `captain_id` (nullable) | text | |
| `gate` | text | `MissionWaitGateEnum` (section 8.1) |
| `started_utc`, `ended_utc` (nullable) | datetime | an open interval has `ended_utc` null |
| `detail` | text | the blocker summary at the time the interval opened |

Indexes: `(mission_id)`, `(vessel_id, started_utc)`, `(ended_utc)` for open intervals. Retention: purged along with the mission, and after `Concurrency.WaitIntervalRetentionDays`.

### 4.6 Enums
- `MissionAssignmentBlockerReasonEnum` gets four new values:
  - `VesselVoyageLimit`: the vessel's voyage slots are full.
  - `VoyageMissionLimit`: the voyage's mission slots are full.
  - `CaptainConcurrencyLimit`: an eligible captain exists, but all of its slots are busy.
  - `ParentNotFinished`
- `VesselConcurrencyLimit` is no longer produced. The value stays in the enum, marked obsolete, only so stored blocker events still deserialize.
- New `MissionBranchStrategyEnum`: `New`, `ContinueParent`, `ForkFromParent` (D16).
- New `MissionWaitGateEnum`: `Dependency`, `Parent`, `IntegrationBarrier`, `BroadScope`, `BranchLock`, `VesselVoyageSlot`, `VoyageMissionSlot`, `CaptainSlot`, `EndpointSlot`, `RuntimeSlot`, `GlobalSlot`, `Placement`, `LaunchBackoff`, `Other`.

Both enums follow the existing pattern: string JSON converter plus `EnumMember`.

### 4.7 Value clamping (applies to every configurable value in this plan)

One helper, `ConcurrencyClamp` (new static class in `Armada.Core/Services/`), owns every range below. Model setters, `ConcurrencySettings.Normalize()`, the DB readers, the route and MCP handlers and the admission evaluator all call it, so the ranges live in one place. The Dashboard, TUI and Mobile copy the ranges into their form validation and get them from `GET /api/v1/settings` where they depend on settings.

| Value | Range | Default | Below min | Above max | Notes |
|---|---|---|---|---|---|
| `Vessel.MaxParallelVoyages` | 0..1000, 0 = unlimited | 1 (settings) | negative -> **1** | -> 1000 | |
| `Vessel.MaxParallelMissionsPerVoyage` | 0..1000, 0 = unlimited | 4 (settings) | negative -> **1** | -> 1000 | |
| `Captain.MaxConcurrency` | 1..`MaxCaptainConcurrency` | 1 CLI, 4 endpoint/Mux | -> 1 | -> `MaxCaptainConcurrency` | 0 is **not** unlimited for captains; it clamps to 1 |
| `Concurrency.MaxCaptainConcurrency` | 1..256 (`Constants.CaptainConcurrencyCeiling`) | 256 | -> 1 | -> 256 | |
| `Concurrency.DefaultCliCaptainMaxConcurrency` | 1..`MaxCaptainConcurrency` | 1 | -> 1 | -> `MaxCaptainConcurrency` | cross-field, in `Normalize()` |
| `Concurrency.DefaultEndpointCaptainMaxConcurrency` | 1..`MaxCaptainConcurrency` | 4 | -> 1 | -> `MaxCaptainConcurrency` | cross-field, in `Normalize()` |
| `Concurrency.DefaultVesselMaxParallelVoyages` | 0..1000 | 1 | negative -> 1 | -> 1000 | |
| `Concurrency.DefaultVesselMaxParallelMissionsPerVoyage` | 0..1000 | 4 | negative -> 1 | -> 1000 | |
| `Concurrency.MaxHierarchyDepth` | 1..16 | 4 | -> 1 | -> 16 | a plan deeper than this is **rejected**, not flattened |
| `Concurrency.StarvationWarningMinutes` | 1..10080 | 60 | -> 1 | -> 10080 | |
| `Concurrency.AdmissionLockTimeoutSeconds` | 1..600 | 30 | -> 1 | -> 600 | |
| `Concurrency.WaitIntervalRetentionDays` | 1..3650 | 90 | -> 1 | -> 3650 | |
| Endpoint `InferenceOptions.MaxConcurrentMissions` | 1..64 (existing) | existing | -> 1 | -> 64 | already range-checked; switch it to the same clamp behavior |
| Global `ArmadaSettings.MaxConcurrentMissions` | 0..10000, 0 = unlimited | existing | negative -> **1** | -> 10000 | its setter throws today; change it to clamp and log, and note the change in `UPGRADING.md` |

Where clamping happens:
- **Model setters**: hard ranges only, with no dependency on settings.
- **Settings load and settings update API**: `Normalize()` after binding. It logs a Warning naming the setting, the given value and the effective value.
- **Lowering `MaxCaptainConcurrency`** below a captain's stored value does not rewrite the row. The admission evaluator and the captain DTO use `effectiveMaxConcurrency = min(stored, MaxCaptainConcurrency)`, and the captain DTO exposes both `MaxConcurrency` and `EffectiveMaxConcurrency` so the UI can show "8 (capped at 4 by server settings)". D20 applies: running missions are not stopped.
- **DB readers** in all four drivers clamp on read, so a row edited by hand or written by a bad migration cannot produce a negative or huge limit. No DB `CHECK` constraints: they cannot be added consistently to existing SQLite tables, and clamping on read covers the same case.
- **REST and MCP**: clamp, store, and return the stored entity, so the response shows the effective value. MCP tool schemas declare `minimum` and `maximum` for each field so well-behaved clients never send out-of-range values; the server clamps anyway.
- **Helm CLI**: clamp before sending and print `max-concurrency 300 clamped to 256` on stderr.
- **Dashboard, TUI, Mobile**: number inputs set `min` and `max`, clamp on blur, and show the range in the helper text ("1-256"). The "Unlimited" toggle is the only way to set 0 on a vessel field.
- **Architect prompt placeholder** `{MaxParallelMissionsPerVoyage}` renders `unlimited` for 0.

## 5. Database (all four drivers)

Migrations 117-121, in this order. Each one goes into the Sqlite, Postgresql and SqlServer `TableQueries.GetMigrations()` and into the migration list in `MysqlDatabaseDriver.cs`.

| # | Change |
|---|---|
| 117 | `vessels`: add `max_parallel_voyages INTEGER NOT NULL DEFAULT 1` and `max_parallel_missions_per_voyage INTEGER NOT NULL DEFAULT 4`. Backfill: `UPDATE vessels SET max_parallel_voyages = CASE WHEN allow_concurrent_missions = 1 THEN 0 ELSE 1 END, max_parallel_missions_per_voyage = CASE WHEN allow_concurrent_missions = 1 THEN 0 ELSE 1 END`. |
| 118 | `captains`: add `max_concurrency INTEGER NOT NULL DEFAULT 1`, `active_mission_count INTEGER NOT NULL DEFAULT 0` and `current_session_id` (nullable text). Backfill `max_concurrency = 4 WHERE runtime IN ('ApiEndpoint','Mux')`, matching the stored enum text of each driver. Backfill `active_mission_count` from the missions table: the count of missions with status Assigned or InProgress per `captain_id`. |
| 119 | `missions`: add `rescue_of_mission_id`, `base_branch_name`, `ready_utc`, `assigned_utc`, `owned_paths_json`, `reads_paths_json`, `depth INTEGER NOT NULL DEFAULT 0` and `branch_strategy TEXT NOT NULL DEFAULT 'New'`. Data step: `UPDATE missions SET rescue_of_mission_id = parent_mission_id, parent_mission_id = NULL WHERE is_rescue = 1`. Backfill `assigned_utc = started_utc` where it is not null. Add an index on `(voyage_id, parent_mission_id)`. |
| 120 | Create `mission_wait_intervals` and its indexes. |
| 121 | Drop the removed columns (D3, D7): `vessels.allow_concurrent_missions`, `captains.current_mission_id` and `captains.current_dock_id`. Before dropping, copy `current_mission_id` into `captains.current_session_id` for captains in Planning, Refining or Analyzing. SQLite drops them through a table rebuild (the existing one at `:567` for vessels; add one for captains if none exists). It runs after 117-120 so their backfills can still read the old columns. |

For each driver:
- Update the base DDL: Postgres `TableQueries.cs:99`, SqlServer `:2077`, MySQL `:51`, and the SQLite create statement.
- **Update the SQLite vessels rebuild** at `Sqlite/Queries/TableQueries.cs:567`. It lists columns explicitly, so the new columns are lost if you skip it. Check the same for any captains or missions rebuild.
- Update the readers (`SqliteDatabaseDriver.cs:593`, `SqlServerDatabaseDriver.cs:482`, `MysqlDatabaseDriver.cs:1122`, Postgres mapping) and the insert and update SQL in `Implementations/{Vessel,Captain,Mission}Methods.cs`.
- Add `IMissionWaitIntervalMethods` and one implementation per driver, following the interface-per-entity pattern. Expose it on the database driver as `MissionWaitIntervals`.

New `ICaptainMethods` operations, replacing the Idle-only claim and the release that matches on the current mission:
- `TryClaimSlotAsync(captainId, missionId)`: `UPDATE captains SET active_mission_count = active_mission_count + 1, state = 'Working', last_update_utc = @now WHERE id = @id AND active_mission_count < max_concurrency AND state IN ('Idle','Working')`. It returns true when exactly one row was updated.
- `TryReleaseSlotAsync(captainId, missionId)`: decrements `active_mission_count`, never below 0, and sets `state = 'Idle'` when the count reaches 0. It runs only if the mission's `captain_id` still equals this captain, so releasing the same mission twice does not decrement twice. To guarantee that, run it in one transaction with the mission's `captain_id` and status update, or guard it on a `released_utc` that is still null; pick one per driver and document it.
- `ReconcileActiveCountsAsync()`: recomputes `active_mission_count` from the missions table and returns the rows it corrected. Called by the invariant monitor.

Delete `TryClaimAsync` and `TryReleaseFromMissionAsync` in M2. Planning and refinement get `TryClaimExclusiveAsync(captainId, sessionId, state)`: `UPDATE ... WHERE id = @id AND state = 'Idle' AND active_mission_count = 0`, which sets `current_session_id`; `TryReleaseExclusiveAsync` clears it.

Tests: extend `Database/ConcurrentAccessSuite.cs` with 50 parallel `TryClaimSlotAsync` calls against `max_concurrency = 4`. Exactly 4 must succeed, on every driver the test runner can reach.

## 6. Scheduling and enforcement

### 6.1 Gate order in `TryAssignAsync` and `GetAssignmentBlockerAsync`

Both functions must use **one shared evaluator**, a new `MissionAdmissionEvaluator` (`Armada.Core/Services/MissionAdmissionEvaluator.cs`) that returns an `AdmissionDecision { Admit, Gate, Reason, Summary, CandidateCaptain }`. That stops the blocker explanation from drifting away from what assignment actually does, which is the problem today with the logic at `:328` versus `:554`.

Order. Cheap and structural checks come first; capacity checks come last, from the outermost gate to the innermost.
1. Mission still Pending, launch backoff, voyage not terminal (existing).
2. **Parent**: the parent's terminal stage has finished (D15). Gate `Parent`.
3. Dependency (DependsOn, existing). Gate `Dependency`.
4. Integration barrier (existing). Gate `IntegrationBarrier`.
5. Broad scope (existing). Gate `BroadScope`.
6. **Vessel voyage slot**:
   - If the mission's voyage (or the mission itself, if standalone) already holds a slot, it passes.
   - Otherwise it passes only if `activeVoyages < MaxParallelVoyages` and it is first in voyage fairness order (D19).
   - Gate `VesselVoyageSlot`.
7. **Voyage mission slot**: `activeMissionsInVoyage < MaxParallelMissionsPerVoyage`, and the mission is first in its voyage's order among the eligible missions. Gate `VoyageMissionSlot`.
8. Branch in use (existing `BranchHolders`). Gate `BranchLock`.
9. Global cap: active missions `< MaxConcurrentMissions`. Gate `GlobalSlot`.
10. Captain selection:
    - Eligible captains: persona, tier, requested captain, Harbor policy and quarantine, as today.
    - Of those, keep the captains with free slots: `ActiveMissionCount < MaxConcurrency` and a state that admits work.
    - Then apply the runtime cap (`RuntimeSlot`) and the endpoint cap (`EndpointSlot`), both counting active missions (D12).
    - If eligible captains exist but none has a free slot, the gate is `CaptainSlot` and the reason is `CaptainConcurrencyLimit`.
    - If no captain is eligible at all, report `NoEligibleCaptain` or `NoCaptains` as today.
11. Placement (existing). Gate `Placement`.

Among captains with free slots, prefer in this order:
1. The requested captain.
2. Existing routing preferences.
3. The captain with the **fewest active missions**, then the earliest-idle one. This spreads load across captains rather than stacking it on one.

### 6.2 Admission lock (D8a)

- Add `VesselAdmissionLocks`, a `ConcurrentDictionary<string, SemaphoreSlim>` keyed by vessel id, in `MissionService` or a small new `VesselAdmissionLockRegistry`.
- `TryAssignAsync` takes the vessel's lock **after** steps 1-5 and holds it through steps 6-11, `TryClaimSlotAsync`, and the `TryUpdateIfStatusAsync(Pending -> Assigned)` that sets `CaptainId` and `AssignedUtc`.
- It **releases the lock before** dock provisioning and process launch, which are slow. The mission is already Assigned by then, so later evaluations count it.
- If the claim succeeds but the status update fails, release the captain slot before releasing the lock.
- Wait for the lock up to `AdmissionLockTimeoutSeconds`. On timeout, return "not assigned", log a warning and increment `armada.admission.lock_timeouts`. The next tick retries. Never block the tick indefinitely.
- Lock ordering: only one vessel lock is held at a time, and no other lock is taken while holding it, apart from the DB transaction. This rules out lock-order deadlocks. Document it in code with a comment at the registry.
- The global, endpoint and runtime caps span vessels, so they could still be raced by two vessels admitting at once. Close that with a single process-wide `CapacityLock`, taken **inside** the vessel lock and only around steps 9-10 plus the claim. Fixed order (vessel lock, then capacity lock) keeps this deadlock-free. Endpoint and runtime caps are enforced only under this lock; there is no SQL guard for them, which is acceptable because there is one Admiral process.

### 6.3 Capacity checks elsewhere
- `AdmiralService.HasAvailableCapacityAsync` (`:2209-2236`): count active missions instead of Working captains, and replace "any Idle captain" with "any captain with a free slot".
- `ApplyCapacityLimitsAsync` (`MissionService.cs:1049-1149`): the endpoint and runtime counts become sums of `ActiveMissionCount`, or counts of active missions grouped by the captain's endpoint key.
- `FindAvailableCaptainAsync` (`CaptainRouting.cs:199`): list captains with free slots, not only Idle ones.
- `ActiveMissionsOnVesselAsync` (`CaptainRouting.cs:435`): keep it. Add `ActiveVoyageSlotsOnVesselAsync` (D9 and D10) and `ActiveMissionsInVoyageAsync`.

### 6.4 Making captains multi-mission (largest change)

Every place that reads or writes `CurrentMissionId`, `CurrentDockId` or `ProcessId` on a captain must take a **mission id** instead. Work through this list file by file, and use grep counts to track progress.

- **`CaptainService.ReleaseAsync(captain)`** becomes `ReleaseMissionAsync(captain, missionId, reason)`, which calls `TryReleaseSlotAsync`. Update all 9 AdmiralService call sites.
- **`RecallAsync`** (`:73`) recalls **all** active missions of the captain, or one if a mission id is passed. It reclaims each mission's dock (`mission.DockId`) rather than `captain.CurrentDockId`.
- **`TryRecoverAsync`** (`:174`) and **`FinalizeRecoveryFailureAsync`** (`:419-422`) work per mission, so recovering one mission does not reset the others.
- **`MissionService.HandleCompletionAsync(Captain)`** (`:865`) becomes `HandleCompletionAsync(Captain, string missionId)`.
- **`AdmiralService.HandleProcessExitAsync`** (`:920`) and **`HandleTerminalProcessExitFailureAsync`** (`:2534`) already receive the mission id. Make sure they use it, not `captain.CurrentMissionId`.
- **Stall detection** (`AdmiralService.cs:1654-1730`) moves from the captain heartbeat to each mission's last output and heartbeat. A stalled mission is handled alone. The captain becomes `Stalled` only when **all** of its active missions are stalled, and `ReleaseRecoveredStalledCaptainsAsync` (`:1190`) follows the same rule. Add `Mission.LastHeartbeatUtc` if output timestamps are not enough; that would be a later migration after 121, so decide during Phase 2.
- **`RecoverOrphanedMissionsAsync`** (`:1970`): a mission is orphaned when its captain is not running it, meaning its process is gone. The test is no longer "the captain's current mission is something else".
- **`ReclaimDockAsync`** (`:2243`) and **`MaintainCaptainPoolAsync`** (`:2737`) must handle several docks per captain.
- **`AgentLifecycleHandler`**:
  - The launch key (`captain.Id + ":" + mission.Id`, `:955`) is already per mission.
  - Replace the single pointer file `logs/captains/{captainId}.current` (`:1003`) with `logs/captains/{captainId}/active/{missionId}` marker files, and update the readers.
  - Restore and reattach (`:892`) iterates over the captain's active missions using `mission.ProcessId`.
- **PlanningSessionCoordinator and ObjectiveRefinementCoordinator** (4 uses each): planning and refinement stay exclusive (D2). They claim a captain only when `ActiveMissionCount == 0` and set Planning or Refining. They move to `CurrentSessionId` with `TryClaimExclusiveAsync` and `TryReleaseExclusiveAsync` (section 5).
- **`AdmiralFleetActionMissionDispatcher`** and **`CaptainRuntimeToolCatalogService`**: switch them to the mission id.
- **Harbor split mode**: confirm that `HARBOR_PROTOCOL.md` launch and exit messages are keyed by mission, not by captain. If any are keyed by captain, add `missionId` to the message and bump the protocol minor version, with a compatibility note in `docs/COMPATIBILITY.md`.
- **Same-captain worktrees**: two missions on one captain must get two docks. Verify that `DockService` never reuses the captain's dock path for a second mission.

### 6.5 Deadlock and starvation analysis (put this in `SCHEDULING.md`)

- **Mission and captain slots have no hold-and-wait.** A slot is claimed atomically together with the transition to Assigned. Pending missions hold nothing. A running mission waits on nothing else in Armada. So no cycle can form through these slots.
- **Voyage slots are sticky (D9), so they can hold and wait.** Voyage V holds its slot while its next stage waits for a captain. That wait can only be on captain or global capacity, and that capacity is released by missions that are already running. Those missions never wait on V's slot, so there is no cycle.
- **Starvation is possible.** A voyage waiting for human review, or stuck in a landing failure, keeps its slot indefinitely. Three mitigations:
  1. The blocker summary names the holding voyage, its current state and what it is waiting for (for example "Review", "LandingFailed").
  2. The starvation event and metric (D19).
  3. `LandingFailed` and `Blocked` missions do **not** count as non-terminal for D9. A voyage whose only unfinished missions are in those states gives up its slot. That makes the slot "resumable": once fixed, the voyage re-acquires a slot through the normal fairness order. Put this rule in the shared `VoyageSlotPolicy`.
- **Integration barrier**: an integration mission waits for all workers in its voyage while it is Pending, so it holds no mission slot, and the workers can always get mission slots. There is one trap: `MaxParallelMissionsPerVoyage = 1` combined with a pending integration mission that sorts first. That mission is not eligible, so fairness ordering must consider **eligible** missions only. Write a test for this case.
- **Hierarchy**: a child is Pending until its parent's chain finishes. With `MaxParallelMissionsPerVoyage = 1`, a voyage of A -> B and D runs A and D one after another, then B. Ordering considers eligible missions only, so a Pending child never blocks its sibling.
- **Broad scope combined with a sticky voyage slot**: a broad-scope mission in voyage W waits for the vessel to have no active missions. Another voyage V holding a slot without running anything does not block it, because broad scope checks active missions, not voyage slots. If W has no voyage slot, it waits at step 6 like any other mission.
- **Lowering limits** (D20): the system only stops admitting until counts fall under the new limit, so it cannot deadlock.
- **Self-healing invariant monitor**, run every tick from `AdmiralService.HealthCheckAsync` (new `ConcurrencyInvariantMonitor`):
  - Reconcile `active_mission_count` (section 5).
  - For each vessel, compute active voyages and the active missions per voyage. For each captain, compute active missions against its limit.
  - On any excess: log a warning with the ids, emit `concurrency.limit_exceeded`, and increment `armada.concurrency.limit_violations`. A positive count is a bug unless it was caused by D20 (a limit lowered while missions ran); tag those with `cause=limit_lowered`.
  - Report a mission that has been eligible with all capacity gates open for more than 2 ticks without being assigned as `armada.admission.stuck`. That is the livelock detector.

### 6.6 Blocker summaries and advice (`GetAssignmentBlockerAsync`)

Clicking a Pending mission already shows why it is waiting. Every capacity blocker must also say **how to change the limit that is holding it**. Add `List<AssignmentBlockerAdvice> Advice` to the blocker result, where each item is `{ Text, Action, TargetType, TargetId, Setting, CurrentValue, SuggestedValue }`. `Action` is one of `EditVessel`, `EditCaptain`, `AddCaptain`, `EditSettings`, `EditEndpoint`, `OpenMission` or `OpenVoyage`. The evaluator builds the advice from the same numbers it used to decide, so the text always matches the real limit.

| Reason | Example summary | Advice (one item per line) |
|---|---|---|
| `VesselVoyageLimit` | "Vessel Armada runs 1 voyage at a time; voyage 'Auth rework' (vyg_...) holds the slot and is waiting for Review." | "Review voyage 'Auth rework' to free the slot" (`OpenVoyage`). "Raise Max parallel voyages on Armada from 1 to 2" (`EditVessel`, `MaxParallelVoyages`, 1 -> 2). |
| `VoyageMissionLimit` | "Voyage 'Auth rework' already has 4 of 4 missions running." | "Raise Max parallel missions per voyage on Armada from 4 to 5" (`EditVessel`). |
| `CaptainConcurrencyLimit` | "All 2 eligible Worker captains are full (claude-1 1/1, claude-2 1/1)." | "Add a Worker captain" (`AddCaptain`). For endpoint-backed captains only: "Raise Max concurrency on mux-1 from 4 to 5" (`EditCaptain`). If the captain is capped by the server: "mux-1 is capped at 4 by the server's Max captain concurrency" (`EditSettings`). |
| `EndpointSlot` / `RuntimeSlot` / `GlobalSlot` | "Endpoint http://gpu-1:8000 is running 3 of 3 missions." | "Raise Max concurrent missions on the endpoint" (`EditEndpoint`), or the runtime or global setting (`EditSettings`). |
| `ParentNotFinished` | "Waiting for parent mission 'Add schema' (msn_...) to finish its pipeline (currently Testing)." | "Open parent" (`OpenMission`). No limit advice: this is not a capacity wait. |
| `NoIdleCaptain` | Kept for exclusive states (all captains are Planning or Refining). | "Wait for the planning session to end, or add a captain" (`AddCaptain`). |

Rules for advice:
- Suggest the **smallest** change that admits this mission (current + 1), never "unlimited".
- Do not suggest raising a CLI captain's `MaxConcurrency` above 1; suggest adding a captain instead, because one CLI harness process runs one mission.
- When a limit was lowered while missions ran (D20), say so: "4 running, limit lowered to 2; new missions start when fewer than 2 run."
- Only advise changes the caller is allowed to make. Users who are not admins get the text without the action link.

Update the Dashboard `components/missions/AssignmentBlockerCard.tsx` and the TUI and Mobile equivalents to show the summary, then each advice item with its action as a link or button that opens the right form with the field focused.

## 7. Mission hierarchy

### 7.1 Creating and validating
- `MissionService.CreateAsync` and `UpdateAsync` run the D14 normalization (stage id to root) and the D17 checks: same voyage or vessel, no cycle, depth at most `MaxHierarchyDepth`. Return validation errors as 400 over REST and as tool errors over MCP.
- Set `Depth` from the parent. On reparent, recompute the depth of the whole subtree.
- `ParentMissionId` can only change while the mission and all of its descendants are Pending.
- `MissionDescription` (`Models/MissionDescription.cs`) gets `string? Key` and `string? ParentKey`. A single `dispatch` call can then describe a tree: keys are local to the request, and the server resolves them to ids. Also add `string? ParentMissionId`, for attaching new work to an existing mission.
- In `AdmiralService.DispatchVoyageAsync` (pipeline chaining at `:370-391`), create the stages for each description as today. Set the root's `ParentMissionId` and copy it onto the stage missions (D14).

### 7.2 Scheduling
- Gate 2 of section 6.1: find the parent's terminal stage with the existing `FindTerminalPipelineStage`, then apply the satisfied-by rules of `IsPipelineHandoffPrepared`.
- Resolve `BranchStrategy` (D16) when the child's root is created; recompute it if a sibling is added or removed while all of them are Pending.
- `ContinueParent`: when the child's root is assigned, set its `BranchName` to the parent's terminal-stage `BranchName`. This is the same continuation `MissionService.Handoff.cs:217` does for stages today, so reuse that code path.
- `ForkFromParent`: set `BaseBranchName` to the parent's terminal-stage `BranchName`. `BuildMissionBranchName` (`MissionService.cs:626-641`) creates the child's own branch, and the dock checks out from `BaseBranchName` instead of the vessel's default branch. Check `DockService` and the Harbor dock provisioning path for the place where the start point is chosen.
- In both cases the `BranchLock` gate (step 8 of 6.1) keeps a second mission off a branch while one is running on it.

### 7.3 Propagation (D18)
- Extend the dependent-cancellation code at `MissionService.Handoff.cs:22` and `:285` to walk descendants by `ParentMissionId`, breadth-first.
- Restarting a parent (`restart_mission`, REST restart, Helm, Dashboard, TUI, Mobile) always resets its descendants that were cancelled with `ParentFailed` back to Pending (D18). There is no opt-out flag. The restart response lists the re-pended mission ids.

### 7.4 Landing
- When the merge queue (`MergeQueueService.cs`) processes an entry, it checks the parent's entry. If the parent's terminal stage has not landed (`Complete`), the child's entry waits with status reason `WaitingForParentLanding`.
- A parent that fails to land is handled by D18.
- `ContinueParent` children share the parent's branch. The parent's entry must land **the commit that the parent's handoff produced**, not the moving branch head, or it would carry the child's unfinished work. Check how `MergeQueueService` picks the ref to land; if it uses the branch head, record the head SHA on the entry at handoff and land that SHA. The child's entry then lands the rest of the branch.
- If the vessel's landing mode batches entries at voyage end, keep that behavior; the ordering rule above still applies inside the batch.
- Siblings land in normal queue order. Parallel siblings never share a branch (D16), so the queue resolves conflicts between them as it does today.

### 7.5 Rescue
- `MissionRecoveryCoordinator.cs:254, 270, 287`: use `RescueOfMissionId` instead of `ParentMissionId`.
- The "no rescue for missions with a parent" rule at `:254` becomes "no rescue of a rescue" (`IsRescue || RescueOfMissionId != null`). Decide separately whether a hierarchy child can be rescued. Recommendation: allow it only when the child is a standalone root without stages; the existing persona and DependsOn rules already cover pipelines.

### 7.6 Surfacing the tree
- New REST endpoint `GET /api/v1/voyages/{id}/tree`. It returns `MissionTreeNode { Mission (summary), Stages[], Children[] }`.
- New MCP tool `voyage_tree`. Also add `parentMissionId` and `rootsOnly` filters to `enumerate` for missions (`McpEnumerateTools.cs:237`).
- The Dashboard `pages/VoyageDetail.tsx` gets a tree view: an indented list with stage pills and live status, plus "running / limit" badges for the voyage. `MissionDetail.tsx` (`:854` already links to the parent) gets a Children list and a breadcrumb.
- The TUI `Operations/MissionScreen.cs` (`:141, 420` already show the parent) and the voyage screen get an indented tree.
- Mobile: `operations/VoyageDetail.tsx` and `operations/MissionDetail.tsx` get the same.

## 8. Telemetry and metrics

### 8.1 Recording wait time
- In `MissionAdmissionEvaluator`, every evaluation of a Pending mission produces a `Gate`. A new `MissionWaitTracker` keeps one open interval per mission:
  - When the gate changes, it closes the open interval (`ended_utc = now`) and opens a new one.
  - When the mission is assigned or leaves Pending, it closes the interval.
- Accuracy is one scheduler tick (`HeartbeatIntervalSeconds`) plus the direct dispatch calls. State this in the docs.
- `ReadyUtc` is set the first time a mission's gate is a **capacity gate**: `VesselVoyageSlot`, `VoyageMissionSlot`, `BranchLock`, `GlobalSlot`, `CaptainSlot`, `EndpointSlot`, `RuntimeSlot` or `Placement`, or the first time it is admitted.
- When the mission is assigned:
  - Set `AssignedUtc`.
  - Record **slot wait** (`AssignedUtc - ReadyUtc`) and **total queue time** (`AssignedUtc - CreatedUtc`, or `- LastPendingUtc` after a restart).
  - Record the per-gate durations from the closed intervals.
- After a server restart, close the intervals that were left open at the restart time with `detail = "admiral restart"`, and open fresh ones on the next evaluation.

### 8.2 Instruments (add to `src/Armada.Core/ArmadaMetrics.cs`)

| Instrument | Type | Unit | Labels |
|---|---|---|---|
| `armada.missions.slot_wait_seconds` | histogram | s | `gate_last` (the last capacity gate before assignment), `persona` |
| `armada.missions.queue_seconds` | histogram | s | `persona` |
| `armada.missions.gate_wait_seconds` | histogram | s | `gate` (one observation per closed interval) |
| `armada.missions.waiting` | observable gauge | - | `gate` |
| `armada.missions.active` | observable gauge | - | `runtime` |
| `armada.captains.slots_in_use` / `armada.captains.slots_total` | observable gauges | - | `runtime` |
| `armada.vessels.voyage_slots_in_use` / `armada.vessels.voyage_slots_total` | observable gauges | - | none (summed across vessels; 0 = unlimited is excluded from the total and counted in `armada.vessels.unlimited`) |
| `armada.admission.lock_wait_seconds` | histogram | s | `lock` (`vessel`, `capacity`) |
| `armada.admission.lock_timeouts` | counter | - | `lock` |
| `armada.admission.stuck` | counter | - | `gate` |
| `armada.concurrency.limit_violations` | counter | - | `limit` (`captain`, `voyage_missions`, `vessel_voyages`, `global`, `endpoint`), `cause` |
| `armada.missions.starving` | counter | - | `gate` |

Rules:
- Follow `docs/TELEMETRY.md`: low-cardinality labels only, no vessel, captain or mission ids.
- Histogram buckets: 1, 5, 15, 30, 60, 120, 300, 600, 1800, 3600, 7200 seconds. Configure them in `ArmadaTelemetryHost.cs` with a view.
- Observable gauges read a cached snapshot that the invariant monitor refreshes each tick. They must not query the DB in the callback.
- Add the new instruments to the not-null assertions in `SettingsSuite.cs` (~`:535`).

### 8.3 Per-entity detail: REST and UI, not Prometheus

Per-vessel, per-captain and per-mission numbers are served by Armada:
- `GET /api/v1/vessels/{id}/concurrency`: voyage slots and mission slots per voyage, both in-use and limit, the holding voyages, the waiting missions with their gate and wait so far, and p50/p95 slot wait over 24h and 7d computed from `mission_wait_intervals`. Also exposed as MCP tool `vessel_concurrency`.
- `GET /api/v1/captains/{id}`: `ActiveMissionIds`, `ActiveMissionCount` and `MaxConcurrency`.
- `GET /api/v1/missions/{id}/waits`: the wait intervals, also on the `get_mission` MCP response when `includeWaits = true`.
- `GET /api/v1/metrics/concurrency?from&to&vesselId&captainId`: aggregates for the dashboard charts.

### 8.4 Grafana and Prometheus
- New `docker/armada/observability/grafana/dashboards/armada-concurrency.json`, provisioned by the existing `dashboards.yaml`. Panels:
  - slot wait p50/p95/p99 over time, from `histogram_quantile` on `armada_missions_slot_wait_seconds_bucket`
  - wait by gate (stacked)
  - missions waiting by gate
  - captain slot utilization (`slots_in_use / slots_total` by runtime)
  - vessel voyage slot utilization
  - admission lock wait p95 and lock timeouts
  - limit violations (should be 0; red when it is not)
  - stuck and starving counters
- Add a "Mission slot wait p95" stat panel to `armada-fleet-operations.json`, and limit violations to `armada-reliability.json`.
- `prometheus.yml`: no new scrape job is needed (it already scrapes `armada-server:9464`). Optionally add recording rules for the p95 series.
- Add alert examples to `TELEMETRY.md`: `armada_concurrency_limit_violations_total` increasing, and slot wait p95 above 30m for 1h.

### 8.5 Armada UI
- The Dashboard Missions list and MissionDetail show "Waited 12m for a captain slot (Voyage mission slot 3m, Captain slot 9m)".
- The Dashboard VesselDetail gets a "Concurrency" card with slot usage and wait percentiles.
- CaptainDetail shows its active missions and slot usage.
- The existing `TokenUsage.tsx`-style charts page gets a "Concurrency" tab using `/metrics/concurrency`.
- The TUI and Mobile get the per-mission wait line and the vessel and captain slot usage. Mobile does not need charts in v1.3.0.

## 9. API, SDK, CLI and Postman

| Surface | Change |
|---|---|
| REST (`src/Armada.Server/Routes/`) | Vessel create, update and read gain `MaxParallelVoyages` and `MaxParallelMissionsPerVoyage`; `AllowConcurrentMissions` is removed; a request that sends it gets a 400 naming the new fields (D7). Captain gains `MaxConcurrency` (optional on create; the runtime default applies) and read-only `ActiveMissionCount`, `ActiveMissionIds` and `CurrentSessionId`; `CurrentMissionId` and `CurrentDockId` are removed (D3). Mission gains `RescueOfMissionId`, `BaseBranchName`, `Depth`, `ReadyUtc` and `AssignedUtc`, all read-only, and `BranchStrategy`; `ParentMissionId` is validated. Dispatch accepts `Key`, `ParentKey`, `ParentMissionId` and `BranchStrategy` on each mission description. The blocker result gains `Advice` (6.6). Plus the new endpoints in 7.6 and 8.3. Validation errors use the existing error shape. Concurrency values are clamped, not rejected (section 4.7), and every create or update response returns the effective value. The captain DTO also gains read-only `EffectiveMaxConcurrency`. |
| MCP (`src/Armada.Server/Mcp/Tools/`) | `add_vessel` and `update_vessel` (McpVesselTools plus `VesselAddArgs` and `VesselUpdateArgs`), `create_captain` and `update_captain`, `create_mission` and `update_mission` (`MissionUpdateArgs.cs:51`, `EntityUpdateMerger.cs:84`), `dispatch`, and the `enumerate` filters (`maxConcurrency`, `parentMissionId`, `rootsOnly`, `waitingGate`). New tools: `voyage_tree` and `vessel_concurrency`. Remove `allowConcurrentMissions` from the vessel args; sending it returns a tool error naming the new fields. `restart_mission` re-pends descendants (7.3) and lists them in its result. |
| WebSocket (`docs/WEBSOCKET_API.md`) | `mission.changed` and `captain.changed` carry the new fields. New tenant-scoped events: `mission.waiting_gate_changed` (throttled to one per mission per gate change), `concurrency.limit_exceeded` and `mission.starving`. Do not add commands. |
| .NET SDK (`src/Armada.Client`) | The models are shared from Armada.Core, so the fields come for free. Add methods: `GetVoyageTreeAsync`, `GetVesselConcurrencyAsync`, `GetMissionWaitsAsync`, `GetConcurrencyMetricsAsync`. Remove the deleted properties from any client-only DTOs. Also update `src/Armada.Core/Client/ArmadaApiClient.cs` if it duplicates these. |
| Helm CLI (`src/Armada.Helm`) | `vessel add` and `vessel update` gain `--max-parallel-voyages` and `--max-parallel-missions`; `--allow-concurrent` is removed and prints an error naming the new flags. `captain add` and `captain update` gain `--max-concurrency`. `mission create` gains `--parent`. New commands `voyage tree <id>` and `vessel concurrency <id>`. The captain list shows `active/max`. |
| Postman (`Armada.postman_collection.json`) | Update the Create and Update Vessel and Captain bodies with the new PascalCase fields. Add requests: Get Voyage Tree, Get Vessel Concurrency, Get Mission Waits, Get Concurrency Metrics, and a "Dispatch Hierarchical Voyage" example using `Key` and `ParentKey`. Add a walkthrough step "5. Inspect concurrency". |
| API surface baseline | Run `scripts/common/generate-api-surface.sh` to regenerate `docs/api-surface-1.0.json` and `docs/API_SURFACE_1.0.md`. The `E2E.ApiContract` test fails until this is done. v1.3.0 makes three **intentional removals** (`Vessel.AllowConcurrentMissions`, `Captain.CurrentMissionId`, `Captain.CurrentDockId`). Record them in the baseline's breaking-changes list (or however `E2E.ApiContract` accepts approved removals; check the script) and in `docs/COMPATIBILITY.md`. Everything else is additive. |
| TypeScript types | `src/Armada.Dashboard/src/types/models.ts` and `src/Armada.Mobile/src/types/models.ts`. |

## 10. Prompts: smaller, hierarchical, cleanly separated missions

### 10.1 Architect plan contract v4

`Models/ArchitectPlanMission.cs` gets:
- `parent` (`int?`, the 1-based index of an **earlier** mission in the plan). This replaces `dependsOn` for hierarchy. Models and custom templates will keep emitting `dependsOn` for a while, so the parser still **reads** it in v3 and v4 plans and maps it to `parent` with a warning. That is input tolerance in the parser, not a public API field.
- `ownedPaths` (`string[]`): the files or directories this mission may change. A trailing `/` means a directory prefix, `*` is allowed.
- `readsPaths` (`string[]`, optional): context the mission needs but must not change.
- `branchStrategy` (optional, `continue` or `fork`): overrides the D16 default rule.
- `waitForOtherMissions` stays (the integration barrier).

`ArchitectPlanParser` (`ValidateContract` `:120`, `Normalize` `:259`) changes:
- Accept `contractVersion` 3 or 4.
- **Reject** a v4 plan when two missions that could run in parallel, meaning neither is an ancestor of the other, have overlapping `ownedPaths`. The repair path then asks the model to fix it, with a message such as "Missions 2 and 4 can run in parallel but both own src/Foo.cs; make one the parent of the other, merge them, or split ownership." For v3, log a warning only.
- Reject a `parent` that refers to itself or a later mission, and reject depth greater than `MaxHierarchyDepth`.
- Copy `parent`, `ownedPaths`, `readsPaths` and `branchStrategy` through `Normalize`, which today copies only title, description, dependsOn and waitForOtherMissions.

`MissionService.ArchitectPlan.cs` changes:
- `ApplyArchitectMissionDependenciesAsync` (`:85-160`) sets `ParentMissionId` to the parent slice's root and stops repointing `DependsOnMissionId`. Branches follow D16: an only child keeps continuing its parent's branch, as a `dependsOn` slice does today, and later siblings fork.
- Store `ownedPaths` and `readsPaths` on the mission in a new `Mission.OwnedPathsJson` and `ReadsPathsJson`. This is migration 119, so add the columns there.
- The legacy `[ARMADA:MISSION]` markers gain an optional `Parent: <n>` and `Owns: a, b`.

### 10.2 Prompt text changes

| Where | Change |
|---|---|
| `src/Armada.Core/Prompts/persona-briefs.json` `persona.architect` | Add **v6**. Keep v1-v5 for upgrades, register the new file in `Armada.Core.csproj:8-14`, and update the upgrade map in `PersonaPromptImprovements.cs` and `PromptTemplateService.cs:433`. The new text is in 10.3. |
| `PromptTemplateService.cs:844-909` (legacy `persona.architect` default) | Align it with v6: `parent`, `ownedPaths`, the guidance in 10.3 and the v4 example block. |
| `ArchitectPlanParser.FormatInstructions()` (`:202-213`) | Contract v4 paragraph, including `parent` and `ownedPaths`. This text is also used by the output-repair path. |
| `MissionPromptBuilder.cs:269, 291, 316, 344, 364` and `MissionService.Instructions.cs:803` | Fallback architect prompts: the same rules in short form. |
| `mission.merge_conflict_avoidance` (`PromptTemplateService.cs:726-762`, fallback `MissionService.Instructions.cs:759`, injected at `:210`) | New placeholders: `{OwnedPaths}` (or "the files named in the description" when empty), `{ConcurrentSiblingPaths}` (paths owned by missions in this voyage that are running or may run at the same time) and `{ParentSummary}` (what the parent changed, from its handoff). New text: "Change only your owned paths. These paths belong to missions running in parallel; do not change them even if it looks helpful: ... If you cannot finish without changing them, stop and report it as a blocker." Bump the template version so untouched templates are upgraded. |
| `ask.system` (`_AskSystemDispatchDefault`, `PromptTemplateService.cs:195`) | Replace "Split large requests into several focused missions" with guidance to split into small missions, use `ParentKey` only where a mission needs another's code, give parallel missions disjoint files and list them in each description, and check the vessel's limits with `vessel_concurrency` so a request is not split into more parallel missions than can run. Keep the older defaults for upgrades. |
| `src/Armada.Server/PlanningSessionCoordinator.cs:480-490, 1120-1123, 380-400` | The dispatch draft JSON becomes `missions: [{key, parentKey, title, description, ownedPaths}]`. A single-mission draft stays valid. `DispatchAsync` builds several `MissionDescription`s with `Key` and `ParentKey`. |
| `ObjectiveRefinementCoordinator.cs:330-340, 816` | Optional `suggestedSlices` (titles plus a parent index) in the summary, offered as a starting point for planning. No dispatch from refinement. |
| `persona.worker` brief | One sentence: "Your mission may run alongside sibling missions; stay inside your owned paths." |

### 10.3 Guidance text for the architect (draft for v6)

> Prefer small missions: one coherent change, usually 1-5 files, that a single agent can finish and verify. Use a parent/child relationship **only** when a mission needs code that another mission produces (a child starts after its parent finishes and branches from the parent's result). Missions that do not depend on each other must be siblings so they can run in parallel. Keep the tree shallow: most plans are flat or one level deep, and depth above 3 is almost always wrong. Give every mission `ownedPaths`. Two missions that can run at the same time (neither an ancestor of the other) must never own the same file. Shared files such as project files, barrel/index exports, lockfiles, migrations registries and changelogs belong to one mission, usually a parent or the final integration mission. Do not split a change so finely that siblings must edit the same file. The vessel runs at most {MaxParallelMissionsPerVoyage} missions of this voyage at once; more parallel siblings than that just queue.

`{MaxParallelMissionsPerVoyage}` is a new placeholder for the architect template, resolved from the vessel. Document it in `docs/MESSAGE_TEMPLATES.md`.

### 10.4 Checking ownership after the fact (warning only in v1.3.0)
- When a stage outcome is recorded, compare the mission's diff file list with its `ownedPaths` and with the paths owned by siblings that overlapped it in time.
- Record `OwnershipViolation` as a suspected cause on the stage outcome (see the stage outcome and failure-cause rules work in CHANGELOG v1.3.0), and show it on the Judge's input.
- It does not fail the mission in v1.3.0.

## 11. UI

| Client | Files | Change |
|---|---|---|
| Dashboard vessel | `components/vessels/VesselFormModal.tsx`, `lib/vesselForm.ts` (+ test), `pages/VesselDetail.tsx`, `pages/Vessels.tsx`, `components/SetupWizard.tsx`, `lib/setupWizard.ts`, `duplicates.ts` | Replace the checkbox with two number inputs: "Max parallel voyages" and "Max parallel missions per voyage", where 0 = unlimited and an "Unlimited" toggle sets 0. Helper text: "Each running mission uses one captain slot. Total parallelism is limited by captain slots, endpoint and global caps." Show an inline hint when the eligible captain slots for the vessel are fewer than the configured parallelism. The Vessels list shows `1 x 4`. |
| Dashboard captain | `lib/captainForm.ts` (+ test), `pages/Captains.tsx`, `pages/CaptainDetail.tsx`, `components/captains/MuxRuntimeFields.tsx` | A "Max concurrency" number input whose default follows the runtime (suggested again when the runtime changes, unless the user edited it). Helper: "How many missions this captain runs at once. CLI harnesses usually 1; endpoint-backed captains can run several." The list shows `active/max`. Detail lists the active missions. Replace every read of `currentMissionId` and `currentDockId` with `activeMissionIds` (the fields no longer exist). |
| Dashboard missions and voyages | `pages/VoyageDetail.tsx`, `lib/voyageDetail.ts`, `VoyageCreate.tsx`, `lib/voyageForm.ts`, `pages/MissionDetail.tsx`, `pages/Missions.tsx`, `pages/MissionsHub.tsx`, `components/missions/AssignmentBlockerCard.tsx` | Tree view with a branch marker per mission (continues parent / forks), a parent picker in VoyageCreate (add a child to a mission row), wait-time lines, and the new blocker reasons with their advice actions (6.6). |
| TUI | `Screens/Build/VesselForm.cs`, `Build/VesselScreen.cs`, `Build/CaptainForm.cs`, `Build/CaptainScreen.cs`, `Operations/MissionScreen.cs`, `Admin/SetupWizardScreen.cs` | The same fields, an indented tree, wait lines and blocker text. Update the `docs/tui-screens` captures if they are checked against. |
| Mobile | `screens/vessels/VesselForm.tsx`, `vessels/VesselDetailView.tsx`, `captains/CaptainFormSheet.tsx`, `captains/CaptainDetailScreen.tsx`, `operations/MissionDetail.tsx`, `operations/VoyageDetail.tsx`, `system/SetupWizard.tsx`; e2e `06-build-vessels.yaml`, `07-build-captains.yaml` | The same fields, a tree list, wait lines and blocker text. Update both e2e flows. |
| Setup wizards (all three) | | Ask "How many missions should run at once?" and offer to create N captains for a CLI runtime, or one endpoint captain with concurrency N. |

## 12. Interaction with captain usage management (CAPTAIN_USAGE_PLAN.md)
- A quota-owned quarantine (usage plan D6) blocks **new admissions** to a captain. Missions already running on it continue (D2 here).
- Several missions on one subscription account use quota faster. The usage plan's per-account view should show the captain's `ActiveMissionCount` next to its utilization.
- Captains that share an account each count their own `MaxConcurrency`. A per-account concurrency cap is **out of scope**. Record it as a follow-up idea in `archive/DECISIONS.md`.
- **Integration order:** usage management already shipped in v1.2.0. Preserve its migrations through 116, capture scheduling, host selection, and quota-owned quarantine. Reserve new migrations 117-121 for this plan. M2/M3 must integrate usage quarantine with slot admission instead of changing the archived usage plan or its shipped migrations.

## 13. Documentation

| Doc | Change |
|---|---|
| `docs/SCHEDULING.md` | Rewrite "Captain Assignment" and "Why a Mission Is Waiting": the gate order (6.1), the new blocker table, the definitions of active voyage and active mission (D9-D11), fairness, the deadlock and starvation analysis (6.5), and a **Capacity** section. Effective parallelism = min(vessel limits, captain slots of eligible captains, endpoint caps, runtime caps, global cap). Worked example: 4 Claude Code workers need 4 captains at concurrency 1, or one ApiEndpoint or Mux captain at 4. Pipelines: every running stage holds a captain slot. How to read the blocker advice (6.6). |
| `docs/PIPELINES.md` | `### Fields on Existing Models` (96), `## 6 Mission Creation and Dependency Chain` (231), `### Dependency Gating` (262), `### Handoff` (361), `### Stage-lag branch hardening` (380), `## 9 Architect Special Handling` (417-475: contract v4, `parent`, `ownedPaths`, fan-out), `## 11 Database Schema` (541). New section "Mission hierarchy": logical mission versus stage, child start rule, branches, landing order, propagation. |
| `docs/PERSONAS.md`, `docs/PERSONAS_GUIDE.md` | `4.2 Mission Dependency Chain`, `5.2 Architect Persona Template`, `Mission Context`, `Architect`, `How Missions Chain`, `The Architect Special Case`. |
| `docs/CAPTAINS.md`, `docs/CAPTAIN_ROUTING.md` | `MaxConcurrency`, defaults by runtime, slot-based routing (fewest active first), exclusive states. |
| `docs/TELEMETRY.md` | The new metrics table, the wait-tracking accuracy note, the Grafana concurrency dashboard, and alert examples. |
| `docs/REST_API.md`, `docs/MCP_API.md`, `docs/WEBSOCKET_API.md` | Fields in the Vessel (8590 and 5543), Captain (8805 and 5660) and Mission (8713 and 5613) model sections; the vessel, captain and mission endpoint and tool sections; the new endpoints, tools and events; deprecation notes. |
| `docs/API_SURFACE_1.0.md`, `docs/api-surface-1.0.json` | Regenerated. |
| `docs/MESSAGE_TEMPLATES.md` | The new placeholders (`{OwnedPaths}`, `{ConcurrentSiblingPaths}`, `{ParentSummary}`, `{MaxParallelMissionsPerVoyage}`). |
| `docs/MODEL_GUIDANCE.md`, `docs/MODEL_HETEROGENEITY.md`, `docs/TESTING_PIPELINES.md` | Hierarchy and the `armada-plan` v4 references. |
| `docs/INSTRUCTIONS_FOR_*.md` (6 files), `docs/*_AS_ORCHESTRATOR.md` (6 files) | Dispatch with `Key` and `ParentKey`, owned paths, and checking `vessel_concurrency`. |
| `docs/UPGRADING.md` (`v120-to-v130`) | Migrations 117-121; how the vessel backfill preserves behavior; that ApiEndpoint and Mux captains go to concurrency 4; the **breaking removals** (`AllowConcurrentMissions`, `CurrentMissionId`, `CurrentDockId`, `--allow-concurrent`) with before/after examples for REST, MCP, SDK and Helm; that downgrade to v1.2 needs a pre-upgrade backup; that the endpoint, runtime and global caps now count missions; how ParentMissionId moves to RescueOfMissionId. |
| `DEPRECATION.md` | Mark `AllowConcurrentMissions`, `Captain.CurrentMissionId` and `CurrentDockId` as **removed in v1.3.0**. Deprecate `VesselConcurrencyLimit` (read-only for stored events) and architect `dependsOn` (still read by the parser). |
| `archive/DECISIONS.md` | D4 (dormant `max_parallelism`), D7 and D3 (removed outright, no shim), D9 (sticky voyage slots), D12, D16 (branch reuse in a chain, fork for siblings), and the per-account cap as a rejected idea. |
| `README.md` (~420), `FAST_TRACK_SETUP.md` (~197), `GETTING_STARTED.md` | One paragraph each on slots and captains, linking to SCHEDULING.md#capacity. |
| `docs/MOBILE.md`, `docs/TUI.md` | The new fields and screens. |
| `CHANGELOG.md` v1.3.0 | A "Concurrency limits and mission hierarchy" section and a "Breaking changes" list (D3, D7). Update the release intro paragraph, which currently describes v1.3.0 as model evaluation plus usage plus mobile, to name concurrency as a cornerstone. |
| `.okf` term and architecture notes | Add: slot, voyage slot, logical mission, owned paths. |

## 14. Delivery plan (missions)

The plan's own missions follow the rules in section 10. Each mission lists its **owned paths**; missions at the same level can run in parallel. Every mission ships its own tests, so test work is not a separate mission.

```
V: Concurrency improvements
  M1 Core model, settings, migrations, DB methods
    M2 Multi-mission captains (claim/release, lifecycle, recovery)
      M3 Admission evaluator, locks, gates, blockers, invariant monitor
        M5 Wait tracking, metrics, Grafana
    M4 Hierarchy semantics (validation, parent gate, branches, landing, propagation, rescue)
      M7 Prompts and architect contract v4
    M6 API, MCP, WebSocket, SDK, Helm, Postman, API surface   (needs M1; adds M3/M4/M5 endpoints behind stubs or after them, see note)
      M8 Dashboard
      M9 TUI
      M10 Mobile
  M11 Docs, CHANGELOG, UPGRADING (last; integration mission, waits for all)
```

| Mission | Owned paths (main) | Depends on | Acceptance |
|---|---|---|---|
| M1 | `Models/{Vessel,Captain,Mission}.cs`, `Settings/ConcurrencySettings.cs`, `ArmadaSettings.cs`, `Constants.cs`, `Enums/{MissionAssignmentBlockerReasonEnum,MissionWaitGateEnum}.cs`, `Services/CaptainConcurrencyDefaults.cs`, `Services/ConcurrencyClamp.cs`, `Database/**` (migrations 117-121, new methods, readers, `IMissionWaitIntervalMethods`) | - | Migrations apply on a copy of a v1.2 DB for each driver. Backfills are correct, including `current_session_id`, before 121 drops the old columns. `AllowConcurrentMissions` is gone from the model. Every section 4.7 range is clamped in setters, `Normalize()` and DB readers, with tests. `ConcurrentAccessSuite` slot-claim test: exactly N of 50 succeed. |
| M2 | `Services/CaptainService.cs`, `Services/AdmiralService.cs` (release, stall, recovery, orphan, dock sites), `Server/AgentLifecycleHandler.cs`, `MissionService.cs` `HandleCompletionAsync`, `PlanningSessionCoordinator.cs` and `ObjectiveRefinementCoordinator.cs` (claim only), `AdmiralFleetActionMissionDispatcher.cs`, `CaptainRuntimeToolCatalogService.cs`, Harbor protocol if needed | M1 | A captain with MaxConcurrency 3 runs 3 missions with 3 docks. Completing, failing, stalling or recalling one leaves the other two running and the count correct. Restart reattaches all 3. `Captain.CurrentMissionId` and `CurrentDockId` are deleted and the solution builds; planning and refinement use `CurrentSessionId`. |
| M3 | `Services/MissionAdmissionEvaluator.cs`, `VesselAdmissionLockRegistry.cs`, `VoyageSlotPolicy.cs`, `ConcurrencyInvariantMonitor.cs`, `MissionService.cs` (`TryAssignAsync`, `GetAssignmentBlockerAsync`, `ApplyCapacityLimitsAsync`), `MissionService.CaptainRouting.cs`, `AdmiralService.HasAvailableCapacityAsync` | M2 | The scheduler test matrix in section 15 passes. The 200-mission stress test shows no limit violations and no stuck missions. |
| M4 | `MissionService.Handoff.cs`, `MissionService.ArchitectPlan.cs` (dependency wiring only), `MissionRecoveryCoordinator.cs`, `MergeQueueService.cs` (landing order, pinned SHA), `DockService.cs` (base branch), `Enums/MissionBranchStrategyEnum.cs`, `AdmiralService.DispatchVoyageAsync`, `Models/MissionDescription.cs`, `Models/MissionTreeNode.cs`, `MissionService` create/update validation | M1 (and M3 for the gate hook and the `BranchLock` interplay: M4 adds `Parent` as an evaluator step, so start it after M3 lands or coordinate on that one method) | The hierarchy tests in section 15 pass. Rescue still works and now uses `RescueOfMissionId`. |
| M5 | `ArmadaMetrics.cs`, `ArmadaTelemetryHost.cs`, `Services/MissionWaitTracker.cs`, `docker/armada/observability/**` | M3 | Metrics appear on `/metrics` in the Docker stack. The Grafana dashboard loads with no broken queries. Wait intervals are correct in the tests. |
| M6 | `src/Armada.Server/Routes/**`, `Mcp/Tools/**`, `Mcp/Args/**`, `src/Armada.Client/**`, `src/Armada.Core/Client/**`, `src/Armada.Helm/**`, `Armada.postman_collection.json`, `docs/api-surface-1.0.json`, `docs/API_SURFACE_1.0.md` | M1. Add the endpoints for M3, M4 and M5 data after those land: split M6 into M6a (fields, after M1) and M6b (new endpoints, after M3-M5) if they run in parallel. | `E2E.ApiContract` passes with the regenerated baseline. REST/MCP parity tests cover the new fields and the 400 for `AllowConcurrentMissions`. Blocker results carry `Advice`. |
| M7 | `Prompts/persona-briefs*.json`, `Armada.Core.csproj` (resource lines only), `PersonaPromptImprovements.cs`, `PromptTemplateService.cs`, `ArchitectPlanParser.cs`, `Models/ArchitectPlan*.cs`, `MissionPromptBuilder.cs`, `MissionService.Instructions.cs`, `PlanningSessionCoordinator.cs` (prompts and dispatch), `ObjectiveRefinementCoordinator.cs` (prompt) | M4 | The parser tests in section 15 pass. Prompt snapshot tests are updated. Untouched templates upgrade and edited ones do not. |
| M8, M9, M10 | Dashboard, TUI and Mobile paths in section 11 | M6 (M6b for the tree, wait and concurrency views) | `npm run test:run`, the TUI suites and the Mobile e2e flows pass. Screenshots are attached to the PR. |
| M11 | Docs in section 13, plus the ordering note in `CAPTAIN_USAGE_PLAN.md` | all | Links checked. The CHANGELOG and UPGRADING text matches the behavior that shipped, including the breaking-changes list. |

Note on shared files: `MissionService.cs` is touched by M2 (one method), M3 and M4, and `AdmiralService.cs` by M2, M3 and M4. Run M2 -> M3 -> M4 in sequence on those files, or let M4 start after M3 lands. That is why the tree shows M4 under M1 but the table makes it wait for M3's evaluator hook.

## 15. Test plan

All suites go in `src/Test.Shared/Suites/` and run with `dotnet run --project src/Test.Automated --framework net10.0 -- --suites <names>`.

### Scheduler matrix (new `Services.ConcurrencyAdmission` suite, plus extensions to `SequentialDispatchSuite`, `PipelineDispatchSuite`, `MissionAssignmentBlockerSuite`, `ResourceAdmissionSuite`, `CaptainRoutingSuite`)

| Case | Expectation |
|---|---|
| Vessel 1x1, voyage of 3 independent missions, 3 free captains | One at a time. Blocker `VoyageMissionLimit`. |
| Vessel 1x4, 2 voyages of 3 missions each, 6 captains | V1 runs 3. V2 waits with `VesselVoyageLimit` until V1 is terminal, **including between V1's pipeline stages** (sticky slot). |
| Vessel 2x2, 3 voyages | Two voyages at a time, at most 2 missions each. The third voyage admitted in FIFO order. |
| Vessel 0x0 (unlimited) | Only captain and global caps apply. Matches the old `AllowConcurrentMissions=true` (the migration 117 backfill). |
| Standalone mission next to a voyage under 1xN | They take turns for the voyage slot (D10). |
| Captain MaxConcurrency 2, 1 captain, vessel 1x4, voyage of 4 | 2 running. Blocker `CaptainConcurrencyLimit`. |
| Endpoint cap 3 shared by two captains at 4 each | At most 3 across both. |
| Global cap 2, two vessels | At most 2 total (capacity lock). |
| Limit lowered from 4 to 1 while 4 run | Nothing stops. No admission until fewer than 1 run. Violation counted with `cause=limit_lowered` only. |
| Voyage waiting in Review holds its slot | The other voyage waits, and the blocker names the holder. With `LandingFailed` the slot is released. |
| Integration barrier plus `MaxParallelMissionsPerVoyage=1` | Workers proceed, integration runs last, no stall. |
| Broad-scope mission while another voyage holds a slot but runs nothing | Broad scope proceeds once no mission is active. |
| Planning captain | Not admitted while Planning. Planning cannot start while it has active missions. |
| Quarantined captain with 2 running | Both continue. No new admissions. |
| Blocker and assignment agree | For every case above, `GetAssignmentBlockerAsync` reports the same gate that `TryAssignAsync` deferred on. |
| Blocker advice | `VesselVoyageLimit` suggests 1 -> 2 on the vessel and names the holding voyage. `CaptainConcurrencyLimit` with only CLI captains suggests adding a captain and never raising a CLI captain above 1. A server-capped captain gets `EditSettings`. Non-admin callers get no action links. |

### Race and stress
- 200 missions across 5 vessels and 10 captains with mixed limits, with `TryAssignAsync` called from 8 parallel tasks plus the tick. Afterwards the invariant monitor reports 0 violations, every mission is eventually assigned, and `admission.stuck` stays 0.
- Double release of the same mission does not decrement the count twice.
- Kill the Admiral mid-assignment (the claim succeeded, the status update did not happen). On restart, reconciliation corrects the count and the mission is assignable.
- Admission lock timeout: inject a slow DB and check that the tick still completes and the counter increments.

### Hierarchy (`Services.MissionHierarchy`, new)
- Voyage A -> B -> C plus D: A and D run in parallel, then B, then C. B and C are only children, so all three use A's branch (`ContinueParent`); D has its own branch.
- Parent P with children X and Y: X continues P's branch, Y forks from P's final head (`BaseBranchName` set). X and Y can run in parallel and never hold the same branch. An explicit `continue` on Y is downgraded to `fork` with a log line.
- Landing a `ContinueParent` chain: P's merge entry lands P's handoff commit, not the branch head with X's work on it.
- A parent whose pipeline is still at Test or Judge: its child stays Pending (`ParentNotFinished`).
- A parent fails: B and C are cancelled with `ParentFailed`, and D is unaffected. Restarting A re-pends B and C on every surface. A descendant an operator cancelled by hand stays cancelled.
- Validation: cycle, cross-voyage parent, depth over the limit, parent pointing at a stage (normalized to the root), reparenting a mission that has a running descendant (rejected).
- Merge queue: a child entry waits for the parent to land.
- Rescue: migrated rows have `RescueOfMissionId` set and `ParentMissionId` null, and a failed hierarchy child is still rescue-eligible according to the 7.5 decision.
- Dispatch with `Key` and `ParentKey` builds the tree in one call. An unknown `ParentKey` gives a 400.

### Prompts and parser (`ArchitectPlanParserSuite`, `PromptTemplateServiceSuite`, `MissionPromptSuite`)
- A v4 plan with overlapping `ownedPaths` on parallel siblings is rejected, and the repair message names both missions.
- The same overlap between a parent and its child is accepted.
- A v3 `dependsOn` maps to `parent` with a warning.
- `{ConcurrentSiblingPaths}` resolves to the running siblings' paths and excludes ancestors.
- Template upgrade: an untouched v5 architect upgrades to v6, an edited one is kept.

### Telemetry
- Use a `MeterListener` (pattern in `Tui/TuiTelemetrySuite.cs`). A mission blocked for 2 simulated ticks on `CaptainSlot`, then assigned, records one `gate_wait_seconds{gate=CaptainSlot}` observation and a `slot_wait_seconds` observation of about 2 ticks.
- Admiral restart closes the open intervals.

### Clamping (`Models.ConcurrencyClamp`, new, plus cases in the existing model and settings suites)
- Every row of the section 4.7 table: below min, min, max, above max, and `int.MinValue`/`int.MaxValue`.
- Vessel fields: `-1` becomes 1, not 0 (unlimited). `0` stays 0.
- Captain `MaxConcurrency = 0` becomes 1.
- `settings.json` with `MaxCaptainConcurrency: 300` loads as 256. `settings.json` with `MaxCaptainConcurrency: 2` and `DefaultEndpointCaptainMaxConcurrency: 4` loads, normalizes the default to 2 and logs a Warning. The server starts.
- `settings.json` with a negative global `MaxConcurrentMissions` starts and clamps to 1 (it threw before).
- Lowering `MaxCaptainConcurrency` from 256 to 2 with a captain stored at 8: the row still holds 8, `EffectiveMaxConcurrency` is 2, and admission stops at 2.
- DB: a row hand-edited to `max_parallel_voyages = -5` and `max_concurrency = 500` reads back as 1 and the effective ceiling, for each driver.
- REST and MCP: `PUT` a vessel with `MaxParallelMissionsPerVoyage: 5000` returns 200 with 1000 in the body. MCP schemas carry `minimum` and `maximum`.
- An architect plan deeper than `MaxHierarchyDepth` is rejected, not clamped.
- Dashboard `vesselForm.test.ts` and `captainForm.test.ts`: clamp on blur, and the Unlimited toggle.

### API and clients
- `E2E.ApiContract` against the regenerated baseline.
- Removed fields: a vessel create or update with `AllowConcurrentMissions` returns 400 (REST) or a tool error (MCP) naming the new fields. Captain responses no longer contain `CurrentMissionId` or `CurrentDockId`. Helm `--allow-concurrent` exits non-zero with a hint.
- A captain created without `MaxConcurrency` gets 1 for ClaudeCode and 4 for Mux.
- Dashboard: `vesselForm.test.ts` and `captainForm.test.ts` (the runtime default suggestion) plus a voyage tree rendering test.
- Mobile: e2e flows 06 and 07.

## 16. Rollout and compatibility
- The vessel backfill preserves scheduling behavior. The one scheduling change on upgrade is that ApiEndpoint and Mux captains go to concurrency 4. UPGRADING tells operators to set them back to 1 if they want the old behavior.
- The endpoint, runtime and global caps change from counting captains to counting missions. With every captain at 1 the numbers are the same, so behavior changes only together with the captain change above.
- **Breaking API changes in v1.3.0** (no deprecation period, by owner decision): `Vessel.AllowConcurrentMissions`, `Captain.CurrentMissionId`, `Captain.CurrentDockId` and Helm `--allow-concurrent` are removed. Old clients that send `AllowConcurrentMissions` get a 400 rather than having it ignored. SDK users must recompile. Call this out at the top of the v1.3.0 CHANGELOG entry.
- **Downgrading to v1.2 is not supported in place**, because migration 121 drops columns v1.2 reads. UPGRADING says to take a backup (`backup` tool or `armada backup`) before upgrading and to restore it to downgrade.
- Still deprecated rather than removed: the `VesselConcurrencyLimit` enum value (read-only, for stored events) and architect `dependsOn` (read by the parser). Revisit both in v1.4.0.

## 17. Owner decisions (resolved 2026-10-27)

All open questions are answered. Build to these.

| # | Question | Decision |
|---|---|---|
| 1 | Sticky voyage slot while waiting for review (D9) | Yes, sticky. `LandingFailed` and `Blocked` release it. |
| 2 | Standalone missions count against `MaxParallelVoyages` (D10) | Yes. |
| 3 | Existing ApiEndpoint and Mux captains go to 4 on upgrade (D5) | Yes. Called out in UPGRADING. |
| 4 | Ownership violation fails the Judge | No. Warning only in v1.3.0; revisit with stage-outcome data. |
| 5 | Per-account concurrency cap | No. Recorded as a rejected idea in `archive/DECISIONS.md`. |
| 6 | Single CLI captain with default 1x4 vessel | Acceptable. The Pending mission's blocker must say why and how to change the limit (6.6). |
| 7 | Numeric limits | `MaxCaptainConcurrency` 256 (default and ceiling), `MaxHierarchyDepth` 4, starvation warning 60 minutes. |
| 8 | Restarting a parent | Always pulls its `ParentFailed` descendants back to Pending (D18, 7.3). |
| 9 | Branches | A chain of missions may reuse one branch; parallel siblings fork (D16). |
| 10 | Old fields | `AllowConcurrentMissions`, `CurrentMissionId` and `CurrentDockId` are removed in v1.3.0, not v1.4.0 (D3, D7, section 16). |
| 11 | Order within v1.3.0 | Concurrency builds on usage management shipped in v1.2.0; preserve its migrations and scheduling (section 12). |
