namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
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
    /// Harbor-hosted mission docks across the Admiral and real in-process Harbors linked over the Harbor protocol, with real
    /// git repositories: routing picks a Harbor that can serve the vessel before the dock is created (skipping one that
    /// cannot, waiting or using the Admiral host when none can); every landing mode runs on the Harbor against a real bare
    /// origin (LocalMerge and MergeAndPush land into the user's checkout there, PullRequest pushes and runs gh there, and a
    /// checkout with uncommitted changes is refused); reclaim removes the dock on the Harbor. The end-to-end case runs a
    /// whole mission with the Admiral's data folder and the Harbor's docks folder apart and checks that no Admiral-side
    /// path is used.
    /// </summary>
    public sealed class HarborDockLifecycleSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.HarborDockLifecycle";
        private const string? UserId = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("routing_skips_harbor_that_cannot_serve_vessel", "Routing asks each eligible Harbor and places the dock on the one that can serve the vessel", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor without = await s.ConnectAsync("hbr_a_without", new HarborDockSettings { DocksDirectory = git.HarborDocks, ReposDirectory = git.HarborRepos }).ConfigureAwait(false);
                HarborDockSettings mapped = git.Settings.Clone();
                mapped.Repositories.Add(new HarborRepositoryMapping("app", git.Checkout));
                using InProcessHarbor with = await s.ConnectAsync("hbr_b_with", mapped).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(mission, captain, vessel).ConfigureAwait(false);

                AssertFalse(placement.Wait, placement.Reason);
                AssertEqual("hbr_b_with", placement.HarborId, "the Harbor with a checkout of the vessel");
            }));

            cases.Add(CaseAsync("routing_waits_when_required_and_none_can_serve", "With requireHarborForLaunch on and no Harbor able to serve the vessel the mission waits, and the reason says what to set", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                s.Settings.RequireHarborForLaunch = true;
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_none", git.Settings.Clone()).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(mission, captain, vessel).ConfigureAwait(false);
                bool assigned = await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false);

                AssertTrue(placement.Wait, "the mission waits");
                AssertNull(placement.HarborId);
                AssertContains("hbr_none", placement.Reason, "the reason names the Harbor");
                AssertContains(vessel.Name, placement.Reason, "the reason names the vessel");
                AssertContains("Harbor > Settings > Repositories", placement.Reason, "the reason says what to set");
                AssertFalse(assigned, "nothing was assigned");
                Mission? stored = await s.Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, stored!.Status);
                AssertEqual(0, (await s.Db.Driver.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false)).Count, "no dock was created anywhere");
            }));

            cases.Add(CaseAsync("routing_uses_admiral_when_none_can_serve_without_policy", "With requireHarborForLaunch off and no Harbor able to serve the vessel the dock goes on the Admiral host", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_cannot", git.Settings.Clone()).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(await s.CreateMissionAsync(vessel).ConfigureAwait(false), await s.CreateCaptainAsync().ConfigureAwait(false), vessel).ConfigureAwait(false);

                AssertFalse(placement.Wait, "no policy requires a Harbor");
                AssertNull(placement.HarborId, "the Admiral host");
            }));

            cases.Add(CaseAsync("older_harbor_keeps_docks_on_admiral", "A Harbor that does not create docks is never asked; the dock goes on the Admiral host as before", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor older = await s.ConnectAsync("hbr_older", null).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);

                DockPlacement placement = await s.Handler.ResolveDockPlacementAsync(await s.CreateMissionAsync(vessel).ConfigureAwait(false), await s.CreateCaptainAsync().ConfigureAwait(false), vessel).ConfigureAwait(false);

                AssertFalse(placement.Wait, placement.Reason);
                AssertNull(placement.HarborId, "the Admiral host");
                AssertFalse(s.Manager.HostsDocks("hbr_older"), "it did not advertise Harbor-side docks");
            }));

            cases.Add(CaseAsync("local_merge_lands_into_harbor_checkout", "LocalMerge on a Harbor dock merges into the user's checkout on the Harbor and does not push", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_local").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_land_local", LandingModeEnum.LocalMerge).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.Complete, run.Mission.Status, run.Mission.FailureReason);
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Checkout, "refs/heads/main", "landed.txt").ConfigureAwait(false), "merged into the checkout's main");
                AssertFalse(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "LocalMerge does not push");
                AssertNotNull(run.Mission.DiffSnapshot, "the diff was captured on the Harbor");
                AssertEqual(run.WorkCommit, run.Mission.CommitHash, "the commit was captured on the Harbor");
            }));

            cases.Add(CaseAsync("merge_and_push_pushes_from_harbor_checkout", "MergeAndPush on a Harbor dock merges into the checkout and pushes it to the origin", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_push").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_land_push", LandingModeEnum.MergeAndPush).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.Complete, run.Mission.Status, run.Mission.FailureReason);
                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Origin, "refs/heads/main", "landed.txt").ConfigureAwait(false), "pushed to the origin");
                AssertNull(await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the mission branch was cleaned up in the user's repository");
            }));

            cases.Add(CaseAsync("pull_request_pushes_and_runs_gh_on_harbor", "PullRequest on a Harbor dock pushes the branch and opens the pull request from the Harbor", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_pr").ConfigureAwait(false);
                LandingRun run = await s.RunLandingAsync("hbr_land_pr", LandingModeEnum.PullRequest).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.PullRequestOpen, run.Mission.Status, run.Mission.FailureReason);
                AssertEqual(FakeGhHostCommandExecutor.PullRequestUrl, run.Mission.PrUrl);
                AssertEqual(run.WorkCommit, await HarborDockGitFixture.ResolveAsync(git.Origin, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the branch was pushed from the Harbor dock");
                List<HostCommandRequest> gh = s.Gh.GhCalls();
                AssertTrue(gh.Count > 0, "gh ran on the Harbor");
                AssertEqual(run.Dock.WorktreePath, gh[0].WorkingDirectory, "gh ran in the Harbor dock");
                AssertFalse(await HarborDockGitFixture.TreeHasFileAsync(git.Checkout, "refs/heads/main", "landed.txt").ConfigureAwait(false), "nothing was merged locally");
            }));

            cases.Add(CaseAsync("local_merge_refuses_dirty_checkout", "LocalMerge into a Harbor checkout with uncommitted changes is refused and the changes are kept", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_land_dirty").ConfigureAwait(false);
                string mainBefore = (await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/main").ConfigureAwait(false))!;
                await File.WriteAllTextAsync(Path.Combine(git.Checkout, "README.md"), "# app, uncommitted\n").ConfigureAwait(false);

                LandingRun run = await s.RunLandingAsync("hbr_land_dirty", LandingModeEnum.LocalMerge).ConfigureAwait(false);

                AssertEqual(MissionStatusEnum.LandingFailed, run.Mission.Status);
                AssertEqual(mainBefore, await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/main").ConfigureAwait(false), "main was not moved");
                AssertEqual("# app, uncommitted\n", await File.ReadAllTextAsync(Path.Combine(git.Checkout, "README.md")).ConfigureAwait(false), "the user's change is kept");
                AssertNotNull(await HarborDockGitFixture.ResolveAsync(git.Checkout, "refs/heads/" + run.Dock.BranchName).ConfigureAwait(false), "the mission branch is kept for a retry");
            }));

            cases.Add(CaseAsync("reclaim_removes_dock_on_harbor", "Reclaiming a Harbor dock removes its worktree on the Harbor and releases the dock", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectMappedAsync("hbr_reclaim").ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);
                Dock dock = await s.Docks.ProvisionOnHarborAsync(vessel, captain, "armada/reclaim", "msn_reclaim", "hbr_reclaim").ConfigureAwait(false);
                AssertTrue(Directory.Exists(dock.WorktreePath!), "created on the Harbor");

                await s.Docks.ReclaimAsync(dock.Id).ConfigureAwait(false);

                AssertFalse(Directory.Exists(dock.WorktreePath!), "removed on the Harbor");
                AssertFalse((await HarborDockGitFixture.WorktreesAsync(git.Checkout).ConfigureAwait(false)).Contains(PathCanonicalizer.Canonicalize(dock.WorktreePath!)), "pruned from the user's repository");
                Dock? stored = await s.Db.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false);
                AssertFalse(stored!.Active, "the dock was released");
            }));

            cases.Add(CaseAsync("provision_failure_names_harbor_and_setting", "A Harbor that cannot create the dock fails provisioning with a reason naming the Harbor, the vessel, and the setting", TestTags.Negative, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_fail", git.Settings.Clone()).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(null, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                Captain captain = await s.CreateCaptainAsync().ConfigureAwait(false);

                DockProvisioningException failure = await CatchAsync(() => s.Docks.ProvisionOnHarborAsync(vessel, captain, "armada/x", "msn_x", "hbr_fail")).ConfigureAwait(false);

                AssertEqual(vessel.Id, failure.VesselId);
                AssertContains("hbr_fail", failure.Message);
                AssertContains(vessel.Name, failure.Message);
                AssertContains("Harbor > Settings > Repositories", failure.Message);
            }));

            cases.Add(CaseAsync("mission_lifecycle_runs_on_harbor_e2e", "A whole mission runs on the Harbor: dock, instructions, launch, landing, and reclaim, with no Admiral-side path used", TestTags.Positive, async () =>
            {
                using HarborDockGitFixture git = await HarborDockGitFixture.CreateAsync().ConfigureAwait(false);
                using Scenario s = await Scenario.CreateAsync(git).ConfigureAwait(false);
                HarborDockSettings discovered = git.Settings.Clone();
                discovered.RepositoryRoots.Add(git.CodeRoot);
                using InProcessHarbor harbor = await s.ConnectAsync("hbr_e2e_docks", discovered).ConfigureAwait(false);
                Vessel vessel = await s.CreateVesselAsync(git.Origin, LandingModeEnum.LocalMerge).ConfigureAwait(false);
                await s.CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await s.CreateMissionAsync(vessel).ConfigureAwait(false);

                bool assigned = await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false);
                AssertTrue(assigned, "the mission was assigned");

                Mission? done = null;
                bool finished = await WaitUntilAsync(async () =>
                {
                    done = await s.Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    return done != null && (done.Status == MissionStatusEnum.Complete || done.Status == MissionStatusEnum.Failed || done.Status == MissionStatusEnum.LandingFailed);
                }).ConfigureAwait(false);
                AssertTrue(finished, "the mission finished");
                AssertEqual(MissionStatusEnum.Complete, done!.Status, done.FailureReason);

                // The dock is reclaimed after landing marks the mission Complete.
                List<Dock> docks = new List<Dock>();
                bool released = await WaitUntilAsync(async () =>
                {
                    docks = await s.Db.Driver.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
                    return docks.Count == 1 && !docks[0].Active;
                }).ConfigureAwait(false);
                AssertEqual(1, docks.Count, "one dock");
                AssertTrue(released, "the dock was released");
                Dock dock = docks[0];
                AssertEqual("hbr_e2e_docks", dock.HarborId);
                AssertEqual(PathCanonicalizer.Canonicalize(git.Checkout), PathCanonicalizer.Canonicalize(dock.CheckoutPath!), "the discovered checkout");
                AssertTrue(HarborDockSettings.IsSameOrUnder(dock.WorktreePath!, git.HarborDocks), "the dock was in the Harbor's docks folder");
                AssertFalse(HarborDockSettings.IsSameOrUnder(dock.WorktreePath!, git.AdmiralData), "not in the Admiral's data folder");

                List<HarborLaunchRequest> launches = s.Runner.LaunchSnapshot();
                AssertEqual(1, launches.Count, "the captain ran on the Harbor");
                AssertEqual(dock.WorktreePath, launches[0].WorkingDirectory, "in the Harbor dock");
                AssertFalse(launches[0].ScratchWorkingDirectory, "never in a scratch directory");

                AssertTrue(await HarborDockGitFixture.TreeHasFileAsync(git.Checkout, "refs/heads/main", CommittingHarborJobRunner.WorkFile).ConfigureAwait(false), "landed into the user's checkout");
                AssertFalse(Directory.Exists(dock.WorktreePath!), "the dock was reclaimed on the Harbor");
                string exclude = await File.ReadAllTextAsync(Path.Combine(git.Checkout, ".git", "info", "exclude")).ConfigureAwait(false);
                AssertTrue(new List<string>(exclude.Split('\n')).Contains("CLAUDE.md"), "the instruction file was excluded in the repository the dock belonged to");

                AssertFalse(Directory.Exists(s.Settings.DocksDirectory), "no dock folder on the Admiral");
                AssertFalse(Directory.Exists(s.Settings.ReposDirectory), "no clone on the Admiral");
                Vessel? storedVessel = await s.Db.Driver.Vessels.ReadAsync(vessel.Id).ConfigureAwait(false);
                AssertNull(storedVessel!.LocalPath, "the vessel has no Admiral-side repository");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Harbor Dock Lifecycle", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<DockProvisioningException> CatchAsync(Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (DockProvisioningException ex)
            {
                return ex;
            }

            throw new AssertionException("Expected DockProvisioningException");
        }

        private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
        {
            MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(60));
            while (!deadline.Passed)
            {
                if (await condition().ConfigureAwait(false)) return true;
                await Task.Delay(50).ConfigureAwait(false);
            }

            return await condition().ConfigureAwait(false);
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

        private sealed class LandingRun
        {
            public Mission Mission { get; }

            public Dock Dock { get; }

            public string WorkCommit { get; }

            public LandingRun(Mission mission, Dock dock, string workCommit)
            {
                Mission = mission;
                Dock = dock;
                WorkCommit = workCommit;
            }
        }

        private sealed class Scenario : IDisposable
        {
            public TestDatabase Db { get; }

            public LoggingModule Logging { get; }

            public ArmadaSettings Settings { get; }

            public HarborConnectionManager Manager { get; }

            public DockService Docks { get; }

            public MissionService Missions { get; }

            public AgentLifecycleHandler Handler { get; }

            public MissionLandingHandler Landing { get; }

            public CommittingHarborJobRunner Runner { get; } = new CommittingHarborJobRunner();

            public FakeGhHostCommandExecutor Gh { get; } = new FakeGhHostCommandExecutor();

            private readonly HarborDockGitFixture _Git;

            private Scenario(TestDatabase db, HarborDockGitFixture git)
            {
                Db = db;
                _Git = git;
                Logging = new LoggingModule();
                Logging.Settings.EnableConsole = false;
                Settings = new ArmadaSettings();
                Settings.LogDirectory = Path.Combine(git.AdmiralData, "logs");
                Settings.DocksDirectory = Path.Combine(git.AdmiralData, "docks");
                Settings.ReposDirectory = Path.Combine(git.AdmiralData, "repos");

                Manager = new HarborConnectionManager(new HarborService(db.Driver, Logging), Logging, null);
                GitService gitService = new GitService(Logging);
                DockHostResolver hosts = new DockHostResolver(gitService, Logging, Manager);
                Docks = new DockService(Logging, db.Driver, Settings, gitService) { Hosts = hosts };
                CaptainService captains = new CaptainService(Logging, db.Driver, Settings, gitService, Docks) { DockHosts = hosts };
                Missions = new MissionService(Logging, db.Driver, Settings, Docks, captains, git: gitService) { DockHosts = hosts };
                AdmiralService admiral = new AdmiralService(Logging, db.Driver, Settings, captains, Missions, new VoyageService(Logging, db.Driver), Docks);

                Handler = new AgentLifecycleHandler(
                    Logging, db.Driver, Settings, new AgentRuntimeFactory(Logging), admiral, new MessageTemplateService(Logging), null, null,
                    (eventType, message, entityType, entityId, captainId, missionId, vesselId, voyageId) => Task.CompletedTask);
                Handler.SetHarborConnections(Manager);

                Landing = new MissionLandingHandler(Logging, db.Driver, Settings, gitService, new MergeQueueService(Logging, db.Driver, Settings, gitService),
                    new MessageTemplateService(Logging), null, Docks, null) { DockHosts = hosts };

                Missions.ResolveDockPlacementAsync = Handler.ResolveDockPlacementAsync;
                admiral.OnLaunchAgent = Handler.HandleLaunchAgentAsync;
                admiral.OnCaptureDiff = Landing.HandleCaptureDiffAsync;
                admiral.OnMissionComplete = Landing.HandleMissionCompleteAsync;
            }

            public static async Task<Scenario> CreateAsync(HarborDockGitFixture git)
            {
                TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                return new Scenario(db, git);
            }

            /// <summary>
            /// Link an in-process Harbor owned by the mission user. With dock settings it creates docks on its host (they
            /// are its live settings); without them it is a Harbor that predates Harbor-side docks.
            /// </summary>
            public Task<InProcessHarbor> ConnectAsync(string harborId, HarborDockSettings? settings)
            {
                HarborDockManager? docks = settings != null ? new HarborDockManager(() => settings, Logging) : null;
                return InProcessHarbor.ConnectAsync(Manager, harborId, null, UserId, new AgentRuntimeFactory(Logging),
                    new List<string> { "ClaudeCode", "git" }, Logging, Gh, Runner, docks);
            }

            public Task<InProcessHarbor> ConnectMappedAsync(string harborId)
            {
                HarborDockSettings settings = _Git.Settings.Clone();
                settings.Repositories.Add(new HarborRepositoryMapping("app", _Git.Checkout));
                return ConnectAsync(harborId, settings);
            }

            public async Task<Vessel> CreateVesselAsync(string? repoUrl, LandingModeEnum landingMode)
            {
                Vessel vessel = new Vessel("app", repoUrl ?? String.Empty);
                vessel.DefaultBranch = "main";
                vessel.LandingMode = landingMode;
                return await Db.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            }

            public async Task<Captain> CreateCaptainAsync()
            {
                Captain captain = new Captain("dock-captain-" + Guid.NewGuid().ToString("N").Substring(0, 8), AgentRuntimeEnum.ClaudeCode);
                captain.State = CaptainStateEnum.Idle;
                return await Db.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
            }

            public async Task<Mission> CreateMissionAsync(Vessel vessel)
            {
                Mission mission = new Mission("Add a file", "Add a file to the repository");
                mission.VesselId = vessel.Id;
                mission.UserId = UserId;
                mission.Status = MissionStatusEnum.Pending;
                return await Db.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
            }

            /// <summary>
            /// Create a dock on the Harbor, commit one file in it (as a captain would), capture the diff, and land it.
            /// </summary>
            public async Task<LandingRun> RunLandingAsync(string harborId, LandingModeEnum landingMode)
            {
                Vessel vessel = await CreateVesselAsync(_Git.Origin, landingMode).ConfigureAwait(false);
                Captain captain = await CreateCaptainAsync().ConfigureAwait(false);
                Mission mission = await CreateMissionAsync(vessel).ConfigureAwait(false);
                mission.BranchName = "armada/land-" + landingMode.ToString().ToLowerInvariant();
                Dock dock = await Docks.ProvisionOnHarborAsync(vessel, captain, mission.BranchName, mission.Id, harborId).ConfigureAwait(false);
                mission.DockId = dock.Id;
                mission.CaptainId = captain.Id;
                mission.Status = MissionStatusEnum.WorkProduced;
                mission = await Db.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);

                string workCommit = await HarborDockGitFixture.CommitFileAsync(dock.WorktreePath!, "landed.txt", "landed\n", "Mission work").ConfigureAwait(false);
                await Landing.HandleCaptureDiffAsync(mission, dock).ConfigureAwait(false);
                await Landing.HandleMissionCompleteAsync(mission, dock).ConfigureAwait(false);

                Mission stored = (await Db.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                return new LandingRun(stored, dock, workCommit);
            }

            public void Dispose()
            {
                Db.Dispose();
            }
        }

        #endregion
    }
}
