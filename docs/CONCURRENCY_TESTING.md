# v1.3.0 concurrency testing

Vessel create/update/read accept `maxParallelVoyages` and `maxParallelMissionsPerVoyage`. Both are integers from 0 to 1000; 0 means unlimited. New vessels default to 1 voyage and 4 missions per voyage. Existing vessels migrate conservatively: the old disabled flag becomes 1/1, enabled becomes 0/0. During the staged migration the legacy flag remains stored, but explicit limits control admission.

Only Assigned and InProgress consume mission slots. A voyage retains admission through review, verification and landing until it is terminal or all its missions settle. Each independent standalone mission forms its own voyage group; its dependent pipeline stages share that group so review can proceed. Waiting missions expose VesselVoyageLimit or VoyageMissionLimit with the IDs holding capacity.

## Automated end-to-end check

From the repository root:

```sh
scripts/common/test-concurrency.sh -f net10.0
```

Requires the .NET SDK, Node/npm dependencies installed in `src/Armada.Dashboard`, and git. The scenario starts an isolated Admiral on temporary ports with a temporary SQLite database and a local git repository. Test captains run real, test-owned sleep processes instead of a model CLI; no provider credentials, model calls or running Armada installation are involved. The fixture stops its processes and server afterward. On Windows, build the Dashboard, then run the equivalent `dotnet run --project src/Test.Automated -c Release -f net10.0 -- --suites E2E.Concurrency` command.

The HTTP scenario proves:

1. Vessel limits survive create/read and database persistence.
2. Three independent tasks in a voyage start exactly two real processes/worktrees; the third exposes `VoyageMissionLimit` and the two holding mission IDs.
3. Cancelling one running task refills its slot without exceeding the cap.
4. Another voyage remains Pending with `VesselVoyageLimit`, even with an idle captain.
5. Updating the vessel through HTTP to admit two voyages starts the waiting voyage on the next dispatch cycle.

Service tests also submit 50 simultaneous assignments against a three-mission limit, exercise completion and redispatch, and verify sticky admission during review and terminal release. The status matrix includes verification, failed landing, blocked work, standalone pipelines and unlimited limits. Migration tests verify the old false/true flag backfills and the fresh-insert defaults.

## Test with your models in the dashboard

Build the Dashboard (`npm --prefix src/Armada.Dashboard run build`) and the solution (`dotnet build src/Armada.sln -c Release`). Run this branch's server using a separate `ARMADA_DATA_DIR` for your test settings/database. Register a test repository and at least three eligible captains with working model/runtime configuration. Captains accept up to their configured Maximum concurrent missions (CLI defaults to 1; ApiEndpoint/Mux defaults to 4).

1. In **Vessels → your vessel → Edit**, set **Parallel voyages = 1** and **Parallel missions per voyage = 2**. Save, reload, and confirm both values in the detail view.
2. Dispatch one voyage with three independent tasks using a Worker-only pipeline. Give tasks separate files/branches and avoid broad-scope wording: broad-scope work and branch ownership intentionally serialize conflicting work.
3. Confirm two tasks run and the third waits with the voyage mission limit explanation. Finish or cancel a running task; the third should start on completion or the next dispatch cycle.
4. Dispatch another voyage on that vessel. It should wait while the first voyage runs or awaits review/landing. Set **Parallel voyages = 2** to admit it on the next dispatch cycle, or finish/cancel the first voyage.
5. Try **0** for either limit to remove that specific cap. Global, runtime, endpoint, captain eligibility and branch-safety gates still apply. Lowering a limit prevents new admissions; it does not stop work already running.

New vessel defaults can be set in the `Concurrency` settings-file group. Explicit values on vessel creation override those defaults. Existing vessels keep their migrated values until edited. The legacy `AllowConcurrentMissions` field is retained during the staged migration but no longer controls admission.

## Current scope

This is a usable vessel and multi-mission captain slice of the v1.3.0 plan. Hierarchy/branch strategies, persisted wait intervals, unified admission/invariants, final legacy-field removal and Grafana changes remain in the plan. SQLite has live migration and end-to-end coverage; the other providers' migration/query implementations compile and pass migration hygiene checks, but have not been exercised against database servers in this increment.

## Multi-mission captain contract

Captain create/update/read accepts `maxConcurrency` (1..256, bounded by the server ceiling). Omitted values default to 1 for CLI runtimes and 4 for ApiEndpoint/Mux; configured defaults apply on server creation. Reads expose server-owned `activeMissionCount`, `activeMissionIds` and `currentSessionId`. Lowering capacity does not stop existing missions. Planning/refinement/analysis remain exclusive. A mission owns a durable slot keyed by mission ID; claiming also assigns the mission atomically. Releasing one slot is idempotent and preserves siblings, including during quarantine. Migration 118 adds slot storage and backfills active assignments. The singular legacy pointer fields remain compatibility surfaces until the final removal migration.


## Test one captain end to end

The same script runs `E2E.CaptainConcurrency`: one captain with capacity 3 runs three real processes in three docks while a fourth mission waits. It cancels a non-primary mission, checks duplicate cancellation, refills the vacancy, crashes another process and restarts its mission while its siblings remain alive. HTTP updates lower and restore capacity and verify that partial edits cannot spoof ownership or reset capacity. Stopping the captain recalls every assignment. `Services.AgentLifecycleHandler` additionally verifies three Harbor processes reconnect to one captain with separate output streams and durable process identities.

To exercise this with your own runtime, build/run the branch, open a vessel and set both vessel limits to Unlimited (or sufficient explicit values). Open a captain and set **Maximum concurrent missions** to 3, then dispatch four independent audit missions targeting that captain. The captain shows three active mission links and the fourth waits with `CaptainConcurrencyLimit`. Cancel one from its mission page and observe the fourth start on the next scheduler tick. Keep your actual endpoint/runtime/global caps high enough to permit three; a lower cap still takes precedence. Use an isolated test repository for coding missions. Planning/refinement cannot reserve the busy captain. Verification retains its recovery record but releases execution capacity while checks queue/run; any repair must pass admission again.

## Admission isolation and progress

`Services.SequentialDispatch` includes per-vessel isolation, bounded wait/cancellation cleanup, load distribution with explicit captain overrides, and 200 missions across five vessels and ten captains. Nine concurrent requests per wave represent eight dispatchers plus a scheduler request. Each wave checks global, voyage and captain bounds, settles admitted work and requires pending work to make progress. This supplements the real-process smoke scenarios; it is not the remaining invariant-monitor or live database-server matrix.

## Validate waiting diagnostics

1. Set a vessel's per-voyage mission limit to 2 and dispatch three independent audit tasks in one voyage. Open the Pending mission. Its waiting card should show `VoyageMissionLimit`, the holding missions and **Next steps** with a suggestion of 3 and an **Edit vessel limits** button.
2. Click the button. It opens the existing vessel edit modal under the global breadcrumb. Raise the limit, save and wait for the next scheduler cycle; the third task can start, subject to other gates.
3. Saturate a configured runtime or shared inference endpoint while a compatible captain still has capacity. The Pending mission should report the runtime/endpoint gate instead of claiming it is ready. Disable an endpoint to see the distinct unavailability explanation without a capacity-increase recommendation.
4. Lower a limit below current usage. Existing work continues, the explanation names the lower limit and the recommendation accounts for actual usage. A full CLI captain suggests adding a captain; endpoint-backed captains may offer **Edit captain capacity** within the server ceiling.
5. Read the same mission with MCP `mission_status`; its blocker and advice should match REST. Ordinary readers should see the next-step text without configuration links. Runtime caps and server captain ceilings remain settings-file configuration in this increment.

## Verification capacity and recovery

The concurrency test script includes Services.VerificationCapacity, Services.StageCheckResumeSafety and Services.ConcurrencyInvariant. With a captain limited to one concurrent mission, use a slow build/test command and let its agent exit cleanly. The mission should become Verifying, the captain should have zero occupied execution slots, and independent work should be admitted when other gates allow it. The mission detail card explains this state.

For a failed check, fill the captain or an outer capacity gate while verification finishes. Repair must wait without rerunning an already persisted check result. Free capacity and wait for a health tick: exactly one repair should start in the original dock with the check output. Repeat across an Admiral restart and confirm no extra launches. Cancel the verifying mission or recall its captain (including Recall All); late results must not restart it or release a sibling's slot. See [verification validation evidence](concurrency/VERIFICATION_CAPACITY_VALIDATION.md).

## Outer-gate and stuck-admission monitoring

Services.OuterGateInvariant and Services.AdmissionStuck are included in the concurrency script. Lower global/runtime/endpoint limits below active usage and inspect Activity > Events as a global administrator. Running work must continue; observations should reflect a known reduction or unknown ordering, without leaking aggregate usage to scoped readers. Endpoint events open the existing configuration editor. A shared URL with different alias limits remains ambiguous.

A stuck-admission warning requires the same Pending version ready before and after three sampled dispatch opportunities. Known launch/provisioning failures, unavailable placement, memory/global deferrals and ordinary capacity/dependency blockers must not warn. Large queues are covered through bounded rotating windows. See [monitor validation](concurrency/OUTER_GATE_MONITOR_VALIDATION.md).

### Captain selection parity

`Services.CaptainAdmissionParity` is part of the concurrency script alongside persona-priority, tier and Worker-envelope regressions. It compares diagnostics, monitoring and actual dispatch for unresolved strict/non-strict voyage preferences, busy defaults, priority availability, runtime limits and effective captain ceilings, and verifies read-only diagnostics plus launch-failure backoff. See [UX and regression evidence](concurrency/CAPTAIN_ADMISSION_VALIDATION.md).

To validate manually, fill an explicitly pinned captain while leaving an unrelated captain free. Open the Pending mission: its waiting card should identify captain slot saturation. Free the pinned slot and dispatch; it should use the pinned captain. Give a Working captain spare capacity but restrict it to another persona: the waiting card should explain the persona mismatch rather than imply it is available.
