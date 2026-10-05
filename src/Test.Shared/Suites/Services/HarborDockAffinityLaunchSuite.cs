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
    /// pinned to a Harbor is only ever launched on that Harbor: while it is offline the launch is refused with
    /// <see cref="HarborLaunchUnavailableException"/> instead of falling back to the Admiral host, and a
    /// stall-recovery relaunch that hits that refusal spends its recovery attempt and fails the mission with a typed
    /// failure kind. With requireHarborForLaunch on, a launch never runs locally and never on a Harbor owned by
    /// another user.
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

            cases.Add(CaseAsync("pinned_dock_with_no_harbor_connected_is_not_launched_locally", "A dock pinned to a Harbor is not launched on the Admiral host when no Harbor is connected", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                await scenario.LinkAsync("hbr_pinned_a", "usr_a", new List<HarborLaunchRequest>()).ConfigureAwait(false);
                await scenario.Manager.OnDisconnectedAsync("hbr_pinned_a").ConfigureAwait(false);

                Dock dock = scenario.NewDock("hbr_pinned_a");
                HarborLaunchUnavailableException refused = await CatchRefusalAsync(
                    () => scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_a"), dock)).ConfigureAwait(false);

                AssertEqual("hbr_pinned_a", refused.PinnedHarborId);
                AssertFalse(refused.RequiredByPolicy, "refused by dock affinity, not by policy");
                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");
            }));

            cases.Add(CaseAsync("pinned_dock_is_not_moved_to_another_connected_harbor_or_local", "A dock pinned to an offline Harbor is neither moved to another connected Harbor nor run locally", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                List<HarborLaunchRequest> pinnedLaunches = new List<HarborLaunchRequest>();
                List<HarborLaunchRequest> otherLaunches = new List<HarborLaunchRequest>();
                await scenario.LinkAsync("hbr_pinned_b", "usr_b", pinnedLaunches).ConfigureAwait(false);
                await scenario.LinkAsync("hbr_other_b", "usr_b", otherLaunches).ConfigureAwait(false);
                await scenario.Manager.OnDisconnectedAsync("hbr_pinned_b").ConfigureAwait(false);

                await AssertThrowsAsync<HarborLaunchUnavailableException>(
                    () => scenario.Handler.HandleLaunchAgentAsync(scenario.Captain, scenario.NewMission("usr_b"), scenario.NewDock("hbr_pinned_b"))).ConfigureAwait(false);

                AssertEqual(0, scenario.LocalLaunches, "nothing ran on the Admiral host");
                AssertEqual(0, otherLaunches.Count, "the dock did not hop to another Harbor");
                AssertEqual(0, pinnedLaunches.Count, "the offline Harbor received nothing");
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

            cases.Add(CaseAsync("stall_recovery_on_offline_pinned_harbor_fails_typed", "A stall-recovery relaunch whose dock's Harbor is offline spends the attempt and fails the mission as StallRecoveryExhausted", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using Scenario scenario = new Scenario(testDb);
                await scenario.LinkAsync("hbr_pinned_g", "usr_g", new List<HarborLaunchRequest>()).ConfigureAwait(false);
                await scenario.Manager.OnDisconnectedAsync("hbr_pinned_g").ConfigureAwait(false);

                DirCreatingGitService git = new DirCreatingGitService();
                IDockService docks = new DockService(scenario.Logging, testDb.Driver, scenario.Settings, git);
                CaptainService captains = new CaptainService(scenario.Logging, testDb.Driver, scenario.Settings, git, docks);
                captains.OnLaunchAgent = scenario.Handler.HandleLaunchAgentAsync;

                Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("affinity-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
                Captain captain = new Captain("affinity-captain", AgentRuntimeEnum.ClaudeCode);
                captain.State = CaptainStateEnum.Working;
                captain.CurrentMissionId = "msn_affinity_recover";
                captain.CurrentDockId = "dck_affinity_recover";
                captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                Dock dock = new Dock(vessel.Id);
                dock.Id = "dck_affinity_recover";
                dock.CaptainId = captain.Id;
                dock.HarborId = "hbr_pinned_g";
                dock.WorktreePath = scenario.WorktreePath;
                dock.BranchName = "armada/affinity";
                dock.Active = true;
                await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                Mission mission = new Mission("Affinity recovery mission");
                mission.Id = "msn_affinity_recover";
                mission.VesselId = vessel.Id;
                mission.CaptainId = captain.Id;
                mission.DockId = "dck_affinity_recover";
                mission.Status = MissionStatusEnum.InProgress;
                mission.ProcessId = 4242;
                await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                await captains.TryRecoverAsync(captain).ConfigureAwait(false);

                Mission? updated = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                Captain? released = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                AssertEqual(1, captain.RecoveryAttempts, "the relaunch spent a recovery attempt");
                AssertEqual(MissionStatusEnum.Failed, updated!.Status, "the mission ends in a typed failure");
                AssertEqual(MissionFailureKindEnum.StallRecoveryExhausted, updated.FailureKind);
                AssertEqual(CaptainStateEnum.Idle, released!.State, "the captain is released");
                AssertEqual(0, scenario.LocalLaunches, "the relaunch never ran on the Admiral host");
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
        /// A lifecycle handler wired to a real Harbor connection manager, with a local runtime that counts every
        /// launch that ran on the Admiral host.
        /// </summary>
        private sealed class Scenario : IDisposable
        {
            public LoggingModule Logging { get; }

            public ArmadaSettings Settings { get; }

            public HarborConnectionManager Manager { get; }

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
                CaptainService captains = new CaptainService(Logging, db.Driver, Settings, git, docks);
                MissionService missions = new MissionService(Logging, db.Driver, Settings, docks, captains, git: git);
                AdmiralService admiral = new AdmiralService(Logging, db.Driver, Settings, captains, missions, new VoyageService(Logging, db.Driver), docks);

                Handler = new AgentLifecycleHandler(
                    Logging, db.Driver, Settings, runtimeFactory, admiral, new MessageTemplateService(Logging), null, null,
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
                Handler.SetHarborConnections(Manager);
            }

            public async Task LinkAsync(string harborId, string userId, List<HarborLaunchRequest> launches)
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
