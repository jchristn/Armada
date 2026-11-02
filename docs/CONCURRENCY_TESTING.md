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

Build the Dashboard (`npm --prefix src/Armada.Dashboard run build`) and the solution (`dotnet build src/Armada.sln -c Release`). Run this branch's server using a separate `ARMADA_DATA_DIR` for your test settings/database. Register a test repository and at least three eligible captains with working model/runtime configuration. Each captain still runs one mission at a time in this increment.

1. In **Vessels → your vessel → Edit**, set **Parallel voyages = 1** and **Parallel missions per voyage = 2**. Save, reload, and confirm both values in the detail view.
2. Dispatch one voyage with three independent tasks using a Worker-only pipeline. Give tasks separate files/branches and avoid broad-scope wording: broad-scope work and branch ownership intentionally serialize conflicting work.
3. Confirm two tasks run and the third waits with the voyage mission limit explanation. Finish or cancel a running task; the third should start on completion or the next dispatch cycle.
4. Dispatch another voyage on that vessel. It should wait while the first voyage runs or awaits review/landing. Set **Parallel voyages = 2** to admit it on the next dispatch cycle, or finish/cancel the first voyage.
5. Try **0** for either limit to remove that specific cap. Global, runtime, endpoint, captain eligibility and branch-safety gates still apply. Lowering a limit prevents new admissions; it does not stop work already running.

New vessel defaults can be set in the `Concurrency` settings-file group. Explicit values on vessel creation override those defaults. Existing vessels keep their migrated values until edited. The legacy `AllowConcurrentMissions` field is retained during the staged migration but no longer controls admission.

## Current scope

This is a usable vessel admission slice of the v1.3.0 plan. A single captain still owns one running mission. Multi-slot captains, hierarchy/branch strategies, persisted wait intervals and Grafana changes remain in the plan. SQLite has live migration and end-to-end coverage; the other providers' migration/query implementations compile and pass migration hygiene checks, but have not been exercised against database servers in this increment.
