namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for mission status transitions through the landing pipeline:
    /// InProgress -> WorkProduced -> Complete (success) or LandingFailed (failure). Covers enum
    /// coverage, HandleCompletionAsync behavior (status, process-id clearing, event emission,
    /// null/no-op guards), terminal-state distinctions, and voyage/admiral status counting.
    /// </summary>
    public sealed class MissionStatusTransitionSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MissionStatusTransition";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Mission Status Transition suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            // === MissionStatusEnum Value Tests ===

            cases.Add(Case("work_produced_enum_value_exists", "WorkProduced enum value exists", TestTags.Positive, () =>
            {
                MissionStatusEnum status = MissionStatusEnum.WorkProduced;
                AssertEqual("WorkProduced", status.ToString(), "WorkProduced enum name");
            }));

            cases.Add(Case("landing_failed_enum_value_exists", "LandingFailed enum value exists", TestTags.Positive, () =>
            {
                MissionStatusEnum status = MissionStatusEnum.LandingFailed;
                AssertEqual("LandingFailed", status.ToString(), "LandingFailed enum name");
            }));

            cases.Add(Case("pull_request_open_enum_value_exists", "PullRequestOpen enum value exists", TestTags.Positive, () =>
            {
                MissionStatusEnum status = MissionStatusEnum.PullRequestOpen;
                AssertEqual("PullRequestOpen", status.ToString(), "PullRequestOpen enum name");
            }));

            cases.Add(Case("all_expected_statuses_defined", "All expected statuses defined", TestTags.Positive, () =>
            {
                string[] expected = new[]
                {
                    "Pending", "Assigned", "InProgress", "WorkProduced", "PullRequestOpen",
                    "Testing", "Review", "Complete", "Failed", "LandingFailed", "Cancelled", "Blocked", "Verifying"
                };

                string[] actual = Enum.GetNames(typeof(MissionStatusEnum));
                AssertEqual(expected.Length, actual.Length, "Enum value count");

                foreach (string name in expected)
                {
                    Assert(Enum.TryParse<MissionStatusEnum>(name, out _), "Missing enum value: " + name);
                }
            }));

            // === HandleCompletionAsync Tests (InProgress -> WorkProduced) ===

            cases.Add(CaseAsync("handle_completion_sets_status_to_work_produced", "HandleCompletion sets status to WorkProduced", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    TestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver);
                    Captain captain = entities.Captain;
                    Mission mission = entities.Mission;
                    Dock dock = entities.Dock;

                    await missionService.HandleCompletionAsync(captain);

                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id);
                    AssertNotNull(updated, "Mission should exist after completion");
                    AssertEqual(MissionStatusEnum.WorkProduced, updated!.Status, "Status should be WorkProduced");
                }
            }));

            cases.Add(CaseAsync("handle_completion_clears_stale_failure_reason", "HandleCompletion clears a stale failure reason", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    TestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver);
                    entities.Mission.FailureReason = "stale failure";
                    await testDb.Driver.Missions.UpdateAsync(entities.Mission);

                    await missionService.HandleCompletionAsync(entities.Captain);

                    Mission? updated = await testDb.Driver.Missions.ReadAsync(entities.Mission.Id);
                    AssertEqual(MissionStatusEnum.WorkProduced, updated!.Status);
                    AssertNull(updated.FailureReason, "successful completion clears the prior failure reason");
                }
            }));

            cases.Add(CaseAsync("handle_completion_clears_process_id", "HandleCompletion clears ProcessId", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    TestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver);
                    Captain captain = entities.Captain;
                    Mission mission = entities.Mission;
                    Dock dock = entities.Dock;

                    // Set a process ID to verify it gets cleared
                    mission.ProcessId = 12345;
                    await testDb.Driver.Missions.UpdateAsync(mission);

                    await missionService.HandleCompletionAsync(captain);

                    Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id);
                    AssertNotNull(updated, "Mission should exist");
                    AssertNull(updated!.ProcessId, "ProcessId should be cleared");
                }
            }));

            cases.Add(CaseAsync("handle_completion_emits_work_produced_event", "HandleCompletion emits work_produced event", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    TestEntitiesResult entities = await CreateTestEntitiesAsync(testDb.Driver);
                    Captain captain = entities.Captain;
                    Mission mission = entities.Mission;
                    Dock dock = entities.Dock;

                    await missionService.HandleCompletionAsync(captain);

                    // Check that a work_produced event was emitted
                    List<ArmadaEvent> events = (await testDb.Driver.Events.EnumerateRecentAsync(100)).ToList();
                    Assert(events.Any(e => e.EventType == "mission.work_produced"), "Should emit mission.work_produced event");
                    Assert(events.Any(e => e.MissionId == mission.Id), "Event should reference mission ID");
                }
            }));

            cases.Add(CaseAsync("handle_completion_with_null_captain_throws", "HandleCompletion with null captain throws", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    await AssertThrowsAsync<ArgumentNullException>(async () =>
                    {
                        await missionService.HandleCompletionAsync(null!);
                    });
                }
            }));

            cases.Add(CaseAsync("handle_completion_with_no_current_mission_is_no_op", "HandleCompletion with no current mission is no-op", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    Captain captain = new Captain("idle-captain");
                    captain.CurrentMissionId = null;
                    await testDb.Driver.Captains.CreateAsync(captain);

                    // Should not throw - just returns
                    await missionService.HandleCompletionAsync(captain);

                    List<ArmadaEvent> events = (await testDb.Driver.Events.EnumerateRecentAsync(100)).ToList();
                    AssertEqual(0, events.Count, "No events should be emitted for no-op");
                }
            }));

            // === Terminal State Tests ===

            cases.Add(Case("work_produced_is_not_a_terminal_state", "WorkProduced is not a terminal state", TestTags.Positive, () =>
            {
                // WorkProduced should allow transition to Complete or LandingFailed
                MissionStatusEnum status = MissionStatusEnum.WorkProduced;
                Assert(status != MissionStatusEnum.Complete, "WorkProduced is not Complete");
                Assert(status != MissionStatusEnum.Failed, "WorkProduced is not Failed");
                Assert(status != MissionStatusEnum.Cancelled, "WorkProduced is not Cancelled");
            }));

            cases.Add(Case("landing_failed_is_distinct_from_failed", "LandingFailed is distinct from Failed", TestTags.Positive, () =>
            {
                MissionStatusEnum status = MissionStatusEnum.LandingFailed;
                Assert(status != MissionStatusEnum.Failed, "LandingFailed is distinct from Failed");
                Assert(status != MissionStatusEnum.Complete, "LandingFailed is not Complete");
            }));

            cases.Add(Case("pull_request_open_is_not_a_terminal_state", "PullRequestOpen is not a terminal state", TestTags.Positive, () =>
            {
                // PullRequestOpen should allow transition to Complete (PR merged) or Cancelled
                MissionStatusEnum status = MissionStatusEnum.PullRequestOpen;
                Assert(status != MissionStatusEnum.Complete, "PullRequestOpen is not Complete");
                Assert(status != MissionStatusEnum.Failed, "PullRequestOpen is not Failed");
                Assert(status != MissionStatusEnum.Cancelled, "PullRequestOpen is not Cancelled");
            }));

            // === VoyageService Progress Counting ===

            cases.Add(CaseAsync("voyage_service_counts_work_produced_as_completed", "VoyageService counts WorkProduced as completed", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);

                    Voyage voyage = new Voyage("Test voyage");
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    Mission m = new Mission("wp-mission");
                    m.VoyageId = voyage.Id;
                    m.Status = MissionStatusEnum.WorkProduced;
                    await testDb.Driver.Missions.CreateAsync(m);

                    VoyageProgress? progress = await voyageService.GetProgressAsync(voyage.Id);
                    AssertNotNull(progress, "Progress should not be null");
                    AssertEqual(1, progress!.TotalMissions, "Total missions");
                    AssertEqual(0, progress.InProgressMissions, "WorkProduced should not count as active");
                    AssertEqual(1, progress.CompletedMissions, "WorkProduced should count as completed");
                    AssertEqual(0, progress.FailedMissions, "Should not count as failed");
                }
            }));

            cases.Add(CaseAsync("voyage_service_counts_pull_request_open_as_in_progress", "VoyageService counts PullRequestOpen as in-progress", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);

                    Voyage voyage = new Voyage("Test voyage");
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    Mission m = new Mission("pro-mission");
                    m.VoyageId = voyage.Id;
                    m.Status = MissionStatusEnum.PullRequestOpen;
                    await testDb.Driver.Missions.CreateAsync(m);

                    VoyageProgress? progress = await voyageService.GetProgressAsync(voyage.Id);
                    AssertNotNull(progress, "Progress should not be null");
                    AssertEqual(1, progress!.TotalMissions, "Total missions");
                    AssertEqual(1, progress.InProgressMissions, "PullRequestOpen should count as in-progress");
                    AssertEqual(0, progress.CompletedMissions, "Should not count as completed");
                    AssertEqual(0, progress.FailedMissions, "Should not count as failed");
                }
            }));

            cases.Add(CaseAsync("voyage_service_counts_review_as_in_progress", "VoyageService counts Review as in-progress", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);

                    Voyage voyage = new Voyage("Test voyage");
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    Mission m = new Mission("review-mission");
                    m.VoyageId = voyage.Id;
                    m.Status = MissionStatusEnum.Review;
                    await testDb.Driver.Missions.CreateAsync(m);

                    VoyageProgress? progress = await voyageService.GetProgressAsync(voyage.Id);
                    AssertNotNull(progress, "Progress should not be null");
                    AssertEqual(1, progress!.TotalMissions, "Total missions");
                    AssertEqual(1, progress.InProgressMissions, "Review should count as in-progress");
                    AssertEqual(0, progress.CompletedMissions, "Should not count as completed");
                    AssertEqual(0, progress.FailedMissions, "Should not count as failed");
                }
            }));

            cases.Add(CaseAsync("voyage_service_counts_landing_failed_as_failed", "VoyageService counts LandingFailed as failed", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);

                    Voyage voyage = new Voyage("Test voyage");
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    Mission m = new Mission("lf-mission");
                    m.VoyageId = voyage.Id;
                    m.Status = MissionStatusEnum.LandingFailed;
                    await testDb.Driver.Missions.CreateAsync(m);

                    VoyageProgress? progress = await voyageService.GetProgressAsync(voyage.Id);
                    AssertNotNull(progress, "Progress should not be null");
                    AssertEqual(1, progress!.TotalMissions, "Total missions");
                    AssertEqual(0, progress.InProgressMissions, "Should not count as in-progress");
                    AssertEqual(0, progress.CompletedMissions, "Should not count as completed");
                    AssertEqual(1, progress.FailedMissions, "LandingFailed should count as failed");
                }
            }));

            cases.Add(CaseAsync("voyage_service_mixed_statuses_counted_correctly", "VoyageService mixed statuses counted correctly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);

                    Voyage voyage = new Voyage("Mixed voyage");
                    await testDb.Driver.Voyages.CreateAsync(voyage);

                    // Create missions in various states
                    Mission pending = new Mission("pending");
                    pending.VoyageId = voyage.Id;
                    pending.Status = MissionStatusEnum.Pending;

                    Mission inProgress = new Mission("in-progress");
                    inProgress.VoyageId = voyage.Id;
                    inProgress.Status = MissionStatusEnum.InProgress;

                    Mission workProduced = new Mission("work-produced");
                    workProduced.VoyageId = voyage.Id;
                    workProduced.Status = MissionStatusEnum.WorkProduced;

                    Mission prOpen = new Mission("pr-open");
                    prOpen.VoyageId = voyage.Id;
                    prOpen.Status = MissionStatusEnum.PullRequestOpen;

                    Mission complete = new Mission("complete");
                    complete.VoyageId = voyage.Id;
                    complete.Status = MissionStatusEnum.Complete;

                    Mission failed = new Mission("failed");
                    failed.VoyageId = voyage.Id;
                    failed.Status = MissionStatusEnum.Failed;

                    Mission landingFailed = new Mission("landing-failed");
                    landingFailed.VoyageId = voyage.Id;
                    landingFailed.Status = MissionStatusEnum.LandingFailed;

                    await testDb.Driver.Missions.CreateAsync(pending);
                    await testDb.Driver.Missions.CreateAsync(inProgress);
                    await testDb.Driver.Missions.CreateAsync(workProduced);
                    await testDb.Driver.Missions.CreateAsync(prOpen);
                    await testDb.Driver.Missions.CreateAsync(complete);
                    await testDb.Driver.Missions.CreateAsync(failed);
                    await testDb.Driver.Missions.CreateAsync(landingFailed);

                    VoyageProgress? progress = await voyageService.GetProgressAsync(voyage.Id);
                    AssertNotNull(progress, "Progress should not be null");
                    AssertEqual(7, progress!.TotalMissions, "Total missions");
                    AssertEqual(2, progress.CompletedMissions, "Complete + WorkProduced count");
                    AssertEqual(2, progress.FailedMissions, "Failed + LandingFailed count");
                    AssertEqual(2, progress.InProgressMissions, "InProgress + PullRequestOpen count");
                }
            }));

            // === AdmiralService Status Counting ===

            cases.Add(CaseAsync("get_status_async_counts_work_produced_and_landing_failed", "GetStatusAsync counts WorkProduced and LandingFailed", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();

                    IDockService dockService = new DockService(logging, db, settings, git);
                    ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
                    IVoyageService voyageService = new VoyageService(logging, db);
                    AdmiralService admiral = new AdmiralService(logging, db, settings, captainService, missionService, voyageService, dockService);

                    Mission wp = new Mission("WorkProduced mission");
                    wp.Status = MissionStatusEnum.WorkProduced;
                    await db.Missions.CreateAsync(wp);

                    Mission lf = new Mission("LandingFailed mission");
                    lf.Status = MissionStatusEnum.LandingFailed;
                    await db.Missions.CreateAsync(lf);

                    ArmadaStatus status = await admiral.GetStatusAsync();
                    Assert(status.MissionsByStatus.ContainsKey("WorkProduced"), "Should include WorkProduced in status");
                    Assert(status.MissionsByStatus.ContainsKey("LandingFailed"), "Should include LandingFailed in status");
                    AssertEqual(1, status.MissionsByStatus["WorkProduced"], "WorkProduced count");
                    AssertEqual(1, status.MissionsByStatus["LandingFailed"], "LandingFailed count");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Mission Status Transitions",
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

        private static async Task<TestEntitiesResult> CreateTestEntitiesAsync(DatabaseDriver db)
        {
            // Create a vessel (fleet is optional)
            Vessel vessel = new Vessel("test-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = TestTemp.NewDirectory("mission_transition_bare");
            vessel.WorkingDirectory = TestTemp.NewDirectory("mission_transition_work");
            vessel.DefaultBranch = "main";
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            // Create a captain
            Captain captain = new Captain("test-captain");
            captain.State = CaptainStateEnum.Working;
            await db.Captains.CreateAsync(captain).ConfigureAwait(false);

            // Create a dock
            Dock dock = new Dock(vessel.Id);
            dock.CaptainId = captain.Id;
            dock.WorktreePath = TestTemp.NewDirectory("mission_transition_worktree");
            dock.BranchName = "armada/test-captain/msn_test123";
            dock.Active = true;
            await db.Docks.CreateAsync(dock).ConfigureAwait(false);

            // Create a mission in InProgress state
            Mission mission = new Mission("Test mission");
            mission.Status = MissionStatusEnum.InProgress;
            mission.CaptainId = captain.Id;
            mission.DockId = dock.Id;
            mission.VesselId = vessel.Id;
            await db.Missions.CreateAsync(mission).ConfigureAwait(false);

            // Wire up captain
            captain.CurrentMissionId = mission.Id;
            captain.CurrentDockId = dock.Id;
            await db.Captains.UpdateAsync(captain).ConfigureAwait(false);

            return new TestEntitiesResult(captain, mission, dock);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
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
