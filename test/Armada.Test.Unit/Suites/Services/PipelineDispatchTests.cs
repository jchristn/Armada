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
            await RunTest("Direct dispatch sanitizes captain names in mission branches", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    captainService.OnLaunchAgent = (_, _, _) => Task.FromResult(12345);
                    IMissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    Vessel vessel = new Vessel("setup-vessel", "https://github.com/test/repo.git");
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain captain = new Captain("Setup Captain");
                    captain.State = CaptainStateEnum.Idle;
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Mission mission = new Mission("Repository onboarding survey", "Inspect the repository without changing files.");
                    mission.VesselId = vessel.Id;
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    bool assigned = await missionService.TryAssignAsync(mission, vessel).ConfigureAwait(false);
                    Mission? assignedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);

                    AssertTrue(assigned, "Mission should be assigned");
                    AssertNotNull(assignedMission, "Assigned mission should be persisted");
                    AssertEqual("armada/setup-captain/" + mission.Id, assignedMission!.BranchName, "Captain name should be sanitized in branch");
                    AssertFalse(assignedMission.BranchName!.Contains(" "), "Branch should not contain spaces");
                    AssertTrue(git.ExistingBranches.Contains(assignedMission.BranchName), "Worktree should be provisioned with sanitized branch");
                }
            });

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
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);

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

            await RunTest("Pending mission on cancelled voyage is cancelled instead of assigned", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    StubGitService git = new StubGitService();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);

                    Vessel vessel = new Vessel("cancelled-voyage-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain captain = new Captain("cancelled-voyage-captain");
                    captain.State = CaptainStateEnum.Idle;
                    captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("cancelled-voyage");
                    voyage.Status = VoyageStatusEnum.Cancelled;
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission("Cancelled child mission");
                    mission.VoyageId = voyage.Id;
                    mission.VesselId = vessel.Id;
                    mission.Status = MissionStatusEnum.Pending;
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    bool assigned = await missionService.TryAssignAsync(mission, vessel).ConfigureAwait(false);
                    AssertFalse(assigned, "Mission should not be assigned when its voyage is already cancelled");

                    Mission? updatedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);

                    AssertNotNull(updatedMission, "Mission should still exist");
                    AssertNotNull(updatedCaptain, "Captain should still exist");
                    AssertEqual(MissionStatusEnum.Cancelled, updatedMission!.Status, "Pending mission should be cancelled when its voyage is terminal");
                    AssertContains("Parent voyage", updatedMission.FailureReason ?? String.Empty, "Mission should record why assignment was skipped");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Captain should remain idle when assignment is skipped");
                }
            });

            await RunTest("Dependent mission waits for pipeline handoff when dependency is WorkProduced", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(12345);

                    // Create vessel
                    Vessel vessel = new Vessel("handoff-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    // Create an idle captain
                    Captain captain = new Captain("handoff-captain");
                    captain.State = CaptainStateEnum.Idle;
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    // Upstream mission has finished but downstream handoff has not yet marked the
                    // worker mission as architect-prepared.
                    Mission architectMission = new Mission("[Architect] Plan work");
                    architectMission.VesselId = vessel.Id;
                    architectMission.Persona = "Architect";
                    architectMission.Status = MissionStatusEnum.WorkProduced;
                    architectMission.BranchName = "armada/handoff-captain/msn_architect";
                    architectMission = await testDb.Driver.Missions.CreateAsync(architectMission).ConfigureAwait(false);

                    Mission workerMission = new Mission("[Worker] Implement work", "Original dispatch description");
                    workerMission.VesselId = vessel.Id;
                    workerMission.Persona = "Worker";
                    workerMission.Status = MissionStatusEnum.Pending;
                    workerMission.DependsOnMissionId = architectMission.Id;
                    workerMission = await testDb.Driver.Missions.CreateAsync(workerMission).ConfigureAwait(false);

                    try
                    {
                        bool assignedBeforeHandoff = await missionService.TryAssignAsync(workerMission, vessel).ConfigureAwait(false);
                        AssertFalse(assignedBeforeHandoff, "Worker mission should not be assigned before architect handoff marks it prepared");

                        workerMission.Description = "<!-- ARMADA:ARCHITECT-HANDOFF -->\nPrepared handoff description";
                        await testDb.Driver.Missions.UpdateAsync(workerMission).ConfigureAwait(false);

                        bool assignedAfterHandoff = await missionService.TryAssignAsync(workerMission, vessel).ConfigureAwait(false);
                        AssertTrue(assignedAfterHandoff, "Worker mission should assign after architect handoff marks it prepared");

                        Mission? assignedMission = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                        AssertNotNull(assignedMission, "Assigned worker mission should be readable");
                        AssertFalse(String.IsNullOrEmpty(assignedMission!.BranchName), "Dependent mission should receive its own worker branch");
                        AssertFalse(String.Equals(architectMission.BranchName, assignedMission.BranchName, StringComparison.Ordinal), "Architect-created worker should not continue on the architect branch");
                        AssertTrue(git.WorktreeBranches.Contains(assignedMission.BranchName!), "Provisioned worktree should use the assigned worker branch");
                    }
                    finally
                    {
                        try { Directory.Delete(settings.DocksDirectory, true); } catch { }
                    }
                }
            });

            await RunTest("Architect failure during handoff does not trigger landing", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);

                    int landingCalls = 0;
                    missionService.OnMissionComplete = (Mission mission, Dock dock) =>
                    {
                        landingCalls++;
                        return Task.CompletedTask;
                    };
                    missionService.OnGetMissionOutput = _ => "Architect summary without mission markers";

                    Vessel vessel = new Vessel("architect-failure-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain captain = new Captain("architect-failure-captain");
                    captain.State = CaptainStateEnum.Working;
                    captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("architect-failure-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Dock dock = new Dock(vessel.Id);
                    dock.CaptainId = captain.Id;
                    dock.WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_wt_" + Guid.NewGuid().ToString("N"));
                    dock.BranchName = "armada/architect-failure/msn_arch";
                    dock.Active = true;
                    dock = await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

                    Mission architectMission = new Mission("[Architect] Plan work", "Break this down");
                    architectMission.VesselId = vessel.Id;
                    architectMission.VoyageId = voyage.Id;
                    architectMission.CaptainId = captain.Id;
                    architectMission.DockId = dock.Id;
                    architectMission.Persona = "Architect";
                    architectMission.Status = MissionStatusEnum.InProgress;
                    architectMission.BranchName = dock.BranchName;
                    architectMission = await testDb.Driver.Missions.CreateAsync(architectMission).ConfigureAwait(false);

                    Mission workerMission = new Mission("[Worker] Placeholder", "Original dispatch description");
                    workerMission.VesselId = vessel.Id;
                    workerMission.VoyageId = voyage.Id;
                    workerMission.Persona = "Worker";
                    workerMission.Status = MissionStatusEnum.Pending;
                    workerMission.DependsOnMissionId = architectMission.Id;
                    workerMission = await testDb.Driver.Missions.CreateAsync(workerMission).ConfigureAwait(false);

                    captain.CurrentMissionId = architectMission.Id;
                    captain.CurrentDockId = dock.Id;
                    await testDb.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);

                    try
                    {
                        await missionService.HandleCompletionAsync(captain, architectMission.Id).ConfigureAwait(false);

                        Mission? updatedArchitect = await testDb.Driver.Missions.ReadAsync(architectMission.Id).ConfigureAwait(false);
                        Mission? updatedWorker = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                        Voyage? updatedVoyage = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                        Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                        Dock? updatedDock = await testDb.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false);

                        AssertNotNull(updatedArchitect, "Architect mission should still exist");
                        AssertEqual(MissionStatusEnum.Failed, updatedArchitect!.Status, "Architect mission should fail when no mission markers are produced");
                        AssertContains("no [ARMADA:MISSION] markers", updatedArchitect.FailureReason ?? String.Empty);
                        AssertNotNull(updatedWorker, "Dependent worker mission should still exist");
                        AssertEqual(MissionStatusEnum.Cancelled, updatedWorker!.Status, "Dependent worker mission should be cancelled when architect handoff fails");
                        AssertContains("Blocked by failed dependency", updatedWorker.FailureReason ?? String.Empty);
                        AssertNotNull(updatedVoyage, "Voyage should still exist");
                        AssertEqual(VoyageStatusEnum.Failed, updatedVoyage!.Status, "Voyage should fail when architect handoff failure blocks the remaining pipeline");
                        AssertEqual(0, landingCalls, "Landing should not run when handoff fails");
                        AssertNotNull(updatedCaptain, "Captain should still exist");
                        AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Captain should be released after architect handoff failure");
                        AssertNotNull(updatedDock, "Dock should still exist");
                        AssertFalse(updatedDock!.Active, "Dock should be reclaimed after architect handoff failure");
                    }
                    finally
                    {
                        try { Directory.Delete(settings.DocksDirectory, true); } catch { }
                    }
                }
            });

            await RunTest("Architect numbered summary without markers fans out cleanly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(2500 + git.WorktreeCalls.Count);

                    Vessel vessel = new Vessel("architect-summary-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectCaptain = new Captain("architect-summary-captain");
                    architectCaptain.State = CaptainStateEnum.Working;
                    architectCaptain = await testDb.Driver.Captains.CreateAsync(architectCaptain).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("architect-summary-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("architect-summary-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Plan", "Break this down");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.CaptainId = architectCaptain.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.BranchName = "armada/architect/summary";
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = architectCaptain.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
                    architectDock.BranchName = architect.BranchName;
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);
                    architect.DockId = architectDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(architect).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Placeholder", "Initial worker");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Placeholder", "Initial review");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = testEngineer.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    architectCaptain.CurrentMissionId = architect.Id;
                    architectCaptain.CurrentDockId = architectDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(architectCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Vessel context updated. The architect mission is complete. Here's a summary of the 7 missions decomposed:\n" +
                        "1. **Captain model property + all database backends + migration scripts** (HIGH) -- Core data layer: add Model to Captain, TotalRuntimeMs to Mission, migrations v26-v27 across all 4 DB backends, migration scripts\n" +
                        "2. **Runtime model pass-through + model validation on captain update** (MEDIUM) -- Add model param to IAgentRuntime.StartAsync, propagate through all runtimes, add validation logic in AgentLifecycleHandler\n" +
                        "3. **REST + MCP API updates for captain model and mission total runtime** (MEDIUM) -- Wire new fields through CaptainRoutes, MissionRoutes, MCP tools, args classes, and registrar\n" +
                        "4. **Dashboard: captain model field, mission detail 4-column + runtime, dispatch cleanup, error modal** (MEDIUM) -- TypeScript types, CaptainDetail model input, MissionDetail 4-col grid + runtime display, Dispatch task detection removal\n" +
                        "5. **Version bump to 0.7.0 across all version locations** (LOW) -- Helm csproj, compose.yaml, Postman, REST_API.md, MCP_API.md\n" +
                        "6. **Documentation updates: REST_API.md, MCP_API.md, Postman collection for new fields** (MEDIUM) -- API docs for model and totalRuntimeMs fields\n" +
                        "7. **README and CHANGELOG updates for v0.7.0** (LOW) -- Release notes and feature documentation\n" +
                        "Missions 1 and 2 are foundational. Missions 3-4 depend on 1. Missions 5-7 can run in parallel with each other and with 3-4.";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    List<Mission> workerMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Worker", StringComparison.OrdinalIgnoreCase)).ToList();
                    List<Mission> testMissions = afterArchitect.Where(m => String.Equals(m.Persona, "TestEngineer", StringComparison.OrdinalIgnoreCase)).ToList();
                    List<Mission> judgeMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Judge", StringComparison.OrdinalIgnoreCase)).ToList();

                    AssertEqual(22, afterArchitect.Count, "Architect numbered summary should fan out the entire downstream chain");
                    AssertEqual(7, workerMissions.Count, "Architect numbered summary should produce seven worker missions");
                    AssertEqual(7, testMissions.Count, "Architect numbered summary should clone seven test stages");
                    AssertEqual(7, judgeMissions.Count, "Architect numbered summary should clone seven judge stages");

                    Mission? apiMission = workerMissions.FirstOrDefault(m => m.Title == "REST + MCP API updates for captain model and mission total runtime [Worker]");
                    AssertNotNull(apiMission, "Summary parser should preserve the numbered mission titles");
                    AssertContains("Wire new fields through CaptainRoutes", apiMission!.Description ?? String.Empty, "Summary parser should carry the inline description into the worker mission");

                    Mission? firstWorker = workerMissions.FirstOrDefault(m => m.Title == "Captain model property + all database backends + migration scripts [Worker]");
                    AssertNotNull(firstWorker, "First summary mission should map onto the existing worker slot");
                    AssertEqual(MissionStatusEnum.InProgress, firstWorker!.Status, "First parsed worker mission should auto-dispatch after architect completion");
                }
            });

            await RunTest("Architect markdown mission headings with multiline descriptions fan out cleanly", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(2600 + git.WorktreeCalls.Count);

                    Vessel vessel = new Vessel("architect-markdown-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectCaptain = new Captain("architect-markdown-captain");
                    architectCaptain.State = CaptainStateEnum.Working;
                    architectCaptain = await testDb.Driver.Captains.CreateAsync(architectCaptain).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("architect-markdown-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("architect-markdown-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Plan", "Break this down");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.CaptainId = architectCaptain.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.BranchName = "armada/architect/markdown";
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = architectCaptain.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
                    architectDock.BranchName = architect.BranchName;
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);
                    architect.DockId = architectDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(architect).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Placeholder", "Initial worker");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Placeholder", "Initial review");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = testEngineer.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    architectCaptain.CurrentMissionId = architect.Id;
                    architectCaptain.CurrentDockId = architectDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(architectCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Architect mission complete. Here is the summary of 2 missions decomposed:\n" +
                        "**Mission 1: Core model and database groundwork** (9 files)\n" +
                        "Add Captain.Model and Mission.TotalRuntimeMs across core models and baseline persistence.\n" +
                        "**Mission 2: API exposure and validation** (6 files)\n" +
                        "Depends on Mission 1. Expose the new fields through REST and MCP and validate configured captain models.\n";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    List<Mission> workerMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Worker", StringComparison.OrdinalIgnoreCase)).ToList();
                    List<Mission> testMissions = afterArchitect.Where(m => String.Equals(m.Persona, "TestEngineer", StringComparison.OrdinalIgnoreCase)).ToList();
                    List<Mission> judgeMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Judge", StringComparison.OrdinalIgnoreCase)).ToList();

                    AssertEqual(7, afterArchitect.Count, "Markdown architect summary should fan out two downstream chains");
                    AssertEqual(2, workerMissions.Count, "Markdown architect summary should produce two worker missions");
                    AssertEqual(2, testMissions.Count, "Markdown architect summary should produce two test missions");
                    AssertEqual(2, judgeMissions.Count, "Markdown architect summary should produce two judge missions");

                    Mission? secondWorker = workerMissions.FirstOrDefault(m =>
                        (m.Description ?? String.Empty).Contains("Expose the new fields through REST and MCP", StringComparison.Ordinal));
                    AssertNotNull(secondWorker, "Expected second markdown summary worker mission to exist");
                    Mission? dependencyMission = afterArchitect.FirstOrDefault(m => m.Id == secondWorker!.DependsOnMissionId);
                    AssertNotNull(dependencyMission, "Expected markdown summary dependency to resolve to an existing mission");
                    AssertEqual("Judge", dependencyMission!.Persona, "Natural-language dependency sentence should point the second worker to the first chain terminal stage");
                    AssertContains("Expose the new fields through REST and MCP", secondWorker.Description ?? String.Empty, "Trailing description after the dependency sentence should be preserved");
                    AssertFalse((secondWorker.Description ?? String.Empty).StartsWith("Depends on", StringComparison.OrdinalIgnoreCase), "Dependency sentence should be removed from the worker description after parsing");
                }
            });

            await RunTest("Architect fan-out clones full downstream chain and lands only terminal stage", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(1000 + git.WorktreeCalls.Count);

                    int landingCalls = 0;
                    missionService.OnMissionComplete = (Mission mission, Dock dock) =>
                    {
                        landingCalls++;
                        mission.Status = MissionStatusEnum.Complete;
                        mission.CompletedUtc = DateTime.UtcNow;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        return testDb.Driver.Missions.UpdateAsync(mission);
                    };

                    Vessel vessel = new Vessel("fanout-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel.BranchCleanupPolicy = BranchCleanupPolicyEnum.LocalAndRemote;
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain captain1 = new Captain("fanout-captain-1");
                    captain1.State = CaptainStateEnum.Idle;
                    captain1 = await testDb.Driver.Captains.CreateAsync(captain1).ConfigureAwait(false);

                    Captain captain2 = new Captain("fanout-captain-2");
                    captain2.State = CaptainStateEnum.Idle;
                    captain2 = await testDb.Driver.Captains.CreateAsync(captain2).ConfigureAwait(false);

                    Voyage voyage = new Voyage("fanout-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Plan", "Break this down");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.CaptainId = captain1.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.BranchName = "armada/fanout/architect";
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = captain1.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
                    architectDock.BranchName = architect.BranchName;
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);
                    architect.DockId = architectDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(architect).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Placeholder", "Initial worker");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Placeholder", "Initial review");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = testEngineer.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    captain1.CurrentMissionId = architect.Id;
                    captain1.CurrentDockId = architectDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(captain1).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "[ARMADA:MISSION] Add API endpoint\nImplement endpoint\n" +
                        "[ARMADA:MISSION] Update docs\nDocument endpoint";

                    await missionService.HandleCompletionAsync(captain1, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    AssertEqual(7, afterArchitect.Count, "Architect fan-out should create full cloned downstream chain");
                    AssertEqual(0, landingCalls, "Architect completion should not land while downstream stages remain");
                    AssertTrue(git.DeletedLocalBranches.Contains(architect.BranchName), "Architect branch should be deleted locally after successful handoff");
                    AssertTrue(git.DeletedRemoteBranches.Contains(architect.BranchName), "Architect branch should be deleted remotely after successful handoff");

                    Mission? apiWorker = afterArchitect.FirstOrDefault(m => m.Title == "Add API endpoint [Worker]");
                    Mission? apiTest = afterArchitect.FirstOrDefault(m => m.Title == "Add API endpoint [TestEngineer]");
                    Mission? apiJudge = afterArchitect.FirstOrDefault(m => m.Title == "Add API endpoint [Judge]");
                    Mission? docsWorker = afterArchitect.FirstOrDefault(m => m.Title == "Update docs [Worker]");
                    Mission? docsTest = afterArchitect.FirstOrDefault(m => m.Title == "Update docs [TestEngineer]");
                    Mission? docsJudge = afterArchitect.FirstOrDefault(m => m.Title == "Update docs [Judge]");

                    AssertNotNull(apiWorker, "Primary worker should exist");
                    AssertNotNull(apiTest, "Primary test stage should exist");
                    AssertNotNull(apiJudge, "Primary judge stage should exist");
                    AssertNotNull(docsWorker, "Secondary worker should exist");
                    AssertNotNull(docsTest, "Secondary test stage should exist");
                    AssertNotNull(docsJudge, "Secondary judge stage should exist");
                    AssertEqual(docsWorker!.Id, docsTest!.DependsOnMissionId, "Cloned test stage should depend on cloned worker");
                    AssertEqual(docsTest.Id, docsJudge!.DependsOnMissionId, "Cloned judge stage should depend on cloned test stage");
                    AssertTrue(String.IsNullOrEmpty(docsWorker.BranchName), "Cloned worker should wait for its own branch assignment");
                    AssertContains("Implement endpoint", apiTest!.Description ?? String.Empty, "Primary test stage should inherit architect-split mission scope");
                    AssertContains("Implement endpoint", apiJudge!.Description ?? String.Empty, "Primary judge stage should inherit architect-split mission scope");
                    AssertContains("Document endpoint", docsTest.Description ?? String.Empty, "Cloned test stage should inherit architect-split mission scope");
                    AssertContains("Document endpoint", docsJudge.Description ?? String.Empty, "Cloned judge stage should inherit architect-split mission scope");

                    apiWorker = await testDb.Driver.Missions.ReadAsync(apiWorker!.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, apiWorker!.Status, "Primary worker should auto-dispatch after architect completion");

                    Captain? workerCaptain = await testDb.Driver.Captains.ReadAsync(apiWorker.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ => "worker complete";
                    await missionService.HandleCompletionAsync(workerCaptain!, apiWorker.Id).ConfigureAwait(false);

                    apiWorker = await testDb.Driver.Missions.ReadAsync(apiWorker.Id).ConfigureAwait(false);
                    apiTest = await testDb.Driver.Missions.ReadAsync(apiTest!.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, apiWorker!.Status, "Worker should remain WorkProduced until downstream review completes");
                    AssertEqual(0, landingCalls, "Worker completion should not trigger landing");
                    AssertEqual(MissionStatusEnum.InProgress, apiTest!.Status, "Test stage should start after worker completion");

                    Captain? testCaptain = await testDb.Driver.Captains.ReadAsync(apiTest.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ => "test complete";
                    await missionService.HandleCompletionAsync(testCaptain!, apiTest.Id).ConfigureAwait(false);

                    apiTest = await testDb.Driver.Missions.ReadAsync(apiTest.Id).ConfigureAwait(false);
                    apiJudge = await testDb.Driver.Missions.ReadAsync(apiJudge!.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, apiTest!.Status, "TestEngineer should remain WorkProduced until judge completes");
                    AssertEqual(0, landingCalls, "TestEngineer completion should not trigger landing");
                    AssertEqual(MissionStatusEnum.InProgress, apiJudge!.Status, "Judge should start after TestEngineer completion");

                    Captain? judgeCaptain = await testDb.Driver.Captains.ReadAsync(apiJudge.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ =>
                        "## Completeness\n" +
                        "The staged work covers the assigned requirements and there are no missing deliverables in this chain.\n\n" +
                        "## Correctness\n" +
                        "The implementation and test updates are coherent, and I do not see logic or scope defects in the reviewed diff.\n\n" +
                        "## Tests\n" +
                        "The automated tests added in the prior stage cover the reviewed behavior adequately for this mission.\n\n" +
                        "## Failure Modes\n" +
                        "I reviewed the relevant edge and failure behavior for this scope and did not find any unresolved blockers.\n\n" +
                        "## Verdict\n" +
                        "[ARMADA:VERDICT] PASS\n" +
                        "The work is complete and correctly scoped.\n" +
                        "tokens used\n" +
                        "48,676";
                    await missionService.HandleCompletionAsync(judgeCaptain!, apiJudge.Id).ConfigureAwait(false);

                    apiJudge = await testDb.Driver.Missions.ReadAsync(apiJudge.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, apiJudge!.Status, "Judge completion should trigger terminal landing");
                    AssertEqual(1, landingCalls, "Only the terminal judge stage should land");
                }
            });

            await RunTest("Architect fan-out honors explicit mission dependencies across worker chains", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(2000 + git.WorktreeCalls.Count);

                    int landingCalls = 0;
                    missionService.OnMissionComplete = (Mission mission, Dock dock) =>
                    {
                        landingCalls++;
                        mission.Status = MissionStatusEnum.Complete;
                        mission.CompletedUtc = DateTime.UtcNow;
                        mission.LastUpdateUtc = DateTime.UtcNow;
                        return testDb.Driver.Missions.UpdateAsync(mission);
                    };

                    Vessel vessel = new Vessel("sequenced-fanout-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel.BranchCleanupPolicy = BranchCleanupPolicyEnum.LocalAndRemote;
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectCaptain = new Captain("sequenced-architect");
                    architectCaptain.State = CaptainStateEnum.Idle;
                    architectCaptain = await testDb.Driver.Captains.CreateAsync(architectCaptain).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("sequenced-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Captain reviewerCaptain = new Captain("sequenced-reviewer");
                    reviewerCaptain.State = CaptainStateEnum.Idle;
                    reviewerCaptain = await testDb.Driver.Captains.CreateAsync(reviewerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("sequenced-fanout-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Plan", "Break this down");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.CaptainId = architectCaptain.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.BranchName = "armada/sequenced/architect";
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = architectCaptain.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
                    architectDock.BranchName = architect.BranchName;
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);
                    architect.DockId = architectDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(architect).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Placeholder", "Initial worker");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Placeholder", "Initial review");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = testEngineer.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    architectCaptain.CurrentMissionId = architect.Id;
                    architectCaptain.CurrentDockId = architectDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(architectCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "[ARMADA:MISSION] Add core model properties\n" +
                        "Update Captain.cs and Mission.cs.\n" +
                        "[ARMADA:MISSION] Extend secondary backends\n" +
                        "Depends on: Mission 1 (core model changes must land first)\n" +
                        "Update PostgreSQL, MySQL, and migration scripts.";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    Mission? coreWorker = afterArchitect.FirstOrDefault(m => m.Title == "Add core model properties [Worker]");
                    Mission? coreTest = afterArchitect.FirstOrDefault(m => m.Title == "Add core model properties [TestEngineer]");
                    Mission? coreJudge = afterArchitect.FirstOrDefault(m => m.Title == "Add core model properties [Judge]");
                    Mission? backendWorker = afterArchitect.FirstOrDefault(m => m.Title == "Extend secondary backends [Worker]");
                    Mission? backendTest = afterArchitect.FirstOrDefault(m => m.Title == "Extend secondary backends [TestEngineer]");
                    Mission? backendJudge = afterArchitect.FirstOrDefault(m => m.Title == "Extend secondary backends [Judge]");

                    AssertNotNull(coreWorker, "Primary worker should exist");
                    AssertNotNull(coreTest, "Primary test stage should exist");
                    AssertNotNull(coreJudge, "Primary judge stage should exist");
                    AssertNotNull(backendWorker, "Dependent worker should exist");
                    AssertNotNull(backendTest, "Dependent test stage should exist");
                    AssertNotNull(backendJudge, "Dependent judge stage should exist");
                    AssertEqual(coreJudge!.Id, backendWorker!.DependsOnMissionId, "Dependent worker should wait for the upstream chain's terminal stage");
                    AssertFalse((backendWorker.Description ?? String.Empty).Contains("Depends on:", StringComparison.OrdinalIgnoreCase),
                        "Dependency metadata should be removed from the worker description after parsing");
                    AssertEqual(MissionStatusEnum.InProgress, coreWorker!.Status, "Primary worker should auto-dispatch after architect completion");
                    AssertEqual(MissionStatusEnum.Pending, backendWorker.Status, "Dependent worker should remain pending until the upstream chain completes");

                    Captain? activeWorkerCaptain = await testDb.Driver.Captains.ReadAsync(coreWorker.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ => "core worker complete";
                    await missionService.HandleCompletionAsync(activeWorkerCaptain!, coreWorker.Id).ConfigureAwait(false);

                    coreTest = await testDb.Driver.Missions.ReadAsync(coreTest!.Id).ConfigureAwait(false);
                    backendWorker = await testDb.Driver.Missions.ReadAsync(backendWorker.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, coreTest!.Status, "Primary test stage should start after the primary worker");
                    AssertEqual(MissionStatusEnum.Pending, backendWorker!.Status, "Dependent worker should still wait while upstream review is running");

                    Captain? activeTestCaptain = await testDb.Driver.Captains.ReadAsync(coreTest.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ => "core tests complete";
                    await missionService.HandleCompletionAsync(activeTestCaptain!, coreTest.Id).ConfigureAwait(false);

                    coreJudge = await testDb.Driver.Missions.ReadAsync(coreJudge.Id).ConfigureAwait(false);
                    backendWorker = await testDb.Driver.Missions.ReadAsync(backendWorker.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, coreJudge!.Status, "Primary judge should start after the primary test stage");
                    AssertEqual(MissionStatusEnum.Pending, backendWorker!.Status, "Dependent worker should still wait while the primary judge is running");

                    Captain? activeJudgeCaptain = await testDb.Driver.Captains.ReadAsync(coreJudge.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ =>
                        "## Completeness\n" +
                        "The upstream worker and test stages completed the requested scope and nothing material is missing.\n\n" +
                        "## Correctness\n" +
                        "I reviewed the diff and prior output and did not find correctness issues in the completed upstream chain.\n\n" +
                        "## Tests\n" +
                        "The upstream tests cover the changed behavior and are sufficient for this dependency chain.\n\n" +
                        "## Failure Modes\n" +
                        "Relevant error and edge paths were reviewed for this scope and I do not see unresolved safety concerns.\n\n" +
                        "## Verdict\n" +
                        "[ARMADA:VERDICT] PASS\n" +
                        "Upstream chain is approved.\n";
                    await missionService.HandleCompletionAsync(activeJudgeCaptain!, coreJudge.Id).ConfigureAwait(false);

                    coreJudge = await testDb.Driver.Missions.ReadAsync(coreJudge.Id).ConfigureAwait(false);
                    backendWorker = await testDb.Driver.Missions.ReadAsync(backendWorker.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, coreJudge!.Status, "Upstream judge should remain WorkProduced while dependent stages still exist");
                    AssertEqual(0, landingCalls, "Upstream judge should not land while a dependent worker chain remains");
                    AssertEqual(MissionStatusEnum.InProgress, backendWorker!.Status, "Dependent worker should start once the upstream chain completes");
                    AssertEqual(coreJudge.BranchName, backendWorker.BranchName, "Dependent worker should inherit the approved upstream branch");
                }
            });

            await RunTest("Architect handoff de-duplicates repeated mission blocks", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(3000 + git.WorktreeCalls.Count);

                    Vessel vessel = new Vessel("dedupe-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectCaptain = new Captain("dedupe-architect");
                    architectCaptain.State = CaptainStateEnum.Working;
                    architectCaptain = await testDb.Driver.Captains.CreateAsync(architectCaptain).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("dedupe-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("dedupe-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Plan", "Break this down");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.CaptainId = architectCaptain.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.BranchName = "armada/dedupe/architect";
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = architectCaptain.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
                    architectDock.BranchName = architect.BranchName;
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);
                    architect.DockId = architectDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(architect).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Placeholder", "Initial worker");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Placeholder", "Initial review");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = testEngineer.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    architectCaptain.CurrentMissionId = architect.Id;
                    architectCaptain.CurrentDockId = architectDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(architectCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "[ARMADA:MISSION] Add ops endpoint note to README\n" +
                        "Update README only.\n" +
                        "tokens used\n" +
                        "[ARMADA:MISSION] Add ops endpoint note to README\n" +
                        "12,883\n" +
                        "Update README only.";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    AssertEqual(4, afterArchitect.Count, "Repeated architect blocks should not create duplicate downstream missions");

                    int workerCount = afterArchitect.Count(m => m.Persona == "Worker");
                    int testCount = afterArchitect.Count(m => m.Persona == "TestEngineer");
                    int judgeCount = afterArchitect.Count(m => m.Persona == "Judge");
                    AssertEqual(1, workerCount, "Expected a single worker mission after de-duplication");
                    AssertEqual(1, testCount, "Expected a single test mission after de-duplication");
                    AssertEqual(1, judgeCount, "Expected a single judge mission after de-duplication");

                    Mission? preparedWorker = afterArchitect.FirstOrDefault(m => m.Persona == "Worker");
                    AssertNotNull(preparedWorker, "Expected worker mission to exist after de-duplication");
                    AssertFalse((preparedWorker!.Description ?? String.Empty).Contains("tokens used", StringComparison.OrdinalIgnoreCase),
                        "Worker mission description should not include architect token footer noise");
                    AssertFalse((preparedWorker.Description ?? String.Empty).Contains("12,883", StringComparison.Ordinal),
                        "Worker mission description should not include architect token counts");
                }
            });

            await RunTest("Architect title-only mission blocks keep downstream descriptions non-empty", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(2700 + git.WorktreeCalls.Count);

                    Vessel vessel = new Vessel("architect-title-only-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectCaptain = new Captain("architect-title-only-captain");
                    architectCaptain.State = CaptainStateEnum.Working;
                    architectCaptain = await testDb.Driver.Captains.CreateAsync(architectCaptain).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("architect-title-only-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("architect-title-only-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Plan", "Break this down");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.CaptainId = architectCaptain.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.BranchName = "armada/architect/title-only";
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = architectCaptain.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
                    architectDock.BranchName = architect.BranchName;
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);
                    architect.DockId = architectDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(architect).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Placeholder", "Initial worker");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Placeholder", "Initial review");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = testEngineer.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    architectCaptain.CurrentMissionId = architect.Id;
                    architectCaptain.CurrentDockId = architectDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(architectCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "[ARMADA:MISSION] REST and Postman examples for captain model validation and mission total runtime.\n\n" +
                        "[ARMADA:MISSION] MCP schema and response consistency for captain model handling.\n";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    Mission? firstWorker = afterArchitect.FirstOrDefault(m => m.Title == "REST and Postman examples for captain model validation and mission total runtime. [Worker]");
                    Mission? secondWorker = afterArchitect.FirstOrDefault(m => m.Title == "MCP schema and response consistency for captain model handling. [Worker]");

                    AssertNotNull(firstWorker, "Expected first worker mission to exist after architect handoff");
                    AssertNotNull(secondWorker, "Expected second worker mission to exist after architect handoff");
                    AssertContains("REST and Postman examples for captain model validation and mission total runtime.", firstWorker!.Description ?? String.Empty);
                    AssertContains("MCP schema and response consistency for captain model handling.", secondWorker!.Description ?? String.Empty);
                    AssertFalse((firstWorker.Description ?? String.Empty).EndsWith("<!-- ARMADA:ARCHITECT-HANDOFF -->", StringComparison.Ordinal), "Worker description should contain actionable text, not only the handoff marker");
                }
            });

            await RunTest("Architect handoff strips trailing ARMADA control signals from worker description", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(4000 + git.WorktreeCalls.Count);

                    Vessel vessel = new Vessel("signal-scrub-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectCaptain = new Captain("signal-scrub-architect");
                    architectCaptain.State = CaptainStateEnum.Working;
                    architectCaptain = await testDb.Driver.Captains.CreateAsync(architectCaptain).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("signal-scrub-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("signal-scrub-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Plan", "Break this down");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.CaptainId = architectCaptain.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.BranchName = "armada/signal-scrub/architect";
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = architectCaptain.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
                    architectDock.BranchName = architect.BranchName;
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);
                    architect.DockId = architectDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(architect).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Placeholder", "Initial worker");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Placeholder", "Initial review");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = testEngineer.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    architectCaptain.CurrentMissionId = architect.Id;
                    architectCaptain.CurrentDockId = architectDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(architectCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "[ARMADA:MISSION] Version bumps and release metadata\n" +
                        "Update version references and changelog.\n" +
                        "[ARMADA:PROGRESS] 100\n" +
                        "[ARMADA:STATUS] Review";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    Mission? preparedWorker = afterArchitect.FirstOrDefault(m => m.Title == "Version bumps and release metadata [Worker]");
                    AssertNotNull(preparedWorker, "Expected worker mission to exist after architect handoff");
                    AssertFalse((preparedWorker!.Description ?? String.Empty).Contains("[ARMADA:PROGRESS]", StringComparison.Ordinal),
                        "Worker mission description should not inherit architect progress control lines");
                    AssertFalse((preparedWorker.Description ?? String.Empty).Contains("[ARMADA:STATUS]", StringComparison.Ordinal),
                        "Worker mission description should not inherit architect status control lines");
                }
            });

            await RunTest("Architect handoff ignores placeholder example blocks", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(4000 + git.WorktreeCalls.Count);

                    Vessel vessel = new Vessel("placeholder-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectCaptain = new Captain("placeholder-architect");
                    architectCaptain.State = CaptainStateEnum.Working;
                    architectCaptain = await testDb.Driver.Captains.CreateAsync(architectCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("placeholder-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Plan", "Break this down");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.CaptainId = architectCaptain.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.BranchName = "armada/placeholder/architect";
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = architectCaptain.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, architect.Id);
                    architectDock.BranchName = architect.BranchName;
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);

                    architect.DockId = architectDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(architect).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Placeholder", "Initial worker");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Placeholder", "Initial review");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = testEngineer.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Provide the objective to decompose.\n" +
                        "Use any format you want. Once you send it, I'll return a mission breakdown like:\n" +
                        "[ARMADA:MISSION]\n" +
                        "title: ...\n" +
                        "goal: ...\n" +
                        "inputs: ...\n" +
                        "deliverables: ...\n" +
                        "dependencies: ...\n" +
                        "risks: ...\n" +
                        "done_when: ...\n" +
                        "[/ARMADA:MISSION]";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    Mission? updatedArchitect = await testDb.Driver.Missions.ReadAsync(architect.Id).ConfigureAwait(false);
                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);

                    AssertEqual(MissionStatusEnum.Failed, updatedArchitect!.Status, "Placeholder architect example should not create downstream missions");
                    AssertContains("no valid [ARMADA:MISSION] definitions", updatedArchitect.FailureReason ?? String.Empty);
                    AssertEqual(4, afterArchitect.Count, "Placeholder architect example should not fan out the pipeline");
                }
            });

            await RunTest("Architect fan-out workers do not inherit architect branch", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

                    Vessel vessel = new Vessel("fanout-branch-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel.AllowConcurrentMissions = true;
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectCaptain = new Captain("fanout-architect");
                    architectCaptain.State = CaptainStateEnum.Working;
                    architectCaptain = await testDb.Driver.Captains.CreateAsync(architectCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("fanout-branch-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Dock architectDock = new Dock(vessel.Id);
                    architectDock.CaptainId = architectCaptain.Id;
                    architectDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, "architect");
                    architectDock.BranchName = "armada/architect/shared";
                    architectDock.Active = true;
                    architectDock = await testDb.Driver.Docks.CreateAsync(architectDock).ConfigureAwait(false);

                    Mission architect = new Mission("[Architect] Split work", "Plan work");
                    architect.VesselId = vessel.Id;
                    architect.VoyageId = voyage.Id;
                    architect.Persona = "Architect";
                    architect.Status = MissionStatusEnum.InProgress;
                    architect.CaptainId = architectCaptain.Id;
                    architect.DockId = architectDock.Id;
                    architect.BranchName = architectDock.BranchName;
                    architect = await testDb.Driver.Missions.CreateAsync(architect).ConfigureAwait(false);

                    architectCaptain.CurrentMissionId = architect.Id;
                    architectCaptain.CurrentDockId = architectDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(architectCaptain).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Template worker", "Template");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.Pending;
                    worker.DependsOnMissionId = architect.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Template test", "Template");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "[ARMADA:MISSION] First task\nImplement first task\n" +
                        "[ARMADA:MISSION] Second task\nImplement second task";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> voyageMissions = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    List<Mission> workerMissions = voyageMissions
                        .Where(m => String.Equals(m.Persona, "Worker", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    AssertEqual(2, workerMissions.Count, "Architect fan-out should create two worker missions");
                    foreach (Mission workerMission in workerMissions)
                    {
                        AssertTrue(String.IsNullOrEmpty(workerMission.BranchName), "Architect-created worker missions should not inherit the architect branch");
                    }
                }
            });

            await RunTest("Architect sequenced worker mission defers until sibling worker missions settle", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(4000 + git.WorktreeCalls.Count);

                    Vessel vessel = new Vessel("sequenced-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel.AllowConcurrentMissions = true;
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("sequenced-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("sequenced-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission implementationA = new Mission("Implementation A", "Do the first implementation");
                    implementationA.VesselId = vessel.Id;
                    implementationA.VoyageId = voyage.Id;
                    implementationA.Persona = "Worker";
                    implementationA.Status = MissionStatusEnum.InProgress;
                    implementationA = await testDb.Driver.Missions.CreateAsync(implementationA).ConfigureAwait(false);

                    Mission implementationB = new Mission("Implementation B", "Do the second implementation");
                    implementationB.VesselId = vessel.Id;
                    implementationB.VoyageId = voyage.Id;
                    implementationB.Persona = "Worker";
                    implementationB.Status = MissionStatusEnum.Pending;
                    implementationB = await testDb.Driver.Missions.CreateAsync(implementationB).ConfigureAwait(false);

                    Mission docsMission = new Mission(
                        "Document the final behavior",
                        "Update README.md and REST_API.md after both implementation missions complete so the docs match the final shipped behavior.");
                    docsMission.VesselId = vessel.Id;
                    docsMission.VoyageId = voyage.Id;
                    docsMission.Persona = "Worker";
                    docsMission.Status = MissionStatusEnum.Pending;
                    docsMission = await testDb.Driver.Missions.CreateAsync(docsMission).ConfigureAwait(false);

                    bool assignedWhileSiblingActive = await missionService.TryAssignAsync(docsMission, vessel).ConfigureAwait(false);
                    AssertFalse(assignedWhileSiblingActive, "Sequenced docs mission should defer while sibling worker implementation missions are active or pending");

                    implementationA.Status = MissionStatusEnum.Complete;
                    implementationA.LastUpdateUtc = DateTime.UtcNow;
                    await testDb.Driver.Missions.UpdateAsync(implementationA).ConfigureAwait(false);

                    implementationB.Status = MissionStatusEnum.Complete;
                    implementationB.LastUpdateUtc = DateTime.UtcNow;
                    await testDb.Driver.Missions.UpdateAsync(implementationB).ConfigureAwait(false);

                    bool assignedAfterSiblingsSettled = await missionService.TryAssignAsync(docsMission, vessel).ConfigureAwait(false);
                    AssertTrue(assignedAfterSiblingsSettled, "Sequenced docs mission should assign once sibling worker missions are settled");
                }
            });

            await RunTest("Judge NEEDS_REVISION blocks landing and marks mission failed", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    int landingCalls = 0;
                    missionService.OnMissionComplete = (m, d) =>
                    {
                        landingCalls++;
                        return Task.CompletedTask;
                    };

                    Vessel vessel = new Vessel("judge-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain judgeCaptain = new Captain("judge-captain");
                    judgeCaptain.State = CaptainStateEnum.Working;
                    judgeCaptain = await testDb.Driver.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("judge-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Review worker output", "Review changes");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.InProgress;
                    judge.CaptainId = judgeCaptain.Id;
                    judge.BranchName = "armada/judge/review";
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    Dock judgeDock = new Dock(vessel.Id);
                    judgeDock.CaptainId = judgeCaptain.Id;
                    judgeDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, judge.Id);
                    judgeDock.BranchName = judge.BranchName;
                    judgeDock.Active = true;
                    judgeDock = await testDb.Driver.Docks.CreateAsync(judgeDock).ConfigureAwait(false);

                    judge.DockId = judgeDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                    judgeCaptain.CurrentMissionId = judge.Id;
                    judgeCaptain.CurrentDockId = judgeDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(judgeCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "NEEDS_REVISION\n" +
                        "Scope violation: test file should be removed.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "Judge NEEDS_REVISION should block landing");
                    AssertEqual("Judge verdict: NEEDS_REVISION", reloadedJudge.FailureReason, "Judge failure reason should preserve verdict");
                    AssertEqual(0, landingCalls, "Judge NEEDS_REVISION must not invoke landing");
                }
            });

            await RunTest("Judge parser ignores verdict legend echoed from instructions", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    int landingCalls = 0;
                    missionService.OnMissionComplete = (m, d) =>
                    {
                        landingCalls++;
                        return Task.CompletedTask;
                    };

                    Vessel vessel = new Vessel("judge-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain judgeCaptain = new Captain("judge-captain");
                    judgeCaptain.State = CaptainStateEnum.Working;
                    judgeCaptain = await testDb.Driver.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("judge-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Review worker output", "Review changes");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.InProgress;
                    judge.CaptainId = judgeCaptain.Id;
                    judge.BranchName = "armada/judge/review";
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    Dock judgeDock = new Dock(vessel.Id);
                    judgeDock.CaptainId = judgeCaptain.Id;
                    judgeDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, judge.Id);
                    judgeDock.BranchName = judge.BranchName;
                    judgeDock.Active = true;
                    judgeDock = await testDb.Driver.Docks.CreateAsync(judgeDock).ConfigureAwait(false);

                    judge.DockId = judgeDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                    judgeCaptain.CurrentMissionId = judge.Id;
                    judgeCaptain.CurrentDockId = judgeDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(judgeCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Verdict options:\n" +
                        "- **PASS** -- all requirements satisfied.\n" +
                        "- **FAIL** -- the branch is incorrect.\n" +
                        "- **NEEDS_REVISION** -- scope or quality issues remain.\n\n" +
                        "`NEEDS_REVISION`\n" +
                        "Scope violation: this branch contains unrelated files.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "Judge should honor the final NEEDS_REVISION verdict instead of the legend");
                    AssertEqual("Judge verdict: NEEDS_REVISION", reloadedJudge.FailureReason, "Judge failure reason should preserve verdict");
                    AssertEqual(0, landingCalls, "Judge NEEDS_REVISION must not invoke landing");
                }
            });

            await RunTest("Judge parser accepts structured ARMADA verdict signal", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    int landingCalls = 0;
                    missionService.OnMissionComplete = (m, d) =>
                    {
                        landingCalls++;
                        m.Status = MissionStatusEnum.Complete;
                        m.CompletedUtc = DateTime.UtcNow;
                        m.LastUpdateUtc = DateTime.UtcNow;
                        return testDb.Driver.Missions.UpdateAsync(m);
                    };

                    Vessel vessel = new Vessel("judge-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain judgeCaptain = new Captain("judge-captain");
                    judgeCaptain.State = CaptainStateEnum.Working;
                    judgeCaptain = await testDb.Driver.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("judge-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Review worker output", "Review changes");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.InProgress;
                    judge.CaptainId = judgeCaptain.Id;
                    judge.BranchName = "armada/judge/review";
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    Dock judgeDock = new Dock(vessel.Id);
                    judgeDock.CaptainId = judgeCaptain.Id;
                    judgeDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, judge.Id);
                    judgeDock.BranchName = judge.BranchName;
                    judgeDock.Active = true;
                    judgeDock = await testDb.Driver.Docks.CreateAsync(judgeDock).ConfigureAwait(false);

                    judge.DockId = judgeDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                    judgeCaptain.CurrentMissionId = judge.Id;
                    judgeCaptain.CurrentDockId = judgeDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(judgeCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "## Completeness\n" +
                        "The mission requirements are fully implemented with no missing scope items.\n\n" +
                        "## Correctness\n" +
                        "The reviewed changes are logically consistent and I do not see defects in the touched paths.\n\n" +
                        "## Tests\n" +
                        "Automated coverage exists for the new behavior and the affected scenarios are exercised.\n\n" +
                        "## Failure Modes\n" +
                        "I reviewed error and edge behavior for this scope and found no unaddressed safety issues.\n\n" +
                        "## Verdict\n" +
                        "[ARMADA:VERDICT] PASS\n" +
                        "Everything is complete and correctly scoped.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Complete, reloadedJudge!.Status, "Structured verdict signal should permit landing");
                    AssertEqual(1, landingCalls, "Structured PASS verdict should invoke landing");
                }
            });

            await RunTest("Judge parser accepts markdown verdict heading emitted by Claude", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    int landingCalls = 0;
                    missionService.OnMissionComplete = (m, d) =>
                    {
                        landingCalls++;
                        m.Status = MissionStatusEnum.Complete;
                        m.CompletedUtc = DateTime.UtcNow;
                        m.LastUpdateUtc = DateTime.UtcNow;
                        return testDb.Driver.Missions.UpdateAsync(m);
                    };

                    Vessel vessel = new Vessel("judge-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain judgeCaptain = new Captain("judge-captain");
                    judgeCaptain.State = CaptainStateEnum.Working;
                    judgeCaptain = await testDb.Driver.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("judge-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Review worker output", "Review changes");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.InProgress;
                    judge.CaptainId = judgeCaptain.Id;
                    judge.BranchName = "armada/judge/review";
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    Dock judgeDock = new Dock(vessel.Id);
                    judgeDock.CaptainId = judgeCaptain.Id;
                    judgeDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, judge.Id);
                    judgeDock.BranchName = judge.BranchName;
                    judgeDock.Active = true;
                    judgeDock = await testDb.Driver.Docks.CreateAsync(judgeDock).ConfigureAwait(false);

                    judge.DockId = judgeDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                    judgeCaptain.CurrentMissionId = judge.Id;
                    judgeCaptain.CurrentDockId = judgeDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(judgeCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "## Completeness\n" +
                        "Everything required by the mission is present and stays within the assigned scope.\n\n" +
                        "## Correctness\n" +
                        "The implementation follows the intended behavior and I did not find logic errors in the reviewed diff.\n\n" +
                        "## Tests\n" +
                        "The updated tests cover the changed behavior and are sufficient for this mission.\n\n" +
                        "## Failure Modes\n" +
                        "I explicitly reviewed edge and failure paths relevant to this change and found no remaining blockers.\n\n" +
                        "### Verdict: **PASS**\n" +
                        "The mission is complete and correct.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Complete, reloadedJudge!.Status, "Markdown verdict heading should permit landing");
                    AssertEqual(1, landingCalls, "PASS verdict emitted as a markdown heading should invoke landing");
                }
            });

            await RunTest("Judge parser accepts inline sentence verdict emitted by Claude", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    int landingCalls = 0;
                    missionService.OnMissionComplete = (m, d) =>
                    {
                        landingCalls++;
                        m.Status = MissionStatusEnum.Complete;
                        m.CompletedUtc = DateTime.UtcNow;
                        m.LastUpdateUtc = DateTime.UtcNow;
                        return testDb.Driver.Missions.UpdateAsync(m);
                    };

                    Vessel vessel = new Vessel("judge-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain judgeCaptain = new Captain("judge-captain");
                    judgeCaptain.State = CaptainStateEnum.Working;
                    judgeCaptain = await testDb.Driver.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("judge-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Review worker output", "Review changes");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.InProgress;
                    judge.CaptainId = judgeCaptain.Id;
                    judge.BranchName = "armada/judge/review";
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    Dock judgeDock = new Dock(vessel.Id);
                    judgeDock.CaptainId = judgeCaptain.Id;
                    judgeDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, judge.Id);
                    judgeDock.BranchName = judge.BranchName;
                    judgeDock.Active = true;
                    judgeDock = await testDb.Driver.Docks.CreateAsync(judgeDock).ConfigureAwait(false);

                    judge.DockId = judgeDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                    judgeCaptain.CurrentMissionId = judge.Id;
                    judgeCaptain.CurrentDockId = judgeDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(judgeCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "## Completeness\n" +
                        "Everything required by the mission is present and stays within the assigned scope.\n\n" +
                        "## Correctness\n" +
                        "The implementation follows the intended behavior and I did not find logic errors in the reviewed diff.\n\n" +
                        "## Tests\n" +
                        "The updated tests cover the changed behavior and are sufficient for this mission.\n\n" +
                        "## Failure Modes\n" +
                        "I explicitly reviewed edge and failure paths relevant to this change and found no remaining blockers.\n\n" +
                        "Judge review complete. Verdict: **PASS**. All 8 release surfaces are checked, negative paths are covered, and the REST_API.md drift fix stays within scope.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Complete, reloadedJudge!.Status, "Inline sentence verdict should permit landing");
                    AssertEqual(1, landingCalls, "Sentence-style PASS verdict should invoke landing");
                }
            });

            await RunTest("Judge failure emits failure telemetry instead of work produced", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    int landingCalls = 0;
                    missionService.OnMissionComplete = (m, d) =>
                    {
                        landingCalls++;
                        return Task.CompletedTask;
                    };

                    Vessel vessel = new Vessel("judge-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain judgeCaptain = new Captain("judge-captain");
                    judgeCaptain.State = CaptainStateEnum.Working;
                    judgeCaptain = await testDb.Driver.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("judge-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Review worker output", "Review changes");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.InProgress;
                    judge.CaptainId = judgeCaptain.Id;
                    judge.BranchName = "armada/judge/review";
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    Dock judgeDock = new Dock(vessel.Id);
                    judgeDock.CaptainId = judgeCaptain.Id;
                    judgeDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, judge.Id);
                    judgeDock.BranchName = judge.BranchName;
                    judgeDock.Active = true;
                    judgeDock = await testDb.Driver.Docks.CreateAsync(judgeDock).ConfigureAwait(false);

                    judge.DockId = judgeDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                    judgeCaptain.CurrentMissionId = judge.Id;
                    judgeCaptain.CurrentDockId = judgeDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(judgeCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "## Completeness\n" +
                        "The reviewed work is missing part of the required contract alignment.\n\n" +
                        "## Correctness\n" +
                        "The update path can still drop omitted settings, so I cannot approve it yet.\n\n" +
                        "## Tests\n" +
                        "Coverage is still missing for the omitted-field preservation path.\n\n" +
                        "## Failure Modes\n" +
                        "This can silently erase stored captain configuration during partial updates.\n\n" +
                        "Judge review complete. Verdict: NEEDS_REVISION. The REST and MCP update contracts still diverge.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "Judge failure should block landing");
                    AssertEqual("Judge verdict: NEEDS_REVISION", reloadedJudge.FailureReason, "Failure reason should preserve the verdict");
                    AssertEqual(0, landingCalls, "Judge failure must not invoke landing");

                    List<Signal> signals = await testDb.Driver.Signals.EnumerateRecentAsync(10).ConfigureAwait(false);
                    AssertFalse(
                        signals.Any(s => s.Type == SignalTypeEnum.Completion && s.Payload == "Work produced: " + judge.Title),
                        "Failed judge review should not emit a completion signal");
                    AssertTrue(
                        signals.Any(s => s.Type == SignalTypeEnum.Error && (s.Payload ?? String.Empty).Contains("Judge verdict: NEEDS_REVISION")),
                        "Failed judge review should emit an error signal with the failure reason");

                    List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateRecentAsync(10).ConfigureAwait(false);
                    AssertFalse(
                        events.Any(e => e.EventType == "mission.work_produced" && e.MissionId == judge.Id),
                        "Failed judge review should not emit a mission.work_produced event");
                    AssertTrue(
                        events.Any(e => e.EventType == "mission.failed" && e.MissionId == judge.Id),
                        "Failed judge review should emit a mission.failed event");
                }
            });

            await RunTest("Judge PASS requires structured review sections before landing", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    int landingCalls = 0;
                    missionService.OnMissionComplete = (m, d) =>
                    {
                        landingCalls++;
                        m.Status = MissionStatusEnum.Complete;
                        m.CompletedUtc = DateTime.UtcNow;
                        m.LastUpdateUtc = DateTime.UtcNow;
                        return testDb.Driver.Missions.UpdateAsync(m);
                    };

                    Vessel vessel = new Vessel("judge-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain judgeCaptain = new Captain("judge-captain");
                    judgeCaptain.State = CaptainStateEnum.Working;
                    judgeCaptain = await testDb.Driver.Captains.CreateAsync(judgeCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("judge-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission judge = new Mission("[Judge] Review worker output", "Review changes");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.InProgress;
                    judge.CaptainId = judgeCaptain.Id;
                    judge.BranchName = "armada/judge/review";
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    Dock judgeDock = new Dock(vessel.Id);
                    judgeDock.CaptainId = judgeCaptain.Id;
                    judgeDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, judge.Id);
                    judgeDock.BranchName = judge.BranchName;
                    judgeDock.Active = true;
                    judgeDock = await testDb.Driver.Docks.CreateAsync(judgeDock).ConfigureAwait(false);

                    judge.DockId = judgeDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                    judgeCaptain.CurrentMissionId = judge.Id;
                    judgeCaptain.CurrentDockId = judgeDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(judgeCaptain).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Looks good overall.\n" +
                        "[ARMADA:VERDICT] PASS\n";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "PASS without structured review sections should be rejected");
                    AssertEqual(0, landingCalls, "Rejected PASS review should not invoke landing");
                    AssertContains("missing required review sections", reloadedJudge.FailureReason, "Failure reason should explain why the PASS review was rejected");
                }
            });

            await RunTest("Completion backfills missing branch from dock before handoff", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => Task.FromResult(2000 + git.WorktreeCalls.Count);

                    Vessel vessel = new Vessel("branch-backfill-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("branch-backfill-worker");
                    workerCaptain.State = CaptainStateEnum.Working;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Captain reviewerCaptain = new Captain("branch-backfill-reviewer");
                    reviewerCaptain.State = CaptainStateEnum.Idle;
                    reviewerCaptain = await testDb.Driver.Captains.CreateAsync(reviewerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("branch-backfill-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission worker = new Mission("Implement branch-sensitive change", "Change code");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.InProgress;
                    worker.CaptainId = workerCaptain.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Dock workerDock = new Dock(vessel.Id);
                    workerDock.CaptainId = workerCaptain.Id;
                    workerDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, worker.Id);
                    workerDock.BranchName = "armada/backfill/shared";
                    workerDock.Active = true;
                    workerDock = await testDb.Driver.Docks.CreateAsync(workerDock).ConfigureAwait(false);

                    worker.DockId = workerDock.Id;
                    worker.LastUpdateUtc = DateTime.UtcNow;
                    await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);

                    workerCaptain.CurrentMissionId = worker.Id;
                    workerCaptain.CurrentDockId = workerDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(workerCaptain).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[TestEngineer] Review branch handoff", "Write tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "TestEngineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ => "worker complete";

                    await missionService.HandleCompletionAsync(workerCaptain, worker.Id).ConfigureAwait(false);

                    Mission? completedWorker = await testDb.Driver.Missions.ReadAsync(worker.Id).ConfigureAwait(false);
                    Mission? handedOffTest = await testDb.Driver.Missions.ReadAsync(testEngineer.Id).ConfigureAwait(false);
                    AssertNotNull(completedWorker, "Completed worker mission should remain readable");
                    AssertNotNull(handedOffTest, "Dependent test mission should remain readable");
                    AssertEqual(workerDock.BranchName, completedWorker!.BranchName, "Completion should backfill the mission branch from the dock");
                    AssertEqual(workerDock.BranchName, handedOffTest!.BranchName, "Downstream handoff should inherit the backfilled branch");
                    AssertTrue((handedOffTest.Description ?? String.Empty).Contains("Branch: " + workerDock.BranchName), "Handoff context should show the recovered branch");
                    AssertEqual(MissionStatusEnum.InProgress, handedOffTest.Status, "Dependent stage should dispatch once branch context is restored");
                }
            });

            await RunTest("Completion fails mission that modifies files outside scoped list", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.LogDirectory = Path.Combine(Path.GetTempPath(), "armada_test_logs_" + Guid.NewGuid().ToString("N"));
                    StubGitService git = new StubGitService();
                    git.ChangedFilesSinceResult = new[]
                    {
                        "src/Armada.Server/Routes/CaptainRoutes.cs",
                        "src/Armada.Server/Routes/MissionRoutes.cs",
                        "CODEX.md"
                    };
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);

                    bool landingCalled = false;
                    missionService.OnMissionComplete = (m, d) =>
                    {
                        landingCalled = true;
                        return Task.CompletedTask;
                    };
                    missionService.OnGetMissionOutput = _ => "worker complete";

                    Vessel vessel = new Vessel("scope-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("scope-worker");
                    workerCaptain.State = CaptainStateEnum.Working;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("scope-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission worker = new Mission(
                        "REST route scope check",
                        "Touch only src/Armada.Server/Routes/CaptainRoutes.cs, docs/REST_API.md, and Armada.postman_collection.json.");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.InProgress;
                    worker.CaptainId = workerCaptain.Id;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Dock workerDock = new Dock(vessel.Id);
                    workerDock.CaptainId = workerCaptain.Id;
                    workerDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, worker.Id);
                    workerDock.BranchName = "armada/scope/shared";
                    workerDock.Active = true;
                    workerDock = await testDb.Driver.Docks.CreateAsync(workerDock).ConfigureAwait(false);

                    worker.DockId = workerDock.Id;
                    worker.LastUpdateUtc = DateTime.UtcNow;
                    await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);

                    workerCaptain.CurrentMissionId = worker.Id;
                    workerCaptain.CurrentDockId = workerDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(workerCaptain).ConfigureAwait(false);

                    Mission dependent = new Mission("[TestEngineer] Downstream should be cancelled", "Write tests");
                    dependent.VesselId = vessel.Id;
                    dependent.VoyageId = voyage.Id;
                    dependent.Persona = "TestEngineer";
                    dependent.Status = MissionStatusEnum.Pending;
                    dependent.DependsOnMissionId = worker.Id;
                    dependent = await testDb.Driver.Missions.CreateAsync(dependent).ConfigureAwait(false);

                    Directory.CreateDirectory(Path.Combine(settings.LogDirectory, "docks"));
                    await File.WriteAllTextAsync(
                        Path.Combine(settings.LogDirectory, "docks", workerDock.Id + ".start"),
                        "abc123def456\n").ConfigureAwait(false);

                    await missionService.HandleCompletionAsync(workerCaptain, worker.Id).ConfigureAwait(false);

                    Mission? failedWorker = await testDb.Driver.Missions.ReadAsync(worker.Id).ConfigureAwait(false);
                    Mission? cancelledDependent = await testDb.Driver.Missions.ReadAsync(dependent.Id).ConfigureAwait(false);

                    AssertNotNull(failedWorker, "Failed worker mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, failedWorker!.Status, "Out-of-scope file changes should fail the mission");
                    AssertContains("MissionRoutes.cs", failedWorker.FailureReason, "Failure reason should list the out-of-scope file");
                    AssertNotNull(cancelledDependent, "Dependent mission should remain readable");
                    AssertEqual(MissionStatusEnum.Cancelled, cancelledDependent!.Status, "Dependent missions should be cancelled when scope validation fails");
                    AssertFalse(landingCalled, "Scope validation failure should block landing");
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

            await RunTest("No eligible persona captain leaves mission pending", async () =>
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

                    Vessel vessel = new Vessel("no-eligible-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain architectOnly = new Captain("architect-only");
                    architectOnly.State = CaptainStateEnum.Idle;
                    architectOnly.AllowedPersonas = "[\"Architect\"]";
                    await testDb.Driver.Captains.CreateAsync(architectOnly).ConfigureAwait(false);

                    Mission workerMission = new Mission("Implement worker task");
                    workerMission.VesselId = vessel.Id;
                    workerMission.Persona = "Worker";
                    workerMission.Status = MissionStatusEnum.Pending;
                    workerMission = await testDb.Driver.Missions.CreateAsync(workerMission).ConfigureAwait(false);

                    bool assigned = await missionService.TryAssignAsync(workerMission, vessel).ConfigureAwait(false);
                    AssertFalse(assigned, "Mission should remain pending when no captain is eligible for the requested persona");

                    Mission? reloaded = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                    AssertNotNull(reloaded, "Mission should remain readable after failed assignment");
                    AssertNull(reloaded!.CaptainId, "No ineligible captain should be assigned");
                    AssertEqual(MissionStatusEnum.Pending, reloaded.Status, "Mission should stay Pending when no persona-compatible captain exists");
                }
            });

            await RunTest("Launch failure reclaims dock and leaves no active dock", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => throw new InvalidOperationException("synthetic launch failure");

                    Vessel vessel = new Vessel("launch-failure-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("launch-failure-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain.AllowedPersonas = "[\"Worker\"]";
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Mission workerMission = new Mission("Launch failure mission");
                    workerMission.VesselId = vessel.Id;
                    workerMission.Persona = "Worker";
                    workerMission.Status = MissionStatusEnum.Pending;
                    workerMission = await testDb.Driver.Missions.CreateAsync(workerMission).ConfigureAwait(false);

                    bool assigned = await missionService.TryAssignAsync(workerMission, vessel).ConfigureAwait(false);

                    Mission? reloadedMission = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                    Captain? reloadedCaptain = await testDb.Driver.Captains.ReadAsync(workerCaptain.Id).ConfigureAwait(false);
                    List<Dock> docks = await testDb.Driver.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);

                    AssertFalse(assigned, "Assignment should fail when the runtime launch throws");
                    AssertNotNull(reloadedMission, "Mission should remain readable after launch failure");
                    AssertEqual(MissionStatusEnum.Pending, reloadedMission!.Status, "Mission should return to Pending after launch failure");
                    AssertNull(reloadedMission.CaptainId, "Mission should clear the captain after launch failure");
                    AssertNull(reloadedMission.DockId, "Mission should clear the dock after launch failure");
                    AssertNotNull(reloadedCaptain, "Captain should remain readable after launch failure");
                    AssertEqual(CaptainStateEnum.Idle, reloadedCaptain!.State, "Captain should be released back to Idle after launch failure");
                    AssertEqual(0, docks.Count(d => d.Active), "Launch failure should not leave behind an active dock");
                }
            });

            await RunTest("Duplicate TryAssignAsync does not reprovision same mission", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    TaskCompletionSource<bool> createStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    TaskCompletionSource<bool> releaseCreate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    git.OnCreateWorktreeAsync = async () =>
                    {
                        createStarted.TrySetResult(true);
                        await releaseCreate.Task.ConfigureAwait(false);
                    };

                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    int launchCalls = 0;
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) =>
                    {
                        launchCalls++;
                        return Task.FromResult(12345);
                    };

                    Vessel vessel = new Vessel("duplicate-assign-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("duplicate-worker");
                    workerCaptain.State = CaptainStateEnum.Idle;
                    workerCaptain.AllowedPersonas = "[\"Worker\"]";
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Mission workerMission = new Mission("Duplicate assignment probe");
                    workerMission.VesselId = vessel.Id;
                    workerMission.Persona = "Worker";
                    workerMission.Status = MissionStatusEnum.Pending;
                    workerMission = await testDb.Driver.Missions.CreateAsync(workerMission).ConfigureAwait(false);

                    Mission? stalePendingCopy = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                    AssertNotNull(stalePendingCopy, "Stale mission copy should be readable before assignment");

                    Task<bool> firstAssignTask = missionService.TryAssignAsync(workerMission, vessel);
                    await createStarted.Task.ConfigureAwait(false);

                    bool secondAssigned = await missionService.TryAssignAsync(stalePendingCopy!, vessel).ConfigureAwait(false);

                    releaseCreate.TrySetResult(true);
                    bool firstAssigned = await firstAssignTask.ConfigureAwait(false);

                    Mission? reloadedMission = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                    List<Dock> docks = await testDb.Driver.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);

                    AssertTrue(firstAssigned, "First assignment should succeed");
                    AssertFalse(secondAssigned, "Duplicate assignment should be skipped");
                    AssertNotNull(reloadedMission, "Mission should remain readable");
                    AssertEqual(MissionStatusEnum.InProgress, reloadedMission!.Status, "Mission should only be assigned once");
                    AssertEqual(1, git.WorktreeCalls.Count, "Mission should provision only one worktree");
                    AssertEqual(1, launchCalls, "Mission should launch only one agent process");
                    AssertEqual(1, docks.Count(d => d.Active), "Only one active dock should remain for the mission");
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

            /// <summary>Branch names used for worktree creation.</summary>
            public List<string> WorktreeBranches { get; } = new List<string>();

            /// <summary>Branch names deleted from the bare repository.</summary>
            public List<string> DeletedLocalBranches { get; } = new List<string>();

            /// <summary>Branch names deleted from the remote repository.</summary>
            public List<string> DeletedRemoteBranches { get; } = new List<string>();

            /// <summary>Optional hook invoked during worktree creation.</summary>
            public Func<Task>? OnCreateWorktreeAsync { get; set; }

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
                WorktreeBranches.Add(branchName);
                return OnCreateWorktreeAsync?.Invoke() ?? Task.CompletedTask;
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
            public Task DeleteLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default)
            {
                DeletedLocalBranches.Add(branchName);
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public Task DeleteRemoteBranchAsync(string repoPath, string branchName, CancellationToken token = default)
            {
                DeletedRemoteBranches.Add(branchName);
                return Task.CompletedTask;
            }

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
            public Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(string worktreePath, string startCommit, CancellationToken token = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            /// <inheritdoc />
            public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default)
                => Task.FromResult(branchName == "main" || WorktreeBranches.Contains(branchName));

            /// <inheritdoc />
            public Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default)
                => BranchExistsAsync(repoPath, branchName, token);

            /// <inheritdoc />
            public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);
        }

        #endregion
    }
}
