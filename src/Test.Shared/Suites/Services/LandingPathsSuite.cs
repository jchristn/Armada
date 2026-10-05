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
                    AssertEqual(MissionFailureKindEnum.LandingConflict, mission.FailureKind, "failure kind is recorded where the failure happens");
                    AssertFalse(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "branch kept for retry");
                }
            }));

            cases.Add(CaseAsync("local_merge_push_failure_sets_landing_failed", "LocalMerge: push after merge fails, LandingFailed and branch kept", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    StubGitService git = new StubGitService { ShouldThrowOnPush = true };
                    MissionLandingHandler handler = CreateHandler(testDb.Driver, git, new RecordingMergeQueueService());
                    LandingTestEntitiesResult entities = await CreateEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge, null, git).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    Mission? mission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.LandingFailed, mission!.Status);
                    AssertContains("Local merge succeeded but push failed", mission.FailureReason ?? "");
                    AssertEqual(MissionFailureKindEnum.LandingConflict, mission.FailureKind, "failure kind is recorded where the failure happens");
                    AssertEqual(1, git.MergeBranchCalls.Count, "merge ran");
                    AssertFalse(git.OperationCalls.Contains("delete-local-branch:" + entities.Dock.BranchName), "branch kept for retry");
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
                    AssertEqual(MissionFailureKindEnum.LandingConflict, mission.FailureKind, "failure kind is recorded where the failure happens");
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

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Landing Paths", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static MissionLandingHandler CreateHandler(DatabaseDriver db, StubGitService git, IMergeQueueService queue)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            IDockService docks = new DockService(logging, db, settings, git);
            return new MissionLandingHandler(logging, db, settings, git, queue, new MessageTemplateService(logging), null, docks, null);
        }

        private static async Task<LandingTestEntitiesResult> CreateEntitiesAsync(DatabaseDriver db, LandingModeEnum vesselMode, LandingModeEnum? voyageMode, StubGitService git)
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
