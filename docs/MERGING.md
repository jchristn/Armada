# Merge Queue

## Overview

Armada includes a built-in merge queue that serializes branch merges into a target branch, running optional tests before landing each one. Entries targeting the same vessel and target branch are processed **sequentially** to avoid conflicts, while different vessel+target-branch groups are processed **in parallel** for throughput. This design ensures correctness within a group (each merge sees the result of the previous one) while maximizing overall processing speed across independent repositories and branches.

The merge queue is managed through MCP tools (`armada_enqueue_merge`, `armada_process_merge_queue`, `armada_enumerate` with entityType 'merge_queue', etc.) and operates on the bare repository clones that Armada maintains for each vessel.

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

Terminal states: `Landed`, `Failed`, `Cancelled`.

---

## Processing Flow

1. **Acquire global lock** -- only one queue processing run can happen at a time across *all* vessels and target branches. There is a single lock on the entire `MergeQueueService` instance, not one lock per vessel. If already processing, the call returns immediately (no-op). This means that if you call `armada_process_merge_queue` while a previous run is still working through entries, the second call is silently dropped. Within a single processing run, however, independent vessel+target-branch groups are processed in parallel (see step 3).

2. **Fetch queued entries** -- all entries with status `Queued` are loaded, ordered by priority (lower number = higher priority) then by creation time.

3. **Group by vessel + target branch** -- entries targeting the same vessel and branch form a group. Groups are processed **in parallel** using `Task.WhenAll`. Each group is wrapped in error isolation (`ProcessGroupSafeAsync`) so that one group's failure does not affect other groups. Entries *within* each group remain strictly sequential: each entry is merged, tested, and landed before the next entry in the same group begins. Each entry gets its own temporary worktree path, so the integration worktrees do not collide. Note, however, that different groups may still resolve to the same underlying bare repository when they target different branches on the same vessel.

4. **For each entry in a group** (sequential within the group):
   1. Mark the entry as `Testing`.
   2. Fetch latest refs from the remote (`git fetch`).
   3. Create a temporary worktree from the current target branch.
   4. Merge the entry's branch into the worktree (`git merge --no-ff`).
   5. If the merge conflicts, mark the entry `Failed` and move on.
   6. Run the configured test command (if any). If tests fail, mark `Failed`.
   7. Push the integration branch to update the target (`git push origin integration:target`).
   8. Mark the entry `Landed`.
   9. Clean up the temporary worktree.

Because each entry is landed immediately, the next entry in the same group always merges against the up-to-date target branch. This eliminates the cascade failures that occur with batch-style merge queues.

---

## Thread Safety

- A single `_ProcessLock` object gate-keeps entry to `ProcessQueueAsync`. The lock is checked-and-set inside a `lock` block. If `_Processing` is already `true`, the call returns immediately.
- Within a processing run, each vessel+target-branch group runs as an independent `Task`. Groups execute in parallel via `Task.WhenAll`. Each group task is wrapped in a `try-catch` (`ProcessGroupSafeAsync`) for error isolation, so a failure in one group does not cancel or affect other groups. Worktree paths under `_merge-queue/` are unique per entry, and groups update different merge-entry rows. Different groups can still point at the same bare repository if they target different branches on the same vessel.
- Within a single group, entries are processed strictly one at a time. There is no concurrency within a group.
- The lock is released in a `finally` block, so even if `Task.WhenAll` throws, the next call to `ProcessQueueAsync` will be able to proceed.

---

## Failure Scenarios

| Scenario | Behavior |
|---|---|
| **Merge conflict** | Entry marked `Failed` with message. Worktree cleaned up. Next entry in the same group continues. |
| **Test failure** | Entry marked `Failed` with exit code and truncated output. Worktree cleaned up. Next entry continues. |
| **Push failure** | Entry marked `Failed` with error message. Typically means the remote rejected the push (force-push protection, etc.). |
| **Vessel not found** | All entries in the group are marked `Failed` with a message indicating the vessel could not be resolved. |
| **Unexpected exception** | Entry marked `Failed` with error message. Best-effort worktree cleanup. Processing continues to the next entry. Group-level exceptions are caught by `ProcessGroupSafeAsync` and logged as warnings. |

---

## Best Practices

- **One branch per entry.** Each merge queue entry corresponds to a single feature branch being merged into a target branch.
- **Keep test commands fast.** Tests run synchronously per entry, blocking subsequent entries in the same group. Long tests slow down the entire group's queue throughput.
- **Use priorities.** Lower priority numbers are processed first within a group. Use this to land critical fixes ahead of routine changes.
- **Monitor terminal entries.** Use `armada_enumerate` with entityType 'merge_queue' and status 'Failed' to check for entries that may need attention.
- **Clean up regularly.** Use `armada_delete_merge` or `armada_purge_merge_queue` to remove terminal entries and their associated git branches.

---

## Commands Reference

| Tool | Description |
|---|---|
| `armada_enqueue_merge` | Add an entry to the merge queue. |
| `armada_process_merge_queue` | Trigger a processing run (no-op if already running). |
| `armada_process_merge_entry` | Process a single entry by ID. |
| `armada_get_merge_entry` | Get a single entry by ID. |
| `armada_cancel_merge` | Cancel a queued entry. |
| `armada_delete_merge` | Delete a terminal entry and clean up its branches. |
| `armada_purge_merge_queue` | Bulk delete all terminal entries, with optional vessel/status filters. |

---

## Landing Mode

When a mission's agent exits successfully, Armada sets the mission to `WorkProduced` and then applies the **landing mode** to determine how to integrate the work. The landing mode is resolved in priority order:

1. **Voyage-level** `LandingMode` (if the mission belongs to a voyage with a non-null `LandingMode`)
2. **Vessel-level** `LandingMode` (on the target vessel)
3. **Global** `LandingMode` (in `ArmadaSettings`)
4. **Legacy booleans** (`AutoPush`, `AutoCreatePullRequests`, `AutoMergePullRequests`) if all of the above are null

| Landing Mode | Behavior |
|---|---|
| `LocalMerge` | Merge the branch into the vessel's configured working directory and optionally push, but only when the vessel has both `WorkingDirectory` and `LocalPath` configured. Mission transitions to `Complete` on success or `LandingFailed` on failure. If those vessel paths are not configured, the mission remains at `WorkProduced`. |
| `PullRequest` | Create a pull request. Mission transitions to `PullRequestOpen`. Armada polls for merge confirmation; once merged, transitions to `Complete`. |
| `MergeQueue` | Enqueue the branch into Armada's merge queue for serialized testing and landing. |
| `None` | No automated landing. The mission stays at `WorkProduced` for manual handling. |

---

## Branch Cleanup Policy

After a mission's work has landed, Armada can automatically clean up the mission branch. The policy is resolved from the vessel level (falling back to global settings):

| Policy | Behavior |
|---|---|
| `LocalOnly` | Delete the local branch only (default) |
| `LocalAndRemote` | Delete both local and remote branches |
| `None` | Leave branches in place |

---

## Configuration

- **`LandingMode`** (in `ArmadaSettings`) -- global landing policy. Can be overridden per-vessel (`Vessel.LandingMode`) or per-voyage (`Voyage.LandingMode`).
- **`BranchCleanupPolicy`** (in `ArmadaSettings`) -- global branch cleanup policy. Can be overridden per-vessel (`Vessel.BranchCleanupPolicy`).
- **`MergeQueueTestCommand`** (in `ArmadaSettings`) -- default test command to run for entries that don't specify their own. Can be overridden per entry via the `testCommand` parameter on `armada_enqueue_merge`.
- **`DocksDirectory`** -- parent directory for temporary merge worktrees. Worktrees are created under `_merge-queue/` within this directory.
- **`ReposDirectory`** -- fallback repository path when a vessel's `LocalPath` is not set.
