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

            await RunTest("Dependent mission waits for pipeline handoff when dependency is WorkProduced", async () =>
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
                    Vessel vessel = new Vessel("handoff-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    // Create an idle captain
                    Captain captain = new Captain("handoff-captain");
                    captain.State = CaptainStateEnum.Idle;
                    await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                    // Upstream mission has finished but downstream handoff has not stamped the branch yet
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
                        AssertFalse(assignedBeforeHandoff, "Worker mission should not be assigned before handoff stamps the branch");

                        workerMission.BranchName = architectMission.BranchName;
                        await testDb.Driver.Missions.UpdateAsync(workerMission).ConfigureAwait(false);

                        bool assignedAfterHandoff = await missionService.TryAssignAsync(workerMission, vessel).ConfigureAwait(false);
                        AssertTrue(assignedAfterHandoff, "Worker mission should assign after handoff stamps the branch");

                        Mission? assignedMission = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                        AssertNotNull(assignedMission, "Assigned worker mission should be readable");
                        AssertEqual(architectMission.BranchName, assignedMission!.BranchName, "Dependent mission should continue on the architect branch");
                        AssertTrue(git.WorktreeBranches.Contains(architectMission.BranchName!), "Provisioned worktree should use the inherited handoff branch");
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
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);

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
                        Captain? updatedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                        Dock? updatedDock = await testDb.Driver.Docks.ReadAsync(dock.Id).ConfigureAwait(false);

                        AssertNotNull(updatedArchitect, "Architect mission should still exist");
                        AssertEqual(MissionStatusEnum.Failed, updatedArchitect!.Status, "Architect mission should fail when no mission markers are produced");
                        AssertContains("no [ARMADA:MISSION] markers", updatedArchitect.FailureReason ?? String.Empty);
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

            await RunTest("Architect fan-out clones full downstream chain and lands only terminal stage", async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
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

                    Mission? apiWorker = afterArchitect.FirstOrDefault(m => m.Title == "Add API endpoint [Worker]");
                    Mission? apiTest = afterArchitect.FirstOrDefault(m => m.Title == "[TestEngineer] Placeholder");
                    Mission? apiJudge = afterArchitect.FirstOrDefault(m => m.Title == "[Judge] Placeholder");
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
                    AssertEqual(architect.BranchName, docsWorker.BranchName, "Cloned worker should inherit architect branch");

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
                    missionService.OnGetMissionOutput = _ => "PASS";
                    await missionService.HandleCompletionAsync(judgeCaptain!, apiJudge.Id).ConfigureAwait(false);

                    apiJudge = await testDb.Driver.Missions.ReadAsync(apiJudge.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Complete, apiJudge!.Status, "Judge completion should trigger terminal landing");
                    AssertEqual(1, landingCalls, "Only the terminal judge stage should land");
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

            /// <summary>Branch names used for worktree creation.</summary>
            public List<string> WorktreeBranches { get; } = new List<string>();

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
            public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default)
                => Task.FromResult(branchName == "main" || WorktreeBranches.Contains(branchName));

            /// <inheritdoc />
            public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);
        }

        #endregion
    }
}
