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
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Drives <see cref="MissionLandingHandler.HandleMissionCompleteAsync"/> through each landing mode and its failure
    /// branches with a stubbed git service (which stands in for the gh CLI used to open and query pull requests), plus
    /// the pull-request reconciler. Complements Services.LandingPipeline, which covers the successful local merge.
    /// </summary>
    public sealed class LandingPathsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.LandingPaths";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("local_merge_failure_sets_landing_failed", "LocalMerge: merge error sets LandingFailed and keeps the branch", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService { ShouldThrowOnMergeLocal = true };
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, mission!.Status);
                    AssertContains("Error merging locally: Simulated merge failure", mission.FailureReason ?? "");
                    AssertEqual(MissionFailureKindEnum.Infra, mission.FailureKind, "only a merge conflict git reports is a LandingConflict");
                    AssertFalse(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "branch kept for retry");
                }
            }));

            cases.Add(CaseAsync("merge_and_push_push_failure_sets_landing_failed", "MergeAndPush: verified push failure sets LandingFailed and keeps the source branch", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService { ShouldThrowOnPush = true };
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.MergeAndPush, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, mission!.Status);
                    AssertContains("Error publishing verified commit: Simulated push failure", mission.FailureReason ?? "");
                    AssertEqual(MissionFailureKindEnum.Infra, mission.FailureKind, "only a merge conflict git reports is a LandingConflict");
                    AssertEqual(0, git.MergeBranchCalls.Count, "No checkout merge before publication");
                    AssertFalse(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "branch kept for retry");
                }
            }));

            cases.Add(CaseAsync("local_merge_does_not_push", "LocalMerge: merges into the working directory, never pushes, Complete and branch cleaned up", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, mission!.Status);
                    AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                    AssertEqual(0, git.PushCalls.Count, "LocalMerge never pushes");
                    AssertEqual(0, git.PrCalls.Count, "no pull request");
                    AssertTrue(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "branch cleaned up after landing");
                }
            }));

            cases.Add(CaseAsync("local_merge_unaffected_by_push_failure", "LocalMerge: a remote that would refuse a push does not fail the landing", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService { ShouldThrowOnPush = true };
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, mission!.Status, "no push is attempted, so nothing can fail it");
                    AssertNull(mission.FailureReason, "no failure reason");
                    AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                    AssertEqual(0, git.PushCalls.Count, "LocalMerge never pushes");
                }
            }));

            cases.Add(CaseAsync("merge_and_push_merges_then_pushes", "MergeAndPush: publishes the verified dock and cleans its branch after success", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.MergeAndPush, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, mission!.Status);
                    AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                    AssertEqual(1, git.PushCalls.Count, "pushed once");
                    AssertEqual(entities.Dock.WorktreePath, git.PushCalls[0], "publishes from the isolated verified dock");
                    AssertEqual(0, git.PrCalls.Count, "no pull request");
                    AssertTrue(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "branch cleaned up after the push");
                }
            }));

            cases.Add(CaseAsync("merge_and_push_ignores_checkout_merge_failure", "MergeAndPush publishes without merging into the developer checkout", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                StubGitService git = new() { ShouldThrowOnMergeLocal = true };
                MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.MergeAndPush, null, git);
                await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock);
                AssertEqual(MissionStatusEnum.Complete, (await testDb.Driver.Missions.ReadAsync(entities.Mission.Id))!.Status);
                AssertEqual(0, git.MergeBranchCalls.Count);
                AssertEqual(entities.Dock.WorktreePath, git.PushCalls.Single());
            }));

            foreach (LandingModeEnum localMode in new[] { LandingModeEnum.LocalMerge, LandingModeEnum.MergeAndPush })
            {
                LandingModeEnum mode = localMode;
                cases.Add(CaseAsync(mode.ToString().ToLowerInvariant() + "_without_working_directory_stays_work_produced", mode + ": a vessel without a working directory is not merged or pushed and stays WorkProduced", TestTags.Negative, async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        StubGitService git = new StubGitService();
                        MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                        LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, mode, null, git).ConfigureAwait(false);
                        entities.Vessel.WorkingDirectory = null;
                        await testDb.Driver.Vessels.UpdateAsync(entities.Vessel).ConfigureAwait(false);

                        await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                        Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                        AssertEqual(localMode == LandingModeEnum.MergeAndPush ? MissionStatusEnum.Complete : MissionStatusEnum.WorkProduced, mission!.Status);
                        AssertEqual(0, git.MergeBranchCalls.Count, "no checkout merge");
                        AssertEqual(localMode == LandingModeEnum.MergeAndPush ? 1 : 0, git.PushCalls.Count, "Only LocalMerge requires a checkout");
                    }
                }));

                foreach (string missingEvidence in new[] { "missing_diff", "empty_diff", "missing_branch" })
                {
                    string evidence = missingEvidence;
                    cases.Add(CaseAsync(mode.ToString().ToLowerInvariant() + "_" + evidence + "_keeps_work_unlanded", mode + ": " + evidence + " cannot count as a successful landing", TestTags.Negative, async () =>
                    {
                        using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                        {
                            StubGitService git = new StubGitService();
                            RecordingMergeQueueService queue = new RecordingMergeQueueService();
                            MissionLandingHandler handler = CreateHandler(testDb.Driver, git, queue);
                            LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, mode, null, git).ConfigureAwait(false);
                            if (evidence == "missing_branch") git.ExistingBranches.Remove(entities.Dock.BranchName!);
                            else entities.Mission.DiffSnapshot = evidence == "empty_diff" ? "  " : null;
                            await testDb.Driver.Missions.UpdateAsync(entities.Mission).ConfigureAwait(false);

                            await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                            Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                            AssertEqual(MissionStatusEnum.WorkProduced, mission!.Status, "no confirmed landing");
                            AssertNotEqual(MissionStatusEnum.Complete, mission.Status, "a recorded work commit is not a landing");
                            AssertNull(mission.CompletedUtc, "not completed");
                            AssertContains("Work remains unlanded", mission.FailureReason!);
                            AssertEqual(0, git.MergeBranchCalls.Count, "no merge");
                            AssertEqual(0, git.PushCalls.Count, "no push");
                            AssertEqual(0, queue.Enqueued.Count, "not queued implicitly");
                            AssertFalse(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "preserve the only copy of the work");
                            List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false);
                            AssertFalse(events.Exists(e => e.EventType == "mission.completed"), "no false completion event");
                        }
                    }));
                }
            }

            cases.Add(CaseAsync("voyage_merge_and_push_overrides_vessel_local_merge", "Voyage MergeAndPush over vessel LocalMerge: merges and pushes", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge, LandingModeEnum.MergeAndPush, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, mission!.Status);
                    AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                    AssertEqual(1, git.PushCalls.Count, "the voyage's MergeAndPush pushes");
                }
            }));

            cases.Add(CaseAsync("voyage_local_merge_overrides_vessel_merge_and_push", "Voyage LocalMerge over vessel MergeAndPush: merges without pushing", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.MergeAndPush, LandingModeEnum.LocalMerge, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, mission!.Status);
                    AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                    AssertEqual(0, git.PushCalls.Count, "the voyage's LocalMerge does not push");
                }
            }));

            foreach (LandingModeEnum globalMode in new[] { LandingModeEnum.LocalMerge, LandingModeEnum.MergeAndPush })
            {
                LandingModeEnum mode = globalMode;
                int expectedPushes = mode == LandingModeEnum.MergeAndPush ? 1 : 0;
                cases.Add(CaseAsync("global_" + mode.ToString().ToLowerInvariant() + "_applies_when_vessel_unset", "Global LandingMode " + mode + " applies when the vessel and voyage leave it unset", TestTags.Positive, async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        StubGitService git = new StubGitService();
                        ArmadaSettings settings = CreateSettings();
                        settings.LandingMode = mode;
                        MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService(), settings);
                        LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, null, null, git).ConfigureAwait(false);

                        await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                        Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                        AssertEqual(MissionStatusEnum.Complete, mission!.Status);
                        AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                        AssertEqual(expectedPushes, git.PushCalls.Count, "the global landing mode decides the push");
                    }
                }));
            }

            cases.Add(CaseAsync("default_settings_merge_and_push", "No landing mode on the vessel or voyage and default settings: merges and pushes (MergeAndPush)", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService(), CreateSettings());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, null, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, mission!.Status);
                    AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                    AssertEqual(1, git.PushCalls.Count, "the MergeAndPush default pushes");
                }
            }));

            cases.Add(CaseAsync("unset_global_falls_back_to_merge_and_push", "Global landing mode cleared to null: still merges and pushes (MergeAndPush is used when unset)", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.LandingMode = null;
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService(), settings);
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, null, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, mission!.Status);
                    AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                    AssertEqual(1, git.PushCalls.Count, "MergeAndPush when nothing sets a landing mode");
                    AssertEqual(0, git.PrCalls.Count, "no pull request");
                }
            }));

            cases.Add(CaseAsync("voyage_legacy_flags_ignored", "A voyage's legacy AutoPush and AutoCreatePullRequests flags no longer change the landing", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge, null, git).ConfigureAwait(false);
                    Voyage voyage = new Voyage("legacy-flags-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    voyage.AutoPush = true;
                    voyage.AutoCreatePullRequests = true;
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                    entities.Mission.VoyageId = voyage.Id;
                    await testDb.Driver.Missions.UpdateAsync(entities.Mission).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, mission!.Status, "the vessel's LocalMerge applies");
                    AssertEqual(git.PushCalls.Count > 0 ? 0 : 1, git.MergeBranchCalls.Count, "Only LocalMerge changes the developer checkout");
                    AssertEqual(0, git.PushCalls.Count, "no push");
                    AssertEqual(0, git.PrCalls.Count, "no pull request");
                }
            }));

            cases.Add(CaseAsync("pull_request_opened", "PullRequest: push and PR create set PullRequestOpen with the PR URL", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService { CreatePrResult = "https://github.com/test/repo/pull/42" };
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.PullRequest, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.PullRequestOpen, mission!.Status);
                    AssertEqual("https://github.com/test/repo/pull/42", mission.PrUrl);
                    AssertEqual(1, git.PushCalls.Count, "branch pushed");
                    AssertEqual(1, git.PrCalls.Count, "PR created");
                    AssertEqual(0, git.MergeBranchCalls.Count, "no local merge in PR mode");
                    List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false);
                    AssertTrue(events.Exists(e => e.EventType == "mission.pull_request_open"), "mission.pull_request_open event");
                }
            }));

            cases.Add(CaseAsync("pull_request_create_failure", "PullRequest: PR creation error sets LandingFailed", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService { ShouldThrowOnCreatePr = true };
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.PullRequest, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, mission!.Status);
                    AssertContains("Error pushing/creating PR", mission.FailureReason ?? "");
                    AssertEqual(MissionFailureKindEnum.Infra, mission.FailureKind, "only a merge conflict git reports is a LandingConflict");
                    AssertNull(mission.PrUrl, "no PR URL recorded");
                }
            }));

            cases.Add(CaseAsync("pull_request_push_failure", "PullRequest: push error sets LandingFailed before any PR", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService { ShouldThrowOnPush = true };
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.PullRequest, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, mission!.Status);
                    AssertEqual(0, git.PrCalls.Count, "no PR without a pushed branch");
                }
            }));

            cases.Add(CaseAsync("pull_request_reconciler", "PR reconciler completes a merged PR and leaves an open one", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService { IsPrMergedResult = false };
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.PullRequest, null, git).ConfigureAwait(false);
                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);
                    Mission? open = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.PullRequestOpen, open!.Status);

                    AssertFalse(await handler.HandleReconcilePullRequestAsync(open).ConfigureAwait(false), "not merged yet");
                    AssertEqual(MissionStatusEnum.PullRequestOpen, (await testDb.Driver.Missions.ReadAsync(open.Id).ConfigureAwait(false))!.Status);

                    git.IsPrMergedResult = true;
                    AssertTrue(await handler.HandleReconcilePullRequestAsync(open).ConfigureAwait(false), "merged");
                    Mission? done = await testDb.Driver.Missions.ReadAsync(open.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, done!.Status);
                    AssertNotNull(done.CompletedUtc);
                }
            }));

            cases.Add(CaseAsync("merge_queue_mode_enqueues", "MergeQueue: enqueues the branch even when the vessel has a working directory", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    RecordingMergeQueueService queue = new RecordingMergeQueueService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, queue);
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.MergeQueue, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    AssertEqual(1, queue.Enqueued.Count, "entry enqueued");
                    AssertEqual(entities.Dock.BranchName, queue.Enqueued[0].BranchName);
                    AssertEqual("main", queue.Enqueued[0].TargetBranch);
                    AssertEqual(entities.Mission.Id, queue.Enqueued[0].MissionId);
                    AssertEqual(0, git.MergeBranchCalls.Count, "MergeQueue mode must not merge locally");
                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, mission!.Status, "the queue lands it later");
                }
            }));

            cases.Add(CaseAsync("none_mode_leaves_work_produced", "None: no landing, mission stays WorkProduced, branch kept", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    RecordingMergeQueueService queue = new RecordingMergeQueueService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, queue);
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.None, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, mission!.Status);
                    AssertEqual(0, git.MergeBranchCalls.Count, "None must not merge");
                    AssertEqual(0, git.PushCalls.Count, "None must not push");
                    AssertEqual(0, queue.Enqueued.Count, "None must not enqueue");
                    AssertFalse(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "branch kept for manual integration");
                }
            }));

            cases.Add(CaseAsync("voyage_landing_mode_overrides_vessel", "Voyage landing mode takes precedence over the vessel's", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge, LandingModeEnum.PullRequest, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.PullRequestOpen, mission!.Status, "voyage PullRequest beats vessel LocalMerge");
                    AssertEqual(0, git.MergeBranchCalls.Count);
                }
            }));

            foreach (bool cancelDuringCheck in new[] { false, true })
            {
                cases.Add(CaseAsync("refused_publication_" + cancelDuringCheck, cancelDuringCheck
                    ? "Cancellation during final checks cannot publish or be overwritten by completion"
                    : "A failing final check preserves the dock and refuses publication", TestTags.Negative, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                    StubGitService git = new();
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.MergeAndPush, null, git);
                    await testDb.Driver.WorkflowProfiles.CreateAsync(new WorkflowProfile { Name = "final check", Scope = WorkflowProfileScopeEnum.Vessel,
                        VesselId = entities.Vessel.Id, IsDefault = true, Active = true, BuildCommand = "check final integration" });
                    handler.LocalVerificationCommands = new FakeHostCommandExecutor { Handler = _ =>
                    {
                        if (cancelDuringCheck)
                        {
                            Mission current = testDb.Driver.Missions.ReadAsync(entities.Mission.Id).GetAwaiter().GetResult()!;
                            current.Status = MissionStatusEnum.Cancelled;
                            current.LastUpdateUtc = DateTime.UtcNow;
                            testDb.Driver.Missions.UpdateAsync(current).GetAwaiter().GetResult();
                        }
                        return new HostCommandResult { ExitCode = cancelDuringCheck ? 0 : 9, StandardError = "final check evidence" };
                    } };
                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock);
                    AssertEqual(cancelDuringCheck ? MissionStatusEnum.Cancelled : MissionStatusEnum.LandingFailed,
                        (await testDb.Driver.Missions.ReadAsync(entities.Mission.Id))!.Status);
                    AssertEqual(0, git.PushCalls.Count);
                    AssertFalse(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "Failed or cancelled checks preserve work.");
                }));
            }

            cases.Add(CaseAsync("merge_and_push_lands_with_dirty_checkout", "MergeAndPush lands from the dock while the developer checkout is dirty; the checkout sync is skipped with a warning event", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush);
                string readme = Path.Combine(f.Repos.Work, "README.md");
                string edited = File.ReadAllText(readme) + "operator edit in progress" + Environment.NewLine;
                File.WriteAllText(readme, edited);

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock);

                Mission landed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!;
                AssertEqual(MissionStatusEnum.Complete, landed.Status, landed.FailureReason);
                AssertEqual(f.WorkCommit, f.Repos.OriginHead("main"), "the verified commit is published");
                AssertEqual(edited, File.ReadAllText(readme), "the operator's edit is untouched");
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(f.Mission.Id);
                AssertTrue(events.Exists(e => e.EventType == "mission.checkout_sync_skipped"), "the skipped checkout sync is recorded");
            }));

            cases.Add(CaseAsync("merge_and_push_lands_with_unpushed_checkout_commit", "MergeAndPush lands when the developer checkout has a local commit the candidate does not include; the checkout keeps it", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                RealLandingFixture f = await RealLandingFixture.CreateAsync(testDb.Driver, LandingModeEnum.MergeAndPush);
                string local = await HarborDockGitFixture.CommitFileAsync(f.Repos.Work, "local-only.txt", "not pushed", "Operator commit not yet pushed");

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock);

                Mission landed = (await testDb.Driver.Missions.ReadAsync(f.Mission.Id))!;
                AssertEqual(MissionStatusEnum.Complete, landed.Status, landed.FailureReason);
                AssertEqual(f.WorkCommit, f.Repos.OriginHead("main"), "the verified commit is published");
                AssertFalse(f.Repos.OriginHasFile("main", "local-only.txt"), "the operator's unpushed commit is not published");
                AssertEqual(local, await HarborDockGitFixture.ResolveAsync(f.Repos.Work, "refs/heads/main"), "the checkout keeps its own commit");
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(f.Mission.Id);
                AssertTrue(events.Exists(e => e.EventType == "mission.checkout_sync_skipped"), "the skipped checkout sync is recorded");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Landing Paths", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private static MissionLandingHandler CreateHandler(DatabaseDriver db, StubGitService git, IMergeQueueService queue, ArmadaSettings? settings = null)
        {
            git.ResolveBranchTipResult = git.HeadCommitResult;
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            settings = settings ?? CreateSettings();
            IDockService docks = new DockService(logging, db, settings, git);
            return new MissionLandingHandler(logging, db, settings, git, queue, new MessageTemplateService(logging), null, docks, null);
        }

        private static async Task<LandingTestEntitiesResult> CreateEntitiesAsync(DatabaseDriver db, LandingModeEnum? vesselMode, LandingModeEnum? voyageMode, StubGitService git)
        {
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 12);
            Vessel vessel = new Vessel("landing-vessel-" + suffix, "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + suffix);
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + suffix);
            vessel.DefaultBranch = "main";
            vessel.LandingMode = vesselMode;
            vessel.BranchCleanupPolicy = BranchCleanupPolicyEnum.LocalOnly;
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Voyage? voyage = null;
            if (voyageMode.HasValue)
            {
                voyage = new Voyage("landing-voyage-" + suffix);
                voyage.LandingMode = voyageMode;
                voyage = await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);
            }

            Captain captain = new Captain("landing-captain-" + suffix);
            captain.State = CaptainStateEnum.Working;
            captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);

            Dock dock = new Dock(vessel.Id);
            dock.CaptainId = captain.Id;
            dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_wt_" + suffix);
            // The dock's worktree exists, as it does after a captain finishes; landing recreates a missing one.
            Directory.CreateDirectory(dock.WorktreePath);
            dock.BranchName = "armada/landing/" + suffix;
            dock.Active = true;
            dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);
            git.ExistingBranches.Add(dock.BranchName);

            Mission mission = new Mission("Landing mission " + suffix);
            mission.Status = MissionStatusEnum.WorkProduced;
            mission.CaptainId = captain.Id;
            mission.DockId = dock.Id;
            mission.VesselId = vessel.Id;
            mission.VoyageId = voyage?.Id;
            mission.DiffSnapshot = "diff --git a/file.txt b/file.txt";
            mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);

            return new LandingTestEntitiesResult(captain, mission, dock, vessel);
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
