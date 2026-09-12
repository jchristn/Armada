namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
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
    /// Descriptors for <see cref="AdmiralService"/>: constructor guards, fleet status aggregation,
    /// mission and voyage dispatch, captain recall, runtime process-exit handling, and health-check
    /// reconciliation (orphan recovery, voyage completion, and retry-flag maintenance). Each case
    /// builds a fresh SQLite store and a stub git service so the coordinator can be exercised without
    /// a real repository or agent process.
    /// </summary>
    public sealed class AdmiralServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AdmiralService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Admiral Service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("constructor_null_logging_throws", "Constructor NullLogging Throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() =>
                    new AdmiralService(null!, null!, null!, null!, null!, null!, null!));
            }));

            cases.Add(Case("constructor_null_database_throws", "Constructor NullDatabase Throws", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() =>
                    new AdmiralService(CreateLogging(), null!, null!, null!, null!, null!, null!));
            }));

            cases.Add(CaseAsync("get_status_async_empty_database_returns_defaults", "GetStatusAsync EmptyDatabase ReturnsDefaults", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("get_status_async_with_captains_counts_correctly", "GetStatusAsync WithCaptains CountsCorrectly", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("get_status_async_with_missions_groups_by_status", "GetStatusAsync WithMissions GroupsByStatus", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("get_status_async_with_active_voyages_includes_progress", "GetStatusAsync WithActiveVoyages IncludesProgress", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("dispatch_mission_async_null_mission_throws", "DispatchMissionAsync NullMission Throws", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<ArgumentNullException>(() => service.DispatchMissionAsync(null!));
                }
            }));

            cases.Add(CaseAsync("dispatch_mission_async_creates_mission_in_db", "DispatchMissionAsync CreatesMissionInDb", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Mission mission = new Mission("Test Dispatch");
                    Mission result = await service.DispatchMissionAsync(mission);

                    AssertNotNull(result);
                    AssertEqual("Test Dispatch", result.Title);

                    Mission? fromDb = await db.Missions.ReadAsync(result.Id);
                    AssertNotNull(fromDb);
                }
            }));

            cases.Add(CaseAsync("dispatch_voyage_async_null_title_throws", "DispatchVoyageAsync NullTitle Throws", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<ArgumentNullException>(() =>
                        service.DispatchVoyageAsync(null!, "desc", "vsl_id", new List<MissionDescription> { new MissionDescription("m1", "d1") }));
                }
            }));

            cases.Add(CaseAsync("dispatch_voyage_async_empty_missions_throws", "DispatchVoyageAsync EmptyMissions Throws", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<ArgumentException>(() =>
                        service.DispatchVoyageAsync("Voyage", "desc", "vsl_id", new List<MissionDescription>()));
                }
            }));

            cases.Add(CaseAsync("dispatch_voyage_async_non_existent_vessel_throws", "DispatchVoyageAsync NonExistentVessel Throws", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<InvalidOperationException>(() =>
                        service.DispatchVoyageAsync("Voyage", "desc", "vsl_nonexistent",
                            new List<MissionDescription> { new MissionDescription("m1", "d1") }));
                }
            }));

            cases.Add(CaseAsync("dispatch_voyage_async_creates_voyage_and_missions", "DispatchVoyageAsync CreatesVoyageAndMissions", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("recall_captain_async_null_id_throws", "RecallCaptainAsync NullId Throws", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<ArgumentNullException>(() => service.RecallCaptainAsync(null!));
                }
            }));

            cases.Add(CaseAsync("recall_captain_async_non_existent_captain_throws", "RecallCaptainAsync NonExistentCaptain Throws", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await AssertThrowsAsync<InvalidOperationException>(() => service.RecallCaptainAsync("cpt_nonexistent"));
                }
            }));

            cases.Add(CaseAsync("recall_captain_async_sets_captain_to_idle", "RecallCaptainAsync SetsCaptainToIdle", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("recall_captain_async_fails_active_mission", "RecallCaptainAsync FailsActiveMission", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("recall_all_async_recalls_all_working_captains", "RecallAllAsync RecallsAllWorkingCaptains", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("handle_process_exit_async_stalls_captain_on_non_retryable_runtime_failure", "HandleProcessExitAsync stalls captain on non-retryable runtime failure", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("handle_process_exit_async_redispatches_on_interruption", "HandleProcessExitAsync re-dispatches (not fails) on an interruption (exit -1)", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxNoOpRedispatchAttempts = 1;
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Voyage voyage = new Voyage("Interrupted Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("Interrupted Judge");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 5150;
                    mission.RedispatchAttempts = 0;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("interrupted-judge");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = 5150;
                    await db.Captains.CreateAsync(captain);

                    // Exit code -1 == interruption (stop/restart cancellation), not a genuine failure.
                    await service.HandleProcessExitAsync(5150, -1, captain.Id, mission.Id).ConfigureAwait(false);

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Voyage? updatedVoyage = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertEqual(MissionStatusEnum.Pending, updatedMission!.Status, "An interrupted mission should be re-queued as Pending, not Failed");
                    AssertEqual(1, updatedMission.RedispatchAttempts, "Redispatch attempts should be incremented");
                    AssertNull(updatedMission.FailureReason, "Failure reason should be cleared on re-dispatch");
                    AssertNull(updatedMission.CaptainId, "Captain assignment should be cleared on re-dispatch");
                    AssertNotNull(updatedVoyage, "Voyage should still exist");
                    AssertEqual(VoyageStatusEnum.InProgress, updatedVoyage!.Status, "The voyage should keep running (not be halted) on an interruption");
                }
            }));

            cases.Add(CaseAsync("handle_process_exit_async_fails_when_redispatch_budget_exhausted", "HandleProcessExitAsync fails an interruption terminally once the re-dispatch budget is exhausted", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxNoOpRedispatchAttempts = 1;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Voyage voyage = new Voyage("Exhausted Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("Exhausted Judge");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 5151;
                    mission.RedispatchAttempts = 1; // already at the max, so a further interruption must fail terminally
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("exhausted-judge");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = 5151;
                    await db.Captains.CreateAsync(captain);

                    await service.HandleProcessExitAsync(5151, -1, captain.Id, mission.Id).ConfigureAwait(false);

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Voyage? updatedVoyage = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertEqual(MissionStatusEnum.Failed, updatedMission!.Status, "Once the redispatch budget is exhausted, an interruption should fail terminally");
                    AssertNotNull(updatedVoyage, "Voyage should still exist");
                    AssertEqual(VoyageStatusEnum.Cancelled, updatedVoyage!.Status, "The voyage should halt once the interrupted mission can no longer be re-dispatched");
                }
            }));

            cases.Add(CaseAsync("health_check_async_no_captains_does_not_throw", "HealthCheckAsync NoCaptains DoesNotThrow", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    await service.HealthCheckAsync();
                }
            }));

            cases.Add(CaseAsync("health_check_async_working_captain_no_process_id_no_mission_releases_to_idle", "HealthCheckAsync WorkingCaptainNoProcessId NoMission ReleasesToIdle", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_dead_process_recovery_exhausted_stalls_captain", "HealthCheckAsync DeadProcess RecoveryExhausted StallsCaptain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_assigned_orphan_without_started_process_reverts_to_pending", "HealthCheckAsync AssignedOrphanWithoutStartedProcess RevertsToPending", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_fresh_assigned_mission_without_started_process_skips_orphan_recovery", "HealthCheckAsync FreshAssignedMissionWithoutStartedProcess SkipsOrphanRecovery", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_fresh_work_produced_mission_skips_captain_release", "HealthCheckAsync FreshWorkProducedMission SkipsCaptainRelease", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_stale_work_produced_mission_releases_captain", "HealthCheckAsync StaleWorkProducedMission ReleasesCaptain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_checks_voyage_completions", "HealthCheckAsync ChecksVoyageCompletions", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_voyage_not_complete_when_missions_still_active", "HealthCheckAsync VoyageNotComplete WhenMissionsStillActive", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_voyage_fails_with_mix_of_complete_failed_cancelled", "HealthCheckAsync VoyageFailsWithMixOfCompleteFailedCancelled", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("health_check_async_clears_retry_flag_when_no_pending_missions_remain", "HealthCheckAsync ClearsRetryFlagWhenNoPendingMissionsRemain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), git);

                    SetRetryDispatchNeeded(service, true);

                    await service.HealthCheckAsync();

                    AssertFalse(GetRetryDispatchNeeded(service));
                }
            }));

            cases.Add(CaseAsync("health_check_async_keeps_retry_flag_when_pending_mission_has_no_capacity", "HealthCheckAsync KeepsRetryFlagWhenPendingMissionHasNoCapacity", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
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
            }));

            cases.Add(CaseAsync("validate_dispatch_full_request_is_valid", "ValidateDispatchAsync accepts a full vessel+missions request", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("v", "https://github.com/test/repo.git"));

                    DispatchValidationResult result = await service.ValidateDispatchAsync(null, null, null, vessel.Id, 2, allowBareVoyage: true);
                    AssertTrue(result.IsValid, "a full request should be valid");
                    AssertFalse(result.IsBareVoyage, "a vessel with missions is not a bare voyage");
                }
            }));

            cases.Add(CaseAsync("validate_dispatch_unknown_objective_is_rejected", "ValidateDispatchAsync rejects an unknown objective", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("v", "https://github.com/test/repo.git"));

                    DispatchValidationResult result = await service.ValidateDispatchAsync("obj_missing", null, null, vessel.Id, 1, allowBareVoyage: true);
                    AssertFalse(result.IsValid, "an unknown objective should reject");
                    AssertEqual(DispatchValidationErrorEnum.ObjectiveNotFound, result.Error);
                }
            }));

            cases.Add(CaseAsync("validate_dispatch_unknown_pipeline_name_is_rejected", "ValidateDispatchAsync rejects an unknown pipeline name", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("v", "https://github.com/test/repo.git"));

                    DispatchValidationResult result = await service.ValidateDispatchAsync(null, null, "NoSuchPipeline", vessel.Id, 1, allowBareVoyage: true);
                    AssertFalse(result.IsValid, "an unknown pipeline name should reject");
                    AssertEqual(DispatchValidationErrorEnum.PipelineNotFound, result.Error);
                }
            }));

            cases.Add(CaseAsync("validate_dispatch_missing_vessel_bare_allowed_is_bare", "ValidateDispatchAsync treats a missing vessel as a bare voyage when allowed", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    DispatchValidationResult result = await service.ValidateDispatchAsync(null, null, null, null, 0, allowBareVoyage: true);
                    AssertTrue(result.IsValid, "a bare voyage is valid when allowed");
                    AssertTrue(result.IsBareVoyage, "no vessel / no missions is a bare voyage");
                }
            }));

            cases.Add(CaseAsync("validate_dispatch_missing_vessel_bare_disallowed_rejects", "ValidateDispatchAsync rejects a missing vessel when bare voyages are disallowed", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    DispatchValidationResult result = await service.ValidateDispatchAsync(null, null, null, null, 1, allowBareVoyage: false);
                    AssertFalse(result.IsValid, "a missing vessel should reject when bare is disallowed");
                    AssertEqual(DispatchValidationErrorEnum.MissingVessel, result.Error);
                }
            }));

            cases.Add(CaseAsync("validate_dispatch_zero_missions_bare_disallowed_rejects", "ValidateDispatchAsync rejects zero missions when bare voyages are disallowed", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("v", "https://github.com/test/repo.git"));
                    DispatchValidationResult result = await service.ValidateDispatchAsync(null, null, null, vessel.Id, 0, allowBareVoyage: false);
                    AssertFalse(result.IsValid, "zero missions should reject when bare is disallowed");
                    AssertEqual(DispatchValidationErrorEnum.NoMissions, result.Error);
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Admiral Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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
            FieldInfo? field = typeof(AdmiralService).GetField(
                "_RetryDispatchNeeded",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Could not find _RetryDispatchNeeded field.");
            return (bool)field!.GetValue(service)!;
        }

        private static void SetRetryDispatchNeeded(AdmiralService service, bool value)
        {
            FieldInfo? field = typeof(AdmiralService).GetField(
                "_RetryDispatchNeeded",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("Could not find _RetryDispatchNeeded field.");
            field!.SetValue(service, value);
        }

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

        private static AdmiralService CreateAdmiralService(LoggingModule logging, DatabaseDriver db, ArmadaSettings settings, StubGitService git)
        {
            IDockService dockService = new DockService(logging, db, settings, git);
            ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            IMissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
            IVoyageService voyageService = new VoyageService(logging, db);
            return new AdmiralService(logging, db, settings, captainService, missionService, voyageService, dockService);
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
