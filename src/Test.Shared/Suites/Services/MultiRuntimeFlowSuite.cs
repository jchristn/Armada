namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
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
    /// The flow where frontier captains plan and review and a local-model captain does the work: missions without a
    /// persona are routed as Worker, a captain that cannot start backs off with a visible reason, a gated Judge block
    /// goes to a person instead of failing the voyage, a restarted later stage keeps the earlier stages' branch, and
    /// work a captain left uncommitted is committed instead of lost.
    /// </summary>
    public sealed class MultiRuntimeFlowSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.MultiRuntimeFlow";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("persona_less_mission_routes_as_worker", "A mission dispatched without a persona follows the Worker persona's captain priorities", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(4242);

                Persona worker = new Persona(Armada.Core.PersonaCatalog.Worker, "persona.worker");
                worker.CaptainPriorities = new List<PersonaCaptainPriority> { new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Mux } };
                await testDb.Driver.Personas.CreateAsync(worker).ConfigureAwait(false);

                // By name the Claude captain sorts first, which is what legacy routing picked.
                Captain claude = await CreateCaptainAsync(testDb, "A Claude", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                Captain mux = await CreateCaptainAsync(testDb, "Z Mux", AgentRuntimeEnum.Mux).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);

                Mission mission = new Mission("No persona", "Plain dispatch");
                mission.VesselId = vessel.Id;
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AssertTrue(await h.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "assigned");
                Mission? stored = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(mux.Id, stored!.CaptainId, "the Worker priority picked the Mux captain, not " + claude.Name);
            }));

            cases.Add(CaseAsync("launch_failure_backs_off_with_reason", "A captain whose CLI is missing is not relaunched every cycle, and the mission says why it is waiting", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                int launches = 0;
                h.Captains.OnLaunchAgent = (_, _, _) =>
                {
                    launches++;
                    throw new AgentRuntimeNotInstalledException(AgentRuntimeEnum.Mux, "mux", null);
                };

                await CreateCaptainAsync(testDb, "Mux", AgentRuntimeEnum.Mux).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission mission = new Mission("Needs mux", "Work");
                mission.VesselId = vessel.Id;
                mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

                AssertFalse(await h.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "first launch fails");
                Mission? stored = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, stored!.Status, "back to Pending");

                MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(stored).ConfigureAwait(false);
                AssertNotNull(blocker, "a blocker explains the wait");
                AssertEqual(MissionAssignmentBlockerReasonEnum.LaunchFailing, blocker.Reason);
                AssertContains("not installed", blocker.Summary);
                AssertNotNull(blocker.UntilUtc, "the next attempt time is shown");

                AssertFalse(await h.Missions.TryAssignAsync(stored, vessel).ConfigureAwait(false), "still backing off");
                AssertEqual(1, launches, "no second launch during the backoff");
            }));

            cases.Add(CaseAsync("gated_judge_block_goes_to_review", "With a review gate, a Judge block waits for a person, and approving lands the work", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                int landingCalls = 0;
                h.Missions.OnMissionComplete = (m, d) => { landingCalls++; return Task.CompletedTask; };
                h.Missions.OnGetMissionOutput = _ => "[ARMADA:VERDICT] NEEDS_REVISION\nScenario: the change misses a case.";

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Captain judgeCaptain = await CreateCaptainAsync(testDb, "Codex", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("judged")).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, judgeCaptain, "Judge", true).ConfigureAwait(false);

                await h.Missions.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);
                Mission? inReview = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Review, inReview!.Status, "a gated Judge block waits for a person");
                AssertContains("Judge blocked landing", inReview.ReviewComment ?? "");
                AssertEqual(0, landingCalls, "nothing landed yet");

                Mission approved = await h.Missions.ApproveReviewAsync(judge.Id, "usr_reviewer", "Overriding: the case is out of scope.").ConfigureAwait(false);
                AssertEqual(1, landingCalls, "approving the override lands the work");
                AssertNotEqual(MissionStatusEnum.Failed, approved.Status);
            }));

            cases.Add(CaseAsync("ungated_judge_block_fails", "Without a review gate, an unsubstantiated Judge block gets one output repair turn and then fails the mission automatically", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                int landingCalls = 0;
                h.Missions.OnMissionComplete = (m, d) => { landingCalls++; return Task.CompletedTask; };
                h.Missions.OnGetMissionOutput = _ => "[ARMADA:VERDICT] FAIL\nScenario: the build breaks.";

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Captain judgeCaptain = await CreateCaptainAsync(testDb, "Codex", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("judged")).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, judgeCaptain, "Judge", false).ConfigureAwait(false);

                // First violation: a block without an affected-case section is a format failure, so the Judge runs again.
                await h.Missions.HandleCompletionAsync(judgeCaptain, judge.Id).ConfigureAwait(false);
                Mission? repaired = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                AssertTrue(repaired!.Status != MissionStatusEnum.Failed, "the first contract violation is repaired, not failed");
                AssertEqual(1, repaired.OutputRepairAttempts);
                AssertContains("Affected Case", repaired.Description ?? String.Empty, "the repair note quotes the Judge contract");
                AssertEqual(0, landingCalls);

                // Second violation with the budget spent: the mission fails closed and says why.
                repaired.Status = MissionStatusEnum.InProgress;
                repaired.CaptainId = judgeCaptain.Id;
                await testDb.Driver.Missions.UpdateAsync(repaired).ConfigureAwait(false);
                Captain? holder = await testDb.Driver.Captains.ReadAsync(judgeCaptain.Id).ConfigureAwait(false);
                holder!.State = CaptainStateEnum.Working;
                holder.CurrentMissionId = judge.Id;
                await testDb.Driver.Captains.UpdateAsync(holder).ConfigureAwait(false);

                await h.Missions.HandleCompletionAsync(holder, judge.Id).ConfigureAwait(false);
                Mission? failed = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Failed, failed!.Status);
                AssertEqual(MissionFailureKindEnum.JudgeRejected, failed.FailureKind);
                AssertContains("output-contract repair", failed.FailureReason ?? String.Empty, "the reason records the spent repair");
                AssertEqual(1, failed.OutputRepairAttempts, "the budget is not exceeded");
                AssertEqual(0, landingCalls);

                Captain? released = await testDb.Driver.Captains.ReadAsync(judgeCaptain.Id).ConfigureAwait(false);
                AssertTrue(released!.CurrentMissionId != judge.Id, "the captain is released after the terminal failure");
            }));

            cases.Add(CaseAsync("restart_later_stage_keeps_branch", "Restarting a failed later stage keeps the earlier stage's branch, reopens the voyage, and restores the cancelled stages after it", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = new Voyage("pipeline");
                voyage.Status = VoyageStatusEnum.Failed;
                voyage = await testDb.Driver.Voyages.CreateAsync(voyage).ConfigureAwait(false);

                Mission worker = new Mission("[Worker] Build", "Build it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.Complete, BranchName = "armada/mux/worker" };
                worker = await testDb.Driver.Missions.CreateAsync(worker).ConfigureAwait(false);
                Mission judge = new Mission("[Judge] Review", "Review it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.Failed, DependsOnMissionId = worker.Id, FailureReason = "crashed", FailureKind = MissionFailureKindEnum.Crash };
                judge = await testDb.Driver.Missions.CreateAsync(judge).ConfigureAwait(false);
                Mission recorder = new Mission("[Recorder] Record", "Record it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Recorder", Status = MissionStatusEnum.Cancelled, DependsOnMissionId = judge.Id, FailureKind = MissionFailureKindEnum.DependencyFailed };
                recorder = await testDb.Driver.Missions.CreateAsync(recorder).ConfigureAwait(false);

                Mission restarted = await h.Missions.RestartAsync(judge).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, restarted.Status);
                AssertEqual("armada/mux/worker", restarted.BranchName, "the Judge reviews the Worker's branch, not a fresh one");
                AssertNull(restarted.FailureReason, "the old failure is cleared");

                Voyage? reopened = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                AssertEqual(VoyageStatusEnum.InProgress, reopened!.Status, "the voyage is reopened");
                Mission? restoredRecorder = await testDb.Driver.Missions.ReadAsync(recorder.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, restoredRecorder!.Status, "the stage cancelled because of the failure is restored");

                bool refused = false;
                try { await h.Missions.RestartAsync(worker).ConfigureAwait(false); }
                catch (InvalidOperationException) { refused = true; }
                AssertTrue(refused, "a Complete mission cannot be restarted");
            }));

            cases.Add(CaseAsync("restart_blocked_verifier_sends_work_back_to_worker", "Restarting a Test Engineer that failed on a blocking product defect after the rework budget sends the work back to a Worker with the operator's guidance instead of re-verifying the unchanged code", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.Failed }).ConfigureAwait(false);
                const string branch = "armada/flow/phase4";
                string blockedOutput = "## Affected Case\nFile: src/Armada.Helm/Commands/McpStdioCommand.cs:90\nstdio capture_captain_usage {} returns result.isError instead of -32602.\n" + VerifierContract.BlockedMarker;

                // The recorded failure shape: the escalated rework round's verifier blocked with the budget exhausted.
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Phase 4", "Implement the usage surfaces.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = branch }).ConfigureAwait(false);
                Mission firstTester = await testDb.Driver.Missions.CreateAsync(new Mission("[Test Engineer] Phase 4", "Verify.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Test Engineer", Status = MissionStatusEnum.WorkProduced, BranchName = branch, DependsOnMissionId = worker.Id, AgentOutput = blockedOutput }).ConfigureAwait(false);
                Mission escalated = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 3 (escalated): Phase 4", "Implement the usage surfaces.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = branch, DependsOnMissionId = firstTester.Id, ReworkRound = 3, ReworkEscalated = true }).ConfigureAwait(false);
                Mission tester = await testDb.Driver.Missions.CreateAsync(new Mission("[Test Engineer] Rework 3: Phase 4", "Verify.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Test Engineer", Status = MissionStatusEnum.Failed, BranchName = branch, DependsOnMissionId = escalated.Id, AgentOutput = blockedOutput, FailureKind = MissionFailureKindEnum.JudgeRejected, FailureReason = "Test Engineer reported a blocking defect (see its Affected Case).\nAutomatic Judge rework limit reached after 1 rework round(s), including an escalated attempt." }).ConfigureAwait(false);
                Mission judge = await testDb.Driver.Missions.CreateAsync(new Mission("[Judge] Phase 4", "Review.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.Cancelled, BranchName = branch, DependsOnMissionId = tester.Id, FailureKind = MissionFailureKindEnum.DependencyFailed, FailureReason = "dependency failed" }).ConfigureAwait(false);

                Mission routed = await h.Missions.RestartAsync(tester, description: "Install the required-argument guard on the stdio server too.").ConfigureAwait(false);

                AssertEqual("Worker", routed.Persona, "the restart answers with the Worker that will correct the product code, not the verifier: " + routed.Title);
                AssertNotEqual(tester.Id, routed.Id, "a new rework Worker, not the blocked verifier");
                AssertEqual(tester.Id, routed.DependsOnMissionId, "the rework hangs off the blocked verifier, whose handoff carries the affected case");
                AssertEqual(MissionStatusEnum.Pending, routed.Status, "no idle captain in this harness: the rework waits for the dispatcher");
                AssertEqual(2, routed.ReworkRound, "an operator-approved round past the automatic budget");
                AssertFalse(routed.ReworkEscalated, "an operator restart uses normal routing, not another automatic escalation");
                AssertContains("Implement the usage surfaces.", routed.Description, "the original task travels with the rework");
                AssertContains("Install the required-argument guard on the stdio server too.", routed.Description, "the operator's restart description is guidance for the Worker");

                Mission? blocked = await testDb.Driver.Missions.ReadAsync(tester.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.WorkProduced, blocked!.Status, "the blocked verifier keeps its finding as produced work, as with automatic rework");
                AssertNull(blocked.FailureKind, "the verifier is no longer a failed mission the voyage settles on");
                AssertEqual(blockedOutput, blocked.AgentOutput, "the verifier's affected case is preserved for the rework");

                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                Mission? nextTester = all.Find(m => m.DependsOnMissionId == routed.Id && m.Persona == "Test Engineer");
                AssertNotNull(nextTester, "a fresh Test Engineer verifies the rework");
                AssertEqual(MissionStatusEnum.Pending, nextTester!.Status);
                Mission? restoredJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                AssertEqual(nextTester.Id, restoredJudge!.DependsOnMissionId, "the Judge now waits for the fresh Test Engineer");
                AssertEqual(MissionStatusEnum.Pending, restoredJudge.Status, "the Judge cancelled by the failure is restored");
                AssertEqual(VoyageStatusEnum.InProgress, (await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.Status, "the voyage is reopened");
                AssertEqual(0, all.Count(m => m.Persona == "Test Engineer" && m.Status == MissionStatusEnum.Failed), "no failed verifier remains to fail the voyage again");

                bool refused = false;
                try { await h.Missions.RestartAsync(blocked).ConfigureAwait(false); }
                catch (InvalidOperationException) { refused = true; }
                AssertTrue(refused, "the routed verifier cannot be restarted a second time into a duplicate chain");
            }));

            cases.Add(CaseAsync("restart_unblocked_failed_verifier_restarts_itself", "A Test Engineer that failed for another reason (a crash, no BLOCKED outcome) still restarts itself on the kept branch", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.Failed }).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Build", "Build it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = "armada/flow/crash" }).ConfigureAwait(false);
                Mission crashed = await testDb.Driver.Missions.CreateAsync(new Mission("[Test Engineer] Build", "Verify.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Test Engineer", Status = MissionStatusEnum.Failed, DependsOnMissionId = worker.Id, FailureKind = MissionFailureKindEnum.Crash, FailureReason = "crashed", AgentOutput = "partial output" }).ConfigureAwait(false);
                Mission blockedButCrashed = await testDb.Driver.Missions.CreateAsync(new Mission("[Test Engineer] Other", "Verify.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Test Engineer", Status = MissionStatusEnum.Failed, DependsOnMissionId = worker.Id, FailureKind = MissionFailureKindEnum.Crash, FailureReason = "crashed", AgentOutput = VerifierContract.BlockedMarker }).ConfigureAwait(false);

                Mission restarted = await h.Missions.RestartAsync(crashed).ConfigureAwait(false);
                AssertEqual(crashed.Id, restarted.Id, "the verifier itself restarts");
                AssertEqual(MissionStatusEnum.Pending, restarted.Status);
                AssertEqual("armada/flow/crash", restarted.BranchName, "on the Worker's branch");
                Mission again = await h.Missions.RestartAsync(blockedButCrashed).ConfigureAwait(false);
                AssertEqual(blockedButCrashed.Id, again.Id, "only a JudgeRejected blocking defect is routed to the Worker");
                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                AssertEqual(0, all.Count(m => IntegrationBarrier.IsRework(m)), "no rework chain was written");
            }));

            cases.Add(CaseAsync("restarted_judge_retains_findings","A restarted rejected Judge receives prior findings as historical evidence alongside its current requirements", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                Mission judge = await testDb.Driver.Missions.CreateAsync(new Mission("Review", "Original requirements")
                {
                    Persona = "Judge", Status = MissionStatusEnum.Failed, FailureKind = MissionFailureKindEnum.JudgeRejected,
                    AgentOutput = "## Affected Case\nBlank-key tool chat sends an unauthenticated request.\n[ARMADA:VERDICT] NEEDS_REVISION"
                }).ConfigureAwait(false);
                Mission restarted = await h.Missions.RestartAsync(judge, description: "Updated requirements").ConfigureAwait(false);
                AssertContains("Updated requirements", restarted.Description);
                AssertContains("Blank-key tool chat sends an unauthenticated request.", restarted.Description);
                AssertContains("historical", restarted.Description);
                AssertContains("recheck", restarted.Description);
                restarted.Status = MissionStatusEnum.Failed;
                restarted.FailureKind = MissionFailureKindEnum.JudgeRejected;
                restarted.AgentOutput = "## Affected Case\nUnknown providers need an explicit unsupported error.\n[ARMADA:VERDICT] NEEDS_REVISION";
                await testDb.Driver.Missions.UpdateAsync(restarted).ConfigureAwait(false);
                Mission again = await h.Missions.RestartAsync(restarted).ConfigureAwait(false);
                AssertContains("Blank-key tool chat sends an unauthenticated request.", again.Description);
                AssertContains("Unknown providers need an explicit unsupported error.", again.Description);
            }));

            cases.Add(CaseAsync("restart_pending_starts_with_idle_captain", "Restarting a Pending mission applies the new title and description, resets nothing, and launches it at once on an idle captain", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                int launches = 0;
                h.Captains.OnLaunchAgent = (_, _, _) => { launches++; return Task.FromResult(4350); };
                Captain captain = await CreateCaptainAsync(testDb, "Starter", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission pending = await testDb.Driver.Missions.CreateAsync(new Mission("Stuck", "Old instructions") { VesselId = vessel.Id, Priority = 7 }).ConfigureAwait(false);

                Mission started = await h.Missions.RestartAsync(pending, "Unstuck", "New instructions").ConfigureAwait(false);
                AssertTrue(started.Status == MissionStatusEnum.Assigned || started.Status == MissionStatusEnum.InProgress, "the mission is launched, not " + started.Status);
                AssertEqual(captain.Id, started.CaptainId, "the idle captain took it");
                AssertEqual(1, launches, "the agent was launched");
                AssertEqual("Unstuck", started.Title);
                AssertEqual("New instructions", started.Description);
                AssertEqual(7, started.Priority, "nothing else is reset");
                AssertEqual(pending.CreatedUtc.ToString("o"), started.CreatedUtc.ToString("o"), "the creation time is kept");
            }));

            cases.Add(CaseAsync("restart_pending_without_captain_stays_pending", "Restarting a Pending mission with no idle captain keeps it Pending without error and keeps its fields", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                int launches = 0;
                h.Captains.OnLaunchAgent = (_, _, _) => { launches++; return Task.FromResult(4351); };
                await CreateCaptainAsync(testDb, "Busy", AgentRuntimeEnum.ClaudeCode, CaptainStateEnum.Working).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission pending = await testDb.Driver.Missions.CreateAsync(new Mission("Waiting", "Keep me") { VesselId = vessel.Id, BranchName = "armada/kept", Priority = 3 }).ConfigureAwait(false);

                Mission restarted = await h.Missions.RestartAsync(pending).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, restarted.Status);
                AssertNull(restarted.CaptainId);
                AssertEqual(0, launches, "nothing was launched");
                AssertEqual("Waiting", restarted.Title, "a null title keeps the original");
                AssertEqual("Keep me", restarted.Description, "a null description keeps the original");
                AssertEqual("armada/kept", restarted.BranchName, "a Pending restart does not reset the branch");
                AssertEqual(3, restarted.Priority);
                Mission? stored = await testDb.Driver.Missions.ReadAsync(pending.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, stored!.Status, "the stored mission is still Pending for the regular dispatcher");
            }));

            cases.Add(CaseAsync("restart_pending_respects_pipeline_dependency", "Restarting a Pending later stage whose dependency is not finished does not launch it, even with an idle captain", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                int launches = 0;
                h.Captains.OnLaunchAgent = (_, _, _) => { launches++; return Task.FromResult(4352); };
                await CreateCaptainAsync(testDb, "Idle", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline")).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Build", "Build") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.Failed }).ConfigureAwait(false);
                Mission judge = await testDb.Driver.Missions.CreateAsync(new Mission("[Judge] Review", "Review") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = worker.Id }).ConfigureAwait(false);

                Mission restarted = await h.Missions.RestartAsync(judge).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, restarted.Status, "the stage still waits for its dependency");
                AssertEqual(0, launches);
            }));

            cases.Add(CaseAsync("restart_failed_resets_then_dispatches", "Restarting a Failed mission still clears its run state, and it is launched at once when a captain is idle", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Mission failed = await testDb.Driver.Missions.CreateAsync(new Mission("Broken", "Fix it")
                {
                    VesselId = vessel.Id, Status = MissionStatusEnum.Failed, FailureReason = "crashed", FailureKind = MissionFailureKindEnum.Crash,
                    BranchName = "armada/old", PrUrl = "https://example.invalid/pr/1", CommitHash = "abc123", CompletedUtc = DateTime.UtcNow
                }).ConfigureAwait(false);

                Mission reset = await h.Missions.RestartAsync(failed).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, reset.Status, "no captain exists, so it waits");
                AssertNull(reset.FailureReason);
                AssertNull(reset.FailureKind);
                AssertNull(reset.BranchName);
                AssertNull(reset.PrUrl);
                AssertNull(reset.CommitHash);
                AssertNull(reset.CompletedUtc);

                int launches = 0;
                h.Captains.OnLaunchAgent = (_, _, _) => { launches++; return Task.FromResult(4353); };
                Captain captain = await CreateCaptainAsync(testDb, "Fixer", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                reset.Status = MissionStatusEnum.Failed;
                reset.FailureReason = "crashed again";
                await testDb.Driver.Missions.UpdateAsync(reset).ConfigureAwait(false);
                Mission relaunched = await h.Missions.RestartAsync(reset).ConfigureAwait(false);
                AssertTrue(relaunched.Status == MissionStatusEnum.Assigned || relaunched.Status == MissionStatusEnum.InProgress, "the mission is launched, not " + relaunched.Status);
                AssertEqual(captain.Id, relaunched.CaptainId);
                AssertEqual(1, launches);
                AssertNull(relaunched.FailureReason);
            }));

            cases.Add(CaseAsync("restart_refuses_active_statuses", "Restart still refuses WorkProduced and InProgress missions, and the message lists Pending as allowed", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                foreach (MissionStatusEnum status in new[] { MissionStatusEnum.WorkProduced, MissionStatusEnum.InProgress, MissionStatusEnum.Assigned, MissionStatusEnum.Complete })
                {
                    Mission mission = await testDb.Driver.Missions.CreateAsync(new Mission("Active " + status, "Busy") { Status = status, BranchName = "armada/busy" }).ConfigureAwait(false);
                    string? message = null;
                    try { await h.Missions.RestartAsync(mission).ConfigureAwait(false); }
                    catch (InvalidOperationException ex) { message = ex.Message; }
                    AssertNotNull(message, status + " is refused");
                    AssertContains("Only Pending, Failed, LandingFailed, or Cancelled missions can be restarted", message!);
                    AssertContains("current: " + status, message!);
                    Mission? stored = await testDb.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false);
                    AssertEqual(status, stored!.Status, "a refused restart changes nothing");
                    AssertEqual("armada/busy", stored.BranchName);
                }
            }));

            cases.Add(CaseAsync("uncommitted_work_is_committed", "Work a captain left uncommitted is committed before the diff is taken", TestTags.Positive, async () =>
            {
                string repo = Path.Combine(Path.GetTempPath(), "armada_uncommitted_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(repo);
                try
                {
                    await GitAsync(repo, "init", "-q", "-b", "main").ConfigureAwait(false);
                    await GitAsync(repo, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "initial").ConfigureAwait(false);
                    await File.WriteAllTextAsync(Path.Combine(repo, "work.txt"), "done by a model that forgot to commit\n").ConfigureAwait(false);

                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb, new DirCreatingGitService(), new GitService(CreateLogging()));
                    h.Missions.OnGetMissionOutput = _ => "Done.";
                    Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                    Captain mux = await CreateCaptainAsync(testDb, "Mux", AgentRuntimeEnum.Mux, CaptainStateEnum.Working).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("work")).ConfigureAwait(false);

                    // A review gate keeps the dock in place so the commit can be inspected afterwards.
                    Mission mission = await CreateRunningMissionAsync(testDb, h, vessel, voyage, mux, "Worker", true, repo).ConfigureAwait(false);
                    await h.Missions.HandleCompletionAsync(mux, mission.Id).ConfigureAwait(false);

                    string log = await GitAsync(repo, "log", "--format=%s", "-n", "1").ConfigureAwait(false);
                    AssertContains("Armada: commit work left uncommitted by Mux", log);
                    string status = await GitAsync(repo, "status", "--porcelain").ConfigureAwait(false);
                    AssertEqual("", status.Trim(), "nothing is left uncommitted");
                }
                finally
                {
                    try { Directory.Delete(repo, true); } catch { }
                }
            }));

            cases.Add(CaseAsync("stopped_stage_settles_the_stages_after_it", "A stage failed by stopping its captain (which does not cascade) has its waiting stages settled by the health check: cancelled by default, Blocked when configured", TestTags.Negative, async () =>
            {
                foreach (bool block in new[] { false, true })
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb, new DirCreatingGitService());
                    h.Settings.BlockDependentsOfFailedStages = block;
                    Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Feature [Worker]", "Do it.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.Failed, FailureKind = MissionFailureKindEnum.OperatorAction }).ConfigureAwait(false);
                    Mission judge = await testDb.Driver.Missions.CreateAsync(new Mission("Feature [Judge]", "Review.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = worker.Id }).ConfigureAwait(false);

                    AssertEqual(1, await h.Missions.SettleStagesBehindFailedDependenciesAsync().ConfigureAwait(false));
                    Mission? settledJudge = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                    AssertEqual(block ? MissionStatusEnum.Blocked : MissionStatusEnum.Cancelled, settledJudge!.Status);
                    AssertEqual(MissionFailureKindEnum.DependencyFailed, settledJudge.FailureKind);
                    Voyage? after = await testDb.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false);
                    if (block) AssertNotEqual(VoyageStatusEnum.Failed, after!.Status, "Blocked keeps the voyage open");
                    else AssertEqual(VoyageStatusEnum.Failed, after!.Status, "with nothing left active the voyage settles Failed");
                    AssertEqual(0, await h.Missions.SettleStagesBehindFailedDependenciesAsync().ConfigureAwait(false), "nothing left to settle");
                }
            }));

            cases.Add(CaseAsync("blocked_verifier_sends_work_back_before_the_judge", "A Test Engineer that reports a blocking product defect sends the work back to the Worker at once; a new Test Engineer checks the rework and the Judge waits for it", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Missions.OnGetMissionOutput = _ => "## Coverage Added\nAdded a nested Bearer case.\n## Affected Case\nScenario: Redact(\"Authorization: Bearer Bearer t\") leaves t visible.\nExpected every token masked.\n" + VerifierContract.BlockedMarker;
                int landingCalls = 0;
                h.Missions.OnMissionComplete = (m, d) => { landingCalls++; return Task.CompletedTask; };

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Redactor [Worker]", "Mask bearer tokens.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = "armada/flow/blocked" }).ConfigureAwait(false);
                Captain claude = await CreateCaptainAsync(testDb, "Claude", AgentRuntimeEnum.ClaudeCode, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission tester = await CreateRunningMissionAsync(testDb, h, vessel, voyage, claude, "Test Engineer", false).ConfigureAwait(false);
                tester.DependsOnMissionId = worker.Id;
                await testDb.Driver.Missions.UpdateAsync(tester).ConfigureAwait(false);
                Mission judge = await testDb.Driver.Missions.CreateAsync(new Mission("Redactor [Judge]", "Review.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = tester.Id }).ConfigureAwait(false);

                await h.Missions.HandleCompletionAsync(claude, tester.Id).ConfigureAwait(false);

                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                Mission? rework = all.Find(m => m.DependsOnMissionId == tester.Id && m.Persona == "Worker");
                AssertNotNull(rework, "the Worker gets the defect back");
                AssertEqual(1, rework!.ReworkRound);
                AssertContains("Mask bearer tokens.", rework.Description);
                Mission? nextTester = all.Find(m => m.DependsOnMissionId == rework.Id && m.Persona == "Test Engineer");
                AssertNotNull(nextTester, "a new Test Engineer checks the rework");
                AssertEqual(nextTester!.Id, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.DependsOnMissionId, "the Judge waits for the new Test Engineer");
                AssertEqual(MissionStatusEnum.Pending, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.Status, "the Judge never ran on the known defect");
                AssertEqual(0, landingCalls);
            }));

            cases.Add(CaseAsync("blocked_verifier_without_affected_case_is_repaired", "A verifier reporting BLOCKED without a concrete affected case gets an output repair turn instead of a rework or a pass", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(4347);
                h.Missions.OnGetMissionOutput = _ => "## Code Style\nSomething looks off.\n" + VerifierContract.BlockedMarker;
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Feature [Worker]", "Do it.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = "armada/flow/lint" }).ConfigureAwait(false);
                Captain claude = await CreateCaptainAsync(testDb, "Claude", AgentRuntimeEnum.ClaudeCode, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission linter = await CreateRunningMissionAsync(testDb, h, vessel, voyage, claude, "Linter", false).ConfigureAwait(false);
                linter.DependsOnMissionId = worker.Id;
                await testDb.Driver.Missions.UpdateAsync(linter).ConfigureAwait(false);

                await h.Missions.HandleCompletionAsync(claude, linter.Id).ConfigureAwait(false);

                Mission? repaired = await testDb.Driver.Missions.ReadAsync(linter.Id).ConfigureAwait(false);
                AssertEqual(1, repaired!.OutputRepairAttempts, "the unsubstantiated block gets one repair turn");
                AssertContains("without a concrete affected case", repaired.Description ?? "");
                AssertFalse((await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false)).Exists(m => m.DependsOnMissionId == linter.Id && m.Persona == "Worker"), "no rework on an unsubstantiated block");
                AssertTrue(VerifierContract.IsBlocked("x\n  " + VerifierContract.BlockedMarker + "  \n"));
                AssertFalse(VerifierContract.IsBlocked("The text mentions " + VerifierContract.BlockedMarker + " inline"));
                AssertFalse(VerifierContract.Applies("Judge"));
                AssertTrue(VerifierContract.Applies("Test Engineer") && VerifierContract.Applies("Linter") && VerifierContract.Applies("Usability Engineer"));
            }));

            cases.Add(CaseAsync("judge_revision_sends_work_back", "An ungated Judge asking for concrete revisions sends the work back to a Worker on the same branch, then to a new Judge, and the rest of the pipeline follows the new Judge; after two rounds it escalates to an alternative captain even without a planner", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Missions.OnGetMissionOutput = _ => "## Correctness\nMostly right.\n## Affected Case\nFile: tests/test_cli.py:70\nThe test leaks stderr.\n[ARMADA:VERDICT] NEEDS_REVISION";
                int landingCalls = 0;
                h.Missions.OnMissionComplete = (m, d) => { landingCalls++; return Task.CompletedTask; };

                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Add the CLI [Worker]", "Write textkit/cli.py.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = "armada/flow/judge", WaitForVoyageWorkers = true }).ConfigureAwait(false);
                Captain codex = await CreateCaptainAsync(testDb, "Codex", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, codex, "Judge", false).ConfigureAwait(false);
                judge.DependsOnMissionId = worker.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                Mission recorder = await testDb.Driver.Missions.CreateAsync(new Mission("Record [Recorder]", "Record.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Recorder", DependsOnMissionId = judge.Id }).ConfigureAwait(false);

                await h.Missions.HandleCompletionAsync(codex, judge.Id).ConfigureAwait(false);

                Mission? reviewed = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                AssertNotEqual(MissionStatusEnum.Failed, reviewed!.Status, "a fixable review does not fail the chain");
                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                Mission? rework = all.Find(m => m.DependsOnMissionId == judge.Id && m.Persona == "Worker");
                AssertNotNull(rework, "a rework Worker follows the Judge");
                AssertContains("Rework round 1", rework.Description);
                AssertContains("Write textkit/cli.py.", rework.Description);
                AssertTrue(rework.WaitForVoyageWorkers, "Rework preserves the original final-stage barrier");
                AssertEqual("armada/flow/judge", rework.BranchName, "the rework continues on the same branch");
                Mission? nextJudge = all.Find(m => m.DependsOnMissionId == rework.Id && m.Persona == "Judge");
                AssertNotNull(nextJudge, "a new Judge reviews the rework");
                AssertContains("including those labelled nonblocking", rework.Description, "Rework must reconcile prior verifier findings against the original contract.");
                Mission? movedRecorder = await testDb.Driver.Missions.ReadAsync(recorder.Id).ConfigureAwait(false);
                AssertEqual(nextJudge.Id, movedRecorder!.DependsOnMissionId, "the Recorder now waits for the new Judge");
                AssertEqual(0, landingCalls, "nothing lands before the work passes");

                // Two rework rounds already in the chain: try one different captain even without a planner.
                Mission round2 = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 2: Add the CLI [Worker]", "x") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = nextJudge.Id, BranchName = "armada/flow/judge" }).ConfigureAwait(false);
                rework.Status = MissionStatusEnum.WorkProduced;
                await testDb.Driver.Missions.UpdateAsync(rework).ConfigureAwait(false);
                Captain codex2 = await CreateCaptainAsync(testDb, "Codex 2", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission lastJudge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, codex2, "Judge", false).ConfigureAwait(false);
                lastJudge.DependsOnMissionId = round2.Id;
                await testDb.Driver.Missions.UpdateAsync(lastJudge).ConfigureAwait(false);
                await h.Missions.HandleCompletionAsync(codex2, lastJudge.Id).ConfigureAwait(false);
                Mission? exhausted = await testDb.Driver.Missions.ReadAsync(lastJudge.Id).ConfigureAwait(false);
                AssertNotEqual(MissionStatusEnum.Failed, exhausted!.Status, "a Worker > Judge pipeline can recover without a planner");
                all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                Mission? alternative = all.Find(m => m.DependsOnMissionId == lastJudge.Id && m.Persona == "Worker");
                AssertNotNull(alternative, "one bounded escalation is created");
                AssertEqual(codex2.Id, alternative.RequestedCaptainId, "the known reviewer is an alternative when no planner exists");
                AssertContains("(escalated)", alternative.Title);
                alternative.Status = MissionStatusEnum.WorkProduced;
                await testDb.Driver.Missions.UpdateAsync(alternative).ConfigureAwait(false);
                Captain finalReviewer = await CreateCaptainAsync(testDb, "Final reviewer", AgentRuntimeEnum.ClaudeCode, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission finalJudge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, finalReviewer, "Judge", false).ConfigureAwait(false);
                finalJudge.DependsOnMissionId = alternative.Id;
                await testDb.Driver.Missions.UpdateAsync(finalJudge).ConfigureAwait(false);
                await h.Missions.HandleCompletionAsync(finalReviewer, finalJudge.Id).ConfigureAwait(false);
                Mission stopped = (await testDb.Driver.Missions.ReadAsync(finalJudge.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Failed, stopped.Status, "A rejected escalated attempt cannot create an infinite recovery chain");
                AssertContains("Judge rework limit reached", stopped.FailureReason);
                AssertContains("escalated attempt", stopped.FailureReason);
                AssertContains("Remediation:", stopped.FailureReason);
                AssertContains("independent Judge", stopped.FailureReason);
            }));

            cases.Add(CaseAsync("full_pipeline_rework_repeats_verification", "Judge revisions preserve all intervening FullPipeline stages and move Recorder after their new Judge", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Missions.OnGetMissionOutput = _ => "## Correctness\nMissing failure case.\n## Affected Case\nScenario: xAI tool stream ends before completion but reports success.\nExpected failure, actual success.\n[ARMADA:VERDICT] NEEDS_REVISION";
                int landings = 0;
                h.Missions.OnMissionComplete = (m, d) => { landings++; return Task.CompletedTask; };
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("full") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Implement [Worker]", "AC-EOF: reject missing completion.")
                { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = "armada/full/rework" }).ConfigureAwait(false);
                Mission preceding = worker;
                foreach (string persona in new[] { "Usability Engineer", "Test Engineer", "Linter" })
                {
                    preceding = await testDb.Driver.Missions.CreateAsync(new Mission("Verify", "AC-EOF: original contract.")
                    { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = persona, Status = MissionStatusEnum.WorkProduced,
                        DependsOnMissionId = preceding.Id, Tier = CaptainTierEnum.Standard, RequestedCaptainId = "cpt_verifier" }).ConfigureAwait(false);
                }
                Captain captain = await CreateCaptainAsync(testDb, "Reviewer", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, captain, "Judge", false).ConfigureAwait(false);
                judge.DependsOnMissionId = preceding.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                Mission recorder = await testDb.Driver.Missions.CreateAsync(new Mission("Record", "Record facts")
                { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Recorder", DependsOnMissionId = judge.Id }).ConfigureAwait(false);
                await h.Missions.HandleCompletionAsync(captain, judge.Id).ConfigureAwait(false);
                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                string parent = judge.Id;
                foreach (string persona in new[] { "Worker", "Usability Engineer", "Test Engineer", "Linter", "Judge" })
                {
                    Mission? next = all.Find(m => m.DependsOnMissionId == parent && m.Persona == persona);
                    AssertNotNull(next, "Rework must repeat " + persona);
                    AssertContains("AC-EOF", next!.Description, "Original slice criteria retained");
                    if (persona != "Worker" && persona != "Judge")
                    {
                        AssertEqual(CaptainTierEnum.Standard, next.Tier);
                        AssertEqual("cpt_verifier", next.RequestedCaptainId);
                        AssertEqual(MissionStatusEnum.Pending, next.Status);
                    }
                    parent = next.Id;
                }
                AssertEqual(parent, (await testDb.Driver.Missions.ReadAsync(recorder.Id).ConfigureAwait(false))!.DependsOnMissionId);
                AssertEqual(0, landings, "Full verification must finish before landing");
            }));

            cases.Add(CaseAsync("architect_slice_has_own_rework_budget", "A later Architect slice starts its own bounded rework budget after an earlier slice passed an escalated review", TestTags.Positive, async () =>
            {
                foreach (string priorTitle in new[] { "[Worker] Rework 3 (escalated): Catalog", "[Worker] Rework 1: Catalog" })
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb, new DirCreatingGitService());
                    h.Missions.OnGetMissionOutput = _ => "## Affected Case\nFile: client.cs:10\nReject the wrong authentication header.\n[ARMADA:VERDICT] NEEDS_REVISION";
                    Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("Architect slices") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Mission prior = await testDb.Driver.Missions.CreateAsync(new Mission(priorTitle, "Catalog") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CommitHash = "catalog-commit", BranchName = "armada/slices" }).ConfigureAwait(false);
                    Mission passed = await testDb.Driver.Missions.CreateAsync(new Mission("Review catalog", "Catalog") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = prior.Id, AgentOutput = "[ARMADA:VERDICT] PASS" }).ConfigureAwait(false);
                    Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Inference [Worker]", "Implement inference") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = passed.Id, CommitHash = "catalog-commit", BranchName = "armada/slices" }).ConfigureAwait(false);
                    Captain reviewer = await CreateCaptainAsync(testDb, "Reviewer", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                    Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, reviewer, "Judge", false).ConfigureAwait(false);
                    judge.DependsOnMissionId = worker.Id;
                    judge.BranchName = "armada/slices";
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                    await h.Missions.HandleCompletionAsync(reviewer, judge.Id).ConfigureAwait(false);

                    Mission reviewed = (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!;
                    AssertNotEqual(MissionStatusEnum.Failed, reviewed.Status, "An earlier slice's escalation must not fail a later slice's first review");
                    List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    Mission? rework = all.Find(m => m.DependsOnMissionId == judge.Id && m.Persona == "Worker");
                    AssertNotNull(rework, "The new slice gets a correction attempt");
                    AssertContains("Rework round 1", rework.Description);
                    AssertEqual<string?>(null, rework.RequestedCaptainId, "An earlier slice must not force escalation");
                    AssertEqual("armada/slices", rework.BranchName);
                }
            }));

            cases.Add(CaseAsync("failed_slice_cancels_integration_and_restart_restores_it", "A failed sibling slice cancels the final integration and its stages; restarting the failed mission restores them with the branch kept", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(4343);
                Captain integrator = await CreateCaptainAsync(testDb, "Integrator", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("Sliced plan")).ConfigureAwait(false);
                Mission slice = await testDb.Driver.Missions.CreateAsync(new Mission("Slice A [Worker]", "Implement A") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CommitHash = "aaa111", BranchName = "armada/a" }).ConfigureAwait(false);
                Mission rejected = await testDb.Driver.Missions.CreateAsync(new Mission("Review A", "Review") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.Failed, FailureKind = MissionFailureKindEnum.JudgeRejected, DependsOnMissionId = slice.Id, BranchName = "armada/a" }).ConfigureAwait(false);
                Mission integration = await testDb.Driver.Missions.CreateAsync(new Mission("Integrate [Worker]", "Integrate") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", WaitForVoyageWorkers = true, BranchName = "armada/integrate" }).ConfigureAwait(false);
                Mission integrationJudge = await testDb.Driver.Missions.CreateAsync(new Mission("Review integration", "Review") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = integration.Id }).ConfigureAwait(false);

                MissionAssignmentBlocker? blocker = await h.Missions.GetAssignmentBlockerAsync(integration).ConfigureAwait(false);
                AssertContains(rejected.Id, String.Join(",", blocker!.BlockingMissionIds));
                AssertFalse(await h.Missions.TryAssignAsync(integration, vessel).ConfigureAwait(false), "integration does not run without the failed slice");

                Mission? cancelled = await testDb.Driver.Missions.ReadAsync(integration.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Cancelled, cancelled!.Status);
                AssertEqual(MissionFailureKindEnum.DependencyFailed, cancelled.FailureKind);
                AssertContains(rejected.Id, cancelled.FailureReason ?? "");
                AssertEqual(MissionStatusEnum.Cancelled, (await testDb.Driver.Missions.ReadAsync(integrationJudge.Id).ConfigureAwait(false))!.Status, "its own stages stop with it");

                // Restart dispatches at once when a captain is idle; keep the only captain busy so the restarted Judge waits
                // (this harness's git has no slice commits, so launching it would fail it again and re-cancel integration).
                integrator.State = CaptainStateEnum.Working;
                await testDb.Driver.Captains.UpdateAsync(integrator).ConfigureAwait(false);
                await h.Missions.RestartAsync((await testDb.Driver.Missions.ReadAsync(rejected.Id).ConfigureAwait(false))!).ConfigureAwait(false);
                Mission? restored = await testDb.Driver.Missions.ReadAsync(integration.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, restored!.Status, "restarting the failed mission brings integration back");
                AssertEqual("armada/integrate", restored.BranchName, "the integration keeps its branch");
                AssertEqual(MissionStatusEnum.Pending, (await testDb.Driver.Missions.ReadAsync(integrationJudge.Id).ConfigureAwait(false))!.Status);
                AssertEqual(MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers, (await h.Missions.GetAssignmentBlockerAsync(restored).ConfigureAwait(false))!.Reason,
                    "it waits for the restarted slice instead of running");
            }));

            cases.Add(CaseAsync("failed_slice_blocks_integration_when_configured", "With BlockDependentsOfFailedStages on, a failed sibling slice Blocks the integration instead of cancelling it, and restarting the failed mission returns it to Pending", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Settings.BlockDependentsOfFailedStages = true;
                h.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(4346);
                await CreateCaptainAsync(testDb, "Integrator", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("Sliced plan")).ConfigureAwait(false);
                Mission slice = await testDb.Driver.Missions.CreateAsync(new Mission("Slice A [Worker]", "A") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CommitHash = "aaa111" }).ConfigureAwait(false);
                Mission rejected = await testDb.Driver.Missions.CreateAsync(new Mission("Review A", "Review") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.Failed, DependsOnMissionId = slice.Id }).ConfigureAwait(false);
                Mission integration = await testDb.Driver.Missions.CreateAsync(new Mission("Integrate [Worker]", "Integrate") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", WaitForVoyageWorkers = true }).ConfigureAwait(false);

                AssertFalse(await h.Missions.TryAssignAsync(integration, vessel).ConfigureAwait(false));
                Mission? blocked = await testDb.Driver.Missions.ReadAsync(integration.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Blocked, blocked!.Status);
                AssertContains(rejected.Id, blocked.FailureReason ?? "");

                await h.Missions.RestartAsync((await testDb.Driver.Missions.ReadAsync(rejected.Id).ConfigureAwait(false))!).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Pending, (await testDb.Driver.Missions.ReadAsync(integration.Id).ConfigureAwait(false))!.Status);
            }));

            cases.Add(CaseAsync("integration_merges_sibling_work_before_launch", "Integration starts with the other slices' commits merged, a conflict left to the Worker with its files named, and an operator-cancelled slice listed as dropped", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingMergeGit merges = new RecordingMergeGit();
                Harness h = new Harness(testDb, new DirCreatingGitService(), merges);
                h.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(4344);
                await CreateCaptainAsync(testDb, "Integrator", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("Sliced plan")).ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("Slice A [Worker]", "A") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CommitHash = "aaa111", BranchName = "armada/a", CompletedUtc = DateTime.UtcNow.AddMinutes(-3) }).ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("Slice B [Worker]", "B") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CommitHash = "bbb222", BranchName = "armada/b", CompletedUtc = DateTime.UtcNow.AddMinutes(-2) }).ConfigureAwait(false);
                Mission dropped = await testDb.Driver.Missions.CreateAsync(new Mission("Slice C [Worker]", "C") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CommitHash = "ccc333", BranchName = "armada/c", CompletedUtc = DateTime.UtcNow.AddMinutes(-1) }).ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("Review C", "Review") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.Cancelled, FailureKind = MissionFailureKindEnum.OperatorAction, DependsOnMissionId = dropped.Id }).ConfigureAwait(false);
                Mission integration = await testDb.Driver.Missions.CreateAsync(new Mission("Integrate [Worker]", "Integrate the slices.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", WaitForVoyageWorkers = true }).ConfigureAwait(false);

                AssertTrue(await h.Missions.TryAssignAsync(integration, vessel).ConfigureAwait(false), "integration starts once the slices settle");
                AssertEqual("aaa111@armada/a,bbb222@armada/b", String.Join(",", merges.Calls), "each kept slice's commit is merged, oldest first; the dropped slice is not");
                Mission? started = await testDb.Driver.Missions.ReadAsync(integration.Id).ConfigureAwait(false);
                string description = started!.Description ?? "";
                AssertContains("Slice A [Worker]", description);
                AssertContains(": merged", description);
                AssertContains("`git merge bbb222` conflicts in `src/shared.cs`", description);
                AssertContains("cancelled by an operator", description);
                AssertContains(dropped.Id, description);
            }));

            cases.Add(CaseAsync("integration_refused_when_sibling_commit_missing", "Integration is refused, not started without the work, when a sibling's commit is not in the dock's repository", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingMergeGit merges = new RecordingMergeGit();
                Harness h = new Harness(testDb, new DirCreatingGitService(), merges);
                h.Captains.OnLaunchAgent = (_, _, _) => Task.FromResult(4345);
                await CreateCaptainAsync(testDb, "Integrator", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("Sliced plan")).ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("Slice Z [Worker]", "Z") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CommitHash = "zzz999", BranchName = "armada/z" }).ConfigureAwait(false);
                Mission integration = await testDb.Driver.Missions.CreateAsync(new Mission("Integrate [Worker]", "Integrate") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", WaitForVoyageWorkers = true }).ConfigureAwait(false);

                AssertFalse(await h.Missions.TryAssignAsync(integration, vessel).ConfigureAwait(false));
                Mission? refused = await testDb.Driver.Missions.ReadAsync(integration.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Failed, refused!.Status);
                AssertContains("zzz999", refused.FailureReason ?? "");
                AssertContains("push its branch", refused.FailureReason ?? "");
            }));

            cases.Add(CaseAsync("existing_rework_inherits_final_stage_barrier", "A stored rework inherits its root's barrier and waits for sibling review without waiting on its own Judge", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("Final documentation")).ConfigureAwait(false);
                Mission root = await testDb.Driver.Missions.CreateAsync(new Mission("Docs [Worker]", "Document") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, WaitForVoyageWorkers = true }).ConfigureAwait(false);
                Mission rejected = await testDb.Driver.Missions.CreateAsync(new Mission("Review docs", "Review") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = root.Id }).ConfigureAwait(false);
                Mission rework = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 1: Docs", "Correct docs") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", DependsOnMissionId = rejected.Id }).ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("Review docs again", "Review") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", DependsOnMissionId = rework.Id }).ConfigureAwait(false);
                Mission sibling = await testDb.Driver.Missions.CreateAsync(new Mission("Review implementation", "Review") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.Review }).ConfigureAwait(false);
                MissionAssignmentBlocker? blocked = await h.Missions.GetAssignmentBlockerAsync(rework).ConfigureAwait(false);
                AssertEqual(MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers, blocked!.Reason);
                sibling.Status = MissionStatusEnum.Complete;
                await testDb.Driver.Missions.UpdateAsync(sibling).ConfigureAwait(false);
                MissionAssignmentBlocker? released = await h.Missions.GetAssignmentBlockerAsync(rework).ConfigureAwait(false);
                AssertNotEqual(MissionAssignmentBlockerReasonEnum.WaitingForVoyageWorkers, released!.Reason);
            }));

            cases.Add(CaseAsync("repeated_affected_case_escalates_early", "A rework that changed code but leaves the Judge reporting the same affected case switches captain early; a new case gets an ordinary round", TestTags.Negative, async () =>
            {
                foreach (bool sameCase in new[] { true, false })
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    Harness h = new Harness(testDb, new DirCreatingGitService());
                    h.Missions.OnGetMissionOutput = _ => "## Affected Case\nFile: " + (sameCase ? "src/a.py:42" : "src/b.py:3") + "\nThe null input still throws.\n[ARMADA:VERDICT] NEEDS_REVISION";
                    Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                    Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("repeated case") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Captain deepseek = await CreateCaptainAsync(testDb, "Deepseek", AgentRuntimeEnum.ApiEndpoint).ConfigureAwait(false);
                    Mission initial = await testDb.Driver.Missions.CreateAsync(new Mission("Implement", "Implement it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "commit-one", BranchName = "b" }).ConfigureAwait(false);
                    Mission firstReview = await testDb.Driver.Missions.CreateAsync(new Mission("Review", "Review it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Judge", Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = initial.Id,
                        AgentOutput = "## Affected Case\nFile: src/a.py:17\nThe null input throws.\n[ARMADA:VERDICT] NEEDS_REVISION" }).ConfigureAwait(false);
                    Mission rework = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 1: Implement", "Implement it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "commit-two", BranchName = "b", DependsOnMissionId = firstReview.Id }).ConfigureAwait(false);
                    Captain codex = await CreateCaptainAsync(testDb, "Reviewer", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                    Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, codex, "Judge", false).ConfigureAwait(false);
                    judge.DependsOnMissionId = rework.Id;
                    await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                    await h.Missions.HandleCompletionAsync(codex, judge.Id).ConfigureAwait(false);

                    List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                    Mission? next = all.Find(m => m.DependsOnMissionId == judge.Id && m.Persona == "Worker");
                    AssertNotNull(next, "a rework round is scheduled");
                    if (sameCase) AssertContains("(escalated)", next!.Title, "the same affected case after a changed commit escalates now");
                    else AssertFalse(next!.Title.Contains("(escalated)", StringComparison.Ordinal), "a new affected case gets an ordinary round");
                }
            }));

            cases.Add(CaseAsync("unchanged_rework_escalates_early", "A rejected rework that preserves the previous worker commit switches captain before exhausting ordinary retries", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Missions.OnGetMissionOutput = _ => "## Affected Case\nFile: a.py:1\nThe implementation is still missing.\n[ARMADA:VERDICT] NEEDS_REVISION";
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("stagnation") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Captain deepseek = await CreateCaptainAsync(testDb, "Deepseek", AgentRuntimeEnum.ApiEndpoint).ConfigureAwait(false);
                Mission initial = await testDb.Driver.Missions.CreateAsync(new Mission("Implement", "Implement it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "same-commit", BranchName = "b" }).ConfigureAwait(false);
                Mission rework = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 1: Implement", "Implement it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "same-commit", BranchName = "b", DependsOnMissionId = initial.Id }).ConfigureAwait(false);
                Captain codex = await CreateCaptainAsync(testDb, "Reviewer", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, codex, "Judge", false).ConfigureAwait(false);
                judge.DependsOnMissionId = rework.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                await h.Missions.HandleCompletionAsync(codex, judge.Id).ConfigureAwait(false);
                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                Mission? escalated = all.Find(m => m.DependsOnMissionId == judge.Id && m.Persona == "Worker");
                AssertNotNull(escalated, "A stagnant rework switches captain immediately");
                AssertEqual(codex.Id, escalated.RequestedCaptainId);
                AssertContains("(escalated)", escalated.Title);
            }));

            cases.Add(CaseAsync("strict_rework_cannot_escalate", "Strict captain selection reports remediation rather than escalating unchanged work", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Missions.OnGetMissionOutput = _ => "## Affected Case\nFile: a.py:1\nThe implementation is still missing.\n[ARMADA:VERDICT] NEEDS_REVISION";
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("stagnation") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Captain deepseek = await CreateCaptainAsync(testDb, "Deepseek", AgentRuntimeEnum.ApiEndpoint).ConfigureAwait(false);
                voyage.CaptainOverridesJson = MissionService.SerializeCaptainOverrides(new List<CaptainAssignmentOverride>
                    { new CaptainAssignmentOverride("Worker", deepseek.Id, null) { StrictCaptain = true } });
                await testDb.Driver.Voyages.UpdateAsync(voyage).ConfigureAwait(false);
                Mission initial = await testDb.Driver.Missions.CreateAsync(new Mission("Implement", "Implement it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "same-commit", BranchName = "b" }).ConfigureAwait(false);
                Mission rework = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 1: Implement", "Implement it") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "same-commit", BranchName = "b", DependsOnMissionId = initial.Id }).ConfigureAwait(false);
                Captain codex = await CreateCaptainAsync(testDb, "Reviewer", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, codex, "Judge", false).ConfigureAwait(false);
                judge.DependsOnMissionId = rework.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                await h.Missions.HandleCompletionAsync(codex, judge.Id).ConfigureAwait(false);
                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                Mission? escalated = all.Find(m => m.DependsOnMissionId == judge.Id && m.Persona == "Worker");
                AssertNull(escalated, "Strict captain mode must not dispatch another model");
                Mission? failed = await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Failed, failed!.Status);
                AssertContains("Strict captain selection", failed.FailureReason!);
                AssertContains("same rejected commit after 1 rework", failed.FailureReason!);
                AssertFalse(failed.FailureReason!.Contains("budget exhausted", StringComparison.Ordinal), "Stagnation is distinct from budget exhaustion");
                AssertContains("Remediation", failed.FailureReason!);
            }));

            cases.Add(CaseAsync("strict_rework_budget_explained", "Strict captain failure names the exhausted per-slice budget and code remediation without substituting a captain", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Missions.OnGetMissionOutput = _ => "## Affected Case\nFile: redactor.cs:110\nShifted credential spans still leak.\n[ARMADA:VERDICT] NEEDS_REVISION";
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Captain deepseek = await CreateCaptainAsync(testDb, "Deepseek", AgentRuntimeEnum.ApiEndpoint).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("bounded rework")
                {
                    Status = VoyageStatusEnum.InProgress,
                    CaptainOverridesJson = MissionService.SerializeCaptainOverrides(new List<CaptainAssignmentOverride>
                    { new CaptainAssignmentOverride("Worker", deepseek.Id, null) { StrictCaptain = true } })
                }).ConfigureAwait(false);
                Mission initial = await testDb.Driver.Missions.CreateAsync(new Mission("Implement", "Implement it")
                { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "initial", BranchName = "b" }).ConfigureAwait(false);
                Mission first = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 1: Implement", "Implement it")
                { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "first", BranchName = "b", DependsOnMissionId = initial.Id }).ConfigureAwait(false);
                Mission second = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 2: Implement", "Implement it")
                { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, CaptainId = deepseek.Id, CommitHash = "second", BranchName = "b", DependsOnMissionId = first.Id }).ConfigureAwait(false);
                Captain reviewer = await CreateCaptainAsync(testDb, "Reviewer", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, reviewer, "Judge", false).ConfigureAwait(false);
                judge.DependsOnMissionId = second.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                Mission recorder = await testDb.Driver.Missions.CreateAsync(new Mission("Record", "Record it")
                { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Recorder", DependsOnMissionId = judge.Id }).ConfigureAwait(false);

                await h.Missions.HandleCompletionAsync(reviewer, judge.Id).ConfigureAwait(false);
                Mission failed = (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Failed, failed.Status);
                AssertEqual(MissionFailureKindEnum.JudgeRejected, failed.FailureKind);
                AssertContains("budget exhausted for this slice (2 of 2 rounds)", failed.FailureReason!);
                AssertContains("no captain substitution was made", failed.FailureReason!);
                AssertContains("code or configuration defects", failed.FailureReason!);
                AssertContains("independent Judge", failed.FailureReason!);
                AssertEqual(MissionStatusEnum.Cancelled, (await testDb.Driver.Missions.ReadAsync(recorder.Id).ConfigureAwait(false))!.Status);
                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                AssertFalse(all.Exists(m => m.DependsOnMissionId == judge.Id && String.Equals(m.Persona, "Worker", StringComparison.OrdinalIgnoreCase)), "Exhaustion cannot dispatch another Worker");
            }));

            cases.Add(CaseAsync("judge_rework_escalates_to_planner", "When the Worker's rework rounds are used up, one escalated round goes to the captain that planned the voyage; after it the chain fails", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Missions.OnGetMissionOutput = _ => "## Affected Case\nFile: a.py:1\nStill wrong.\n[ARMADA:VERDICT] NEEDS_REVISION";
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Captain claude = await CreateCaptainAsync(testDb, "Claude", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                await testDb.Driver.Missions.CreateAsync(new Mission("Plan [Architect]", "plan") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Architect", Status = MissionStatusEnum.WorkProduced, CaptainId = claude.Id }).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Do it [Worker]", "Write a.py.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, BranchName = "b" }).ConfigureAwait(false);
                Mission r1 = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 1: Do it", "x") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = worker.Id, BranchName = "b" }).ConfigureAwait(false);
                Mission r2 = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 2: Do it", "x") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = r1.Id, BranchName = "b" }).ConfigureAwait(false);
                Captain codex = await CreateCaptainAsync(testDb, "Codex", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, codex, "Judge", false).ConfigureAwait(false);
                judge.DependsOnMissionId = r2.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);

                await h.Missions.HandleCompletionAsync(codex, judge.Id).ConfigureAwait(false);
                List<Mission> all = await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);
                Mission? escalated = all.Find(m => m.DependsOnMissionId == judge.Id);
                AssertNotNull(escalated, "an escalated rework follows");
                AssertContains("(escalated)", escalated.Title);
                AssertEqual(claude.Id, escalated.RequestedCaptainId, "the planner's captain takes the last round");
                AssertNotEqual(MissionStatusEnum.Failed, (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!.Status);
            }));

            cases.Add(CaseAsync("missing_rework_captain_has_configuration_guidance", "A missing eligible escalation captain reports configuration guidance instead of only a Judge verdict", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Harness h = new Harness(testDb, new DirCreatingGitService());
                h.Missions.OnGetMissionOutput = _ => "## Affected Case\nFile: a.py:1\nStill wrong.\n[ARMADA:VERDICT] NEEDS_REVISION";
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Captain workerCaptain = await CreateCaptainAsync(testDb, "Worker", AgentRuntimeEnum.ApiEndpoint).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("Do it [Worker]", "Write a.py.") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", CaptainId = workerCaptain.Id, Status = MissionStatusEnum.WorkProduced, BranchName = "b" }).ConfigureAwait(false);
                Mission r1 = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 1: Do it", "x") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", CaptainId = workerCaptain.Id, Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = worker.Id, BranchName = "b" }).ConfigureAwait(false);
                Mission r2 = await testDb.Driver.Missions.CreateAsync(new Mission("[Worker] Rework 2: Do it", "x") { VesselId = vessel.Id, VoyageId = voyage.Id, Persona = "Worker", CaptainId = workerCaptain.Id, Status = MissionStatusEnum.WorkProduced, DependsOnMissionId = r1.Id, BranchName = "b" }).ConfigureAwait(false);
                Captain reviewer = await CreateCaptainAsync(testDb, "Judge only", AgentRuntimeEnum.Codex, CaptainStateEnum.Working).ConfigureAwait(false);
                reviewer.AllowedPersonas = "[\"Judge\"]";
                await testDb.Driver.Captains.UpdateAsync(reviewer).ConfigureAwait(false);
                Mission judge = await CreateRunningMissionAsync(testDb, h, vessel, voyage, reviewer, "Judge", false).ConfigureAwait(false);
                judge.DependsOnMissionId = r2.Id;
                await testDb.Driver.Missions.UpdateAsync(judge).ConfigureAwait(false);
                await h.Missions.HandleCompletionAsync(reviewer, judge.Id).ConfigureAwait(false);
                Mission failed = (await testDb.Driver.Missions.ReadAsync(judge.Id).ConfigureAwait(false))!;
                AssertEqual(MissionStatusEnum.Failed, failed.Status);
                AssertContains("No alternate eligible Worker captain", failed.FailureReason);
                AssertContains("persona priorities", failed.FailureReason);
                AssertContains("Remediation:", failed.FailureReason);
            }));

            cases.Add(CaseAsync("escalation_respects_scope_and_availability", "Recovery excludes failed, quarantined, wrong-owner, disallowed and insufficient-tier captains and honors alternative Worker priorities", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Captain failed = await CreateCaptainAsync(testDb, "Failed", AgentRuntimeEnum.ApiEndpoint).ConfigureAwait(false);
                Captain quarantined = await CreateCaptainAsync(testDb, "Quarantined", AgentRuntimeEnum.Codex, CaptainStateEnum.Quarantined).ConfigureAwait(false);
                Captain disallowed = await CreateCaptainAsync(testDb, "Judge only", AgentRuntimeEnum.ClaudeCode).ConfigureAwait(false);
                disallowed.AllowedPersonas = "[\"Judge\"]";
                await testDb.Driver.Captains.UpdateAsync(disallowed).ConfigureAwait(false);
                Captain foreign = await CreateCaptainAsync(testDb, "Foreign owner", AgentRuntimeEnum.Codex).ConfigureAwait(false);
                TenantMetadata foreignTenant = await testDb.Driver.Tenants.CreateAsync(new TenantMetadata("Escalation ownership fence")).ConfigureAwait(false);
                UserMaster foreignUser = await testDb.Driver.Users.CreateAsync(new UserMaster(foreignTenant.Id, "escalation-other@example.com", "password")).ConfigureAwait(false);
                foreign.UserId = foreignUser.Id;
                await testDb.Driver.Captains.UpdateAsync(foreign).ConfigureAwait(false);
                Mission mission = new Mission("Recover", "Finish") { CaptainId = failed.Id };
                AssertNull(await CaptainEscalationSelector.SelectAsync(testDb.Driver, mission, failed.Id, new List<Mission>()).ConfigureAwait(false), "No unauthorized fallback is selected");
                Captain alternative = await CreateCaptainAsync(testDb, "Alternative", AgentRuntimeEnum.Codex).ConfigureAwait(false);
                alternative.Tier = CaptainTierEnum.Standard;
                await testDb.Driver.Captains.UpdateAsync(alternative).ConfigureAwait(false);
                mission.Tier = CaptainTierEnum.Premium;
                AssertNull(await CaptainEscalationSelector.SelectAsync(testDb.Driver, mission, failed.Id, new List<Mission>()).ConfigureAwait(false), "The mission tier floor applies");
                mission.Tier = null;
                Persona worker = await testDb.Driver.Personas.CreateAsync(new Persona("Worker", "persona.worker") { CaptainPriorities = new List<PersonaCaptainPriority> { new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ApiEndpoint }, new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.Codex } } }).ConfigureAwait(false);
                Captain? selected = await CaptainEscalationSelector.SelectAsync(testDb.Driver, mission, failed.Id, new List<Mission>()).ConfigureAwait(false);
                AssertEqual(alternative.Id, selected!.Id, "Worker priorities select the next eligible captain instead of retrying the failed endpoint");
                worker.CaptainPriorities = new List<PersonaCaptainPriority> { new PersonaCaptainPriority { Runtime = AgentRuntimeEnum.ApiEndpoint } };
                worker.PriorityExhaustion = PersonaPriorityExhaustionEnum.Wait;
                await testDb.Driver.Personas.UpdateAsync(worker).ConfigureAwait(false);
                Mission pendingJudge = new Mission("Review", "Review the implementation") { Persona = "Judge", RequestedCaptainId = alternative.Id };
                selected = await CaptainEscalationSelector.SelectAsync(testDb.Driver, mission, failed.Id, new List<Mission> { pendingJudge }).ConfigureAwait(false);
                AssertEqual(alternative.Id, selected!.Id, "The explicitly selected pending Judge can recover work before the first review runs");
                pendingJudge.RequestedCaptainId = null;
                await testDb.Driver.Personas.CreateAsync(new Persona("Judge", "persona.judge") { DefaultCaptainId = alternative.Id }).ConfigureAwait(false);
                selected = await CaptainEscalationSelector.SelectAsync(testDb.Driver, mission, failed.Id, new List<Mission> { pendingJudge }).ConfigureAwait(false);
                AssertEqual(alternative.Id, selected!.Id, "The pending Judge's configured default can also recover an early worker failure");
            }));

            cases.Add(CaseAsync("finished_voyage_settles_intermediate_stages", "When a voyage completes, handed-off intermediate stages become Complete; a final WorkProduced result is left alone", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                Vessel vessel = await CreateVesselAsync(testDb).ConfigureAwait(false);
                Voyage voyage = await testDb.Driver.Voyages.CreateAsync(new Voyage("done")).ConfigureAwait(false);
                Mission worker = await testDb.Driver.Missions.CreateAsync(new Mission("w", "w") { VesselId = vessel.Id, VoyageId = voyage.Id, Status = MissionStatusEnum.WorkProduced }).ConfigureAwait(false);
                Mission judge = await testDb.Driver.Missions.CreateAsync(new Mission("j", "j") { VesselId = vessel.Id, VoyageId = voyage.Id, Status = MissionStatusEnum.Complete, DependsOnMissionId = worker.Id }).ConfigureAwait(false);
                Mission standalone = await testDb.Driver.Missions.CreateAsync(new Mission("s", "s") { VesselId = vessel.Id, VoyageId = voyage.Id, Status = MissionStatusEnum.WorkProduced }).ConfigureAwait(false);

                int settled = await PipelineStageSettler.SettleAsync(testDb.Driver, await testDb.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false)).ConfigureAwait(false);
                AssertEqual(1, settled);
                AssertEqual(MissionStatusEnum.Complete, (await testDb.Driver.Missions.ReadAsync(worker.Id).ConfigureAwait(false))!.Status, "the handed-off stage is settled");
                AssertEqual(MissionStatusEnum.WorkProduced, (await testDb.Driver.Missions.ReadAsync(standalone.Id).ConfigureAwait(false))!.Status, "a final result on its branch is kept");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Multi-Runtime Flow", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<Captain> CreateCaptainAsync(TestDatabase testDb, string name, AgentRuntimeEnum runtime, CaptainStateEnum state = CaptainStateEnum.Idle)
        {
            Captain captain = new Captain(name, runtime);
            captain.State = state;
            return await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);
        }

        private static async Task<Vessel> CreateVesselAsync(TestDatabase testDb)
        {
            Vessel vessel = new Vessel("flow-vessel", "https://github.com/test/repo.git");
            vessel.LocalPath = Path.Combine(Path.GetTempPath(), "armada_flow_bare_" + Guid.NewGuid().ToString("N"));
            vessel.WorkingDirectory = Path.Combine(Path.GetTempPath(), "armada_flow_work_" + Guid.NewGuid().ToString("N"));
            vessel.DefaultBranch = "main";
            return await testDb.Driver.Vessels.CreateAsync(vessel).ConfigureAwait(false);
        }

        private static async Task<Mission> CreateRunningMissionAsync(TestDatabase testDb, Harness h, Vessel vessel, Voyage voyage, Captain captain, string persona, bool requiresReview, string? worktreePath = null)
        {
            Mission mission = new Mission("[" + persona + "] Task", "Do the task");
            mission.VesselId = vessel.Id;
            mission.VoyageId = voyage.Id;
            mission.Persona = persona;
            mission.RequiresReview = requiresReview;
            mission.Status = MissionStatusEnum.InProgress;
            mission.CaptainId = captain.Id;
            mission.BranchName = "armada/flow/" + persona.ToLowerInvariant();
            mission = await testDb.Driver.Missions.CreateAsync(mission).ConfigureAwait(false);

            Dock dock = new Dock(vessel.Id);
            dock.CaptainId = captain.Id;
            dock.WorktreePath = worktreePath ?? Path.Combine(h.Settings.DocksDirectory, vessel.Name, mission.Id);
            Directory.CreateDirectory(dock.WorktreePath);
            dock.BranchName = mission.BranchName;
            dock.Active = true;
            dock = await testDb.Driver.Docks.CreateAsync(dock).ConfigureAwait(false);

            mission.DockId = dock.Id;
            await testDb.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
            captain.CurrentMissionId = mission.Id;
            captain.CurrentDockId = dock.Id;
            await testDb.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);
            return mission;
        }

        private static async Task<string> GitAsync(string directory, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            string output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            return output;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
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

        private sealed class RecordingMergeGit : StubGitService, IGitService
        {
            public List<string> Calls { get; } = new List<string>();

            public Task<GitCommitMergeResult> MergeCommitAsync(string worktreePath, string commitHash, string? fetchBranch, string message, CancellationToken token = default)
            {
                Calls.Add(commitHash + "@" + fetchBranch);
                if (commitHash == "zzz999") return Task.FromResult(new GitCommitMergeResult(GitCommitMergeOutcomeEnum.Missing, "not here"));
                if (commitHash == "bbb222")
                {
                    GitCommitMergeResult conflict = new GitCommitMergeResult(GitCommitMergeOutcomeEnum.Conflicted, "CONFLICT");
                    conflict.ConflictedFiles = new List<string> { "src/shared.cs" };
                    return Task.FromResult(conflict);
                }
                return Task.FromResult(new GitCommitMergeResult(GitCommitMergeOutcomeEnum.Merged));
            }
        }

        private sealed class Harness
        {
            public ArmadaSettings Settings { get; }

            public ICaptainService Captains { get; }

            public MissionService Missions { get; }

            public Harness(TestDatabase testDb, IGitService dockGit, IGitService? missionGit = null)
            {
                LoggingModule logging = CreateLogging();
                Settings = new ArmadaSettings();
                Settings.DocksDirectory = Path.Combine(Path.GetTempPath(), "armada_flow_docks_" + Guid.NewGuid().ToString("N"));
                Settings.ReposDirectory = Path.Combine(Path.GetTempPath(), "armada_flow_repos_" + Guid.NewGuid().ToString("N"));
                IDockService docks = new DockService(logging, testDb.Driver, Settings, dockGit);
                Captains = new CaptainService(logging, testDb.Driver, Settings, dockGit, docks);
                Missions = new MissionService(logging, testDb.Driver, Settings, docks, Captains, git: missionGit ?? dockGit);
            }
        }

        #endregion
    }
}
