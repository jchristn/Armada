# Armada Failure Case Implementation Plan

> Consensus plan produced by Claude and Codex after independent code analysis,
> cross-critique, and debate. This plan targets the two symptoms the operator
> sees most: **merge conflicts on every other landing** and **missions stuck in
> semi-failed states that require manual recovery**.

---

## Root Cause Summary

The merge conflicts and failed states trace back to five structural issues:

1. **Concurrent missions per vessel** — `TryAssignAsync` only warns; it does not
   block. Two missions branch from the same base, edit overlapping files, and
   conflict at landing.
2. **Fire-and-forget landing** — `HandleCompletionAsync` releases the captain
   and kicks off landing in a background `Task.Run`. If landing fails, no one
   retries it. If the server restarts, the landing is lost entirely.
3. **Non-transactional state transitions** — Mission claim, captain claim, and
   dock provisioning are separate DB calls. A failure between them leaves
   orphaned state.
4. **User working directory as integration engine** — `LocalMerge` checks out
   the target branch in the user's live workspace, merges, and pushes. Dirty
   trees, wrong branches, and push failures leave the workspace in an
   unrecoverable state.
5. **Missing reconciliation loops** — Merge-queue landing updates
   `MergeEntry.Status` but never updates the linked mission. PR polling is
   ephemeral and lost on restart.

---

## Implementation Tiers

### Tier 1 — Stop the Bleeding

These are small, targeted changes that eliminate the most common conflict and
failure paths. Each item is independently deployable.

---

#### T1-1: Block same-vessel concurrency by default

**Problem:** `MissionService.TryAssignAsync` (line ~98-102) logs a warning when
a vessel already has an active mission but proceeds with assignment. This is the
single largest source of merge conflicts.

**File:** `src/Armada.Core/Services/MissionService.cs`

**Change:**

1. In `TryAssignAsync`, after querying for active missions on the vessel
   (statuses `Assigned`, `InProgress`, `WorkProduced`, `PullRequestOpen`),
   **return false** instead of logging a warning and continuing.

2. Add a per-vessel setting `AllowConcurrentMissions` (default `false`) that
   operators can enable for vessels where parallel work is safe (e.g.,
   non-overlapping file sets with explicit manifests).

3. When blocked, log at Info level: `"Vessel {vessel} already has active mission
   {missionId}; deferring assignment until it completes."` This gives the
   operator visibility without requiring action.

**Verification:**
- Unit test: `TryAssignAsync` returns false when vessel has an `InProgress`
  mission and `AllowConcurrentMissions` is false.
- Integration test: dispatch two missions to the same vessel; confirm the second
  stays `Pending` until the first reaches `Complete` or a terminal state.

---

#### T1-2: Fix missing-PID treated as success

**Problem:** `AdmiralService.HealthCheckCaptainAsync` (line ~549-553) catches
`ArgumentException` from `Process.GetProcessById()` and sets `exitCode = 0`.
This treats a crashed or killed agent as a clean success, promoting its mission
to `WorkProduced` even if no work was produced.

**File:** `src/Armada.Core/Services/AdmiralService.cs`

**Change:**

1. Change `exitCode = 0` to `exitCode = -1` in the `ArgumentException` catch
   block.

2. In `HandleCompletionAsync`, treat `exitCode < 0` as a failure: set mission
   status to `Failed` rather than `WorkProduced`.

3. Add a secondary check: only promote to `WorkProduced` if the mission branch
   has commits beyond the base branch. Use `git log base..branch --oneline` to
   verify. If no commits exist, the mission produced no work regardless of exit
   code.

**Verification:**
- Kill an agent process externally; confirm the mission transitions to `Failed`,
  not `WorkProduced`.
- Run a mission that produces no commits but exits cleanly; confirm it
  transitions to `Failed` with a descriptive reason.

---

#### T1-3: Stop deleting sibling worktrees

**Problem:** `DockService.ProvisionAsync` (lines ~93-140) iterates all
directories under the vessel's dock directory and deletes any that don't match
the current captain's name. It only checks `if (dirName == captain.Name)
continue;` — it does not check whether the directory belongs to another active
captain.

**File:** `src/Armada.Core/Services/DockService.cs`

**Change:**

1. Before deleting a directory, query the database for any dock with that path
   that is in `Provisioned` or `Active` state and assigned to any captain in
   `Working` or `Idle` state.

2. If such a dock exists, skip deletion and log a warning:
   `"Skipping cleanup of {path}: still in use by captain {captainName} for
   mission {missionId}."`.

3. Only delete directories that have no active dock record, or whose dock record
   is in a terminal state (`Released`, `Failed`).

**Verification:**
- Provision two captains on the same vessel simultaneously; confirm neither
  deletes the other's worktree.
- Provision a captain after a previous captain's dock has been released; confirm
  the stale directory IS cleaned up.

---

#### T1-4: Stop marking local landing Complete on push failure

**Problem:** In the local merge landing flow, `landingSucceeded` can remain
`true` even when the `git push` step fails, because the push failure is caught
and logged but doesn't always flip the flag.

**File:** `src/Armada.Core/Services/MergeQueueService.cs` (and/or the
`MergeService` local-merge path — verify exact location)

**Change:**

1. Ensure that any exception or non-zero exit from `git push` sets
   `landingSucceeded = false`.

2. If push fails, set mission status to `LandingFailed` with the push error in
   the reason field.

3. Do NOT delete the mission branch on landing failure — it is needed for retry.

**Verification:**
- Simulate a push failure (e.g., remote reject due to branch protection); confirm
  mission lands in `LandingFailed`, not `Complete`.
- Confirm the mission branch is preserved for manual or automatic retry.

---

#### T1-5: Reconcile merge-queue result back to mission status

**Problem:** `MergeQueueService.LandEntryAsync` (lines ~450-471) updates
`MergeEntry.Status = Landed` but never updates the linked mission's status. The
mission remains `WorkProduced` or `PullRequestOpen` indefinitely even though the
work has landed.

**File:** `src/Armada.Core/Services/MergeQueueService.cs`

**Change:**

1. After setting `MergeEntry.Status = Landed`, look up the linked mission by
   `MergeEntry.MissionId`.

2. If the mission exists and is not already in a terminal state, update it to
   `Complete` with a reason like `"Landed via merge queue entry {entryId}"`.

3. Similarly, when a merge entry transitions to `Failed`, update the linked
   mission to `LandingFailed`.

4. After updating the mission, check if this was the last pending mission in its
   voyage. If so, update the voyage status to `Complete`.

**Verification:**
- Enqueue a merge entry, process it to `Landed`; confirm the linked mission is
  `Complete`.
- Fail a merge entry; confirm the linked mission is `LandingFailed`.
- Complete all missions in a voyage via merge queue; confirm the voyage reaches
  `Complete`.

---

### Tier 2 — Close State Machine Gaps

These changes fix the recovery and reconciliation paths so that missions don't
get stuck in intermediate states requiring manual intervention.

---

#### T2-1: Persistent PR reconciler

**Problem:** PR polling for `PullRequestOpen` missions is ephemeral. If the
server restarts, missions with open PRs are never checked again. They sit in
`PullRequestOpen` forever.

**File:** `src/Armada.Core/Services/AdmiralService.cs`

**Change:**

1. In `HealthCheckAsync` (the periodic health-check loop), add a step that
   queries all missions in `PullRequestOpen` status.

2. For each, check the PR status via the git hosting API (GitHub/GitLab).

3. If the PR is merged, transition the mission to `Complete`.

4. If the PR is closed without merge, transition to `LandingFailed`.

5. If the PR has merge conflicts, log a warning and optionally attempt a rebase.

6. Rate-limit API calls (e.g., check at most N PRs per health-check cycle, with
   a minimum interval between checks for the same PR).

**Verification:**
- Create a mission with an open PR. Restart the server. Merge the PR externally.
  Confirm the health check picks it up and transitions the mission to `Complete`.

---

#### T2-2: Explicit `armada_retry_landing` command

**Problem:** When a mission reaches `LandingFailed`, the operator has no
one-click recovery path. Manual recovery involves understanding the internal
state machine, finding the branch, and manually merging.

**New file:** Add the tool handler in the appropriate tools/handlers location.

**Change:**

1. Add an `armada_retry_landing` tool that accepts a mission ID.

2. Validate the mission is in `LandingFailed` status.

3. Rebase the mission branch onto the current target branch head.

4. If the rebase succeeds cleanly, re-run the landing flow (respecting the
   mission's configured landing mode).

5. If the rebase has conflicts, report them to the operator and leave the
   mission in `LandingFailed`.

6. Log all retry attempts for auditability.

**Verification:**
- Fail a landing due to target-branch drift. Call `armada_retry_landing`. Confirm
  the rebase succeeds and the mission lands.
- Fail a landing due to a genuine conflict. Call `armada_retry_landing`. Confirm
  the conflicts are reported and the mission stays in `LandingFailed`.

---

#### T2-3: Bounded auto-retry for target-branch drift

**Problem:** The most common `LandingFailed` cause is that the target branch
moved forward between mission completion and landing. This is a transient
condition that resolves with a rebase.

**File:** Landing logic in `MergeQueueService` / `MergeService`

**Change:**

1. When landing fails due to a merge conflict, attempt an automatic rebase of
   the mission branch onto the current target branch head.

2. If the rebase succeeds cleanly (no conflicts), retry the landing immediately.

3. Allow at most `MaxLandingRetries` (default: 3) automatic retries per mission.

4. If all retries fail, transition to `LandingFailed` and log the conflict
   details.

5. Do NOT auto-retry if the conflict involves files that were modified in the
   mission's own commits (this indicates a genuine conflict, not drift).

**Important:** This is only safe AFTER T1-1 (serialized per-vessel) is in place.
Without serialization, auto-retry can thrash as concurrent missions keep
conflicting with each other.

**Verification:**
- Land a mission while another commit is pushed to the target branch. Confirm
  auto-rebase and successful landing.
- Create a genuine conflict (mission edits file X, someone else also edits file
  X on target). Confirm auto-retry fails and mission reaches `LandingFailed`
  with conflict details.

---

#### T2-4: Captain not released until post-run handoff completes

**Problem:** `HandleCompletionAsync` (line ~383) releases the captain before the
background landing task finishes. The captain gets reassigned, re-provisions the
same dock path, and collides with the still-running landing task.

**File:** `src/Armada.Core/Services/MissionService.cs`

**Change:**

1. Split `HandleCompletionAsync` into two phases:
   - **Phase A (synchronous):** Determine landing mode, execute the immediate
     handoff step (push the branch, create the PR, or enqueue in merge queue).
     This is the part that needs the dock and worktree.
   - **Phase B (after captain release):** Wait for final landing resolution
     (PR merge, queue processing). This does NOT hold the captain.

2. Release the captain only after Phase A completes successfully.

3. If Phase A fails, set the mission to `LandingFailed` and release the captain
   with the dock intact (for retry).

4. Phase B is handled by the persistent PR reconciler (T2-1) and merge-queue
   reconciliation (T1-5), not by holding the captain.

**Verification:**
- Complete a mission. Confirm the captain is not released until the branch is
  pushed / PR is created / entry is enqueued.
- Confirm the captain IS released before waiting for PR merge or queue
  processing.

---

#### T2-5: Reclaim docks from failed launches

**Problem:** If `ProvisionAsync` or agent launch fails after the dock record is
created, the dock remains in `Provisioned` state with no captain using it. It
blocks future provisioning on the same path.

**File:** `src/Armada.Core/Services/DockService.cs`

**Change:**

1. In the health-check loop, query for docks in `Provisioned` state with no
   associated active captain (captain is `Idle` or doesn't exist).

2. If the dock has been in `Provisioned` for longer than a threshold (e.g., 5
   minutes), release it and clean up the worktree.

3. Log the reclamation for debugging.

**Verification:**
- Simulate a launch failure after dock provisioning. Confirm the dock is
  reclaimed on the next health-check cycle.

---

### Tier 3 — Structural Hardening

These are larger changes that eliminate entire categories of race conditions and
failure modes. They should be implemented after Tiers 1 and 2 are stable.

---

#### T3-1: Per-mission dock paths

**Problem:** Dock paths use the pattern `{vessel}/{captain}`, which means a
captain's new mission reuses the same path as the previous mission. If the
previous mission's background landing task is still running, it collides with
the new mission's provisioning.

**Files:**
- `src/Armada.Core/Services/DockService.cs`
- Any code that constructs or references dock paths

**Change:**

1. Change the dock path pattern from `{vessel}/{captain}` to
   `{vessel}/{missionId}`.

2. Since mission IDs are unique, this eliminates path-reuse races entirely.

3. Update `ProvisionAsync` to create the new path without needing to clean up
   the "previous occupant" of the same path.

4. Move worktree cleanup to a separate, explicit cleanup step that runs after
   the mission reaches a terminal state (`Complete`, `Failed`, `Cancelled`).

5. Add a periodic cleanup job in the health-check loop that removes worktrees
   for missions in terminal states older than a configurable threshold.

**Disk usage consideration:** More worktrees will exist simultaneously. Mitigate
by running cleanup aggressively after landing completes. Monitor disk usage and
alert if it exceeds a threshold.

**Verification:**
- Run two sequential missions on the same captain. Confirm they use different
  dock paths and don't interfere with each other.
- Confirm worktrees are cleaned up after missions reach terminal states.

---

#### T3-2: Transactional assignment

**Problem:** `TryAssignAsync` performs mission claim, captain claim, and dock
provisioning as separate operations. A failure between any two leaves orphaned
state (e.g., a claimed captain with no mission, or a provisioned dock with no
captain).

**File:** `src/Armada.Core/Services/MissionService.cs`

**Change:**

1. Wrap the assignment sequence in a single database transaction:
   - Claim the mission (set status to `Assigned`, set `CaptainId`)
   - Claim the captain (set state to `Working`, set `CurrentMissionId`)
   - Create the dock record

2. If any step fails, the transaction rolls back and all three records remain
   unchanged.

3. Dock *provisioning* (the actual `git worktree add`) happens AFTER the
   transaction commits, since it's a filesystem operation that can't be part of
   a DB transaction.

4. If provisioning fails after the transaction commits, transition the mission
   to `Failed` and release the captain — both within a new transaction.

**Verification:**
- Simulate a captain-claim failure after mission-claim succeeds. Confirm neither
  the mission nor the captain is left in a dirty state.
- Simulate a dock-provisioning failure after the transaction commits. Confirm
  the mission is rolled back to `Failed` and the captain is released.

---

#### T3-3: Durable landing state machine

**Problem:** Landing is currently a fire-and-forget `Task.Run` that is lost on
server restart and has no persistent state to drive retries.

**Files:**
- New: `src/Armada.Core/Models/LandingJob.cs` (or extend `MergeEntry`)
- `src/Armada.Core/Services/MergeQueueService.cs`
- `src/Armada.Core/Services/AdmiralService.cs`

**Change:**

1. Create a persistent `LandingJob` entity (or extend the existing
   `MergeEntry`) with states:
   ```
   Pending → Rebasing → Merging → Pushing → CreatingPR → Complete
                                                        → Failed
   ```

2. When a mission produces work and is ready to land, create a `LandingJob`
   record in the database with status `Pending`.

3. The health-check loop picks up `Pending` landing jobs and drives them through
   the state machine.

4. Each state transition is persisted to the database before executing the
   corresponding operation.

5. On server restart, the health-check loop resumes any in-progress landing jobs
   from their last persisted state.

6. Failed landing jobs can be retried via `armada_retry_landing` (T2-2) or
   auto-retry (T2-3).

**Verification:**
- Start a landing, kill the server mid-push. Restart. Confirm the landing
  resumes from the `Pushing` state.
- Fail a landing at the `Merging` state. Confirm the job is in `Failed` with
  the error details preserved.

---

#### T3-4: Replace user-working-directory integration with dedicated worktree

**Problem:** `LocalMerge` uses the user's live working directory as the
integration engine. It checks out the target branch, merges the mission branch,
and pushes — all in a workspace the user may be actively editing. If the user
has uncommitted changes, is on a different branch, or the push fails, the
workspace is left in a dirty/diverged state with no rollback.

**Files:**
- `src/Armada.Core/Services/MergeService.cs` (or wherever `MergeLocallyAsync`
  / local merge logic lives)
- `src/Armada.Core/Services/MergeQueueService.cs`

**Change:**

1. **Integration step:** Perform the merge in a dedicated, temporary worktree
   (or bare-repo mediated merge). This worktree is created specifically for
   landing and is not the user's working directory.

2. **Push step:** Push from the dedicated worktree. If push fails, the user's
   workspace is untouched.

3. **User sync step (optional, post-success):** After the push succeeds, if the
   user's working directory is on the target branch and clean, fast-forward it
   to include the new changes. If it's dirty or on a different branch, skip the
   sync and log an informational message: `"Landing succeeded. Your working
   directory was not updated because [reason]. Run 'git pull' to sync."`.

4. If the integration worktree merge fails, the user's workspace is completely
   untouched. The operator can retry or resolve conflicts in the integration
   worktree without risk to the user's work.

**Design principle:** "Update the user's working directory" is a feature.
"Use the user's working directory as the integration engine" is a bug.

**Verification:**
- Land a mission while the user has uncommitted changes. Confirm the landing
  succeeds (push to remote) and the user's uncommitted changes are untouched.
- Land a mission while the user is on a different branch. Confirm the landing
  succeeds and the user stays on their branch.
- Land a mission while the user is on the target branch with a clean tree.
  Confirm the working directory is fast-forwarded after success.

---

#### T3-5: Fix stall detection

**Problem:** `HealthCheckCaptainAsync` (lines ~624-670) updates the heartbeat
timestamp as part of the health check itself whenever the process is alive. This
means a hung agent that is still technically running (process exists, but making
no progress) will never be detected as stalled — the heartbeat keeps getting
refreshed by the health check, not by actual agent activity.

**File:** `src/Armada.Core/Services/AdmiralService.cs`

**Change:**

1. Stop updating heartbeat from the health-check loop.

2. Instead, have the agent runtime update heartbeat based on actual agent
   output — e.g., when new stdout/stderr is produced, or when the agent writes
   to a progress file.

3. The health check compares the agent-updated heartbeat against the stall
   threshold. If the agent hasn't produced output in `StallThresholdMinutes`,
   it's genuinely stalled.

4. Alternatively, if modifying the agent runtime is not feasible, track a
   separate `LastOutputTimestamp` in the captain record. The runtime updates this
   on each output line. The health check uses this for stall detection instead of
   `LastHeartbeatUtc`.

**Verification:**
- Run a mission with an agent that hangs (produces no output for longer than the
  stall threshold). Confirm the health check detects the stall and initiates
  recovery.
- Run a mission with a slow but active agent (produces output every few
  minutes). Confirm it is NOT falsely detected as stalled.

---

## Implementation Order and Dependencies

```
T1-1 (serialize per vessel)          ── no dependencies, deploy first
T1-2 (fix PID-as-success)           ── no dependencies
T1-3 (stop deleting sibling docks)  ── no dependencies
T1-4 (fix push-failure marking)     ── no dependencies
T1-5 (merge-queue reconciliation)   ── no dependencies

  All T1 items are independent and can be implemented in parallel.

T2-1 (persistent PR reconciler)     ── after T1-5 (reconciliation pattern)
T2-2 (retry landing command)        ── after T1-4 (LandingFailed must be real)
T2-3 (auto-retry on drift)          ── after T1-1 (serialization) + T2-2
T2-4 (captain release timing)       ── after T1-1 (reduces urgency of races)
T2-5 (reclaim failed docks)         ── after T1-3 (safe deletion checks)

T3-1 (per-mission dock paths)       ── after T2-4 (new path scheme)
T3-2 (transactional assignment)     ── after T1-1 (simpler transaction scope)
T3-3 (durable landing state machine)── after T2-2 + T2-3 (subsumes retry logic)
T3-4 (dedicated integration wktree) ── after T3-1 (dock path scheme settled)
T3-5 (fix stall detection)          ── after T1-2 (exit code handling settled)
```

---

## Key Design Principles

1. **Correctness over throughput.** Serializing missions per vessel reduces
   parallelism but eliminates the #1 source of merge conflicts. Throughput can
   be recovered later with explicit opt-in concurrency for non-overlapping work.

2. **Single-server assumption.** Armada is single-server today. No distributed
   coordination (distributed locks, consensus protocols) is needed. If
   multi-instance support is added later, the durable landing state machine
   (T3-3) provides the foundation for distributed coordination.

3. **The user's working directory is a sync target, not the integration
   engine.** Integration happens in a dedicated worktree. The user's workspace
   is updated as a convenience after confirmed success.

4. **Every state transition that can fail must have an explicit recovery path.**
   No fire-and-forget. No "log and hope." Every failure mode has either an
   automatic retry or an operator-facing command.

5. **Preserve evidence on failure.** Don't delete branches, worktrees, or logs
   when something fails. They're needed for diagnosis and retry.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Serialization reduces throughput | High | Medium | Opt-in `AllowConcurrentMissions` per vessel; queue visualization so operators see pending work |
| Synchronous handoff blocks captains on slow pushes | Medium | Medium | Timeout on push operations (e.g., 5 minutes); fail landing on timeout |
| Per-mission dock paths increase disk usage | Medium | Low | Aggressive cleanup after terminal states; disk usage monitoring and alerting |
| Durable landing state machine adds DB load | Low | Low | Landing jobs are short-lived; index on status column |
| Dedicated integration worktree adds git overhead | Low | Low | One extra worktree per landing; cleaned up immediately after |

---

## Success Criteria

After full implementation:

- **Zero merge conflicts from concurrent same-vessel missions** (the most common
  category today) — enforced by T1-1.
- **No missions stuck in `WorkProduced` or `PullRequestOpen` indefinitely** —
  enforced by T1-5 and T2-1.
- **No false-success missions from crashed agents** — enforced by T1-2.
- **One-click recovery for `LandingFailed`** — provided by T2-2.
- **Server restart does not lose in-flight landings** — enforced by T3-3.
- **User's working directory is never left in a dirty state by Armada** —
  enforced by T3-4.
