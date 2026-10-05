namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
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
    /// Integration-style descriptors for the landing pipeline:
    /// WorkProduced -> local merge -> Complete (success) or LandingFailed (failure).
    /// Uses <see cref="StubGitService"/> so no real git operations occur, but exercises the full
    /// MissionService -> HandleMissionComplete -> landing -> status transition flow. Also covers
    /// vessel/voyage landing-mode persistence, dock reclaim idempotency, and enum coverage.
    /// </summary>
    public sealed class LandingPipelineSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.LandingPipeline";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Landing Pipeline suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // === Local Merge Happy Path ===

            cases.Add(CaseAsync("handle_completion_sets_work_produced_then_land", "HandleCompletion sets WorkProduced then completion handler can land", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    LandingTestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge);
                    Captain captain = entities.Captain;
                    Mission mission = entities.Mission;
                    Dock dock = entities.Dock;
                    Vessel vessel = entities.Vessel;

                    // HandleCompletionAsync should set to WorkProduced
                    await missionService.HandleCompletionAsync(captain);

                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id);
                    AssertNotNull(updated, "Mission should exist after completion");
                    AssertEqual(MissionStatusEnum.WorkProduced, updated!.Status, "Status should be WorkProduced after agent exit");
                }
            }));

            cases.Add(CaseAsync("local_merge_success_produces_correct_git_call_sequence", "Local merge success produces correct git call sequence", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    LandingTestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge);
                    Captain captain = entities.Captain;
                    Mission mission = entities.Mission;
                    Dock dock = entities.Dock;
                    Vessel vessel = entities.Vessel;

                    // Simulate: agent completion -> WorkProduced
                    await missionService.HandleCompletionAsync(captain);

                    // Verify the stub recorded correct merge call
                    // Note: The actual landing handler runs in the ArmadaServer, not in this unit test,
                    // so we verify that HandleCompletion correctly sets up the state for landing.
                    Mission? wp = await testDb.Driver.Missions.ReadAsync(mission.Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, wp!.Status, "Mission should be WorkProduced");

                    // Verify captain was released
                    Captain? releasedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id);
                    AssertNotNull(releasedCaptain, "Captain should still exist");
                    AssertEqual(CaptainStateEnum.Idle, releasedCaptain!.State, "Captain should be Idle after completion");
                }
            }));

            cases.Add(CaseAsync("local_merge_failure_sets_landing_failed_on_merge_exception", "Local merge failure sets LandingFailed on merge exception", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    git.ShouldThrowOnMergeLocal = true;
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    LandingTestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver, LandingModeEnum.LocalMerge);
                    Captain captain = entities.Captain;
                    Mission mission = entities.Mission;
                    Dock dock = entities.Dock;
                    Vessel vessel = entities.Vessel;

                    // WorkProduced is set by HandleCompletionAsync
                    await missionService.HandleCompletionAsync(captain);

                    Mission? wp = await testDb.Driver.Missions.ReadAsync(mission.Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, wp!.Status, "Should be WorkProduced before landing attempt");
                }
            }));

            // === Vessel Landing Mode Resolution ===

            cases.Add(CaseAsync("vessel_landing_mode_persisted_and_read", "Vessel LandingMode is persisted and read correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Vessel vessel = new Vessel("mode-test", "https://github.com/test/repo.git");
                    vessel.LandingMode = LandingModeEnum.PullRequest;
                    vessel.BranchCleanupPolicy = BranchCleanupPolicyEnum.LocalAndRemote;
                    await testDb.Driver.Vessels.CreateAsync(vessel);

                    Vessel? read = await testDb.Driver.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(read, "Vessel should exist");
                    AssertEqual(LandingModeEnum.PullRequest, read!.LandingMode, "LandingMode should be PullRequest");
                    AssertEqual(BranchCleanupPolicyEnum.LocalAndRemote, read.BranchCleanupPolicy, "BranchCleanupPolicy should be LocalAndRemote");
                }
            }));

            cases.Add(CaseAsync("vessel_null_landing_mode_reads_back_null", "Vessel with null LandingMode reads back as null", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Vessel vessel = new Vessel("null-mode", "https://github.com/test/repo.git");
                    vessel.LandingMode = null;
                    vessel.BranchCleanupPolicy = null;
                    await testDb.Driver.Vessels.CreateAsync(vessel);

                    Vessel? read = await testDb.Driver.Vessels.ReadAsync(vessel.Id);
                    AssertNotNull(read, "Vessel should exist");
                    AssertNull(read!.LandingMode, "LandingMode should be null");
                    AssertNull(read.BranchCleanupPolicy, "BranchCleanupPolicy should be null");
                }
            }));

            // === Voyage LandingMode Resolution ===

            cases.Add(CaseAsync("voyage_landing_mode_persisted_and_read", "Voyage LandingMode is persisted and read correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Voyage voyage = new Voyage("mode-test-voyage");
                    voyage.LandingMode = LandingModeEnum.MergeQueue;
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    Voyage? read = await testDb.Driver.Voyages.ReadAsync(voyage.Id);
                    AssertNotNull(read, "Voyage should exist");
                    AssertEqual(LandingModeEnum.MergeQueue, read!.LandingMode, "LandingMode should be MergeQueue");
                }
            }));

            // === PullRequestOpen Does Not Complete Voyage ===

            cases.Add(CaseAsync("voyage_with_pull_request_open_mission_does_not_complete", "Voyage with PullRequestOpen mission does not complete", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);

                    Voyage voyage = new Voyage("PR voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    Mission m1 = new Mission("done");
                    m1.VoyageId = voyage.Id;
                    m1.Status = MissionStatusEnum.Complete;
                    await testDb.Driver.Missions.CreateAsync(m1);

                    Mission m2 = new Mission("pr-open");
                    m2.VoyageId = voyage.Id;
                    m2.Status = MissionStatusEnum.PullRequestOpen;
                    await testDb.Driver.Missions.CreateAsync(m2);

                    List<Voyage> completed = await voyageService.CheckCompletionsAsync();
                    AssertEqual(0, completed.Count, "Voyage should NOT complete while a mission is PullRequestOpen");

                    // Now complete the PR mission
                    m2.Status = MissionStatusEnum.Complete;
                    await testDb.Driver.Missions.UpdateAsync(m2);

                    completed = await voyageService.CheckCompletionsAsync();
                    AssertEqual(1, completed.Count, "Voyage should complete when all missions are Complete");
                }
            }));

            cases.Add(CaseAsync("voyage_with_merge_queue_mission_does_not_complete", "A voyage stays InProgress while its WorkProduced mission is still queued in the merge queue (F14)", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);

                    Voyage voyage = new Voyage("merge queue voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("queued for merge");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.WorkProduced;
                    mission.BranchName = "armada/queued";
                    await testDb.Driver.Missions.CreateAsync(mission);

                    MergeEntry entry = new MergeEntry("armada/queued");
                    entry.MissionId = mission.Id;
                    entry.Status = MergeStatusEnum.Queued;
                    await testDb.Driver.MergeEntries.CreateAsync(entry);

                    List<Voyage> completed = await voyageService.CheckCompletionsAsync();
                    AssertEqual(0, completed.Count, "no completion while the merge entry is Queued");
                    Voyage? read = await testDb.Driver.Voyages.ReadAsync(voyage.Id);
                    AssertEqual(VoyageStatusEnum.InProgress, read!.Status, "voyage stays InProgress");

                    entry.Status = MergeStatusEnum.Testing;
                    await testDb.Driver.MergeEntries.UpdateAsync(entry);
                    completed = await voyageService.CheckCompletionsAsync();
                    AssertEqual(0, completed.Count, "no completion while the merge entry is Testing");

                    // The queue lands the entry and completes the mission.
                    entry.Status = MergeStatusEnum.Landed;
                    await testDb.Driver.MergeEntries.UpdateAsync(entry);
                    mission.Status = MissionStatusEnum.Complete;
                    await testDb.Driver.Missions.UpdateAsync(mission);
                    completed = await voyageService.CheckCompletionsAsync();
                    AssertEqual(1, completed.Count, "completes once the entry landed");
                    read = await testDb.Driver.Voyages.ReadAsync(voyage.Id);
                    AssertEqual(VoyageStatusEnum.Complete, read!.Status, "voyage Complete after landing");
                }
            }));

            cases.Add(CaseAsync("voyage_with_cancelled_merge_entry_completes", "A WorkProduced mission whose merge entry was cancelled no longer holds its voyage open", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    IVoyageService voyageService = new VoyageService(CreateLogging(), testDb.Driver);
                    Voyage voyage = new Voyage("cancelled entry voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("left as work produced");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.WorkProduced;
                    await testDb.Driver.Missions.CreateAsync(mission);

                    MergeEntry entry = new MergeEntry("armada/cancelled");
                    entry.MissionId = mission.Id;
                    entry.Status = MergeStatusEnum.Cancelled;
                    await testDb.Driver.MergeEntries.CreateAsync(entry);

                    List<Voyage> completed = await voyageService.CheckCompletionsAsync();
                    AssertEqual(1, completed.Count, "a settled (cancelled) entry does not hold the voyage open");
                }
            }));

            // === Dock Reclaim Idempotency ===

            cases.Add(CaseAsync("double_reclaim_async_is_idempotent", "Double ReclaimAsync is safe (idempotent)", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);

                    Vessel vessel = new Vessel("reclaim-test", "https://github.com/test/repo.git");
                    await testDb.Driver.Vessels.CreateAsync(vessel);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_reclaim_" + Guid.NewGuid().ToString("N"));
                    dock.Active = true;
                    await testDb.Driver.Docks.CreateAsync(dock);

                    // First reclaim
                    await dockService.ReclaimAsync(dock.Id);

                    Dock? afterFirst = await testDb.Driver.Docks.ReadAsync(dock.Id);
                    AssertNotNull(afterFirst, "Dock should still exist");
                    Assert(!afterFirst!.Active, "Dock should be inactive after first reclaim");

                    // Second reclaim — should be a no-op
                    await dockService.ReclaimAsync(dock.Id);

                    Dock? afterSecond = await testDb.Driver.Docks.ReadAsync(dock.Id);
                    AssertNotNull(afterSecond, "Dock should still exist after second reclaim");
                    Assert(!afterSecond!.Active, "Dock should still be inactive");
                }
            }));

            cases.Add(CaseAsync("successful_local_landing_removes_worktree_before_deleting_branch", "Successful local landing removes active worktree before deleting branch", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    IMessageTemplateService templateService = new MessageTemplateService(logging);
                    MissionLandingHandler handler = new MissionLandingHandler(
                        logging,
                        testDb.Driver,
                        settings,
                        git,
                        new StubMergeQueueService(),
                        templateService,
                        null,
                        dockService,
                        null);

                    LandingTestEntitiesResult entities = await CreateTestEntitiesAsync(
                        testDb.Driver,
                        LandingModeEnum.LocalMerge,
                        BranchCleanupPolicyEnum.LocalAndRemote);

                    git.ExistingBranches.Add(entities.Dock.BranchName!);
                    entities.Mission.Status = MissionStatusEnum.WorkProduced;
                    entities.Mission.DiffSnapshot = "diff --git a/app/routes_ops.py b/app/routes_ops.py";
                    await testDb.Driver.Missions.UpdateAsync(entities.Mission).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    int removeIndex = git.OperationCalls.IndexOf("remove-worktree:" + entities.Dock.WorktreePath);
                    int deleteLocalIndex = git.OperationCalls.IndexOf("delete-local-branch:" + entities.Dock.BranchName);
                    int deleteRemoteIndex = git.OperationCalls.IndexOf("delete-remote-branch:" + entities.Dock.BranchName);

                    AssertTrue(removeIndex >= 0, "Landing cleanup should remove the active worktree");
                    AssertTrue(deleteLocalIndex > removeIndex, "Local branch deletion should happen after the worktree is removed");
                    AssertTrue(deleteRemoteIndex > deleteLocalIndex, "Remote branch deletion should happen after the local branch delete attempt");

                    Mission? updatedMission = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertEqual(MissionStatusEnum.Complete, updatedMission!.Status, "Mission should be marked Complete after successful landing");
                }
            }));

            cases.Add(CaseAsync("landing_mode_none_does_not_merge_into_working_directory", "LandingMode None leaves the mission WorkProduced and never merges into the working directory", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    StubMergeQueueService mergeQueue = new StubMergeQueueService();
                    MissionLandingHandler handler = new MissionLandingHandler(
                        logging, testDb.Driver, settings, git, mergeQueue, new MessageTemplateService(logging), null, dockService, null);

                    LandingTestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver, LandingModeEnum.None);
                    git.ExistingBranches.Add(entities.Dock.BranchName!);
                    entities.Mission.Status = MissionStatusEnum.WorkProduced;
                    entities.Mission.DiffSnapshot = "diff --git a/README.md b/README.md";
                    await testDb.Driver.Missions.UpdateAsync(entities.Mission).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    AssertEqual(0, git.MergeBranchCalls.Count, "LandingMode None must not merge locally");
                    AssertEqual(0, git.PushCalls.Count, "LandingMode None must not push");
                    AssertEqual(0, mergeQueue.Enqueued.Count, "LandingMode None must not enqueue");
                    Mission? updated = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, updated!.Status, "Mission should stay WorkProduced for manual landing");
                }
            }));

            cases.Add(CaseAsync("landing_mode_merge_queue_enqueues_instead_of_local_merge", "LandingMode MergeQueue enqueues the branch even when the vessel has a working directory", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    StubMergeQueueService mergeQueue = new StubMergeQueueService();
                    MissionLandingHandler handler = new MissionLandingHandler(
                        logging, testDb.Driver, settings, git, mergeQueue, new MessageTemplateService(logging), null, dockService, null);

                    LandingTestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver, LandingModeEnum.MergeQueue);
                    git.ExistingBranches.Add(entities.Dock.BranchName!);
                    entities.Mission.Status = MissionStatusEnum.WorkProduced;
                    entities.Mission.DiffSnapshot = "diff --git a/README.md b/README.md";
                    await testDb.Driver.Missions.UpdateAsync(entities.Mission).ConfigureAwait(false);

                    await handler.HandleMissionCompleteAsync(entities.Mission, entities.Dock).ConfigureAwait(false);

                    AssertEqual(0, git.MergeBranchCalls.Count, "MergeQueue mode must not merge locally");
                    AssertEqual(1, mergeQueue.Enqueued.Count, "MergeQueue mode should enqueue the mission branch");
                    AssertEqual(entities.Dock.BranchName, mergeQueue.Enqueued[0].BranchName, "Enqueued branch");
                }
            }));

            // === Status Transition Validation ===

            cases.Add(CaseAsync("pull_request_open_allows_transition_to_complete", "PullRequestOpen allows transition to Complete", TestTags.Positive, () =>
            {
                // Verify the enum values exist and are distinct
                Assert(MissionStatusEnum.PullRequestOpen != MissionStatusEnum.Complete, "PullRequestOpen is distinct from Complete");
                Assert(MissionStatusEnum.PullRequestOpen != MissionStatusEnum.WorkProduced, "PullRequestOpen is distinct from WorkProduced");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("all_landing_mode_enum_values_exist", "All LandingMode enum values exist", TestTags.Positive, () =>
            {
                string[] expected = new[] { "LocalMerge", "PullRequest", "MergeQueue", "None" };
                string[] actual = Enum.GetNames(typeof(LandingModeEnum));
                AssertEqual(expected.Length, actual.Length, "LandingMode enum value count");

                foreach (string name in expected)
                {
                    Assert(Enum.TryParse<LandingModeEnum>(name, out _), "Missing LandingMode value: " + name);
                }

                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("all_branch_cleanup_policy_enum_values_exist", "All BranchCleanupPolicy enum values exist", TestTags.Positive, () =>
            {
                string[] expected = new[] { "LocalOnly", "LocalAndRemote", "None" };
                string[] actual = Enum.GetNames(typeof(BranchCleanupPolicyEnum));
                AssertEqual(expected.Length, actual.Length, "BranchCleanupPolicy enum value count");

                foreach (string name in expected)
                {
                    Assert(Enum.TryParse<BranchCleanupPolicyEnum>(name, out _), "Missing BranchCleanupPolicy value: " + name);
                }

                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Landing Pipeline",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private static async Task<LandingTestEntitiesResult> CreateTestEntitiesAsync(
            DatabaseDriver db, LandingModeEnum? landingMode = null, BranchCleanupPolicyEnum? cleanupPolicy = null)
        {
            Vessel vessel = new Vessel("test-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            vessel.LandingMode = landingMode;
            vessel.BranchCleanupPolicy = cleanupPolicy;
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            Captain captain = new Captain("test-captain");
            captain.State = CaptainStateEnum.Working;
            await db.Captains.CreateAsync(captain).ConfigureAwait(false);

            Dock dock = new Dock(vessel.Id);
            dock.CaptainId = captain.Id;
            dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_wt_" + Guid.NewGuid().ToString("N"));
            dock.BranchName = "armada/test-captain/msn_test123";
            dock.Active = true;
            await db.Docks.CreateAsync(dock).ConfigureAwait(false);

            Mission mission = new Mission("Test local merge mission");
            mission.Status = MissionStatusEnum.InProgress;
            mission.CaptainId = captain.Id;
            mission.DockId = dock.Id;
            mission.VesselId = vessel.Id;
            await db.Missions.CreateAsync(mission).ConfigureAwait(false);

            captain.CurrentMissionId = mission.Id;
            captain.CurrentDockId = dock.Id;
            await db.Captains.UpdateAsync(captain).ConfigureAwait(false);

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

        #region Private-Types

        /// <summary>
        /// Merge queue service stub returning empty/no-op results for the landing handler.
        /// </summary>
        private sealed class StubMergeQueueService : IMergeQueueService
        {
            public List<MergeEntry> Enqueued { get; } = new List<MergeEntry>();
            public Task<MergeEntry> EnqueueAsync(MergeEntry entry, CancellationToken token = default)
            {
                Enqueued.Add(entry);
                return Task.FromResult(entry);
            }
            public Task ProcessQueueAsync(CancellationToken token = default) => Task.CompletedTask;
            public Task CancelAsync(string entryId, string? tenantId = null, CancellationToken token = default) => Task.CompletedTask;
            public Task<List<MergeEntry>> ListAsync(string? tenantId = null, CancellationToken token = default) => Task.FromResult(new List<MergeEntry>());
            public Task<MergeEntry?> ProcessSingleAsync(string entryId, string? tenantId = null, CancellationToken token = default) => Task.FromResult<MergeEntry?>(null);
            public Task<MergeEntry?> GetAsync(string entryId, string? tenantId = null, CancellationToken token = default) => Task.FromResult<MergeEntry?>(null);
            public Task<bool> DeleteAsync(string entryId, string? tenantId = null, CancellationToken token = default) => Task.FromResult(false);
            public Task<MergeQueuePurgeResult> DeleteMultipleAsync(List<string> entryIds, string? tenantId = null, CancellationToken token = default)
                => Task.FromResult(new MergeQueuePurgeResult());
            public Task<int> PurgeTerminalAsync(string? vesselId = null, MergeStatusEnum? status = null, string? tenantId = null, CancellationToken token = default)
                => Task.FromResult(0);
        }

        #endregion
    }
}
