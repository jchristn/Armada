# 1.5.0 upgrade test and crash-point matrix

Release verification for RS-14.3 and RS-14.4, run 2026-11-26 on macOS 26.6 (Arm64), .NET SDK 10.0.403, test runner
`Test.Automated` net10.0. Server providers: `postgres:17-alpine`, `mysql:8.4`,
`mcr.microsoft.com/mssql/server:2022-latest`, each a local container on 127.0.0.1.

## RS-14.3: upgrade from v1.4.0

Baseline: commit `a2503b7a2` ("Release v1.4.0"; the repository has no `v1.4.0` tag). The `Upgrade` suite
(`UpgradeFromBaselineSuite`) starts the v1.4.0 Admiral on a throwaway database, seeds through its API, stops it, then
migrates with the current build and verifies the database and the current Admiral's API.

Seeded data includes one mission per reachable status and, for the RS-3.1 backfill (migration 133), missions in every
text-marker state: an `## Escalated to` heading, an Architect with `<!-- armada:fanout-complete -->`, three Worker
slices with `<!-- ARMADA:ARCHITECT-HANDOFF -->` created in plan order, and a Judge that carries the handoff marker but
is not a slice. After the upgrade the suite checks escalation count 1, fan-out completion set, slice source and plan
index 0, 1, 2, the Judge left alone, an unmarked mission untouched, and every description unchanged.

| Provider | Result |
|---|---|
| SQLite | 8/8 passed |
| PostgreSQL | 8/8 passed |
| MySQL | 8/8 passed |
| SQL Server | 8/8 passed |

Suite changes made for a v1.4.0 baseline (the suite was written against v0.9.0):

- Vessels are seeded with `MaxParallelVoyages` and `MaxParallelMissionsPerVoyage` on a v1.3.0 or later baseline,
  because v1.3.0 refuses the removed `AllowConcurrentMissions` flag.
- The selection history check compares against the decisions the baseline itself recorded. A v1.4.0 baseline
  records its own captain selection decisions when it dispatches, so "no history" was wrong for it; the check still
  proves the upgrade adds none.

The SQL Server container had exited (code 137) before the run and was restarted; the run itself had no failures.

## RS-14.4: crash-point matrix (RS-1.3)

Suite `Services.CrashConsistency`, all 13 named points in `CrashPointNames` plus the injector scope case.

| Point | Case | SQLite | PostgreSQL | MySQL | SQL Server |
|---|---|---|---|---|---|
| (injector) | An armed point freezes only its own flow | Pass | Pass | Pass | Pass |
| `MergeQueuePushed` | After restart the entry is Landed and the mission Complete | Pass | Pass | Pass | Pass |
| `MergeQueueEntryLanded` | After restart the mission is Complete | Pass | Pass | Pass | Pass |
| `RecoveryIncidentCreated` | The next cycle leaves one incident with its failure kind | Pass | Pass | Pass | Pass |
| `RecoveryRescueCreated` | The next cycle adopts the rescue instead of dispatching a second | Pass | Pass | Pass | Pass |
| `VoyageCreated` | The voyage has all of its missions or none | Pass | Pass | Pass | Pass |
| `VoyageMissionCreated` | The voyage has all of its missions or none | Pass | Pass | Pass | Pass |
| `VoyageOpened` | The voyage has all of its missions or none | Pass | Pass | Pass | Pass |
| `VoyageMissionAssigned` | The voyage has all of its missions or none | Pass | Pass | Pass | Pass |
| `LaunchBeforeProcessRecorded` | The orphaned agent is found; one agent runs | Pass | Pass | Pass | Pass |
| `HandoffPlanned` | Both dependents were prepared or neither | Pass | Pass | Pass | Pass |
| `HandoffWritten` | Both dependents were prepared or neither | Pass | Pass | Pass | Pass |
| `FanOutPlanned` | All three slices were written or none | Pass | Pass | Pass | Pass |
| `FanOutWritten` | All three slices were written or none | Pass | Pass | Pass | Pass |

14/14 on every provider, none skipped. The five cases RS-1.3 recorded as skipped (RS-2.3, RS-2.5, RS-2.6 twice,
RS-4.3) now run and pass.
