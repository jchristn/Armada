namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Runtimes;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Harbor dock affinity and the requireHarborForLaunch policy at launch time (experimental split mode). A dock
    /// pinned to a Harbor relaunches on that Harbor while it is connected; with the policy off and that Harbor
    /// unavailable it falls back to the Admiral host (the stall-detection recovery accepted for 1.0), never to another
    /// Harbor. With requireHarborForLaunch on, a launch never runs locally or on another user's Harbor; a refused
    /// stall-recovery relaunch spends one recovery attempt and the next stall check retries it, so a Harbor that
    /// reconnects gets the mission back, and the mission fails as StallRecoveryExhausted once the attempts run out.
    /// </summary>
    public sealed class HarborDockAffinityLaunchSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborDockAffinityLaunch";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("pinned_dock_with_no_harbor_connected_falls_back_locally_without_policy", "With requireHarborForLaunch off, a dock pinned to a Harbor runs on the Admiral host when no Harbor is connected", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                await scenario.LinkAsync("hbr_pinned_a", "usr_a", new List<HarborLaunchRequest>()).ConfigureAwait(false);
                await scenario.Manager.OnDisconnectedAsync("hbr_pinned_a").ConfigureAwait(false);

                int processId = await scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_a"), scenario.NewDock("hbr_pinned_a")).ConfigureAwait(false);

                AssertTrue(processId > 0, "the local runtime reported a process id");
                AssertEqual(1, scenario.LocalLaunches, "stall-detection recovery runs on the Admiral host");
            }));

            cases.Add(CaseAsync("pinned_dock_is_not_moved_to_another_connected_harbor", "With requireHarborForLaunch off, a dock pinned to an offline Harbor falls back to the Admiral host, never to another connected Harbor", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                List<HarborLaunchRequest> pinnedLaunches = new List<HarborLaunchRequest>();
                List<HarborLaunchRequest> otherLaunches = new List<HarborLaunchRequest>();
                await scenario.LinkAsync("hbr_pinned_b", "usr_b", pinnedLaunches).ConfigureAwait(false);
                await scenario.LinkAsync("hbr_other_b", "usr_b", otherLaunches).ConfigureAwait(false);
                await scenario.Manager.OnDisconnectedAsync("hbr_pinned_b").ConfigureAwait(false);

                await scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_b"), scenario.NewDock("hbr_pinned_b")).ConfigureAwait(false);

                AssertEqual(1, scenario.LocalLaunches, "ran on the Admiral host");
                AssertEqual(0, otherLaunches.Count, "the dock did not hop to another Harbor");
                AssertEqual(0, pinnedLaunches.Count, "the offline Harbor received nothing");
            }));

            cases.Add(CaseAsync("pinned_dock_on_offline_harbor_is_refused_under_policy", "With requireHarborForLaunch on, a dock pinned to an offline Harbor is refused instead of running locally", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                scenario.Settings.RequireHarborForLaunch = true;
                await scenario.LinkAsync("hbr_pinned_h", "usr_h", new List<HarborLaunchRequest>()).ConfigureAwait(false);
                await scenario.Manager.OnDisconnectedAsync("hbr_pinned_h").ConfigureAwait(false);

                HarborLaunchUnavailableException refused = await CatchRefusalAsync(
                    () => scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_h"), scenario.NewDock("hbr_pinned_h"))).ConfigureAwait(false);

                AssertEqual("hbr_pinned_h", refused.PinnedHarborId);
                AssertTrue(refused.RequiredByPolicy, "refused by policy");
                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");
            }));

            cases.Add(CaseAsync("pinned_dock_relaunches_on_its_harbor_when_connected", "A dock pinned to a connected Harbor launches on that Harbor", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                List<HarborLaunchRequest> pinnedLaunches = new List<HarborLaunchRequest>();
                List<HarborLaunchRequest> otherLaunches = new List<HarborLaunchRequest>();
                await scenario.LinkAsync("hbr_pinned_c", "usr_c", pinnedLaunches).ConfigureAwait(false);
                await scenario.LinkAsync("hbr_other_c", "usr_c", otherLaunches).ConfigureAwait(false);

                Dock dock = scenario.NewDock("hbr_pinned_c");
                int processId = await scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_c"), dock).ConfigureAwait(false);

                AssertTrue(processId > 0, "the Harbor reported a process id");
                AssertEqual(1, pinnedLaunches.Count, "launched on the dock's Harbor");
                AssertEqual(dock.WorktreePath, pinnedLaunches[0].WorkingDirectory);
                AssertEqual(0, otherLaunches.Count, "no other Harbor was used");
                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");
            }));

            cases.Add(CaseAsync("unpinned_launch_without_policy_still_runs_locally", "Without a pinned dock or requireHarborForLaunch, a launch with no Harbor runs locally as before", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);

                int processId = await scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_d"), scenario.NewDock(null)).ConfigureAwait(false);

                AssertTrue(processId > 0, "the local runtime reported a process id");
                AssertEqual(1, scenario.LocalLaunches, "ran on the Admiral host");
            }));

            cases.Add(CaseAsync("require_harbor_for_launch_never_falls_back_to_local", "With requireHarborForLaunch on and no Harbor connected, a launch is refused rather than run locally", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                scenario.Settings.RequireHarborForLaunch = true;

                HarborLaunchUnavailableException refused = await CatchRefusalAsync(
                    () => scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_e"), scenario.NewDock(null))).ConfigureAwait(false);

                AssertTrue(refused.RequiredByPolicy, "refused by policy");
                AssertNull(refused.PinnedHarborId, "the dock was not pinned");
                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");
            }));

            cases.Add(CaseAsync("require_harbor_for_launch_routes_only_to_the_owners_harbor", "With requireHarborForLaunch on, a launch goes to the mission user's Harbor and never to another user's", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                scenario.Settings.RequireHarborForLaunch = true;
                List<HarborLaunchRequest> foreignLaunches = new List<HarborLaunchRequest>();
                List<HarborLaunchRequest> ownerLaunches = new List<HarborLaunchRequest>();

                // Only another user's Harbor is connected: the launch is refused.
                await scenario.LinkAsync("hbr_aaa_foreign", "usr_someone_else", foreignLaunches).ConfigureAwait(false);
                await AssertThrowsAsync<HarborLaunchUnavailableException>(
                    () => scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_f"), scenario.NewDock(null))).ConfigureAwait(false);
                AssertEqual(0, foreignLaunches.Count, "another user's Harbor was not used");

                // The owner's Harbor connects (it sorts after the foreign one, so plain routing would pick the foreign one).
                await scenario.LinkAsync("hbr_zzz_owner", "usr_f", ownerLaunches).ConfigureAwait(false);
                await scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_f"), scenario.NewDock(null)).ConfigureAwait(false);

                AssertEqual(1, ownerLaunches.Count, "launched on the owner's Harbor");
                AssertEqual(0, foreignLaunches.Count, "another user's Harbor was never used");
                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");
            }));

            cases.Add(CaseAsync("refused_recovery_spends_one_attempt_and_resumes_when_the_harbor_returns", "Under requireHarborForLaunch, a refused stall-recovery relaunch spends one attempt, and the next stall check relaunches on the reconnected Harbor", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                scenario.Settings.RequireHarborForLaunch = true;
                List<HarborLaunchRequest> launches = new List<HarborLaunchRequest>();
                await scenario.LinkAsync("hbr_pinned_g", null, launches).ConfigureAwait(false);
                await scenario.Manager.OnDisconnectedAsync("hbr_pinned_g").ConfigureAwait(false);
                RecoveryRecords records = await scenario.CreateStalledMissionAsync("hbr_pinned_g").ConfigureAwait(false);

                await scenario.Captains.TryRecoverAsync(records.Captain).ConfigureAwait(false);

                Mission? waiting = await testDb.Driver.Missions.ReadAsync(records.Mission.Id).ConfigureAwait(false);
                Captain? waitingCaptain = await testDb.Driver.Captains.ReadAsync(records.Captain.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.InProgress, waiting!.Status, "the mission keeps waiting for its Harbor");
                AssertNull(waiting.ProcessId, "no process while waiting");
                AssertEqual(CaptainStateEnum.Working, waitingCaptain!.State, "the captain stays on the mission");
                AssertEqual(1, waitingCaptain.RecoveryAttempts, "one recovery attempt spent");
                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");

                // The Harbor reconnects; once the stall threshold passes, the health check retries recovery on it.
                await scenario.LinkAsync("hbr_pinned_g", null, launches).ConfigureAwait(false);
                await scenario.AgeHeartbeatAsync(records.Captain.Id).ConfigureAwait(false);
                await scenario.Admiral.HealthCheckAsync().ConfigureAwait(false);

                Mission? resumed = await testDb.Driver.Missions.ReadAsync(records.Mission.Id).ConfigureAwait(false);
                Captain? resumedCaptain = await testDb.Driver.Captains.ReadAsync(records.Captain.Id).ConfigureAwait(false);
                AssertEqual(1, launches.Count, "relaunched on the reconnected Harbor");
                AssertEqual(MissionStatusEnum.InProgress, resumed!.Status);
                AssertNotNull(resumed.ProcessId, "the relaunch recorded its process");
                AssertEqual(2, resumedCaptain!.RecoveryAttempts, "the successful relaunch was the second attempt");
                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");
            }));

            cases.Add(CaseAsync("refused_recovery_fails_typed_when_attempts_run_out", "Under requireHarborForLaunch, refused stall-recovery relaunches end in StallRecoveryExhausted once the attempts run out", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                scenario.Settings.RequireHarborForLaunch = true;
                scenario.Settings.MaxRecoveryAttempts = 2;
                await scenario.LinkAsync("hbr_pinned_x", null, new List<HarborLaunchRequest>()).ConfigureAwait(false);
                await scenario.Manager.OnDisconnectedAsync("hbr_pinned_x").ConfigureAwait(false);
                RecoveryRecords records = await scenario.CreateStalledMissionAsync("hbr_pinned_x").ConfigureAwait(false);

                await scenario.Captains.TryRecoverAsync(records.Captain).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.InProgress, (await testDb.Driver.Missions.ReadAsync(records.Mission.Id).ConfigureAwait(false))!.Status, "first refusal waits");

                await scenario.AgeHeartbeatAsync(records.Captain.Id).ConfigureAwait(false);
                await scenario.Admiral.HealthCheckAsync().ConfigureAwait(false);

                Mission? failed = await testDb.Driver.Missions.ReadAsync(records.Mission.Id).ConfigureAwait(false);
                Captain? released = await testDb.Driver.Captains.ReadAsync(records.Captain.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Failed, failed!.Status, "the mission ends in a typed failure");
                AssertEqual(MissionFailureKindEnum.StallRecoveryExhausted, failed.FailureKind);
                AssertEqual(CaptainStateEnum.Idle, released!.State, "the captain is released");
                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Harbor dock affinity and requireHarborForLaunch at launch (experimental split mode)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<HarborLaunchUnavailableException> CatchRefusalAsync(Func<Task<int>> launch)
        {
            try
            {
                await launch().ConfigureAwait(false);
            }
            catch (HarborLaunchUnavailableException ex)
            {
                return ex;
            }

            throw new InvalidOperationException("Expected the launch to be refused with HarborLaunchUnavailableException, but it ran.");
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

        #region Nested-Types

        /// <summary>
        /// The captain and mission of a stalled mission whose dock is pinned to a Harbor.
        /// </summary>
        private sealed class RecoveryRecords
        {
            public Captain Captain { get; }

            public Mission Mission { get; }

            public RecoveryRecords(Captain captain, Mission mission)
            {
                Captain = captain;
                Mission = mission;
            }
        }

        /// <summary>
        /// A lifecycle handler wired to a real Harbor connection manager, with a local runtime that counts every
        /// launch that ran on the Admiral host.
        /// </summary>
        private sealed class Scenario : IDisposable
        {
            public LoggingModule Logging { get; }

            public ArmadaSettings Settings { get; }

            public HarborConnectionManager Manager { get; }

            public CaptainService Captains { get; }

            public AdmiralService Admiral { get; }

            public AgentLifecycleHandler Handler { get; }

            public Captain Captain { get; } = new Captain("affinity-launch-captain", AgentRuntimeEnum.ClaudeCode);

            public string WorktreePath { get; }

            public int LocalLaunches => _LocalLaunches;

            private readonly TestDatabase _Db;
            private int _LocalLaunches = 0;
            private int _NextHarborPid = 7000;

            public Scenario(TestDatabase db)
            {
                _Db = db;
                Logging = new LoggingModule();
                Logging.Settings.EnableConsole = false;
                Settings = new ArmadaSettings();
                Settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_affinity_logs_" + Guid.NewGuid().ToString("N"));
                Settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_affinity_docks_" + Guid.NewGuid().ToString("N"));
                Settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_affinity_repos_" + Guid.NewGuid().ToString("N"));
                WorktreePath = Path.Combine(Path.GetTempPath(), "armada_affinity_wt_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(WorktreePath);

                StubCaptainBehavior behavior = new StubCaptainBehavior();
                AgentRuntimeFactory runtimeFactory = new AgentRuntimeFactory(Logging);
                runtimeFactory.Override(AgentRuntimeEnum.ClaudeCode, () =>
                {
                    Interlocked.Increment(ref _LocalLaunches);
                    return new StubCaptainRuntime(Logging, behavior);
                });

                Manager = new HarborConnectionManager(new HarborService(db.Driver, Logging), Logging, null);
                DirCreatingGitService git = new DirCreatingGitService();
                IDockService docks = new DockService(Logging, db.Driver, Settings, git);
                Captains = new CaptainService(Logging, db.Driver, Settings, git, docks);
                MissionService missions = new MissionService(Logging, db.Driver, Settings, docks, Captains, git: git);
                Admiral = new AdmiralService(Logging, db.Driver, Settings, Captains, missions, new VoyageService(Logging, db.Driver), docks);

                Handler = new AgentLifecycleHandler(
                    Logging, db.Driver, Settings, runtimeFactory, Admiral, new MessageTemplateService(Logging), null, null,
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
                Handler.SetHarborConnections(Manager);
                Captains.OnLaunchAgent = Handler.HandleLaunchAgentAsync;
            }

            public async Task<RecoveryRecords> CreateStalledMissionAsync(string harborId)
            {
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                Vessel vessel = await _Db.Driver.Vessels.CreateAsync(new Vessel("affinity-vessel-" + suffix, "https://github.com/test/repo.git")).ConfigureAwait(false);
                Captain captain = new Captain("affinity-captain-" + suffix, AgentRuntimeEnum.ClaudeCode);
                captain.State = CaptainStateEnum.Working;
                captain.CurrentMissionId = "msn_affinity_" + suffix;
                captain.CurrentDockId = "dck_affinity_" + suffix;
                captain.ProcessId = 4242;
                captain = await _Db.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                Dock dock = new Dock(vessel.Id);
                dock.Id = "dck_affinity_" + suffix;
                dock.CaptainId = captain.Id;
                dock.HarborId = harborId;
                dock.WorktreePath = WorktreePath;
                dock.BranchName = "armada/affinity";
                dock.Active = true;
                await _Db.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                Mission mission = new Mission("Affinity recovery mission");
                mission.Id = "msn_affinity_" + suffix;
                mission.VesselId = vessel.Id;
                mission.CaptainId = captain.Id;
                mission.DockId = dock.Id;
                mission.Status = MissionStatusEnum.InProgress;
                mission.ProcessId = 4242;
                mission = await _Db.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                return new RecoveryRecords(captain, mission);
            }

            public async Task AgeHeartbeatAsync(string captainId)
            {
                Captain? captain = await _Db.Driver.Captains.ReadAsync(captainId).ConfigureAwait(false);
                captain!.LastHeartbeatUtc = DateTime.UtcNow.AddMinutes(-(Settings.StallThresholdMinutes + 5));
                await _Db.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);
            }

            public async Task LinkAsync(string harborId, string? userId, List<HarborLaunchRequest> launches)
            {
                HarborSendDelegate send = async (message, token) =>
                {
                    if (message is HarborLaunchRequest launch)
                    {
                        lock (launches) launches.Add(launch);
                        int pid = Interlocked.Increment(ref _NextHarborPid);
                        await Manager.OnMessageAsync(harborId, new HarborStarted { JobId = launch.JobId, ProcessId = pid }, token).ConfigureAwait(false);
                    }
                };

                HarborHandshake handshake = new HarborHandshake
                {
                    HarborId = harborId,
                    Name = harborId,
                    ProtocolVersion = HarborProtocol.Version,
                    MaxConcurrentJobs = 4,
                    Capabilities = new List<HarborCapability> { new HarborCapability { Name = "ClaudeCode", Available = true } }
                };

                HarborHandshakeAck ack = await Manager.OnHandshakeAsync(handshake, null, userId, send).ConfigureAwait(false);
                AssertTrue(ack.Accepted, "Harbor " + harborId + " linked");
            }

            public Mission NewMission(string userId)
            {
                return new Mission("Affinity mission") { UserId = userId, BranchName = "armada/affinity" };
            }

            public Dock NewDock(string? harborId)
            {
                return new Dock { BranchName = "armada/affinity", WorktreePath = WorktreePath, HarborId = harborId };
            }

            public void Dispose()
            {
                try { Directory.Delete(WorktreePath, true); } catch { }
                try { Directory.Delete(Settings.LogDirectory, true); } catch { }
            }
        }

        #endregion
    }
}
