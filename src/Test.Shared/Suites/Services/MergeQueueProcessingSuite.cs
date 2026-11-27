namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end merge queue processing against real git repositories: an "origin" bare repository, the vessel's bare
    /// repository (cloned from origin, as Armada keeps it), and branches pushed into the vessel repository the way a
    /// captain's dock does. Covers a clean land (origin's main advances, linked mission Complete), a merge conflict, a
    /// failing test command, and that a failure marks the linked mission LandingFailed instead of leaving it WorkProduced.
    /// </summary>
    public sealed class MergeQueueProcessingSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MergeQueueProcessing";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("clean_branch_lands_and_completes_mission", "Clean branch lands on origin main and completes the mission", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/clean", "clean.txt", "clean change\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    mission.FailureReason = "stale failure";
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/clean", mission.Id, null).ConfigureAwait(false);

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Landed, processed!.Status, "entry status; output: " + processed.TestOutput);
                    AssertTrue(repos.OriginHasFile("main", "clean.txt"), "origin main contains the landed change");
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, updated!.Status);
                    AssertNull(updated.FailureReason, "successful landing clears the prior failure reason");
                }
            }));

            cases.Add(CaseAsync("child_entry_waits_for_parent_landing", "A hierarchy child's entry stays queued until its parent lands (section 7.4)", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/child", "child.txt", "child change\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Vessel vessel = await EnsureVesselAsync(testDb.Driver, repos).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("tree") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Mission parent = await testDb.Driver.Missions.CreateAsync(new Mission("Parent") { VesselId = vessel.Id, VoyageId = voyage.Id, Status = MissionStatusEnum.WorkProduced }).ConfigureAwait(false);
                    Mission child = await testDb.Driver.Missions.CreateAsync(new Mission("Child") { VesselId = vessel.Id, VoyageId = voyage.Id, ParentMissionId = parent.Id, Depth = 1,
                        BranchStrategy = MissionBranchStrategyEnum.ForkFromParent, Status = MissionStatusEnum.WorkProduced }).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/child", child.Id, null).ConfigureAwait(false);

                    MergeEntry? waiting = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);
                    AssertEqual(MergeStatusEnum.Queued, waiting!.Status, "the child waits");
                    AssertContains("WaitingForParentLanding", waiting.TestOutput ?? "");
                    AssertFalse(repos.OriginHasFile("main", "child.txt"));

                    parent.Status = MissionStatusEnum.Complete;
                    await testDb.Driver.Missions.UpdateAsync(parent).ConfigureAwait(false);
                    MergeEntry? landed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);
                    AssertEqual(MergeStatusEnum.Landed, landed!.Status, "the child lands once the parent has; output: " + landed.TestOutput);
                }
            }));

            cases.Add(CaseAsync("parent_lands_its_handoff_commit_not_the_shared_head", "A parent whose branch a continuing child extends lands its own commit, not the child's work", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/shared", "parent.txt", "parent change\n");
                    string handoff = repos.VesselHead("feature/shared");
                    repos.ExtendBranch("feature/shared", "child.txt", "unfinished child work\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Vessel vessel = await EnsureVesselAsync(testDb.Driver, repos).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("chain") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Mission parent = await testDb.Driver.Missions.CreateAsync(new Mission("Parent") { VesselId = vessel.Id, VoyageId = voyage.Id, Status = MissionStatusEnum.WorkProduced,
                        BranchName = "feature/shared", CommitHash = handoff }).ConfigureAwait(false);
                    await testDb.Driver.Missions.CreateAsync(new Mission("Child") { VesselId = vessel.Id, VoyageId = voyage.Id, ParentMissionId = parent.Id, Depth = 1,
                        BranchStrategy = MissionBranchStrategyEnum.ContinueParent, BranchName = "feature/shared", Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/shared", parent.Id, null).ConfigureAwait(false);

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);
                    AssertEqual(MergeStatusEnum.Landed, processed!.Status, "output: " + processed.TestOutput);
                    AssertTrue(repos.OriginHasFile("main", "parent.txt"), "the parent's work lands");
                    AssertFalse(repos.OriginHasFile("main", "child.txt"), "the child's unfinished work on the shared branch does not");
                }
            }));

            cases.Add(CaseAsync("conflict_fails_entry_and_mission", "Merge conflict fails the entry and marks the mission LandingFailed", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/conflict", "README.md", "branch version\n");
                    repos.AdvanceMain("README.md", "main version\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/conflict", mission.Id, null).ConfigureAwait(false);
                    string mainBefore = repos.OriginHead("main");

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                    AssertContains("Merge conflict", processed.TestOutput ?? "");
                    AssertEqual(mainBefore, repos.OriginHead("main"), "origin main unchanged");
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, updated!.Status, "mission must not stay WorkProduced");
                    AssertContains("merge conflict", updated.FailureReason ?? "");
                    AssertEqual(MissionFailureKindEnum.LandingConflict, updated.FailureKind, "failure kind is recorded where the failure happens");
                    AssertContains("README.md", updated.FailureReason ?? "", "the conflicted file is named, read before the merge was aborted");
                    AssertContains("README.md", processed.TestOutput ?? "");
                    MergeQueueFailure failure = await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MergeQueueFailureStepEnum.Merge, failure.Step);
                    AssertEqual(MissionFailureKindEnum.LandingConflict, failure.Kind);
                    AssertEqual("README.md", String.Join(",", failure.ConflictedFiles));
                    AssertEqual(entry.Id, failure.MergeEntryId);
                    AssertEqual(mainBefore, failure.TargetCommit, "the target commit the entry integrated against");
                }
            }));

            foreach (MissionStatusEnum waitingStatus in new[] { MissionStatusEnum.WorkProduced, MissionStatusEnum.LandingFailed })
            {
                MissionStatusEnum startStatus = waitingStatus;
                cases.Add(CaseAsync("missing_branch_with_landed_commit_lands_entry_" + startStatus.ToString().ToLowerInvariant(),
                    "Missing branch whose mission commit is on target lands the entry and completes a " + startStatus + " mission", TestTags.Positive, async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                        const string branch = "feature/already-landed";
                        repos.PushBranch(branch, "landed.txt", "landed change\n");
                        VesselHealthGitHelper.RunGit(repos.Work, null, "push", "origin", branch + ":main");
                        string commitHash = repos.OriginHead("main");

                        MergeQueueService queue = CreateQueue(testDb.Driver);
                        Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                        mission.CommitHash = commitHash;
                        mission.Status = startStatus;
                        await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                        MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, branch, mission.Id, null).ConfigureAwait(false);

                        // The branch is deleted after the entry was queued (its commit already landed on origin main).
                        VesselHealthGitHelper.RunGit(repos.VesselRepo, null, "update-ref", "-d", "refs/heads/" + branch);

                        MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                        AssertEqual(MergeStatusEnum.Landed, processed!.Status, "entry status; output: " + processed.TestOutput);
                        AssertContains("already on main", processed.TestOutput ?? "");
                        AssertNotNull(processed.CompletedUtc, "entry completion time");
                        Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                        AssertEqual(MissionStatusEnum.Complete, updated!.Status, "the mission is reconciled as landed, not left waiting");
                        AssertNull(updated.FailureKind, "no failure is recorded");
                        AssertEqual(commitHash, updated.CommitHash, "the landed commit is kept");
                        AssertEqual(commitHash, repos.OriginHead("main"), "nothing is pushed");
                    }
                }));
            }

            cases.Add(CaseAsync("missing_branch_without_commit_hash_still_fails", "Missing branch without a recorded commit still fails", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    const string branch = "feature/missing-hash";
                    repos.PushBranch(branch, "missing-hash.txt", "branch change\n");
                    VesselHealthGitHelper.RunGit(repos.VesselRepo, null, "update-ref", "-d", "refs/heads/" + branch);

                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, branch, mission.Id, null).ConfigureAwait(false);

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, updated!.Status);
                }
            }));

            cases.Add(CaseAsync("missing_branch_with_unlanded_commit_still_fails", "Missing branch whose recorded commit is outside target still fails", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    const string branch = "feature/unlanded";
                    repos.PushBranch(branch, "unlanded.txt", "branch change\n");
                    string commitHash = VesselHealthGitHelper.RunGit(repos.VesselRepo, null, "rev-parse", "refs/heads/" + branch).Trim();
                    VesselHealthGitHelper.RunGit(repos.VesselRepo, null, "update-ref", "-d", "refs/heads/" + branch);

                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    mission.CommitHash = commitHash;
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, branch, mission.Id, null).ConfigureAwait(false);

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, updated!.Status);
                }
            }));

            cases.Add(CaseAsync("present_branch_with_newer_work_is_not_cancelled", "Present branch with work newer than the mission commit is still merged", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    const string branch = "feature/newer-work";
                    string targetCommit = repos.OriginHead("main");
                    repos.PushBranch(branch, "newer.txt", "newer branch change\n");

                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    mission.CommitHash = targetCommit;
                    await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, branch, mission.Id, null).ConfigureAwait(false);

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Landed, processed!.Status, "a present branch contains work after the recorded commit");
                    AssertTrue(repos.OriginHasFile("main", "newer.txt"), "the newer branch work lands");
                }
            }));

            cases.Add(CaseAsync("failing_tests_block_landing", "Failing test command fails the entry without landing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/tests-fail", "tested.txt", "change\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/tests-fail", mission.Id, "exit 3").ConfigureAwait(false);
                    string mainBefore = repos.OriginHead("main");

                    MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                    AssertEqual(3, processed.TestExitCode);
                    AssertEqual(mainBefore, repos.OriginHead("main"), "nothing landed");
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, updated!.Status);
                    AssertEqual(MissionFailureKindEnum.TestFail, updated.FailureKind, "a failing check is a test failure, not a landing conflict");
                    MergeQueueFailure failure = await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                    AssertEqual(MergeQueueFailureStepEnum.Verification, failure.Step);
                    AssertEqual(MissionFailureKindEnum.TestFail, failure.Kind);
                    AssertEqual(0, failure.ConflictedFiles.Count, "no conflicted files for a test failure");
                }
            }));

            cases.Add(CaseAsync("push_failure_is_infra", "A refused push fails the entry as Infra at the publication step, not as a landing conflict", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/push-refused", "pushed.txt", "change\n");
                string hook = Path.Combine(repos.Origin, "hooks", "pre-receive");
                Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
                File.WriteAllText(hook, "#!/bin/sh\necho refused by test hook >&2\nexit 1\n");
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/push-refused", mission.Id, null).ConfigureAwait(false);
                string mainBefore = repos.OriginHead("main");

                MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                AssertEqual(MergeStatusEnum.Failed, processed!.Status, processed.TestOutput);
                AssertEqual(mainBefore, repos.OriginHead("main"), "nothing landed");
                Mission updated = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.LandingFailed, updated.Status);
                AssertEqual(MissionFailureKindEnum.Infra, updated.FailureKind, updated.FailureReason);
                MergeQueueFailure failure = await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                AssertEqual(MergeQueueFailureStepEnum.Publication, failure.Step);
                AssertEqual(MissionFailureKindEnum.Infra, failure.Kind);
            }));

            cases.Add(CaseAsync("unresolvable_repository_is_infra", "An entry whose repository cannot be resolved fails as Infra at the resolution step", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Vessel vessel = new Vessel("mq-missing-" + Guid.NewGuid().ToString("N").Substring(0, 12), "https://github.com/test/missing");
                vessel.LocalPath = Path.Combine(TestTemp.NewDirectory("mq_missing"), "absent.git");
                vessel.DefaultBranch = "main";
                vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                Mission mission = new Mission("Unresolvable") { VesselId = vessel.Id, Status = MissionStatusEnum.WorkProduced };
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
                MergeEntry entry = new MergeEntry("feature/nowhere", "main") { VesselId = vessel.Id, MissionId = mission.Id };
                entry = await queue.EnqueueAsync(entry).ConfigureAwait(false);

                MergeEntry? processed = await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                AssertEqual(MergeStatusEnum.Failed, processed!.Status);
                Mission updated = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.LandingFailed, updated.Status);
                AssertEqual(MissionFailureKindEnum.Infra, updated.FailureKind, updated.FailureReason);
                MergeQueueFailure failure = await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false);
                AssertEqual(MergeQueueFailureStepEnum.Resolution, failure.Step);
            }));

            cases.Add(CaseAsync("mutating_check_is_infra", "A check that rewrites the integration commit fails the mission as Infra, not as a test failure", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/mutating", "mutating.txt", "mutating\n");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos).ConfigureAwait(false);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/mutating", mission.Id, "git reset --hard HEAD^").ConfigureAwait(false);

                await queue.ProcessSingleAsync(entry.Id).ConfigureAwait(false);

                Mission updated = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.LandingFailed, updated.Status);
                AssertEqual(MissionFailureKindEnum.Infra, updated.FailureKind, updated.FailureReason);
                AssertEqual(MergeQueueFailureStepEnum.Verification, (await ReadFailureAsync(testDb.Driver, mission.Id).ConfigureAwait(false)).Step);
            }));

            cases.Add(CaseAsync("failure_classifier_maps_steps_to_kinds", "The merge queue failure classifier maps each failing step to the kind that describes it", TestTags.Positive, async () =>
            {
                AssertEqual(MissionFailureKindEnum.LandingConflict, FailureKindMapper.FromMergeResult(2));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromMergeResult(0), "a merge that failed without conflicts is not a conflict");
                AssertEqual(MissionFailureKindEnum.TestFail, FailureKindMapper.FromMergeQueueException(MergeQueueFailureStepEnum.Verification,
                    new LandingVerificationException("failed", "Failed", "exit 3", 3, "abc", false)));
                AssertEqual(MissionFailureKindEnum.Timeout, FailureKindMapper.FromMergeQueueException(MergeQueueFailureStepEnum.Verification,
                    new LandingVerificationException("timed out", "TimedOut", "sleep 999", null, "abc", false)));
                AssertEqual(MissionFailureKindEnum.Timeout, FailureKindMapper.FromMergeQueueException(MergeQueueFailureStepEnum.Verification, new TimeoutException("budget")));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromMergeQueueException(MergeQueueFailureStepEnum.Verification,
                    new LandingVerificationException("mutated", "Failed", "git reset", 0, "abc", true)));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromMergeQueueException(MergeQueueFailureStepEnum.Verification, new InvalidOperationException("could not start /bin/sh")));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromMergeQueueException(MergeQueueFailureStepEnum.Publication, new InvalidOperationException("push rejected")));
                AssertEqual(MissionFailureKindEnum.Infra, FailureKindMapper.FromMergeQueueException(MergeQueueFailureStepEnum.Preparation, new TimeoutException("fetch timed out")),
                    "a timeout outside verification is an environment failure");
                AssertEqual(MissionFailureKindEnum.LandingConflict, FailureKindMapper.FromMergeQueueException(MergeQueueFailureStepEnum.Preparation,
                    new GitMergeConflictException("conflict", new[] { "a.txt" })));
                await Task.CompletedTask.ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("queue_processes_in_order_and_continues_after_failure", "Queue lands in priority order and continues past a failed entry", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/first", "first.txt", "1\n");
                    repos.PushBranch("feature/broken", "README.md", "conflicting\n");
                    repos.PushBranch("feature/second", "second.txt", "2\n");
                    repos.AdvanceMain("README.md", "main moved\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    MergeEntry first = await EnqueueAsync(queue, testDb.Driver, repos, "feature/first", null, null, 1).ConfigureAwait(false);
                    MergeEntry broken = await EnqueueAsync(queue, testDb.Driver, repos, "feature/broken", null, null, 2).ConfigureAwait(false);
                    MergeEntry second = await EnqueueAsync(queue, testDb.Driver, repos, "feature/second", null, null, 3).ConfigureAwait(false);

                    await queue.ProcessQueueAsync().ConfigureAwait(false);

                    AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(first.Id).ConfigureAwait(false))!.Status);
                    AssertEqual(MergeStatusEnum.Failed, (await queue.GetAsync(broken.Id).ConfigureAwait(false))!.Status);
                    AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(second.Id).ConfigureAwait(false))!.Status);
                    AssertTrue(repos.OriginHasFile("main", "first.txt") && repos.OriginHasFile("main", "second.txt"), "both good branches landed");
                }
            }));

            cases.Add(CaseAsync("stale_testing_entry_is_reprocessed", "Queue resumes a Testing entry after its processing lease expires", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/recovery", "recovered.txt", "recovered\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/recovery", null, null).ConfigureAwait(false);
                    entry.Status = MergeStatusEnum.Testing;
                    entry.TestStartedUtc = DateTime.UtcNow.AddMinutes(-16);
                    entry.TestExitCode = 7;
                    await testDb.Driver.MergeEntries.UpdateAsync(entry).ConfigureAwait(false);

                    await queue.ProcessQueueAsync().ConfigureAwait(false);

                    MergeEntry? processed = await queue.GetAsync(entry.Id).ConfigureAwait(false);
                    AssertEqual(MergeStatusEnum.Landed, processed!.Status, "stale entry is resumed and landed");
                    AssertTrue(repos.OriginHasFile("main", "recovered.txt"), "recovered change reaches origin main");
                }
            }));

            cases.Add(CaseAsync("testing_entry_without_start_time_is_reprocessed", "Queue resumes a Testing entry without a start time", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/missing-start", "missing-start.txt", "recovered\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/missing-start", null, null).ConfigureAwait(false);
                    entry.Status = MergeStatusEnum.Testing;
                    entry.TestStartedUtc = null;
                    await testDb.Driver.MergeEntries.UpdateAsync(entry).ConfigureAwait(false);

                    await queue.ProcessQueueAsync().ConfigureAwait(false);

                    MergeEntry? processed = await queue.GetAsync(entry.Id).ConfigureAwait(false);
                    AssertEqual(MergeStatusEnum.Landed, processed!.Status, "entry without a start time is resumed and landed");
                    AssertTrue(repos.OriginHasFile("main", "missing-start.txt"), "recovered change reaches origin main");
                }
            }));

            cases.Add(CaseAsync("fresh_testing_entry_is_not_reprocessed", "Queue leaves a fresh Testing entry alone", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                    repos.PushBranch("feature/fresh", "fresh.txt", "pending\n");
                    MergeQueueService queue = CreateQueue(testDb.Driver);
                    MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/fresh", null, null).ConfigureAwait(false);
                    entry.Status = MergeStatusEnum.Testing;
                    entry.TestStartedUtc = DateTime.UtcNow;
                    await testDb.Driver.MergeEntries.UpdateAsync(entry).ConfigureAwait(false);

                    await queue.ProcessQueueAsync().ConfigureAwait(false);

                    MergeEntry? unchanged = await queue.GetAsync(entry.Id).ConfigureAwait(false);
                    AssertEqual(MergeStatusEnum.Testing, unchanged!.Status, "fresh entry remains owned by its current processor");
                    AssertTrue(!repos.OriginHasFile("main", "fresh.txt"), "fresh entry is not landed by another pass");
                }
            }));

            cases.Add(CaseAsync("passing_integration_receipt", "Merge queue persists passing checks against the exact SHA pushed to origin", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/receipt", "receipt.txt", "receipt\n");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/receipt", mission.Id, "echo verified");
                await queue.ProcessQueueAsync();
                MergeEntry stored = (await queue.GetAsync(entry.Id))!;
                AssertEqual(MergeStatusEnum.Landed, stored.Status);
                AssertEqual(0, stored.TestExitCode!.Value);
                AssertContains("verified", stored.TestOutput!);
                AssertContains(repos.OriginHead("main"), stored.TestOutput!);
                AssertEqual(repos.OriginHead("main"), (await testDb.Driver.Missions.ReadAsync(mission.Id))!.CommitHash);
            }));
            cases.Add(CaseAsync("merged_result_must_pass", "Merge queue refuses a branch whose checks fail only after integrating current main", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/integration", "feature.txt", "feature\n");
                repos.AdvanceMain("target-only.txt", "new target\n");
                string before = repos.OriginHead("main");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                string command = OperatingSystem.IsWindows() ? "if exist target-only.txt (exit /b 7) else (exit /b 0)" : "test ! -f target-only.txt";
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/integration", null, command);
                await queue.ProcessQueueAsync();
                AssertEqual(MergeStatusEnum.Failed, (await queue.GetAsync(entry.Id))!.Status);
                AssertEqual(before, repos.OriginHead("main"));
                AssertFalse(repos.OriginHasFile("main", "feature.txt"), "Unverified integration is never published.");
            }));
            cases.Add(CaseAsync("test_mutation_refused", "A passing command that changes integration HEAD cannot land a different commit", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/mutates", "mutates.txt", "mutates\n");
                string before = repos.OriginHead("main");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/mutates", null, "git reset --hard HEAD^");
                await queue.ProcessQueueAsync();
                AssertEqual(MergeStatusEnum.Failed, (await queue.GetAsync(entry.Id))!.Status);
                AssertEqual(before, repos.OriginHead("main"));
            }));

            cases.Add(CaseAsync("cancel_during_check", "Cancelling a merge entry during its check cannot be overwritten or publish the commit", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/cancel", "cancel.txt", "cancel\n");
                string before = repos.OriginHead("main");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/cancel", null, "passing check");
                queue.LocalVerificationCommands = new FakeHostCommandExecutor { Handler = _ =>
                {
                    queue.CancelAsync(entry.Id).GetAwaiter().GetResult();
                    return new HostCommandResult { ExitCode = 0 };
                } };
                await queue.ProcessQueueAsync();
                AssertEqual(MergeStatusEnum.Cancelled, (await queue.GetAsync(entry.Id))!.Status);
                AssertEqual(before, repos.OriginHead("main"));
            }));
            cases.Add(CaseAsync("stale_queue_update", "A stale processing result cannot replace a newer merge-entry version", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeEntry entry = await testDb.Driver.MergeEntries.CreateAsync(new MergeEntry("feature/stale"));
                DateTime version = entry.LastUpdateUtc;
                MergeEntry newer = (await testDb.Driver.MergeEntries.ReadAsync(entry.Id))!;
                newer.Status = MergeStatusEnum.Cancelled;
                await testDb.Driver.MergeEntries.UpdateAsync(newer);
                entry.Status = MergeStatusEnum.Landed;
                AssertFalse(await testDb.Driver.MergeEntries.TryUpdateIfUnchangedAsync(entry, version));
                AssertEqual(MergeStatusEnum.Cancelled, (await testDb.Driver.MergeEntries.ReadAsync(entry.Id))!.Status);
            }));

            cases.Add(CaseAsync("push_not_fast_forward_requeues_from_fresh_fetch", "A push refused because the target moved is requeued, integrated again from a fresh fetch, and lands (RS-9.1)", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/raced", "raced.txt", "raced\n");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/raced", mission.Id, "check");
                int checks = 0;
                // Another writer lands on main while the first check runs, so the first push is not a fast-forward.
                queue.LocalVerificationCommands = new FakeHostCommandExecutor { Handler = _ =>
                {
                    if (++checks == 1) repos.AdvanceMain("other.txt", "other writer\n");
                    return new HostCommandResult { ExitCode = 0 };
                } };

                await queue.ProcessQueueAsync();

                AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(entry.Id))!.Status, "the entry lands on the second attempt");
                AssertEqual(2, checks, "the requeued entry is verified again against the moved target");
                AssertTrue(repos.OriginHasFile("main", "raced.txt") && repos.OriginHasFile("main", "other.txt"), "both changes are on main");
                AssertEqual(MissionStatusEnum.Complete, (await testDb.Driver.Missions.ReadAsync(mission.Id))!.Status);
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateRecentAsync(200);
                AssertEqual(1, events.FindAll(e => e.EventType == MergeQueueService.RequeuedEventType && e.EntityId == entry.Id).Count, "one requeue recorded");
            }));

            cases.Add(CaseAsync("push_not_fast_forward_fails_after_requeue_limit", "With MergeQueueRequeueLimit 0 a non-fast-forward push fails the entry as Infra without a retry", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/raced", "raced.txt", "raced\n");
                MergeQueueService queue = CreateQueue(testDb.Driver, s => s.MergeQueueRequeueLimit = 0);
                Mission mission = await CreateMissionAsync(testDb.Driver, repos);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/raced", mission.Id, "check");
                int checks = 0;
                queue.LocalVerificationCommands = new FakeHostCommandExecutor { Handler = _ =>
                {
                    if (++checks == 1) repos.AdvanceMain("other.txt", "other writer\n");
                    return new HostCommandResult { ExitCode = 0 };
                } };

                await queue.ProcessQueueAsync();

                MergeEntry failed = (await queue.GetAsync(entry.Id))!;
                AssertEqual(MergeStatusEnum.Failed, failed.Status);
                AssertTrue(failed.TestOutput!.Contains("mergeQueueRequeueLimit"), "the failure names the limit");
                AssertEqual(1, checks, "no second attempt");
                Mission stored = (await testDb.Driver.Missions.ReadAsync(mission.Id))!;
                AssertEqual(MissionStatusEnum.LandingFailed, stored.Status);
                AssertEqual(MissionFailureKindEnum.Infra, stored.FailureKind);
            }));

            cases.Add(CaseAsync("push_rejected_by_remote_is_not_requeued", "A push the remote refuses while the target did not move fails at once, without a requeue", TestTags.Negative, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/refused", "refused.txt", "refused\n");
                string hook = Path.Combine(repos.Origin, "hooks", "pre-receive");
                File.WriteAllText(hook, "#!/bin/sh\nexit 1\n");
                File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                MergeQueueService queue = CreateQueue(testDb.Driver);
                MergeEntry entry = await EnqueueAsync(queue, testDb.Driver, repos, "feature/refused", null, null);

                await queue.ProcessQueueAsync();

                MergeEntry failed = (await queue.GetAsync(entry.Id))!;
                AssertEqual(MergeStatusEnum.Failed, failed.Status);
                AssertTrue(failed.TestOutput!.Contains("remote refused"), "classified as a refusal, not a moved target");
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateRecentAsync(200);
                AssertEqual(0, events.FindAll(e => e.EventType == MergeQueueService.RequeuedEventType).Count, "no requeue");
            }));

            cases.Add(CaseAsync("group_fetches_once_per_pass", "Three entries of one vessel and target branch land with one fetch (RS-9.3)", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/a", "a.txt", "a\n");
                repos.PushBranch("feature/b", "b.txt", "b\n");
                repos.PushBranch("feature/c", "c.txt", "c\n");
                MergeQueueService queue = CreateQueue(testDb.Driver);
                MergeEntry a = await EnqueueAsync(queue, testDb.Driver, repos, "feature/a", null, null, 1);
                MergeEntry b = await EnqueueAsync(queue, testDb.Driver, repos, "feature/b", null, null, 2);
                MergeEntry c = await EnqueueAsync(queue, testDb.Driver, repos, "feature/c", null, null, 3);

                await queue.ProcessQueueAsync();

                foreach (MergeEntry entry in new[] { a, b, c })
                    AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(entry.Id))!.Status, entry.BranchName);
                AssertTrue(repos.OriginHasFile("main", "a.txt") && repos.OriginHasFile("main", "b.txt") && repos.OriginHasFile("main", "c.txt"), "all three on main");
                AssertEqual(1L, queue.FetchCount, "one fetch for the group");
            }));

            cases.Add(CaseAsync("batched_landing_verifies_once", "With MergeQueueBatchSize 3, three entries merge into one worktree, verify once, and land with one push (RS-9.4)", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/a", "a.txt", "a\n");
                repos.PushBranch("feature/b", "b.txt", "b\n");
                repos.PushBranch("feature/c", "c.txt", "c\n");
                MergeQueueService queue = CreateQueue(testDb.Driver, s => s.MergeQueueBatchSize = 3);
                List<Mission> missions = new List<Mission>();
                List<MergeEntry> entries = new List<MergeEntry>();
                int priority = 1;
                foreach (string branch in new[] { "feature/a", "feature/b", "feature/c" })
                {
                    Mission mission = await CreateMissionAsync(testDb.Driver, repos);
                    missions.Add(mission);
                    entries.Add(await EnqueueAsync(queue, testDb.Driver, repos, branch, mission.Id, "check", priority++));
                }
                int checks = 0;
                queue.LocalVerificationCommands = new FakeHostCommandExecutor { Handler = _ =>
                {
                    checks++;
                    return new HostCommandResult { ExitCode = 0 };
                } };

                await queue.ProcessQueueAsync();

                foreach (MergeEntry entry in entries)
                    AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(entry.Id))!.Status, entry.BranchName);
                AssertEqual(1, checks, "one verification for the batch");
                string head = repos.OriginHead("main");
                foreach (Mission mission in missions)
                {
                    Mission stored = (await testDb.Driver.Missions.ReadAsync(mission.Id))!;
                    AssertEqual(MissionStatusEnum.Complete, stored.Status);
                    AssertEqual(head, stored.CommitHash, "each mission records the published batch commit");
                }
            }));

            cases.Add(CaseAsync("batched_landing_bisects_failing_entry", "A batch that fails verification is split until the failing entry fails alone; the others land (RS-9.4)", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                MergeQueueRepoSet repos = MergeQueueRepoSet.Create();
                repos.PushBranch("feature/a", "a.txt", "a\n");
                repos.PushBranch("feature/b", "b.txt", "b\n");
                repos.PushBranch("feature/bad", "bad.txt", "bad\n");
                repos.PushBranch("feature/d", "d.txt", "d\n");
                MergeQueueService queue = CreateQueue(testDb.Driver, s => s.MergeQueueBatchSize = 4);
                List<MergeEntry> entries = new List<MergeEntry>();
                int priority = 1;
                foreach (string branch in new[] { "feature/a", "feature/b", "feature/bad", "feature/d" })
                    entries.Add(await EnqueueAsync(queue, testDb.Driver, repos, branch, null, "check", priority++));
                int checks = 0;
                queue.LocalVerificationCommands = new FakeHostCommandExecutor { Handler = request =>
                {
                    checks++;
                    bool bad = File.Exists(Path.Combine(request.WorkingDirectory, "bad.txt"));
                    return new HostCommandResult { ExitCode = bad ? 1 : 0, StandardError = bad ? "bad.txt is not allowed" : "" };
                } };

                await queue.ProcessQueueAsync();

                AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(entries[0].Id))!.Status, "a");
                AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(entries[1].Id))!.Status, "b");
                AssertEqual(MergeStatusEnum.Failed, (await queue.GetAsync(entries[2].Id))!.Status, "bad");
                AssertEqual(MergeStatusEnum.Landed, (await queue.GetAsync(entries[3].Id))!.Status, "d");
                AssertFalse(repos.OriginHasFile("main", "bad.txt"), "the failing change is not published");
                AssertTrue(repos.OriginHasFile("main", "a.txt") && repos.OriginHasFile("main", "d.txt"), "the others are");
                AssertTrue(checks <= 5, "bisecting four entries needs at most five checks, got " + checks);
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Merge Queue Processing", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<MergeQueueFailure> ReadFailureAsync(DatabaseDriver db, string missionId)
        {
            List<ArmadaEvent> events = await db.Events.EnumerateRecentAsync(200).ConfigureAwait(false);
            ArmadaEvent? failed = events.Find(e => e.EventType == "mission.landing_failed" && e.MissionId == missionId);
            AssertNotNull(failed, "the merge queue records mission.landing_failed");
            AssertNotNull(failed!.Payload, "the landing failure event carries the structured failure");
            return System.Text.Json.JsonSerializer.Deserialize<MergeQueueFailure>(failed.Payload!)!;
        }

        private static MergeQueueService CreateQueue(DatabaseDriver db, Action<ArmadaSettings>? configure = null)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = TestTemp.NewDirectory("mq_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("mq_repos");
            settings.MergeQueueTestCommand = null;
            configure?.Invoke(settings);
            return new MergeQueueService(logging, db, settings, new GitService(logging));
        }

        private static async Task<Mission> CreateMissionAsync(DatabaseDriver db, MergeQueueRepoSet repos)
        {
            Vessel vessel = await EnsureVesselAsync(db, repos).ConfigureAwait(false);
            Mission mission = new Mission("Queued mission");
            mission.VesselId = vessel.Id;
            mission.Status = MissionStatusEnum.WorkProduced;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<MergeEntry> EnqueueAsync(MergeQueueService queue, DatabaseDriver db, MergeQueueRepoSet repos, string branch, string? missionId, string? testCommand, int priority = 0)
        {
            Vessel vessel = await EnsureVesselAsync(db, repos).ConfigureAwait(false);
            MergeEntry entry = new MergeEntry(branch, "main");
            entry.VesselId = vessel.Id;
            entry.MissionId = missionId;
            entry.TestCommand = testCommand;
            entry.Priority = priority;
            return await queue.EnqueueAsync(entry).ConfigureAwait(false);
        }

        private static async Task<Vessel> EnsureVesselAsync(DatabaseDriver db, MergeQueueRepoSet repos)
        {
            if (repos.VesselId != null)
            {
                Vessel? existing = await db.Vessels.ReadAsync(repos.VesselId).ConfigureAwait(false);
                if (existing != null) return existing;
            }

            Vessel vessel = new Vessel("mq-vessel-" + Guid.NewGuid().ToString("N").Substring(0, 12), repos.Origin);
            vessel.LocalPath = repos.VesselRepo;
            vessel.DefaultBranch = "main";
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            repos.VesselId = vessel.Id;
            return vessel;
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag, TestTags.Process });
        }

        #endregion
    }
}
