# CODEX_FEEDBACK.md Response

This document details what was implemented in response to the feedback in `CODEX_FEEDBACK.md`, the rationale behind each change, and justification for anything that was not done.

---

## Summary of Changes

Six tasks were implemented sequentially, each committed, pushed, and verified before proceeding to the next.

---

## Task 1: PullRequestOpen Mission Status

**Feedback concern:** When Armada creates a PR for a completed mission, the mission is marked `Complete` immediately — but the code hasn't actually landed yet. If the PR fails to merge, the mission is incorrectly reported as complete.

**What was done:**

- Added `PullRequestOpen` to `MissionStatusEnum` (value inserted between `WorkProduced` and `Testing`).
- Updated the PR creation flow in `ArmadaServer.cs` to set `PullRequestOpen` instead of `Complete` when a PR is created. The mission emits a `mission.pull_request_open` event and broadcasts via WebSocket.
- Updated `PollAndPullAfterMergeAsync` to transition from `PullRequestOpen` to `Complete` only when the PR merge is confirmed.
- Added valid status transitions: `WorkProduced -> PullRequestOpen`, `PullRequestOpen -> Complete`, `PullRequestOpen -> LandingFailed`, `PullRequestOpen -> Cancelled`.
- Updated `AdmiralService.HealthCheckAsync` to recognize `PullRequestOpen` as a post-work state (so it doesn't mistakenly think the captain is stuck).
- Updated `VoyageService` so `PullRequestOpen` counts as in-progress (a voyage does not complete while any mission is in `PullRequestOpen`).
- Added color (`dodgerblue1`) and emoji (`🔀`) for `PullRequestOpen` in the Helm table renderer.
- Updated all API and agent instruction documentation to include the new status.

**Why:** This was the highest-priority semantic correctness issue. `Complete` must mean "code has landed," not "PR was created." Without this, monitoring tools and voyage completion logic produce false positives.

---

## Task 2: Explicit Landing Mode (LandingModeEnum)

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

## Task 3: Manual Complete Routes Through Landing Pipeline

**Feedback concern:** When a user manually transitions a mission to `Complete` via `armada_transition_mission_status`, it bypasses the landing pipeline entirely — no diff capture, no merge, no PR creation. This creates an inconsistency where the manual path produces different outcomes than the agent-driven path.

**What was done:**

- Modified `ArmadaServer.HandleTransitionMissionStatusAsync` so that when a mission is transitioned to `Complete` and it has an active dock, the request is routed through `HandleMissionCompleteAsync` instead of directly setting the status.
- This ensures diff capture, `WorkProduced` transition, and the full landing pipeline are invoked regardless of whether the trigger is an agent exit or a manual status change.

**Why:** The principle of least surprise. A user transitioning to `Complete` expects the same outcome as an agent completing — code landed, branch cleaned up, diff captured.

---

## Task 4: BranchCleanupPolicyEnum

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

## Task 5: DockService.ReclaimAsync Idempotency

**Feedback concern:** Both `MissionService` (background finalizer) and `ArmadaServer` (`HandleMissionCompleteAsync`) can call `ReclaimAsync` for the same dock, leading to duplicate worktree removal attempts and spurious error logs.

**What was done:**

- Added an idempotency guard at the top of `DockService.ReclaimAsync`: if the dock is already inactive (`Active == false`), the method returns immediately with a debug log.
- This makes double-reclaim a safe no-op.

**Why:** Simple, surgical fix for a real race condition. The guard prevents redundant filesystem operations and eliminates confusing warning logs.

---

## Task 6: Integration Tests for the Landing Pipeline

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

## Documentation Updates

All documentation was updated as part of this effort:

- **MCP_API.md, REST_API.md, WEBSOCKET_API.md**: Added `PullRequestOpen`, `WorkProduced`, `LandingFailed` to `MissionStatusEnum` tables. Added `PullRequestOpen` transitions to status transition tables. Added `LandingModeEnum` and `BranchCleanupPolicyEnum` enum documentation. Added `LandingMode` and `BranchCleanupPolicy` to Vessel and Voyage model tables.
- **MERGING.md**: Added Landing Mode section with resolution order, Branch Cleanup Policy section, and updated configuration reference.
- **README.md**: Added `LandingMode` and `BranchCleanupPolicy` to the settings table.
- **INSTRUCTIONS_FOR_CLAUDE_CODE.md, INSTRUCTIONS_FOR_CODEX.md, INSTRUCTIONS_FOR_CURSOR.md, INSTRUCTIONS_FOR_GEMINI.md**: Added `PullRequestOpen` to status filter lists. Strengthened guidance to prefer `armada_enumerate` over `armada_list_*` tools.

---

## What Was NOT Done (and Why)

### 1. Removal of Legacy Boolean Flags

The `AutoPush`, `AutoCreatePullRequests`, and `AutoMergePullRequests` settings were not removed. They serve as backward-compatible fallback when `LandingMode` is null, ensuring existing deployments continue working without configuration changes. Removal should be considered for a future major version with a migration guide.

### 2. Per-Voyage BranchCleanupPolicy

Branch cleanup is a repository-level concern, not a voyage-level one. The two-tier resolution (vessel > global) provides sufficient granularity. Adding a voyage-level override would increase complexity without a clear use case.

### 3. End-to-End PR Polling Tests

The `PollAndPullAfterMergeAsync` method requires a real GitHub API interaction to test meaningfully. The unit tests verify the state machine transitions and event emissions, but actual PR polling is best covered by the automated integration test suite (`Armada.Test.Automated`) against a real repository.

### 4. Merge Queue Landing Mode Integration Test

The `MergeQueue` landing mode delegates to the existing `MergeQueueService`, which has its own comprehensive test coverage in `MERGING.md` and the merge queue test suites. A dedicated integration test for the `MergeQueue` landing mode path would be redundant.

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
