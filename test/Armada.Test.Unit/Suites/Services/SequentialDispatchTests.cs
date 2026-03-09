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
    /// Tests for sequential mission dispatch to the same captain.
    /// Validates the scenario where more missions than captains are dispatched
    /// and missions must be processed sequentially via HandleCompletionAsync re-dispatch.
    /// </summary>
    public class SequentialDispatchTests : TestSuite
    {
        /// <summary>
        /// Suite name.
        /// </summary>
        public override string Name => "Sequential Dispatch";

        #region Private-Methods

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

        /// <summary>
        /// Create a MissionService with a CaptainService that has a stub OnLaunchAgent.
        /// Returns the mission service, captain service, and dock service.
        /// </summary>
        private ServiceSet CreateServices(LoggingModule logging, SqliteDatabaseDriver db, ArmadaSettings settings, StubGitService git)
        {
            IDockService dockService = new DockService(logging, db, settings, git);
            CaptainService captainService = new CaptainService(logging, db, settings, git);

            int nextPid = 1000;
            captainService.OnLaunchAgent = (captain, mission, dock) =>
            {
                return Task.FromResult(nextPid++);
            };

            MissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
            return new ServiceSet(missionService, captainService, dockService);
        }

        /// <summary>
        /// Create a fleet, vessel, voyage, captains, and pending missions.
        /// </summary>
        private async Task<TestFixture> SetupFixtureAsync(
            SqliteDatabaseDriver db,
            int captainCount,
            int missionCount,
            int maxParallelism = 1)
        {
            Fleet fleet = new Fleet("TestFleet");
            await db.Fleets.CreateAsync(fleet);

            Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
            vessel.FleetId = fleet.Id;
            await db.Vessels.CreateAsync(vessel);

            Voyage voyage = new Voyage("TestVoyage");
            voyage.Status = VoyageStatusEnum.InProgress;
            await db.Voyages.CreateAsync(voyage);

            List<Captain> captains = new List<Captain>();
            for (int i = 0; i < captainCount; i++)
            {
                Captain captain = new Captain("captain-" + (i + 1));
                captain.State = CaptainStateEnum.Idle;
                captain.MaxParallelism = maxParallelism;
                await db.Captains.CreateAsync(captain);
                captains.Add(captain);
            }

            List<Mission> missions = new List<Mission>();
            for (int i = 0; i < missionCount; i++)
            {
                Mission mission = new Mission("Mission " + (i + 1));
                mission.Status = MissionStatusEnum.Pending;
                mission.VesselId = vessel.Id;
                mission.VoyageId = voyage.Id;
                mission.Priority = 100 + i;
                await db.Missions.CreateAsync(mission);
                missions.Add(mission);
            }

            return new TestFixture(fleet, vessel, voyage, captains, missions);
        }

        #endregion

        /// <summary>
        /// Run all sequential dispatch tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("OneCaptain_ThreeMissions_SequentialDispatch", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 3);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;
                    List<Mission> missions = fixture.Missions;

                    // Step 1: Assign mission 1 — captain is idle, should get assigned
                    await missionService.TryAssignAsync(missions[0], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(missions[0].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m1!.Status, "Mission 1 should be InProgress");
                    AssertEqual(captain.Id, m1.CaptainId, "Mission 1 should be assigned to the captain");

                    Captain? captainAfterM1 = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, captainAfterM1!.State, "Captain should be Working");
                    AssertEqual(m1.Id, captainAfterM1.CurrentMissionId, "Captain should track mission 1");

                    // Step 2: Try to assign mission 2 — captain is working with MaxParallelism=1, should NOT be assigned
                    await missionService.TryAssignAsync(missions[1], vessel);

                    Mission? m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.Pending, m2!.Status, "Mission 2 should still be Pending");

                    // Step 3: Try to assign mission 3 — same, should NOT be assigned
                    await missionService.TryAssignAsync(missions[2], vessel);

                    Mission? m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.Pending, m3!.Status, "Mission 3 should still be Pending");

                    // Step 4: Complete mission 1 — should release captain and pick up mission 2
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, captainForCompletion!.CurrentMissionId!);

                    m1 = await db.Missions.ReadAsync(missions[0].Id);
                    AssertEqual(MissionStatusEnum.Complete, m1!.Status, "Mission 1 should be Complete");
                    AssertNotNull(m1.CompletedUtc, "Mission 1 should have CompletedUtc set");

                    m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m2!.Status, "Mission 2 should be InProgress after mission 1 completion");
                    AssertEqual(captain.Id, m2.CaptainId, "Mission 2 should be assigned to the same captain");

                    Captain? captainAfterM2Assign = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, captainAfterM2Assign!.State, "Captain should be Working with mission 2");

                    // Step 5: Complete mission 2 — should pick up mission 3
                    captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, m2.Id);

                    m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.Complete, m2!.Status, "Mission 2 should be Complete");

                    m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m3!.Status, "Mission 3 should be InProgress after mission 2 completion");
                    AssertEqual(captain.Id, m3.CaptainId, "Mission 3 should be assigned to the same captain");

                    // Step 6: Complete mission 3 — captain should be released to Idle
                    captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, m3.Id);

                    m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.Complete, m3!.Status, "Mission 3 should be Complete");

                    Captain? captainFinal = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, captainFinal!.State, "Captain should be Idle after all missions complete");
                    AssertNull(captainFinal.CurrentMissionId, "Captain CurrentMissionId should be null");
                    AssertNull(captainFinal.CurrentDockId, "Captain CurrentDockId should be null");

                    // Verify all 3 missions reached Complete
                    List<Mission> allMissions = await db.Missions.EnumerateByVoyageAsync(fixture.Voyage.Id);
                    int completeCount = allMissions.Count(m => m.Status == MissionStatusEnum.Complete);
                    AssertEqual(3, completeCount, "All 3 missions should be Complete");
                }
            });

            await RunTest("TwoCaptains_FiveMissions_ParallelThenSequential", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 2, missionCount: 5);
                    List<Captain> captains = fixture.Captains;
                    Vessel vessel = fixture.Vessel;
                    List<Mission> missions = fixture.Missions;

                    // Assign first 2 missions — each captain should get one
                    await missionService.TryAssignAsync(missions[0], vessel);
                    await missionService.TryAssignAsync(missions[1], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(missions[0].Id);
                    Mission? m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m1!.Status, "Mission 1 should be InProgress");
                    AssertEqual(MissionStatusEnum.InProgress, m2!.Status, "Mission 2 should be InProgress");

                    string? m1CaptainId = m1.CaptainId;
                    string? m2CaptainId = m2.CaptainId;
                    AssertNotNull(m1CaptainId, "Mission 1 should have a captain");
                    AssertNotNull(m2CaptainId, "Mission 2 should have a captain");
                    AssertNotEqual(m1CaptainId!, m2CaptainId!, "Missions should be on different captains");

                    // Missions 3, 4, 5 should remain Pending (no capacity)
                    await missionService.TryAssignAsync(missions[2], vessel);
                    await missionService.TryAssignAsync(missions[3], vessel);
                    await missionService.TryAssignAsync(missions[4], vessel);

                    Mission? m3 = await db.Missions.ReadAsync(missions[2].Id);
                    Mission? m4 = await db.Missions.ReadAsync(missions[3].Id);
                    Mission? m5 = await db.Missions.ReadAsync(missions[4].Id);
                    AssertEqual(MissionStatusEnum.Pending, m3!.Status, "Mission 3 should be Pending");
                    AssertEqual(MissionStatusEnum.Pending, m4!.Status, "Mission 4 should be Pending");
                    AssertEqual(MissionStatusEnum.Pending, m5!.Status, "Mission 5 should be Pending");

                    // Complete mission 1 — the captain should pick up mission 3
                    Captain? captain1 = await db.Captains.ReadAsync(m1CaptainId!);
                    await missionService.HandleCompletionAsync(captain1!, m1.Id);

                    m1 = await db.Missions.ReadAsync(missions[0].Id);
                    AssertEqual(MissionStatusEnum.Complete, m1!.Status, "Mission 1 should be Complete");

                    m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m3!.Status, "Mission 3 should be InProgress after mission 1 completion");

                    // Complete mission 2 — the other captain should pick up mission 4
                    Captain? captain2 = await db.Captains.ReadAsync(m2CaptainId!);
                    await missionService.HandleCompletionAsync(captain2!, m2.Id);

                    m2 = await db.Missions.ReadAsync(missions[1].Id);
                    AssertEqual(MissionStatusEnum.Complete, m2!.Status, "Mission 2 should be Complete");

                    m4 = await db.Missions.ReadAsync(missions[3].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m4!.Status, "Mission 4 should be InProgress after mission 2 completion");

                    // Complete mission 3 — should pick up mission 5
                    captain1 = await db.Captains.ReadAsync(m3.CaptainId!);
                    await missionService.HandleCompletionAsync(captain1!, m3.Id);

                    m3 = await db.Missions.ReadAsync(missions[2].Id);
                    AssertEqual(MissionStatusEnum.Complete, m3!.Status, "Mission 3 should be Complete");

                    m5 = await db.Missions.ReadAsync(missions[4].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m5!.Status, "Mission 5 should be InProgress after mission 3 completion");

                    // Complete mission 4 — no more pending, captain should go Idle
                    captain2 = await db.Captains.ReadAsync(m4.CaptainId!);
                    await missionService.HandleCompletionAsync(captain2!, m4.Id);

                    m4 = await db.Missions.ReadAsync(missions[3].Id);
                    AssertEqual(MissionStatusEnum.Complete, m4!.Status, "Mission 4 should be Complete");

                    captain2 = await db.Captains.ReadAsync(captain2!.Id);
                    AssertEqual(CaptainStateEnum.Idle, captain2!.State, "Captain 2 should be Idle");

                    // Complete mission 5 — last captain should go Idle
                    captain1 = await db.Captains.ReadAsync(m5.CaptainId!);
                    await missionService.HandleCompletionAsync(captain1!, m5.Id);

                    m5 = await db.Missions.ReadAsync(missions[4].Id);
                    AssertEqual(MissionStatusEnum.Complete, m5!.Status, "Mission 5 should be Complete");

                    // Verify all 5 missions complete
                    List<Mission> allMissions = await db.Missions.EnumerateByVoyageAsync(fixture.Voyage.Id);
                    int completeCount = allMissions.Count(m => m.Status == MissionStatusEnum.Complete);
                    AssertEqual(5, completeCount, "All 5 missions should be Complete");

                    // Verify both captains are Idle
                    foreach (Captain c in captains)
                    {
                        Captain? finalCaptain = await db.Captains.ReadAsync(c.Id);
                        AssertEqual(CaptainStateEnum.Idle, finalCaptain!.State, "Captain " + c.Name + " should be Idle");
                    }
                }
            });

            await RunTest("DockCleanup_WorktreePathReusable_BetweenAssignments", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 2);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign mission 1
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertNotNull(m1!.DockId, "Mission 1 should have a dock");

                    Dock? dock1 = await db.Docks.ReadAsync(m1.DockId!);
                    AssertNotNull(dock1, "Dock for mission 1 should exist");
                    string? worktreePath1 = dock1!.WorktreePath;
                    AssertNotNull(worktreePath1, "Dock should have a worktree path");

                    // Complete mission 1 — should auto-assign mission 2
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, m1.Id);

                    // Mission 2 should now be assigned with its own dock
                    Mission? m2 = await db.Missions.ReadAsync(fixture.Missions[1].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m2!.Status, "Mission 2 should be InProgress");
                    AssertNotNull(m2.DockId, "Mission 2 should have a dock");

                    Dock? dock2 = await db.Docks.ReadAsync(m2.DockId!);
                    AssertNotNull(dock2, "Dock for mission 2 should exist");

                    // The worktree path should be the same directory (same vessel + same captain name)
                    // because DockService uses {DocksDirectory}/{vesselName}/{captainName}
                    AssertEqual(worktreePath1!, dock2!.WorktreePath!, "Worktree path should be reused for same captain on same vessel");

                    // Verify git worktree creation was called for both missions
                    AssertTrue(git.WorktreeCalls.Count >= 2, "Should have at least 2 worktree create calls");
                }
            });

            await RunTest("CompletionHandler_Throws_CaptainStillReleased_RedispatchAttempted", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    // Set OnMissionComplete handler that throws
                    missionService.OnMissionComplete = (mission, dock) =>
                    {
                        throw new InvalidOperationException("Simulated push failure");
                    };

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 2);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign and start mission 1
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m1!.Status, "Mission 1 should be InProgress");

                    // Complete mission 1 — OnMissionComplete will throw (fire-and-forget),
                    // but captain should still be released and mission 2 should be picked up
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, m1.Id);

                    // Verify mission 1 is Complete despite handler failure
                    m1 = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertEqual(MissionStatusEnum.Complete, m1!.Status, "Mission 1 should be Complete even though handler threw");

                    // Verify mission 2 was picked up (re-dispatch was attempted)
                    Mission? m2 = await db.Missions.ReadAsync(fixture.Missions[1].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m2!.Status, "Mission 2 should be InProgress — handler failure should not block re-dispatch");
                    AssertEqual(captain.Id, m2.CaptainId, "Mission 2 should be assigned to the same captain");
                }
            });

            await RunTest("HandleCompletionAsync_EmitsMissionCompletedEvent", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 1);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign and start mission
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    // Complete mission
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, fixture.Missions[0].Id);

                    // Verify mission.completed event was emitted
                    EnumerationResult<ArmadaEvent> eventResult = await db.Events.EnumerateAsync(new EnumerationQuery());
                    List<ArmadaEvent> completionEvents = eventResult.Objects.Where(e => e.EventType == "mission.completed").ToList();
                    AssertTrue(completionEvents.Count >= 1, "Should have at least 1 mission.completed event");
                    AssertEqual(fixture.Missions[0].Id, completionEvents[0].MissionId, "Event should reference the completed mission");
                    AssertEqual(captain.Id, completionEvents[0].CaptainId, "Event should reference the captain");
                }
            });

            await RunTest("NoPendingMissions_CaptainReleasedToIdle", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 1);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign the single mission
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    // Complete it — no more pending missions, captain should become Idle
                    Captain? captainForCompletion = await db.Captains.ReadAsync(captain.Id);
                    await missionService.HandleCompletionAsync(captainForCompletion!, fixture.Missions[0].Id);

                    Captain? finalCaptain = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, finalCaptain!.State, "Captain should be Idle when no pending missions remain");
                    AssertNull(finalCaptain.CurrentMissionId, "CurrentMissionId should be null");
                    AssertNull(finalCaptain.CurrentDockId, "CurrentDockId should be null");
                    AssertNull(finalCaptain.ProcessId, "ProcessId should be null");
                }
            });

            await RunTest("MaxParallelism1_BlocksConcurrentAssignment", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    LoggingModule logging = CreateLogging();
                    ServiceSet services = CreateServices(logging, db, settings, git);
                    MissionService missionService = services.Missions;

                    TestFixture fixture = await SetupFixtureAsync(db, captainCount: 1, missionCount: 2, maxParallelism: 1);
                    Captain captain = fixture.Captains[0];
                    Vessel vessel = fixture.Vessel;

                    // Assign mission 1 — should succeed
                    await missionService.TryAssignAsync(fixture.Missions[0], vessel);

                    Mission? m1 = await db.Missions.ReadAsync(fixture.Missions[0].Id);
                    AssertEqual(MissionStatusEnum.InProgress, m1!.Status, "Mission 1 should be InProgress");

                    // Try to assign mission 2 — should fail due to MaxParallelism=1
                    await missionService.TryAssignAsync(fixture.Missions[1], vessel);

                    Mission? m2 = await db.Missions.ReadAsync(fixture.Missions[1].Id);
                    AssertEqual(MissionStatusEnum.Pending, m2!.Status, "Mission 2 should remain Pending with MaxParallelism=1");

                    // Captain should still be working on mission 1 only
                    Captain? captainAfter = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Working, captainAfter!.State, "Captain should still be Working");
                    AssertEqual(m1.Id, captainAfter.CurrentMissionId, "Captain should still track mission 1");
                }
            });
        }

        #region Helper-Classes

        /// <summary>
        /// Container for test service instances.
        /// </summary>
        private class ServiceSet
        {
            /// <summary>
            /// Mission service.
            /// </summary>
            public MissionService Missions { get; }

            /// <summary>
            /// Captain service.
            /// </summary>
            public CaptainService Captains { get; }

            /// <summary>
            /// Dock service.
            /// </summary>
            public IDockService Docks { get; }

            /// <summary>
            /// Instantiate.
            /// </summary>
            public ServiceSet(MissionService missions, CaptainService captains, IDockService docks)
            {
                Missions = missions;
                Captains = captains;
                Docks = docks;
            }
        }

        /// <summary>
        /// Test fixture containing pre-created entities.
        /// </summary>
        private class TestFixture
        {
            /// <summary>
            /// Test fleet.
            /// </summary>
            public Fleet Fleet { get; }

            /// <summary>
            /// Test vessel.
            /// </summary>
            public Vessel Vessel { get; }

            /// <summary>
            /// Test voyage.
            /// </summary>
            public Voyage Voyage { get; }

            /// <summary>
            /// Test captains.
            /// </summary>
            public List<Captain> Captains { get; }

            /// <summary>
            /// Test missions.
            /// </summary>
            public List<Mission> Missions { get; }

            /// <summary>
            /// Instantiate.
            /// </summary>
            public TestFixture(Fleet fleet, Vessel vessel, Voyage voyage, List<Captain> captains, List<Mission> missions)
            {
                Fleet = fleet;
                Vessel = vessel;
                Voyage = voyage;
                Captains = captains;
                Missions = missions;
            }
        }

        #endregion
    }
}
