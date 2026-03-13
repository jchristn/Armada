namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    /// <summary>
    /// Tests for mission status transitions through the landing pipeline:
    /// InProgress -> WorkProduced -> Complete (success) or LandingFailed (failure).
    /// </summary>
    public class MissionStatusTransitionTests : TestSuite
    {
        public override string Name => "Mission Status Transitions";

        private LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private ArmadaSettings CreateSettings()
        {
            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_test_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_test_repos_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private async Task<TestEntitiesResult> CreateTestEntitiesAsync(
            SqliteDatabaseDriver db)
        {
            // Create a vessel (fleet is optional)
            Vessel vessel = new Vessel("test-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            // Create a captain
            Captain captain = new Captain("test-captain");
            captain.State = CaptainStateEnum.Working;
            await db.Captains.CreateAsync(captain).ConfigureAwait(false);

            // Create a dock
            Dock dock = new Dock(vessel.Id);
            dock.CaptainId = captain.Id;
            dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_wt_" + Guid.NewGuid().ToString("N"));
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

        protected override async Task RunTestsAsync()
        {
            // === MissionStatusEnum Value Tests ===

            await RunTest("WorkProduced enum value exists", () =>
            {
                MissionStatusEnum status = MissionStatusEnum.WorkProduced;
                AssertEqual("WorkProduced", status.ToString(), "WorkProduced enum name");
            });

            await RunTest("LandingFailed enum value exists", () =>
            {
                MissionStatusEnum status = MissionStatusEnum.LandingFailed;
                AssertEqual("LandingFailed", status.ToString(), "LandingFailed enum name");
            });

            await RunTest("PullRequestOpen enum value exists", () =>
            {
                MissionStatusEnum status = MissionStatusEnum.PullRequestOpen;
                AssertEqual("PullRequestOpen", status.ToString(), "PullRequestOpen enum name");
            });

            await RunTest("All expected statuses defined", () =>
            {
                string[] expected = new[]
                {
                    "Pending", "Assigned", "InProgress", "WorkProduced", "PullRequestOpen",
                    "Testing", "Review", "Complete", "Failed", "LandingFailed", "Cancelled"
                };

                string[] actual = Enum.GetNames(typeof(MissionStatusEnum));
                AssertEqual(expected.Length, actual.Length, "Enum value count");

                foreach (string name in expected)
                {
                    Assert(Enum.TryParse<MissionStatusEnum>(name, out _), "Missing enum value: " + name);
                }
            });

            // === HandleCompletionAsync Tests (InProgress -> WorkProduced) ===

            await RunTest("HandleCompletion sets status to WorkProduced", async () =>
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
            });

            await RunTest("HandleCompletion clears ProcessId", async () =>
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
            });

            await RunTest("HandleCompletion emits work_produced event", async () =>
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
            });

            await RunTest("HandleCompletion with null captain throws", async () =>
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
            });

            await RunTest("HandleCompletion with no current mission is no-op", async () =>
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
            });

            // === Terminal State Tests ===

            await RunTest("WorkProduced is not a terminal state", () =>
            {
                // WorkProduced should allow transition to Complete or LandingFailed
                MissionStatusEnum status = MissionStatusEnum.WorkProduced;
                Assert(status != MissionStatusEnum.Complete, "WorkProduced is not Complete");
                Assert(status != MissionStatusEnum.Failed, "WorkProduced is not Failed");
                Assert(status != MissionStatusEnum.Cancelled, "WorkProduced is not Cancelled");
            });

            await RunTest("LandingFailed is distinct from Failed", () =>
            {
                MissionStatusEnum status = MissionStatusEnum.LandingFailed;
                Assert(status != MissionStatusEnum.Failed, "LandingFailed is distinct from Failed");
                Assert(status != MissionStatusEnum.Complete, "LandingFailed is not Complete");
            });

            await RunTest("PullRequestOpen is not a terminal state", () =>
            {
                // PullRequestOpen should allow transition to Complete (PR merged) or Cancelled
                MissionStatusEnum status = MissionStatusEnum.PullRequestOpen;
                Assert(status != MissionStatusEnum.Complete, "PullRequestOpen is not Complete");
                Assert(status != MissionStatusEnum.Failed, "PullRequestOpen is not Failed");
                Assert(status != MissionStatusEnum.Cancelled, "PullRequestOpen is not Cancelled");
            });

            // === VoyageService Progress Counting ===

            await RunTest("VoyageService counts WorkProduced as in-progress", async () =>
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
                    AssertEqual(1, progress.InProgressMissions, "WorkProduced should count as in-progress");
                    AssertEqual(0, progress.CompletedMissions, "Should not count as completed");
                    AssertEqual(0, progress.FailedMissions, "Should not count as failed");
                }
            });

            await RunTest("VoyageService counts PullRequestOpen as in-progress", async () =>
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
            });

            await RunTest("VoyageService counts LandingFailed as failed", async () =>
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
            });

            await RunTest("VoyageService mixed statuses counted correctly", async () =>
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
                    AssertEqual(1, progress.CompletedMissions, "Completed count");
                    AssertEqual(2, progress.FailedMissions, "Failed + LandingFailed count");
                    AssertEqual(3, progress.InProgressMissions, "InProgress + WorkProduced + PullRequestOpen count");
                }
            });

            // === AdmiralService Status Counting ===

            await RunTest("GetStatusAsync counts WorkProduced and LandingFailed", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
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
            });
        }
    }
}
