# Verification capacity and restart recovery

Verification retains its exited-process assignment record, but that record no longer counts as execution capacity. ActiveMissionCount, ActiveMissionIds and the legacy primary execution pointer exclude Verifying assignments. Durable process handles remain queryable for restart replay; no schema migration is required. All four database providers use the shared transactional store and filtered captain projections. Existing standalone/branch ownership and cancellation version guards remain in place.

The exit handler releases capacity before checks queue/run. Health checks include captains with verification work even when Idle. Successful results complete from Verifying without reacquiring execution. Failed results are persisted with an exact verificationVersion and reused while waiting. Repairs pass current vessel/branch, global, runtime, endpoint, captain, eligibility and launch-policy gates before their record atomically becomes execution ownership again. Quarantine, disabling, exclusive sessions and capacity reductions prevent relaunch. Later/stale results cannot overwrite cancellation or a new attempt. Legacy pointer-only ownership adopts its captain and recovery budget only after that exact pointer was validated.

## Tests

Added Services.VerificationCapacity with 16 scenarios: durable handle retention and reconciliation, sibling admission, concurrent readmission (exactly one winner), stale ownership refusal, long checks releasing capacity, duplicate exits, five saturated repair gates, cached-result restart replay without repeated mechanical checks, idle-captain replay, recall and recall-all with zero active slots, public cancellation cleanup, cancellation while checking, stale cached evidence, exhausted recovery and preserving an exclusive session's identity/process/budget. The service scenarios use SQLite, a temporary worktree, stub git and injected command/launch callbacks; existing HTTP/git/real-process concurrency tests cover broader lifecycle behavior.

The first broad run exposed two pointer-only fixture regressions, including a recovery budget that had only been stored on the captain. The compatibility path now preserves those fields safely. The final .NET 10 regression run passes 360 cases, plus 91 captain-routing/agent-lifecycle cases. The .NET 8 compatibility run passes 344 service/database cases. These include public cancellation and recall-all after those cleanup paths were audited. The .NET 8/.NET 10 solution builds with zero warnings/errors.

The Dashboard adds a shared-style verification explanation and two tests covering its semantics and translations in all eight non-English locales. All 1,858 Dashboard tests pass, and its production build passes.

## UX/layout/aesthetic pass

A disposable local Admiral served the production Dashboard with a seeded failed, versioned check waiting on a disabled captain. Real REST reads report mission Verifying while the captain is Idle with zero active slots and an empty active-ID list. No response interception or model calls were used. Mission and captain details passed light/dark checks at 1440, 768 and 390 pixels (12 cases). The explanation wraps within its card, standard action buttons and shared breadcrumb ancestry remain aligned, and there is no document horizontal overflow. The owned fixture server was stopped after validation.

See [measurements](verification-layout-results.json). Screenshots were reviewed locally and are not committed.

## Manual validation

Use a captain with concurrency 1 and a slow stage check. When its process exits cleanly, confirm the mission becomes Verifying, the execution slot becomes available, and independent work may start. A failed check must wait if another mission fills the captain or any outer gate. Free capacity and wait for a health tick: repair should launch once in the same dock, with the failed-check output, without rerunning the cached check. Restart the Admiral during verification and while a repair is waiting; replay must retain ownership without exceeding capacity. Recall/cancel must prevent late completion or relaunch.

REST_API, MCP_API, README, CHANGELOG and the concurrency plan describe the new semantics. Runtime/endpoint/global invariant observers, stuck-admission detection, general eligibility unification, migrations/legacy-field removal and hierarchy remain open. Only SQLite was executed in this wave; the other provider implementations compile and share the transaction logic, but were not tested against live database servers.

Release command: ./build-all.sh v1.3.0; output: /tmp/armada-v130-verification-build-all.log.
