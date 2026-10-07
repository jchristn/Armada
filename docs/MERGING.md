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
   2. Fetch latest refs from the remote (`git fetch`).
   3. Create a temporary worktree from the current target branch.
   4. Merge the entry's branch into the worktree (`git merge --no-ff`).
   5. If the merge conflicts, mark the entry `Failed` and move on.
   6. Run the configured test command (if any) through the platform shell (`/bin/sh -c` or `cmd.exe /c`). If tests fail, mark `Failed`.
   7. Push the integration branch to update the target (`git push origin integration:target`).
   8. Mark the entry `Landed`.
   9. Clean up the temporary worktree.

Every git and test subprocess run by the queue has a hard 10-minute timeout; a hung command is killed and the entry is marked `Failed` instead of wedging the queue.

When an entry has a linked mission (`missionId`), the mission is reconciled with the entry's outcome: `Landed` moves the mission to `Complete`, and `Failed` moves it to `LandingFailed` (with the failure reason recorded). Missions that are already `Complete`, `Failed`, or `Cancelled` are left alone.

Because each entry is landed immediately, the next entry in the same group always merges against the up-to-date target branch. This eliminates the cascade failures that occur with batch-style merge queues.

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
| **Push failure** | Entry marked `Failed` with error message. Linked mission moves to `LandingFailed`. Typically means the remote rejected the push (force-push protection, etc.). |
| **Timeout** | A git or test subprocess that runs longer than 10 minutes is killed; the entry is marked `Failed`. |
| **Vessel not found** | All entries in the group are marked `Failed` with a message indicating the repository path for the vessel could not be resolved. |
| **Unexpected exception** | Entry marked `Failed` with error message. Best-effort worktree cleanup. Processing continues to the next entry. Group-level exceptions are caught by `ProcessGroupSafeAsync` and logged as warnings. |

---

## Best Practices

- **One branch per entry.** Each merge queue entry corresponds to a single feature branch being merged into a target branch.
- **Keep test commands fast.** Tests run synchronously per entry, blocking subsequent entries in the same group. Long tests slow down the entire group's queue throughput.
- **Use priorities.** Lower priority numbers are processed first within a group. Use this to land critical fixes ahead of routine changes.
- **Monitor terminal entries.** Use `enumerate` with entityType 'merge_queue' and status 'Failed' to check for entries that may need attention.
- **Clean up regularly.** Use `delete_merge`, `purge_merge_entry`, `purge_merge_entries`, or `purge_merge_queue` to remove terminal entries and their associated git branches (local and remote, best effort).

---

## Commands Reference

| Tool | Description |
|---|---|
| `enqueue_merge` | Add an entry to the merge queue. Required: `vesselId`, `branchName`. Optional: `missionId`, `targetBranch` (default `main`), `priority` (default 0, lower first), `testCommand`. |
| `process_merge_queue` | Trigger a processing run (no-op if already running). |
| `process_merge_entry` | Process a single entry by ID. |
| `get_merge_entry` | Get a single entry by ID. |
| `cancel_merge` | Cancel a queued entry. |
| `delete_merge` | Delete a terminal entry and clean up its branches. |
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

After a mission's work has landed, Armada can automatically clean up the mission branch. The policy is resolved from the vessel level (falling back to global settings):

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
- **`DocksDirectory`** -- parent directory for temporary merge worktrees. Worktrees are created under `_merge-queue/` within this directory.
- **`ReposDirectory`** -- fallback repository path when a vessel's `LocalPath` is not set.
