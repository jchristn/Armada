# CODEX_FEEDBACK.md Response

This document details what was implemented in response to the original feedback in `CODEX_FEEDBACK.md`, the rationale behind each change, justification for anything that was not done, and a thorough assessment of the second-round feedback.

---

## Part 1: What Was Implemented

Six tasks were implemented sequentially, each committed, pushed, and verified before proceeding to the next.

---

### Task 1: PullRequestOpen Mission Status

**Feedback concern:** When Armada creates a PR for a completed mission, the mission is marked `Complete` immediately — but the code hasn't actually landed yet. If the PR fails to merge, the mission is incorrectly reported as complete.

**What was done:**

- Added `PullRequestOpen` to `MissionStatusEnum` (value inserted between `WorkProduced` and `Testing`).
- Updated the PR creation flow in `ArmadaServer.cs` to set `PullRequestOpen` instead of `Complete` when a PR is created. The mission emits a `mission.pull_request_open` event and broadcasts via WebSocket.
- Updated `PollAndPullAfterMergeAsync` to transition from `PullRequestOpen` to `Complete` only when the PR merge is confirmed.
- Added valid status transitions: `WorkProduced -> PullRequestOpen`, `PullRequestOpen -> Complete`, `PullRequestOpen -> LandingFailed`, `PullRequestOpen -> Cancelled`.
- Updated `AdmiralService.HealthCheckAsync` to recognize `PullRequestOpen` as a post-work state (so it doesn't mistakenly think the captain is stuck).
- Updated `VoyageService` so `PullRequestOpen` counts as in-progress (a voyage does not complete while any mission is in `PullRequestOpen`).
- Added color (`dodgerblue1`) and emoji display for `PullRequestOpen` in the Helm table renderer.
- Updated all API and agent instruction documentation to include the new status.

**Why:** This was the highest-priority semantic correctness issue. `Complete` must mean "code has landed," not "PR was created." Without this, monitoring tools and voyage completion logic produce false positives.

---

### Task 2: Explicit Landing Mode (LandingModeEnum)

**Feedback concern:** The landing strategy is derived from a combination of boolean flags (`AutoPush`, `AutoCreatePullRequests`, `AutoMergePullRequests`), making it difficult to reason about which landing path will execute. There's no way to set the landing mode per-vessel or per-voyage.

**What was done:**

- Created `LandingModeEnum` with values: `LocalMerge`, `PullRequest`, `MergeQueue`, `None`.
- Added `LandingMode` property to `Vessel`, `Voyage`, and `ArmadaSettings`.
- Implemented three-tier resolution in `ArmadaServer.HandleMissionCompleteAsync`: voyage-level > vessel-level > global setting, with fallback to legacy booleans when all are null.
- Added database columns and schema migrations (migration 10) across all four database drivers (SQLite, PostgreSQL, MySQL, SQL Server).
- Updated all API documentation with the new enum and model properties.

**Why:** An explicit enum eliminates the combinatorial confusion of three booleans and enables per-vessel/per-voyage configuration — critical for organizations with heterogeneous repository policies.

**What was NOT done:** The legacy boolean flags (`AutoPush`, `AutoCreatePullRequests`, `AutoMergePullRequests`) were not removed. They serve as a backward-compatible fallback when `LandingMode` is null. Removing them would be a breaking change that should be deferred to a major version.

---

### Task 3: Manual Complete Routes Through Landing Pipeline

**Feedback concern:** When a user manually transitions a mission to `Complete` via `armada_transition_mission_status`, it bypasses the landing pipeline entirely — no diff capture, no merge, no PR creation. This creates an inconsistency where the manual path produces different outcomes than the agent-driven path.

**What was done:**

- Modified `ArmadaServer.HandleTransitionMissionStatusAsync` so that when a mission is transitioned to `Complete` and it has an active dock, the request is routed through `HandleMissionCompleteAsync` instead of directly setting the status.
- This ensures diff capture, `WorkProduced` transition, and the full landing pipeline are invoked regardless of whether the trigger is an agent exit or a manual status change.

**Why:** The principle of least surprise. A user transitioning to `Complete` expects the same outcome as an agent completing — code landed, branch cleaned up, diff captured.

---

### Task 4: BranchCleanupPolicyEnum

**Feedback concern:** After a mission lands, the branch cleanup behavior is implicit and inconsistent. Local branches may or may not be deleted, and remote branches are never cleaned up.

**What was done:**

- Created `BranchCleanupPolicyEnum` with values: `LocalOnly`, `LocalAndRemote`, `None`.
- Added `BranchCleanupPolicy` property to `Vessel` and `ArmadaSettings` (default: `LocalOnly`).
- Added `DeleteRemoteBranchAsync` to `IGitService`/`GitService` (`git push origin --delete <branch>`).
- Updated both local merge and PR merge paths in `ArmadaServer` to respect the cleanup policy.
- Added database column and schema migration (migration 11) across all four database drivers.
- Updated all documentation.

**Why:** Branch hygiene is essential at scale. Without explicit cleanup, repositories accumulate hundreds of stale branches, making `git branch` and remote UI unusable.

**What was NOT done:** Per-voyage `BranchCleanupPolicy` was not added. Unlike landing mode (which may genuinely differ per voyage), branch cleanup is a repository-level concern. Per-vessel + global is sufficient granularity.

---

### Task 5: DockService.ReclaimAsync Idempotency

**Feedback concern:** Both `MissionService` (background finalizer) and `ArmadaServer` (`HandleMissionCompleteAsync`) can call `ReclaimAsync` for the same dock, leading to duplicate worktree removal attempts and spurious error logs.

**What was done:**

- Added an idempotency guard at the top of `DockService.ReclaimAsync`: if the dock is already inactive (`Active == false`), the method returns immediately with a debug log.
- This makes double-reclaim a safe no-op.

**Why:** Simple, surgical fix for a real race condition. The guard prevents redundant filesystem operations and eliminates confusing warning logs.

---

### Task 6: Integration Tests for the Landing Pipeline

**Feedback concern:** The landing pipeline had no dedicated test coverage. Changes to status transitions, landing modes, or branch cleanup could silently break the pipeline.

**What was done:**

- Created `LandingPipelineTests.cs` with 11 integration-style tests:
  - `WorkProduced` flow from `HandleCompletionAsync`
  - Local merge call sequence verification
  - Merge failure sets `LandingFailed`
  - `LandingMode` persistence (vessel and voyage)
  - Null `LandingMode` round-trip
  - `PullRequestOpen` blocks voyage completion
  - Dock reclaim idempotency
  - Status transition validation
  - Enum value verification for `LandingModeEnum` and `BranchCleanupPolicyEnum`
- Fixed all pre-existing test failures caused by the Task 1 changes:
  - `SequentialDispatchTests`: 5 tests updated to expect `WorkProduced` instead of `Complete`, and `mission.work_produced` event instead of `mission.completed`.
  - `AdmiralServiceTests`: `DispatchVoyageAsync` test updated to expect `VoyageStatusEnum.Open` (correct when no captains are available); health check test updated to expect orphaned Working captain with no mission is correctly released to Idle.
- All 491 unit tests passing.

**Why:** The landing pipeline is the most critical post-agent path in Armada. Every change in Tasks 1-5 touches it. Without tests, regressions are invisible until production.

---

### Documentation Updates

All documentation was updated as part of this effort:

- **MCP_API.md, REST_API.md, WEBSOCKET_API.md**: Added `PullRequestOpen`, `WorkProduced`, `LandingFailed` to `MissionStatusEnum` tables. Added `PullRequestOpen` transitions to status transition tables. Added `LandingModeEnum` and `BranchCleanupPolicyEnum` enum documentation. Added `LandingMode` and `BranchCleanupPolicy` to Vessel and Voyage model tables.
- **MERGING.md**: Added Landing Mode section with resolution order, Branch Cleanup Policy section, and updated configuration reference.
- **README.md**: Added `LandingMode` and `BranchCleanupPolicy` to the settings table.
- **INSTRUCTIONS_FOR_CLAUDE_CODE.md, INSTRUCTIONS_FOR_CODEX.md, INSTRUCTIONS_FOR_CURSOR.md, INSTRUCTIONS_FOR_GEMINI.md**: Added `PullRequestOpen` to status filter lists. Strengthened guidance to prefer `armada_enumerate` over `armada_list_*` tools.

---

## Part 2: Assessment of Second-Round Feedback

After the six tasks and documentation updates were completed, a second round of feedback was provided in `CODEX_FEEDBACK.md`. This section provides a thorough assessment of every claim in that feedback: where it is correct, where it overstates severity, and where it is wrong.

---

### Overall Assessment of the Feedback

The second-round feedback is well-reasoned and honest. It correctly identifies the MergeQueue gap as the single biggest remaining issue and accurately characterizes the event emission leak on the PR path. The tone is constructive and the priority ordering is sensible. Where it falls short is in a few places where it applies architecture-purity standards to problems that are already adequately solved by pragmatic design, and one place where it recommends a testing strategy that would produce worse outcomes than the current approach.

---

### Where Codex Is Right

#### Priority 1: MergeQueue Auto-Enqueue

**Codex's claim:** `LandingMode.MergeQueue` is presented as a landing mode, but the current implementation does not actually enqueue automatically. It logs guidance and leaves the mission at `WorkProduced`, expecting the operator to call `armada_enqueue_merge` manually.

**Our assessment: Codex is correct. This is the biggest remaining gap.**

The enum value `MergeQueue` makes a promise the code does not keep. When a user configures `LandingMode = MergeQueue` on a vessel, they reasonably expect that completed missions will be automatically enqueued into the merge queue. Instead, they get a log message telling them to do it themselves. That is a handoff mechanism, not a landing mode.

The right fix is to auto-create a `MergeEntry` in the landing handler when the resolved landing mode is `MergeQueue`. The entry should be created with status `Queued`, targeting the vessel's default branch, using the mission's branch name. Processing (the actual test-and-merge cycle) should remain a separate trigger via `armada_process_merge_queue` — that separation is correct because processing policy (when to run, how often) is distinct from enqueue policy (what to do when work is produced).

This is a real contract gap between the enum name, the documentation, and the implementation. It should be fixed.

#### Priority 2: PR Path Duplicate Event Emission

**Codex's claim:** The PR creation path emits a specific `mission.pull_request_open` event correctly, but then the generic broadcast block at the bottom of `HandleMissionCompleteAsync` evaluates the `landingAttempted` and `landingSucceeded` boolean flags, finds both false (because the PR path resets them), and emits a `mission.work_produced` broadcast. This produces two events for a single PR creation — one correct, one misleading.

**Our assessment: Codex is correct. This is a legitimate semantic leak.**

The mission's database status is correct (`PullRequestOpen`), so there is no data corruption or state machine bug. But WebSocket listeners and event consumers see both `mission.pull_request_open` and `mission.work_produced` for the same mission in the same completion cycle. That is confusing for dashboards, automation hooks, and anyone building on the event stream.

The fix is straightforward: the final broadcast block should derive the event type from `mission.Status` rather than from the two boolean flags. If the status is already `PullRequestOpen`, the broadcast should emit `mission.pull_request_open` (or skip the broadcast entirely since it was already emitted). This is a small change with clear correctness improvement.

#### Priority 4: Deeper Landing-Flow Tests

**Codex's claim:** The `LandingPipelineTests` are useful but do not exercise the full `ArmadaServer.HandleMissionCompleteAsync` landing logic. The highest-risk code paths (PR open to complete, merge queue behavior, branch cleanup, final event emission) are only partially covered.

**Our assessment: Codex is correct that there is a coverage gap, but wrong about the recommended solution (see below). The gap itself is real.**

The `LandingPipelineTests` are honest about their scope — they test at the service layer and verify persistence, state setup, enum existence, and idempotency. They do not drive the `ArmadaServer.HandleMissionCompleteAsync` method, which is where the actual landing orchestration happens. That method is the highest-risk code in Armada and it deserves dedicated coverage. See the "Where Codex Is Wrong" section below for why the recommended approach (unit tests with stubs/mocks) is not the right solution.

---

### Where Codex Is Technically Right but Overstates the Severity

#### Priority 3: Manual Complete Without Dock

**Codex's claim:** Manual completion still has two behaviors depending on whether the worktree exists. If a dock is present, the request routes through the landing pipeline. If no dock exists, it falls back to a direct status update. Codex recommends either rejecting the direct `Complete` transition or allowing only `WorkProduced`/`LandingFailed` when the dock is missing.

**Our assessment: Codex is technically correct that two paths exist, but the recommendation would make the system worse, not better.**

The no-dock path is genuinely a different scenario from the dock-exists path. The worktree may be gone because:

- The server restarted and the dock was reclaimed during cleanup.
- The mission was partially failed and the dock was already torn down.
- An operator is cleaning up after a manual intervention.

In all of these cases, the operator is making an explicit decision to mark the mission `Complete` knowing the current state. Rejecting the transition outright would frustrate operators who are cleaning up after partial failures or restarts. Restricting to `WorkProduced`/`LandingFailed` forces operators into a multi-step dance when they already know the outcome they want.

**What we would do instead:** Add a warning event (`mission.manual_complete_no_dock` or similar) so the action is logged and auditable, but do not block the transition. The operator chose to mark it Complete — let them, but make it visible in the event stream. This preserves operational flexibility while maintaining audit trail integrity.

#### Item 4: Dual Reclaim Initiation (Cleanup Ownership)

**Codex's claim:** The idempotency guard fixes the correctness issue, but reclaim is still initiated from two places (`MissionService` background finalizer and `ArmadaServer` completion handler). This makes the lifecycle harder to reason about than necessary. Codex recommends making one layer responsible for triggering reclaim.

**Our assessment: Codex is right that two call sites exist, but wrong that this is a problem worth solving.**

Both call sites are legitimate and serve different purposes:

- **`ArmadaServer` completion handler:** The fast path. When a mission completes normally, the dock should be reclaimed immediately so the captain can be reassigned. Waiting for the background tick would add unnecessary latency.
- **`MissionService` background finalizer:** The safety net. If the fast path fails (exception, server crash between completion and reclaim, etc.), the background finalizer catches it on the next health check cycle.

Consolidating to one call site means either:

1. **Only the fast path:** Then a crash between completion and reclaim leaks the dock until manual intervention. The safety net disappears.
2. **Only the background tick:** Then every mission completion adds 10+ seconds of latency (the health check interval) before the captain becomes available for the next mission. This directly degrades throughput in sequential dispatch scenarios.

The current design — two callers, idempotent target — is the standard pattern for reliable resource cleanup in distributed systems. It is the same pattern used by Kubernetes for pod cleanup, by database connection pools for connection return, and by most task queue systems for worker release. The `Active` guard makes it cheap and safe. There is no correctness issue, no performance issue, and no readability issue that justifies changing this.

**Our recommendation: Leave as-is.** This is architecture taste, not a bug.

#### Item 6: Dock Cleanup Heuristics in ProvisionAsync

**Codex's claim:** `DockService.ProvisionAsync` deletes non-current captain directories based on directory heuristics (checking for `.git` file/directory presence), which is broader than ideal for a maturing system.

**Our assessment: Codex is right that the heuristic is broad, but wrong that it matters in practice.**

The stale worktree cleanup in `ProvisionAsync` solves a real and painful problem: renamed or deleted captains leave behind orphaned worktrees that cause `git fetch` to fail with "refusing to fetch into branch checked out at..." errors. Without this cleanup, provisioning new docks fails silently and operators must manually intervene.

The heuristic is conservative:

1. It only examines directories under the vessel's dock directory (not arbitrary filesystem paths).
2. It checks for `.git` file or directory presence (only git-managed directories are touched).
3. It checks whether the worktree is registered with the bare repo before attempting `git worktree remove`.
4. It skips the current captain's directory entirely.

Could it be more precise? Yes — it could cross-reference against the captains table to verify the directory belongs to a no-longer-active captain. But this would add a database query per directory, and the current approach has been reliable in production with no false positives. The risk/reward ratio of changing it is unfavorable.

**Our recommendation: Leave as-is.** If a false positive is ever observed, tighten the heuristic then.

---

### Where Codex Is Wrong

#### Item 5: Recommendation to Add ArmadaServer-Level Unit Tests with Stubs/Mocks

**Codex's claim:** Add tests that directly drive the `ArmadaServer` landing flow, even if with stubs/mocks, so the actual post-mission state machine is covered.

**Our assessment: This recommendation would produce worse test quality, not better.**

`ArmadaServer` is the composition root of the entire system. `HandleMissionCompleteAsync` alone orchestrates:

- Database reads and writes (missions, captains, docks, vessels, voyages, events)
- Git operations (diff, merge, push, branch delete, worktree remove)
- GitHub API calls (PR creation, PR status polling)
- WebSocket broadcasts
- HTTP client calls (notifications)
- Merge queue service interactions
- Captain lifecycle management

Testing this method at the unit level requires mocking or stubbing **all** of these dependencies. The result is a test that:

- Is extremely brittle — any refactor to the method's internal structure breaks the test, even if behavior is preserved.
- Tests mock wiring rather than actual behavior — "verify that `MockGitService.MergeLocal` was called with these arguments" does not prove the merge actually works.
- Provides false confidence — a passing test means the mocks were set up correctly, not that the system works correctly.
- Is expensive to maintain — every new dependency or internal restructuring requires updating the mock setup across multiple tests.

This is the classic "integration-unit hybrid" antipattern. It looks like thorough testing but actually couples the tests to implementation details rather than observable behavior.

**The right approach is to cover `ArmadaServer`-level flows in `Armada.Test.Automated`, which runs against a real server with real git repositories.** That suite exercises the actual composition, the actual git operations, and the actual HTTP/WebSocket interactions. It is slower to run but produces genuinely meaningful coverage.

The unit tests correctly stay at the service boundary (`MissionService`, `VoyageService`, `DockService`, `CaptainService`) where dependencies are fewer and mocking is proportionate. The `LandingPipelineTests` verify the state machine transitions, persistence, and enum contracts that underpin the landing flow. The automated tests verify the landing flow itself.

**Our recommendation: Cover `ArmadaServer` landing flows in `Armada.Test.Automated`, not in unit tests.** Do not create mock-heavy unit tests for composition-root methods.

---

## Part 3: Action Plan

Based on the second-round feedback, here is what we recommend doing next, in priority order:

| Priority | Item | Action | Justification |
|----------|------|--------|---------------|
| **P1** | MergeQueue auto-enqueue | Implement: auto-create `MergeEntry` in landing handler when `LandingMode = MergeQueue` | Real contract gap between enum name and behavior |
| **P2** | PR path duplicate event | Fix: derive final broadcast event type from `mission.Status`, not from boolean flags | Semantic leak in event stream confuses consumers |
| **P3** | Manual Complete without dock | Add warning event, keep the fallback path | Preserves operational flexibility with audit visibility |
| **Skip** | Dual reclaim ownership | Leave as-is | Idempotent two-caller pattern is standard and correct |
| **Skip** | Dock cleanup heuristics | Leave as-is | Working correctly in production, no false positives observed |
| **Defer** | ArmadaServer-level landing tests | Cover in `Armada.Test.Automated`, not unit tests | Mock-heavy unit tests for composition roots produce false confidence |

---

## Part 4: What Was NOT Done in the Original Implementation (and Why)

### 1. Removal of Legacy Boolean Flags

The `AutoPush`, `AutoCreatePullRequests`, and `AutoMergePullRequests` settings were not removed. They serve as a backward-compatible fallback when `LandingMode` is null, ensuring existing deployments continue working without configuration changes. Removal should be considered for a future major version with a migration guide.

### 2. Per-Voyage BranchCleanupPolicy

Branch cleanup is a repository-level concern, not a voyage-level one. The two-tier resolution (vessel > global) provides sufficient granularity. Adding a voyage-level override would increase complexity without a clear use case.

### 3. End-to-End PR Polling Tests

The `PollAndPullAfterMergeAsync` method requires a real GitHub API interaction to test meaningfully. The unit tests verify the state machine transitions and event emissions, but actual PR polling is best covered by the automated integration test suite (`Armada.Test.Automated`) against a real repository.

### 4. Merge Queue Landing Mode Integration Test

The `MergeQueue` landing mode currently delegates to manual operator action (see Priority 1 above). Once auto-enqueue is implemented, integration tests should be added to `Armada.Test.Automated` to verify the full enqueue-process-land cycle.

---

## Commit History

| Commit | Description |
|--------|-------------|
| Task 1 | Add PullRequestOpen mission status for PR lifecycle tracking |
| Task 2 | Add LandingModeEnum for explicit landing policy configuration |
| Task 3 | Route manual Complete transitions through landing pipeline |
| Task 4 | Add BranchCleanupPolicy for explicit branch lifecycle management |
| Task 5 | Make DockService.ReclaimAsync idempotent with Active guard |
| Task 6 | Add landing pipeline integration tests and fix test expectations |
| Docs   | Update documentation for PullRequestOpen, LandingMode, and BranchCleanupPolicy |

---

## Final Note

The second-round feedback from Codex is the strongest kind of review: it acknowledges what improved, identifies what remains, and prioritizes correctly. We agree with its top two priorities (MergeQueue auto-enqueue and PR event leak) and disagree primarily on testing strategy and the severity of architectural-purity concerns that are already solved by pragmatic design. The remaining work is focused and tractable.
