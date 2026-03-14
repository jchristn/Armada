# Armada Failure Cases Interrogation

This document is based on direct code inspection of Armada's dispatch, scheduling, docking, runtime, merge, landing, and cleanup paths in:

- `src/Armada.Core/Services/*.cs`
- `src/Armada.Server/ArmadaServer.cs`
- `src/Armada.Core/Database/Sqlite/Implementations/*.cs`
- `src/Armada.Core/Services/GitService.cs`
- `docs/SCHEDULING.md`
- `docs/MERGING.md`
- related unit and automated tests

The short version is:

1. Armada allows too much concurrent work on the same repository with too little real conflict isolation.
2. State is spread across mission rows, captain rows, dock rows, git branches/worktrees, and in-memory process maps, but updates are not transactional across those boundaries.
3. Cleanup and recovery are frequently best-effort instead of authoritative.
4. Some code paths can mark failed work as successful, or lose the references needed to recover it.
5. Merge/landing completion is not durably reconciled back into mission state in all modes.

If the symptom is "merge conflicts all the time" and "failed or half-failed states that need manual recovery", the code supports that diagnosis.

## Most Important Root Causes

These are the biggest structural reasons the system can keep ending up in broken states:

1. Parallel missions on the same vessel are mostly allowed.
   Only a weak "broad scope" text heuristic tries to stop dangerous overlap.

2. Assignment is only partially atomic.
   Captain claiming is atomic. Mission claiming is not.

3. Landing is not a durable job with a durable state machine.
   It is often a fire-and-forget callback running after the captain has already been released.

4. Docks are reused too aggressively.
   The same captain/vessel path is reused while the previous mission's background completion work may still be running.

5. Cleanup is often "best effort".
   Many exceptions are swallowed, state references are cleared anyway, and later recovery becomes guesswork.

6. Some recovery logic assumes "missing process means success".
   That can convert real failures into `WorkProduced` and reclaim the evidence.

7. Merge queue and PR flows do not reliably drive the mission to its final state.
   Missions can remain `WorkProduced` or `PullRequestOpen` indefinitely even after the work is actually landed.

## Highest-Risk Findings

### 1. Dock provisioning can delete another active captain's worktree

In `DockService.ProvisionAsync`, Armada scans the vessel's dock directory and removes every other git worktree directory except the current captain's directory. It does not check whether those other directories belong to active captains.

Effect:

- One captain provisioning a new dock can remove another captain's active worktree on the same vessel.
- That will cause missing files, failed fetches, failed pushes, phantom stalls, or corrupted recovery.

What to do:

- Stop deleting sibling dock directories blindly.
- Only reclaim docks that are inactive in the database and not referenced by any captain or mission.
- Make dock cleanup a separate reconciler, not part of normal provisioning.

### 2. Missing process can be treated as a clean success

`AdmiralService.HealthCheckCaptainAsync` treats `Process.GetProcessById` throwing `ArgumentException` as `exitCode = 0`.

Effect:

- Crashed or externally-killed agents can be interpreted as successful completions.
- Missions can be advanced to `WorkProduced`.
- Docks can be reclaimed and evidence can be deleted even though the work never succeeded.

What to do:

- Treat "process missing from process table" as unknown, not success.
- Only treat a mission as successful if Armada observed a clean process exit event or can prove the branch has new commits.

### 3. Local merge can mark the mission `Complete` even if push failed

In the local merge path, a merge into the user's working directory can succeed locally and then the push back to origin can fail. The code still treats landing as successful.

Effect:

- Mission is marked `Complete`.
- User assumes work landed remotely.
- Remote branch may still be unchanged.
- Subsequent work can diverge and conflict.

What to do:

- Split "local merge succeeded" from "remote landing succeeded".
- Only mark `Complete` after the push succeeds.
- If push fails, keep the mission in `LandingFailed` or a more precise `PushFailed` state.

### 4. Merge queue mode does not appear to complete the mission

The mission is auto-enqueued in `HandleMissionCompleteAsync`, but the merge queue processing code updates `MergeEntry` rows only. It does not reconcile the linked mission to `Complete` when the branch lands.

Effect:

- Merge queue can successfully land code while the mission remains `WorkProduced`.
- Voyages can stay open indefinitely.
- Users will see "semi-failed" or stale mission state even when the branch is landed.

What to do:

- On merge entry `Landed`, update the linked mission to `Complete`.
- On merge entry `Failed`, update the linked mission to `LandingFailed`.
- Emit events and WebSocket updates from that reconciliation step.

### 5. Pull request mode can leave missions stuck at `PullRequestOpen` forever

PR creation sets the mission to `PullRequestOpen`. Transition to `Complete` depends on a short-lived poller started only when auto-merge is enabled. Manual merge later is not durably reconciled.

Effect:

- PR merged manually or after five minutes can leave the mission stuck forever.
- Voyage completion becomes wrong.
- Cleanup policies may never run.

What to do:

- Add a persistent PR reconciliation job.
- Poll all open PR missions from durable state, not a fire-and-forget task.
- Do not stop after five minutes.

### 6. Mission completion runs in the background after the captain has already been released

`MissionService.HandleCompletionAsync` sets `WorkProduced`, queues `OnMissionComplete` in the background, releases the captain, and may immediately assign the next mission.

Effect:

- The next mission can start before the previous mission's landing and cleanup are finished.
- The same captain/vessel dock path can be reused while the prior mission is still pushing, merging, or reclaiming the dock.
- This can produce missing worktrees, push failures, and hard-to-reproduce races.

What to do:

- Either keep the captain unavailable until landing/cleanup finishes, or
- use a unique dock path per mission instead of reusing `{vessel}/{captain}`.

## Failure Catalog

The table below lists as many concrete failure modes as possible from the inspected implementation.

| ID | Area | Failure mode | Why it can happen | Likely effect | Mitigation |
|---|---|---|---|---|---|
| D1 | Dispatch | Same mission can be raced by multiple assigners | No mission-level atomic `TryClaim`; only captain claim is atomic | Duplicate dock provisioning, conflicting captain assignment attempts | Add mission CAS: `Pending -> Assigned` only if still pending |
| D2 | Dispatch | Mission row is updated before captain claim succeeds | `MissionService.TryAssignAsync` mutates mission before `Captains.TryClaimAsync` | Split-brain state if later steps fail | Put mission claim and captain claim in one DB transaction |
| D3 | Scheduling | Documented voyage priority is not fully implemented | `HandleCompletionAsync` picks pending mission by priority and created time only | Unexpected interleaving, more cross-voyage conflicts | Implement the same ordering everywhere |
| D4 | Scheduling | Same vessel work is allowed by default | Non-broad missions only log a warning | Frequent merge conflicts | Default to one active mission per vessel unless explicitly allowed |
| D5 | Scheduling | Broad-scope protection is weak | Text-matching heuristic only | False negatives and false positives | Use explicit scope metadata or file/path plans |
| D6 | Scheduling | Broad-scope check is not atomic | Active mission count and assignment happen in separate steps | Two broad or overlapping missions can still start together | Lock or transact per vessel during assignment |
| D7 | Scheduling | Broad-scope gate ignores post-work states | Only `Assigned` and `InProgress` block | A `WorkProduced`/`PullRequestOpen` branch can still conflict with new work | Treat unresolved landing states as vessel activity |
| D8 | Scheduling | Same captain is favored repeatedly | First idle captain is selected, no balancing | Uneven load, more path reuse races | Add least-recently-used or fair scheduling |
| D9 | Dispatch | Voyage creation is not transactional | Voyage and missions are created one-by-one | Partial voyages on failures | Wrap voyage creation and child mission creation in one transaction |
| D10 | Dispatch | Auto-spawn captain names can collide | Pool names are based on current count, not highest suffix | Unique key failures during autoscaling | Generate names from IDs or a monotonic sequence |
| D11 | Dispatch | Launch failure leaks dock | Launch rollback clears mission/captain but does not reclaim the new dock | Active stale dock, blocked future provisioning | Reclaim dock on launch failure and no-launch-handler failure |
| D12 | Dispatch | No launch handler leaks dock | Same as above when `OnLaunchAgent` is null | Stale dock and stale branch | Same fix as D11 |
| G1 | Docking | Provisioning can delete active sibling worktrees | Vessel dock cleanup removes all other git dirs | Active mission worktree disappears | Only clean inactive, unreferenced docks |
| G2 | Docking | Fetch failure is tolerated | `DockService` continues with stale local state | Old base branch, later merge conflicts | Fail provisioning if fetch fails for active repos, or mark degraded explicitly |
| G3 | Docking | Fallback fetch may still leave stale refs | `GitService.FetchAsync` falls back to `git fetch origin` | Worktree created from stale base branch | Verify target ref freshness before worktree add |
| G4 | Docking | Worktree directory reuse creates races | Path is `{DocksDirectory}/{vessel}/{captain}` | New mission collides with previous mission cleanup | Use unique per-mission dock paths |
| G5 | Docking | Old branch evidence can be deleted before diagnosis | Provisioning deletes stale branch if it exists | Harder recovery and forensics | Preserve failed branches until explicitly purged |
| G6 | Docking | Partial cleanup failures are swallowed | Many `catch { }` blocks | Ghost dirs, stale worktree registrations | Surface cleanup failures and track repair-needed state |
| G7 | Docking | Force directory removal can fail silently | After retries, it warns and returns | Repeated provisioning failures on leftover dirs | Mark dock unreclaimed and block reuse until repaired |
| G8 | Git | Worktree repair destroys uncommitted work | `checkout -- .` and `clean -fd` are destructive | Lost agent output during recovery | Capture branch status and diff before repair |
| G9 | Git | 120s git timeout is too low for large repos | Fixed timeout for clone/fetch/push | Spurious failures and half-created state | Make timeouts configurable and stage-specific |
| G10 | Git | Worktree creation assumes local base ref exists | No explicit validation of base branch availability | Provisioning failure on unusual repos | Resolve remote default branch and verify ref first |
| G11 | Git | Diff snapshot can be wrong | Diff is against local base branch name, not guaranteed-fresh remote base | Misleading saved diff | Diff against merge-base with refreshed remote ref |
| G12 | Git | Local merge uses user's working directory directly | No dedicated landing worktree for local merge | User checkout conflicts, dirty-tree failures | Never merge into the user's live working tree |
| G13 | Git | Local merge ignores dirty working tree preflight | No cleanliness check before merge | Merge failure, user work contamination | Require clean working tree or use a temporary integration worktree |
| R1 | Runtime | Missing process can be counted as success | PID lookup failure becomes exit code 0 | False `WorkProduced` | Treat as unknown/failure until proven otherwise |
| R2 | Runtime | Stall detection is effectively broken for live processes | Heartbeat is updated from health check when process is alive | Hung agents are never recognized as stalled | Base stall detection on agent output/progress heartbeat, not health-check heartbeat |
| R3 | Runtime | Recovery with missing mission/dock stalls captain but does not requeue work | `TryRecoverAsync` sets captain `Stalled` only | Mission remains stranded | Mark mission failed or pending with reason |
| R4 | Runtime | Recovery launch failure leaves mission active | Captain becomes `Stalled`, mission may stay `InProgress` | Manual recovery needed | Mark mission failed or pending on recovery-launch failure |
| R5 | Runtime | Stop can fail after state is already cleared | Recall removes tracking before stop completion | Stray agent keeps running | Confirm stop before clearing authoritative state |
| R6 | Runtime | Server restart loses process maps | `_ProcessToCaptain` and `_ProcessToMission` are in-memory only | Ambiguous exit handling after restart | Persist active process metadata durably |
| R7 | Runtime | PID reuse can fool startup cleanup | Process liveness is checked by raw PID | Captain may be left tied to unrelated process | Store process start time or a runtime session token |
| R8 | Runtime | Orphan recovery can complete work that never actually happened | Comment says "check if there are commits"; code does not | Bad missions become `WorkProduced` | Require commit/hash proof before completion |
| R9 | Runtime | Progress status updates are fire-and-forget | Async background task updates mission state later | Out-of-order transitions, lost signals | Serialize progress handling per mission |
| R10 | Runtime | Prompt/log setup can fail before durable rollback | Log pointer file is written before full launch completes | Broken pointers and confusing diagnostics | Finalize log pointer after launch succeeds |
| L1 | Landing | Local merge can succeed locally but fail remotely | Push failure does not fail the mission | False `Complete` | Only complete after remote push succeeds |
| L2 | Landing | Landing mode can silently degrade to manual | Local merge path requires both `WorkingDirectory` and `LocalPath` | Mission stays `WorkProduced` though landing mode requested | Fail loudly when required vessel config is missing |
| L3 | Landing | PR mode can strand missions forever | No durable PR reconciliation, short poll window | `PullRequestOpen` forever | Add a persistent PR reconciler |
| L4 | Landing | Merge queue mode does not reconcile mission outcome | Merge queue updates entries, not missions | `WorkProduced` forever | Update linked mission on queue result |
| L5 | Landing | Merge queue processing trigger can be silently dropped | `ProcessQueueAsync` returns immediately if already processing | Newly queued entries look ignored until another trigger | Queue a rerun request instead of dropping it |
| L6 | Landing | Merge queue global lock is process-local only | `_Processing` is in-memory | Multiple server instances can double-process entries | Use a DB-backed distributed lease |
| L7 | Landing | `ProcessSingleAsync` can race normal queue processing | It does not use the global queue lock | Double landing attempt on same repo/entry | Route single-entry processing through same locking path |
| L8 | Landing | Merge queue has no mission-state reconciliation | Queue status and mission status diverge | Voyages never finish correctly | Add mission linkage updates and events |
| L9 | Landing | Merge queue cancellation is too permissive | Cancel can mark any entry `Cancelled` | In-flight entry can continue after operator "cancelled" it | Prevent cancel outside `Queued` |
| L10 | Landing | Merge queue cleanup is weaker than dock cleanup | Temp worktree cleanup is only `git worktree remove` | `_merge-queue` directories can accumulate | Add forced directory removal and repair state |
| L11 | Landing | Merge/test process output can deadlock | Stdout and stderr are read sequentially in redirected mode | Hung tests or git commands | Read both streams concurrently |
| L12 | Landing | Test command quoting is fragile | Shell command string is passed through platform-specific wrappers | Misparsed commands or injection risk | Use explicit executable + args or vetted scripts |
| C1 | Cleanup | Dock reference can be cleared even when reclaim failed | `ReclaimDockAsync` clears mission/captain refs after best-effort reclaim | Lost pointer to stuck worktree | Only clear refs after confirmed reclaim |
| C2 | Cleanup | Data expiry deletes merge entries without branch cleanup | `DataExpiryService` does raw SQL delete | Remote/local branches leak forever | Expiry should call service-layer cleanup, not raw delete |
| C3 | Cleanup | Data expiry removes forensic data too early | Old failed missions/events/signals are deleted automatically | Manual recovery becomes impossible | Keep failed-state artifacts longer than successful ones |
| C4 | Cleanup | Inactive dock expiry uses `created_utc`, not inactivity age | Recently reclaimed old docks are purged immediately | Operators lose recovery context too fast | Expire by `last_update_utc` or `reclaimed_utc` |
| C5 | Cleanup | Startup stale-captain cleanup can orphan real work | It resets active missions to `Pending` and reclaims docks based on PID liveness only | Branches and worktrees may be left behind with no linkage | Reconcile against git branch state before resetting |
| C6 | Cleanup | Manual `Complete` without dock bypasses landing | Status route allows direct completion when no active dock exists | False `Complete`, branch never landed | Require explicit "force complete" command with warnings and evidence capture |
| C7 | Cleanup | Event/signal failures are swallowed | Audit creation is best-effort almost everywhere | Harder diagnosis, blind spots | Use an outbox or persistent retry for operational events |
| C8 | Cleanup | Merge lock dictionary grows forever | Per-vessel semaphores are never removed | Small memory leak over long uptime | Remove idle locks or use bounded cache |
| T1 | Testing | Most unit tests stub git heavily | Real git races and worktree corruption paths are not exercised | Bugs survive until production | Add real-repo concurrency tests |
| T2 | Testing | Merge queue automated tests mostly test API shape | They do not validate real queue-to-mission reconciliation | Critical logic gaps remain untested | Add end-to-end landing tests with real branches and real queue processing |
| T3 | Testing | Scheduling docs drift from code | Docs say voyage tie-break priority exists; core path does not consistently do that | Operators trust behavior that is not real | Make tests assert documented scheduling order |

## Why Merge Conflicts Are Likely Happening So Often

The current design naturally creates merge conflict pressure:

1. Multiple captains can work on the same vessel at once.
2. Overlap prevention is heuristic, not real.
3. Fetch failures are tolerated, so captains can branch from stale bases.
4. Local landing merges directly into the user's working directory.
5. Missions can be re-dispatched or started while prior landing/cleanup is still running.
6. Merge queue is optional and even when used does not appear to close the mission lifecycle cleanly.

That combination is enough to produce constant conflict churn even when each individual piece "usually works."

## Why Failed And Semi-Failed States Are Likely Happening

These are the strongest reasons Armada can end up needing manual recovery:

1. A mission can lose its dock reference even when reclaim failed.
2. A mission can be marked `WorkProduced` even when the agent did not truly succeed.
3. A mission can be marked `Complete` even when the remote push failed.
4. A mission can remain `WorkProduced` or `PullRequestOpen` forever after real landing.
5. A dock can leak on launch failure.
6. Active worktrees can be deleted during another captain's provisioning.
7. Recovery can destroy uncommitted work before preserving evidence.
8. Expiry can delete the state you need in order to understand what happened.

## Recommended Fix Order

If I were fixing this system, I would do it in this order:

### Immediate

1. Stop deleting sibling worktrees during provisioning.
2. Stop treating missing PID as success.
3. Reclaim docks on launch failure and no-launch-handler failure.
4. Do not mark local-merge missions `Complete` when push failed.
5. Reconcile merge queue results back to mission status.
6. Add a persistent reconciler for `PullRequestOpen` missions.

### Next

1. Introduce atomic mission claim plus captain claim in one transaction.
2. Move landing to a durable DB-backed job/state machine.
3. Stop merging directly into the user's working directory.
4. Switch docks to unique per-mission paths.
5. Base stall detection on actual captain progress, not health-check liveness.

### After That

1. Default to one active mission per vessel.
2. Add explicit mission scope metadata instead of string heuristics.
3. Make cleanup authoritative and auditable instead of best-effort.
4. Make expiry call service-layer cleanup logic.
5. Expand tests to real git repos, real conflicts, restart recovery, and concurrent queue processing.

## A Better Operational Model

Armada will be much more stable if it adopts these principles:

1. Mission assignment must be transactional across mission, captain, and dock records.
2. Landing must be durable and restart-safe.
3. No operation should clear the only reference to a worktree unless the reclaim succeeded.
4. Success states must require proof.
   Example: observed clean exit, recorded commit hash, successful landing, successful remote push.
5. Recovery must preserve evidence before mutating or cleaning anything.
6. Repository concurrency should be conservative by default.

## Bottom Line

Armada is currently doing orchestration on top of git worktrees, external runtimes, database rows, and in-memory process bookkeeping without a strong transactional boundary or durable reconciliation layer. The result is predictable:

- too many same-repo conflicts
- too many stale or contradictory states
- too much best-effort cleanup
- too much manual recovery

The system can be made much more reliable, but it needs stricter state ownership, durable landing/recovery workflows, and much more conservative repository concurrency.
