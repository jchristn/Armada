namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="MissionRecoveryCoordinator"/> over a live SQLite store. Positive cases
    /// confirm a recoverable failure opens an incident and dispatches a bounded rescue, and that a rescue
    /// success advances the incident Open -&gt; Mitigated -&gt; Closed. Negative cases confirm a non-recoverable
    /// failure opens no incident, a failed rescue re-dispatches until the cap and then leaves the incident
    /// open, and repeated passes never duplicate an incident.
    /// </summary>
    public sealed class MissionRecoveryCoordinatorSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the mission-recovery-coordinator suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("open_reapply_suppresses_rescue", "An open sibling linked to the original prevents another auto-rescue", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission failed = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.TestFail, "test failed").ConfigureAwait(false);
                Mission reapply = await testDb.Driver.Missions.CreateAsync(new Mission("reapply", "same work") { VesselId = vessel.Id, ParentMissionId = failed.Id, Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, new ArmadaSettings { MaxMissionRecoveryAttempts = 2 });
                await coordinator.MaintainAsync().ConfigureAwait(false);
                await coordinator.MaintainAsync().ConfigureAwait(false);
                List<Mission> missions = await testDb.Driver.Missions.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
                AssertEqual(2, missions.Count, "no competing rescue is created");
                Incident incident = (await ReadRecoveryIncidentsAsync(new IncidentService(testDb.Driver)).ConfigureAwait(false))[0];
                AssertEqual(0, incident.RecoveryAttempts, "suppression does not spend rescue budget");
            }));

            cases.Add(CaseAsync("operator_recovery_referencing_parent_suppresses_rescue", "An open mission whose brief names the failed mission or its commit prevents another rescue", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission failed = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.LandingConflict, "conflict").ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("Land existing work on main", "Recover the work of " + failed.Id + " and land it.") { VesselId = vessel.Id, Status = MissionStatusEnum.InProgress }).ConfigureAwait(false);
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, new ArmadaSettings { MaxMissionRecoveryAttempts = 2 });
                await coordinator.MaintainAsync().ConfigureAwait(false);
                List<Mission> missions = await testDb.Driver.Missions.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
                AssertEqual(2, missions.Count, "the operator's recovery is the only recovery");
                List<ArmadaEvent> events = await testDb.Driver.Events.EnumerateByMissionAsync(failed.Id).ConfigureAwait(false);
                AssertTrue(events.Exists(e => e.EventType == "mission.rescue_deduplicated"), "the suppression is recorded");
            }));

            cases.Add(CaseAsync("rescue_with_recorded_commit_integrates_it", "A rescue for a mission that left a commit on its branch integrates that commit instead of re-implementing", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission failed = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.LandingConflict, "Cannot integrate current landing target: conflict in a.txt").ConfigureAwait(false);
                failed.Status = MissionStatusEnum.LandingFailed;
                failed.CommitHash = "6add5c4f0a1b2c3d4e5f60718293a4b5c6d7e8f9";
                failed.BranchName = "armada/worker/" + failed.Id;
                await testDb.Driver.Missions.UpdateAsync(failed).ConfigureAwait(false);
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, new ArmadaSettings { MaxMissionRecoveryAttempts = 2 });
                await coordinator.MaintainAsync().ConfigureAwait(false);
                Mission rescue = (await testDb.Driver.Missions.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false)).Single(m => m.IsRescue);
                AssertContains("Do NOT re-implement", rescue.Description ?? "");
                AssertContains("git merge --no-edit " + failed.CommitHash, rescue.Description ?? "");
                AssertContains(failed.BranchName, rescue.Description ?? "");
                AssertFalse((rescue.Description ?? "").Contains("Re-implement the change correctly", StringComparison.Ordinal), "not the re-implementation brief");
            }));

            cases.Add(CaseAsync("open_same_title_suppresses_rescue", "An open mission with the same title on the same vessel prevents another rescue", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission failed = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.TestFail, "test failed").ConfigureAwait(false);
                Mission reapply = await testDb.Driver.Missions.CreateAsync(new Mission(failed.Title, "same work") { VesselId = vessel.Id, Status = MissionStatusEnum.Pending }).ConfigureAwait(false);
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, new ArmadaSettings { MaxMissionRecoveryAttempts = 2 });
                await coordinator.MaintainAsync().ConfigureAwait(false);
                await coordinator.MaintainAsync().ConfigureAwait(false);
                List<Mission> missions = await testDb.Driver.Missions.EnumerateByVesselAsync(vessel.Id).ConfigureAwait(false);
                AssertEqual(2, missions.Count, "no competing rescue is created");
                Incident incident = (await ReadRecoveryIncidentsAsync(new IncidentService(testDb.Driver)).ConfigureAwait(false))[0];
                AssertEqual(0, incident.RecoveryAttempts, "suppression does not spend rescue budget");
            }));

            cases.Add(CaseAsync("recoverable_failure_opens_incident_and_dispatches_rescue", "A recoverable failure opens an incident and dispatches a rescue", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission failed = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.Compile, "Definition-of-Done gate failed (Compile): build phase exited 1").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(1, recovery.Count, "exactly one recovery incident should open");
                Incident incident = recovery[0];
                AssertEqual("Compile", incident.FailureKind, "the incident should record the classified failure kind");
                AssertEqual(failed.Id, incident.MissionId, "the incident should link the failed mission");
                AssertEqual(IncidentStatusEnum.Open, incident.Status, "a fresh recovery incident is Open");
                AssertEqual(1, incident.RecoveryAttempts, "one rescue should be dispatched");
                AssertEqual(1, incident.RescueMissionIds.Count, "one rescue mission should be linked");

                Mission? rescue = await testDb.Driver.Missions.ReadAsync(incident.RescueMissionIds[0]).ConfigureAwait(false);
                AssertNotNull(rescue, "the rescue mission should exist");
                AssertEqual(MissionStatusEnum.Pending, rescue!.Status, "the rescue mission starts Pending for dispatch");
                AssertEqual(failed.Id, rescue.ParentMissionId, "the rescue links back to the failed mission");
                AssertTrue(rescue.Title.StartsWith("[Rescue]", StringComparison.Ordinal), "the rescue title is marked");
            }));

            cases.Add(CaseAsync("pipeline_and_review_stages_are_not_rescued", "A failed review stage or pipeline stage is not rescued by a standalone Worker that would land beside its pipeline", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission judge = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.Compile, "Definition-of-Done gate failed (Compile)").ConfigureAwait(false);
                judge.Persona = "Judge";
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline")).ConfigureAwait(false);
                Mission stage = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.Compile, "Definition-of-Done gate failed (Compile)").ConfigureAwait(false);
                stage.Persona = "Worker";
                stage.VoyageId = voyage.Id;
                await testDb.Driver.Missions.UpdateAsync(stage).ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("[Judge] Review", "Review it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = stage.Id, Status = MissionStatusEnum.Cancelled }).ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(0, recovery.Count, "neither the Judge nor the pipeline Worker is rescued outside its pipeline");
            }));

            cases.Add(CaseAsync("non_recoverable_failure_opens_no_incident", "A non-recoverable failure opens no incident", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.JudgeRejected, "Judge verdict: FAIL").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(0, recovery.Count, "a judge rejection is not auto-rescued and opens no incident");
            }));

            cases.Add(CaseAsync("rescue_success_mitigates_then_closes", "A rescue success advances the incident Open -> Mitigated -> Closed", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.TestFail, "Definition-of-Done gate failed (TestFail): test phase exited 1").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                Incident incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];

                // The rescue lands successfully.
                Mission rescue = (await testDb.Driver.Missions.ReadAsync(incident.RescueMissionIds[0]).ConfigureAwait(false))!;
                rescue.Status = MissionStatusEnum.Complete;
                await testDb.Driver.Missions.UpdateAsync(rescue).ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                Incident mitigated = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(IncidentStatusEnum.Mitigated, mitigated.Status, "a landed rescue mitigates the incident");
                Mission resolved = (await testDb.Driver.Missions.ReadAsync(mitigated.MissionId!).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Complete, resolved.Status, "landed rescue clears the original failure");
                AssertContains(rescue.Id, resolved.FailureReason ?? "", "resolution links to the landed rescue");

                await coordinator.MaintainAsync().ConfigureAwait(false);
                Incident closed = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(IncidentStatusEnum.Closed, closed.Status, "a held mitigation closes the incident");
            }));

            cases.Add(CaseAsync("failed_rescue_redispatches_until_cap", "A failed rescue re-dispatches until the cap and then leaves the incident open", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.Compile, "Definition-of-Done gate failed (Compile): build phase exited 1").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                Incident incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(1, incident.RecoveryAttempts, "first rescue dispatched");

                // First rescue fails -> a second rescue should be dispatched (under the cap of 2).
                await FailMissionAsync(testDb, incident.RescueMissionIds[0], MissionFailureKindEnum.Compile, "Definition-of-Done gate failed (Compile): build phase exited 1").ConfigureAwait(false);
                await coordinator.MaintainAsync().ConfigureAwait(false);
                incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(2, incident.RecoveryAttempts, "second rescue dispatched under the cap");
                AssertEqual(2, incident.RescueMissionIds.Count, "two rescues linked");
                AssertEqual(IncidentStatusEnum.Open, incident.Status, "incident still open while retrying");

                // Second rescue fails -> the cap is reached; the incident stays open for a human.
                await FailMissionAsync(testDb, incident.RescueMissionIds[1], MissionFailureKindEnum.Compile, "Definition-of-Done gate failed (Compile): build phase exited 1").ConfigureAwait(false);
                await coordinator.MaintainAsync().ConfigureAwait(false);
                incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                AssertEqual(2, incident.RecoveryAttempts, "no rescue beyond the cap");
                AssertEqual(IncidentStatusEnum.Open, incident.Status, "an exhausted incident is left open for a human");
                AssertContains("exhausted", incident.RecoveryNotes ?? String.Empty, "the incident notes explain the exhausted budget");
            }));

            cases.Add(CaseAsync("orphaned_rescue_is_not_rescued_again", "A failed rescue whose incident is gone opens no new incident, so rescues never spawn rescues", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                // Found live: purging the original mission removed its incident, and each of its two failed rescues then
                // looked like a new failure and got a rescue of its own.
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                for (int i = 0; i < 2; i++)
                {
                    Mission orphan = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.LandingConflict, "Error merging locally").ConfigureAwait(false);
                    // Purging the original cleared ParentMissionId (ON DELETE SET NULL); only the rescue flag remains.
                    orphan.Title = "[Rescue] Do the thing";
                    orphan.IsRescue = true;
                    await testDb.Driver.Missions.UpdateAsync(orphan).ConfigureAwait(false);
                }

                await coordinator.MaintainAsync().ConfigureAwait(false);
                AssertEqual(0, (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false)).Count, "a rescue's failure belongs to its own incident");
                List<Mission> all = await testDb.Driver.Missions.EnumerateAsync().ConfigureAwait(false);
                AssertFalse(all.Any(m => m.Title.StartsWith("[Rescue] [Rescue]", StringComparison.Ordinal)), "no rescue of a rescue");
            }));

            cases.Add(CaseAsync("environment_landing_failure_is_not_rescued", "A landing refused by a dirty checkout or unpushed target commits is Infra and is not rescued", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission mission = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.Infra, "Error merging locally: Git checkout /repo contains tracked modifications").ConfigureAwait(false);
                mission.Status = MissionStatusEnum.LandingFailed;
                await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                AssertEqual(0, (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false)).Count, "a rescue cannot clean the operator's checkout");
            }));

            cases.Add(CaseAsync("repeated_passes_do_not_duplicate_incident", "Repeated passes never duplicate an incident", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.Compile, "Definition-of-Done gate failed (Compile): build phase exited 1").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);
                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(1, recovery.Count, "the incident is not duplicated across passes");
                AssertEqual(1, recovery[0].RecoveryAttempts, "no extra rescue while one is still in flight");
            }));

            cases.Add(CaseAsync("reason_text_without_kind_opens_no_incident", "Failure reason text that names a recoverable failure does not open an incident without a recorded kind", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);

                // The old classifier rescued anything whose reason text contained "definition_of_done_compile",
                // "conflict", "crash", "exited", or "timeout", whatever actually happened.
                await CreateFailedMissionAsync(testDb, vessel.Id, null, "definition_of_done_compile: merge conflict, captain exited, timed out").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(0, recovery.Count, "reason text alone never drives auto-rescue");
            }));

            cases.Add(CaseAsync("judge_rejection_reason_quoting_compile_opens_no_incident", "A judge rejection whose reason quotes a compile failure is not auto-rescued", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = new ArmadaSettings();
                settings.MaxMissionRecoveryAttempts = 2;
                MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                IncidentService incidents = new IncidentService(testDb.Driver);

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.JudgeRejected, "Judge verdict: FAIL (definition_of_done_compile crash timeout)").ConfigureAwait(false);

                await coordinator.MaintainAsync().ConfigureAwait(false);

                List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
                AssertEqual(0, recovery.Count, "the recorded kind decides, not the reason text");
            }));

            cases.Add(CaseAsync("long_task_rescue_keeps_prior_stage_handoff", "A rescue brief keeps the first 3000 task characters and the complete prior-stage handoff", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.MaxMissionRecoveryAttempts = 2;
                    MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                    IncidentService incidents = new IncidentService(testDb.Driver);
                    Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);

                    Mission stage = new Mission("Prior stage", "Scoped acceptance criteria");
                    stage.Persona = "Worker";
                    stage.BranchName = "feature/prior-stage";
                    stage.CommitHash = "0123456789abcdef0123456789abcdef01234567";
                    string handoff = PipelineHandoffContext.Build(stage);
                    string taskHead = "task-head-marker";
                    string taskTail = "task-tail-beyond-prefix";
                    string task = taskHead + new string('t', 4000) + taskTail;
                    AssertTrue(task.Length > 3000, "the task prefix is longer than the rescue task budget");
                    AssertFalse(task.Substring(0, 3000).Contains(taskTail, StringComparison.Ordinal), "the task tail sits past the budget");
                    AssertContains("## Prior Stage Output", handoff, "the handoff carries the prior-stage heading");
                    AssertContains("Branch: feature/prior-stage", handoff, "the handoff carries the branch");
                    AssertContains("Commit: 0123456789abcdef0123456789abcdef01234567", handoff, "the handoff carries the commit");
                    AssertContains("list_handoff_artifacts", handoff, "the handoff carries the retrieval instruction");

                    string embedded = await RescueTaskTextAsync(testDb, coordinator, incidents, vessel.Id, task + handoff).ConfigureAwait(false);
                    string expected = task.Substring(0, 3000) + handoff;
                    AssertEqual(expected, embedded, "the rescue keeps the task prefix and the handoff bytes");
                    AssertTrue(embedded.Length <= 3000 + handoff.Length, "the preserved text stays within the task budget plus the handoff");
                    AssertFalse(embedded.Contains(taskTail, StringComparison.Ordinal), "task characters past the budget are omitted");

                    Vessel handoffVessel = new Vessel("handoff-recovery-vessel", "https://github.com/test/handoff.git");
                    handoffVessel.DefaultBranch = "main";
                    handoffVessel = await testDb.Driver.Vessels.CreateAsync(handoffVessel).ConfigureAwait(false);
                    string handoffOnly = await RescueTaskTextAsync(testDb, coordinator, incidents, handoffVessel.Id, handoff).ConfigureAwait(false);
                    AssertEqual(handoff, handoffOnly, "a description that is the handoff keeps that block");
                }
            }));

            cases.Add(CaseAsync("long_description_without_handoff_keeps_trim", "A long description without a prior-stage handoff keeps the existing 3000-character trim", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.MaxMissionRecoveryAttempts = 2;
                    MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                    IncidentService incidents = new IncidentService(testDb.Driver);
                    Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);

                    string taskHead = "plain-task-head";
                    string taskTail = "plain-task-tail-beyond-limit";
                    string task = taskHead + new string('p', 4000) + taskTail;
                    AssertTrue(task.Length > 3000, "the description is longer than the rescue task budget");
                    AssertFalse(task.Substring(0, 3000).Contains(taskTail, StringComparison.Ordinal), "the tail sits past the budget");

                    string embedded = await RescueTaskTextAsync(testDb, coordinator, incidents, vessel.Id, task).ConfigureAwait(false);
                    AssertEqual(task.Substring(0, 3000) + "...(truncated)", embedded, "a description without a handoff keeps the existing trim");
                    AssertFalse(embedded.Contains(taskTail, StringComparison.Ordinal), "characters past the budget are omitted");
                }
            }));

            cases.Add(CaseAsync("separator_without_handoff_mission_line_keeps_trim", "A prior-stage heading without the handoff mission line keeps the existing trim", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ArmadaSettings settings = new ArmadaSettings();
                    settings.MaxMissionRecoveryAttempts = 2;
                    MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                    IncidentService incidents = new IncidentService(testDb.Driver);
                    Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);

                    string taskTail = "fake-handoff-tail";
                    string task = new string('f', 2500)
                        + "\n\n---\n## Prior Stage Output\nThis is ordinary task text.\n"
                        + new string('f', 1000)
                        + taskTail;
                    AssertTrue(task.Length > 3000, "the imitated heading sits in a long description");
                    AssertFalse(task.Substring(0, 3000).Contains(taskTail, StringComparison.Ordinal), "the tail sits past the budget");

                    string embedded = await RescueTaskTextAsync(testDb, coordinator, incidents, vessel.Id, task).ConfigureAwait(false);
                    AssertEqual(task.Substring(0, 3000) + "...(truncated)", embedded, "an imitated heading does not keep the rest of the task");
                    AssertFalse(embedded.Contains(taskTail, StringComparison.Ordinal), "the imitated suffix is not preserved");
                }
            }));

            foreach (string operation in new[] { "cancel_original", "delete_original", "cancel_rescue", "delete_rescue" })
            {
                cases.Add(CaseAsync("operator_" + operation + "_stops_recovery", "Operator " + operation + " stops further rescue dispatch", TestTags.Negative, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    ArmadaSettings settings = new ArmadaSettings { MaxMissionRecoveryAttempts = 3 };
                    MissionRecoveryCoordinator coordinator = new MissionRecoveryCoordinator(CreateLogging(), testDb.Driver, settings);
                    IncidentService incidents = new IncidentService(testDb.Driver);
                    Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                    Mission failed = await CreateFailedMissionAsync(testDb, vessel.Id, MissionFailureKindEnum.Crash, "Agent process exited with code 1").ConfigureAwait(false);
                    await coordinator.MaintainAsync().ConfigureAwait(false);
                    Incident incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                    string targetId = operation.EndsWith("original", StringComparison.Ordinal) ? failed.Id : incident.RescueMissionIds[0];
                    if (operation.StartsWith("delete", StringComparison.Ordinal))
                        await testDb.Driver.Missions.DeleteAsync(targetId).ConfigureAwait(false);
                    else
                    {
                        Mission target = (await testDb.Driver.Missions.ReadAsync(targetId).ConfigureAwait(false))!;
                        target.Status = MissionStatusEnum.Cancelled;
                        await testDb.Driver.Missions.UpdateAsync(target).ConfigureAwait(false);
                    }
                    await coordinator.MaintainAsync().ConfigureAwait(false);
                    await coordinator.MaintainAsync().ConfigureAwait(false);
                    incident = (await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false))[0];
                    AssertEqual(IncidentStatusEnum.Closed, incident.Status, "operator intent closes the recovery chain");
                    AssertEqual(1, incident.RecoveryAttempts, "no replacement rescue after cancellation or deletion");
                    AssertEqual(1, incident.RescueMissionIds.Count, "no additional rescue linked");
                    AssertContains("Recovery stopped", incident.RecoveryNotes ?? String.Empty, "closure must not claim a successful fix");
                }));
            }

            return new TestSuiteDescriptor(
                suiteId: "Services.MissionRecoveryCoordinator",
                displayName: "Mission Recovery Coordinator",
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

        private static async Task<Vessel> CreateVesselAsync(TestDatabase testDb)
        {
            Vessel vessel = new Vessel("recovery-vessel", "https://github.com/test/repo.git");
            vessel.DefaultBranch = "main";
            return await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        private static async Task<Mission> CreateFailedMissionAsync(TestDatabase testDb, string vesselId, MissionFailureKindEnum? failureKind, string failureReason)
        {
            Mission mission = new Mission("Do the thing", "Implement the thing correctly.");
            mission.VesselId = vesselId;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureKind = failureKind;
            mission.FailureReason = failureReason;
            mission.DiffSnapshot = "diff --git a/x.cs b/x.cs\n+++ b/x.cs\n+broken";
            return await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<string> RescueTaskTextAsync(
            TestDatabase testDb,
            MissionRecoveryCoordinator coordinator,
            IncidentService incidents,
            string vesselId,
            string description)
        {
            Mission failed = await CreateFailedMissionAsync(
                testDb,
                vesselId,
                MissionFailureKindEnum.Compile,
                "Definition-of-Done gate failed (Compile): build phase exited 1").ConfigureAwait(false);
            failed.Title = "Keep the prior stage";
            failed.Description = description;
            await testDb.Driver.Missions.UpdateAsync(failed).ConfigureAwait(false);

            await coordinator.MaintainAsync().ConfigureAwait(false);

            List<Incident> recovery = await ReadRecoveryIncidentsAsync(incidents).ConfigureAwait(false);
            Incident incident = recovery.Single(item => item.MissionId == failed.Id);
            Mission rescue = (await testDb.Driver.Missions.ReadAsync(incident.RescueMissionIds[0]).ConfigureAwait(false))!;
            string brief = rescue.Description ?? String.Empty;
            AssertContains("+broken", brief, "the prior diff excerpt remains");
            AssertContains("Definition-of-Done gate failed (Compile)", brief, "the failure reason remains");
            return EmbeddedOriginalDescription(brief, failed.Title);
        }

        private static string EmbeddedOriginalDescription(string brief, string title)
        {
            string startToken = "## Original Mission\n" + title + "\n\n";
            int start = brief.IndexOf(startToken, StringComparison.Ordinal);
            AssertTrue(start >= 0, "the rescue brief contains the original mission heading");
            int textAt = start + startToken.Length;
            string endToken = "\n\n## Failure To Fix\n";
            int end = brief.IndexOf(endToken, textAt, StringComparison.Ordinal);
            AssertTrue(end >= textAt, "the rescue brief contains the failure section");
            return brief.Substring(textAt, end - textAt);
        }

        private static async Task FailMissionAsync(TestDatabase testDb, string missionId, MissionFailureKindEnum failureKind, string failureReason)
        {
            Mission mission = (await testDb.Driver.Missions.ReadAsync(missionId).ConfigureAwait(false))!;
            mission.Status = MissionStatusEnum.Failed;
            mission.FailureKind = failureKind;
            mission.FailureReason = failureReason;
            await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
        }

        private static async Task<List<Incident>> ReadRecoveryIncidentsAsync(IncidentService incidents)
        {
            AuthContext auth = AuthContext.Authenticated("system", "system", true, false, "UnitTest");
            EnumerationResult<Incident> result = await incidents.EnumerateAsync(auth, new IncidentQuery { PageNumber = 1, PageSize = 200 }).ConfigureAwait(false);
            return result.Objects.Where(i => !String.IsNullOrEmpty(i.FailureKind)).ToList();
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.MissionRecoveryCoordinator",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (System.Threading.CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
