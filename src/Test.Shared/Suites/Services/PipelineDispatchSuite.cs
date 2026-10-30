namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database.Sqlite;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for pipeline-aware dispatch: multi-stage pipeline fan-out, dependency and
    /// handoff gating, persona-aware captain routing, architect summary parsing, judge verdict
    /// parsing, branch backfill, scope enforcement, and launch/duplicate-assignment safety. Each
    /// case builds a fresh SQLite store via <see cref="TestDatabaseHelper"/> and wires
    /// <see cref="MissionService"/> with git stubs and launch/completion seams. Negative cases
    /// cover skipped/blocked assignment, cancelled voyages, architect handoff failures, judge
    /// FAIL/NEEDS_REVISION rejections, out-of-scope changes, and launch failures.
    /// </summary>
    public sealed class PipelineDispatchSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.PipelineDispatch";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Pipeline Dispatch suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("stage_handoff_is_bounded_and_preserves_artifact_references", "Large stage evidence stays bounded without hiding full-artifact access", TestTags.Positive, async () =>
            {
                await Task.CompletedTask;
                Mission prior = new Mission("Implement provider support", "Scoped acceptance criteria");
                prior.Persona = "Worker";
                prior.BranchName = "armada/provider-fix";
                prior.CommitHash = "abc123";
                prior.AgentOutput = "Requirement summary\n" + new string('x', 100000) + "\nUnresolved: missing key guard\n[ARMADA:VERDICT] NEEDS_REVISION";
                prior.DiffSnapshot = new string('D', 200000);
                string context = PipelineHandoffContext.Build(prior);
                AssertTrue(context.Length < 1500, "full output and diff must not inflate the persisted handoff");
                AssertContains("Branch: armada/provider-fix", context, "exact source branch");
                AssertContains("Commit: abc123", context, "exact source commit");
                AssertContains(PipelineHandoffContext.ArtifactPlaceholder, context, "artifact rendered at launch");
                AssertContains(prior.Id, context, "complete artifacts remain discoverable");
                AssertContains("handoff-artifacts", context, "artifact retrieval reference");
                AssertFalse(context.Contains(new string('x', 100)), "no output excerpt persisted");
                AssertFalse(context.Contains(new string('D', 100)), "cumulative diff omitted");
                string filled = PipelineHandoffContext.Fill("Task" + context, "RENDERED BLOCK");
                AssertContains("RENDERED BLOCK", filled, "placeholder replaced");
                AssertFalse(filled.Contains(PipelineHandoffContext.ArtifactPlaceholder), "placeholder consumed");
                AssertFalse(filled.Contains("rendered into the mission instructions at launch"), "placeholder note consumed");
                AssertEqual("legacy text", PipelineHandoffContext.Fill("legacy text", "RENDERED BLOCK"), "description without placeholder unchanged");
            }));

            cases.Add(CaseAsync("direct_dispatch_sanitizes_captain_names_in_mission_branches", "Direct dispatch sanitizes captain names in mission branches", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("wildcard_captain_override_pins_any_persona", "A wildcard captain override pins the captain for any persona/step", TestTags.Positive, async () =>
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

                    Vessel vessel = new Vessel("wildcard-vessel", "https://github.com/test/repo.git");
                    vessel.DefaultBranch = "main";
                    vessel.AllowConcurrentMissions = true;
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain target = new Captain("Target Captain");
                    target.State = CaptainStateEnum.Idle;
                    target = await testDb.Driver.Captains.CreateAsync(target).ConfigureAwait(false);

                    Armada.Core.Models.Voyage voyage = new Armada.Core.Models.Voyage("wildcard voyage", "d");
                    voyage.CaptainOverridesJson = MissionService.SerializeCaptainOverrides(new List<Armada.Core.Models.CaptainAssignmentOverride>
                    {
                        new Armada.Core.Models.CaptainAssignmentOverride { Persona = "*", CaptainId = target.Id }
                    });
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission mission = new Mission("[Judge] review the work", "Review it.");
                    mission.VesselId = vessel.Id;
                    mission.VoyageId = voyage.Id;
                    mission.Persona = "Judge";
                    mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                    bool assigned = await missionService.TryAssignAsync(mission, vessel).ConfigureAwait(false);
                    Mission? reloaded = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);

                    AssertTrue(assigned, "Mission should be assigned.");
                    AssertNotNull(reloaded, "Assigned mission should persist.");
                    AssertEqual(target.Id, reloaded!.RequestedCaptainId ?? String.Empty);
                    AssertEqual(target.Id, reloaded.CaptainId ?? String.Empty);
                }
            }));

            cases.Add(CaseAsync("try_assign_async_skips_vessels_reusing_local_and_working_directory_path", "TryAssignAsync skips vessels that reuse the same local and working directory path", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (_, _, _) => Task.FromResult(12345);

                    string workingDirectory = Path.Combine(Path.GetTempPath(), "armada_shared_dispatch_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(workingDirectory);

                    try
                    {
                        Vessel vessel = new Vessel("shared-path-vessel", "https://github.com/test/repo.git");
                        vessel.LocalPath = workingDirectory;
                        vessel.WorkingDirectory = workingDirectory;
                        vessel.DefaultBranch = "main";
                        vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                        Captain captain = new Captain("shared-path-captain");
                        captain.State = CaptainStateEnum.Idle;
                        captain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                        Mission mission = new Mission("Shared path dispatch probe");
                        mission.VesselId = vessel.Id;
                        mission.Status = MissionStatusEnum.Pending;
                        mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                        bool assigned = await missionService.TryAssignAsync(mission, vessel).ConfigureAwait(false);

                        Mission? reloadedMission = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                        Captain? reloadedCaptain = await testDb.Driver.Captains.ReadAsync(captain.Id).ConfigureAwait(false);
                        List<Dock> docks = await testDb.Driver.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);

                        AssertFalse(assigned, "Mission should remain pending when LocalPath and WorkingDirectory share the same path");
                        AssertNotNull(reloadedMission, "Mission should remain readable after skipped assignment");
                        AssertEqual(MissionStatusEnum.Pending, reloadedMission!.Status, "Mission should stay Pending");
                        AssertNull(reloadedMission.CaptainId, "Mission should not claim a captain");
                        AssertNull(reloadedMission.DockId, "Mission should not provision a dock");
                        AssertNotNull(reloadedCaptain, "Captain should remain readable");
                        AssertEqual(CaptainStateEnum.Idle, reloadedCaptain!.State, "Captain should remain Idle");
                        AssertEqual(0, docks.Count, "Skipped assignment should not create any dock records");
                        AssertEqual(0, git.WorktreeCalls.Count, "Skipped assignment should not provision any worktrees");
                    }
                    finally
                    {
                        try { Directory.Delete(workingDirectory, true); } catch { }
                    }
                }
            }));

            cases.Add(CaseAsync("dispatch_single_stage_pipeline_creates_normal_missions", "Dispatch with single-stage pipeline creates normal missions", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("dispatch_multi_stage_pipeline_creates_chained_missions", "Dispatch with multi-stage pipeline creates chained missions", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("dependency_check_blocks_assignment_of_dependent_mission", "Dependency check blocks assignment of dependent mission", TestTags.Negative, async () =>
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
            }));

            cases.Add(CaseAsync("pending_mission_on_cancelled_voyage_is_cancelled_instead_of_assigned", "Pending mission on cancelled voyage is cancelled instead of assigned", TestTags.Negative, async () =>
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
                    AssertEqual(MissionFailureKindEnum.DependencyFailed, updatedMission.FailureKind, "failure kind is recorded where the failure happens");
                    AssertEqual(CaptainStateEnum.Idle, updatedCaptain!.State, "Captain should remain idle when assignment is skipped");
                }
            }));

            cases.Add(CaseAsync("dependent_mission_waits_for_pipeline_handoff_when_dependency_is_work_produced", "Dependent mission waits for pipeline handoff when dependency is WorkProduced", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("failed_stage_blocks_dependents_when_configured", "With BlockDependentsOfFailedStages on, a failed stage's dependents are Blocked, the voyage stays open, and restarting the failed stage returns them to Pending", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0;
                    settings.BlockDependentsOfFailedStages = true;
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);
                    missionService.OnMissionComplete = (Mission mission, Dock dock) => Task.CompletedTask;
                    missionService.OnGetMissionOutput = _ => "Architect summary without mission markers";

                    Vessel vessel = new Vessel("blocked-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                    Captain captain = await testDb.Driver.Captains.CreateAsync(new Captain("blocked-captain") { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("blocked-voyage")).ConfigureAwait(false);
                    Dock dock = await testDb.Driver.Docks.CreateAsync(new Dock(vessel.Id)
                    {
                        CaptainId = captain.Id, WorktreePath = Path.Combine(Path.GetTempPath(), "armada_test_wt_" + Guid.NewGuid().ToString("N")), BranchName = "armada/blocked/arch", Active = true
                    }).ConfigureAwait(false);
                    Mission architect = await testDb.Driver.Missions.CreateAsync(new Mission("[Architect] Plan", "Plan")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, CaptainId = captain.Id, DockId = dock.Id, Persona = "Architect", Status = MissionStatusEnum.InProgress, BranchName = dock.BranchName
                    }).ConfigureAwait(false);
                    Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Placeholder", "Work")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", DependsOnMissionId = architect.Id
                    }).ConfigureAwait(false);
                    Mission judge = await testDb.Driver.Missions.CreateAsync(new Mission("[Judge] Placeholder", "Review")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = worker.Id
                    }).ConfigureAwait(false);
                    captain.CurrentMissionId = architect.Id;
                    captain.CurrentDockId = dock.Id;
                    await testDb.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);

                    await missionService.HandleCompletionAsync(captain, architect.Id).ConfigureAwait(false);

                    AssertEqual(MissionStatusEnum.Failed, (await testDb.Driver.Missions.ReadAsync(architect.Id).ConfigureAwait(false))!.Status);
                    Mission? blockedWorker = await testDb.Driver.Missions.ReadAsync(worker.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Blocked, blockedWorker!.Status, "the waiting Worker is Blocked, not Cancelled");
                    AssertEqual(MissionFailureKindEnum.DependencyFailed, blockedWorker.FailureKind);
                    AssertContains("Restart that mission", blockedWorker.FailureReason ?? "");
                    AssertNull(blockedWorker.CompletedUtc, "a Blocked mission has not finished");
                    AssertEqual(MissionStatusEnum.Blocked, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.Status, "the stage after it is Blocked too");
                    Voyage? open = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    AssertNotEqual(VoyageStatusEnum.Failed, open!.Status, "Blocked stages keep the voyage open");

                    await missionService.RestartAsync((await testDb.Driver.Missions.ReadAsync(architect.Id).ConfigureAwait(false))!).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Pending, (await testDb.Driver.Missions.ReadAsync(worker.Id).ConfigureAwait(false))!.Status, "restarting the failed stage unblocks the Worker");
                    AssertEqual(MissionStatusEnum.Pending, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.Status, "and the stage after it");
                    AssertTrue(MissionStateMachine.IsValidTransition(MissionStatusEnum.Blocked, MissionStatusEnum.Cancelled), "cancelling a voyage can cancel a Blocked stage");
                    AssertFalse(MissionStateMachine.IsValidTransition(MissionStatusEnum.Blocked, MissionStatusEnum.Assigned), "a Blocked stage is never dispatched");
                }
            }));

            cases.Add(CaseAsync("architect_failure_during_handoff_does_not_trigger_landing", "Architect failure during handoff does not trigger landing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                        AssertEqual(MissionFailureKindEnum.InvalidOutput, updatedArchitect.FailureKind, "failure kind is recorded where the failure happens");
                        AssertNotNull(updatedWorker, "Dependent worker mission should still exist");
                        AssertEqual(MissionStatusEnum.Cancelled, updatedWorker!.Status, "Dependent worker mission should be cancelled when architect handoff fails");
                        AssertContains("Blocked by failed dependency", updatedWorker.FailureReason ?? String.Empty);
                        AssertEqual(MissionFailureKindEnum.DependencyFailed, updatedWorker.FailureKind, "failure kind is recorded where the failure happens");
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
            }));

            cases.Add(CaseAsync("invalid_versioned_plan_never_fans_out", "Invalid structured contract blocks fan-out, cancels dependents and reports remediation", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                    missionService.OnGetMissionOutput = _ => "```armada-plan\n{\"contractVersion\":2,\"missions\":[{\"title\":\"Unsafe\",\"description\":\"No criteria\"}]}\n```\n[ARMADA:MISSION] Never fall back";

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
                        AssertContains("Architect plan contract rejected", updatedArchitect.FailureReason ?? String.Empty);
                        AssertEqual(MissionFailureKindEnum.InvalidOutput, updatedArchitect.FailureKind, "failure kind is recorded where the failure happens");
                        AssertNotNull(updatedWorker, "Dependent worker mission should still exist");
                        AssertEqual(MissionStatusEnum.Cancelled, updatedWorker!.Status, "Dependent worker mission should be cancelled when architect handoff fails");
                        AssertContains("Blocked by failed dependency", updatedWorker.FailureReason ?? String.Empty);
                        AssertEqual(MissionFailureKindEnum.DependencyFailed, updatedWorker.FailureKind, "failure kind is recorded where the failure happens");
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
            }));

            cases.Add(CaseAsync("orphaned_decision_never_fans_out", "Invalid structured contract blocks fan-out, cancels dependents and reports remediation", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                    missionService.OnGetMissionOutput = _ => "```armada-plan\n{\"contractVersion\": 3, \"requirements\": [\"R1\"], \"decisions\": [\"Transport injection seam is required.\"], \"missions\": [{\"title\": \"Integration\", \"description\": \"Validate all paths\", \"kind\": \"integration\", \"waitForOtherMissions\": true, \"validationCommands\": [\"dotnet test\"], \"acceptanceCriteria\": [{\"id\": \"AC1\", \"requirementIds\": [\"R1\"], \"decisionIndexes\": [], \"scenario\": \"Read response\", \"expected\": \"Success\", \"verification\": \"ReadResponse test\", \"negativeCases\": [\"401 rejected\"]}]}]}\n```\n[ARMADA:MISSION] Never fall back";

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
                        AssertContains("Architect plan contract rejected", updatedArchitect.FailureReason ?? String.Empty);
                        AssertContains("decision D1 has no acceptance check", updatedArchitect.FailureReason ?? String.Empty);
                        AssertEqual(MissionFailureKindEnum.InvalidOutput, updatedArchitect.FailureKind, "failure kind is recorded where the failure happens");
                        AssertNotNull(updatedWorker, "Dependent worker mission should still exist");
                        AssertEqual(MissionStatusEnum.Cancelled, updatedWorker!.Status, "Dependent worker mission should be cancelled when architect handoff fails");
                        AssertContains("Blocked by failed dependency", updatedWorker.FailureReason ?? String.Empty);
                        AssertEqual(MissionFailureKindEnum.DependencyFailed, updatedWorker.FailureKind, "failure kind is recorded where the failure happens");
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
            }));

            cases.Add(CaseAsync("architect_numbered_summary_without_plan_gets_repair_not_fan_out", "Architect numbered narration without a plan block is never fanned out; the Architect gets an output repair turn", TestTags.Negative, async () =>
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
                        "5. **Version bump to 0.8.0 across all version locations** (LOW) -- Helm csproj, compose.yaml, Postman, REST_API.md, MCP_API.md\n" +
                        "6. **Documentation updates: REST_API.md, MCP_API.md, Postman collection for new fields** (MEDIUM) -- API docs for model and totalRuntimeMs fields\n" +
                        "7. **README and CHANGELOG updates for v0.8.0** (LOW) -- Release notes and feature documentation\n" +
                        "Missions 1 and 2 are foundational. Missions 3-4 depend on 1. Missions 5-7 can run in parallel with each other and with 3-4.";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    List<Mission> workerMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Worker", StringComparison.OrdinalIgnoreCase)).ToList();
                    List<Mission> testMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Test Engineer", StringComparison.OrdinalIgnoreCase)).ToList();
                    List<Mission> judgeMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Judge", StringComparison.OrdinalIgnoreCase)).ToList();

                    AssertEqual(4, afterArchitect.Count, "Numbered narration must not create any missions");
                    AssertEqual(1, workerMissions.Count, "Only the original Worker slot exists");
                    AssertEqual("[Worker] Placeholder", workerMissions[0].Title, "The Worker slot is not overwritten from narration");
                    AssertEqual(MissionStatusEnum.Pending, workerMissions[0].Status, "The Worker keeps waiting for a real plan");

                    Mission? repaired = await testDb.Driver.Missions.ReadAsync(architect.Id).ConfigureAwait(false);
                    AssertNotNull(repaired);
                    AssertEqual(1, repaired!.OutputRepairAttempts, "The Architect gets one output repair turn");
                    AssertTrue(repaired.Status != MissionStatusEnum.Failed, "The first contract violation is repaired, not failed");
                    AssertContains("Output contract repair (attempt 1 of 1)", repaired.Description ?? String.Empty);
                    AssertContains("armada-plan", repaired.Description ?? String.Empty, "The repair note quotes the required plan format");
                    AssertNull(repaired.AgentOutput, "The unusable output is cleared before the repair turn");

                    Captain? releasedArchitect = await testDb.Driver.Captains.ReadAsync(architectCaptain.Id).ConfigureAwait(false);
                    AssertTrue(releasedArchitect!.CurrentMissionId != architect.Id || releasedArchitect.State == CaptainStateEnum.Working,
                        "The Architect captain is released from the unusable attempt (or re-assigned to the repair turn)");
                }
            }));

            cases.Add(CaseAsync("architect_markdown_mission_headings_without_plan_fail_closed", "Markdown mission headings without a plan block fail closed once repair is exhausted, creating no missions", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0;
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
                    List<Mission> testMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Test Engineer", StringComparison.OrdinalIgnoreCase)).ToList();
                    List<Mission> judgeMissions = afterArchitect.Where(m => String.Equals(m.Persona, "Judge", StringComparison.OrdinalIgnoreCase)).ToList();

                    AssertEqual(4, afterArchitect.Count, "Markdown narration must not create any missions");
                    AssertEqual(1, workerMissions.Count, "Only the original Worker slot exists");

                    Mission? failedArchitect = await testDb.Driver.Missions.ReadAsync(architect.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, failedArchitect!.Status, "With repair exhausted the Architect fails closed");
                    AssertEqual(MissionFailureKindEnum.InvalidOutput, failedArchitect.FailureKind);
                    AssertContains("armada-plan", failedArchitect.FailureReason ?? String.Empty, "The failure names the missing plan block");
                    foreach (Mission downstream in afterArchitect.Where(m => m.Id != architect.Id))
                        AssertEqual(MissionStatusEnum.Cancelled, downstream.Status, "Downstream stages settle instead of waiting forever");
                }
            }));

            cases.Add(CaseAsync("architect_fan_out_clones_full_downstream_chain_and_lands_only_terminal_stage", "Architect fan-out clones full downstream chain and lands only terminal stage", TestTags.Positive, async () =>
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
                    worker.Mode = MissionModeEnum.Audit;
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer.Mode = MissionModeEnum.Audit;
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
                    Mission? apiTest = afterArchitect.FirstOrDefault(m => m.Title == "Add API endpoint [Test Engineer]");
                    Mission? apiJudge = afterArchitect.FirstOrDefault(m => m.Title == "Add API endpoint [Judge]");
                    Mission? docsWorker = afterArchitect.FirstOrDefault(m => m.Title == "Update docs [Worker]");
                    Mission? docsTest = afterArchitect.FirstOrDefault(m => m.Title == "Update docs [Test Engineer]");
                    Mission? docsJudge = afterArchitect.FirstOrDefault(m => m.Title == "Update docs [Judge]");

                    AssertNotNull(apiWorker, "Primary worker should exist");
                    AssertNotNull(apiTest, "Primary test stage should exist");
                    AssertNotNull(apiJudge, "Primary judge stage should exist");
                    AssertNotNull(docsWorker, "Secondary worker should exist");
                    AssertNotNull(docsTest, "Secondary test stage should exist");
                    AssertNotNull(docsJudge, "Secondary judge stage should exist");
                    AssertTrue(apiWorker!.Mode == MissionModeEnum.Audit
                        && apiTest!.Mode == MissionModeEnum.Audit
                        && apiJudge!.Mode == MissionModeEnum.Implementation
                        && docsWorker!.Mode == MissionModeEnum.Audit
                        && docsTest!.Mode == MissionModeEnum.Audit
                        && docsJudge!.Mode == MissionModeEnum.Implementation,
                        "Every Architect-created worker and cloned stage should keep its template mode; actual modes: "
                        + String.Join(",", new[] { apiWorker.Mode.ToString(), apiTest.Mode.ToString(), apiJudge.Mode.ToString(), docsWorker.Mode.ToString(), docsTest.Mode.ToString(), docsJudge.Mode.ToString() }));
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
                    missionService.OnGetMissionOutput = _ => "worker complete\n[ARMADA:RESULT] COMPLETE";
                    await missionService.HandleCompletionAsync(workerCaptain!, apiWorker.Id).ConfigureAwait(false);

                    apiWorker = await testDb.Driver.Missions.ReadAsync(apiWorker.Id).ConfigureAwait(false);
                    apiTest = await testDb.Driver.Missions.ReadAsync(apiTest!.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, apiWorker!.Status, "Worker should remain WorkProduced until downstream review completes");
                    AssertEqual(0, landingCalls, "Worker completion should not trigger landing");
                    AssertEqual(MissionStatusEnum.InProgress, apiTest!.Status, "Test stage should start after worker completion");

                    Captain? testCaptain = await testDb.Driver.Captains.ReadAsync(apiTest.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ => "test complete\n[ARMADA:RESULT] COMPLETE";
                    await missionService.HandleCompletionAsync(testCaptain!, apiTest.Id).ConfigureAwait(false);

                    apiTest = await testDb.Driver.Missions.ReadAsync(apiTest.Id).ConfigureAwait(false);
                    apiJudge = await testDb.Driver.Missions.ReadAsync(apiJudge!.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.WorkProduced, apiTest!.Status, "Test Engineer should remain WorkProduced until judge completes");
                    AssertEqual(0, landingCalls, "Test Engineer completion should not trigger landing");
                    AssertEqual(MissionStatusEnum.InProgress, apiJudge!.Status, "Judge should start after Test Engineer completion");

                    Captain? judgeCaptain = await testDb.Driver.Captains.ReadAsync(apiJudge.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ =>
                        "## Blast Radius\n" +
                        "The staged work covers the assigned requirements and there are no missing deliverables in this chain.\n\n" +
                        "## Correctness\n" +
                        "The implementation and test updates are coherent, and I do not see logic or scope defects in the reviewed diff.\n\n" +
                        "## Source Fidelity\n" +
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
            }));

            cases.Add(CaseAsync("architect_plan_json_drives_dependencies_and_deferral", "Architect armada-plan JSON drives dependencies and wait-for-others sequencing", TestTags.Positive, async () =>
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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

                    ArchitectPlan plan = new ArchitectPlan
                    {
                        ContractVersion = 3,
                        Requirements = new List<string> { "REQ-PERSIST" },
                        Decisions = new List<string> { "Preserve explicit stored values." }
                    };
                    string[] titles = { "Add core model properties", "Extend secondary backends", "Document the final behavior" };
                    for (int i = 0; i < titles.Length; i++) plan.Missions.Add(new ArchitectPlanMission
                    {
                        Title = titles[i],
                        Description = "Update relevant components; run dotnet run --project src/Test.Automated.",
                        Kind = i == 2 ? "integration" : "implementation",
                        DependsOn = i == 1 ? 1 : null,
                        WaitForOtherMissions = i == 2,
                        ValidationCommands = new List<string> { "dotnet run --project src/Test.Automated" },
                        AcceptanceCriteria = new List<ArchitectAcceptanceCriterion> { new ArchitectAcceptanceCriterion
                        {
                            DecisionIndexes = new List<int> { 1 },
                            Id = "AC-" + (i + 1), RequirementIds = new List<string> { "REQ-PERSIST" },
                            Scenario = "Save and reload explicit false", Expected = "False remains false",
                            Verification = "SettingsRoundTrip checks the persisted value",
                            NegativeCases = new List<string> { "Invalid input does not change stored settings" }
                        } }
                    });
                    missionService.OnGetMissionOutput = _ => "```armada-plan\n" + JsonSerializer.Serialize(plan)
                        + "\n```\n[ARMADA:MISSION] Ignored legacy block\nThis is ignored.";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    Mission? coreWorker = afterArchitect.FirstOrDefault(m => m.Title == "Add core model properties [Worker]");
                    Mission? coreJudge = afterArchitect.FirstOrDefault(m => m.Title == "Add core model properties [Judge]");
                    Mission? backendWorker = afterArchitect.FirstOrDefault(m => m.Title == "Extend secondary backends [Worker]");
                    Mission? docsWorker = afterArchitect.FirstOrDefault(m => m.Title == "Document the final behavior [Worker]");
                    Mission? ignored = afterArchitect.FirstOrDefault(m => m.Title == "Ignored legacy block [Worker]");

                    AssertNotNull(coreWorker, "Primary worker should exist");
                    AssertContains("Decisions checked: D1", coreWorker!.Description!);
                    AssertContains("Decisions checked: D1", coreJudge!.Description!);
                    AssertContains("AC-1", coreWorker!.Description ?? String.Empty, "fan-out retains acceptance criteria inside parsed description");
                    AssertContains("run dotnet run --project", coreWorker.Description ?? String.Empty, "fan-out retains execution instructions");
                    AssertNotNull(coreJudge, "Primary judge stage should exist");
                    AssertContains("REQ-PERSIST", coreJudge!.Description!, "Judge retains objective traceability");
                    AssertContains("GET /api/v1/missions/" + architect.Id, coreWorker.Description!, "Full plan remains retrievable without copying it");
                    AssertContains("Invalid input does not change stored settings", coreJudge.Description!, "Judge retains negative cases");
                    AssertContains("Preserve explicit stored values", coreWorker.Description!, "Worker retains resolved decisions");
                    AssertNotNull(backendWorker, "Dependent worker should exist");
                    AssertNotNull(docsWorker, "Deferred docs worker should exist");
                    AssertNull(ignored, "Legacy marker blocks are not read when a structured plan is present");
                    AssertEqual(coreJudge!.Id, backendWorker!.DependsOnMissionId, "dependsOn: 1 waits for mission 1's terminal stage, not the text in the description");
                    AssertFalse(coreWorker!.WaitForVoyageWorkers, "missions without waitForOtherMissions are not deferred");
                    AssertTrue(docsWorker!.WaitForVoyageWorkers, "waitForOtherMissions sets the structured flag");
                    AssertEqual(MissionStatusEnum.InProgress, coreWorker.Status, "Primary worker should auto-dispatch after architect completion");
                    AssertEqual(MissionStatusEnum.Pending, backendWorker.Status, "Dependent worker should remain pending until the upstream chain completes");
                }
            }));

            cases.Add(CaseAsync("interrupted_architect_fanout_is_finished_without_duplicates", "An Architect fan-out interrupted by a restart is finished by recovery: unstarted fan-out runs, missing slices and stage chains are created once, and started slices are not rewired", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);

                    Vessel vessel = await testDb.Driver.Vessels.CreateAsync(new Vessel("fanout-recovery-vessel", "https://github.com/test/repo.git")).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("fanout-recovery-voyage")).ConfigureAwait(false);

                    ArchitectPlan plan = new ArchitectPlan { ContractVersion = 3, Requirements = new List<string> { "REQ-1" }, Decisions = new List<string> { "Keep it small." } };
                    string[] titles = { "Core slice", "Second slice", "Integrate slices" };
                    for (int i = 0; i < titles.Length; i++) plan.Missions.Add(new ArchitectPlanMission
                    {
                        Title = titles[i], Description = "Do the work; run dotnet test.", Kind = i == 2 ? "integration" : "implementation",
                        DependsOn = i == 1 ? 1 : null, WaitForOtherMissions = i == 2, ValidationCommands = new List<string> { "dotnet test" },
                        AcceptanceCriteria = new List<ArchitectAcceptanceCriterion> { new ArchitectAcceptanceCriterion
                        {
                            Id = "AC-" + (i + 1), RequirementIds = new List<string> { "REQ-1" }, DecisionIndexes = new List<int> { 1 },
                            Scenario = "Run it", Expected = "It works", Verification = "A test checks it", NegativeCases = new List<string> { "Bad input is rejected" }
                        } }
                    });

                    Mission architect = await testDb.Driver.Missions.CreateAsync(new Mission("[Architect] Plan", "Break this down")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Architect", Status = MissionStatusEnum.WorkProduced,
                        AgentOutput = "```armada-plan\n" + JsonSerializer.Serialize(plan) + "\n```"
                    }).ConfigureAwait(false);
                    Mission placeholder = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Placeholder", "Initial worker")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", DependsOnMissionId = architect.Id
                    }).ConfigureAwait(false);
                    await testDb.Driver.Missions.CreateAsync(new Mission("[Judge] Placeholder", "Initial review")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = placeholder.Id
                    }).ConfigureAwait(false);

                    // A restart before the fan-out started: recovery runs it.
                    AssertEqual(1, await missionService.RecoverDanglingHandoffsAsync().ConfigureAwait(false), "the unstarted fan-out is run");
                    List<Mission> fanned = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    foreach (string title in titles) AssertEqual(1, fanned.Count(m => m.Title == title + " [Worker]"), title);
                    AssertContains("armada:fanout-complete", (await testDb.Driver.Missions.ReadAsync(architect.Id).ConfigureAwait(false))!.Description ?? "");
                    AssertEqual(0, await missionService.RecoverDanglingHandoffsAsync().ConfigureAwait(false), "a completed fan-out is left alone");

                    // A restart part-way through: the third slice was never created, the second lost its stage chain, and the
                    // first already started.
                    Mission third = fanned.Single(m => m.Title == "Integrate slices [Worker]");
                    foreach (Mission stage in fanned.Where(m => m.DependsOnMissionId == third.Id)) await testDb.Driver.Missions.DeleteAsync(stage.Id).ConfigureAwait(false);
                    await testDb.Driver.Missions.DeleteAsync(third.Id).ConfigureAwait(false);
                    Mission second = fanned.Single(m => m.Title == "Second slice [Worker]");
                    foreach (Mission stage in fanned.Where(m => m.DependsOnMissionId == second.Id)) await testDb.Driver.Missions.DeleteAsync(stage.Id).ConfigureAwait(false);
                    second.DependsOnMissionId = architect.Id;
                    await testDb.Driver.Missions.UpdateAsync(second).ConfigureAwait(false);
                    Mission first = fanned.Single(m => m.Title == "Core slice [Worker]");
                    first.Status = MissionStatusEnum.InProgress;
                    first.BranchName = "armada/core";
                    await testDb.Driver.Missions.UpdateAsync(first).ConfigureAwait(false);
                    Mission interrupted = (await testDb.Driver.Missions.ReadAsync(architect.Id).ConfigureAwait(false))!;
                    interrupted.Description = "Break this down";
                    await testDb.Driver.Missions.UpdateAsync(interrupted).ConfigureAwait(false);

                    AssertEqual(1, await missionService.RecoverDanglingHandoffsAsync().ConfigureAwait(false), "the interrupted fan-out is finished");
                    List<Mission> repaired = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    foreach (string title in titles) AssertEqual(1, repaired.Count(m => m.Title == title + " [Worker]"), "exactly one Worker for " + title);
                    Mission rebuilt = repaired.Single(m => m.Title == "Integrate slices [Worker]");
                    AssertTrue(rebuilt.WaitForVoyageWorkers, "the recreated integration slice keeps its barrier");
                    AssertTrue(repaired.Any(m => m.DependsOnMissionId == rebuilt.Id && m.Persona == "Judge"), "the recreated slice has its Judge");
                    Mission secondAfter = repaired.Single(m => m.Id == second.Id);
                    AssertTrue(repaired.Any(m => m.DependsOnMissionId == second.Id && m.Persona == "Judge"), "the slice that lost its chain gets one");
                    Mission coreJudge = repaired.Single(m => m.DependsOnMissionId == first.Id && m.Persona == "Judge");
                    AssertEqual(coreJudge.Id, secondAfter.DependsOnMissionId, "dependsOn: 1 is applied to the slice that has not started");
                    Mission firstAfter = repaired.Single(m => m.Id == first.Id);
                    AssertEqual(MissionStatusEnum.InProgress, firstAfter.Status);
                    AssertEqual("armada/core", firstAfter.BranchName, "a started slice is never rewired");
                    AssertEqual(0, await missionService.RecoverDanglingHandoffsAsync().ConfigureAwait(false));
                    AssertEqual(repaired.Count, (await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false)).Count, "nothing is created twice");
                }
            }));

            cases.Add(CaseAsync("architect_legacy_marker_deferral_phrase_sets_structured_flag", "Legacy marker-format deferral wording is converted once into the structured flag", TestTags.Positive, async () =>
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
                        "[ARMADA:MISSION] Document the final behavior\n" +
                        "Update README.md after both implementation missions complete so the docs match.";

                    await missionService.HandleCompletionAsync(architectCaptain, architect.Id).ConfigureAwait(false);

                    List<Mission> afterArchitect = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    Mission? coreWorker = afterArchitect.FirstOrDefault(m => m.Title == "Add core model properties [Worker]");
                    Mission? docsWorker = afterArchitect.FirstOrDefault(m => m.Title == "Document the final behavior [Worker]");
                    AssertNotNull(coreWorker);
                    AssertNotNull(docsWorker);
                    AssertFalse(coreWorker!.WaitForVoyageWorkers);
                    AssertTrue(docsWorker!.WaitForVoyageWorkers, "the legacy fallback converts the wording into the flag at parse time");
                }
            }));

            cases.Add(CaseAsync("architect_fan_out_honors_explicit_mission_dependencies_across_worker_chains", "Architect fan-out honors explicit mission dependencies across worker chains", TestTags.Positive, async () =>
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
                    Mission? coreTest = afterArchitect.FirstOrDefault(m => m.Title == "Add core model properties [Test Engineer]");
                    Mission? coreJudge = afterArchitect.FirstOrDefault(m => m.Title == "Add core model properties [Judge]");
                    Mission? backendWorker = afterArchitect.FirstOrDefault(m => m.Title == "Extend secondary backends [Worker]");
                    Mission? backendTest = afterArchitect.FirstOrDefault(m => m.Title == "Extend secondary backends [Test Engineer]");
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
                    missionService.OnGetMissionOutput = _ => "core worker complete\n[ARMADA:RESULT] COMPLETE";
                    await missionService.HandleCompletionAsync(activeWorkerCaptain!, coreWorker.Id).ConfigureAwait(false);

                    coreTest = await testDb.Driver.Missions.ReadAsync(coreTest!.Id).ConfigureAwait(false);
                    backendWorker = await testDb.Driver.Missions.ReadAsync(backendWorker.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, coreTest!.Status, "Primary test stage should start after the primary worker");
                    AssertEqual(MissionStatusEnum.Pending, backendWorker!.Status, "Dependent worker should still wait while upstream review is running");

                    Captain? activeTestCaptain = await testDb.Driver.Captains.ReadAsync(coreTest.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ => "core tests complete\n[ARMADA:RESULT] COMPLETE";
                    await missionService.HandleCompletionAsync(activeTestCaptain!, coreTest.Id).ConfigureAwait(false);

                    coreJudge = await testDb.Driver.Missions.ReadAsync(coreJudge.Id).ConfigureAwait(false);
                    backendWorker = await testDb.Driver.Missions.ReadAsync(backendWorker.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.InProgress, coreJudge!.Status, "Primary judge should start after the primary test stage");
                    AssertEqual(MissionStatusEnum.Pending, backendWorker!.Status, "Dependent worker should still wait while the primary judge is running");

                    Captain? activeJudgeCaptain = await testDb.Driver.Captains.ReadAsync(coreJudge.CaptainId!).ConfigureAwait(false);
                    missionService.OnGetMissionOutput = _ =>
                        "## Blast Radius\n" +
                        "The upstream worker and test stages completed the requested scope and nothing material is missing.\n\n" +
                        "## Correctness\n" +
                        "I reviewed the diff and prior output and did not find correctness issues in the completed upstream chain.\n\n" +
                        "## Source Fidelity\n" +
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
            }));

            cases.Add(CaseAsync("architect_handoff_de_duplicates_repeated_mission_blocks", "Architect handoff de-duplicates repeated mission blocks", TestTags.Positive, async () =>
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
                    int testCount = afterArchitect.Count(m => m.Persona == "Test Engineer");
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
            }));

            cases.Add(CaseAsync("architect_title_only_mission_blocks_keep_downstream_descriptions_non_empty", "Architect title-only mission blocks keep downstream descriptions non-empty", TestTags.Positive, async () =>
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
            }));

            cases.Add(CaseAsync("architect_handoff_strips_trailing_armada_control_signals_from_worker_description", "Architect handoff strips trailing ARMADA control signals from worker description", TestTags.Positive, async () =>
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
            }));

            cases.Add(CaseAsync("architect_handoff_ignores_placeholder_example_blocks", "Architect handoff ignores placeholder example blocks", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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

                    Mission testEngineer = new Mission("[Test Engineer] Placeholder", "Initial tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
                    AssertEqual(MissionFailureKindEnum.InvalidOutput, updatedArchitect.FailureKind, "failure kind is recorded where the failure happens");
                    AssertEqual(4, afterArchitect.Count, "Placeholder architect example should not fan out the pipeline");
                }
            }));

            cases.Add(CaseAsync("architect_fan_out_workers_do_not_inherit_architect_branch", "Architect fan-out workers do not inherit architect branch", TestTags.Positive, async () =>
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
                    architect.Mode = MissionModeEnum.Audit;
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

                    Mission testEngineer = new Mission("[Test Engineer] Template test", "Template");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
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
                    AssertTrue(workerMissions.All(m => m.Mode == MissionModeEnum.Implementation), "Generated workers should keep the template worker mode instead of the Architect mode");
                    Mission? clonedTest = voyageMissions.FirstOrDefault(m => m.Title == "Second task [Test Engineer]");
                    AssertNotNull(clonedTest, "The downstream test stage should be cloned");
                    AssertEqual(MissionModeEnum.Implementation, clonedTest!.Mode, "Cloned stages should keep their own template mode");
                    Persona workerPersona = new Persona("Worker", "persona.worker");
                    workerPersona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Mux, Model = "gpt-oss:120b", Endpoint = "fanout" });
                    workerPersona.PriorityAvailability = PersonaPriorityAvailabilityEnum.WaitForFirstPriority;
                    await testDb.Driver.Personas.CreateAsync(workerPersona).ConfigureAwait(false);
                    ((CaptainService)captainService).OnLaunchAgent = (c, m, d) => Task.FromResult(12345);
                    for (int index = 0; index < workerMissions.Count; index++)
                    {
                        Captain mux = new Captain("fanout-mux-" + index, AgentRuntimeEnum.Mux);
                        mux.Model = "gpt-oss:120b";
                        mux.RuntimeOptionsJson = "{\"endpoint\":\"fanout\"}";
                        await testDb.Driver.Captains.CreateAsync(mux).ConfigureAwait(false);
                    }
                    foreach (Mission workerMission in workerMissions)
                    {
                        AssertTrue(String.IsNullOrEmpty(workerMission.BranchName), "Architect-created worker missions should not inherit the architect branch");
                        AssertTrue(await missionService.TryAssignAsync(workerMission, vessel).ConfigureAwait(false), "Fan-out worker should use its persona hierarchy");
                        Mission assigned = (await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false))!;
                        Captain selected = (await testDb.Driver.Captains.ReadAsync(assigned.CaptainId!).ConfigureAwait(false))!;
                        AssertEqual(AgentRuntimeEnum.Mux, selected.Runtime);
                        AssertNull(assigned.RequestedCaptainId, "Fan-out hierarchy should not persist an explicit override");
                    }
                }
            }));

            cases.Add(CaseAsync("architect_sequenced_worker_mission_defers_until_sibling_worker_missions_settle", "Architect sequenced worker mission defers until sibling worker missions settle", TestTags.Positive, async () =>
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
                    docsMission.WaitForVoyageWorkers = true;
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

                    implementationB.Status = MissionStatusEnum.WorkProduced;
                    await testDb.Driver.Missions.UpdateAsync(implementationB).ConfigureAwait(false);
                    Mission siblingJudge = await testDb.Driver.Missions.CreateAsync(new Mission("Review implementation B", "Review")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge",
                        DependsOnMissionId = implementationB.Id, Status = MissionStatusEnum.Pending
                    }).ConfigureAwait(false);
                    await testDb.Driver.Missions.CreateAsync(new Mission("Review docs", "Review")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge",
                        DependsOnMissionId = docsMission.Id, Status = MissionStatusEnum.Pending
                    }).ConfigureAwait(false);
                    await testDb.Driver.Missions.CreateAsync(new Mission("Plan", "Plan")
                    {
                        VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Architect", Status = MissionStatusEnum.WorkProduced
                    }).ConfigureAwait(false);
                    AssertFalse(await missionService.TryAssignAsync(docsMission, vessel).ConfigureAwait(false),
                        "A produced Worker does not release final documentation while its Judge is pending");
                    MissionAssignmentBlocker? barrier = await missionService.GetAssignmentBlockerAsync(docsMission).ConfigureAwait(false);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers, barrier!.Reason);
                    AssertContains("review", barrier.Summary);
                    siblingJudge.Status = MissionStatusEnum.Review;
                    await testDb.Driver.Missions.UpdateAsync(siblingJudge).ConfigureAwait(false);
                    AssertFalse(await missionService.TryAssignAsync(docsMission, vessel).ConfigureAwait(false),
                        "A pending review decision still blocks final documentation");
                    siblingJudge.Status = MissionStatusEnum.Complete;
                    await testDb.Driver.Missions.UpdateAsync(siblingJudge).ConfigureAwait(false);

                    bool assignedAfterSiblingsSettled = await missionService.TryAssignAsync(docsMission, vessel).ConfigureAwait(false);
                    AssertTrue(assignedAfterSiblingsSettled, "Documentation starts after sibling reviews and ignores its own pending Judge and produced Architect");
                }
            }));

            cases.Add(CaseAsync("description_wording_alone_does_not_defer_worker", "Deferral wording in a description without the structured flag does not defer the mission", TestTags.Negative, async () =>
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

                    // Before the fix six English phrases in any Worker description deferred it; only the structured
                    // WaitForVoyageWorkers flag does now.
                    bool assigned = await missionService.TryAssignAsync(docsMission, vessel).ConfigureAwait(false);
                    AssertTrue(assigned, "A mission is not deferred by description wording alone");
                }
            }));

            cases.Add(CaseAsync("judge_needs_revision_blocks_landing_and_marks_mission_failed", "Judge NEEDS_REVISION blocks landing and marks mission failed", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                        "[ARMADA:VERDICT] NEEDS_REVISION\n" +
                        "Scope violation: test file should be removed.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "Judge NEEDS_REVISION should block landing");
                    AssertContains("Judge verdict: NEEDS_REVISION", reloadedJudge.FailureReason!, "Judge failure reason should preserve verdict");
                    AssertEqual(MissionFailureKindEnum.JudgeRejected, reloadedJudge.FailureKind, "failure kind is recorded where the failure happens");
                    AssertEqual(0, landingCalls, "Judge NEEDS_REVISION must not invoke landing");
                }
            }));

            cases.Add(CaseAsync("judge_parser_ignores_verdict_legend_echoed_from_instructions", "Judge parser ignores verdict legend echoed from instructions", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                        "- **NEEDS_REVISION** -- scope or quality issues remain.\n" +
                        "- `[ARMADA:VERDICT] PASS` -- judge approves the mission\n" +
                        "```\n[ARMADA:VERDICT] PASS\n```\n\n" +
                        "[ARMADA:VERDICT] NEEDS_REVISION\n" +
                        "Scope violation: this branch contains unrelated files.\n" +
                        "PASS";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "Judge should honor the final NEEDS_REVISION verdict instead of the legend");
                    AssertContains("Judge verdict: NEEDS_REVISION", reloadedJudge.FailureReason!, "Judge failure reason should preserve verdict");
                    AssertEqual(MissionFailureKindEnum.JudgeRejected, reloadedJudge.FailureKind, "failure kind is recorded where the failure happens");
                    AssertEqual(0, landingCalls, "Judge NEEDS_REVISION must not invoke landing");
                }
            }));

            cases.Add(CaseAsync("judge_parser_accepts_structured_armada_verdict_signal", "Judge parser accepts structured ARMADA verdict signal", TestTags.Positive, async () =>
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
                        "## Blast Radius\n" +
                        "The mission requirements are fully implemented with no missing scope items.\n\n" +
                        "## Correctness\n" +
                        "The reviewed changes are logically consistent and I do not see defects in the touched paths.\n\n" +
                        "## Source Fidelity\n" +
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
            }));

            cases.Add(CaseAsync("judge_parser_rejects_markdown_verdict_heading_without_protocol_line", "Judge parser does not land on a markdown verdict heading without the protocol line", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                        "## Blast Radius\n" +
                        "Everything required by the mission is present and stays within the assigned scope.\n\n" +
                        "## Correctness\n" +
                        "The implementation follows the intended behavior and I did not find logic errors in the reviewed diff.\n\n" +
                        "## Source Fidelity\n" +
                        "The updated tests cover the changed behavior and are sufficient for this mission.\n\n" +
                        "## Failure Modes\n" +
                        "I explicitly reviewed edge and failure paths relevant to this change and found no remaining blockers.\n\n" +
                        "### Verdict: **PASS**\n" +
                        "The mission is complete and correct.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "A prose verdict heading is not a structured verdict and must not land");
                    AssertContains("did not emit an explicit PASS verdict", reloadedJudge.FailureReason ?? String.Empty, "Missing structured verdict is reported");
                    AssertEqual(MissionFailureKindEnum.JudgeRejected, reloadedJudge.FailureKind);
                    AssertEqual(0, landingCalls, "Only the [ARMADA:VERDICT] line can permit landing");
                }
            }));

            cases.Add(CaseAsync("judge_parser_rejects_inline_sentence_verdict_without_protocol_line", "Judge parser does not land on an inline sentence verdict without the protocol line", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                        "## Blast Radius\n" +
                        "Everything required by the mission is present and stays within the assigned scope.\n\n" +
                        "## Correctness\n" +
                        "The implementation follows the intended behavior and I did not find logic errors in the reviewed diff.\n\n" +
                        "## Source Fidelity\n" +
                        "The updated tests cover the changed behavior and are sufficient for this mission.\n\n" +
                        "## Failure Modes\n" +
                        "I explicitly reviewed edge and failure paths relevant to this change and found no remaining blockers.\n\n" +
                        "Judge review complete. Verdict: **PASS**. All 8 release surfaces are checked, negative paths are covered, and the REST_API.md drift fix stays within scope.";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "A sentence verdict is not a structured verdict and must not land");
                    AssertContains("did not emit an explicit PASS verdict", reloadedJudge.FailureReason ?? String.Empty, "Missing structured verdict is reported");
                    AssertEqual(MissionFailureKindEnum.JudgeRejected, reloadedJudge.FailureKind);
                    AssertEqual(0, landingCalls, "Only the [ARMADA:VERDICT] line can permit landing");
                }
            }));

            cases.Add(CaseAsync("judge_failure_emits_failure_telemetry_instead_of_work_produced", "Judge failure emits failure telemetry instead of work produced", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                        "## Blast Radius\n" +
                        "The reviewed work is missing part of the required contract alignment.\n\n" +
                        "## Correctness\n" +
                        "The update path can still drop omitted settings, so I cannot approve it yet.\n\n" +
                        "## Source Fidelity\n" +
                        "Coverage is still missing for the omitted-field preservation path.\n\n" +
                        "## Failure Modes\n" +
                        "This can silently erase stored captain configuration during partial updates.\n\n" +
                        "Judge review complete. Verdict: NEEDS_REVISION. The REST and MCP update contracts still diverge.\n" +
                        "[ARMADA:VERDICT] NEEDS_REVISION";

                    await missionService.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);

                    Mission? reloadedJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertNotNull(reloadedJudge, "Judge mission should remain readable");
                    AssertEqual(MissionStatusEnum.Failed, reloadedJudge!.Status, "Judge failure should block landing");
                    AssertContains("Judge verdict: NEEDS_REVISION", reloadedJudge.FailureReason!, "Failure reason should preserve the verdict");
                    AssertEqual(MissionFailureKindEnum.JudgeRejected, reloadedJudge.FailureKind, "failure kind is recorded where the failure happens");
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
            }));

            cases.Add(CaseAsync("judge_pass_requires_structured_review_sections_before_landing", "Judge PASS requires structured review sections before landing", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    settings.MaxOutputRepairAttempts = 0; // fail-closed outcome once output repair is exhausted
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
                    AssertContains("missing required lens sections", reloadedJudge.FailureReason!, "Failure reason should explain why the PASS review was rejected");
                    AssertEqual(MissionFailureKindEnum.JudgeRejected, reloadedJudge.FailureKind, "failure kind is recorded where the failure happens");
                }
            }));

            cases.Add(CaseAsync("handoff_force_advances_shared_branch_to_dock_head", "Pipeline handoff force-advances the shared branch to the prior dock's live HEAD", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);

                    Vessel vessel = new Vessel("stage-lag-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("stage-lag-worker");
                    workerCaptain.State = CaptainStateEnum.Working;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("stage-lag-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Implement change", "Implement the change");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.InProgress;
                    worker.CaptainId = workerCaptain.Id;
                    worker.BranchName = "armada/stage-lag/pipeline";
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Dock workerDock = new Dock(vessel.Id);
                    workerDock.CaptainId = workerCaptain.Id;
                    workerDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, worker.Id);
                    workerDock.BranchName = worker.BranchName;
                    workerDock.Active = true;
                    workerDock = await testDb.Driver.Docks.CreateAsync(workerDock).ConfigureAwait(false);
                    Directory.CreateDirectory(workerDock.WorktreePath);

                    worker.DockId = workerDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);

                    workerCaptain.CurrentMissionId = worker.Id;
                    workerCaptain.CurrentDockId = workerDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(workerCaptain).ConfigureAwait(false);

                    // A dependent Judge stage that reuses the same branch -- this is the stage that would read
                    // stale code if the shared branch ref were not advanced to the worker dock's produced commit.
                    Mission judge = new Mission("[Judge] Review", "Review the change");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = worker.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Implemented the requested change across the touched files and verified the build locally.\n" +
                        "[ARMADA:RESULT] COMPLETE\nDone.";

                    await missionService.HandleCompletionAsync(workerCaptain, worker.Id).ConfigureAwait(false);

                    AssertTrue(
                        git.ForceAdvancedBranches.Any(entry => entry.StartsWith("armada/stage-lag/pipeline=", StringComparison.Ordinal)),
                        "handoff should force-advance the shared branch to the prior dock's live HEAD");
                    AssertTrue(
                        git.ForceAdvancedBranches.Contains("armada/stage-lag/pipeline=abc123def456"),
                        "the branch should be advanced to the dock's resolved HEAD commit");
                }
            }));

            cases.Add(CaseAsync("handoff_refusal_blocks_downstream", "Unsafe branch advance fails visibly before downstream dispatch", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub { AdvanceResult = false };
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);
                    int landings = 0;
                    missionService.OnMissionComplete = (m, d) => { landings++; return Task.CompletedTask; };

                    Vessel vessel = new Vessel("stage-lag-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("stage-lag-worker");
                    workerCaptain.State = CaptainStateEnum.Working;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("stage-lag-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Implement change", "Implement the change");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.InProgress;
                    worker.CaptainId = workerCaptain.Id;
                    worker.BranchName = "armada/stage-lag/pipeline";
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Dock workerDock = new Dock(vessel.Id);
                    workerDock.CaptainId = workerCaptain.Id;
                    workerDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, worker.Id);
                    workerDock.BranchName = worker.BranchName;
                    workerDock.Active = true;
                    workerDock = await testDb.Driver.Docks.CreateAsync(workerDock).ConfigureAwait(false);
                    Directory.CreateDirectory(workerDock.WorktreePath);

                    worker.DockId = workerDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);

                    workerCaptain.CurrentMissionId = worker.Id;
                    workerCaptain.CurrentDockId = workerDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(workerCaptain).ConfigureAwait(false);

                    // A dependent Judge stage that reuses the same branch -- this is the stage that would read
                    // stale code if the shared branch ref were not advanced to the worker dock's produced commit.
                    Mission judge = new Mission("[Judge] Review", "Review the change");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = worker.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Implemented the requested change across the touched files and verified the build locally.\n" +
                        "[ARMADA:RESULT] COMPLETE\nDone.";

                    await missionService.HandleCompletionAsync(workerCaptain, worker.Id).ConfigureAwait(false);

                    Mission? failed = await testDb.Driver.Missions.ReadAsync(worker.Id).ConfigureAwait(false);
                    Mission? blocked = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, failed!.Status);
                    AssertEqual(MissionFailureKindEnum.LandingConflict, failed.FailureKind);
                    AssertContains("Remediation", failed.FailureReason!);
                    AssertContains("abc123def456", failed.FailureReason!);
                    AssertEqual(MissionStatusEnum.Cancelled, blocked!.Status);
                    AssertEqual(0, landings, "Unsafe work cannot land");
                }
            }));

            cases.Add(CaseAsync("redriven_handoff_refusal_cancels_descendants", "Redriven handoff failures settle blocked descendants instead of leaving Pending work", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub { AdvanceResult = false };
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);
                    int landings = 0;
                    missionService.OnMissionComplete = (m, d) => { landings++; return Task.CompletedTask; };

                    Vessel vessel = new Vessel("stage-lag-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("stage-lag-worker");
                    workerCaptain.State = CaptainStateEnum.Working;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("stage-lag-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Implement change", "Implement the change");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.InProgress;
                    worker.CaptainId = workerCaptain.Id;
                    worker.BranchName = "armada/stage-lag/pipeline";
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Dock workerDock = new Dock(vessel.Id);
                    workerDock.CaptainId = workerCaptain.Id;
                    workerDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, worker.Id);
                    workerDock.BranchName = worker.BranchName;
                    workerDock.Active = true;
                    workerDock = await testDb.Driver.Docks.CreateAsync(workerDock).ConfigureAwait(false);
                    Directory.CreateDirectory(workerDock.WorktreePath);

                    worker.DockId = workerDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);

                    workerCaptain.CurrentMissionId = worker.Id;
                    workerCaptain.CurrentDockId = workerDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(workerCaptain).ConfigureAwait(false);

                    // A dependent Judge stage that reuses the same branch -- this is the stage that would read
                    // stale code if the shared branch ref were not advanced to the worker dock's produced commit.
                    Mission judge = new Mission("[Judge] Review", "Review the change");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = worker.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Implemented the requested change across the touched files and verified the build locally.\n" +
                        "[ARMADA:RESULT] COMPLETE\nDone.";

                    worker.Status = MissionStatusEnum.WorkProduced;
                    await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);
                    await missionService.RecoverDanglingHandoffsAsync().ConfigureAwait(false);

                    Mission? failed = await testDb.Driver.Missions.ReadAsync(worker.Id).ConfigureAwait(false);
                    Mission? blocked = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertEqual(MissionStatusEnum.Failed, failed!.Status);
                    AssertEqual(MissionFailureKindEnum.LandingConflict, failed.FailureKind);
                    AssertContains("Remediation", failed.FailureReason!);
                    AssertContains("abc123def456", failed.FailureReason!);
                    AssertEqual(MissionStatusEnum.Cancelled, blocked!.Status);
                    AssertEqual(0, landings, "Unsafe work cannot land");
                }
            }));

            cases.Add(CaseAsync("handoff_keeps_already_integrated_newer_tip", "An already integrated stage never asks the host to rewind the branch", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub { AncestorResult = true };
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);
                    int landings = 0;
                    missionService.OnMissionComplete = (m, d) => { landings++; return Task.CompletedTask; };

                    Vessel vessel = new Vessel("stage-lag-vessel", "https://github.com/test/repo.git");
                    vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_test_bare_" + Guid.NewGuid().ToString("N"));
                    vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_test_work_" + Guid.NewGuid().ToString("N"));
                    vessel.DefaultBranch = "main";
                    vessel = await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    Captain workerCaptain = new Captain("stage-lag-worker");
                    workerCaptain.State = CaptainStateEnum.Working;
                    workerCaptain = await testDb.Driver.Captains.CreateAsync(workerCaptain).ConfigureAwait(false);

                    Voyage voyage = new Voyage("stage-lag-voyage");
                    voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                    Mission worker = new Mission("[Worker] Implement change", "Implement the change");
                    worker.VesselId = vessel.Id;
                    worker.VoyageId = voyage.Id;
                    worker.Persona = "Worker";
                    worker.Status = MissionStatusEnum.InProgress;
                    worker.CaptainId = workerCaptain.Id;
                    worker.BranchName = "armada/stage-lag/pipeline";
                    worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);

                    Dock workerDock = new Dock(vessel.Id);
                    workerDock.CaptainId = workerCaptain.Id;
                    workerDock.WorktreePath = Path.Combine(settings.DocksDirectory, vessel.Name, worker.Id);
                    workerDock.BranchName = worker.BranchName;
                    workerDock.Active = true;
                    workerDock = await testDb.Driver.Docks.CreateAsync(workerDock).ConfigureAwait(false);
                    Directory.CreateDirectory(workerDock.WorktreePath);

                    worker.DockId = workerDock.Id;
                    await testDb.Driver.Missions.UpdateAsync(worker).ConfigureAwait(false);

                    workerCaptain.CurrentMissionId = worker.Id;
                    workerCaptain.CurrentDockId = workerDock.Id;
                    await testDb.Driver.Captains.UpdateAsync(workerCaptain).ConfigureAwait(false);

                    // A dependent Judge stage that reuses the same branch -- this is the stage that would read
                    // stale code if the shared branch ref were not advanced to the worker dock's produced commit.
                    Mission judge = new Mission("[Judge] Review", "Review the change");
                    judge.VesselId = vessel.Id;
                    judge.VoyageId = voyage.Id;
                    judge.Persona = "Judge";
                    judge.Status = MissionStatusEnum.Pending;
                    judge.DependsOnMissionId = worker.Id;
                    judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ =>
                        "Implemented the requested change across the touched files and verified the build locally.\n" +
                        "[ARMADA:RESULT] COMPLETE\nDone.";

                    await missionService.HandleCompletionAsync(workerCaptain, worker.Id).ConfigureAwait(false);

                    AssertEqual(0, git.ForceAdvancedBranches.Count, "No ref update when stage is already contained");
                    AssertEqual(MissionStatusEnum.WorkProduced, (await testDb.Driver.Missions.ReadAsync(worker.Id).ConfigureAwait(false))!.Status);
                }
            }));

            cases.Add(CaseAsync("completion_backfills_missing_branch_from_dock_before_handoff", "Completion backfills missing branch from dock before handoff", TestTags.Positive, async () =>
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

                    Mission testEngineer = new Mission("[Test Engineer] Review branch handoff", "Write tests");
                    testEngineer.VesselId = vessel.Id;
                    testEngineer.VoyageId = voyage.Id;
                    testEngineer.Persona = "Test Engineer";
                    testEngineer.Status = MissionStatusEnum.Pending;
                    testEngineer.DependsOnMissionId = worker.Id;
                    testEngineer = await testDb.Driver.Missions.CreateAsync(testEngineer).ConfigureAwait(false);

                    missionService.OnGetMissionOutput = _ => "worker complete\n[ARMADA:RESULT] COMPLETE";

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
            }));

            cases.Add(CaseAsync("completion_fails_mission_that_modifies_files_outside_scoped_list", "Completion fails mission that modifies files outside scoped list", TestTags.Negative, async () =>
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
                    missionService.OnGetMissionOutput = _ => "worker complete\n[ARMADA:RESULT] COMPLETE";

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

                    Mission dependent = new Mission("[Test Engineer] Downstream should be cancelled", "Write tests");
                    dependent.VesselId = vessel.Id;
                    dependent.VoyageId = voyage.Id;
                    dependent.Persona = "Test Engineer";
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
                    AssertEqual(MissionFailureKindEnum.ScopeViolation, failedWorker.FailureKind, "failure kind is recorded where the failure happens");
                    AssertNotNull(cancelledDependent, "Dependent mission should remain readable");
                    AssertEqual(MissionStatusEnum.Cancelled, cancelledDependent!.Status, "Dependent missions should be cancelled when scope validation fails");
                    AssertFalse(landingCalled, "Scope validation failure should block landing");
                }
            }));

            cases.Add(CaseAsync("completion_passes_scope_with_phantom_empty_path_and_bare_name", "Completion does not fail scope for an empty path or a bare-name match in a nested directory", TestTags.Positive, async () =>
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
                        "postman/Armada.postman_collection.json",
                        "CODEX.md",
                        "\n",
                        ""
                    };
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService, git: git);

                    missionService.OnGetMissionOutput = _ => "worker complete\n[ARMADA:RESULT] COMPLETE";

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

                    Mission dependent = new Mission("[Test Engineer] Downstream should be cancelled", "Write tests");
                    dependent.VesselId = vessel.Id;
                    dependent.VoyageId = voyage.Id;
                    dependent.Persona = "Test Engineer";
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

                    AssertNotNull(failedWorker, "Worker mission should remain readable");
                    AssertTrue(failedWorker!.FailureKind != MissionFailureKindEnum.ScopeViolation, "no scope violation: " + failedWorker.FailureReason);
                    AssertTrue(failedWorker.FailureReason == null || !failedWorker.FailureReason.Contains("scoped file list"), "no scope failure reason: " + failedWorker.FailureReason);
                    AssertNotNull(cancelledDependent, "Dependent mission should remain readable");
                    AssertTrue(cancelledDependent!.Status != MissionStatusEnum.Cancelled, "dependent is not cancelled by a false scope violation");
                }
            }));

            cases.Add(CaseAsync("persona_aware_captain_routing_prefers_matching_captain", "Persona-aware captain routing prefers matching captain", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("allowed_personas_filters_ineligible_captains", "AllowedPersonas filters ineligible captains", TestTags.Positive, async () =>
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
            }));

            cases.Add(CaseAsync("no_eligible_persona_captain_leaves_mission_pending", "No eligible persona captain leaves mission pending", TestTags.Negative, async () =>
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
            }));

            cases.Add(CaseAsync("malformed_allowed_personas_is_not_substring_matched", "Malformed AllowedPersonas JSON is not searched as text", TestTags.Negative, async () =>
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
                    // Not valid JSON. The old fallback searched the raw text for "\"Worker\"" and matched.
                    architectOnly.AllowedPersonas = "[\"Architect\", \"Worker\"";
                    await testDb.Driver.Captains.CreateAsync(architectOnly).ConfigureAwait(false);

                    Mission workerMission = new Mission("Implement worker task");
                    workerMission.VesselId = vessel.Id;
                    workerMission.Persona = "Worker";
                    workerMission.Status = MissionStatusEnum.Pending;
                    workerMission = await testDb.Driver.Missions.CreateAsync(workerMission).ConfigureAwait(false);

                    bool assigned = await missionService.TryAssignAsync(workerMission, vessel).ConfigureAwait(false);
                    AssertFalse(assigned, "A malformed persona list never admits a persona by substring");

                    Mission? reloaded = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                    AssertNotNull(reloaded, "Mission should remain readable after failed assignment");
                    AssertNull(reloaded!.CaptainId, "No ineligible captain should be assigned");
                    AssertEqual(MissionStatusEnum.Pending, reloaded.Status, "Mission should stay Pending when no persona-compatible captain exists");
                }
            }));

            cases.Add(CaseAsync("launch_failure_reclaims_dock_and_leaves_no_active_dock", "Launch failure reclaims dock and leaves no active dock", TestTags.Negative, async () =>
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
            }));

            cases.Add(CaseAsync("configuration_failure_is_terminal_owned_and_cancels_dependents", "A mission configuration failure persists remediation, releases resources and cancels dependents without retry", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    LoggingModule logging = CreateLogging();
                    ArmadaSettings settings = CreateSettings();
                    DirCreatingGitStub git = new DirCreatingGitStub();
                    IDockService dockService = new DockService(logging, testDb.Driver, settings, git);
                    ICaptainService captainService = new CaptainService(logging, testDb.Driver, settings, git, dockService);
                    MissionService missionService = new MissionService(logging, testDb.Driver, settings, dockService, captainService);
                    captainService.OnLaunchAgent = (Captain c, Mission m, Dock d) => throw new MissionLaunchConfigurationException("Enable tool calls in Configuration > Endpoints, then restart this mission.");

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
                    UserMaster owner = await testDb.Driver.Users.CreateAsync(new UserMaster
                    {
                        TenantId = Armada.Core.Constants.DefaultTenantId, Email = "configuration-fixture@acceptance.invalid"
                    }).ConfigureAwait(false);
                    workerMission.TenantId = Armada.Core.Constants.DefaultTenantId;
                    workerMission.UserId = owner.Id;
                    workerCaptain.TenantId = workerMission.TenantId;
                    workerCaptain.UserId = owner.Id;
                    await testDb.Driver.Captains.UpdateAsync(workerCaptain).ConfigureAwait(false);
                    vessel.TenantId = workerMission.TenantId;
                    vessel.UserId = owner.Id;
                    await testDb.Driver.Vessels.UpdateAsync(vessel).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("Configuration failure") { TenantId = workerMission.TenantId, UserId = workerMission.UserId, Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    workerMission.VoyageId = voyage.Id;
                    workerMission = await testDb.Driver.Missions.CreateAsync(workerMission).ConfigureAwait(false);
                    Mission dependent = await testDb.Driver.Missions.CreateAsync(new Mission("Dependent Judge")
                    {
                        TenantId = workerMission.TenantId, VesselId = vessel.Id, VoyageId = voyage.Id, UserId = workerMission.UserId,
                        Persona = "Judge", Status = MissionStatusEnum.Pending, DependsOnMissionId = workerMission.Id
                    }).ConfigureAwait(false);

                    bool assigned = await missionService.TryAssignAsync(workerMission, vessel).ConfigureAwait(false);

                    Mission? reloadedMission = await testDb.Driver.Missions.ReadAsync(workerMission.Id).ConfigureAwait(false);
                    Captain? reloadedCaptain = await testDb.Driver.Captains.ReadAsync(workerCaptain.Id).ConfigureAwait(false);
                    List<Dock> docks = await testDb.Driver.Docks.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);

                    AssertFalse(assigned, "Assignment should fail when the runtime launch throws");
                    AssertNotNull(reloadedMission, "Mission should remain readable after launch failure");
                    AssertEqual(MissionStatusEnum.Failed, reloadedMission!.Status, "Configuration failure must not loop through Pending.");
                    AssertEqual(MissionFailureKindEnum.Infra, reloadedMission.FailureKind);
                    AssertContains("Configuration > Endpoints", reloadedMission.FailureReason!);
                    AssertNotNull(reloadedMission.CompletedUtc);
                    AssertEqual(0, reloadedMission.RedispatchAttempts);
                    AssertFalse(await missionService.TryAssignAsync(reloadedMission, vessel).ConfigureAwait(false), "Terminal configuration failure is not dispatched again.");
                    AssertEqual(MissionStatusEnum.Cancelled, (await testDb.Driver.Missions.ReadAsync(dependent.Id).ConfigureAwait(false))!.Status);
                    AssertEqual(VoyageStatusEnum.Failed, (await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.Status);
                    List<Signal> signals = await testDb.Driver.Signals.EnumerateAsync(workerMission.TenantId, workerMission.UserId).ConfigureAwait(false);
                    Signal owned = signals.Single(x => x.Payload != null && x.Payload.Contains("Configuration > Endpoints"));
                    AssertEqual(workerMission.TenantId, owned.TenantId);
                    AssertEqual(workerMission.UserId, owned.UserId);
                    AssertNull(reloadedMission.CaptainId, "Mission should clear the captain after launch failure");
                    AssertNull(reloadedMission.DockId, "Mission should clear the dock after launch failure");
                    AssertNotNull(reloadedCaptain, "Captain should remain readable after launch failure");
                    AssertNull(reloadedCaptain!.CurrentMissionId, "The captain no longer holds the failed mission");
                    AssertEqual(CaptainStateEnum.Quarantined, reloadedCaptain.State, "A deterministic configuration failure quarantines the captain instead of handing it the next mission");
                    AssertEqual(0, docks.Count(d => d.Active), "Launch failure should not leave behind an active dock");
                }
            }));

            cases.Add(CaseAsync("duplicate_try_assign_async_does_not_reprovision_same_mission", "Duplicate TryAssignAsync does not reprovision same mission", TestTags.Negative, async () =>
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
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Pipeline Dispatch",
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
            return settings;
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
            public Task<IReadOnlyList<Armada.Core.Models.GitChangedFile>> GetBranchChangesAsync(string worktreePath, string baseBranch = "main", CancellationToken token = default)
                => Task.FromResult<IReadOnlyList<Armada.Core.Models.GitChangedFile>>(Array.Empty<Armada.Core.Models.GitChangedFile>());

            /// <inheritdoc />
            public Task<bool> IsPrMergedAsync(string workingDirectory, string prUrl, CancellationToken token = default) => Task.FromResult(true);

            /// <inheritdoc />
            public Task<string?> GetHeadCommitHashAsync(string worktreePath, CancellationToken token = default) => Task.FromResult<string?>("abc123def456");

            /// <inheritdoc />
            public Task<IReadOnlyList<string>> GetChangedFilesSinceAsync(string worktreePath, string startCommit, CancellationToken token = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            public Task<IReadOnlyList<string>> GetConflictedFilesAsync(string worktreePath, CancellationToken token = default)
                => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            /// <inheritdoc />
            public Task<bool> BranchExistsAsync(string repoPath, string branchName, CancellationToken token = default)
                => Task.FromResult(branchName == "main" || WorktreeBranches.Contains(branchName));

            /// <inheritdoc />
            public Task<bool> EnsureLocalBranchAsync(string repoPath, string branchName, CancellationToken token = default)
                => BranchExistsAsync(repoPath, branchName, token);

            /// <inheritdoc />
            public Task<bool> IsWorktreeRegisteredAsync(string repoPath, string worktreePath, CancellationToken token = default) => Task.FromResult(false);

            /// <summary>Branch ref force-advances recorded as "branchName=commitHash".</summary>
            public List<string> ForceAdvancedBranches { get; } = new List<string>();
            public bool AdvanceResult { get; set; } = true;
            public bool? AncestorResult { get; set; } = null;

            /// <inheritdoc />
            public Task<bool> ForceAdvanceBranchAsync(string worktreePath, string branchName, string commitHash, CancellationToken token = default)
            {
                ForceAdvancedBranches.Add(branchName + "=" + commitHash);
                return Task.FromResult(AdvanceResult);
            }
            public Task<IReadOnlyList<string>> GetRecentCommitsForPathsAsync(string worktreePath, IReadOnlyList<string> paths, int maxPerPath, CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
            public Task<IReadOnlyList<string>> FindExistingSubjectTermsAsync(string worktreePath, IReadOnlyList<string> terms, CancellationToken token = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
            public Task<IReadOnlyList<Armada.Core.Models.BranchInfo>> ListBranchesAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default) => Task.FromResult<IReadOnlyList<Armada.Core.Models.BranchInfo>>(new List<Armada.Core.Models.BranchInfo>());
            public Task<IReadOnlyList<Armada.Core.Models.BranchInfo>> ListBranchTipsAsync(string repoPath, string defaultBranch = "main", CancellationToken token = default) => Task.FromResult<IReadOnlyList<Armada.Core.Models.BranchInfo>>(new List<Armada.Core.Models.BranchInfo>());
            public Task MeasureBranchDivergenceAsync(string repoPath, string defaultBranch, IReadOnlyList<Armada.Core.Models.BranchInfo> branches, CancellationToken token = default) => Task.CompletedTask;
            public Task FetchRemotesAsync(string repoPath, CancellationToken token = default) => Task.CompletedTask;
            public Task<Armada.Core.Models.GitDivergenceCounts?> GetDivergenceAsync(string repoPath, string baseRef, string headRef, CancellationToken token = default) => Task.FromResult<Armada.Core.Models.GitDivergenceCounts?>(null);
            public Task<bool?> IsAncestorAsync(string repoPath, string ancestorRef, string descendantRef, CancellationToken token = default) => Task.FromResult(AncestorResult);
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
