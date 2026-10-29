namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
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
                    AssertEqual(0, status.InProgressVoyages);
                    AssertEqual(0, status.OpenVoyages);
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
                    AssertEqual(1, status.InProgressVoyages);
                    EnumerationResult<VoyageProgress> page = await VoyageProgressReader.EnumerateAsync(db, AuthContext.Authenticated(null!, null!, true, false, "Test"), new EnumerationQuery());
                    AssertEqual(1L, page.TotalRecords);
                    AssertEqual(2, page.Objects[0].TotalMissions);
                    AssertEqual(1, page.Objects[0].CompletedMissions);
                    AssertEqual(1, page.Objects[0].InProgressMissions);
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

                    await AssertThrowsAsync<KeyNotFoundException>(() =>
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

            cases.Add(CaseAsync("dispatch_voyage_evaluation_flag_propagates", "DispatchVoyageAsync with isEvaluation flags the voyage, every mission (standard and pipeline paths) and their stage outcomes", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), new StubGitService());
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("EvalVessel", "https://github.com/test/eval")).ConfigureAwait(false);
                    Pipeline pipeline = new Pipeline("EvalTwoStage");
                    pipeline.Stages = new List<PipelineStage> { new PipelineStage(1, "Worker"), new PipelineStage(2, "Judge") };
                    pipeline = await db.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

                    Voyage standard = await service.DispatchVoyageAsync("Eval", "d", vessel.Id,
                        new List<MissionDescription> { new MissionDescription("m1", "d1"), new MissionDescription("m2", "d2") },
                        null, null, null, null, CancellationToken.None, null, true).ConfigureAwait(false);
                    Voyage staged = await service.DispatchVoyageAsync("Eval staged", "d", vessel.Id,
                        new List<MissionDescription> { new MissionDescription("m3", "d3") },
                        pipeline.Id, null, null, null, CancellationToken.None, null, true).ConfigureAwait(false);

                    foreach (Voyage voyage in new[] { standard, staged })
                    {
                        AssertTrue(voyage.IsEvaluation, "returned voyage flagged");
                        AssertTrue((await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.IsEvaluation, "persisted voyage flagged");
                        List<Mission> missions = await db.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                        AssertTrue(missions.Count > 0, "missions created");
                        foreach (Mission m in missions) AssertTrue(m.IsEvaluation, "mission inherits the voyage flag: " + m.Title);
                    }
                    AssertEqual(2, (await db.Missions.EnumerateByVoyageAsync(staged.Id).ConfigureAwait(false)).Count, "pipeline path created both stages");

                    Captain captain = await db.Captains.CreateAsync(new Captain("eval-captain")).ConfigureAwait(false);
                    Mission first = (await db.Missions.EnumerateByVoyageAsync(standard.Id).ConfigureAwait(false))[0];
                    StageOutcome? outcome = await new StageOutcomeService(db, CreateLogging()).RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = first.Id, Captain = captain, ExitCode = 0 }).ConfigureAwait(false);
                    AssertNotNull(outcome, "outcome recorded");
                    AssertTrue(outcome!.IsEvaluation, "stage outcome tagged from the mission flag");
                    AssertTrue((await db.StageOutcomes.ReadAsync(outcome.Id).ConfigureAwait(false))!.IsEvaluation, "persisted outcome tagged");

                    Mission added = await service.DispatchMissionAsync(new Mission("late", "d") { VoyageId = standard.Id, VesselId = vessel.Id }).ConfigureAwait(false);
                    AssertTrue(added.IsEvaluation, "a mission added to an evaluation voyage inherits the flag");
                }
            }));

            cases.Add(CaseAsync("dispatch_voyage_default_is_not_evaluation", "DispatchVoyageAsync without the flag leaves the voyage, missions and stage outcomes as production", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), new StubGitService());
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("ProdVessel", "https://github.com/test/prod")).ConfigureAwait(false);

                    Voyage voyage = await service.DispatchVoyageAsync("Prod", "d", vessel.Id,
                        new List<MissionDescription> { new MissionDescription("m1", "d1") }, (string?)null).ConfigureAwait(false);
                    AssertFalse(voyage.IsEvaluation, "voyage not flagged");
                    AssertFalse((await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.IsEvaluation, "persisted voyage not flagged");
                    Mission mission = (await db.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false)).Single();
                    AssertFalse(mission.IsEvaluation, "mission not flagged");

                    Captain captain = await db.Captains.CreateAsync(new Captain("prod-captain")).ConfigureAwait(false);
                    StageOutcome? outcome = await new StageOutcomeService(db, CreateLogging()).RecordAttemptAsync(new StageAttemptCapture { Claimed = true, MissionId = mission.Id, Captain = captain, ExitCode = 0 }).ConfigureAwait(false);
                    AssertFalse(outcome!.IsEvaluation, "production stage outcome not tagged");

                    Mission added = await service.DispatchMissionAsync(new Mission("late", "d") { VoyageId = voyage.Id, VesselId = vessel.Id }).ConfigureAwait(false);
                    AssertFalse(added.IsEvaluation, "a mission added to a production voyage stays production");
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

                    await AssertThrowsAsync<KeyNotFoundException>(() => service.RecallCaptainAsync("cpt_nonexistent"));
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

            cases.Add(CaseAsync("recall_captain_async_cancels_active_mission", "RecallCaptainAsync cancels the active mission as an operator stop with a reason", TestTags.Positive, async () =>
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
                    AssertEqual(MissionStatusEnum.Cancelled, result!.Status, "an operator stop is a cancel, not a failure");
                    AssertEqual(MissionFailureKindEnum.OperatorAction, result.FailureKind, "classified as an operator action");
                    AssertContains("Stopped by operator via recall at ", result.FailureReason ?? "", "the reason names the stop");
                    AssertNotNull(result.CompletedUtc);
                }
            }));

            cases.Add(CaseAsync("recall_captain_with_context_records_source_user_and_event", "RecallCaptainAsync records who stopped the mission, how, and emits mission.stopped_by_operator", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("stop-ctx-vessel", "https://github.com/test/repo.git"));
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("stop-ctx-voyage"));
                    Mission mission = new Mission("Stopped Mission");
                    mission.Status = MissionStatusEnum.Assigned;
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("recall-context");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    await db.Captains.CreateAsync(captain);

                    OperatorStopContext stop = new OperatorStopContext(OperatorStopContext.SourceMcpStopCaptain, "usr_operator");
                    stop.RequestedUtc = new DateTime(2026, 10, 28, 2, 37, 59, DateTimeKind.Utc);
                    await service.RecallCaptainAsync(captain.Id, stop);

                    Mission? result = await db.Missions.ReadAsync(mission.Id);
                    AssertEqual(MissionStatusEnum.Cancelled, result!.Status);
                    AssertEqual("Stopped by operator (usr_operator) via stop_captain (MCP) at 2026-10-28T02:37:59Z.", result.FailureReason);

                    List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync(mission.Id);
                    ArmadaEvent? stopped = events.Find(e => e.EventType == "mission.stopped_by_operator");
                    AssertNotNull(stopped, "a mission.stopped_by_operator event is emitted");
                    AssertEqual(captain.Id, stopped!.CaptainId);
                    AssertEqual(vessel.Id, stopped.VesselId);
                    AssertEqual(voyage.Id, stopped.VoyageId);
                    AssertContains("(usr_operator)", stopped.Message);
                    OperatorStopEventPayload? payload = System.Text.Json.JsonSerializer.Deserialize<OperatorStopEventPayload>(stopped.Payload ?? "{}");
                    AssertEqual(OperatorStopContext.SourceMcpStopCaptain, payload!.Source);
                    AssertEqual("Assigned", payload.PreviousStatus);
                    AssertEqual("usr_operator", payload.UserId);
                }
            }));

            cases.Add(CaseAsync("recall_all_with_context_records_stop_all", "RecallAllAsync records stop_all on every interrupted mission", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, CreateSettings(), git);

                    List<string> missionIds = new List<string>();
                    for (int i = 0; i < 2; i++)
                    {
                        Mission mission = await db.Missions.CreateAsync(new Mission("stop-all " + i) { Status = MissionStatusEnum.InProgress });
                        missionIds.Add(mission.Id);
                        await db.Captains.CreateAsync(new Captain("stop-all-" + i) { State = CaptainStateEnum.Working, CurrentMissionId = mission.Id });
                    }

                    await service.RecallAllAsync(new OperatorStopContext(OperatorStopContext.SourceRestStopAll, "usr_admin"));

                    foreach (string id in missionIds)
                    {
                        Mission? result = await db.Missions.ReadAsync(id);
                        AssertEqual(MissionStatusEnum.Cancelled, result!.Status);
                        AssertContains("(usr_admin) via stop_all (REST)", result.FailureReason ?? "");
                    }
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

                    Voyage voyage = new Voyage("Model Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("Model Judge");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 4242;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("model-judge");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = 4242;
                    await db.Captains.CreateAsync(captain);

                    // The runtime reported a structured provider error: the model does not exist for these credentials.
                    RuntimeProviderError providerError = new RuntimeProviderError { HttpStatusCode = 404, ErrorType = "not_found_error", Message = "model: claude-nonexistent" };
                    await service.HandleProcessExitAsync(4242, RuntimeFailureClassifier.Decide(1, providerError), captain.Id, mission.Id).ConfigureAwait(false);

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? updatedCaptain = await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    Voyage? updatedVoyage = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);

                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertNotNull(updatedVoyage, "Voyage should still exist");
                    AssertEqual(MissionStatusEnum.Failed, updatedMission!.Status, "Mission should fail immediately on a non-retryable runtime error");
                    AssertEqual(MissionFailureKindEnum.Crash, updatedMission.FailureKind, "A runtime exit failure is recorded as a crash");
                    AssertContains("claude-nonexistent", updatedMission.FailureReason ?? String.Empty, "Mission should preserve the provider's error message");
                    AssertEqual(CaptainStateEnum.Stalled, updatedCaptain!.State, "Captain should be stalled when the runtime is unavailable");
                    AssertNull(updatedCaptain.CurrentMissionId, "Captain mission assignment should be cleared");
                    AssertEqual(VoyageStatusEnum.Cancelled, updatedVoyage!.Status, "Voyage should be halted after the failed mission");
                }
            }));

            cases.Add(CaseAsync("handle_process_exit_async_resumes_in_place_on_transient_model_error", "A model endpoint 5xx relaunches the captain in the same dock with a note, and fails only after MaxRecoveryAttempts", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 2;
                    settings.TransientOutageGraceMinutes = 0;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    LoggingModule logging = CreateLogging();
                    IDockService dockService = new DockService(logging, db, settings, git);
                    CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
                    int relaunches = 0;
                    captainService.OnLaunchAgent = (_, _, _) => { relaunches++; return Task.FromResult(7000 + relaunches); };
                    IMissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
                    AdmiralService service = new AdmiralService(logging, db, settings, captainService, missionService, new VoyageService(logging, db), dockService);

                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("resume-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
                    Voyage voyage = new Voyage("Resume Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_resume_dock_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dock.WorktreePath);
                    dock.Active = true;
                    dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);

                    Mission mission = new Mission("Worker on a local model", "Do the work.");
                    mission.VoyageId = voyage.Id;
                    mission.VesselId = vessel.Id;
                    mission.Persona = "Worker";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 4242;
                    mission.DockId = dock.Id;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("mux", AgentRuntimeEnum.Mux);
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.CurrentDockId = dock.Id;
                    captain.ProcessId = 4242;
                    await db.Captains.CreateAsync(captain);

                    RuntimeProviderError endpoint500 = new RuntimeProviderError { HttpStatusCode = 500, ErrorType = "llm_error", Message = "error parsing tool call", Reason = "the model endpoint rejected the request" };
                    await service.HandleProcessExitAsync(4242, RuntimeFailureClassifier.Decide(1, endpoint500), captain.Id, mission.Id).ConfigureAwait(false);

                    Mission? resumed = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, resumed!.Status, "a transient endpoint error resumes instead of failing");
                    AssertEqual(1, relaunches, "relaunched once in the same dock");
                    AssertEqual(dock.Id, resumed.DockId, "the same dock, with the work so far");
                    AssertContains("Earlier attempt 1 stopped", resumed.Description ?? "");
                    AssertContains("git status", resumed.Description ?? "");
                    Voyage? stillRunning = await db.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertEqual(VoyageStatusEnum.InProgress, stillRunning!.Status, "the voyage keeps running");

                    // Second failure: last allowed resume.
                    await service.HandleProcessExitAsync(resumed.ProcessId!.Value, RuntimeFailureClassifier.Decide(1, endpoint500), captain.Id, mission.Id).ConfigureAwait(false);
                    Mission? second = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, second!.Status);
                    AssertEqual(2, relaunches);

                    // Third failure: attempts exhausted, the mission fails with the reason.
                    await service.HandleProcessExitAsync(second.ProcessId!.Value, RuntimeFailureClassifier.Decide(1, endpoint500), captain.Id, mission.Id).ConfigureAwait(false);
                    Mission? failed = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, failed!.Status, "fails once MaxRecoveryAttempts is used up");
                    AssertEqual(2, relaunches);
                    AssertContains("rejected the request", failed.FailureReason ?? "");
                    AssertEqual(MissionFailureKindEnum.Infra, failed.FailureKind, "an endpoint outage is the environment, not a crash to rescue");
                    try { Directory.Delete(dock.WorktreePath, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("transient_outage_is_ridden_out_past_max_recovery", "Within TransientOutageGraceMinutes a repeating endpoint 5xx keeps resuming past MaxRecoveryAttempts, recording the failure once", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 1;
                    settings.TransientOutageGraceMinutes = 10;
                    settings.TransientResumeDelaySeconds = 0;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    LoggingModule logging = CreateLogging();
                    IDockService dockService = new DockService(logging, db, settings, git);
                    CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
                    int relaunches = 0;
                    captainService.OnLaunchAgent = (_, _, _) => { relaunches++; return Task.FromResult(7000 + relaunches); };
                    IMissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
                    AdmiralService service = new AdmiralService(logging, db, settings, captainService, missionService, new VoyageService(logging, db), dockService);

                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("resume-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
                    Voyage voyage = new Voyage("Resume Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Dock dock = new Dock(vessel.Id);
                    dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_resume_dock_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dock.WorktreePath);
                    dock.Active = true;
                    dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);

                    Mission mission = new Mission("Worker on a local model", "Do the work.");
                    mission.VoyageId = voyage.Id;
                    mission.VesselId = vessel.Id;
                    mission.Persona = "Worker";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 4242;
                    mission.DockId = dock.Id;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("mux", AgentRuntimeEnum.Mux);
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.CurrentDockId = dock.Id;
                    captain.ProcessId = 4242;
                    await db.Captains.CreateAsync(captain);

                    RuntimeProviderError endpoint500 = new RuntimeProviderError { HttpStatusCode = 500, ErrorType = "llm_error", Message = "error parsing tool call", Reason = "the model endpoint rejected the request" };
                    await service.HandleProcessExitAsync(4242, RuntimeFailureClassifier.Decide(1, endpoint500), captain.Id, mission.Id).ConfigureAwait(false);

                    int? processId = 4242;
                    for (int i = 0; i < 3; i++)
                    {
                        if (i > 0) await service.HandleProcessExitAsync(processId!.Value, RuntimeFailureClassifier.Decide(1, endpoint500), captain.Id, mission.Id).ConfigureAwait(false);
                        Mission? current = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                        AssertEqual(MissionStatusEnum.InProgress, current!.Status, "still resuming on failure " + (i + 1) + " of an outage inside the grace window");
                        processId = current.ProcessId;
                    }
                    AssertEqual(3, relaunches, "resumed past MaxRecoveryAttempts of 1");
                    Mission? after = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    int notes = (after!.Description ?? "").Split("stopped" + "\n", StringSplitOptions.None).Length - 1;
                    AssertEqual(1, notes, "a repeating failure is recorded once, not once per resume");
                    try { Directory.Delete(dock.WorktreePath, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("stage_check_resumes_local_model_on_failing_build", "A Mux stage that exits 0 but leaves the vessel's build failing is resumed with the build output; a passing build or a frontier captain proceeds", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 2;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    LoggingModule logging = CreateLogging();
                    IDockService dockService = new DockService(logging, db, settings, git);
                    CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
                    int relaunches = 0;
                    captainService.OnLaunchAgent = (_, _, _) => { relaunches++; return Task.FromResult(8000 + relaunches); };
                    MissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
                    AdmiralService service = new AdmiralService(logging, db, settings, captainService, missionService, new VoyageService(logging, db), dockService);

                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("check-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
                    WorkflowProfile profile = new WorkflowProfile { Name = "check", Scope = WorkflowProfileScopeEnum.Vessel, VesselId = vessel.Id, IsDefault = true, Active = true, BuildCommand = "echo 'error CS1513: } expected' && exit 3" };
                    await db.WorkflowProfiles.CreateAsync(profile).ConfigureAwait(false);
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("check") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);

                    async Task<Mission> RunningAsync(Captain captain)
                    {
                        Dock dock = new Dock(vessel.Id);
                        dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_check_dock_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dock.WorktreePath);
                        dock.Active = true;
                        dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);
                        Mission m = await db.Missions.CreateAsync(new Mission("Worker on a local model", "Do it.") { VoyageId = voyage.Id, VesselId = vessel.Id, Persona = "Worker", Status = MissionStatusEnum.InProgress, ProcessId = 4242, DockId = dock.Id }).ConfigureAwait(false);
                        captain.State = CaptainStateEnum.Working;
                        captain.CurrentMissionId = m.Id;
                        captain.CurrentDockId = dock.Id;
                        captain.ProcessId = 4242;
                        await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                        return m;
                    }

                    Captain mux = new Captain("mux", AgentRuntimeEnum.Mux);
                    Mission mission = await RunningAsync(mux).ConfigureAwait(false);
                    await service.HandleProcessExitAsync(4242, 0, mux.Id, mission.Id).ConfigureAwait(false);
                    Mission? resumed = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, resumed!.Status, "a failing build resumes the local model");
                    AssertEqual(1, relaunches);
                    AssertContains("stage check failed", resumed.Description ?? "");
                    AssertContains("CS1513", resumed.Description ?? "", "the build output reaches the model");

                    // A frontier captain is not checked: it proceeds to completion.
                    Captain claude = new Captain("claude", AgentRuntimeEnum.ClaudeCode);
                    Mission frontier = await RunningAsync(claude).ConfigureAwait(false);
                    await service.HandleProcessExitAsync(4242, 0, claude.Id, frontier.Id).ConfigureAwait(false);
                    Mission? done = await db.Missions.ReadAsync(frontier.Id).ConfigureAwait(false);
                    AssertNotEqual(MissionStatusEnum.InProgress, done!.Status, "a frontier captain's clean exit is not re-run");
                    AssertEqual(1, relaunches);

                    // A passing build lets the local model's stage proceed.
                    profile.BuildCommand = "true";
                    await db.WorkflowProfiles.UpdateAsync(profile).ConfigureAwait(false);
                    Captain mux2 = new Captain("mux2", AgentRuntimeEnum.Mux);
                    Mission passing = await RunningAsync(mux2).ConfigureAwait(false);
                    await service.HandleProcessExitAsync(4242, 0, mux2.Id, passing.Id).ConfigureAwait(false);
                    Mission? passed = await db.Missions.ReadAsync(passing.Id).ConfigureAwait(false);
                    AssertNotEqual(MissionStatusEnum.InProgress, passed!.Status, "a passing check proceeds");
                    AssertEqual(1, relaunches);

                    profile.BuildCommand = "exit 3";
                    await db.WorkflowProfiles.UpdateAsync(profile).ConfigureAwait(false);
                    Captain exhaustedCaptain = new Captain("exhausted", AgentRuntimeEnum.ApiEndpoint) { RecoveryAttempts = settings.MaxRecoveryAttempts };
                    Mission exhausted = await RunningAsync(exhaustedCaptain).ConfigureAwait(false);
                    exhausted.VoyageId = null;
                    await db.Missions.UpdateAsync(exhausted).ConfigureAwait(false);
                    await service.HandleProcessExitAsync(4242, 0, exhaustedCaptain.Id, exhausted.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, (await db.Missions.ReadAsync(exhausted.Id).ConfigureAwait(false))!.Status, "A failed check cannot advance when recovery is exhausted");
                    AssertEqual(1, relaunches);
                }
            }));

            cases.Add(CaseAsync("stuck_local_worker_escalates_to_planner", "A Mux Worker that keeps failing after its resumes is handed, on the same branch, to the captain that planned the voyage", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 1;
                    settings.TransientOutageGraceMinutes = 0;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("esc-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("esc") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Captain claude = await db.Captains.CreateAsync(new Captain("claude", AgentRuntimeEnum.ClaudeCode) { State = CaptainStateEnum.Idle }).ConfigureAwait(false);
                    await db.Missions.CreateAsync(new Mission("Plan [Architect]", "plan") { VoyageId = voyage.Id, VesselId = vessel.Id, Persona = "Architect", Status = MissionStatusEnum.WorkProduced, CaptainId = claude.Id }).ConfigureAwait(false);

                    Dock dock = new Dock(vessel.Id) { WorktreePath = Path.Combine(Path.GetTempPath(), "armada_esc_dock_" + Guid.NewGuid().ToString("N")), Active = true };
                    Directory.CreateDirectory(dock.WorktreePath);
                    dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);
                    Mission worker = await db.Missions.CreateAsync(new Mission("Build it [Worker]", "Do it.") { VoyageId = voyage.Id, VesselId = vessel.Id, Persona = "Worker", Status = MissionStatusEnum.InProgress, ProcessId = 4242, DockId = dock.Id, BranchName = "armada/mux/w" }).ConfigureAwait(false);
                    Captain mux = await db.Captains.CreateAsync(new Captain("mux", AgentRuntimeEnum.Mux) { State = CaptainStateEnum.Working, CurrentMissionId = worker.Id, CurrentDockId = dock.Id, ProcessId = 4242, RecoveryAttempts = 1 }).ConfigureAwait(false);

                    RuntimeProviderError endpoint500 = new RuntimeProviderError { HttpStatusCode = 500, ErrorType = "llm_error", Reason = "the model endpoint rejected the request" };
                    await service.HandleProcessExitAsync(4242, RuntimeFailureClassifier.Decide(1, endpoint500), mux.Id, worker.Id).ConfigureAwait(false);

                    Mission? escalated = await db.Missions.ReadAsync(worker.Id).ConfigureAwait(false);
                    AssertNotEqual(MissionStatusEnum.Failed, escalated!.Status, "the stuck Worker is not failed");
                    AssertEqual(claude.Id, escalated.RequestedCaptainId, "the planner's captain takes it over");
                    AssertEqual("armada/mux/w", escalated.BranchName, "on the same branch");
                    AssertContains("## Escalated to claude", escalated.Description ?? "");
                    try { Directory.Delete(dock.WorktreePath, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("strict_worker_is_not_escalated_to_another_captain", "A Worker strictly assigned to one captain fails after its resumes instead of finishing on the planner's captain", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxRecoveryAttempts = 1;
                    settings.TransientOutageGraceMinutes = 0;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("strict-esc-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
                    Captain claude = await db.Captains.CreateAsync(new Captain("claude", AgentRuntimeEnum.ClaudeCode) { State = CaptainStateEnum.Idle }).ConfigureAwait(false);
                    Captain mux = await db.Captains.CreateAsync(new Captain("mux", AgentRuntimeEnum.Mux) { State = CaptainStateEnum.Working, ProcessId = 4343, RecoveryAttempts = 1 }).ConfigureAwait(false);
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("strict-esc") { Status = VoyageStatusEnum.InProgress,
                        CaptainOverridesJson = System.Text.Json.JsonSerializer.Serialize(new List<CaptainAssignmentOverride> { new CaptainAssignmentOverride { Persona = "Worker", CaptainId = mux.Id, StrictCaptain = true } }) }).ConfigureAwait(false);
                    await db.Missions.CreateAsync(new Mission("Plan [Architect]", "plan") { VoyageId = voyage.Id, VesselId = vessel.Id, Persona = "Architect", Status = MissionStatusEnum.WorkProduced, CaptainId = claude.Id }).ConfigureAwait(false);

                    Dock dock = new Dock(vessel.Id) { WorktreePath = Path.Combine(Path.GetTempPath(), "armada_esc_dock_" + Guid.NewGuid().ToString("N")), Active = true };
                    Directory.CreateDirectory(dock.WorktreePath);
                    dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);
                    Mission worker = await db.Missions.CreateAsync(new Mission("Build it [Worker]", "Do it.") { VoyageId = voyage.Id, VesselId = vessel.Id, Persona = "Worker", Status = MissionStatusEnum.InProgress, ProcessId = 4343, DockId = dock.Id, BranchName = "armada/mux/strict", RequestedCaptainId = mux.Id, CaptainId = mux.Id }).ConfigureAwait(false);
                    mux.CurrentMissionId = worker.Id;
                    mux.CurrentDockId = dock.Id;
                    await db.Captains.UpdateAsync(mux).ConfigureAwait(false);

                    RuntimeProviderError endpoint502 = new RuntimeProviderError { HttpStatusCode = 502, ErrorType = "llm_error", Reason = "the model endpoint rejected the request" };
                    await service.HandleProcessExitAsync(4343, RuntimeFailureClassifier.Decide(1, endpoint502), mux.Id, worker.Id).ConfigureAwait(false);

                    Mission? after = await db.Missions.ReadAsync(worker.Id).ConfigureAwait(false);
                    AssertEqual(mux.Id, after!.RequestedCaptainId, "the strict captain is kept");
                    AssertFalse((after.Description ?? "").Contains("## Escalated to", StringComparison.Ordinal), "no escalation to a different model");
                    try { Directory.Delete(dock.WorktreePath, true); } catch { }
                }
            }));

            cases.Add(Case("runtime_failure_classifier_transient_and_task_failures", "Endpoint 5xx and connection errors are Transient; iteration and budget stops are TaskFailure", TestTags.Positive, () =>
            {
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 500, ErrorType = "llm_error" }));
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 503 }));
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "llm_connection_error" }));
                AssertEqual(RuntimeFailureKindEnum.TaskFailure, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { ErrorType = "max_iterations_reached" }));
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, RuntimeFailureClassifier.Classify(1, new RuntimeProviderError { HttpStatusCode = 429 }));
                AssertEqual(RuntimeFailureKindEnum.Crash, RuntimeFailureClassifier.Classify(1, null));
            }));

            cases.Add(CaseAsync("handle_process_exit_async_waits_for_launch_to_be_recorded", "HandleProcessExitAsync waits for an Assigned mission's launch to be recorded before handling a fast exit", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    StubGitService git = new StubGitService();
                    ArmadaSettings settings = CreateSettings();
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, git);

                    Voyage voyage = new Voyage("Fast Exit Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    // The in-process captain finished before the launch path wrote InProgress.
                    Mission mission = new Mission("Fast Exit Mission");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.Assigned;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("fast-exit-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    await db.Captains.CreateAsync(captain);

                    string missionLogDir = Path.Combine(settings.LogDirectory, "missions");
                    Directory.CreateDirectory(missionLogDir);
                    await File.WriteAllTextAsync(
                        Path.Combine(missionLogDir, mission.Id + ".log"),
                        "[stderr] You've hit your limit and must wait for reset.\n[2026-04-02 23:49:03] Agent exited with code 1").ConfigureAwait(false);

                    Task exitTask = service.HandleProcessExitAsync(2000000001, 1, captain.Id, mission.Id);
                    await Task.Delay(500).ConfigureAwait(false);
                    Mission? whileWaiting = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Assigned, whileWaiting!.Status, "The exit must not be handled while the launch is unrecorded");

                    // The launch path now records the launch, as MissionService does after OnLaunchAgent returns.
                    whileWaiting.Status = MissionStatusEnum.InProgress;
                    whileWaiting.ProcessId = 2000000001;
                    await db.Missions.UpdateAsync(whileWaiting).ConfigureAwait(false);

                    await exitTask.ConfigureAwait(false);

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, updatedMission!.Status, "The exit should be handled once the launch is recorded, not lost");
                }
            }));

            cases.Add(CaseAsync("handle_process_exit_async_launch_wait_survives_wall_clock_jump", "HandleProcessExitAsync waits for the launch to be recorded on the monotonic clock: a wall-clock jump (sleep/wake) does not end the wait early", TestTags.Reliability, async () =>
            {
                // The 30 s wait used a DateTime.UtcNow deadline: when the host slept, the wall clock jumped past it and
                // the exit was handled against the still-Assigned mission (which cannot move to Failed), so the
                // outcome was lost once the launch path wrote InProgress. Every wall-clock reading here is 10 minutes
                // after the last.
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());
                    JumpingTimeProvider time = new JumpingTimeProvider();
                    service.Time = time;

                    Voyage voyage = new Voyage("Jump Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);
                    Mission mission = new Mission("Jump Mission");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.Assigned;
                    await db.Missions.CreateAsync(mission);
                    Captain captain = new Captain("jump-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    await db.Captains.CreateAsync(captain);

                    Task exitTask = service.HandleProcessExitAsync(2000000002, 1, captain.Id, mission.Id);

                    // Wait (on a condition, not a sleep) until the wait loop has gone around a few times.
                    MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(20));
                    while (time.TimestampReadings < 4 && !exitTask.IsCompleted && !deadline.Passed) await Task.Delay(10).ConfigureAwait(false);
                    AssertFalse(exitTask.IsCompleted, "the exit is still waiting for the launch to be recorded");
                    Mission? whileWaiting = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Assigned, whileWaiting!.Status, "the exit must not be handled while the launch is unrecorded");

                    whileWaiting.Status = MissionStatusEnum.InProgress;
                    whileWaiting.ProcessId = 2000000002;
                    await db.Missions.UpdateAsync(whileWaiting).ConfigureAwait(false);
                    await exitTask.ConfigureAwait(false);

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, updatedMission!.Status, "the exit is handled once the launch is recorded, not lost");
                }
            }));

            cases.Add(CaseAsync("crash_loop_window_uses_monotonic_clock", "Crash-loop detection counts crashes inside the window on the monotonic clock: wall-clock jumps neither empty nor stretch the window", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    ArmadaSettings settings = CreateSettings();
                    settings.CaptainCrashLoopThreshold = 3;
                    settings.CaptainCrashLoopWindowMinutes = 15;
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, settings, new StubGitService());
                    JumpingTimeProvider time = new JumpingTimeProvider();
                    service.Time = time;

                    // Three crashes seconds apart while the wall clock leaps 10 minutes per reading: still a loop.
                    AssertFalse(service.RecordCrashAndCheckLoop("cpt_jump"), "first crash");
                    AssertFalse(service.RecordCrashAndCheckLoop("cpt_jump"), "second crash");
                    AssertTrue(service.RecordCrashAndCheckLoop("cpt_jump"), "third crash inside the window is a loop despite the wall-clock jumps");

                    // Crashes that really are further apart than the window (monotonic time) are not a loop.
                    AssertFalse(service.RecordCrashAndCheckLoop("cpt_slow"), "first slow crash");
                    time.Advance(TimeSpan.FromMinutes(16));
                    AssertFalse(service.RecordCrashAndCheckLoop("cpt_slow"), "second slow crash, first one aged out");
                    time.Advance(TimeSpan.FromMinutes(16));
                    AssertFalse(service.RecordCrashAndCheckLoop("cpt_slow"), "third slow crash, earlier ones aged out");
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
                    mission.RedispatchAttempts = 3; // interruptions get at least 3 redispatches; at the max a further one must fail terminally
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

            cases.Add(CaseAsync("heartbeat_recovery_retains_diagnostic_history", "Successful stall recovery retains its reason after active mission failure fields clear", TestTags.Positive, async () =>
            {
                string worktree = TestTemp.NewDirectory("stall-history");
                try
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                    {
                        DatabaseDriver db = testDb.Driver;
                        ArmadaSettings settings = CreateSettings();
                        settings.StallThresholdMinutes = 1;
                        settings.MaxRecoveryAttempts = 1;
                        AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());
                        service.OnIsProcessTracked = _ => true;
                        service.OnStopAgent = _ => Task.CompletedTask;
                        service.OnLaunchAgent = (captain, mission, dock) => Task.FromResult(2000000033);
                        TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata { Name = "recovery-owner" });
                        UserMaster user = await db.Users.CreateAsync(new UserMaster { TenantId = tenant.Id, Email = "recovery-owner@acceptance.invalid" });
                        Vessel vessel = await db.Vessels.CreateAsync(new Vessel("recovery", "https://example.invalid/repo.git") { TenantId = tenant.Id, UserId = user.Id });
                        Voyage voyage = await db.Voyages.CreateAsync(new Voyage("recovery-history") { TenantId = tenant.Id, UserId = user.Id, Status = VoyageStatusEnum.InProgress });
                        Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { TenantId = tenant.Id, UserId = user.Id, WorktreePath = worktree, Active = true });
                        Mission mission = await db.Missions.CreateAsync(new Mission("recover task")
                        {
                            TenantId = tenant.Id, UserId = user.Id, VoyageId = voyage.Id, VesselId = vessel.Id, DockId = dock.Id,
                            Status = MissionStatusEnum.InProgress, ProcessId = 98765432, StartedUtc = DateTime.UtcNow.AddMinutes(-2)
                        });
                        Captain captain = await db.Captains.CreateAsync(new Captain("recovery owner")
                        {
                            TenantId = tenant.Id, UserId = user.Id, State = CaptainStateEnum.Working,
                            ProcessId = mission.ProcessId, CurrentMissionId = mission.Id, CurrentDockId = dock.Id,
                            LastHeartbeatUtc = DateTime.UtcNow.AddMinutes(-10)
                        });
                        await service.HealthCheckAsync().ConfigureAwait(false);
                        await service.HealthCheckAsync().ConfigureAwait(false);
                        Mission updated = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                        AssertEqual(MissionStatusEnum.InProgress, updated.Status);
                        AssertEqual(1, updated.RedispatchAttempts);
                        AssertEqual(2000000033, updated.ProcessId!.Value);
                        AssertNull(updated.FailureReason);
                        List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync(mission.Id, 100).ConfigureAwait(false);
                        List<ArmadaEvent> diagnostics = events.FindAll(e => e.EventType == "captain.stalled");
                        AssertEqual(1, diagnostics.Count, "Fresh recovery must not repeat the old stall on the next health check.");
                        AssertEqual(tenant.Id, diagnostics[0].TenantId);
                        AssertEqual(user.Id, diagnostics[0].UserId);
                        AssertEqual(voyage.Id, diagnostics[0].VoyageId);
                        AssertContains("Automatic recovery 1 of 1 will be attempted", diagnostics[0].Message);
                        AssertContains("Remediation:", diagnostics[0].Message);
                    }
                }
                finally { try { Directory.Delete(worktree, true); } catch { } }
            }));

            cases.Add(CaseAsync("unconfirmed_stop_never_launches_replacement", "A failed stop cannot launch a replacement while the original job remains alive", TestTags.Negative, async () =>
            {
                string worktree = TestTemp.NewDirectory("stall-history");
                try
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                    {
                        DatabaseDriver db = testDb.Driver;
                        ArmadaSettings settings = CreateSettings();
                        settings.StallThresholdMinutes = 1;
                        settings.MaxRecoveryAttempts = 1;
                        AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());
                        service.OnIsProcessTracked = _ => true;
                        int replacementLaunches = 0;
                        service.OnStopAgent = _ => throw new TimeoutException("The Harbor stop could not be delivered; original job remains running.");
                        service.OnLaunchAgent = (captain, mission, dock) =>
                        {
                            replacementLaunches++;
                            return Task.FromResult(2000000033);
                        };
                        TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata { Name = "recovery-owner" });
                        UserMaster user = await db.Users.CreateAsync(new UserMaster { TenantId = tenant.Id, Email = "recovery-owner@acceptance.invalid" });
                        Vessel vessel = await db.Vessels.CreateAsync(new Vessel("recovery", "https://example.invalid/repo.git") { TenantId = tenant.Id, UserId = user.Id });
                        Voyage voyage = await db.Voyages.CreateAsync(new Voyage("recovery-history") { TenantId = tenant.Id, UserId = user.Id, Status = VoyageStatusEnum.InProgress });
                        Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { TenantId = tenant.Id, UserId = user.Id, WorktreePath = worktree, Active = true });
                        Mission mission = await db.Missions.CreateAsync(new Mission("recover task")
                        {
                            TenantId = tenant.Id, UserId = user.Id, VoyageId = voyage.Id, VesselId = vessel.Id, DockId = dock.Id,
                            Status = MissionStatusEnum.InProgress, ProcessId = 98765432, StartedUtc = DateTime.UtcNow.AddMinutes(-2)
                        });
                        Captain captain = await db.Captains.CreateAsync(new Captain("recovery owner")
                        {
                            TenantId = tenant.Id, UserId = user.Id, State = CaptainStateEnum.Working,
                            ProcessId = mission.ProcessId, CurrentMissionId = mission.Id, CurrentDockId = dock.Id,
                            LastHeartbeatUtc = DateTime.UtcNow.AddMinutes(-10)
                        });
                        await service.HealthCheckAsync().ConfigureAwait(false);
                        AssertEqual(0, replacementLaunches, "Do not overlap an unconfirmed original with a recovery process.");
                        Mission updated = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                        AssertEqual(0, updated.RedispatchAttempts, "Unconfirmed stops must not consume a relaunch attempt.");
                    }
                }
                finally { try { Directory.Delete(worktree, true); } catch { } }
            }));

            cases.Add(CaseAsync("heartbeat_stall_records_owned_reason_and_remediation", "A heartbeat stall preserves caller-scoped diagnostic history and terminal remediation", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.StallThresholdMinutes = 1;
                    settings.MaxRecoveryAttempts = 0;
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());
                    service.OnIsProcessTracked = _ => true;
                    service.OnStopAgent = _ => Task.CompletedTask;
                    TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata { Name = "stall-owner" });
                    UserMaster user = await db.Users.CreateAsync(new UserMaster { TenantId = tenant.Id, Email = "stall-owner@acceptance.invalid" });
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("stall-history") { TenantId = tenant.Id, UserId = user.Id, Status = VoyageStatusEnum.InProgress });
                    Mission mission = await db.Missions.CreateAsync(new Mission("stalled task")
                    {
                        TenantId = tenant.Id, UserId = user.Id, VoyageId = voyage.Id,
                        Status = MissionStatusEnum.InProgress, ProcessId = 98765432, StartedUtc = DateTime.UtcNow.AddMinutes(-2)
                    });
                    Captain captain = await db.Captains.CreateAsync(new Captain("stalled owner")
                    {
                        TenantId = tenant.Id, UserId = user.Id, State = CaptainStateEnum.Working,
                        ProcessId = mission.ProcessId, CurrentMissionId = mission.Id,
                        LastHeartbeatUtc = DateTime.UtcNow.AddMinutes(-10)
                    });
                    await service.HealthCheckAsync().ConfigureAwait(false);
                    Mission updated = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.Failed, updated.Status);
                    AssertEqual(MissionFailureKindEnum.StallRecoveryExhausted, updated.FailureKind!.Value);
                    AssertContains("configured stall threshold: 1 min", updated.FailureReason!);
                    AssertContains("Remediation:", updated.FailureReason!);
                    List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync(mission.Id, 100).ConfigureAwait(false);
                    ArmadaEvent? diagnostic = events.Find(e => e.EventType == "captain.stalled");
                    AssertNotNull(diagnostic, "The reason must survive separately from mutable mission failure state.");
                    AssertEqual(tenant.Id, diagnostic!.TenantId);
                    AssertEqual(user.Id, diagnostic.UserId);
                    AssertEqual(voyage.Id, diagnostic.VoyageId);
                    AssertEqual(captain.Id, diagnostic.CaptainId);
                    AssertContains("Automatic stall recovery is exhausted", diagnostic.Message);
                    AssertContains("Remediation:", diagnostic.Message);
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

            cases.Add(CaseAsync("max_runtime_retains_dock_when_mission_enters_landing_state", "Max runtime keeps a dock when the mission enters a landing state", TestTags.Reliability, async () =>
            {
                await AssertRuntimeReclaimRetainsDockAsync(true, MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                await AssertRuntimeReclaimRetainsDockAsync(true, MissionStatusEnum.PullRequestOpen).ConfigureAwait(false);
                await AssertRuntimeReclaimRetainsDockAsync(true, MissionStatusEnum.LandingFailed).ConfigureAwait(false);
                await AssertRuntimeReclaimRetainsDockAsync(true, MissionStatusEnum.InProgress).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("exhausted_stall_retains_dock_when_mission_enters_landing_state", "Exhausted stall recovery keeps a dock when the mission enters a landing state", TestTags.Reliability, async () =>
            {
                await AssertRuntimeReclaimRetainsDockAsync(false, MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                await AssertRuntimeReclaimRetainsDockAsync(false, MissionStatusEnum.PullRequestOpen).ConfigureAwait(false);
                await AssertRuntimeReclaimRetainsDockAsync(false, MissionStatusEnum.LandingFailed).ConfigureAwait(false);
                await AssertRuntimeReclaimRetainsDockAsync(false, MissionStatusEnum.InProgress).ConfigureAwait(false);
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

            cases.Add(CaseAsync("validate_dispatch_unknown_pipeline_id_is_rejected", "ValidateDispatchAsync rejects an unknown pipeline id and resolves an id given as a name", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("v", "https://github.com/test/repo.git"));

                    DispatchValidationResult missing = await service.ValidateDispatchAsync(null, "ppl_doesnotexist", null, vessel.Id, 1, allowBareVoyage: true);
                    AssertFalse(missing.IsValid, "an unknown pipeline id should reject");
                    AssertEqual(DispatchValidationErrorEnum.PipelineNotFound, missing.Error);

                    Pipeline pipeline = new Pipeline("NamedPipe");
                    pipeline.Stages.Add(new PipelineStage(1, "Worker"));
                    pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline);
                    DispatchValidationResult byName = await service.ValidateDispatchAsync(null, "NamedPipe", null, vessel.Id, 1, allowBareVoyage: true);
                    AssertTrue(byName.IsValid, "a pipeline name passed as the id still resolves");
                    AssertEqual(pipeline.Id, byName.ResolvedPipelineId);
                }
            }));

            cases.Add(CaseAsync("dispatch_unknown_pipeline_does_not_fall_back_to_default", "DispatchVoyageAsync with an unknown pipeline throws PipelineNotFoundException instead of using the vessel default", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    Pipeline fallback = new Pipeline("VesselDefault");
                    fallback.Stages.Add(new PipelineStage(1, "Worker"));
                    fallback = await testDb.Driver.Pipelines.CreateAsync(fallback);
                    Vessel vessel = new Vessel("v", "https://github.com/test/repo.git");
                    vessel.DefaultPipelineId = fallback.Id;
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel);

                    PipelineNotFoundException? thrown = null;
                    try
                    {
                        await service.DispatchVoyageAsync("t", "d", vessel.Id, new List<MissionDescription> { new MissionDescription("m", "d") }, "ppl_doesnotexist");
                    }
                    catch (PipelineNotFoundException ex)
                    {
                        thrown = ex;
                    }

                    AssertNotNull(thrown, "expected PipelineNotFoundException");
                    AssertEqual("ppl_doesnotexist", thrown!.PipelineReference);
                    List<Voyage> voyages = await testDb.Driver.Voyages.EnumerateAsync();
                    AssertEqual(0, voyages.Count, "no voyage may be created");
                }
            }));

            cases.Add(CaseAsync("fleet_action_persona_dispatch_sets_mission_persona", "A persona dispatch runs one mission with that persona instead of the vessel default pipeline", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    AdmiralService service = CreateAdmiralService(CreateLogging(), testDb.Driver, CreateSettings(), new StubGitService());
                    Pipeline reviewed = new Pipeline("WorkerThenJudge");
                    reviewed.Stages.Add(new PipelineStage(1, "Worker"));
                    reviewed.Stages.Add(new PipelineStage(2, "Judge"));
                    reviewed = await testDb.Driver.Pipelines.CreateAsync(reviewed);
                    Vessel vessel = new Vessel("v", "https://github.com/test/repo.git");
                    vessel.DefaultPipelineId = reviewed.Id;
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel);

                    AdmiralFleetActionMissionDispatcher dispatcher = new AdmiralFleetActionMissionDispatcher(testDb.Driver, service);
                    string voyageId = await dispatcher.DispatchAsync(vessel, "Fleet action: review", "Review the repo", null, "Judge");
                    List<Mission> missions = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyageId);
                    AssertEqual(1, missions.Count, "a persona dispatch is a single stage");
                    AssertEqual("Judge", missions[0].Persona);

                    string plainVoyageId = await dispatcher.DispatchAsync(vessel, "Fleet action: plain", "Do the work", null, null);
                    List<Mission> plain = await testDb.Driver.Missions.EnumerateByVoyageAsync(plainVoyageId);
                    AssertEqual(2, plain.Count, "without a persona the vessel default pipeline still applies");
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

            cases.Add(CaseAsync("handle_process_exit_async_quarantines_on_structured_usage_limit", "HandleProcessExitAsync quarantines a captain until the provider's stated reset", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.CaptainQuarantineMinutes = 60;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());

                    Mission mission = new Mission("Throttled");
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 4343;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("throttled-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = 4343;
                    await db.Captains.CreateAsync(captain);

                    DateTime before = DateTime.UtcNow;
                    RuntimeProviderError providerError = new RuntimeProviderError { HttpStatusCode = 429, ErrorType = "rate_limit_error", RetryAfterSeconds = 300 };
                    await service.HandleProcessExitAsync(4343, RuntimeFailureClassifier.Decide(1, providerError), captain.Id, mission.Id).ConfigureAwait(false);

                    Captain? updatedCaptain = await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertEqual(CaptainStateEnum.Quarantined, updatedCaptain!.State, "A structured usage limit quarantines the captain");
                    AssertNotNull(updatedCaptain.QuarantineUntilUtc);
                    double minutes = (updatedCaptain.QuarantineUntilUtc!.Value - before).TotalMinutes;
                    AssertTrue(minutes > 4 && minutes < 6, "Quarantine window follows the provider's Retry-After (5 minutes), not the 60-minute default; got " + minutes);
                }
            }));

            cases.Add(CaseAsync("handle_process_exit_async_ignores_error_words_in_log", "HandleProcessExitAsync never quarantines or stalls from words in the mission log", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());

                    Mission mission = new Mission("Build error");
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 4444;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("build-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = 4444;
                    await db.Captains.CreateAsync(captain);

                    // Before the fix these lines quarantined (429, billing, capacity) or stalled (forbidden, invalid model,
                    // hit your limit) the captain because the classifier searched the log for those words.
                    string missionLogDir = Path.Combine(settings.LogDirectory, "missions");
                    Directory.CreateDirectory(missionLogDir);
                    await File.WriteAllTextAsync(
                        Path.Combine(missionLogDir, mission.Id + ".log"),
                        "[stderr] src/BillingService.cs(10,5): error CS0403: capacity 429 forbidden invalid model\n" +
                        "[stderr] You've hit your limit and must wait for reset. permission denied\n" +
                        "[2026-04-02 23:49:03] Agent exited with code 1").ConfigureAwait(false);

                    await service.HandleProcessExitAsync(4444, 1, captain.Id, mission.Id).ConfigureAwait(false);

                    Mission? updatedMission = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? updatedCaptain = await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, updatedMission!.Status);
                    AssertEqual("Agent process exited with code 1", updatedMission.FailureReason, "The failure reason comes from the exit info, not a scraped log line");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Log words neither stall nor quarantine the captain");
                    AssertNull(updatedCaptain.QuarantineUntilUtc);
                }
            }));

            cases.Add(CaseAsync("process_exit_has_one_owner_when_health_check_races_exit_callback", "A process exit the health check read as vanished while the exit callback handled it has one owner: completed, not also re-dispatched", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                using (ManualResetEventSlim healthPaused = new ManualResetEventSlim(false))
                using (ManualResetEventSlim resumeHealth = new ManualResetEventSlim(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxNoOpRedispatchAttempts = 1;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());

                    // A synthetic process id, as an in-process ApiEndpoint captain reports: no OS process has it, so an
                    // OS probe always reads it as vanished (exit -1, an interruption) even while the loop is running.
                    const int processId = 2000000201;

                    Voyage voyage = new Voyage("Exit Race Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);

                    Mission mission = new Mission("Exit Race Mission");
                    mission.VoyageId = voyage.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = processId;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("exit-race-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = processId;
                    await db.Captains.CreateAsync(captain);
                    mission.CaptainId = captain.Id;
                    await db.Missions.UpdateAsync(mission);

                    // Hold the health check at its exit-handled consult: it has already read the mission as InProgress
                    // and has not yet recorded an exit decision. The run finishes (exit 0) in exactly that window, as it
                    // did in the docker install verification, and its exit callback runs to completion.
                    service.OnIsProcessExitHandled = pid =>
                    {
                        if (pid == processId)
                        {
                            healthPaused.Set();
                            resumeHealth.Wait(TimeSpan.FromSeconds(30));
                        }

                        return false;
                    };

                    Task health = service.HealthCheckAsync();
                    AssertTrue(healthPaused.Wait(TimeSpan.FromSeconds(30)), "The health check should reach its exit decision for the running process");

                    await service.HandleProcessExitAsync(processId, 0, captain.Id, mission.Id).ConfigureAwait(false);
                    resumeHealth.Set();
                    await health.ConfigureAwait(false);

                    Mission? updated = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync(mission.Id, 100).ConfigureAwait(false);
                    AssertNotNull(updated, "Mission should still exist");
                    AssertEqual(MissionStatusEnum.WorkProduced, updated!.Status, "The clean exit owns the outcome; a stale health-check verdict must not move the mission back");
                    AssertEqual(0, updated.RedispatchAttempts, "The mission must not also be re-dispatched as interrupted");
                    AssertFalse(events.Exists(e => e.EventType == "mission.redispatched"), "No interruption re-dispatch may be recorded for an exit the callback handled");
                }
            }));

            cases.Add(CaseAsync("health_check_skips_exit_whose_handler_is_still_running", "While an exit handler is still verifying the stage (past the handled-exit record's lifetime), the health check does not decide the same exit again", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                using (ManualResetEventSlim handlerPaused = new ManualResetEventSlim(false))
                using (ManualResetEventSlim releaseHandler = new ManualResetEventSlim(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    LoggingModule logging = CreateLogging();
                    StubGitService git = new StubGitService();
                    IDockService dockService = new DockService(logging, db, settings, git);
                    ICaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, db, settings, dockService, captainService);
                    AdmiralService service = new AdmiralService(logging, db, settings, captainService, missionService, new VoyageService(logging, db), dockService);

                    // A Harbor job's synthetic handle: an OS probe reads it as vanished (exit -1), and the time-limited
                    // handled-exit record has expired, as it did 5 minutes into msn_mvmiaphg_7NXYdbigLoJ's stage check.
                    const int processId = 1000000006;
                    service.OnIsProcessTracked = pid => false;
                    service.OnIsProcessExitHandled = pid => false;
                    // The stage check pauses where it reads the verifier's output, standing in for a long test run.
                    missionService.OnPeekMissionOutput = id =>
                    {
                        handlerPaused.Set();
                        releaseHandler.Wait(TimeSpan.FromSeconds(30));
                        return null;
                    };

                    Voyage voyage = new Voyage("Long Check Voyage");
                    voyage.Status = VoyageStatusEnum.InProgress;
                    await db.Voyages.CreateAsync(voyage);
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("long-check", "https://github.com/test/long-check.git")).ConfigureAwait(false);
                    Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = Path.GetTempPath(), BranchName = "armada/long-check" }).ConfigureAwait(false);

                    Mission mission = new Mission("Long Check Mission");
                    mission.VoyageId = voyage.Id;
                    mission.Persona = Armada.Core.PersonaCatalog.TestEngineer;
                    mission.DockId = dock.Id;
                    mission.VesselId = vessel.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = processId;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("long-check-captain");
                    captain.Runtime = AgentRuntimeEnum.ApiEndpoint;
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = processId;
                    await db.Captains.CreateAsync(captain);
                    mission.CaptainId = captain.Id;
                    await db.Missions.UpdateAsync(mission);

                    Task exit = Task.Run(() => service.HandleProcessExitAsync(processId, 0, captain.Id, mission.Id));
                    AssertTrue(handlerPaused.Wait(TimeSpan.FromSeconds(30)), "The exit handler should reach the stage check");

                    await service.HealthCheckAsync().ConfigureAwait(false);
                    Mission? during = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    List<ArmadaEvent> events = await db.Events.EnumerateByMissionAsync(mission.Id, 100).ConfigureAwait(false);
                    releaseHandler.Set();
                    await exit.ConfigureAwait(false);

                    AssertEqual(0, during!.RedispatchAttempts, "No interruption relaunch is counted while the exit handler runs");
                    AssertFalse(events.Exists(e => e.EventType == "mission.resumed" || e.EventType == "captain.completed"),
                        "The health check records no second exit and resumes nothing: " + String.Join(", ", events.Select(e => e.EventType)));
                }
            }));

            cases.Add(CaseAsync("health_check_leaves_tracked_process_to_its_exit_callback", "The health check treats a process its runtime still tracks as alive instead of probing the OS for a synthetic id", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxNoOpRedispatchAttempts = 1;
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());

                    const int processId = 2000000501;
                    bool running = true;
                    service.OnIsProcessTracked = pid => pid == processId && running;

                    Mission mission = new Mission("Tracked Mission");
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = processId;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("tracked-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = processId;
                    await db.Captains.CreateAsync(captain);
                    mission.CaptainId = captain.Id;
                    await db.Missions.UpdateAsync(mission);

                    await service.HealthCheckAsync().ConfigureAwait(false);

                    Mission? whileRunning = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? captainWhileRunning = await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, whileRunning!.Status, "A running in-process captain is not an interruption");
                    AssertEqual(processId, whileRunning.ProcessId ?? 0, "The exit stays unclaimed for the callback");
                    AssertEqual(0, whileRunning.RedispatchAttempts);
                    AssertEqual(CaptainStateEnum.Working, captainWhileRunning!.State);

                    running = false;
                    await service.HandleProcessExitAsync(processId, 0, captain.Id, mission.Id).ConfigureAwait(false);
                    Mission? finished = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, finished!.Status, "The exit callback completes the mission");
                }
            }));

            cases.Add(CaseAsync("later_attempt_completion_is_not_swallowed", "The clean exit of a later attempt of the same mission is completed, not skipped as a duplicate of the earlier attempt", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    DatabaseDriver db = testDb.Driver;
                    ArmadaSettings settings = CreateSettings();
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    AdmiralService service = CreateAdmiralService(CreateLogging(), db, settings, new StubGitService());

                    const int firstProcessId = 2000000301;
                    const int secondProcessId = 2000000302;

                    Mission mission = new Mission("Two Attempt Mission");
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = firstProcessId;
                    await db.Missions.CreateAsync(mission);

                    Captain captain = new Captain("two-attempt-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = mission.Id;
                    captain.ProcessId = firstProcessId;
                    await db.Captains.CreateAsync(captain);

                    await service.HandleProcessExitAsync(firstProcessId, 0, captain.Id, mission.Id).ConfigureAwait(false);
                    Mission? afterFirst = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, afterFirst!.Status, "The first attempt completes");

                    // The mission runs again on a new process (an interruption re-dispatch or a review sending it back
                    // does this), and that attempt also exits cleanly within seconds of the first.
                    afterFirst.Status = MissionStatusEnum.InProgress;
                    afterFirst.ProcessId = secondProcessId;
                    afterFirst.CaptainId = captain.Id;
                    afterFirst.CompletedUtc = null;
                    await db.Missions.UpdateAsync(afterFirst).ConfigureAwait(false);
                    Captain? rerun = await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                    rerun!.State = CaptainStateEnum.Working;
                    rerun.CurrentMissionId = mission.Id;
                    rerun.ProcessId = secondProcessId;
                    await db.Captains.UpdateAsync(rerun).ConfigureAwait(false);

                    await service.HandleProcessExitAsync(secondProcessId, 0, captain.Id, mission.Id).ConfigureAwait(false);

                    Mission? afterSecond = await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, afterSecond!.Status, "The second attempt's completion must be handled, not skipped as already in flight");
                }
            }));

            cases.Add(CaseAsync("shutdown_preserves_harbor_agents", "Shutdown stops only Admiral-local agents, leaving Harbor ownership recoverable", TestTags.Reliability, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IDockService docks = new DockService(logging, db, settings, git);
                    CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                    List<int> stopped = new List<int>();
                    captains.OnStopAgent = captain => { stopped.Add(captain.ProcessId!.Value); return Task.CompletedTask; };
                    AdmiralService service = new AdmiralService(logging, db, settings, captains, new MissionService(logging, db, settings, docks, captains), new VoyageService(logging, db), docks);
                    await db.Captains.CreateAsync(new Captain("Harbor") { State = CaptainStateEnum.Working, ProcessId = 2000000007 }).ConfigureAwait(false);
                    await db.Captains.CreateAsync(new Captain("Local") { State = CaptainStateEnum.Working, ProcessId = 2000000008 }).ConfigureAwait(false);
                    service.OnIsRemoteProcessTracked = pid => pid == 2000000007;
                    await service.StopAllAgentProcessesAsync().ConfigureAwait(false);
                    AssertEqual(1, stopped.Count);
                    AssertEqual(2000000008, stopped[0], "only the local process stops");
                }
            }));

            foreach (bool tracked in new[] { true, false })
            {
                bool captured = tracked;
                cases.Add(CaseAsync("startup_interruption_" + captured, "Startup " + (captured ? "retains a restored synthetic PID" : "counts relaunch and preserves uncommitted dock work"), TestTags.Reliability, async () =>
                {
                    using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                    {
                        DatabaseDriver db = testDb.Driver;
                        LoggingModule logging = CreateLogging();
                        ArmadaSettings settings = CreateSettings();
                        StubGitService git = new StubGitService();
                        IDockService docks = new DockService(logging, db, settings, git);
                        CaptainService captains = new CaptainService(logging, db, settings, git, docks);
                        int launches = 0;
                        captains.OnLaunchAgent = (_, _, _) => Task.FromResult(2000000010 + ++launches);
                        AdmiralService service = new AdmiralService(logging, db, settings, captains, new MissionService(logging, db, settings, docks, captains), new VoyageService(logging, db), docks);
                        Vessel vessel = await db.Vessels.CreateAsync(new Vessel("startup", "https://github.com/test/startup.git")).ConfigureAwait(false);
                        string path = Path.Combine(Path.GetTempPath(), "armada_startup_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(path);
                        File.WriteAllText(Path.Combine(path, "uncommitted.txt"), "keep my work");
                        Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = path, BranchName = "armada/same" }).ConfigureAwait(false);
                        Captain captain = await db.Captains.CreateAsync(new Captain("restart") { State = CaptainStateEnum.Working, ProcessId = 2000000007, CurrentDockId = dock.Id }).ConfigureAwait(false);
                        Mission mission = await db.Missions.CreateAsync(new Mission("restart") { Status = MissionStatusEnum.InProgress, CaptainId = captain.Id, ProcessId = captain.ProcessId, VesselId = vessel.Id, DockId = dock.Id, StartedUtc = DateTime.UtcNow.AddMinutes(-2), BranchName = dock.BranchName }).ConfigureAwait(false);
                        captain.CurrentMissionId = mission.Id;
                        await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                        service.OnIsProcessTracked = pid => captured || pid == 2000000011;
                        await service.CleanupStaleCaptainsAsync().ConfigureAwait(false);
                        await service.CleanupStaleCaptainsAsync().ConfigureAwait(false);
                        Mission after = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                        AssertEqual(captured ? 0 : 1, launches, "one actual interrupted launch, never duplicate cleanup");
                        AssertEqual(captured ? 0 : 1, after.RedispatchAttempts, "counts real relaunches, not reattachment");
                        AssertEqual(mission.StartedUtc, after.StartedUtc);
                        AssertEqual(dock.Id, after.DockId);
                        AssertEqual("keep my work", File.ReadAllText(Path.Combine(path, "uncommitted.txt")));
                        AssertTrue((await db.Docks.ReadAsync(dock.Id).ConfigureAwait(false))!.Active, "original dock stays active");
                        if (captured)
                        {
                            captain.LastHeartbeatUtc = DateTime.UtcNow.AddHours(-2);
                            await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                            service.OnIsProcessAwaitingReconnect = _ => true;
                            await service.HealthCheckAsync().ConfigureAwait(false);
                            AssertEqual(0, launches, "startup must not stall-recover an old heartbeat before the Harbor reports");
                        }
                        Directory.Delete(path, true);
                    }
                }));
            }

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

        private static async Task AssertRuntimeReclaimRetainsDockAsync(bool maxRuntime, MissionStatusEnum statusAtStop)
        {
            // An earlier case in this process can delete the process temp directory. Create the
            // worktree first so the parent exists again before the database file is opened.
            string worktree = TestTemp.NewDirectory("landing-worktree");
            string logs = TestTemp.NewDirectory("landing-logs");
            string marker = Path.Combine(worktree, "landing-payload.txt");
            File.WriteAllText(marker, "landing payload");
            try
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    DatabaseDriver db = testDb.Driver;
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.LogDirectory = logs;
                    settings.MaxMissionRuntimeMinutes = maxRuntime ? 1 : 0;
                    settings.StallThresholdMinutes = 1;
                    settings.MaxRecoveryAttempts = 0;
                    StubGitService git = new StubGitService();
                    IDockService dockService = new DockService(logging, db, settings, git);
                    ICaptainService captains = new CaptainService(logging, db, settings, git, dockService);
                    IMissionService missions = new MissionService(logging, db, settings, dockService, captains);
                    AdmiralService service = new AdmiralService(logging, db, settings, captains, missions, new VoyageService(logging, db), dockService);
                    service.OnIsProcessTracked = _ => true;

                    string suffix = statusAtStop.ToString() + "-" + Guid.NewGuid().ToString("N");
                    Vessel vessel = await db.Vessels.CreateAsync(new Vessel("landing-vessel-" + suffix, "https://example.invalid/repo.git")).ConfigureAwait(false);
                    Dock dock = await db.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = worktree, Active = true }).ConfigureAwait(false);
                    Mission mission = await db.Missions.CreateAsync(new Mission("landing-mission-" + suffix)
                    {
                        VesselId = vessel.Id,
                        DockId = dock.Id,
                        Status = MissionStatusEnum.InProgress,
                        ProcessId = 739184,
                        StartedUtc = DateTime.UtcNow.AddMinutes(-5)
                    }).ConfigureAwait(false);
                    Captain captain = await db.Captains.CreateAsync(new Captain("landing-captain-" + suffix)
                    {
                        State = CaptainStateEnum.Working,
                        ProcessId = mission.ProcessId,
                        CurrentMissionId = mission.Id,
                        CurrentDockId = dock.Id,
                        LastHeartbeatUtc = DateTime.UtcNow.AddMinutes(-5)
                    }).ConfigureAwait(false);

                    service.OnStopAgent = async _ =>
                    {
                        Mission current = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                        current.Status = statusAtStop;
                        current.LastUpdateUtc = DateTime.UtcNow;
                        await db.Missions.UpdateAsync(current).ConfigureAwait(false);
                    };

                    await service.HealthCheckAsync().ConfigureAwait(false);

                    Mission updatedMission = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    Captain updatedCaptain = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                    Dock updatedDock = (await db.Docks.ReadAsync(dock.Id).ConfigureAwait(false))!;
                    bool landingState = statusAtStop == MissionStatusEnum.WorkProduced
                        || statusAtStop == MissionStatusEnum.PullRequestOpen
                        || statusAtStop == MissionStatusEnum.LandingFailed;
                    // Exhausted stall recovery retains the dock even when the mission fails, so uncommitted work survives.
                    bool retainDock = landingState || !maxRuntime;
                    AssertEqual(landingState ? statusAtStop : MissionStatusEnum.Failed, updatedMission.Status, "A concurrent landing-state transition must not be replaced by stale health-check state");
                    AssertEqual(retainDock ? dock.Id : null, updatedMission.DockId, "Landing states and exhausted stall recovery keep the mission dock reference");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain.State, "The captain is released after stopping the process");
                    AssertNull(updatedCaptain.CurrentDockId, "The released captain no longer owns the retained dock");
                    AssertEqual(!retainDock, !updatedDock.Active, "The dock is reclaimed only when failure cleanup does not still need the worktree");
                    AssertEqual(retainDock, Directory.Exists(worktree), "A retained dock keeps the worktree available");
                    AssertEqual(retainDock, File.Exists(marker), "A retained dock keeps the worktree contents available");
                }
            }
            finally
            {
                TestTemp.TryDelete(worktree);
                TestTemp.TryDelete(logs);
            }
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
