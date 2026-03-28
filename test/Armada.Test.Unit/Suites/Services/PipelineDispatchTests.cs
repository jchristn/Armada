namespace Armada.Test.Unit.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Test.Common;
    using Armada.Test.Unit.TestHelpers;

    /// <summary>
    /// Tests for pipeline-aware dispatch including multi-stage pipelines,
    /// dependency checking, and persona-aware captain routing.
    /// </summary>
    public class PipelineDispatchTests : TestSuite
    {
        /// <summary>
        /// Suite name.
        /// </summary>
        public override string Name => "Pipeline Dispatch";

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
        /// Run all tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            await RunTest("Dispatch with single-stage pipeline creates normal missions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);
                    AdmiralService admiralService = new AdmiralService(logging, testDb.Driver, settings, captainService, missionService, voyageService, dockService);

                    // Create vessel
                    Vessel vessel = new Vessel("dispatch-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    // Create a WorkerOnly pipeline (single stage)
                    Pipeline pipeline = new Pipeline("WorkerOnly");
                    pipeline.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Worker")
                    };
                    pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

                    // Dispatch with the single-stage pipeline
                    List<MissionDescription> missions = new List<MissionDescription>
                    {
                        new MissionDescription("Fix bug", "Fix the login bug")
                    };

                    Voyage voyage = await admiralService.DispatchVoyageAsync(
                        "Test Voyage",
                        "Test description",
                        vessel.Id,
                        missions,
                        pipeline.Id).ConfigureAwait(false);

                    AssertNotNull(voyage, "Voyage should be created");

                    // Single-stage Worker pipeline should use the standard dispatch path
                    List<Mission> voyageMissions = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    AssertTrue(voyageMissions.Count >= 1, "Should have at least 1 mission");

                    // Verify missions have Worker persona or null (standard path)
                    foreach (Mission mission in voyageMissions)
                    {
                        AssertTrue(
                            mission.Persona == null || mission.Persona == "Worker",
                            "Single-stage pipeline missions should have Worker or null persona, got: " + mission.Persona);
                    }
                }
            });

            await RunTest("Dispatch with multi-stage pipeline creates chained missions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    IVoyageService voyageService = new VoyageService(logging, testDb.Driver);
                    AdmiralService admiralService = new AdmiralService(logging, testDb.Driver, settings, captainService, missionService, voyageService, dockService);

                    // Create vessel
                    Vessel vessel = new Vessel("pipeline-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    // Create a 3-stage pipeline
                    Pipeline pipeline = new Pipeline("FullPipeline");
                    pipeline.Stages = new List<PipelineStage>
                    {
                        new PipelineStage(1, "Architect"),
                        new PipelineStage(2, "Worker"),
                        new PipelineStage(3, "Judge")
                    };
                    pipeline = await testDb.Driver.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);

                    // Dispatch with multi-stage pipeline
                    List<MissionDescription> missions = new List<MissionDescription>
                    {
                        new MissionDescription("Implement feature", "Implement the new feature")
                    };

                    Voyage voyage = await admiralService.DispatchVoyageAsync(
                        "Pipeline Voyage",
                        "Test pipeline dispatch",
                        vessel.Id,
                        missions,
                        pipeline.Id).ConfigureAwait(false);

                    AssertNotNull(voyage, "Voyage should be created");

                    // Should have 3 missions (one per stage)
                    List<Mission> voyageMissions = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    AssertEqual(3, voyageMissions.Count, "Should have 3 missions for 3-stage pipeline");

                    // Verify personas match pipeline stages
                    List<string> personas = voyageMissions.Select(m => m.Persona ?? "").ToList();
                    AssertTrue(personas.Contains("Architect"), "Should have Architect persona mission");
                    AssertTrue(personas.Contains("Worker"), "Should have Worker persona mission");
                    AssertTrue(personas.Contains("Judge"), "Should have Judge persona mission");

                    // Verify dependency chain: first mission has no dependency, others depend on prior
                    Mission? architectMission = voyageMissions.FirstOrDefault(m => m.Persona == "Architect");
                    Mission? workerMission = voyageMissions.FirstOrDefault(m => m.Persona == "Worker");
                    Mission? judgeMission = voyageMissions.FirstOrDefault(m => m.Persona == "Judge");

                    AssertNotNull(architectMission, "Architect mission should exist");
                    AssertNotNull(workerMission, "Worker mission should exist");
                    AssertNotNull(judgeMission, "Judge mission should exist");

                    AssertNull(architectMission!.DependsOnMissionId, "Architect (first stage) should have no dependency");
                    AssertEqual(architectMission.Id, workerMission!.DependsOnMissionId, "Worker should depend on Architect");
                    AssertEqual(workerMission.Id, judgeMission!.DependsOnMissionId, "Judge should depend on Worker");
                }
            });

            await RunTest("Dependency check blocks assignment of dependent mission", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    // Create vessel
                    Vessel vessel = new Vessel("dep-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    // Create an idle captain
                    Captain captain = new Captain("dep-captain");
                    captain.State = CaptainStateEnum.Idle;
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    // Create mission A (Pending, no dependency)
                    Mission missionA = new Mission("Mission A");
                    missionA.VesselId = vessel.Id;
                    missionA.Status = MissionStatusEnum.Pending;
                    missionA = await testDb.Driver.Missions.CreateAsync(missionA).ConfigureAwait(false);

                    // Create mission B that depends on A
                    Mission missionB = new Mission("Mission B");
                    missionB.VesselId = vessel.Id;
                    missionB.Status = MissionStatusEnum.Pending;
                    missionB.DependsOnMissionId = missionA.Id;
                    missionB = await testDb.Driver.Missions.CreateAsync(missionB).ConfigureAwait(false);

                    // Try to assign B -- should fail because A is still Pending
                    bool assigned = await missionService.TryAssignAsync(missionB, vessel).ConfigureAwait(false);
                    AssertFalse(assigned, "Mission B should not be assigned while A is Pending");
                }
            });

            await RunTest("Persona-aware captain routing prefers matching captain", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(12345);

                    // Create vessel
                    Vessel vessel = new Vessel("persona-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    // Create two idle captains: one with PreferredPersona=Judge, one without
                    Captain genericCaptain = new Captain("generic-captain");
                    genericCaptain.State = CaptainStateEnum.Idle;
                    await testDb.Driver.Captains.CreateAsync(genericCaptain).ConfigureAwait(false);

                    Captain judgeCaptain = new Captain("judge-captain");
                    judgeCaptain.State = CaptainStateEnum.Idle;
                    judgeCaptain.PreferredPersona = "Judge";
                    await testDb.Driver.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);

                    // Create a Judge mission
                    Mission judgeMission = new Mission("Review code");
                    judgeMission.VesselId = vessel.Id;
                    judgeMission.Persona = "Judge";
                    judgeMission.Status = MissionStatusEnum.Pending;
                    judgeMission = await testDb.Driver.Missions.CreateAsync(judgeMission).ConfigureAwait(false);

                    try
                    {
                        // Try to assign -- the judge captain should be preferred
                        bool assigned = await missionService.TryAssignAsync(judgeMission, vessel).ConfigureAwait(false);
                        AssertTrue(assigned, "Mission should be assigned");

                        // Reload the mission to check which captain was selected
                        Mission? reloaded = await testDb.Driver.Missions.ReadAsync(judgeMission.Id).ConfigureAwait(false);
                        AssertNotNull(reloaded, "Mission should be readable after assignment");
                        AssertEqual(judgeCaptain.Id, reloaded!.CaptainId, "Judge mission should be assigned to the judge-preferred captain");
                    }
                    finally
                    {
                        try { Directory.Delete(settings.DocksDirectory, true); } catch { }
                    }
                }
            });

            await RunTest("AllowedPersonas filters ineligible captains", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(12345);

                    // Create vessel
                    Vessel vessel = new Vessel("filter-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    // Create captain restricted to Worker only
                    Captain workerOnlyCaptain = new Captain("worker-only");
                    workerOnlyCaptain.State = CaptainStateEnum.Idle;
                    workerOnlyCaptain.AllowedPersonas = "[\"Worker\"]";
                    await testDb.Driver.Captains.CreateAsync(workerOnlyCaptain).ConfigureAwait(false);

                    // Create captain that can handle any persona
                    Captain anyCaptain = new Captain("any-captain");
                    anyCaptain.State = CaptainStateEnum.Idle;
                    await testDb.Driver.Captains.CreateAsync(anyCaptain).ConfigureAwait(false);

                    // Create a Judge mission
                    Mission judgeMission = new Mission("Judge work");
                    judgeMission.VesselId = vessel.Id;
                    judgeMission.Persona = "Judge";
                    judgeMission.Status = MissionStatusEnum.Pending;
                    judgeMission = await testDb.Driver.Missions.CreateAsync(judgeMission).ConfigureAwait(false);

                    try
                    {
                        // Try to assign -- worker-only captain should be filtered out
                        bool assigned = await missionService.TryAssignAsync(judgeMission, vessel).ConfigureAwait(false);
                        AssertTrue(assigned, "Mission should be assigned");

                        // Reload to check captain
                        Mission? reloaded = await testDb.Driver.Missions.ReadAsync(judgeMission.Id).ConfigureAwait(false);
                        AssertNotNull(reloaded, "Mission should be readable after assignment");
                        // The any-captain should be selected because it has no AllowedPersonas restriction
                        AssertEqual(anyCaptain.Id, reloaded!.CaptainId, "Judge mission should be assigned to the unrestricted captain, not the Worker-only captain");
                    }
                    finally
                    {
                        try { Directory.Delete(settings.DocksDirectory, true); } catch { }
                    }
                }
            });
        }

        #region Private-Classes

        /// <summary>
        /// Git service stub that creates worktree directories on disk so that
        /// CLAUDE.md generation succeeds during TryAssignAsync integration tests.
        /// </summary>
        private class DirCreatingGitStub : IGitService
        {
            /// <summary>Call tracking for clone operations.</summary>
            public List<string> CloneCalls { get; } = new List<string>();

            /// <summary>Call tracking for worktree operations.</summary>
            public List<string> WorktreeCalls { get; } = new List<string>();

            /// <inheritdoc />
            public Task CloneBareAsync(string repoUrl, string localPath, CancellationToken token = default)
            {
                CloneCalls.Add(repoUrl + " -> " + localPath);
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task CreateWorktreeAsync(string repoPath, string worktreePath, string branchName, string baseBranch = "main", CancellationToken token = default)
            {
                // Create the directory on disk so CLAUDE.md can be written
                Directory.CreateDirectory(worktreePath);
                WorktreeCalls.Add(worktreePath);
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task RemoveWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task FetchAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task PushBranchAsync(string worktreePath, string remoteName = "origin", CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task<string> CreatePullRequestAsync(string worktreePath, string title, string body, CancellationToken token = default)
                => Task.FromResult("https://github.com/test/repo/pull/1");

            /// <inheritdoc />
            public Task RepairWorktreeAsync(string worktreePath, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task<bool> IsRepositoryAsync(string path, CancellationToken token = default) => Task.FromResult(true);

            /// <inheritdoc />
            public Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task PruneWorktreesAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task EnableAutoMergeAsync(string worktreePath, string prUrl, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task MergeBranchLocalAsync(string targetWorkDir, string sourceRepoPath, string branchName, string? targetBranch = null, string? commitMessage = null, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task PullAsync(string workingDirectory, CancellationToken token = default) => Task.CompletedTask;

            /// <inheritdoc />
            public Task<string> DiffAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default)
                => Task.FromResult("");

            /// <inheritdoc />
            public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) => Task.FromResult(true);

            /// <inheritdoc />
            public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) => Task.FromResult<string?>("abc123def456");

            /// <inheritdoc />
            public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default) => Task.FromResult(false);

            /// <inheritdoc />
            public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);
        }

        #endregion
    }
}
