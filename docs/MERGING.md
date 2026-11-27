# Merge Queue

## Overview

Armada includes a built-in merge queue that serializes branch merges into a target branch, running optional tests before landing each one. Entries targeting the same vessel and target branch are processed **sequentially** to avoid conflicts, while different vessel+target-branch groups are processed **in parallel** for throughput. This design ensures correctness within a group (each merge sees the result of the previous one) while maximizing overall processing speed across independent repositories and branches.

The merge queue is managed through MCP tools (`enqueue_merge`, `process_merge_queue`, `enumerate` with entityType 'merge_queue', etc.), the REST API, the dashboard (**Missions > Merge Queue** tab, with a detail page per entry at `/merge-queue/:id`), and operates on the bare repository clones that Armada maintains for each vessel.

The Admiral drives the queue automatically: after every health check (every `HeartbeatIntervalSeconds`) it runs a processing pass, so entries auto-enqueued by the `MergeQueue` landing mode land without a manual trigger. `process_merge_queue` and `process_merge_entry` remain available to run a pass immediately.

---

## Status State Machine

```
Queued --> Testing --> Landed
  |          |
  v          v
Cancelled  Failed
```

- **Queued** -- waiting to be picked up by a processing run.
- **Testing** -- merged into a temporary integration branch; tests are running.
- **Landed** -- tests passed and the merge was pushed to the target branch.
- **Failed** -- merge conflict or test failure.
- **Cancelled** -- manually removed from the queue.

The `MergeStatusEnum` also defines **Passed**, which is reserved. The queue service lands an entry as soon as its tests pass, so it does not currently leave entries in `Passed`; it is treated as an active (non-terminal) status wherever entries are counted.

Terminal states: `Landed`, `Failed`, `Cancelled`.

---

## Processing Flow

1. **Acquire global lock** -- only one queue processing run can happen at a time across *all* vessels and target branches. There is a single lock on the entire `MergeQueueService` instance, not one lock per vessel, plus a durable cross-instance lease (`merge-queue:process`, 15-minute TTL) so that only one Admiral processes the queue when several share a database. If either is already held, the call returns immediately (no-op). This means that if you call `process_merge_queue` while a previous run is still working through entries, the second call is silently dropped. Within a single processing run, however, independent vessel+target-branch groups are processed in parallel (see step 3).

2. **Fetch queued entries** -- all entries with status `Queued` are loaded, ordered by priority (lower number = higher priority) then by creation time.

3. **Group by vessel + target branch** -- entries targeting the same vessel and branch form a group. Groups are processed **in parallel** using `Task.WhenAll`. Each group is wrapped in error isolation (`ProcessGroupSafeAsync`) so that one group's failure does not affect other groups. Entries *within* each group remain strictly sequential: each entry is merged, tested, and landed before the next entry in the same group begins. Each entry gets its own temporary worktree path, so the integration worktrees do not collide. Note, however, that different groups may still resolve to the same underlying bare repository when they target different branches on the same vessel.

4. **For each entry in a group** (sequential within the group):
   1. Mark the entry as `Testing`.
   2. Fetch latest refs from the remote (`git fetch`), once per repository for the whole group in this pass. A successful push updates the repository's `origin/<target>` itself, so the next entry integrates against the new target without another fetch.
   3. Create a temporary worktree from the current target branch.
   4. Merge the entry's branch into the worktree (`git merge --no-ff`).
   5. If the merge conflicts, mark the entry `Failed` and move on.
   6. Run the configured test command (if any) through the platform shell (`/bin/sh -c` or `cmd.exe /c`). If tests fail, mark `Failed`.
   7. Push the verified commit to update the target (`git push origin <sha>:refs/heads/<target>`).
   8. If the push is refused because the target moved (see below), requeue the entry and go back to step 2 with a fresh fetch.
   9. Mark the entry `Landed`.
   10. Clean up the temporary worktree.

**Requeue on a moved target.** When the remote refuses the push, Armada fetches the target and asks git whether the remote target is an ancestor of the verified commit (`git merge-base --is-ancestor`, decided by its exit status, never by the text git prints). When it is not, another writer landed first and the push was not a fast-forward: the entry goes back to `Queued`, a `merge_queue.requeued` event is recorded, and the entry is integrated and verified again from a fresh fetch, up to `MergeQueueRequeueLimit` times (default 3) before it fails. A refusal while the target did not move (permissions, a protected branch, a hook) fails at once. The same limit applies to a lost landing handoff: a `WorkProduced` merge-queue mission with a passing stage check and no merge entry after five minutes has its branch enqueued again (event `mission.landing_handoff_requeued`, counted from the event log so a restart does not reset it), and is flagged `LandingFailed` only after the limit.

**Batched landing (optional, off by default).** With `MergeQueueBatchSize` above 1, up to that many queued entries of one group that are processed in the same repository with the same test command are merged into one integration worktree, verified once, and published with one push. An entry that conflicts with the target or with an earlier entry of the batch fails as it would have serially, and the batch continues without it. When the batch's verification fails, it is split in half and each half is landed on its own; a half of one entry takes the ordinary path, so the failing entry fails alone and the others land. Every mission of a landed batch records the batch's published commit as its `CommitHash`. A moved target requeues the whole batch under the same limit. `benchmarks/run-landing-batch.sh` compares batched against serial landing.

Every git and test subprocess run by the queue has a hard 10-minute timeout; a hung command is killed and the entry is marked `Failed` instead of wedging the queue.

When an entry has a linked mission (`missionId`), the mission is reconciled with the entry's outcome: `Landed` moves the mission to `Complete`, and `Failed` moves it to `LandingFailed` (with the failure reason recorded). Missions that are already `Complete`, `Failed`, or `Cancelled` are left alone.

Because each entry (or batch) is landed before the next one starts, the next entry in the same group always merges against the up-to-date target branch.

---

## Thread Safety

- A single `_ProcessLock` object gate-keeps entry to `ProcessQueueAsync`. The lock is checked-and-set inside a `lock` block. If `_Processing` is already `true`, the call returns immediately. A database coordination lease then serializes processing across Admiral instances.
- Within a processing run, each vessel+target-branch group runs as an independent `Task`. Groups execute in parallel via `Task.WhenAll`. Each group task is wrapped in a `try-catch` (`ProcessGroupSafeAsync`) for error isolation, so a failure in one group does not cancel or affect other groups. Worktree paths under `_merge-queue/` are unique per entry, and groups update different merge-entry rows. Different groups can still point at the same bare repository if they target different branches on the same vessel.
- Within a single group, entries are processed strictly one at a time. There is no concurrency within a group.
- The lock and the lease are released in a `finally` block, so even if `Task.WhenAll` throws, the next call to `ProcessQueueAsync` will be able to proceed.

---

## Failure Scenarios

| Scenario | Behavior |
|---|---|
| **Merge conflict** | Entry marked `Failed` with message. Worktree cleaned up. Linked mission moves to `LandingFailed`. Next entry in the same group continues. |
| **Test failure** | Entry marked `Failed` with exit code and output truncated to 4096 characters. Worktree cleaned up. Linked mission moves to `LandingFailed`. Next entry continues. |
| **Push not a fast-forward** | The target moved after the entry was integrated. The entry is requeued and integrated again from a fresh fetch, up to `MergeQueueRequeueLimit` times; after that it is marked `Failed` (the reason names the limit) and the linked mission moves to `LandingFailed` with kind `Infra`. |
| **Push refused** | The remote refused the push although the target did not move (force-push protection, a hook, permissions). Entry marked `Failed` with git's message. Linked mission moves to `LandingFailed`. |
| **Batch verification failure** | With batched landing, the batch is split in half and each half retried, down to single entries; only the failing entry ends `Failed`. |
| **Timeout** | A git or test subprocess that runs longer than 10 minutes is killed; the entry is marked `Failed`. |
| **Vessel not found** | All entries in the group are marked `Failed` with a message indicating the repository path for the vessel could not be resolved. |
| **Unexpected exception** | Entry marked `Failed` with error message. Best-effort worktree cleanup. Processing continues to the next entry. Group-level exceptions are caught by `ProcessGroupSafeAsync` and logged as warnings. |

---

## Best Practices

- **One branch per entry.** Each merge queue entry corresponds to a single feature branch being merged into a target branch.
- **Keep test commands fast.** Tests run synchronously per entry, blocking subsequent entries in the same group. Long tests slow down the entire group's queue throughput.
- **Use priorities.** Lower priority numbers are processed first within a group. Use this to land critical fixes ahead of routine changes.
- **Monitor terminal entries.** Use `enumerate` with entityType 'merge_queue' and status 'Failed' to check for entries that may need attention.
- **Clean up regularly.** Use `delete_merge`, `purge_merge_entry`, `purge_merge_entries`, or `purge_merge_queue` to remove terminal entries. A landed entry follows `BranchCleanupPolicy`: `None` keeps the local and origin mission refs, `LocalOnly` deletes the local ref only, and `LocalAndRemote` deletes both. A landed branch that is not contained in the target stays. A failed entry keeps its branch. A cancelled entry loses the local and origin refs when git shows that the branch is merged.

---

## Commands Reference

| Tool | Description |
|---|---|
| `enqueue_merge` | Add an entry to the merge queue. Required: `vesselId`, `branchName`. Optional: `missionId`, `targetBranch` (default `main`), `priority` (default 0, lower first), `testCommand`. |
| `process_merge_queue` | Trigger a processing run (no-op if already running). |
| `process_merge_entry` | Process a single entry by ID. |
| `get_merge_entry` | Get a single entry by ID. |
| `cancel_merge` | Cancel a queued entry. |
| `delete_merge` | Delete a terminal entry and clean up its landed branch under `BranchCleanupPolicy`. |
| `purge_merge_entry` | Delete a single terminal entry by ID. |
| `purge_merge_entries` | Delete several terminal entries by ID; returns purged and skipped counts. |
| `purge_merge_queue` | Bulk delete all terminal entries, with optional vessel/status filters. |
| `enumerate` (entityType `merge_queue`) | List entries with paging and status/vessel filters. |

---

## Landing Mode

When a mission's agent exits successfully, Armada sets the mission to `WorkProduced` and then applies the **landing mode** to determine how to integrate the work. The landing mode is resolved in priority order:

1. **Voyage-level** `LandingMode` (if the mission belongs to a voyage with a non-null `LandingMode`)
2. **Vessel-level** `LandingMode` (on the target vessel)
3. **Global** `LandingMode` (in `ArmadaSettings`, default `MergeAndPush`)
4. `MergeAndPush` when none of the above is set

| Landing Mode | Behavior |
|---|---|
| `LocalMerge` | Merge the branch into the vessel's default branch in the vessel's configured working directory. **Nothing is pushed**: the merge stays in your local checkout until you push it yourself. Runs only when the vessel has both `WorkingDirectory` and `LocalPath` configured. Mission transitions to `Complete` on success or `LandingFailed` if the merge fails. A mission with no diff is marked `Complete` without a merge. If those vessel paths are not configured, the mission remains at `WorkProduced`. |
| `MergeAndPush` | The default. The same local merge as `LocalMerge`, then a push of the working directory's branch to its remote (for example `origin` on GitHub). The working directory needs a remote. Mission transitions to `Complete` when both succeed, or `LandingFailed` when the merge or the push fails; after a failed push the merge stays in the working directory and the mission branch is kept for a retry. Same `WorkingDirectory` and `LocalPath` requirement and no-diff handling as `LocalMerge`. |
| `PullRequest` | Push the branch and create a pull request. Mission transitions to `PullRequestOpen` (or `LandingFailed` if the push or PR creation fails). Each health check polls open PRs; once merged, the mission transitions to `Complete`. When auto-merge is enabled (`AutoMergePullRequests`, global or per voyage), Armada also enables auto-merge on the PR. |
| `MergeQueue` | Enqueue the mission branch into Armada's merge queue (target: the vessel's default branch) for serialized testing and landing. The mission stays `WorkProduced` while the entry is `Queued` or `Testing`, and its voyage stays `InProgress` (not `Complete`) until the entry settles: `Landed` moves the mission to `Complete`, a failure moves it to `LandingFailed`. |
| `None` | No automated landing. The mission stays at `WorkProduced` for manual handling: merge its branch yourself (the dashboard and TUI mission pages offer **Merge in Manage Branches** instead of Land). Once the mission's commit is contained in the vessel's target branch (`git merge-base --is-ancestor`, checked right after a Manage Branches merge and on every health check), Armada moves the mission to `Complete`. |

### The landing reconciler

One reconciler (`LandingReconciler.SweepAsync`) runs on every health check. It reads the unlanded missions once (`WorkProduced`, `LandingFailed`, `Review`, `PullRequestOpen`, a page of up to 500 per status, continuing with the next page on the next check) and runs these cases on that one scan, in order; a mission one case changed is skipped by the later ones:

1. **Landed entry.** A `WorkProduced` or `LandingFailed` mission whose latest merge entry is `Landed` is completed (an Admiral that stopped between marking the entry `Landed` and completing the mission leaves it that way).
2. **Merged by hand.** A `WorkProduced` mission with landing mode `None` whose branch is contained in the target is completed (up to 25 per check).
3. **Missing dock.** The missing worktree of an unlanded mission's dock is recreated from its branch.
4. **Deferred landing.** A `LocalMerge` landing deferred on a dirty checkout is retried once the checkout is clean.
5. **Landed elsewhere.** The stale `LandingFailed` check below.

A case that throws is logged and the other cases still run.

### Reconciling stale LandingFailed missions

A landing can fail for a reason that later clears (a dirty developer checkout, an unpushed local commit), while the same work still reaches the default branch by another path, for example a rescue mission or a manual push. On every health check Armada examines a bounded page of `LandingFailed` missions (25 per check, oldest first, continuing with the next page on the next check) and moves a mission to `Complete` only when it has proof that its work is on `origin/<default branch>`:

- the mission's `CommitHash` is an ancestor of `origin/<default branch>` (`git merge-base --is-ancestor`), or
- a commit on `origin/<default branch>`, committed since the mission was created, carries the trailer `Armada-Mission-Id: <mission id>` (an exact trailer match; a message that only mentions the id does not count).

Content that landed as a different commit without the trailer is not matched. Git runs where the vessel's repository lives: Armada's repository or the working directory on the Admiral host, otherwise the vessel's checkout on a connected Harbor. The repository is fetched at most once per vessel per check; a vessel whose repository cannot be reached or fetched is skipped, so no mission is completed without proof. A reconciled mission has its landing failure reason cleared, and Armada records a `mission.landing_reconciled` event whose payload names the evidence commit, how it was found (`CommitAncestry` or `MissionTrailer`), the ref, and the previous failure reason. `LandingFailed` to `Complete` is also a legal manual transition.

### Where to set it

| Scope | Dashboard | TUI | CLI | REST |
|---|---|---|---|---|
| Global default | Server > Agent Settings > **Default Landing Mode** | Server settings > Agent Settings > Default Landing Mode | `armada config set landingMode MergeAndPush` (edits this machine's `settings.json`; restart the Admiral) | `PUT /api/v1/settings` with `{"LandingMode": "MergeAndPush"}` |
| Vessel | Vessels > Edit > **Landing Mode** | Vessels > edit vessel > Landing Mode | n/a | `PUT /api/v1/vessels/{id}` with `LandingMode` |
| Voyage | Voyages > New voyage > **Landing Mode** | New voyage > Landing Mode | `armada go --landing-mode MergeAndPush ...` | `POST /api/v1/voyages` with `LandingMode` |

The global default lives in `settings.json` as `landingMode`. The legacy `autoPush` and `autoCreatePullRequests` settings and the per-voyage `AutoPush` and `AutoCreatePullRequests` fields no longer affect landing (see the upgrade notes below). `AutoMergePullRequests` still applies to the `PullRequest` mode.

### Upgrading from 1.0.0 (landing modes, fixed in 1.0.1)

Version 1.0.0 shipped with a `LocalMerge` mode that also pushed to the remote, which its name did not say. In 1.0.1:

- `LocalMerge` merges into the working directory **without pushing**.
- The new `MergeAndPush` mode does exactly what `LocalMerge` did in 1.0.0 (merge, then push).
- The global default is `MergeAndPush`, used whenever no voyage, vessel, or global landing mode is set (in 1.0.0 an unset mode fell back to the legacy flags, whose defaults also merged and pushed).
- The legacy flags are gone: the `autoPush` and `autoCreatePullRequests` settings, the settings API's `AutoCreatePr`, the dashboard and TUI "Auto-Create Pull Requests" toggle, the voyage create "Auto-Push" / "Auto-Create PRs" / "Auto-Merge PRs" checkboxes, and `armada go --push/--no-push/--pr/--no-pr/--merge/--no-merge` (use `--landing-mode`). A `settings.json` from 1.0.0 that has no `landingMode` keeps its behavior: `autoCreatePullRequests: true` loads as `PullRequest`, `autoPush: false` loads as `LocalMerge`, anything else as `MergeAndPush`.

Nothing is migrated for you: every `LocalMerge` you set in 1.0.0 now merges without pushing. If you want finished work to keep reaching GitHub (or your other remote), change those settings to `MergeAndPush`:

1. **Find the vessels that use `LocalMerge`.** In the dashboard, open Vessels and filter Landing Mode by **Local Merge**; in the TUI, use the Vessels landing mode filter. Over REST:

   ```bash
   curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:7890/api/v1/vessels?pageSize=1000" \
     | jq -r '.Objects[] | select(.LandingMode == "LocalMerge") | "\(.Id) \(.Name)"'
   ```

2. **Switch each one to `MergeAndPush`.** Dashboard: Vessels > the vessel's actions > Edit > Landing Mode > **Merge and Push -- local merge, then push to the remote** > Save. TUI: Vessels > edit the vessel > Landing Mode > Merge and Push > Save. REST (read the vessel, change the field, write it back):

   ```bash
   curl -s -H "Authorization: Bearer $TOKEN" http://localhost:7890/api/v1/vessels/vsl_abc123 \
     | jq '.LandingMode = "MergeAndPush"' \
     | curl -s -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
         --data @- http://localhost:7890/api/v1/vessels/vsl_abc123
   ```

3. **Check the global default.** If your `settings.json` sets `"landingMode": "LocalMerge"`, change it in the dashboard (Server > Agent Settings > Default Landing Mode), the TUI, with `PUT /api/v1/settings` and `{"LandingMode": "MergeAndPush"}`, or on the Admiral's machine with `armada config set landingMode MergeAndPush` followed by an Admiral restart. If `settings.json` has no `landingMode`, nothing needs to change.

4. **Check voyages and scripts.** Voyages created with `LandingMode` `LocalMerge` keep it; create new voyages with `MergeAndPush` (or no landing mode, to inherit the vessel's). Replace `armada go --push` with `armada go --landing-mode MergeAndPush` and `--pr` with `--landing-mode PullRequest` in scripts.

5. **Push anything that landed without a push.** Missions that landed with `LocalMerge` after the upgrade left their merge in the vessel's working directory; push it with `git -C <working directory> push`.

---

## Branch Cleanup Policy

After a mission's work has landed, Armada can automatically clean up the mission branch. The policy is resolved from the vessel level (falling back to global settings). Purging a landed merge-queue entry uses the same policy.

| Policy | Behavior |
|---|---|
| `LocalOnly` | Delete the local branch only (default) |
| `LocalAndRemote` | Delete both local and remote branches |
| `None` | Leave branches in place |

---

## Configuration

- **`LandingMode`** (in `ArmadaSettings`, default `MergeAndPush`) -- global landing policy. Can be overridden per-vessel (`Vessel.LandingMode`) or per-voyage (`Voyage.LandingMode`).
- **`BranchCleanupPolicy`** (in `ArmadaSettings`) -- global branch cleanup policy. Can be overridden per-vessel (`Vessel.BranchCleanupPolicy`).
- **`MergeQueueTestCommand`** (in `ArmadaSettings`) -- default test command to run for entries that don't specify their own. Can be overridden per entry via the `testCommand` parameter on `enqueue_merge`.
- **`MergeQueueRequeueLimit`** (default 3, 0 to 10, clamped) -- how many times a push that is not a fast-forward is requeued from a fresh fetch, and how many times a lost landing handoff is enqueued again, before the entry or mission fails. 0 fails at once.
- **`MergeQueueBatchSize`** (default 1, which is off; 1 to 20, clamped) -- batched landing: up to this many entries of one group are merged, verified once, and pushed together, with bisection when verification fails.
- **`DocksDirectory`** -- parent directory for temporary merge worktrees. Worktrees are created under `_merge-queue/` within this directory.
- **`ReposDirectory`** -- fallback repository path when a vessel's `LocalPath` is not set.
