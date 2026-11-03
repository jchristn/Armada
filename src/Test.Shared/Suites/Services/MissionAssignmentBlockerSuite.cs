namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
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
    /// Regression coverage for F11: a Pending mission explains why it is waiting (typed reason plus detail) instead of
    /// sitting silently. Each case builds the gate that holds the mission and checks the typed blocker, never its text.
    /// </summary>
    public sealed class MissionAssignmentBlockerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MissionAssignmentBlocker";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (MissionStatusEnum status in Enum.GetValues<MissionStatusEnum>())
            {
                if (status == MissionStatusEnum.Complete || status == MissionStatusEnum.WorkProduced) continue;
                cases.Add(CaseAsync("readiness_dependency_" + status, "Dependency " + status + " blocks diagnostics and dispatch before capacity", TestTags.Positive, async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = fixture.Driver;
                    Harness h = await BuildHarnessAsync(db, launch: true);
                    Mission dependency = await AddMissionAsync(db, h.Vessel, "upstream", status);
                    Mission waiting = await AddMissionAsync(db, h.Vessel, "downstream", MissionStatusEnum.Pending);
                    waiting.DependsOnMissionId = dependency.Id;
                    await db.Missions.UpdateAsync(waiting);
                    // No captains: structural readiness must still be reported first.
                    MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(waiting))!;
                    AssertEqual(MissionAssignmentBlockerReasonEnum.DependencyNotFinished, blocker.Reason);
                    AssertEqual(dependency.Id, blocker.DependsOnMissionId);
                    AssertFalse(await h.Missions.TryAssignAsync(waiting, h.Vessel));
                    AssertEqual(MissionStatusEnum.Pending, (await db.Missions.ReadAsync(waiting.Id))!.Status);
                }));
            }
            foreach (string handoff in new[] { "missing", "branch_pending", "branch_ready", "architect_pending", "architect_ready", "no_branch_ready", "complete" })
            {
                cases.Add(CaseAsync("readiness_handoff_" + handoff, "Dependency handoff is shared by dispatch and diagnostics: " + handoff, TestTags.Positive, async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = fixture.Driver;
                    Harness h = await BuildHarnessAsync(db, launch: true);
                    Mission dependency = await AddMissionAsync(db, h.Vessel, "upstream", handoff == "complete" ? MissionStatusEnum.Complete : MissionStatusEnum.WorkProduced);
                    dependency.BranchName = handoff == "no_branch_ready" ? null : "armada/upstream";
                    dependency.Persona = handoff.StartsWith("architect") ? "Architect" : "Worker";
                    await db.Missions.UpdateAsync(dependency);
                    Mission waiting = await AddMissionAsync(db, h.Vessel, "downstream", MissionStatusEnum.Pending);
                    waiting.DependsOnMissionId = handoff == "missing" ? "msn_missing" : dependency.Id;
                    if (handoff == "branch_ready" || handoff == "architect_pending") waiting.BranchName = dependency.BranchName;
                    if (handoff == "architect_ready") waiting.Description = "<!-- ARMADA:ARCHITECT-HANDOFF -->";
                    await db.Missions.UpdateAsync(waiting);
                    bool ready = handoff.EndsWith("ready") || handoff == "complete";
                    MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(waiting))!;
                    AssertEqual(ready ? MissionAssignmentBlockerReasonEnum.NoCaptains : handoff == "missing"
                        ? MissionAssignmentBlockerReasonEnum.DependencyNotFinished : MissionAssignmentBlockerReasonEnum.DependencyHandoffPending, blocker.Reason);
                    if (ready)
                    {
                        await AddCaptainAsync(db, "Available", CaptainStateEnum.Idle);
                        AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, (await h.Missions.GetAssignmentBlockerAsync(waiting))!.Reason);
                    }
                    AssertEqual(ready, await h.Missions.TryAssignAsync(waiting, h.Vessel));
                }));
            }
            foreach (MissionStatusEnum failure in new[] { MissionStatusEnum.Failed, MissionStatusEnum.LandingFailed })
            foreach (bool block in new[] { false, true })
            {
                cases.Add(CaseAsync("readiness_failed_slice_" + failure + "_" + block, "Diagnostics predict the configured failed-slice transition without mutating the integration", TestTags.Positive, async () =>
                {
                    using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                    var db = fixture.Driver;
                    Harness h = await BuildHarnessAsync(db, launch: true);
                    h.Settings.BlockDependentsOfFailedStages = block;
                    Voyage voyage = await db.Voyages.CreateAsync(new Voyage("Integration readiness"));
                    Mission failed = await AddMissionAsync(db, h.Vessel, "failed worker", failure);
                    failed.VoyageId = voyage.Id; failed.Persona = "Worker"; await db.Missions.UpdateAsync(failed);
                    Mission integration = await AddMissionAsync(db, h.Vessel, "integrate", MissionStatusEnum.Pending);
                    integration.VoyageId = voyage.Id; integration.Persona = "Worker"; integration.WaitForVoyageWorkers = true;
                    await db.Missions.UpdateAsync(integration);
                    MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(integration))!;
                    AssertEqual(MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers, blocker.Reason);
                    AssertContains(block ? "is blocked" : "is cancelled", blocker.Summary);
                    AssertEqual(failed.Id, blocker.BlockingMissionIds.Single());
                    AssertEqual(MissionStatusEnum.Pending, (await db.Missions.ReadAsync(integration.Id))!.Status);
                    AssertFalse(await h.Missions.TryAssignAsync(integration, h.Vessel));
                    AssertEqual(block ? MissionStatusEnum.Blocked : MissionStatusEnum.Cancelled, (await db.Missions.ReadAsync(integration.Id))!.Status);
                }));
            }
            cases.Add(CaseAsync("integration_first_does_not_hold_single_slot", "An integration barrier sorting first never holds the only voyage mission or captain slot", TestTags.Positive, async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Harness h = await BuildHarnessAsync(db, launch: true);
                h.Vessel.MaxParallelVoyages = 1; h.Vessel.MaxParallelMissionsPerVoyage = 1;
                await db.Vessels.UpdateAsync(h.Vessel);
                Captain captain = await AddCaptainAsync(db, "Single slot", CaptainStateEnum.Idle);
                Voyage voyage = await db.Voyages.CreateAsync(new Voyage("Serialized integration"));
                Mission integration = await AddMissionAsync(db, h.Vessel, "integrate first", MissionStatusEnum.Pending);
                integration.Persona = "Worker"; integration.VoyageId = voyage.Id; integration.WaitForVoyageWorkers = true; integration.Priority = 0;
                await db.Missions.UpdateAsync(integration);
                Mission worker = await AddMissionAsync(db, h.Vessel, "worker later", MissionStatusEnum.Pending);
                worker.Persona = "Worker"; worker.VoyageId = voyage.Id; worker.Priority = 100;
                await db.Missions.UpdateAsync(worker);
                AssertEqual(MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers, (await h.Missions.GetAssignmentBlockerAsync(integration))!.Reason);
                AssertFalse(await h.Missions.TryAssignAsync(integration, h.Vessel));
                AssertEqual(0, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
                AssertTrue(await h.Missions.TryAssignAsync(worker, h.Vessel));
                AssertFalse(await h.Missions.TryAssignAsync(integration, h.Vessel));
                AssertEqual(MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers, (await h.Missions.GetAssignmentBlockerAsync(integration))!.Reason);
                worker = (await db.Missions.ReadAsync(worker.Id))!;
                worker.Status = MissionStatusEnum.Complete; worker.ProcessId = null;
                await db.Missions.UpdateAsync(worker);
                await db.Captains.TryReleaseFromMissionAsync(captain.Id, worker.Id);
                AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, (await h.Missions.GetAssignmentBlockerAsync(integration))!.Reason);
                AssertTrue(await h.Missions.TryAssignAsync(integration, h.Vessel));
                AssertEqual(1, (await db.Captains.ReadAsync(captain.Id))!.ActiveMissionCount);
            }));

            cases.Add(CaseAsync("branch_safety_precedes_vessel_capacity", "Dispatch and diagnostics both give branch safety precedence when numeric gates are also full", TestTags.Positive, async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                Harness h = await BuildHarnessAsync(fixture.Driver);
                h.Vessel.MaxParallelMissionsPerVoyage = 1;
                await fixture.Driver.Vessels.UpdateAsync(h.Vessel);
                Mission holder = await AddMissionAsync(fixture.Driver, h.Vessel, "holder", MissionStatusEnum.InProgress);
                holder.BranchName = "armada/shared"; await fixture.Driver.Missions.UpdateAsync(holder);
                Mission waiting = await AddMissionAsync(fixture.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                waiting.BranchName = holder.BranchName; await fixture.Driver.Missions.UpdateAsync(waiting);
                MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(waiting))!;
                AssertEqual(MissionAssignmentBlockerReasonEnum.BranchInUse, blocker.Reason);
                AssertEqual(holder.Id, blocker.Advice[0].TargetId);
                AssertFalse(await h.Missions.TryAssignAsync(waiting, h.Vessel));
                AssertEqual(MissionStatusEnum.Pending, (await fixture.Driver.Missions.ReadAsync(waiting.Id))!.Status);
            }));

            cases.Add(CaseAsync("runtime_capacity_diagnostic", "A free captain behind a full runtime reports the actual capacity gate", TestTags.Positive, async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                Harness h = await BuildHarnessAsync(fixture.Driver);
                h.Settings.Agents = new List<AgentSettings> { new AgentSettings { Runtime = AgentRuntimeEnum.ClaudeCode, MaxConcurrent = 1 } };
                await AddCaptainAsync(fixture.Driver, "Busy", CaptainStateEnum.Working);
                await AddCaptainAsync(fixture.Driver, "Free", CaptainStateEnum.Idle);
                Mission mission = await AddMissionAsync(fixture.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(mission))!;
                AssertEqual(MissionAssignmentBlockerReasonEnum.RuntimeMissionLimit, blocker.Reason);
                AssertEqual(1, blocker.Advice[0].CurrentValue!.Value);
                AssertEqual(2, blocker.Advice[0].SuggestedValue!.Value);
                AssertFalse(await h.Missions.TryAssignAsync(mission, h.Vessel));
            }));

            cases.Add(CaseAsync("endpoint_capacity_and_health_diagnostic", "Endpoint saturation and unavailability have distinct diagnostics and bounded remedies", TestTags.Positive, async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Harness h = await BuildHarnessAsync(db);
                ModelEndpoint endpoint = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "Shared model", BaseUrl = "http://127.0.0.1:19899/v1", Kind = ModelEndpointKindEnum.Inference,
                    InferenceOptions = new InferenceOptions { MaxConcurrentMissions = 1 } });
                Captain busy = await AddCaptainAsync(db, "Busy", CaptainStateEnum.Working);
                Captain free = await AddCaptainAsync(db, "Free", CaptainStateEnum.Idle);
                foreach (Captain captain in new[] { busy, free })
                { captain.Runtime = AgentRuntimeEnum.ApiEndpoint; captain.ModelEndpointId = endpoint.Id; await CaptainAssignmentFixture.UpdateAsync(db, captain); }
                Mission mission = await AddMissionAsync(db, h.Vessel, "waiting", MissionStatusEnum.Pending);
                MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(mission))!;
                AssertEqual(MissionAssignmentBlockerReasonEnum.EndpointMissionLimit, blocker.Reason);
                AssertEqual(endpoint.Id, blocker.Advice[0].TargetId);
                AssertFalse(await h.Missions.TryAssignAsync(mission, h.Vessel));
                endpoint.Enabled = false; await db.ModelEndpoints.UpdateAsync(endpoint);
                blocker = (await h.Missions.GetAssignmentBlockerAsync(mission))!;
                AssertEqual(MissionAssignmentBlockerReasonEnum.EndpointUnavailable, blocker.Reason);
                AssertNull(blocker.Advice[0].SuggestedValue);
            }));

            cases.Add(CaseAsync("lowered_limits_remedy_and_ceiling", "Advice admits one more mission even after a limit reduction and never suggests unlimited or exceeding a ceiling", TestTags.Positive, () =>
            {
                MissionAssignmentBlocker lower = MissionAdmissionEvaluator.Capacity(MissionAssignmentBlockerReasonEnum.VoyageMissionLimit, "voyage", 4, 2,
                    AssignmentBlockerActionEnum.EditVessel, "vessel", "vsl_test", "MaxParallelMissionsPerVoyage", 1000);
                AssertEqual(5, lower.Advice[0].SuggestedValue!.Value);
                AssertContains("existing work continues", lower.Summary);
                MissionAssignmentBlocker ceiling = MissionAdmissionEvaluator.Capacity(MissionAssignmentBlockerReasonEnum.EndpointMissionLimit, "endpoint", 64, 64,
                    AssignmentBlockerActionEnum.EditEndpoint, "endpoint", "mep_test", "MaxConcurrentMissions", 64);
                AssertNull(ceiling.Advice[0].SuggestedValue);
                AssertNull(ceiling.Advice[0].Action);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("full_cli_captain_advice", "A full CLI captain suggests another captain rather than increasing CLI parallelism", TestTags.Positive, async () =>
            {
                using TestDatabase fixture = await TestDatabaseHelper.CreateDatabaseAsync();
                var db = fixture.Driver;
                Harness h = await BuildHarnessAsync(db);
                Captain captain = await AddCaptainAsync(db, "CLI", CaptainStateEnum.Idle);
                Mission held = await AddMissionAsync(db, h.Vessel, "held", MissionStatusEnum.Pending);
                AssertTrue(await db.Captains.TryClaimAsync(captain.Id, held.Id, null));
                h.Vessel.MaxParallelVoyages = 0; h.Vessel.MaxParallelMissionsPerVoyage = 0; await db.Vessels.UpdateAsync(h.Vessel);
                Mission waiting = await AddMissionAsync(db, h.Vessel, "waiting", MissionStatusEnum.Pending);
                MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(waiting))!;
                AssertEqual(MissionAssignmentBlockerReasonEnum.CaptainConcurrencyLimit, blocker.Reason);
                AssertTrue(blocker.Advice.Any(a => a.Action == AssignmentBlockerActionEnum.AddCaptain));
                AssertFalse(blocker.Advice.Any(a => a.Action == AssignmentBlockerActionEnum.EditCaptain));
            }));

            cases.Add(CaseAsync("inactive_explicit_and_auto_assignment_rejected", "Inactive captains are excluded even when explicitly requested; reactivation restores routing", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Captain captain = await AddCaptainAsync(testDb.Driver, "Disabled", CaptainStateEnum.Idle);
                    captain.Active = false;
                    await CaptainAssignmentFixture.UpdateAsync(testDb.Driver, captain, updateActive: true);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "inactive", MissionStatusEnum.Pending);
                    AssertNotNull(await h.Missions.GetAssignmentBlockerAsync(mission));
                    AssertFalse(await h.Missions.TryAssignAsync(mission, h.Vessel));
                    await AddCaptainAsync(testDb.Driver, "Active alternative", CaptainStateEnum.Idle);
                    mission.RequestedCaptainId = captain.Id;
                    await testDb.Driver.Missions.UpdateAsync(mission);
                    AssertFalse(await h.Missions.TryAssignAsync(mission, h.Vessel));
                    AssertNull((await testDb.Driver.Captains.ReadAsync(captain.Id))!.ActiveMissionIds.SingleOrDefault());
                    captain.Active = true;
                    await CaptainAssignmentFixture.UpdateAsync(testDb.Driver, captain, updateActive: true);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, (await h.Missions.GetAssignmentBlockerAsync(mission))!.Reason);
                }
            }));

            cases.Add(CaseAsync("not_pending_has_no_blocker", "A mission that is not Pending has no assignment blocker", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "done", MissionStatusEnum.Complete);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNull(blocker, "no blocker for a Complete mission");
                }
            }));

            cases.Add(CaseAsync("captain_refining_backlog_item", "The only captain refining a backlog item is named, with the session and backlog item", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Captain captain = await AddCaptainAsync(testDb.Driver, "Setup Captain", CaptainStateEnum.Refining);
                    Objective objective = await testDb.Driver.Objectives.CreateAsync(new Objective { Title = "Add retries", Status = ObjectiveStatusEnum.Draft, BacklogState = ObjectiveBacklogStateEnum.Inbox });
                    ObjectiveRefinementSession session = await testDb.Driver.ObjectiveRefinementSessions.CreateAsync(new ObjectiveRefinementSession
                    {
                        ObjectiveId = objective.Id,
                        CaptainId = captain.Id,
                        Status = ObjectiveRefinementSessionStatusEnum.Active
                    });

                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "a Pending mission has a blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoIdleCaptain, blocker!.Reason, "reason");
                    AssertEqual(1, blocker.Captains.Count, "the busy captain is listed");
                    AssertEqual(captain.Id, blocker.Captains[0].CaptainId, "captain id");
                    AssertEqual(CaptainStateEnum.Refining, blocker.Captains[0].State, "captain state");
                    AssertEqual(session.Id, blocker.Captains[0].RefinementSessionId, "refinement session");
                    AssertEqual(objective.Id, blocker.Captains[0].ObjectiveId, "backlog item");
                    AssertFalse(String.IsNullOrEmpty(blocker.Summary), "summary text");
                }
            }));

            cases.Add(CaseAsync("captain_quarantined_until", "A quarantined captain reports when the quarantine ends", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    DateTime until = DateTime.UtcNow.AddMinutes(15);
                    Captain captain = new Captain("Crashy");
                    captain.State = CaptainStateEnum.Quarantined;
                    captain.QuarantineUntilUtc = until;
                    captain.QuarantineReason = "crash loop";
                    captain = await CaptainAssignmentFixture.CreateAsync(testDb.Driver, captain);

                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoIdleCaptain, blocker!.Reason, "reason");
                    AssertEqual(CaptainStateEnum.Quarantined, blocker.Captains[0].State, "state");
                    AssertNotNull(blocker.Captains[0].QuarantineUntilUtc, "quarantine end on the captain");
                    AssertNotNull(blocker.UntilUtc, "blocker clears when the quarantine ends");
                    AssertTrue(Math.Abs((blocker.UntilUtc!.Value - until).TotalSeconds) < 2, "until matches the quarantine end");
                }
            }));

            cases.Add(CaseAsync("vessel_concurrency_limit", "A vessel that runs one mission at a time names the mission holding it", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Mission running = await AddMissionAsync(testDb.Driver, h.Vessel, "running", MissionStatusEnum.InProgress);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.VesselVoyageLimit, blocker!.Reason, "reason");
                    AssertTrue(blocker.BlockingMissionIds.Contains(running.Id), "the running mission is named");
                }
            }));

            cases.Add(CaseAsync("branch_in_use_waits", "Two missions continuing one branch do not run at once, even on a vessel that allows concurrency", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    h.Vessel.MaxParallelVoyages = 0;
                    h.Vessel.MaxParallelMissionsPerVoyage = 0;
                    await testDb.Driver.Vessels.UpdateAsync(h.Vessel);
                    await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Mission running = await AddMissionAsync(testDb.Driver, h.Vessel, "slice two", MissionStatusEnum.InProgress);
                    running.BranchName = "armada/voyage/slice-one";
                    await testDb.Driver.Missions.UpdateAsync(running);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "slice three", MissionStatusEnum.Pending);
                    mission.BranchName = "armada/voyage/slice-one";
                    await testDb.Driver.Missions.UpdateAsync(mission);

                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.BranchInUse, blocker!.Reason, "reason");
                    AssertTrue(blocker.BlockingMissionIds.Contains(running.Id), "the mission holding the branch is named");
                    AssertFalse(await h.Missions.TryAssignAsync(mission, h.Vessel), "no second worktree on a checked-out branch");

                    running.Status = MissionStatusEnum.WorkProduced;
                    await testDb.Driver.Missions.UpdateAsync(running);
                    MissionAssignmentBlocker? after = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertTrue(after == null || after.Reason != MissionAssignmentBlockerReasonEnum.BranchInUse, "the wait ends when the holder finishes");
                }
            }));

            cases.Add(CaseAsync("dependency_not_finished", "A pipeline stage waits for its dependency", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Mission upstream = await AddMissionAsync(testDb.Driver, h.Vessel, "upstream", MissionStatusEnum.Pending);
                    Mission mission = new Mission("downstream", "do it");
                    mission.VesselId = h.Vessel.Id;
                    mission.DependsOnMissionId = upstream.Id;
                    mission = await testDb.Driver.Missions.CreateAsync(mission);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertNotNull(blocker, "blocker");
                    AssertEqual(MissionAssignmentBlockerReasonEnum.DependencyNotFinished, blocker!.Reason, "reason");
                    AssertEqual(upstream.Id, blocker.DependsOnMissionId, "dependency id");
                }
            }));

            cases.Add(CaseAsync("no_captains", "No captains at all is its own reason", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoCaptains, blocker!.Reason, "reason");
                }
            }));

            cases.Add(CaseAsync("persona_fence", "Idle captains that may not take the persona give NoEligibleCaptain", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Captain judgeOnly = new Captain("Judge Only");
                    judgeOnly.State = CaptainStateEnum.Idle;
                    judgeOnly.AllowedPersonas = "[\"Judge\"]";
                    await CaptainAssignmentFixture.CreateAsync(testDb.Driver, judgeOnly);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "worker", MissionStatusEnum.Pending);
                    mission.Persona = "Worker";
                    await testDb.Driver.Missions.UpdateAsync(mission);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason, "reason");
                    AssertEqual(1, blocker.Captains.Count, "the idle captain is listed");
                    AssertEqual("idle (not assigned: persona not allowed)", blocker.Captains[0].Detail, "detail");
                    AssertEqual(false, blocker.Captains[0].EligibleForMission, "not eligible");
                }
            }));

            cases.Add(CaseAsync("idle_captain_below_tier_not_assigned", "An idle captain below the mission's tier is described as not assigned, with the tier", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Captain standard = new Captain("Standard One");
                    standard.State = CaptainStateEnum.Idle;
                    standard.Tier = CaptainTierEnum.Standard;
                    await CaptainAssignmentFixture.CreateAsync(testDb.Driver, standard);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "premium work", MissionStatusEnum.Pending);
                    mission.Tier = CaptainTierEnum.Premium;
                    await testDb.Driver.Missions.UpdateAsync(mission);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason, "reason");
                    AssertEqual(1, blocker.Captains.Count, "the idle captain is listed");
                    AssertEqual("idle (not assigned: tier below Premium)", blocker.Captains[0].Detail, "detail");
                    AssertEqual(false, blocker.Captains[0].EligibleForMission, "not eligible");
                }
            }));

            cases.Add(CaseAsync("idle_captain_outside_persona_priorities_not_assigned", "An idle captain outside the persona's captain priorities is described as not assigned, naming the persona", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Captain claude = new Captain("Claude One");
                    claude.State = CaptainStateEnum.Idle;
                    claude.Runtime = AgentRuntimeEnum.ClaudeCode;
                    await CaptainAssignmentFixture.CreateAsync(testDb.Driver, claude);
                    Persona persona = new Persona("PriorityRole", "persona.worker");
                    persona.CaptainPriorities.Add(new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex });
                    await testDb.Driver.Personas.CreateAsync(persona);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "priority work", MissionStatusEnum.Pending);
                    mission.Persona = persona.Name;
                    await testDb.Driver.Missions.UpdateAsync(mission);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.NoEligibleCaptain, blocker!.Reason, "reason");
                    AssertEqual(1, blocker.Captains.Count, "the idle captain is listed");
                    AssertEqual("idle (not assigned: not in persona PriorityRole's captain priorities)", blocker.Captains[0].Detail, "detail");
                    AssertEqual(false, blocker.Captains[0].EligibleForMission, "not eligible");

                    // With general fallback allowed the same captain may take the mission, and the blocker clears.
                    persona.PriorityExhaustion = PersonaPriorityExhaustionEnum.UseEligibleCaptain;
                    await testDb.Driver.Personas.UpdateAsync(persona);
                    AssertNull(await h.Missions.CaptainIneligibilityReasonAsync(claude, mission), "fallback makes the captain eligible");
                    MissionAssignmentBlocker? cleared = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, cleared!.Reason, "reason with fallback");
                }
            }));

            cases.Add(CaseAsync("idle_eligible_captain_assigned", "An idle captain that may take the mission is described as assigned; busy captains keep their text", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    Captain idle = await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Captain stalled = await AddCaptainAsync(testDb.Driver, "Stalled One", CaptainStateEnum.Stalled);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    AssertNull(await h.Missions.CaptainIneligibilityReasonAsync(idle, mission), "the idle captain is eligible");

                    MissionAssignmentBlocker blocker = new MissionAssignmentBlocker();
                    await h.Missions.DescribeCaptainsAsync(blocker, new List<Captain> { idle, stalled }, mission);
                    AssertEqual(2, blocker.Captains.Count, "both captains are listed");
                    AssertEqual("idle (assigned)", blocker.Captains[0].Detail, "idle detail");
                    AssertEqual(true, blocker.Captains[0].EligibleForMission, "eligible");
                    AssertEqual("stalled and awaiting recovery", blocker.Captains[1].Detail, "busy detail unchanged");
                    AssertNull(blocker.Captains[1].EligibleForMission, "eligibility is only reported for idle captains");
                }
            }));

            cases.Add(CaseAsync("idle_captain_awaiting_dispatch", "With an idle eligible captain the mission is only awaiting the next dispatch", TestTags.Positive, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.AwaitingDispatch, blocker!.Reason, "reason");
                    AssertEqual(0, blocker.Captains.Count, "no captain detail needed");
                }
            }));

            cases.Add(CaseAsync("placement_wait_surfaces_reason", "A free captain whose dock cannot be placed reports the placement reason, not awaiting dispatch", TestTags.Negative, async () =>
            {
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync())
                {
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    await AddCaptainAsync(testDb.Driver, "Idle One", CaptainStateEnum.Idle);
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    h.Missions.ResolveDockPlacementAsync = (m, c, v) => Task.FromResult(DockPlacement.WaitFor("no connected Harbor advertises api-inference-context-v1"));

                    AssertFalse(await h.Missions.TryAssignAsync(mission, h.Vessel), "the mission waits");
                    MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(mission);
                    AssertEqual(MissionAssignmentBlockerReasonEnum.PlacementWaiting, blocker!.Reason, "reason");
                    AssertContains("api-inference-context-v1", blocker.Summary);
                    AssertContains("api-inference-context-v1", h.Missions.GetPlacementWaitReason(mission.Id) ?? String.Empty);

                    // Placement now succeeds; provisioning afterwards may still fail in this harness, which is not under test.
                    h.Missions.ResolveDockPlacementAsync = (m, c, v) => Task.FromResult(DockPlacement.OnAdmiral("test"));
                    await h.Missions.TryAssignAsync(mission, h.Vessel);
                    AssertNull(h.Missions.GetPlacementWaitReason(mission.Id), "the reason is dropped once placement succeeds");
                }
            }));

            foreach (bool privateOwner in new[] { false, true })
                cases.Add(CaseAsync("planning_owner_reference_" + privateOwner, "Waiting diagnostics follow actual planning ownership without exposing private sessions", TestTags.Positive, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync();
                    Harness h = await BuildHarnessAsync(testDb.Driver);
                    TenantMetadata tenant = await testDb.Driver.Tenants.CreateAsync(new TenantMetadata("Ownership privacy"));
                    UserMaster user = await testDb.Driver.Users.CreateAsync(new UserMaster(tenant.Id, "owner@example.invalid", "test-password"));
                    h.Vessel.TenantId = tenant.Id; await testDb.Driver.Vessels.UpdateAsync(h.Vessel);
                    Captain captain = await AddCaptainAsync(testDb.Driver, "Planner", CaptainStateEnum.Idle);
                    captain.TenantId = tenant.Id; await CaptainAssignmentFixture.UpdateAsync(testDb.Driver, captain);
                    PlanningSession owner = await testDb.Driver.PlanningSessions.CreateAsync(new PlanningSession
                    {
                        TenantId = tenant.Id, CaptainId = captain.Id, VesselId = h.Vessel.Id, Title = "Actual owner", Status = PlanningSessionStatusEnum.Created,
                        UserId = privateOwner ? user.Id : null, LastUpdateUtc = DateTime.UtcNow.AddMinutes(-1)
                    });
                    await testDb.Driver.PlanningSessions.CreateAsync(new PlanningSession
                    {
                        TenantId = tenant.Id, CaptainId = captain.Id, VesselId = h.Vessel.Id, Title = "Unclaimed stale session", Status = PlanningSessionStatusEnum.Active
                    });
                    AssertTrue(await testDb.Driver.Captains.TryClaimExclusiveAsync(captain.Id, owner.Id, CaptainStateEnum.Planning));
                    Mission mission = await AddMissionAsync(testDb.Driver, h.Vessel, "waiting", MissionStatusEnum.Pending);
                    mission.TenantId = tenant.Id; await testDb.Driver.Missions.UpdateAsync(mission);
                    MissionAssignmentBlocker blocker = (await h.Missions.GetAssignmentBlockerAsync(mission))!;
                    AssertEqual(privateOwner ? null : owner.Id, blocker.Captains.Single().PlanningSessionId);
                    AssertFalse(blocker.Captains.Single().Detail!.Contains("Unclaimed stale session"));
                    AssertEqual(!privateOwner, blocker.Captains.Single().Detail!.Contains("Actual owner"));
                }));

            return new TestSuiteDescriptor(SuiteId, "Mission assignment blocker", cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static async Task<Harness> BuildHarnessAsync(DatabaseDriver db, bool launch = false)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            ArmadaSettings settings = new ArmadaSettings();
            settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_blocker_docks_" + Guid.NewGuid().ToString("N"));
            settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_blocker_repos_" + Guid.NewGuid().ToString("N"));

            DirCreatingGitService git = new DirCreatingGitService();
            IDockService dockService = new DockService(logging, db, settings, git);
            CaptainService captainService = new CaptainService(logging, db, settings, git, dockService);
            if (launch) captainService.OnLaunchAgent = (_, _, _) => Task.FromResult(424242);
            MissionService missionService = new MissionService(logging, db, settings, dockService, captainService, git: git);

            Vessel vessel = new Vessel("blocker-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_blocker_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_blocker_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

            return new Harness { Missions = missionService, Vessel = vessel, Settings = settings };
        }

        private static async Task<Captain> AddCaptainAsync(DatabaseDriver db, string name, CaptainStateEnum state)
        {
            Captain captain = new Captain(name);
            captain.State = state;
            return await CaptainAssignmentFixture.CreateAsync(db, captain).ConfigureAwait(false);
        }

        private static async Task<Mission> AddMissionAsync(DatabaseDriver db, Vessel vessel, string title, MissionStatusEnum status)
        {
            Mission mission = new Mission(title, "do the work");
            mission.VesselId = vessel.Id;
            mission.Status = status;
            return await db.Missions.CreateAsync(mission).ConfigureAwait(false);
        }

        #endregion

        #region Private-Types

        private sealed class Harness
        {
            public ArmadaSettings Settings { get; set; } = null!;
            public MissionService Missions { get; set; } = null!;
            public Vessel Vessel { get; set; } = null!;
        }

        #endregion
    }
}
