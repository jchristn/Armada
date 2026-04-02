namespace Armada.Test.Unit.Suites.Services
{
    using System.Globalization;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;
    using SyslogLogging;

    public class AdmiralServiceTests : TestSuite
    {
        public override string Name => "Admiral Service";

        private static async Task SetMissionLastUpdateUtcAsync(TestDatabase testDb, string missionId, DateTime lastUpdateUtc)
        {
            using (SqliteConnection conn = new SqliteConnection(testDb.ConnectionString))
            {
                await conn.OpenAsync().ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE missions SET last_update_utc = @last_update_utc WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", missionId);
                    cmd.Parameters.AddWithValue("@last_update_utc", lastUpdateUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
        }

        private static bool GetRetryDispatchNeeded(AdmiralService service)
        {
            System.Reflection.FieldInfo? field = typeof(AdmiralService).GetField(
                "_RetryDispatchNeeded",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Could not find _RetryDispatchNeeded field.");
            return (bool)field!.GetValue(service)!;
        }

        private static void SetRetryDispatchNeeded(AdmiralService service, bool value)
        {
            System.Reflection.FieldInfo? field = typeof(AdmiralService).GetField(
                "_RetryDispatchNeeded",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Could not find _RetryDispatchNeeded field.");
            field!.SetValue(service, value);
        }

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

        private AdmiralService CreateAdmiralService(LoggingModule logging, SqliteDatabaseDriver db, ArmadaSettings settings, StubGitService git)
        {
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            IMissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
            IVoyageService voyageService = new VoyageService(logging, db);
            return new AdmiralService(logging, db, settings, captainService, missionService, voyageService, dockService);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("Constructor NullLogging Throws", () =>
            {
                AssertThrows<ArgumentNullException>(() =>
                    new AdmiralService(null!, null!, null!, null!, null!, null!, null!));
            });

            await RunTest("Constructor NullDatabase Throws", () =>
            {
                AssertThrows<ArgumentNullException>(() =>
                    new AdmiralService(CreateLogging(), null!, null!, null!, null!, null!, null!));
            });

            await RunTest("GetStatusAsync EmptyDatabase ReturnsDefaults", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    ArmadaStatus status = await service.GetStatusAsync();

                    AssertEqual(0, status.TotalCaptains);
                    AssertEqual(0, status.IdleCaptains);
                    AssertEqual(0, status.WorkingCaptains);
                    AssertEqual(0, status.ActiveVoyages);
                    AssertEqual(0, status.Voyages.Count);
                }
            });

            await RunTest("GetStatusAsync WithCaptains CountsCorrectly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Captain c1 = new Captain("idle-1");
                    c1.State = CaptainStateEnum.Idle;
                    Captain c2 = new Captain("working-1");
                    c2.State = CaptainStateEnum.Working;
                    Captain c3 = new Captain("working-2");
                    c3.State = CaptainStateEnum.Working;

                    await db.Captains.CreateAsync(c1);
                    await db.Captains.CreateAsync(c2);
                    await db.Captains.CreateAsync(c3);

                    ArmadaStatus status = await service.GetStatusAsync();

                    AssertEqual(3, status.TotalCaptains);
                    AssertEqual(1, status.IdleCaptains);
                    AssertEqual(2, status.WorkingCaptains);
                }
            });

            await RunTest("GetStatusAsync WithMissions GroupsByStatus", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Mission m1 = new Mission("Pending");
                    m1.Status = MissionStatusEnum.Pending;
                    Mission m2 = new Mission("InProgress");
                    m2.Status = MissionStatusEnum.InProgress;
                    Mission m3 = new Mission("Complete");
                    m3.Status = MissionStatusEnum.Complete;

                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);
                    await db.Missions.CreateAsync(m3);

                    ArmadaStatus status = await service.GetStatusAsync();

                    AssertEqual(1, status.MissionsByStatus["Pending"]);
                    AssertEqual(1, status.MissionsByStatus["InProgress"]);
                    AssertEqual(1, status.MissionsByStatus["Complete"]);
                }
            });

            await RunTest("GetStatusAsync WithActiveVoyages IncludesProgress", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Voyage voyage = new Voyage("Test Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission m1 = new Mission("Done");
                    m1.VoyageId = voyage.Id;
                    m1.Status = MissionStatusEnum.Complete;
                    Mission m2 = new Mission("Working");
                    m2.VoyageId = voyage.Id;
                    m2.Status = MissionStatusEnum.InProgress;
                    await db.Missions.CreateAsync(m1);
                    await db.Missions.CreateAsync(m2);

                    ArmadaStatus status = await service.GetStatusAsync();

                    AssertEqual(1, status.ActiveVoyages);
                    AssertEqual(1, status.Voyages.Count);
                    AssertEqual(2, status.Voyages[0].TotalMissions);
                    AssertEqual(1, status.Voyages[0].CompletedMissions);
                    AssertEqual(1, status.Voyages[0].InProgressMissions);
                }
            });

            await RunTest("DispatchMissionAsync NullMission Throws", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<ArgumentNullException>(() => service.DispatchMissionAsync(null!));
                }
            });

            await RunTest("DispatchMissionAsync CreatesMissionInDb", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Mission mission = new Mission("Test Dispatch");
                    Mission result = await service.DispatchMissionAsync(mission);

                    AssertNotNull(result);
                    AssertEqual("Test Dispatch", result.Title);

                    Mission? fromDb = await db.Missions.ReadAsync(result.Id);
                    AssertNotNull(fromDb);
                }
            });

            await RunTest("DispatchVoyageAsync NullTitle Throws", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<ArgumentNullException>(() =>
                        service.DispatchVoyageAsync(null!, "desc", "vsl_id", new List<MissionDescription> { new MissionDescription("m1", "d1") }));
                }
            });

            await RunTest("DispatchVoyageAsync EmptyMissions Throws", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<ArgumentException>(() =>
                        service.DispatchVoyageAsync("Voyage", "desc", "vsl_id", new List<MissionDescription>()));
                }
            });

            await RunTest("DispatchVoyageAsync NonExistentVessel Throws", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<InvalidOperationException>(() =>
                        service.DispatchVoyageAsync("Voyage", "desc", "vsl_nonexistent",
                            new List<MissionDescription> { new MissionDescription("m1", "d1") }));
                }
            });

            await RunTest("DispatchVoyageAsync CreatesVoyageAndMissions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Fleet fleet = new Fleet("TestFleet");
                    await db.Fleets.CreateAsync(fleet);
                    Vessel vessel = new Vessel("TestVessel", "https://github.com/test/repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Voyage result = await service.DispatchVoyageAsync(
                        "My Voyage", "A test", vessel.Id,
                        new List<MissionDescription> { new MissionDescription("Mission 1", "Desc 1"), new MissionDescription("Mission 2", "Desc 2") });

                    AssertNotNull(result);
                    // Voyage stays Open when no captains are available to auto-assign missions
                    AssertEqual(VoyageStatusEnum.Open, result.Status);

                    List<Mission> missions = await db.Missions.EnumerateByVoyageAsync(result.Id);
                    AssertEqual(2, missions.Count);
                }
            });

            await RunTest("RecallCaptainAsync NullId Throws", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<ArgumentNullException>(() => service.RecallCaptainAsync(null!));
                }
            });

            await RunTest("RecallCaptainAsync NonExistentCaptain Throws", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<InvalidOperationException>(() => service.RecallCaptainAsync("cpt_nonexistent"));
                }
            });

            await RunTest("RecallCaptainAsync SetsCaptainToIdle", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Captain captain = new Captain("recall-target");
                    captain.State = CaptainStateEnum.Working;
                    await db.Captains.CreateAsync(captain);

                    await service.RecallCaptainAsync(captain.Id);

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, result!.State);
                    AssertNull(result.CurrentMissionId);
                    AssertNull(result.CurrentDockId);
                    AssertNull(result.ProcessId);
                }
            });

            await RunTest("RecallCaptainAsync FailsActiveMission", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Mission mission = new Mission("Active Mission");
                    mission.Status = MissionStatusEnum.InProgress;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("recall-active");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    await db.Captains.CreateAsync(captain);

                    await service.RecallCaptainAsync(captain.Id);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual(MissionStatusEnum.Failed, result!.Status);
                    AssertNotNull(result.CompletedUtc);
                }
            });

            await RunTest("RecallAllAsync RecallsAllWorkingCaptains", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Captain c1 = new Captain("worker-1");
                    c1.State = CaptainStateEnum.Working;
                    Captain c2 = new Captain("worker-2");
                    c2.State = CaptainStateEnum.Working;
                    Captain c3 = new Captain("idle-1");
                    c3.State = CaptainStateEnum.Idle;

                    await db.Captains.CreateAsync(c1);
                    await db.Captains.CreateAsync(c2);
                    await db.Captains.CreateAsync(c3);

                    await service.RecallAllAsync();

                    Captain? r1 = await db.Captains.ReadAsync(c1.Id);
                    Captain? r2 = await db.Captains.ReadAsync(c2.Id);
                    Captain? r3 = await db.Captains.ReadAsync(c3.Id);

                    AssertEqual(CaptainStateEnum.Idle, r1!.State);
                    AssertEqual(CaptainStateEnum.Idle, r2!.State);
                    AssertEqual(CaptainStateEnum.Idle, r3!.State);
                }
            });

            await RunTest("HandleProcessExitAsync stalls captain on non-retryable runtime failure", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 3;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Voyage voyage = new Voyage("Quota Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("Quota Judge");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 4242;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("quota-judge");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = 4242;
                    await db.Captains.CreateAsync(captain);

                    string missionLogDir = Path.Combine(settings.LogDirectory, "missions");
                    Directory.CreateDirectory(missionLogDir);
                    await File.WriteAllTextAsync(
                        Path.Combine(missionLogDir, mission.Id + ".log"),
                        "[stderr] You've hit your limit and must wait for reset.\n[2026-04-02 23:49:03] Agent exited with code 1").ConfigureAwait(false);

                    await service.HandleProcessExitAsync(4242, 1, captain.Id, mission.Id).ConfigureAwait(false);

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? updatedCaptain = await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    Voyage? updatedVoyage = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);

                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertNotNull(updatedVoyage, "Voyage should still exist");
                    AssertEqual(MissionStatusEnum.Failed, updatedMission!.Status, "Mission should fail immediately on a non-retryable runtime error");
                    AssertContains("hit your limit", updatedMission.FailureReason ?? String.Empty, "Mission should preserve the runtime failure reason");
                    AssertEqual(CaptainStateEnum.Stalled, updatedCaptain!.State, "Captain should be stalled when the runtime is unavailable");
                    AssertNull(updatedCaptain.CurrentMissionId, "Captain mission assignment should be cleared");
                    AssertEqual(VoyageStatusEnum.Cancelled, updatedVoyage!.Status, "Voyage should be halted after the failed mission");
                }
            });

            await RunTest("HealthCheckAsync NoCaptains DoesNotThrow", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await service.HealthCheckAsync();
                }
            });

            await RunTest("HealthCheckAsync WorkingCaptainNoProcessId NoMission ReleasesToIdle", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    // A Working captain with no process ID and no mission is orphaned
                    // and should be released to Idle by the health check.
                    Captain captain = new Captain("no-pid");
                    captain.State = CaptainStateEnum.Working;
                    captain.ProcessId = null;
                    await db.Captains.CreateAsync(captain);

                    await service.HealthCheckAsync();

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, result!.State);
                }
            });

            await RunTest("HealthCheckAsync DeadProcess RecoveryExhausted StallsCaptain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 0;
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Mission mission = new Mission("Test");
                    mission.Status = MissionStatusEnum.InProgress;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("dead-process");
                    captain.State = CaptainStateEnum.Working;
                    captain.ProcessId = 99999999;
                    captain.CurrentMissionId = mission.Id;
                    await db.Captains.CreateAsync(captain);

                    await service.HealthCheckAsync();

                    Captain? result = await db.Captains.ReadAsync(captain.Id);
                    AssertEqual(CaptainStateEnum.Idle, result!.State);
                }
            });

            await RunTest("HealthCheckAsync AssignedOrphanWithoutStartedProcess RevertsToPending", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Vessel vessel = new Vessel("orphan-vessel", "https://github.com/test/repo.git");
                    await db.Vessels.CreateAsync(vessel);

                    Captain originalCaptain = new Captain("original");
                    originalCaptain.State = CaptainStateEnum.Idle;
                    await db.Captains.CreateAsync(originalCaptain);

                    Captain movedCaptain = new Captain("moved");
                    movedCaptain.State = CaptainStateEnum.Working;
                    await db.Captains.CreateAsync(movedCaptain);

                    Mission mission = new Mission("Assigned orphan");
                    mission.VesselId = vessel.Id;
                    mission.CaptainId = originalCaptain.Id;
                    mission.Status = MissionStatusEnum.Assigned;
                    mission.BranchName = "armada/test/orphan";
                    mission = await db.Missions.CreateAsync(mission);
                    await SetMissionLastUpdateUtcAsync(testDb, mission.Id, DateTime.UtcNow.AddMinutes(-2));

                    originalCaptain.State = CaptainStateEnum.Working;
                    originalCaptain.CurrentMissionId = "different-mission";
                    await db.Captains.UpdateAsync(originalCaptain);

                    await service.HealthCheckAsync();

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertEqual(MissionStatusEnum.Pending, updatedMission!.Status, "Assigned orphan that never started should return to Pending");
                    AssertNull(updatedMission.CaptainId, "Pending orphan should be unassigned");
                    AssertNull(updatedMission.DockId, "Pending orphan should clear dock");
                }
            });

            await RunTest("HealthCheckAsync FreshAssignedMissionWithoutStartedProcess SkipsOrphanRecovery", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Vessel vessel = new Vessel("fresh-assigned-vessel", "https://github.com/test/repo.git");
                    await db.Vessels.CreateAsync(vessel);

                    Captain captain = new Captain("fresh-original");
                    captain.State = CaptainStateEnum.Idle;
                    await db.Captains.CreateAsync(captain);

                    Mission mission = new Mission("Fresh assigned orphan candidate");
                    mission.VesselId = vessel.Id;
                    mission.CaptainId = captain.Id;
                    mission.Status = MissionStatusEnum.Assigned;
                    mission.BranchName = "armada/test/fresh";
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    mission = await db.Missions.CreateAsync(mission);

                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = "different-mission";
                    await db.Captains.UpdateAsync(captain);

                    await service.HealthCheckAsync();

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id);
                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertEqual(MissionStatusEnum.Assigned, updatedMission!.Status, "Freshly assigned mission should remain Assigned during the grace window");
                    AssertEqual(captain.Id, updatedMission.CaptainId, "Freshly assigned mission should keep its captain assignment during the grace window");
                }
            });

            await RunTest("HealthCheckAsync FreshWorkProducedMission SkipsCaptainRelease", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Mission mission = new Mission("Fresh work produced");
                    mission.Status = MissionStatusEnum.WorkProduced;
                    mission.LastUpdateUtc = DateTime.UtcNow;
                    mission = await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("fresh-work-produced");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    await db.Captains.CreateAsync(captain);

                    await service.HealthCheckAsync();

                    Captain? updatedCaptain = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertEqual(CaptainStateEnum.Working, updatedCaptain!.State, "Freshly WorkProduced mission should not release the captain during handoff grace");
                    AssertEqual(mission.Id, updatedCaptain.CurrentMissionId, "Freshly WorkProduced mission should keep the current mission assignment during handoff grace");
                }
            });

            await RunTest("HealthCheckAsync StaleWorkProducedMission ReleasesCaptain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Mission mission = new Mission("Stale work produced");
                    mission.Status = MissionStatusEnum.WorkProduced;
                    mission = await db.Missions.CreateAsync(mission);
                    await SetMissionLastUpdateUtcAsync(testDb, mission.Id, DateTime.UtcNow.AddMinutes(-5));

                    Captain captain = new Captain("stale-work-produced");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    await db.Captains.CreateAsync(captain);

                    await service.HealthCheckAsync();

                    Captain? updatedCaptain = await db.Captains.ReadAsync(captain.Id);
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Stale WorkProduced mission should release the captain");
                    AssertNull(updatedCaptain.CurrentMissionId, "Stale WorkProduced mission should clear the current mission assignment when released");
                }
            });

            await RunTest("HealthCheckAsync ChecksVoyageCompletions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Voyage voyage = new Voyage("Test Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission m1 = new Mission("Done 1");
                    m1.VoyageId = voyage.Id;
                    m1.Status = MissionStatusEnum.Complete;
                    await db.Missions.CreateAsync(m1);

                    Mission m2 = new Mission("Done 2");
                    m2.VoyageId = voyage.Id;
                    m2.Status = MissionStatusEnum.Complete;
                    await db.Missions.CreateAsync(m2);

                    await service.HealthCheckAsync();

                    Voyage? result = await db.Voyages.ReadAsync(voyage.Id);
                    AssertEqual(VoyageStatusEnum.Complete, result!.Status);
                    AssertNotNull(result.CompletedUtc);
                }
            });

            await RunTest("HealthCheckAsync VoyageNotComplete WhenMissionsStillActive", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Voyage voyage = new Voyage("Active Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission m1 = new Mission("Done");
                    m1.VoyageId = voyage.Id;
                    m1.Status = MissionStatusEnum.Complete;
                    await db.Missions.CreateAsync(m1);

                    Mission m2 = new Mission("Still Working");
                    m2.VoyageId = voyage.Id;
                    m2.Status = MissionStatusEnum.InProgress;
                    await db.Missions.CreateAsync(m2);

                    await service.HealthCheckAsync();

                    Voyage? result = await db.Voyages.ReadAsync(voyage.Id);
                    AssertEqual(VoyageStatusEnum.InProgress, result!.Status);
                }
            });

            await RunTest("HealthCheckAsync VoyageFailsWithMixOfCompleteFailedCancelled", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Voyage voyage = new Voyage("Mixed Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission m1 = new Mission("Complete");
                    m1.VoyageId = voyage.Id;
                    m1.Status = MissionStatusEnum.Complete;
                    await db.Missions.CreateAsync(m1);

                    Mission m2 = new Mission("Failed");
                    m2.VoyageId = voyage.Id;
                    m2.Status = MissionStatusEnum.Failed;
                    await db.Missions.CreateAsync(m2);

                    Mission m3 = new Mission("Cancelled");
                    m3.VoyageId = voyage.Id;
                    m3.Status = MissionStatusEnum.Cancelled;
                    await db.Missions.CreateAsync(m3);

                    await service.HealthCheckAsync();

                    Voyage? result = await db.Voyages.ReadAsync(voyage.Id);
                    AssertEqual(VoyageStatusEnum.Failed, result!.Status);
                }
            });

            await RunTest("HealthCheckAsync ClearsRetryFlagWhenNoPendingMissionsRemain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    SetRetryDispatchNeeded(service, true);

                    await service.HealthCheckAsync();

                    AssertFalse(GetRetryDispatchNeeded(service));
                }
            });

            await RunTest("HealthCheckAsync KeepsRetryFlagWhenPendingMissionHasNoCapacity", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    SqliteDatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Fleet fleet = new Fleet("Retry Fleet");
                    await db.Fleets.CreateAsync(fleet);

                    Vessel vessel = new Vessel("Retry Vessel", "https://github.com/test/repo");
                    vessel.FleetId = fleet.Id;
                    await db.Vessels.CreateAsync(vessel);

                    Mission mission = new Mission("Pending Retry");
                    mission.VesselId = vessel.Id;
                    mission.Status = MissionStatusEnum.Pending;
                    await db.Missions.CreateAsync(mission);

                    SetRetryDispatchNeeded(service, true);

                    await service.HealthCheckAsync();

                    AssertTrue(GetRetryDispatchNeeded(service));
                }
            });
        }
    }
}
