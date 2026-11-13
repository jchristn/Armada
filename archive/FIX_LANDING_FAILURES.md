# Fix Landing Failures

> **Status (2026-10-29): implemented and landed on main in `ed4142772`, released in the v1.2.0 images.**
> Done: A1, A2 (bounded auto-retry of LocalMerge after a dirty checkout), A3, A4; all of B (P1, P4, reactive 1-2,
> B3, B4 fully; P2 as "recreate a missing dock at landing" rather than a fresh landing worktree every time; P3 as a
> branch-holds-commit check that records an event rather than a push); C2 (new failure kind `Blocked`; missions end
> Failed, not Blocked status); all of D; all of E. Not done: C1, C3, C4 (owned by the operator's concurrency and
> parent/child work) and per-voyage integration branches (planned to follow that work). Also added: the delete
> progress modal on every dashboard and TUI delete, bulk delete and purge.

Review of every mission currently in `Failed` or `LandingFailed` in Armada (snapshot 2026-10-29 02:40 UTC),
with root causes, categories, and fixes that keep each class of failure from coming back.

## Inventory

| # | Mission | Vessel | Status / FailureKind | Category |
|---|---|---|---|---|
| 1 | `msn_mvskboi0_vJGaqsNlGqU` [Rescue] Add vessel Cleanup modal | Armada | LandingFailed / Infra | A. Developer checkout gated landing |
| 2 | `msn_mvske56h_Zbv05D6cAlk` Land existing Cleanup feature on main | Armada | LandingFailed / Infra | A. Developer checkout gated landing |
| (1, 2) | parent `msn_mvsjeweh_tmCfX52gt3z` (already reconciled) | Armada | was LandingFailed / LandingConflict | B. Missing dock misclassified, duplicate recovery |
| 3 | `msn_mvsx8du2_2WLfCsthTkk` 2. Paginate backend and REST | Armada | Failed / NoOp | C. Voyage ordering not expressible |
| 4 | `msn_mvsx8fvp_Zzp1pZUit5S` 3. Bound configuration, nested and mutation | Armada | Failed / NoOp | C. Voyage ordering not expressible |
| 5 | `msn_mvsx8hvr_JcPJum8giql` 4. Migrate MCP, SDKs, dashboard, CLI | Armada | Failed / NoOp | C. Voyage ordering not expressible |
| 6 | `msn_mvsx8k5u_nbw0xobgeuf` 5. Verify zero unbounded surfaces | Armada | Failed / NoOp | C. Voyage ordering not expressible |
| 7 | `msn_mvsqzal6_b1BfR4tnZ29` Fix prompt_templates seeding race | Garrison | Failed / ScopeViolation | D. False scope violation |
| 8 | `msn_mvsx8blx_booyJi9ez9h` 1. Establish universal enumeration contract | Armada | Failed / OperatorAction | E. Operator stop, no reason recorded |
| 9 | `msn_mvsxcf46_OAQ6lzTdGaL` Fix false scope violations | Armada | Failed / OperatorAction | E. Operator stop, no reason recorded |
| 10 | `msn_mvsxd0q0_rGoF90DgP7d` AD0.2: version 0.2.0 | Garrison | Failed / OperatorAction | E. Operator stop, no reason recorded |

The merge queue has no `Failed` entries.

Category summary:

| Category | Missions | Wasted work | Root cause owner |
|---|---|---|---|
| A. Developer checkout gated landing | 2 | Two verified, pushed branches stranded | Landing pipeline |
| B. Missing dock misclassified | 1 (spawned 1, 2) | Feature implemented once, recovered twice in parallel | Landing pipeline, rescue policy |
| C. Voyage ordering not expressible | 4 | 4 captain runs plus 4 needless re-dispatches | Dispatch contract, completion classifier |
| D. False scope violation | 1 | Correct, tested fix discarded after 35 minutes | Scope validator, host command transport |
| E. Operator stop, no reason recorded | 3 | Unknown; in-progress work reclaimed with the dock | Captain stop path |

---

## A. Landing gated on the developer's primary checkout

### Missions

- **`msn_mvskboi0_vJGaqsNlGqU`** (rescue of the Cleanup feature). Captain produced a clean commit `6add5c4f0`
  with build and 1001 + 1143 tests passing. Landing failed with:
  `Error merging locally: Git checkout /Users/joelchristner/Code/Armada contains tracked modifications: M CHANGELOG.md ... M src/Armada.Core/Services/LandingService.cs ...`
  The staged files were the operator's in-progress landing rework (`LandingVerification.cs`,
  `GitUnpublishedCommitsException.cs`, `MissionArchive.cs`, ...), later committed as `d222eeeb1`.
- **`msn_mvske56h_Zbv05D6cAlk`** (operator-requested recovery of the same feature). Captain produced
  merge commit `8321ac09c`, pushed. Landing failed with:
  `Not merged: main in /Users/joelchristner/Code/Armada has 1 commit(s) that are not on origin/main ...`

### Root cause

Under `MergeAndPush`, the old landing path merged into the vessel's `WorkingDirectory` (the operator's own
checkout) and pushed from there. Any operator activity in that checkout (uncommitted edits, a local commit not
yet pushed) blocked every Armada landing on the vessel, even though the mission dock already held a verified,
pushable commit. Both failures were correctly classified `Infra`, but nothing retried them once the checkout was
clean again, so they stayed stranded.

### Current state

Commit `d222eeeb1` ("Verify exact landing commits and preserve recovery evidence") already changed the
`MergeAndPush` path in `src/Armada.Server/MissionLandingHandler.cs` (~line 446) to publish the verified commit
from the isolated dock with `PushVerifiedCommitAsync`, then only best-effort sync the developer checkout.
`ValidatePublishedAncestryAsync` (`src/Armada.Core/Services/GitService.cs:73`) now fails only when the candidate
itself contains unpublished checkout commits. Both stranded features did reach `origin/main` anyway
(`8321ac09c` and `8ef44a098`), so these two `LandingFailed` records are stale.

### Fixes

1. **Keep the dock-publish path and lock it with tests.** Add `LandingPathsSuite` cases proving that, for
   `MergeAndPush`, a dirty developer checkout and a developer checkout with unpushed local commits (not in the
   candidate's ancestry) both still land, and that the checkout sync afterward is skipped with a warning event
   rather than failing the mission.
2. **`LocalMerge` still requires a clean checkout** (`FastForwardVerifiedCommitAsync`, `GitService.cs:98`). Keep
   that, but make the failure recoverable without a person babysitting it: add a `LandingDeferred` path that
   records the mission as waiting on the checkout and re-attempts landing when the checkout reports clean
   (polled from the existing health loop, bounded by a deadline after which it becomes `LandingFailed`).
3. **Auto-reconcile stale `LandingFailed` records.** On each landing-health sweep, if a `LandingFailed`
   mission's `CommitHash` (or a commit carrying its `Armada-Mission-Id` trailer) is an ancestor of
   `origin/<default>`, transition it to `Complete` with an event `mission.landing_reconciled`. This would have
   cleaned up missions 1 and 2 automatically.
4. **Dashboard/inbox:** show the blocking checkout path and the exact `git status` lines for `Infra` landing
   failures, plus a one-click `retry_landing`.

### Option: per-voyage integration branches

Proposal: each voyage gets its own branch (for example `armada/voyage/<voyageId>`) cut from the default branch
at dispatch. Missions in the voyage start from it and land into it, and the voyage merges to the default branch
once, after its missions finish.

**Does it resolve A?** Partly. The two A failures happened because landing ran in the developer's checkout.
A voyage branch is written only by Armada, so mission landings would never touch the developer's checkout or
`main`, and that part of A goes away for every mission inside a voyage. The final voyage-to-main merge is still
a landing, though. It has the same exposure unless it also publishes from an isolated worktree, which is what
`d222eeeb1` now does for `MergeAndPush`. Voyage branches don't fix A on their own: they need the dock-publish
path, and they cut the number of landings that touch `main` from N per voyage to 1. `LocalMerge` into a dirty
checkout still needs fix A2. Standalone missions (no voyage), and voyages with one mission, should keep landing
directly so they don't pay for an extra merge.

**Concurrency to handle:**

1. **Locking is per vessel and per process today.** `_VesselMergeLocks` in `MissionLandingHandler.cs:49,206` is
   an in-memory `SemaphoreSlim`. It serializes landings only inside one Admiral process. It does not cover
   multiple Admiral replicas, and it does not cover a Harbor and the Admiral working on the same repository.
   Voyage branches need a lock per target branch (voyage branch or `main`), held as a database-backed lease
   with an expiry, so the lock holds across processes and two voyages can land into their own branches in
   parallel.
2. **Missions in a voyage land into the same branch.** Sibling missions that run concurrently all branch from
   the same voyage snapshot, so by the time each lands its base is stale. Each landing must integrate the
   current voyage head and re-verify, the same way target synchronization works for `main` today.
3. **Push races.** `PushVerifiedCommitAsync` does a normal, non-force push, which correctly fails if the target
   moved. Add a bounded loop: fetch, integrate the new head, re-run verification, push again. After N attempts,
   fail as `LandingConflict` with both heads recorded.
4. **Voyage branch drift from `main`.** Other voyages and standalone missions keep landing on `main` while a
   voyage runs, so conflicts only show up at the final merge, and they are larger than per-mission conflicts.
   Mitigations: merge `main` into the voyage branch on a schedule or after each mission lands there, and
   flag the voyage early when that refresh conflicts.
5. **Voyages finishing together.** Two voyages can finish at the same moment and both try to merge to `main`.
   Send voyage-to-main merges through the existing merge queue. `MergeQueueService` already builds
   `armada/merge-queue/<id>` integration branches and serializes them, so this is reuse, not new machinery.
6. **Verifying the combined result.** Each mission passed against the voyage branch, but the voyage branch
   combined with current `main` has never been tested. The voyage merge must run the vessel's full check, not
   per-mission checks.
7. **Partial voyages.** When some missions fail or are cancelled, we need a policy: merge what landed, hold the
   voyage for an operator, or abandon it. The default should be to hold. Parent/child relationships decide
   which unfinished missions block the merge.
8. **Retry and rescue targets.** `retry_landing`, rescue missions and the reconciler in A3 have to know whether a
   mission's target is the voyage branch or `main`. Store the landing target on the mission, not derive it.
9. **Cleanup.** The voyage branch counts as in-use (for Vessel Cleanup and branch cleanup policy) while the
   voyage is open or its merge entry is queued. Delete it after a confirmed merge to `main`.
10. **Developer checkout sync.** After the voyage lands, `SyncCheckoutAfterPrMergeAsync` updates the developer
    checkout on a best-effort basis while the developer may be editing it. It must never fail the landing
    (the same as today), and it must skip a dirty checkout rather than stash or reset it.

Your planned concurrency controls and parent/child relationships (category C) slot in here: a child starts from
the voyage branch after its parent has landed there, which also removes the stale-base problem in item 2 for
dependent missions.

---

## B. Missing dock directory misclassified as a landing conflict, then recovered twice

### Mission

Parent `msn_mvsjeweh_tmCfX52gt3z` (now reconciled, but it caused 1 and 2). Its landing failed with
`Current-target integration failed: An error occurred trying to start process '/usr/bin/git' with working directory '/Users/joelchristner/.armada-harbor/docks/Armada/msn_mvsjeweh_..._dck_mvsjewja_IMSQH7QfyJo'. No such file or directory`,
classified `LandingConflict`.

### Root cause

1. `MissionLandingHandler.HandleMissionCompleteAsync` (`src/Armada.Server/MissionLandingHandler.cs:226-253`)
   runs git in `dock.WorktreePath` without checking that the directory still exists. The catch block defaults
   `landingFailureKind` to `LandingConflict` for every exception except `GitUnpublishedCommitsException`.
2. `LandingConflict` triggers an autonomous rescue mission. The rescue (`msn_mvskboi0`) was told to
   "re-implement the change", and at the same time the operator dispatched `msn_mvske56h` to recover the same
   commit. Two captains redid the same 31-file integration in parallel, and both ended up on main
   (`8ef44a098` and `8321ac09c`).
3. The captain filed a `PlatformBug` papercut for exactly this ("Landing fails permanently when the mission dock
   directory is missing").

`LandingService` already has a dock-existence check (`src/Armada.Core/Services/LandingService.cs:266-282`) that
is Harbor-aware; the mission-complete landing path does not use it.

### Timeline (parent mission events)

| UTC (2026-10-28) | Event |
|---|---|
| 20:21:43.09 | captain process exited, code 0 |
| 20:24:05.36 | stage check passed (ran in the dock, so the dock existed) |
| 20:24:06.20 | `mission.work_produced` |
| 20:24:07.79 | `mission.landing_failed`: dock directory missing |

The dock was deleted during the 2.4 seconds between the passing stage check and landing. That points to a race
inside completion handling, not to outside interference. Several code paths reclaim docks, each with its own
status test (`MissionService.Completion.cs:777-790`, `MissionService.cs:1261,1415`,
`AdmiralService.cs:1652,1737,1859,2346`). If any of them reads the mission status before landing has claimed
the dock, it can delete the worktree that landing is about to use. The events don't say which path did it.
The first step is to add a `dock.reclaimed` event that records the calling path and the mission status at the
time of the call.

### Prophylactic fixes (prevent the dock from disappearing)

These come first. They stop the failure from happening instead of recovering from it afterwards.

P1. **Dock leases enforced in one place.** `DockService.ReclaimAsync` refuses to remove a dock while any
    mission that references it is in `WorkProduced`, `Testing`, `Review`, `Verifying`, `PullRequestOpen` or
    `LandingFailed`, or while a merge queue entry references it, unless the caller passes an explicit
    `force` reason. This replaces the separate status checks at each call site, so a new reclaim path can't
    reintroduce the race. Add a test that runs completion and a reclaim concurrently.
P2. **Landing doesn't depend on the captain's dock.** At landing time, create a short-lived landing worktree
    from the mission branch ref (`git worktree add <tmp> <branch>`) on the owning host, integrate, verify and
    publish there, and then remove it. The captain's dock becomes disposable as soon as the branch is
    pushed, and a missing dock directory can no longer fail a landing.
P3. **Push the branch before anything can reclaim.** Make "branch pushed and its head equals
    `mission.CommitHash`" a precondition for `WorkProduced`. Recovery then always has a durable source.
P4. **Integrity sweep.** In the existing health loop, for every unlanded mission (statuses in P1), check on the
    owning host that its dock exists. If it is missing, rebuild it from the branch and emit
    `dock.repaired`. That fixes problems before a landing or `retry_landing` runs into them.

### Reactive fixes (backstop if a dock is still missing)

1. **Check the dock before landing.** In `HandleMissionCompleteAsync`, call the same Harbor-aware existence
   check before the first git call. If the worktree is gone but the branch exists in the repository (or on
   origin), recreate the worktree from the branch (the `repair_dock` primitive) and continue. If the branch is
   gone too, fail with `FailureKind = Infra` and a reason naming the missing path and branch.
2. **Classify by exception, not by default.** Change the default `landingFailureKind` to `Infra` for process
   start failures (`Win32Exception`, `DirectoryNotFoundException`, `IOException`), and set `LandingConflict` only
   when git actually reports a merge conflict (the integration helper should throw a dedicated
   `GitMergeConflictException`). Add `LandingConflictSuite` cases for "dock missing" and "git cannot start".
3. **Rescue should reuse pushed work.** When the failed mission has a pushed `CommitHash`, the rescue brief
   should say "integrate commit X onto the current target" rather than "re-implement", and the rescue should
   be created with the parent's branch as its starting point.
4. **Deduplicate recovery.** Before creating a rescue, and before accepting a new dispatch whose description
   references a `LandingFailed` mission ID, check for another active mission with the same `ParentMissionId` or
   referencing the same parent/commit; if one exists, do not start a second one (return a conflict naming the
   active mission).

---

## C. Voyage missions require an order that dispatch cannot express

### Missions

`msn_mvsx8du2`, `msn_mvsx8fvp`, `msn_mvsx8hvr`, `msn_mvsx8k5u` (missions 2 to 5 of voyage
`vyg_mvsx8blj_sK1vK8ck0XT`, the universal pagination migration). Each brief says "Execute these five missions in
numbered dependency order ... do not race overlapping contract changes", and missions 2 to 5 say
"Depends on mission N". All five were dispatched within about 11 seconds with `DependsOnMissionId = null` and ran
concurrently on the same Harbor.

Every captain did the right thing: it inspected the branches, found the prerequisites had not landed, made no
changes, emitted `[ARMADA:MESSAGE] BLOCKED: ...` and a `BriefContradiction` papercut of severity High. Armada
then:

- treated each as an "unfinished completion", re-dispatched it once (another full captain run that reached the
  same conclusion), and
- failed it with `FailureKind = NoOp` and the reason "The model is stopping mid-task; check its context window
  and output limits, or use a stronger captain", which is wrong and sends the operator looking at the model.

> **Owner note:** the operator plans to resolve this category with upcoming concurrency controls and
> parent/child mission relationships. Fixes C1, C3 and C4 below are kept as reference for that work. C2
> (treating BLOCKED as a real outcome rather than a no-op) is separate from dependency ordering and is still
> worth doing on its own.

### Root cause

1. **The `dispatch` contract has no dependency field.** `MissionDescription`
   (`src/Armada.Core/Models/MissionDescription.cs`) exposes `Title`, `Description`, `Tier`,
   `RequestedCaptainId`, `Persona`, and nothing else, and the MCP `dispatch` schema
   (`src/Armada.Server/Mcp/Tools/McpVoyageTools.cs:40-70`) mirrors it. `DependsOnMissionId` exists on `Mission`
   and in every database provider, but only the architect plan path can set it. A caller who writes "in order"
   in prose has no way to make the scheduler honour it.
2. **`BLOCKED` is only recognised for verifier personas.** `VerifierContract.IsBlocked` is gated by
   `VerifierContract.Applies(persona)` (`MissionService.Completion.cs:230,602`). A Worker that reports a real
   blocker is indistinguishable from a captain that died mid-task, so the NoOp re-dispatch loop
   (`MissionService.Completion.cs:255-290`) burns a retry and then writes a misleading reason.
3. **High-severity `BriefContradiction` papercuts are not acted on.** Four captains independently reported the
   same dispatch defect, and nothing paused the voyage.

### Fixes

1. **Add ordering to dispatch.** Add `DependsOn` to `MissionDescription` as either a zero-based index into the
   same request's `missions` array or an existing mission ID, and a voyage-level `sequential: true` shorthand
   that chains each mission to the previous one. Resolve indexes to `DependsOnMissionId` after the missions are
   created, reject cycles and out-of-range indexes with 400. Wire it through REST, MCP (`dispatch` schema and
   description), `Armada.Client`, the dashboard dispatch form, and the CLI. A dependent mission stays `Pending`
   until its dependency is `Complete` (landed), and is cancelled with `DependencyFailed` if the dependency
   fails. Update the shared Touchstone, REST/MCP and client-surface tests.
2. **Recognise `BLOCKED` from any persona.** Add a general `[ARMADA:RESULT] BLOCKED` result line to the base
   output contract (`PromptTemplateService`, `MissionService.Instructions`). When a mission ends with it (or
   with `[ARMADA:MESSAGE] BLOCKED:` and no diff), skip the NoOp re-dispatch, set `Status = Blocked`,
   `FailureKind = DependencyFailed` (or a new `BriefBlocked`), and copy the captain's blocking message into
   `FailureReason`. Only use the "model is stopping mid-task" wording when the output has no result line and no
   blocking message at all.
3. **Lint the brief at dispatch time.** When a voyage's mission descriptions contain ordering language
   ("depends on mission", "in numbered order", "after mission N lands") and no `DependsOn` is set, return a
   warning in the dispatch response and on the voyage (or refuse with 422 when `strict` is set). This is cheap
   and would have caught this voyage before any captain ran.
4. **Escalate repeated papercuts.** When two or more missions in the same voyage file a `BriefContradiction` of
   severity High, pause the voyage's remaining `Pending`/`Assigned` missions and raise one inbox item.

---

## D. False scope violation on a correct change

### Mission

`msn_mvsqzal6_b1BfR4tnZ29` (Garrison, prompt_templates seeding race). The captain changed only
`src/Garrison.Server/Protocols/GarrisonMcpServer.cs`, exactly what the brief asked, with full solution tests
passing on both frameworks. It failed with
`Mission modified files outside its scoped file list: , src/Garrison.Server/Protocols/GarrisonMcpServer.cs`.
The operator reported a second Garrison mission with the same symptom and an empty list
(`...scoped file list: ` with nothing after the colon).

### Root cause

Two independent defects, both confirmed in source:

1. **A phantom empty path.** `LocalHostCommandExecutor` (`src/Armada.Core/Services/LocalHostCommandExecutor.cs:46`)
   reads stdout line by line and re-joins with `AppendLine`, so NUL-separated git output `a\0b\0` becomes
   `a\0b\0\n`. `GitMachineOutputParser.SplitNul` (`GitMachineOutputParser.cs:22-31`) only drops a trailing field
   when it is exactly empty, so the final `"\n"` survives; `ParsePathListZ` keeps it because its length is 1;
   `NormalizeMissionPath` then trims it to `""`, which is not in the allow list. This also explains the second
   case: the only out-of-scope "file" was the empty string.
2. **Bare file names never match.** The brief said "Touch only GarrisonMcpServer.cs". The directive parser adds
   the bare token `GarrisonMcpServer.cs` to the allow list, and the validator
   (`MissionService.cs:1572-1585`) compares it for exact equality with the repo-relative path.

The validator also builds a `FailureReason` from whatever is in `outOfScopeFiles`, including empty entries.

Related, from the same mission's first attempt: the stage-check output was truncated before the failing test
name (only the summary lines survived), so the captain could not identify which test had failed. That is what
caused the redispatch the captain later completed.

### Fixes

1. **Fix the transport at the source.** Make `LocalHostCommandExecutor` capture stdout as a raw stream
   (`process.StandardOutput.ReadToEndAsync()`) instead of line events, so bytes are returned unchanged on both
   the Admiral and the Harbor. Verify the Harbor command transport returns the same bytes for the same command
   (add a shared conformance test that runs `printf 'a\0b\0'` through both executors).
2. **Harden the parser.** `SplitNul` drops a final field that is empty after trimming `\r` and `\n`;
   `ParsePathListZ` skips fields that are empty after trimming.
3. **Harden the validator.** Skip empty normalized paths; treat an allow-list token with no `/` as matching any
   changed file with that file name (case-insensitive); never fail when the filtered out-of-scope list is
   empty; never build a `FailureReason` with an empty list.
4. **Keep failing test names in stage-check output.** When truncating check output for the brief, keep the
   head and tail and also extract lines matching the runners' failure markers (`Failed `, `[FAIL]`, `X `, `not ok`)
   into a dedicated "Failing tests" section that is never truncated.
5. Mission `msn_mvsxcf46_OAQ6lzTdGaL` was already implementing fixes 1 to 3 (its log confirms the
   `AppendLine` diagnosis) before it was stopped; see E. Re-dispatching it with the brief above is the shortest
   path once the operator wants it run.

---

## E. Operator stops recorded as failures with no reason

### Missions

`msn_mvsx8blx_booyJi9ez9h`, `msn_mvsxcf46_OAQ6lzTdGaL`, `msn_mvsxd0q0_rGoF90DgP7d` all moved to
`Failed / OperatorAction` between 02:37:59 and 02:38:01 UTC, with `FailureReason = null` and no event other
than `captain.launched`. The scope-fix log ends mid-edit, so the captain was stopped while working. These were
operator-initiated (and, per standing guidance, live failed/cancelled work is deliberate testing and is not to
be restarted by an agent).

### Root cause

`CaptainService` stop path (`src/Armada.Core/Services/CaptainService.cs:113-128`) marks the current mission
`Failed` with `FailureKind = OperatorAction` but sets no `FailureReason`, emits no mission event, and then
reclaims the dock worktree, discarding any uncommitted work. An operator stop is reported as a failure that
looks identical to an unexplained crash, and it inflates failure counts on the dashboard and in captain
evaluation.

### Fixes

1. **Record who stopped it and how.** Set `FailureReason` to `"Stopped by operator (<user>) via <stop_captain | stop_all | dashboard> at <UTC>"`
   and emit a `mission.stopped_by_operator` event with the same data.
2. **Use `Cancelled` for operator stops** (or keep `Failed` but exclude `OperatorAction` from failure-rate
   metrics, captain evaluation, and the inbox failure feed). Cancelled is closer to the truth and keeps the
   failure views meaningful.
3. **Preserve work in progress.** Before reclaiming the dock, if the worktree has changes, commit them to the
   mission branch as a WIP commit (or `git stash` with the mission ID) and push the branch, so a restart can
   continue from the captain's edits instead of starting over.

---

## Cleanup of the current records (operator decision, not automated)

- `msn_mvskboi0_vJGaqsNlGqU` and `msn_mvske56h_Zbv05D6cAlk`: the Cleanup feature is on `origin/main`
  (`b50d2275a`, `8321ac09c` and `8ef44a098` are all ancestors). These records can be moved to `Complete`; the
  rescue commit `6add5c4f0` itself never landed and its branch can be deleted.
- Pagination voyage `vyg_mvsx8blj_sK1vK8ck0XT`: re-plan with `DependsOn` once fix C1 exists, or dispatch one
  mission at a time.
- `msn_mvsqzal6_b1BfR4tnZ29`: the commit `b8c7d9a` is correct; it can be landed manually or retried after fix D.

## Suggested order of work

1. D1 to D3 (small, self-contained, removes false failures on every Harbor vessel).
2. E1 to E3 (operator stop clarity and WIP preservation).
3. B: `dock.reclaimed` diagnostics, then P1 (central dock leases) and P2 (ephemeral landing worktree), then
   P3, P4 and reactive fixes 1 and 2 as a backstop.
4. C2 (correct BLOCKED handling; stops wasted re-dispatches and misleading reasons).
5. A1 to A4 and B3, B4 (landing regression tests, deferred LocalMerge, auto-reconcile, rescue dedupe).
6. Operator's concurrency controls and parent/child relationships (covers C1, C3, C4), then per-voyage
   integration branches on top of them, starting with the cross-process landing lease (A, concurrency item 1).
7. D4 (failing-test extraction in stage-check output).
