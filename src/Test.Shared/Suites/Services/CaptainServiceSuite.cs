namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
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
    /// Descriptors for captain auto-recovery behavior in <see cref="CaptainService"/>. Cases cover
    /// recovery relaunch failure marking the mission failed, missing recovery prerequisites failing
    /// the mission, cancelled-voyage recovery skipping, retention of a landing worktree, and a healthy
    /// worktree relaunch that avoids destructive repair. Each case builds a fresh SQLite store and stubs
    /// the git and dock services.
    /// </summary>
    public sealed class CaptainServiceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.CaptainService";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Captain Service suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("recall_unconfirmed_stop_preserves_assignment", "Recall cannot release a captain or reclaim a dock after its stop failed", TestTags.Negative, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                StubDockService docks = new StubDockService();
                CaptainService service = new CaptainService(CreateLogging(), db.Driver, CreateSettings(), new StubGitService(), docks);
                Mission mission = await db.Driver.Missions.CreateAsync(new Mission("held recall") { Status = MissionStatusEnum.InProgress, ProcessId = 9191 }).ConfigureAwait(false);
                Captain captain = await db.Driver.Captains.CreateAsync(new Captain("held recall")
                {
                    State = CaptainStateEnum.Working, ProcessId = 9191, CurrentMissionId = mission.Id, CurrentDockId = "dck_still_owned"
                }).ConfigureAwait(false);
                service.OnStopAgent = _ => throw new TimeoutException("No owner exit confirmation");
                bool refused = false;
                try { await service.RecallAsync(captain.Id).ConfigureAwait(false); } catch (InvalidOperationException) { refused = true; }
                AssertTrue(refused);
                Captain current = (await db.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                Mission active = (await db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual(CaptainStateEnum.Working, current.State);
                AssertEqual(mission.Id, current.CurrentMissionId);
                AssertEqual((int?)9191, current.ProcessId);
                AssertEqual(MissionStatusEnum.InProgress, active.Status);
                AssertEqual((int?)9191, active.ProcessId);
                AssertEqual(0, docks.ReclaimCalls);
            }));

            cases.Add(CaseAsync("recovery_launch_failure_marks_mission_failed", "Recovery launch failure marks mission failed instead of leaving it in progress", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService { IsRepositoryResult = true };
                    StubDockService docks = new StubDockService();
                    CaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, docks);
                    captainService.OnLaunchAgent = (Captain captain, Mission mission, Dock dock) =>
                    {
                        throw new InvalidOperationException("synthetic relaunch failure");
                    };

                    Vessel vessel = new Vessel("recover-vessel", "https://github.com/test/repo.git");
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain captain = new Captain("recover-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = "msn_recover_test";
                    captain.CurrentDockId = "dck_recover_test";
                    captain.RecoveryAttempts = 0;
                    captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Mission mission = new Mission("Recover Mission");
                    mission.Id = "msn_recover_test";
                    mission.VesselId = vessel.Id;
                    mission.CaptainId = captain.Id;
                    mission.DockId = "dck_recover_test";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 4444;
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Dock dock = new Dock(vessel.Id);
                    dock.Id = "dck_recover_test";
                    dock.CaptainId = captain.Id;
                    dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_recover_" + Guid.NewGuid().ToString("N"));
                    dock.Active = true;
                    await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                    await captainService.TryRecoverAsync(captain).ConfigureAwait(false);

                    Mission? updatedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);

                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertEqual(MissionStatusEnum.Failed, updatedMission!.Status, "Mission should be failed when recovery relaunch fails");
                    AssertContains("synthetic relaunch failure", updatedMission.FailureReason ?? String.Empty);
                    AssertEqual(MissionFailureKindEnum.StallRecoveryExhausted, updatedMission.FailureKind, "failure kind is recorded where the failure happens");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Captain should return to Idle after failed recovery");
                    AssertNull(updatedCaptain.CurrentMissionId, "Captain current mission should be cleared");
                    AssertEqual(0, docks.ReclaimCalls, "Failed stall recovery must retain uncommitted work");
                    AssertEqual(dock.Id, updatedMission.DockId, "Mission keeps the recovery location");
                }
            }));

            cases.Add(CaseAsync("missing_recovery_dock_marks_mission_failed", "Missing recovery dock marks mission failed instead of leaving it active", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    StubDockService docks = new StubDockService();
                    CaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, docks);

                    Captain captain = new Captain("missing-dock-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = "msn_missing_dock";
                    captain.CurrentDockId = "dck_missing_dock";
                    captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Mission mission = new Mission("Missing Dock Mission");
                    mission.Id = "msn_missing_dock";
                    mission.CaptainId = captain.Id;
                    mission.DockId = "dck_missing_dock";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 9999;
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    await captainService.TryRecoverAsync(captain).ConfigureAwait(false);

                    Mission? updatedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);

                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertEqual(MissionStatusEnum.Failed, updatedMission!.Status, "Mission should be failed when recovery prerequisites are missing");
                    AssertContains("mission or dock could not be reloaded", updatedMission.FailureReason ?? String.Empty);
                    AssertEqual(MissionFailureKindEnum.Infra, updatedMission.FailureKind, "failure kind is recorded where the failure happens");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Captain should return to Idle when recovery prerequisites are missing");
                    AssertNull(updatedCaptain.CurrentMissionId, "Captain current mission should be cleared");
                }
            }));

            cases.Add(CaseAsync("cancelled_voyage_does_not_auto_recover_mission", "Cancelled voyage does not auto-recover mission", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    StubDockService docks = new StubDockService();
                    CaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, docks);

                    int launchAttempts = 0;
                    captainService.OnLaunchAgent = (Captain captain, Mission mission, Dock dock) =>
                    {
                        launchAttempts++;
                        return Task.FromResult(7777);
                    };

                    Vessel vessel = new Vessel("recover-cancelled-vessel", "https://github.com/test/repo.git");
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Voyage voyage = new Voyage("recover-cancelled-voyage");
                    voyage.Status = VoyageStatusEnum.Cancelled;
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Captain captain = new Captain("recover-cancelled-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = "msn_recover_cancelled";
                    captain.CurrentDockId = "dck_recover_cancelled";
                    captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Mission mission = new Mission("Cancelled Voyage Mission");
                    mission.Id = "msn_recover_cancelled";
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission.CaptainId = captain.Id;
                    mission.DockId = "dck_recover_cancelled";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 5555;
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Dock dock = new Dock(vessel.Id);
                    dock.Id = "dck_recover_cancelled";
                    dock.CaptainId = captain.Id;
                    dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_recover_cancelled_" + Guid.NewGuid().ToString("N"));
                    dock.Active = true;
                    await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                    await captainService.TryRecoverAsync(captain).ConfigureAwait(false);

                    Mission? updatedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);

                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertEqual(0, launchAttempts, "Cancelled-voyage mission should not be relaunched");
                    AssertEqual(MissionStatusEnum.Cancelled, updatedMission!.Status, "Mission should be cancelled when its voyage is cancelled");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Captain should return to Idle when recovery is skipped");
                    AssertNull(updatedCaptain.CurrentMissionId, "Captain current mission should be cleared");
                    AssertEqual(1, docks.ReclaimCalls, "Dock should be reclaimed when recovery is skipped");
                }
            }));

            cases.Add(CaseAsync("recovery_retains_dock_for_missions_that_still_need_landing", "Recovery keeps docks for missions that still need landing", TestTags.Reliability, async () =>
            {
                await AssertRecoveryRetainsDockAsync(MissionStatusEnum.WorkProduced).ConfigureAwait(false);
                await AssertRecoveryRetainsDockAsync(MissionStatusEnum.PullRequestOpen).ConfigureAwait(false);
                await AssertRecoveryRetainsDockAsync(MissionStatusEnum.LandingFailed).ConfigureAwait(false);
                await AssertRecoveryRetainsDockAsync(MissionStatusEnum.InProgress).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("healthy_recovery_worktree_relaunch_skips_repair", "Healthy recovery worktree relaunch skips destructive repair", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService { IsRepositoryResult = true };
                    StubDockService docks = new StubDockService();
                    CaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, docks);

                    captainService.OnLaunchAgent = (Captain captain, Mission mission, Dock dock) =>
                    {
                        return Task.FromResult(12345);
                    };

                    Vessel vessel = new Vessel("recover-healthy-vessel", "https://github.com/test/repo.git");
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain captain = new Captain("recover-healthy-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain.CurrentMissionId = "msn_recover_healthy";
                    captain.CurrentDockId = "dck_recover_healthy";
                    captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Mission mission = new Mission("Healthy Recovery Mission");
                    mission.Id = "msn_recover_healthy";
                    mission.VesselId = vessel.Id;
                    mission.CaptainId = captain.Id;
                    mission.DockId = "dck_recover_healthy";
                    mission.Status = MissionStatusEnum.InProgress;
                    mission.ProcessId = 2222;
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    Dock dock = new Dock(vessel.Id);
                    dock.Id = "dck_recover_healthy";
                    dock.CaptainId = captain.Id;
                    dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_recover_healthy_" + Guid.NewGuid().ToString("N"));
                    dock.Active = true;
                    await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                    await captainService.TryRecoverAsync(captain).ConfigureAwait(false);

                    Mission? updatedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);

                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertEqual(MissionStatusEnum.InProgress, updatedMission!.Status, "Mission should remain in progress after successful recovery");
                    AssertEqual(12345, updatedMission.ProcessId, "Mission should capture the relaunched process id");
                    AssertEqual(CaptainStateEnum.Working, updatedCaptain!.State, "Captain should remain working after successful recovery");
                    AssertEqual(12345, updatedCaptain.ProcessId, "Captain should capture the relaunched process id");
                    AssertEqual(1, git.IsRepositoryCalls, "Recovery should check whether the dock worktree is usable");
                    AssertEqual(0, git.RepairWorktreeCalls, "Recovery should not destructively repair a healthy worktree");
                }
            }));

            cases.Add(CaseAsync("operator_stop_preserves_uncommitted_work_before_reclaim", "An operator stop commits and pushes uncommitted dock work as WIP before reclaiming the dock", TestTags.Positive, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Test.Shared.Infrastructure.StubGitService git = new Test.Shared.Infrastructure.StubGitService();
                git.WorkingTreeStatusResult = new GitWorkingTreeStatus { ModifiedCount = 2 };
                git.HeadCommitResult = "feedface0001";
                StubDockService docks = new StubDockService();
                int commitsAtReclaim = -1;
                docks.OnReclaim = () => commitsAtReclaim = git.CommitAllCalls.Count;
                string worktree = Path.Combine(Path.GetTempPath(), "armada_test_wip_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(worktree);
                try
                {
                    CaptainService service = new CaptainService(CreateLogging(), db.Driver, CreateSettings(), git, docks);
                    StopFixture fixture = await CreateStopFixtureAsync(db, worktree, "Worker").ConfigureAwait(false);

                    await service.RecallAsync(fixture.Captain.Id, null, new OperatorStopContext(OperatorStopContext.SourceRestStopCaptain, "usr_wip")).ConfigureAwait(false);

                    Mission stopped = (await db.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.Cancelled, stopped.Status);
                    AssertEqual(MissionFailureKindEnum.OperatorAction, stopped.FailureKind);
                    AssertEqual(1, git.CommitAllCalls.Count, "the dirty worktree is committed");
                    AssertEqual(worktree, git.CommitAllCalls[0]);
                    AssertEqual(1, git.PushCalls.Count, "the WIP commit is pushed");
                    AssertEqual(1, commitsAtReclaim, "the commit happens before the dock is reclaimed");
                    AssertEqual(1, docks.ReclaimCalls);
                    AssertContains("Stopped by operator (usr_wip) via stop_captain (REST)", stopped.FailureReason ?? "");
                    AssertContains("WIP commit feedface0001 on branch armada/stop/wip (pushed).", stopped.FailureReason ?? "");

                    List<ArmadaEvent> events = await db.Driver.Events.EnumerateByMissionAsync(fixture.Mission.Id).ConfigureAwait(false);
                    ArmadaEvent? evt = events.Find(e => e.EventType == "mission.stopped_by_operator");
                    AssertNotNull(evt, "event emitted");
                    OperatorStopEventPayload payload = System.Text.Json.JsonSerializer.Deserialize<OperatorStopEventPayload>(evt!.Payload!)!;
                    AssertEqual("feedface0001", payload.WipCommitHash);
                    AssertTrue(payload.WipPushed, "payload records the push");
                    AssertEqual(fixture.Dock.Id, payload.DockId);
                }
                finally
                {
                    try { Directory.Delete(worktree, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("operator_stop_clean_worktree_makes_no_commit", "An operator stop with a clean worktree makes no WIP commit", TestTags.Negative, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Test.Shared.Infrastructure.StubGitService git = new Test.Shared.Infrastructure.StubGitService();
                StubDockService docks = new StubDockService();
                string worktree = Path.Combine(Path.GetTempPath(), "armada_test_wip_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(worktree);
                try
                {
                    CaptainService service = new CaptainService(CreateLogging(), db.Driver, CreateSettings(), git, docks);
                    StopFixture fixture = await CreateStopFixtureAsync(db, worktree, "Worker").ConfigureAwait(false);

                    await service.RecallAsync(fixture.Captain.Id).ConfigureAwait(false);

                    Mission stopped = (await db.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.Cancelled, stopped.Status);
                    AssertEqual(0, git.CommitAllCalls.Count, "nothing to preserve");
                    AssertEqual(0, git.PushCalls.Count, "nothing to push");
                    AssertFalse((stopped.FailureReason ?? "").Contains("WIP", StringComparison.Ordinal), "no WIP note: " + stopped.FailureReason);
                    AssertEqual(1, docks.ReclaimCalls);
                }
                finally
                {
                    try { Directory.Delete(worktree, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("operator_stop_push_failure_still_stops", "A WIP push failure is recorded and never blocks the stop", TestTags.Negative, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Test.Shared.Infrastructure.StubGitService git = new Test.Shared.Infrastructure.StubGitService();
                git.WorkingTreeStatusResult = new GitWorkingTreeStatus { UntrackedCount = 1 };
                git.ShouldThrowOnPush = true;
                StubDockService docks = new StubDockService();
                string worktree = Path.Combine(Path.GetTempPath(), "armada_test_wip_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(worktree);
                try
                {
                    CaptainService service = new CaptainService(CreateLogging(), db.Driver, CreateSettings(), git, docks);
                    StopFixture fixture = await CreateStopFixtureAsync(db, worktree, "Worker").ConfigureAwait(false);

                    await service.RecallAsync(fixture.Captain.Id).ConfigureAwait(false);

                    Mission stopped = (await db.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!;
                    Captain captain = (await db.Driver.Captains.ReadAsync(fixture.Captain.Id).ConfigureAwait(false))!;
                    AssertEqual(MissionStatusEnum.Cancelled, stopped.Status);
                    AssertEqual(1, git.CommitAllCalls.Count);
                    AssertContains("(not pushed).", stopped.FailureReason ?? "");
                    AssertEqual(CaptainStateEnum.Idle, captain.State, "the captain is still released");
                    AssertEqual(1, docks.ReclaimCalls);
                }
                finally
                {
                    try { Directory.Delete(worktree, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("operator_stop_judge_makes_no_wip_commit", "A Judge's dock is not committed on stop", TestTags.Negative, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Test.Shared.Infrastructure.StubGitService git = new Test.Shared.Infrastructure.StubGitService();
                git.WorkingTreeStatusResult = new GitWorkingTreeStatus { ModifiedCount = 1 };
                StubDockService docks = new StubDockService();
                string worktree = Path.Combine(Path.GetTempPath(), "armada_test_wip_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(worktree);
                try
                {
                    CaptainService service = new CaptainService(CreateLogging(), db.Driver, CreateSettings(), git, docks);
                    StopFixture fixture = await CreateStopFixtureAsync(db, worktree, "Judge").ConfigureAwait(false);

                    await service.RecallAsync(fixture.Captain.Id).ConfigureAwait(false);

                    AssertEqual(0, git.CommitAllCalls.Count, "a Judge never changes the branch");
                    AssertEqual(MissionStatusEnum.Cancelled, (await db.Driver.Missions.ReadAsync(fixture.Mission.Id).ConfigureAwait(false))!.Status);
                }
                finally
                {
                    try { Directory.Delete(worktree, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("recall_leaves_already_cancelled_mission_alone", "Recalling the captain of an already cancelled mission keeps its status and reason", TestTags.Negative, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Test.Shared.Infrastructure.StubGitService git = new Test.Shared.Infrastructure.StubGitService();
                StubDockService docks = new StubDockService();
                CaptainService service = new CaptainService(CreateLogging(), db.Driver, CreateSettings(), git, docks);
                Mission mission = await db.Driver.Missions.CreateAsync(new Mission("halted") { Status = MissionStatusEnum.Cancelled, FailureReason = "Voyage halted", FailureKind = MissionFailureKindEnum.DependencyFailed }).ConfigureAwait(false);
                Captain captain = await db.Driver.Captains.CreateAsync(new Captain("halted") { State = CaptainStateEnum.Working, CurrentMissionId = mission.Id }).ConfigureAwait(false);

                await service.RecallAsync(captain.Id).ConfigureAwait(false);

                Mission after = (await db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertEqual("Voyage halted", after.FailureReason);
                AssertEqual(MissionFailureKindEnum.DependencyFailed, after.FailureKind);
                List<ArmadaEvent> events = await db.Driver.Events.EnumerateByMissionAsync(mission.Id).ConfigureAwait(false);
                AssertFalse(events.Exists(e => e.EventType == "mission.stopped_by_operator"), "no operator-stop event for a mission that was not running");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Captain Service",
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
            settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private static async Task AssertRecoveryRetainsDockAsync(MissionStatusEnum status)
        {
            // An earlier case in this process can delete the process temp directory. Create the
            // worktree first so the parent exists again before the database file is opened.
            string worktree = TestTemp.NewDirectory("recovery-worktree");
            string logs = TestTemp.NewDirectory("recovery-logs");
            string marker = Path.Combine(worktree, "landing-payload.txt");
            File.WriteAllText(marker, "landing payload");
            try
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.LogDirectory = logs;
                    Test.Shared.Infrastructure.StubGitService git = new Test.Shared.Infrastructure.StubGitService();
                    // InProgress stays on the relaunch path. Report the directory as not a repository
                    // so recovery fails that mission and reclaims its dock.
                    if (status == MissionStatusEnum.InProgress) git.IsRepositoryResult = false;
                    DockService docks = new DockService(logging, testDb.Driver, settings, git);
                    CaptainService service = new CaptainService(logging, testDb.Driver, settings, git, docks);
                    string suffix = status.ToString() + "-" + Guid.NewGuid().ToString("N");
                    Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("recovery-vessel-" + suffix, "https://example.invalid/repo.git")).ConfigureAwait(false);
                    Dock dock = await testDb.Driver.Docks.CreateAsync(new Dock(vessel.Id) { WorktreePath = worktree, Active = true }).ConfigureAwait(false);
                    Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("recovery-mission-" + suffix)
                    {
                        VesselId = vessel.Id,
                        DockId = dock.Id,
                        Status = status,
                        ProcessId = 739185
                    }).ConfigureAwait(false);
                    Captain captain = await testDb.Driver.Captains.CreateAsync(new Captain("recovery-captain-" + suffix)
                    {
                        State = CaptainStateEnum.Working,
                        ProcessId = mission.ProcessId,
                        CurrentMissionId = mission.Id,
                        CurrentDockId = dock.Id
                    }).ConfigureAwait(false);

                    await service.TryRecoverAsync(captain).ConfigureAwait(false);

                    Mission updatedMission = (await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                    Captain updatedCaptain = (await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                    Dock updatedDock = (await testDb.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false))!;
                    bool shouldRetain = status == MissionStatusEnum.WorkProduced
                        || status == MissionStatusEnum.PullRequestOpen
                        || status == MissionStatusEnum.LandingFailed;
                    MissionStatusEnum expectedStatus = status == MissionStatusEnum.InProgress
                        ? MissionStatusEnum.Failed
                        : status;
                    string statusDetail = status == MissionStatusEnum.InProgress
                        ? "Recovery fails an in-progress mission whose worktree is not a repository"
                        : "Recovery preserves the landing status";
                    AssertEqual(expectedStatus, updatedMission.Status, statusDetail);
                    AssertEqual(dock.Id, updatedMission.DockId, "Recovery keeps the mission dock reference and releases only the captain");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain.State, "Recovery releases the captain");
                    AssertNull(updatedCaptain.CurrentDockId, "A released captain no longer owns the retained dock");
                    AssertEqual(!shouldRetain, !updatedDock.Active, "Recovery reclaims only a dock no longer needed for landing");
                    AssertEqual(shouldRetain, Directory.Exists(worktree), "The landing worktree remains available");
                    AssertEqual(shouldRetain, File.Exists(marker), "The landing worktree contents remain available");
                }
            }
            finally
            {
                TestTemp.TryDelete(worktree);
                TestTemp.TryDelete(logs);
            }
        }

        private static async Task<StopFixture> CreateStopFixtureAsync(TestDatabase db, string worktree, string persona)
        {
            Vessel vessel = await db.Driver.Vessels.CreateAsync(new Vessel("stop-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
            Mission mission = await db.Driver.Missions.CreateAsync(new Mission("stop with work") { Status = MissionStatusEnum.InProgress, VesselId = vessel.Id, Persona = persona }).ConfigureAwait(false);
            Dock dock = new Dock(vessel.Id);
            dock.WorktreePath = worktree;
            dock.BranchName = "armada/stop/wip";
            dock.Active = true;
            dock = await db.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);
            Captain captain = await db.Driver.Captains.CreateAsync(new Captain("stopper")
            {
                State = CaptainStateEnum.Working, CurrentMissionId = mission.Id, CurrentDockId = dock.Id
            }).ConfigureAwait(false);
            mission.DockId = dock.Id;
            mission.CaptainId = captain.Id;
            await db.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
            return new StopFixture(captain, mission, dock);
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
        /// Dock service stub that records reclaim calls and throws for unused operations.
        /// </summary>
        private sealed class StopFixture
        {
            public Captain Captain { get; }
            public Mission Mission { get; }
            public Dock Dock { get; }

            public StopFixture(Captain captain, Mission mission, Dock dock)
            {
                Captain = captain;
                Mission = mission;
                Dock = dock;
            }
        }

        private sealed class StubDockService : IDockService
        {
            public int ReclaimCalls { get; private set; } = 0;

            public Action? OnReclaim { get; set; } = null;

            public Task<Dock?> ProvisionAsync(Vessel vessel, Captain captain, string branchName, string? missionId = null, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task<Dock> ProvisionOnHarborAsync(Vessel vessel, Captain captain, string branchName, string missionId, string harborId, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task<bool> ReclaimAsync(string dockId, string? tenantId = null, CancellationToken token = default, string? forceReason = null, string caller = "")
            {
                OnReclaim?.Invoke();
                ReclaimCalls++;
                return Task.FromResult(true);
            }

            public Task RepairAsync(string dockId, string? tenantId = null, CancellationToken token = default)
            {
                return Task.CompletedTask;
            }

            public Task UnstickAsync(string dockId, string? tenantId = null, CancellationToken token = default)
            {
                ReclaimCalls++;
                return Task.CompletedTask;
            }

            public Task<bool> DeleteAsync(string dockId, string? tenantId = null, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task PurgeAsync(string dockId, string? tenantId = null, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Git service stub that records repository and repair probe counts and throws for
        /// unused operations. Verifies recovery does not destructively repair a healthy worktree.
        /// </summary>
        private sealed class StubGitService : IGitService
        {
            public bool IsRepositoryResult { get; set; }
            public int IsRepositoryCalls { get; private set; }
            public int RepairWorktreeCalls { get; private set; }

            public Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default) { throw new NotImplementedException(); }
            public Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task FetchAsync(string repoPath, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default)
            {
                RepairWorktreeCalls++;
                return Task.CompletedTask;
            }
            public Task<bool> IsRepositoryAsync(string path, CancellationToken token = default)
            {
                IsRepositoryCalls++;
                return Task.FromResult(IsRepositoryResult);
            }
            public Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task PruneWorktreesAsync(string repoPath, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task PullAsync(string workingDirectory, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<Armada.Core.Models.GitChangedFile>> GetBranchChangesAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(string worktreePath, string startCommit, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<string>> GetConflictedFilesAsync(string worktreePath, CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<bool> ForceAdvanceBranchAsync(string worktreePath, string branchName, string commitHash, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<string>> GetRecentCommitsForPathsAsync(string worktreePath, IReadOnlyList<string> paths, int maxPerPath, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<string>> FindExistingSubjectTermsAsync(string worktreePath, IReadOnlyList<string> terms, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<Armada.Core.Models.BranchInfo>> ListBranchesAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default) => Task.FromResult<IReadOnlyList<Armada.Core.Models.BranchInfo>>(new List<Armada.Core.Models.BranchInfo>());
            public Task<IReadOnlyList<Armada.Core.Models.BranchInfo>> ListBranchTipsAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default) => Task.FromResult<IReadOnlyList<Armada.Core.Models.BranchInfo>>(new List<Armada.Core.Models.BranchInfo>());
            public Task MeasureBranchDivergenceAsync(string repoPath, string defaultBranch, IReadOnlyList<Armada.Core.Models.BranchInfo> branches, CancellationToken token = default) => Task.CompletedTask;
            public Task FetchRemotesAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;
            public Task<Armada.Core.Models.GitDivergenceCounts?> GetDivergenceAsync(string repoPath, string baseRef, string headRef, CancellationToken token = default) => Task.FromResult<Armada.Core.Models.GitDivergenceCounts?>(null);
            public Task<bool?> IsAncestorAsync(string repoPath, string ancestorRef, string descendantRef, CancellationToken token = default) => Task.FromResult<bool?>(null);
            public Task<Armada.Core.Models.GitWorkingTreeStatus> GetWorkingTreeStatusAsync(string repoPath, CancellationToken token = default) => Task.FromResult(new Armada.Core.Models.GitWorkingTreeStatus());
            public Task<string?> GetCurrentBranchAsync(string repoPath, CancellationToken token = default) => Task.FromResult<string?>(null);
            public Task<DateTime?> GetLastCommitUtcAsync(string repoPath, CancellationToken token = default) => Task.FromResult<DateTime?>(null);
            public Task<IReadOnlyList<string>> ListTrackedFilesAsync(string repoPath, CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
            public Task<bool> IsBareRepositoryAsync(string path, CancellationToken token = default) => Task.FromResult(false);
            public Task<string?> ResolveBranchTipAsync(string repoPath, string branch, CancellationToken token = default) => Task.FromResult<string?>(null);
            public Task<VesselCommitActivity> GetCommitActivityAsync(string repoPath, string branch, DateTime fromDate, DateTime toDate, int utcOffsetMinutes, CancellationToken token = default) => Task.FromResult(new VesselCommitActivity { Branch = branch });
            public Task<GitCommitLogPage> GetCommitLogAsync(string repoPath, string tipSha, long? untilUnixSeconds, int skip, int limit, CancellationToken token = default) => Task.FromResult(new GitCommitLogPage());
            public Task PushLocalBranchAsync(string repoPath, string branchName, string remoteName = "origin", CancellationToken token = default) => Task.CompletedTask;
            public Task MergeBranchesAsync(string repoPath, string sourceBranch, string targetBranch, bool push, CancellationToken token = default) => Task.CompletedTask;
        }

        #endregion
    }
}
