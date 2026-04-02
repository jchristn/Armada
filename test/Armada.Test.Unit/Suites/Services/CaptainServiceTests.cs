namespace Armada.Test.Unit.Suites.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Tests for captain auto-recovery behavior.
    /// </summary>
    public class CaptainServiceTests : TestSuite
    {
        /// <summary>
        /// Suite name.
        /// </summary>
        public override string Name => "Captain Service";

        /// <summary>
        /// Run all tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Recovery launch failure marks mission failed instead of leaving it in progress", async () =>
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
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Captain should return to Idle after failed recovery");
                    AssertNull(updatedCaptain.CurrentMissionId, "Captain current mission should be cleared");
                    AssertEqual(1, docks.ReclaimCalls, "Dock should be reclaimed after failed recovery");
                }
            });

            await RunTest("Missing recovery dock marks mission failed instead of leaving it active", async () =>
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
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Captain should return to Idle when recovery prerequisites are missing");
                    AssertNull(updatedCaptain.CurrentMissionId, "Captain current mission should be cleared");
                }
            });

            await RunTest("Cancelled voyage does not auto-recover mission", async () =>
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
            });

            await RunTest("Healthy recovery worktree relaunch skips destructive repair", async () =>
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
            });
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
            settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
            return settings;
        }

        private class StubDockService : IDockService
        {
            public int ReclaimCalls { get; private set; } = 0;

            public Task<Dock?> ProvisionAsync(Vessel vessel, Captain captain, string branchName, string? missionId = null, CancellationToken token = default)
            {
                throw new NotImplementedException();
            }

            public Task ReclaimAsync(string dockId, string? tenantId = null, CancellationToken token = default)
            {
                ReclaimCalls++;
                return Task.CompletedTask;
            }

            public Task RepairAsync(string dockId, string? tenantId = null, CancellationToken token = default)
            {
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

        private class StubGitService : IGitService
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
            public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(string worktreePath, string startCommit, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default) { throw new NotImplementedException(); }
            public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) { throw new NotImplementedException(); }
        }
    }
}
