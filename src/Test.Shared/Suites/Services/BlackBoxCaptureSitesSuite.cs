namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
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
    /// Black box capture sites (plan 5.1 and 5.3): every step is wired at its failure write with the right phase, kind,
    /// status before and after and site file; real sites record their volatile facts (conflicted files, dirty paths,
    /// worktree existence, runtime exit, merge-queue step, cascade root); restart and erase paths keep the entry; operator
    /// stops are External; Blocked is a non-terminal entry; a failing capture never breaks the failure path.
    /// </summary>
    public sealed class BlackBoxCaptureSitesSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.BlackBoxCaptureSites";
        private const string MissionServiceFile = "src/Armada.Core/Services/MissionService.cs";
        private const string AdmiralFile = "src/Armada.Core/Services/AdmiralService.cs";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (SiteRow row in Rows())
            {
                SiteRow r = row;
                cases.Add(Case("step_" + r.Step, "Step " + r.Step + " is captured in " + r.CapturedIn + " as " + r.Phase + " with status " + r.Before + " -> " + r.After, TestTags.Positive,
                    () => AssertRowAsync(r)));
            }

            cases.Add(Case("every_step_has_a_row", "Every BlackBoxStepEnum value except Unknown has a wired row or a documented skip", TestTags.Positive, () =>
            {
                HashSet<BlackBoxStepEnum> covered = new HashSet<BlackBoxStepEnum>(Rows().Select(r => r.Step));
                foreach (BlackBoxStepEnum skipped in SkippedSteps()) covered.Add(skipped);
                foreach (BlackBoxStepEnum step in Enum.GetValues(typeof(BlackBoxStepEnum)))
                {
                    if (step == BlackBoxStepEnum.Unknown) continue;
                    AssertTrue(covered.Contains(step), "step " + step + " has no capture row and is not a documented skip");
                }
                return Task.CompletedTask;
            }));

            cases.Add(Case("skipped_steps_are_not_captured", "Steps documented as skipped (not a failure write, or a route-owned site) are not captured by Core or Server services", TestTags.Negative, () =>
            {
                string root = RepoRoot();
                List<string> sources = Directory.GetFiles(Path.Combine(root, "src", "Armada.Core", "Services"), "*.cs")
                    .Concat(new[] { Path.Combine(root, "src", "Armada.Server", "MissionLandingHandler.cs") }).ToList();
                foreach (BlackBoxStepEnum skipped in SkippedSteps())
                    foreach (string file in sources)
                        AssertFalse(File.ReadAllText(file).Contains("BlackBoxStepEnum." + skipped + ",", StringComparison.Ordinal), skipped + " appears as a capture step in " + file);
                return Task.CompletedTask;
            }));

            cases.Add(Case("dispatch_parent_voyage_not_runnable", "TryAssignAsync cancels a Pending mission of a cancelled voyage and records VoyageParentNotRunnable from Pending", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Services s = Services.Create(t.Driver, settings, bb.Capture, new StubGitService());
                Vessel vessel = await VesselAsync(t.Driver).ConfigureAwait(false);
                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("cancelled voyage") { Status = VoyageStatusEnum.Cancelled }).ConfigureAwait(false);
                Mission mission = await t.Driver.Missions.CreateAsync(new Mission("pending of cancelled voyage") { VesselId = vessel.Id, VoyageId = voyage.Id, Status = MissionStatusEnum.Pending }).ConfigureAwait(false);

                AssertFalse(await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "not assigned");
                BlackBoxEntry entry = await CurrentAsync(t.Driver, mission.Id).ConfigureAwait(false);
                AssertEntry(entry, BlackBoxPhaseEnum.Dispatch, BlackBoxStepEnum.VoyageParentNotRunnable, MissionFailureKindEnum.DependencyFailed, "Pending", "Cancelled");
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertEqual(MissionServiceFile, record.Site!.SourceFile);
                AssertEqual("TryAssignAsync", record.Site.Method);
            }));

            cases.Add(Case("assignment_failure_then_restart_then_fail", "A failed assignment keeps the captain and dock it cleared; ResetForRestart keeps the entry; failing again writes attempt 2 and keeps attempt 1", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Services s = Services.Create(t.Driver, settings, bb.Capture, new StubGitService());
                Vessel vessel = await VesselAsync(t.Driver).ConfigureAwait(false);
                Captain captain = await t.Driver.Captains.CreateAsync(new Captain("bb-assign-" + Short())).ConfigureAwait(false);
                Dock dock = await t.Driver.Docks.CreateAsync(new Dock(vessel.Id) { CaptainId = captain.Id, WorktreePath = TestTemp.NewDirectory("bb_assign_dock"), BranchName = "armada/bb/assign", Active = true }).ConfigureAwait(false);
                Mission mission = await t.Driver.Missions.CreateAsync(new Mission("assignment failure") { VesselId = vessel.Id, CaptainId = captain.Id, DockId = dock.Id, Status = MissionStatusEnum.Assigned, BranchName = "armada/bb/assign" }).ConfigureAwait(false);

                await InvokeAsync(s.Missions, "FailAssignmentAsync", mission, captain, dock, "The captain's runtime is misconfigured.", BlackBoxStepEnum.AssignmentFailed, new MissionLaunchConfigurationException("runtime misconfigured")).ConfigureAwait(false);
                BlackBoxEntry first = await CurrentAsync(t.Driver, mission.Id).ConfigureAwait(false);
                AssertEntry(first, BlackBoxPhaseEnum.Dispatch, BlackBoxStepEnum.AssignmentFailed, MissionFailureKindEnum.Infra, "Assigned", "Failed");
                AssertEqual(captain.Id, first.CaptainId, "captain cleared on the mission is still on the entry");
                AssertEqual(dock.Id, first.DockId, "dock cleared on the mission is still on the entry");
                BlackBoxRecord firstRecord = await bb.Pipeline.LoadRecordAsync(first).ConfigureAwait(false);
                AssertEqual(typeof(MissionLaunchConfigurationException).FullName, firstRecord.Mechanism!.ExceptionType);
                AssertEqual(captain.Name, firstRecord.Subject!.CaptainName);

                // ResetForRestart erases the live failure fields; the entry and the pointer survive.
                Mission reloaded = (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                typeof(MissionService).GetMethod("ResetForRestart", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { reloaded, reloaded.BranchName });
                await t.Driver.Missions.UpdateAsync(reloaded).ConfigureAwait(false);
                Mission reset = (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertNull(reset.FailureReason, "restart clears the reason");
                AssertNull(reset.FailureKind, "restart clears the kind");
                AssertEqual(first.Id, reset.LastBlackBoxEntryId, "restart keeps the pointer");
                AssertTrue((await t.Driver.BlackBoxEntries.ReadAsync(first.Id).ConfigureAwait(false))!.IsCurrent, "the entry is still current until the next failure");

                reset.Status = MissionStatusEnum.Assigned; reset.CaptainId = captain.Id; reset.DockId = dock.Id;
                await t.Driver.Missions.UpdateAsync(reset).ConfigureAwait(false);
                dock.Active = true; await t.Driver.Docks.UpdateAsync(dock).ConfigureAwait(false);
                await InvokeAsync(s.Missions, "FailAssignmentAsync", reset, captain, dock, "Harbor dock missing.", BlackBoxStepEnum.HarborUnavailable, new HarborDockNotFoundException("hbr_x", "/tmp/x", "Harbor dock missing.")).ConfigureAwait(false);
                BlackBoxEntry second = await CurrentAsync(t.Driver, mission.Id).ConfigureAwait(false);
                AssertEqual(2, second.Attempt);
                AssertEqual(first.Id, second.SupersedesId);
                AssertEqual(BlackBoxStepEnum.HarborUnavailable, second.Step);
                AssertFalse((await t.Driver.BlackBoxEntries.ReadAsync(first.Id).ConfigureAwait(false))!.IsCurrent, "attempt 1 kept, not current");
                BlackBoxRecord secondRecord = await bb.Pipeline.LoadRecordAsync(second).ConfigureAwait(false);
                AssertEqual(first.Id, secondRecord.Attempts.Single().EntryId, "attempt history");
            }));

            cases.Add(Case("provision_failure_non_terminal_once", "A worktree that cannot be created returns the mission to Pending with one non-terminal WorktreeCreate entry per distinct cause", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                StubGitService git = new StubGitService { ShouldThrowOnWorktree = true };
                Services s = Services.Create(t.Driver, settings, bb.Capture, git);
                Vessel vessel = await VesselAsync(t.Driver).ConfigureAwait(false);
                await t.Driver.Captains.CreateAsync(new Captain("bb-provision-" + Short())).ConfigureAwait(false);
                Mission mission = await t.Driver.Missions.CreateAsync(new Mission("provision failure") { VesselId = vessel.Id, Status = MissionStatusEnum.Pending }).ConfigureAwait(false);

                AssertFalse(await s.Missions.TryAssignAsync(mission, vessel).ConfigureAwait(false), "not assigned");
                AssertEqual(MissionStatusEnum.Pending, (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!.Status, "back to Pending");
                BlackBoxEntry entry = await CurrentAsync(t.Driver, mission.Id).ConfigureAwait(false);
                AssertEntry(entry, BlackBoxPhaseEnum.Provision, BlackBoxStepEnum.WorktreeCreate, null, "Assigned", "Pending");
                AssertContains("WorktreeCreate", entry.ReasonExcerpt ?? "", "reason names the step");
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertNotNull(record.Mechanism!.ExceptionType, "provisioning exception recorded");
                AssertNotNull(record.Workspace!.CheckoutPath, "worktree path recorded");

                AssertFalse(await s.Missions.TryAssignAsync((await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!, vessel).ConfigureAwait(false), "still failing");
                AssertEqual(1L, await CountAsync(t.Driver, mission.Id).ConfigureAwait(false), "the same cause is recorded once, not per dispatch cycle");

                git.ShouldThrowOnWorktree = false;
                git.HeadCommitResult = null;
                AssertFalse(await s.Missions.TryAssignAsync((await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!, vessel).ConfigureAwait(false), "no HEAD");
                BlackBoxEntry second = await CurrentAsync(t.Driver, mission.Id).ConfigureAwait(false);
                AssertEqual(BlackBoxStepEnum.StartCommitRecord, second.Step, "a new cause is a new entry");
                AssertEqual(2L, await CountAsync(t.Driver, mission.Id).ConfigureAwait(false));
            }));

            cases.Add(Case("start_commit_is_durable", "DockService writes the start commit into the dock's git anchors on provision; the scope check uses it when the .start file is gone", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                StubGitService git = new StubGitService { HeadCommitResult = "1111111111111111111111111111111111111111" };
                Services s = Services.Create(t.Driver, settings, bb.Capture, git);
                Vessel vessel = await VesselAsync(t.Driver).ConfigureAwait(false);
                Captain captain = await t.Driver.Captains.CreateAsync(new Captain("bb-start-" + Short())).ConfigureAwait(false);
                Mission mission = await t.Driver.Missions.CreateAsync(new Mission("scoped", "Touch only src/in.cs") { VesselId = vessel.Id, Status = MissionStatusEnum.WorkProduced }).ConfigureAwait(false);

                Dock? dock = await s.Docks.ProvisionAsync(vessel, captain, "armada/bb/start", mission.Id).ConfigureAwait(false);
                AssertNotNull(dock, "provisioned");
                Dock stored = (await t.Driver.Docks.ReadAsync(dock!.Id).ConfigureAwait(false))!;
                AssertEqual("1111111111111111111111111111111111111111", DockStartCommit.FromDock(stored), "start commit on the dock row");
                string startFile = Path.Combine(settings.LogDirectory, "docks", dock.Id + ".start");
                if (File.Exists(startFile)) File.Delete(startFile);

                mission.DockId = dock.Id;
                await t.Driver.Missions.UpdateAsync(mission).ConfigureAwait(false);
                git.ChangedFilesSinceResult = new List<string> { "src/in.cs", "src/out.cs" };
                bool failed = await InvokeAsync<bool>(s.Missions, "TryFailMissionForScopeViolationAsync", mission, stored, CancellationToken.None).ConfigureAwait(false);
                AssertTrue(failed, "scope check ran from the durable start commit");
                BlackBoxEntry entry = await CurrentAsync(t.Driver, mission.Id).ConfigureAwait(false);
                AssertEqual(BlackBoxStepEnum.ScopeViolation, entry.Step);
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertEqual("src/out.cs", record.Scope!.OutOfScopeFiles.Single());
                AssertEqual("1111111111111111111111111111111111111111", record.Workspace!.StartCommit, "workspace start commit from the dock anchors");

                AssertNull(DockStartCommit.FromDock(new Dock("vsl_x") { GitAnchorsJson = "{not json" }), "unreadable anchors give null");
                AssertNull(DockStartCommit.FromDock(null), "no dock gives null");
            }));

            foreach (RuntimeFailureKindEnum kind in new[] { RuntimeFailureKindEnum.Crash, RuntimeFailureKindEnum.UsageLimit, RuntimeFailureKindEnum.AuthFailure, RuntimeFailureKindEnum.ModelUnavailable, RuntimeFailureKindEnum.TaskFailure })
            {
                RuntimeFailureKindEnum k = kind;
                cases.Add(Case("execution_exit_" + k, "A terminal " + k + " exit records " + BlackBoxSiteFacts.ExecutionStepOf(k) + " with the exit code and runtime classification", TestTags.Positive,
                    () => AssertExitAsync(k, 3, 0, BlackBoxSiteFacts.ExecutionStepOf(k))));
            }

            cases.Add(Case("execution_interrupted_exit_over_limit", "An interrupted exit past the redispatch limit records RedispatchExhausted", TestTags.Negative,
                () => AssertExitAsync(RuntimeFailureKindEnum.Crash, -1, 50, BlackBoxStepEnum.RedispatchExhausted)));

            cases.Add(Case("review_denied_and_timed_out", "Denying a review with FailPipeline records ReviewDenied; the approval timer's default answer records ReviewTimeout", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Services s = Services.Create(t.Driver, settings, bb.Capture, new StubGitService());
                Mission denied = await t.Driver.Missions.CreateAsync(new Mission("denied") { Status = MissionStatusEnum.Review, RequiresReview = true }).ConfigureAwait(false);
                await s.Missions.DenyReviewAsync(denied.Id, "usr_reviewer", "not this", ReviewDenyActionEnum.FailPipeline).ConfigureAwait(false);
                AssertEntry(await CurrentAsync(t.Driver, denied.Id).ConfigureAwait(false), BlackBoxPhaseEnum.Review, BlackBoxStepEnum.ReviewDenied, MissionFailureKindEnum.ReviewDenied, "Review", "Failed");

                Mission timedOut = await t.Driver.Missions.CreateAsync(new Mission("timed out") { Status = MissionStatusEnum.Review, RequiresReview = true, ReviewDeadlineUtc = DateTime.UtcNow.AddMinutes(-1) }).ConfigureAwait(false);
                await s.Missions.DenyReviewAsync(timedOut.Id, null, "timer", ReviewDenyActionEnum.FailPipeline).ConfigureAwait(false);
                AssertEntry(await CurrentAsync(t.Driver, timedOut.Id).ConfigureAwait(false), BlackBoxPhaseEnum.Review, BlackBoxStepEnum.ReviewTimeout, MissionFailureKindEnum.ReviewDenied, "Review", "Failed");

                Mission rework = await t.Driver.Missions.CreateAsync(new Mission("rework") { Status = MissionStatusEnum.Review, RequiresReview = true }).ConfigureAwait(false);
                await s.Missions.DenyReviewAsync(rework.Id, "usr_reviewer", "again", ReviewDenyActionEnum.RetryStage).ConfigureAwait(false);
                AssertEqual(0L, await CountAsync(t.Driver, rework.Id).ConfigureAwait(false), "a rework deny is not a failure");
            }));

            cases.Add(Case("dependents_blocked_or_cancelled", "A failed stage blocks (non-terminal entry) or cancels its dependents with DependencyCancelled", TestTags.Positive, async () =>
            {
                foreach (bool block in new[] { true, false })
                {
                    using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    ArmadaSettings settings = Settings();
                    settings.BlockDependentsOfFailedStages = block;
                    using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                    Services s = Services.Create(t.Driver, settings, bb.Capture, new StubGitService());
                    Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("pipeline") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                    Mission upstream = await t.Driver.Missions.CreateAsync(new Mission("upstream") { VoyageId = voyage.Id, Status = MissionStatusEnum.Failed, FailureKind = MissionFailureKindEnum.Crash, FailureReason = "crashed" }).ConfigureAwait(false);
                    Mission dependent = await t.Driver.Missions.CreateAsync(new Mission("dependent") { VoyageId = voyage.Id, Status = MissionStatusEnum.Pending, DependsOnMissionId = upstream.Id }).ConfigureAwait(false);

                    await InvokeAsync(s.Missions, "CancelDependentPipelineStagesAsync", upstream, CancellationToken.None).ConfigureAwait(false);
                    BlackBoxEntry entry = await CurrentAsync(t.Driver, dependent.Id).ConfigureAwait(false);
                    AssertEntry(entry, BlackBoxPhaseEnum.Handoff, BlackBoxStepEnum.DependencyCancelled, MissionFailureKindEnum.DependencyFailed, "Pending", block ? "Blocked" : "Cancelled");
                    BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                    if (block) AssertTrue(record.Truncation.Notes.Any(n => n.Contains("Non-terminal", StringComparison.Ordinal)), "Blocked is recorded as non-terminal");
                }
            }));

            cases.Add(Case("voyage_settlement_cascade_root", "A voyage that settles Failed records its roll-up and points at the root mission's entry; a Complete voyage records nothing", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("settles failed") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission root = await t.Driver.Missions.CreateAsync(new Mission("root failure") { VoyageId = voyage.Id, Status = MissionStatusEnum.Failed, FailureKind = MissionFailureKindEnum.Crash, FailureReason = "crashed", CompletedUtc = DateTime.UtcNow.AddMinutes(-2) }).ConfigureAwait(false);
                string rootEntry = (await bb.Capture.RecordMissionFailureAsync(root, MissionStatusEnum.InProgress, BlackBoxStepEnum.ProcessCrash).ConfigureAwait(false))!;
                Mission ok = await t.Driver.Missions.CreateAsync(new Mission("complete") { VoyageId = voyage.Id, Status = MissionStatusEnum.Complete }).ConfigureAwait(false);
                List<Mission> missions = await t.Driver.Missions.EnumerateByVoyageAsync(voyage.Id).ConfigureAwait(false);

                await VoyageSettlement.ApplyAsync(t.Driver, voyage, VoyageStatusEnum.Failed, missions, CancellationToken.None, bb.Capture).ConfigureAwait(false);
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadCurrentByVoyageAsync(voyage.Id).ConfigureAwait(false))!;
                AssertNotNull(entry, "voyage entry");
                AssertEqual(BlackBoxPhaseEnum.Voyage, entry.Phase); AssertEqual(BlackBoxStepEnum.SettlementFailed, entry.Step);
                AssertEqual("InProgress", entry.StatusBefore); AssertEqual("Failed", entry.StatusAfter);
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertEqual(root.Id, record.Voyage!.CascadeRootMissionId);
                AssertEqual(rootEntry, record.Voyage.CascadeRootEntryId, "cascade root is the mission entry that caused it");
                AssertEqual(rootEntry, record.Related!.CascadeRootEntryId);
                AssertEqual(2, record.Voyage.MissionTotal);
                AssertEqual("src/Armada.Core/Services/VoyageSettlement.cs", record.Site!.SourceFile);

                Voyage fine = await t.Driver.Voyages.CreateAsync(new Voyage("settles complete") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                await VoyageSettlement.ApplyAsync(t.Driver, fine, VoyageStatusEnum.Complete, new List<Mission>(), CancellationToken.None, bb.Capture).ConfigureAwait(false);
                AssertNull(await t.Driver.BlackBoxEntries.ReadCurrentByVoyageAsync(fine.Id).ConfigureAwait(false), "a complete voyage has no entry");
            }));

            cases.Add(Case("voyage_halt_records_missions_and_voyage", "HaltVoyageAsync records each cancelled mission and then the Halted voyage with the failed mission as the cascade root", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Services s = Services.Create(t.Driver, settings, bb.Capture, new StubGitService());
                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("halts") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission failed = await t.Driver.Missions.CreateAsync(new Mission("failed") { VoyageId = voyage.Id, Status = MissionStatusEnum.Failed, FailureKind = MissionFailureKindEnum.Crash, FailureReason = "boom" }).ConfigureAwait(false);
                string failedEntry = (await bb.Capture.RecordMissionFailureAsync(failed, MissionStatusEnum.InProgress, BlackBoxStepEnum.ProcessCrash).ConfigureAwait(false))!;
                Mission sibling = await t.Driver.Missions.CreateAsync(new Mission("sibling") { VoyageId = voyage.Id, Status = MissionStatusEnum.Pending }).ConfigureAwait(false);

                await InvokeAsync(s.Admiral, "HaltVoyageAsync", voyage.Id, failed.Id, "boom", CancellationToken.None).ConfigureAwait(false);
                AssertEntry(await CurrentAsync(t.Driver, sibling.Id).ConfigureAwait(false), BlackBoxPhaseEnum.Handoff, BlackBoxStepEnum.DependencyCancelled, MissionFailureKindEnum.DependencyFailed, "Pending", "Cancelled");
                BlackBoxEntry voyageEntry = (await t.Driver.BlackBoxEntries.ReadCurrentByVoyageAsync(voyage.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStepEnum.Halted, voyageEntry.Step);
                AssertEqual("Cancelled", voyageEntry.StatusAfter);
                AssertTrue(voyageEntry.Triage != BlackBoxTriageEnum.External, "a halt after a failure is not an operator cancel");
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(voyageEntry).ConfigureAwait(false);
                AssertEqual(failedEntry, record.Voyage!.CascadeRootEntryId);
                AssertEqual("boom", record.Voyage.HaltReason);
                AssertEqual(AdmiralFile, record.Site!.SourceFile);
            }));

            cases.Add(Case("operator_stop_and_cancel_are_external", "Operator stops, mission cancels and voyage cancels are captured and triaged External", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Services s = Services.Create(t.Driver, settings, bb.Capture, new StubGitService());

                Mission pending = await t.Driver.Missions.CreateAsync(new Mission("cancel me") { Status = MissionStatusEnum.Pending }).ConfigureAwait(false);
                await s.Admiral.CancelMissionAsync(pending).ConfigureAwait(false);
                BlackBoxEntry cancel = await CurrentAsync(t.Driver, pending.Id).ConfigureAwait(false);
                AssertEntry(cancel, BlackBoxPhaseEnum.External, BlackBoxStepEnum.OperatorTransition, null, "Pending", "Cancelled");
                AssertEqual(BlackBoxTriageEnum.External, cancel.Triage);

                Captain captain = await t.Driver.Captains.CreateAsync(new Captain("bb-recall-" + Short()) { State = CaptainStateEnum.Working }).ConfigureAwait(false);
                Mission running = await t.Driver.Missions.CreateAsync(new Mission("recall me") { Status = MissionStatusEnum.InProgress, CaptainId = captain.Id }).ConfigureAwait(false);
                captain.CurrentMissionId = running.Id; await t.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);
                await s.Captains.RecallAsync(captain.Id, null, new OperatorStopContext { Source = OperatorStopContext.SourceRestStopCaptain, UserId = "usr_op" }).ConfigureAwait(false);
                BlackBoxEntry stop = await CurrentAsync(t.Driver, running.Id).ConfigureAwait(false);
                AssertEntry(stop, BlackBoxPhaseEnum.Execution, BlackBoxStepEnum.OperatorStop, MissionFailureKindEnum.OperatorAction, "InProgress", "Cancelled");
                AssertEqual(BlackBoxTriageEnum.External, stop.Triage);
                AssertEqual("src/Armada.Core/Services/CaptainService.cs", (await bb.Pipeline.LoadRecordAsync(stop).ConfigureAwait(false)).Site!.SourceFile);

                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("cancel voyage") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission queued = await t.Driver.Missions.CreateAsync(new Mission("queued") { VoyageId = voyage.Id, Status = MissionStatusEnum.Pending }).ConfigureAwait(false);
                await VoyageCancellation.CancelAsync(t.Driver, s.Admiral, voyage, null).ConfigureAwait(false);
                BlackBoxEntry voyageEntry = (await t.Driver.BlackBoxEntries.ReadCurrentByVoyageAsync(voyage.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxStepEnum.Cancelled, voyageEntry.Step);
                AssertEqual(BlackBoxTriageEnum.External, voyageEntry.Triage);
                AssertEqual(BlackBoxStepEnum.OperatorTransition, (await CurrentAsync(t.Driver, queued.Id).ConfigureAwait(false)).Step, "its missions are operator transitions");
            }));

            cases.Add(Case("landing_conflict_records_files", "A real target-integration conflict records TargetIntegration, the conflicted files and blackbox_files rows, the branch tip and the target tip", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RealLandingFixture f = await RealLandingFixture.CreateAsync(t.Driver, LandingModeEnum.MergeAndPush, "shared.txt", "mission version\n").ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, f.Settings);
                f.Handler.BlackBox = bb.Capture;
                f.Repos.AdvanceMain("shared.txt", "target version\n");
                string target = f.Repos.OriginHead("main");

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock).ConfigureAwait(false);
                BlackBoxEntry entry = await CurrentAsync(t.Driver, f.Mission.Id).ConfigureAwait(false);
                AssertEntry(entry, BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.TargetIntegration, MissionFailureKindEnum.LandingConflict, "WorkProduced", "LandingFailed");
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                AssertTrue(record.Conflict!.ConflictedFiles.Contains("shared.txt"), "conflicted file recorded: " + String.Join(",", record.Conflict.ConflictedFiles));
                EnumerationResult<BlackBoxFile> files = await t.Driver.BlackBoxFiles.EnumerateAsync(entry.Id, BlackBoxFileRoleEnum.Conflicted, new EnumerationQuery()).ConfigureAwait(false);
                AssertTrue(files.Objects.Any(x => x.Path == "shared.txt"), "blackbox_files row with role Conflicted");
                AssertEqual(f.WorkCommit, record.Workspace!.BranchTip, "dock head before integration");
                AssertEqual(target, record.Workspace.TargetTipAtFailure, "target tip integrated against");
                AssertEqual(true, record.Workspace.WorktreeExists, "the worktree existed");
                AssertEqual("src/Armada.Server/MissionLandingHandler.cs", record.Site!.SourceFile);
            }));

            cases.Add(Case("landing_dock_missing_records_worktree_gone", "A landing whose worktree is gone and cannot be recreated records DockMissing with WorktreeExisted=false", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RealLandingFixture f = await RealLandingFixture.CreateAsync(t.Driver, LandingModeEnum.MergeAndPush).ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, f.Settings);
                f.Handler.BlackBox = bb.Capture;
                Directory.Delete(f.Dock.WorktreePath!, true);
                await t.Driver.Docks.DeleteAsync(f.Dock.Id).ConfigureAwait(false);

                await f.Handler.HandleMissionCompleteAsync(f.Mission, f.Dock).ConfigureAwait(false);
                BlackBoxEntry entry = await CurrentAsync(t.Driver, f.Mission.Id).ConfigureAwait(false);
                AssertEntry(entry, BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.DockMissing, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
                AssertEqual(false, (await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false)).Workspace!.WorktreeExists, "worktree did not exist");
            }));

            cases.Add(Case("retry_landing_dirty_checkout_and_conflict", "A retried landing records dirty paths equal to the porcelain paths (DirtyCheckout) and conflicted files (RetryLanding)", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                StubGitService git = new StubGitService();
                git.ExistingBranches.Add("armada/bb/retry");
                LandingService landing = new LandingService(BlackBoxTestKit.Logging(), t.Driver, settings, git) { BlackBox = bb.Capture };
                Vessel vessel = await VesselAsync(t.Driver).ConfigureAwait(false);

                GitDirtyCheckoutException dirty = new GitDirtyCheckoutException("/work/checkout", " M src/app.cs\n M docs/readme.md\n");
                landing.OnPerformLanding = (m, d) => throw dirty;
                Mission dirtyMission = await t.Driver.Missions.CreateAsync(new Mission("dirty retry") { VesselId = vessel.Id, Status = MissionStatusEnum.LandingFailed, BranchName = "armada/bb/retry" }).ConfigureAwait(false);
                AssertFalse(await landing.RetryLandingAsync(dirtyMission.Id).ConfigureAwait(false), "retry fails");
                BlackBoxEntry dirtyEntry = await CurrentAsync(t.Driver, dirtyMission.Id).ConfigureAwait(false);
                AssertEntry(dirtyEntry, BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum.Infra, "LandingFailed", "LandingFailed");
                BlackBoxRecord dirtyRecord = await bb.Pipeline.LoadRecordAsync(dirtyEntry).ConfigureAwait(false);
                AssertEqual(String.Join("|", dirty.ModifiedPaths), String.Join("|", dirtyRecord.Workspace!.DirtyPaths), "dirty paths are the porcelain paths");
                AssertTrue(dirtyRecord.Workspace.CheckoutDirty == true, "checkout dirty");
                AssertEqual("/work/checkout", dirtyRecord.Workspace.CheckoutPath);
                AssertEqual(2L, (await t.Driver.BlackBoxFiles.EnumerateAsync(dirtyEntry.Id, BlackBoxFileRoleEnum.DirtyInCheckout, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords);

                landing.OnPerformLanding = (m, d) => throw new GitMergeConflictException("merge conflict", new[] { "src/a.cs", "src/b.cs" });
                Mission conflictMission = await t.Driver.Missions.CreateAsync(new Mission("conflict retry") { VesselId = vessel.Id, Status = MissionStatusEnum.LandingFailed, BranchName = "armada/bb/retry" }).ConfigureAwait(false);
                AssertFalse(await landing.RetryLandingAsync(conflictMission.Id).ConfigureAwait(false), "retry fails");
                BlackBoxEntry conflictEntry = await CurrentAsync(t.Driver, conflictMission.Id).ConfigureAwait(false);
                AssertEntry(conflictEntry, BlackBoxPhaseEnum.Landing, BlackBoxStepEnum.RetryLanding, MissionFailureKindEnum.LandingConflict, "LandingFailed", "LandingFailed");
                AssertEqual(2L, (await t.Driver.BlackBoxFiles.EnumerateAsync(conflictEntry.Id, BlackBoxFileRoleEnum.Conflicted, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords);
                AssertEqual("src/Armada.Core/Services/LandingService.cs", (await bb.Pipeline.LoadRecordAsync(conflictEntry).ConfigureAwait(false)).Site!.SourceFile);
            }));

            cases.Add(Case("merge_queue_steps", "Each merge-queue failure step records its queue step, entry, files, target commit and failing command; a landed entry records nothing", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                MergeQueueService queue = new MergeQueueService(BlackBoxTestKit.Logging(), t.Driver, settings, new StubGitService()) { BlackBox = bb.Capture };
                Dictionary<MergeQueueFailureStepEnum, BlackBoxStepEnum> expected = new Dictionary<MergeQueueFailureStepEnum, BlackBoxStepEnum>
                {
                    [MergeQueueFailureStepEnum.Resolution] = BlackBoxStepEnum.QueueResolveRepo,
                    [MergeQueueFailureStepEnum.Preparation] = BlackBoxStepEnum.QueueProcessing,
                    [MergeQueueFailureStepEnum.Merge] = BlackBoxStepEnum.QueueMergeConflict,
                    [MergeQueueFailureStepEnum.Verification] = BlackBoxStepEnum.QueueTests,
                    [MergeQueueFailureStepEnum.Publication] = BlackBoxStepEnum.QueuePush
                };
                foreach (KeyValuePair<MergeQueueFailureStepEnum, BlackBoxStepEnum> pair in expected)
                {
                    Mission mission = await t.Driver.Missions.CreateAsync(new Mission("queued " + pair.Key) { Status = MissionStatusEnum.WorkProduced }).ConfigureAwait(false);
                    MergeQueueFailure failure = new MergeQueueFailure
                    {
                        MergeEntryId = "mrg_" + pair.Key, MissionId = mission.Id, BranchName = "armada/q", TargetBranch = "main", TargetCommit = "2222222222222222222222222222222222222222",
                        Step = pair.Key, Kind = pair.Key == MergeQueueFailureStepEnum.Merge ? MissionFailureKindEnum.LandingConflict : MissionFailureKindEnum.Infra, Reason = "queue failed at " + pair.Key
                    };
                    if (pair.Key == MergeQueueFailureStepEnum.Merge) failure.ConflictedFiles = new List<string> { "lib/x.cs" };
                    Exception? ex = pair.Key == MergeQueueFailureStepEnum.Verification ? new LandingVerificationException("tests failed", "Failed", "dotnet test", 1, "abc", false) : null;
                    await InvokeAsync(queue, "ReconcileMissionStatusAsync", mission.Id, MissionStatusEnum.LandingFailed, failure.Reason, CancellationToken.None, null, failure, ex).ConfigureAwait(false);

                    BlackBoxEntry entry = await CurrentAsync(t.Driver, mission.Id).ConfigureAwait(false);
                    AssertEntry(entry, BlackBoxPhaseEnum.MergeQueue, pair.Value, failure.Kind, "WorkProduced", "LandingFailed");
                    AssertEqual(failure.MergeEntryId, entry.MergeEntryId);
                    BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
                    AssertEqual(failure.TargetCommit, record.Workspace!.TargetTipAtFailure, "target commit integrated against");
                    if (pair.Key == MergeQueueFailureStepEnum.Merge) AssertEqual("lib/x.cs", record.Conflict!.ConflictedFiles.Single());
                    if (pair.Key == MergeQueueFailureStepEnum.Verification)
                    {
                        AssertEqual("dotnet test", record.Mechanism!.FailingCommand);
                        AssertEqual(1, record.Mechanism.CommandExitCode);
                    }
                }

                Mission landed = await t.Driver.Missions.CreateAsync(new Mission("landed") { Status = MissionStatusEnum.WorkProduced }).ConfigureAwait(false);
                await InvokeAsync(queue, "ReconcileMissionStatusAsync", landed.Id, MissionStatusEnum.Complete, "landed", CancellationToken.None, null, null, null).ConfigureAwait(false);
                AssertEqual(0L, await CountAsync(t.Driver, landed.Id).ConfigureAwait(false), "a landed entry is not a failure");
            }));

            cases.Add(Case("erasing_paths_keep_the_entry", "Paths that clear FailureReason and FailureKind (restart, re-dispatch) keep the entry and the mission's pointer", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.ScopeViolation).ConfigureAwait(false))!;
                Mission live = (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                live.Status = MissionStatusEnum.Pending; live.FailureReason = null; live.FailureKind = null; live.LastBlackBoxEntryId = null;
                await t.Driver.Missions.UpdateAsync(live).ConfigureAwait(false);
                Mission after = (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                AssertNull(after.FailureReason);
                AssertEqual(id, after.LastBlackBoxEntryId, "a full-row update never clears the pointer");
                BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!;
                AssertTrue(entry.IsCurrent, "entry kept");
                AssertEqual(mission.FailureReason, (await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false)).Classification!.RawReason, "the erased reason survives in the record");
            }));

            cases.Add(Case("status_changed_on_every_capture", "Each capture whose status changed emits mission.status_changed with PreviousStatus; a same-status entry emits none; it is emitted with the black box disabled", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = BlackBoxTestKit.Settings();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.ProcessCrash).ConfigureAwait(false);
                List<ArmadaEvent> changes = await StatusEventsAsync(t.Driver, mission.Id).ConfigureAwait(false);
                AssertEqual(1, changes.Count, "one status change");
                MissionStatusChangedPayload payload = JsonSerializer.Deserialize<MissionStatusChangedPayload>(changes[0].Payload!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                AssertEqual(MissionStatusEnum.Failed, payload.Status);
                AssertEqual(MissionStatusEnum.InProgress, payload.PreviousStatus);

                await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.Failed, BlackBoxStepEnum.UnconfirmedStop).ConfigureAwait(false);
                AssertEqual(1, (await StatusEventsAsync(t.Driver, mission.Id).ConfigureAwait(false)).Count, "a same-status entry is not a status change");

                List<string> delivered = new List<string>();
                ((BlackBoxCapture)bb.Capture).MissionStatusChanged = (m, previous, message) => { delivered.Add(m.Id + ":" + previous); return Task.CompletedTask; };
                await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.NoOp).ConfigureAwait(false);
                AssertEqual(mission.Id + ":WorkProduced", delivered.Single(), "routed through the Admiral's EmitMissionStatusChangedAsync");

                settings.BlackBox.Enabled = false;
                ((BlackBoxCapture)bb.Capture).MissionStatusChanged = null;
                Mission other = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                AssertNull(await bb.Capture.RecordMissionFailureAsync(other, MissionStatusEnum.InProgress, BlackBoxStepEnum.ProcessCrash).ConfigureAwait(false), "no entry when disabled");
                AssertEqual(1, (await StatusEventsAsync(t.Driver, other.Id).ConfigureAwait(false)).Count, "the status timeline does not depend on the black box");
            }));

            cases.Add(Case("failing_capture_never_breaks_the_site", "A capture that throws, and a store that throws, never break the failure write or the work after it", TestTags.Negative, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                Services s = Services.Create(t.Driver, settings, new ThrowingCapture(), new StubGitService());
                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("throwing capture") { Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
                Mission mission = await t.Driver.Missions.CreateAsync(new Mission("denied") { VoyageId = voyage.Id, Status = MissionStatusEnum.Review, RequiresReview = true }).ConfigureAwait(false);
                Mission dependent = await t.Driver.Missions.CreateAsync(new Mission("dependent") { VoyageId = voyage.Id, Status = MissionStatusEnum.Pending, DependsOnMissionId = mission.Id }).ConfigureAwait(false);
                Mission denied = await s.Missions.DenyReviewAsync(mission.Id, "usr_r", "no", ReviewDenyActionEnum.FailPipeline).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Failed, denied.Status, "the failure write stands");
                AssertTrue((await t.Driver.Missions.ReadAsync(dependent.Id).ConfigureAwait(false))!.Status != MissionStatusEnum.Pending, "the work after the capture ran (dependents settled)");
                AssertEqual(VoyageStatusEnum.Failed, (await t.Driver.Voyages.ReadAsync(voyage.Id).ConfigureAwait(false))!.Status, "voyage settled");

                using TestDatabase t2 = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                RecordingBlackBoxEventSink sink = new RecordingBlackBoxEventSink();
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t2.Driver, Settings(), sink);
                bb.Stores.Register(BlackBoxStorageTargetEnum.Database, _ => new ScriptedBlackBoxStore(BlackBoxStorageTargetEnum.Database) { FailWrites = true });
                Services s2 = Services.Create(t2.Driver, Settings(), bb.Capture, new StubGitService());
                Mission second = await t2.Driver.Missions.CreateAsync(new Mission("denied with a failing store") { Status = MissionStatusEnum.Review, RequiresReview = true }).ConfigureAwait(false);
                Mission secondDenied = await s2.Missions.DenyReviewAsync(second.Id, "usr_r", "no", ReviewDenyActionEnum.FailPipeline).ConfigureAwait(false);
                AssertEqual(MissionStatusEnum.Failed, secondDenied.Status, "the failure write stands with a failing store");
                AssertEqual(MissionStatusEnum.Failed, (await t2.Driver.Missions.ReadAsync(second.Id).ConfigureAwait(false))!.Status);
            }));

            cases.Add(Case("harbor_link_state", "GetLinkState reports NotLinked for an unlinked Harbor and Unknown for no id; a Harbor-hosted dock's entry records it", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
                LoggingModule logging = BlackBoxTestKit.Logging();
                HarborConnectionManager harbors = new HarborConnectionManager(new HarborService(t.Driver, logging), logging, null);
                AssertEqual(HarborLinkStateEnum.NotLinked, harbors.GetLinkState("hbr_away"));
                AssertEqual(HarborLinkStateEnum.Unknown, harbors.GetLinkState(""));
                AssertNull(harbors.GetLinkLastSeenUtc("hbr_away"));

                ((BlackBoxCapture)bb.Capture).Harbors = harbors;
                Vessel vessel = await VesselAsync(t.Driver).ConfigureAwait(false);
                Dock dock = await t.Driver.Docks.CreateAsync(new Dock(vessel.Id) { HarborId = "hbr_away", WorktreePath = "/harbor/dock", BranchName = "armada/h" }).ConfigureAwait(false);
                Mission mission = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string id = (await bb.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.InProgress, BlackBoxStepEnum.HarborUnavailable, c => c.Dock = dock).ConfigureAwait(false))!;
                BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync((await t.Driver.BlackBoxEntries.ReadAsync(id).ConfigureAwait(false))!).ConfigureAwait(false);
                AssertEqual("NotLinked", record.Workspace!.HarborLinkState);
                AssertEqual(BlackBoxHostKindEnum.Harbor, record.Site!.HostKind);

                Mission local = await BlackBoxTestKit.FailedMissionAsync(t.Driver).ConfigureAwait(false);
                string localId = (await bb.Capture.RecordMissionFailureAsync(local, MissionStatusEnum.InProgress, BlackBoxStepEnum.ProcessCrash).ConfigureAwait(false))!;
                AssertNull((await bb.Pipeline.LoadRecordAsync((await t.Driver.BlackBoxEntries.ReadAsync(localId).ConfigureAwait(false))!).ConfigureAwait(false)).Workspace!.HarborLinkState, "an Admiral-hosted mission has no Harbor link state");
            }));

            cases.Add(Case("memory_pressure_dispatch_deferred", "A memory-pressure deferral records one typed mission.dispatch_deferred event per Pending mission and reason", TestTags.Positive, async () =>
            {
                using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ArmadaSettings settings = Settings();
                settings.MinAvailableMemoryBytesForLaunch = 1024L * 1024 * 1024;
                Services s = Services.Create(t.Driver, settings, null, new StubGitService());
                typeof(AdmiralService).GetField("_ResourceProbe", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s.Admiral, new StarvedProbe());
                Vessel vessel = await VesselAsync(t.Driver).ConfigureAwait(false);
                Mission a = await t.Driver.Missions.CreateAsync(new Mission("waits a") { Status = MissionStatusEnum.Pending, VesselId = vessel.Id }).ConfigureAwait(false);
                Mission b = await t.Driver.Missions.CreateAsync(new Mission("waits b") { Status = MissionStatusEnum.Pending, VesselId = vessel.Id }).ConfigureAwait(false);

                await InvokeAsync(s.Admiral, "DispatchPendingMissionsAsync", CancellationToken.None).ConfigureAwait(false);
                await InvokeAsync(s.Admiral, "DispatchPendingMissionsAsync", CancellationToken.None).ConfigureAwait(false);
                foreach (Mission m in new[] { a, b })
                {
                    EnumerationResult<ArmadaEvent> events = await t.Driver.Events.EnumerateAsync(new EnumerationQuery { MissionId = m.Id, EventType = "mission.dispatch_deferred", PageSize = 10 }).ConfigureAwait(false);
                    AssertEqual(1, events.Objects.Count, "one deferral event per mission and reason, not per cycle");
                    MissionDispatchDeferredPayload payload = JsonSerializer.Deserialize<MissionDispatchDeferredPayload>(events.Objects[0].Payload!)!;
                    AssertEqual(m.Id, payload.MissionId);
                    AssertEqual(settings.MinAvailableMemoryBytesForLaunch, payload.MinimumAvailableBytes);
                    AssertEqual(1L, payload.AvailableBytes);
                }
            }));

            cases.Add(Case("site_facts_negative", "BlackBoxSiteFacts ignores null exceptions, keeps existing facts and maps unknown steps to their fallback", TestTags.Negative, () =>
            {
                BlackBoxCaptureContext context = new BlackBoxCaptureContext { Mission = new Mission("x") };
                BlackBoxSiteFacts.ApplyException(context, null);
                AssertNull(context.Exception); AssertNull(context.GitFacts);
                AssertNull(BlackBoxSiteFacts.GitFactsFrom(new InvalidOperationException("plain")));
                AssertEqual(BlackBoxStepEnum.Push, BlackBoxSiteFacts.LandingStepOf(new InvalidOperationException("plain"), BlackBoxStepEnum.Push));
                AssertEqual(BlackBoxStepEnum.DirtyCheckout, BlackBoxSiteFacts.LandingStepOf(new InvalidOperationException("wrapped", new GitDirtyCheckoutException("/c", " M a\n")), BlackBoxStepEnum.Push), "inner exceptions are read");
                AssertEqual(BlackBoxStepEnum.QueueProcessing, BlackBoxSiteFacts.MergeQueueStepOf(null));
                context.GitFacts = new BlackBoxGitFacts { ConflictedFiles = new List<string> { "a" }, TargetTipAfter = "kept" };
                BlackBoxSiteFacts.ApplyException(context, new GitMergeConflictException("c", new[] { "a", "b" }));
                AssertEqual("a,b", String.Join(",", context.GitFacts!.ConflictedFiles), "lists are unioned without duplicates");
                AssertEqual("kept", context.GitFacts.TargetTipAfter, "single values already set are kept");
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box capture sites", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<SiteRow> Rows()
        {
            const string ms = MissionServiceFile;
            const string msbb = "src/Armada.Core/Services/MissionService.BlackBox.cs";
            const string completion = "src/Armada.Core/Services/MissionService.Completion.cs";
            const string review = "src/Armada.Core/Services/MissionService.Review.cs";
            const string handoff = "src/Armada.Core/Services/MissionService.Handoff.cs";
            const string barrier = "src/Armada.Core/Services/MissionService.IntegrationBarrier.cs";
            const string facts = "src/Armada.Core/BlackBox/BlackBoxSiteFacts.cs";
            const string dock = "src/Armada.Core/Services/DockService.cs";
            const string captains = "src/Armada.Core/Services/CaptainService.cs";
            const string landing = "src/Armada.Server/MissionLandingHandler.cs";
            const string retry = "src/Armada.Core/Services/LandingService.cs";
            const string integrity = "src/Armada.Core/Services/LandingIntegrityService.cs";
            const string queue = "src/Armada.Core/Services/MergeQueueService.cs";
            const string recovery = "src/Armada.Core/Services/MissionRecoveryCoordinator.cs";
            const string settlement = "src/Armada.Core/Services/VoyageSettlement.cs";
            const string cancellation = "src/Armada.Core/Services/VoyageCancellation.cs";
            const string adm = AdmiralFile;

            yield return M(BlackBoxStepEnum.VoyageParentNotRunnable, BlackBoxPhaseEnum.Dispatch, ms, ms, MissionFailureKindEnum.DependencyFailed, "Pending", "Cancelled");
            yield return M(BlackBoxStepEnum.AssignmentFailed, BlackBoxPhaseEnum.Dispatch, ms, ms, MissionFailureKindEnum.Infra, "Assigned", "Failed");
            yield return M(BlackBoxStepEnum.HarborUnavailable, BlackBoxPhaseEnum.Dispatch, ms, ms, MissionFailureKindEnum.Infra, "Assigned", "Failed");
            yield return M(BlackBoxStepEnum.DockProvision, BlackBoxPhaseEnum.Provision, msbb, msbb, null, "Assigned", "Pending");
            yield return M(BlackBoxStepEnum.WorktreeCreate, BlackBoxPhaseEnum.Provision, dock, msbb, null, "Assigned", "Pending");
            yield return M(BlackBoxStepEnum.StartCommitRecord, BlackBoxPhaseEnum.Provision, dock, msbb, null, "Assigned", "Pending");
            yield return M(BlackBoxStepEnum.BranchCreate, BlackBoxPhaseEnum.Provision, ms, ms, MissionFailureKindEnum.Infra, "Assigned", "Failed");
            yield return M(BlackBoxStepEnum.ProcessCrash, BlackBoxPhaseEnum.Execution, facts, adm, MissionFailureKindEnum.Crash, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.UsageLimit, BlackBoxPhaseEnum.Execution, facts, adm, MissionFailureKindEnum.Crash, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.AuthFailure, BlackBoxPhaseEnum.Execution, facts, adm, MissionFailureKindEnum.Crash, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.ModelUnavailable, BlackBoxPhaseEnum.Execution, facts, adm, MissionFailureKindEnum.Crash, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.Stall, BlackBoxPhaseEnum.Execution, adm, adm, MissionFailureKindEnum.StallRecoveryExhausted, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.MaxRuntime, BlackBoxPhaseEnum.Execution, adm, adm, MissionFailureKindEnum.MaxRuntimeExceeded, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.RecoveryExhausted, BlackBoxPhaseEnum.Execution, captains, captains, MissionFailureKindEnum.StallRecoveryExhausted, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.Interrupted, BlackBoxPhaseEnum.Execution, adm, adm, MissionFailureKindEnum.Infra, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.OperatorStop, BlackBoxPhaseEnum.Execution, captains, captains, MissionFailureKindEnum.OperatorAction, "InProgress", "Cancelled");
            yield return M(BlackBoxStepEnum.NoOp, BlackBoxPhaseEnum.Completion, completion, completion, MissionFailureKindEnum.NoOp, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.Unfinished, BlackBoxPhaseEnum.Completion, completion, completion, MissionFailureKindEnum.NoOp, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.CaptainBlocked, BlackBoxPhaseEnum.Completion, completion, completion, MissionFailureKindEnum.Blocked, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.InvalidOutput, BlackBoxPhaseEnum.Completion, facts, adm, MissionFailureKindEnum.InvalidOutput, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.ScopeViolation, BlackBoxPhaseEnum.Completion, ms, ms, MissionFailureKindEnum.ScopeViolation, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.BoundaryViolation, BlackBoxPhaseEnum.Completion, completion, completion, MissionFailureKindEnum.Boundary, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.DefinitionOfDone, BlackBoxPhaseEnum.Completion, completion, completion, MissionFailureKindEnum.TestFail, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.StageCheck, BlackBoxPhaseEnum.Completion, adm, adm, MissionFailureKindEnum.InvalidOutput, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.JudgeRejected, BlackBoxPhaseEnum.Review, completion, completion, MissionFailureKindEnum.JudgeRejected, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.ReviewDenied, BlackBoxPhaseEnum.Review, review, review, MissionFailureKindEnum.ReviewDenied, "Review", "Failed");
            yield return M(BlackBoxStepEnum.ReviewTimeout, BlackBoxPhaseEnum.Review, review, review, MissionFailureKindEnum.ReviewDenied, "Review", "Failed");
            yield return M(BlackBoxStepEnum.ReviewApprovedDockMissing, BlackBoxPhaseEnum.Review, review, review, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.PlanParse, BlackBoxPhaseEnum.Handoff, handoff, handoff, MissionFailureKindEnum.InvalidOutput, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.StageBranchHarden, BlackBoxPhaseEnum.Handoff, handoff, handoff, MissionFailureKindEnum.LandingConflict, "WorkProduced", "Failed");
            yield return M(BlackBoxStepEnum.DependencyCancelled, BlackBoxPhaseEnum.Handoff, handoff, handoff, MissionFailureKindEnum.DependencyFailed, "Pending", "Blocked");
            yield return M(BlackBoxStepEnum.IntegrationBarrier, BlackBoxPhaseEnum.Handoff, barrier, barrier, MissionFailureKindEnum.DependencyFailed, "Pending", "Cancelled");
            yield return M(BlackBoxStepEnum.DockMissing, BlackBoxPhaseEnum.Landing, landing, landing, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.TargetIntegration, BlackBoxPhaseEnum.Landing, landing, landing, MissionFailureKindEnum.LandingConflict, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.Verification, BlackBoxPhaseEnum.Landing, landing, landing, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.DirtyCheckout, BlackBoxPhaseEnum.Landing, facts, landing, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.UnpublishedAncestry, BlackBoxPhaseEnum.Landing, facts, landing, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.Push, BlackBoxPhaseEnum.Landing, landing, landing, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.PullRequest, BlackBoxPhaseEnum.Landing, landing, landing, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.FastForward, BlackBoxPhaseEnum.Landing, landing, landing, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.LocalMerge, BlackBoxPhaseEnum.Landing, landing, landing, MissionFailureKindEnum.Infra, "WorkProduced", "WorkProduced");
            yield return M(BlackBoxStepEnum.RetryLanding, BlackBoxPhaseEnum.Landing, retry, retry, MissionFailureKindEnum.Infra, "LandingFailed", "LandingFailed");
            yield return M(BlackBoxStepEnum.DeferralExpired, BlackBoxPhaseEnum.Landing, integrity, integrity, MissionFailureKindEnum.Infra, "LandingFailed", "LandingFailed");
            yield return M(BlackBoxStepEnum.QueueResolveRepo, BlackBoxPhaseEnum.MergeQueue, facts, queue, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.QueueMergeConflict, BlackBoxPhaseEnum.MergeQueue, facts, queue, MissionFailureKindEnum.LandingConflict, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.QueueTests, BlackBoxPhaseEnum.MergeQueue, facts, queue, MissionFailureKindEnum.TestFail, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.QueueProcessing, BlackBoxPhaseEnum.MergeQueue, facts, queue, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.QueuePush, BlackBoxPhaseEnum.MergeQueue, facts, queue, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return M(BlackBoxStepEnum.RescueFailed, BlackBoxPhaseEnum.Recovery, recovery, recovery, MissionFailureKindEnum.Crash, "Failed", "Failed");
            yield return M(BlackBoxStepEnum.RedispatchExhausted, BlackBoxPhaseEnum.Recovery, adm, adm, MissionFailureKindEnum.Infra, "InProgress", "Failed");
            yield return M(BlackBoxStepEnum.OrphanedExit, BlackBoxPhaseEnum.Recovery, adm, adm, MissionFailureKindEnum.Infra, "InProgress", "WorkProduced");
            yield return M(BlackBoxStepEnum.UnconfirmedStop, BlackBoxPhaseEnum.Recovery, adm, adm, MissionFailureKindEnum.Infra, "InProgress", "InProgress");
            yield return M(BlackBoxStepEnum.MissingMergeHandoff, BlackBoxPhaseEnum.Recovery, adm, adm, MissionFailureKindEnum.Infra, "WorkProduced", "LandingFailed");
            yield return V(BlackBoxStepEnum.SettlementFailed, settlement, "InProgress", "Failed");
            yield return V(BlackBoxStepEnum.Halted, adm, "InProgress", "Cancelled");
            yield return V(BlackBoxStepEnum.StageFailed, adm, "InProgress", "Failed");
            yield return V(BlackBoxStepEnum.Cancelled, cancellation, "InProgress", "Cancelled");
            yield return M(BlackBoxStepEnum.OperatorTransition, BlackBoxPhaseEnum.External, adm, adm, null, "Pending", "Cancelled");
        }

        private static IEnumerable<BlackBoxStepEnum> SkippedSteps()
        {
            // NoEligibleCaptain and MemoryPressure are waits, not failure writes (MemoryPressure records the typed
            // mission.dispatch_deferred event). VesselDeleted is written by the vessel routes and MCP tools, which are
            // owned by the API wave; CancelMissionAsync records those missions as OperatorTransition.
            return new[] { BlackBoxStepEnum.NoEligibleCaptain, BlackBoxStepEnum.MemoryPressure, BlackBoxStepEnum.VesselDeleted };
        }

        private static SiteRow M(BlackBoxStepEnum step, BlackBoxPhaseEnum phase, string namedIn, string capturedIn, MissionFailureKindEnum? kind, string before, string after)
        {
            return new SiteRow { Step = step, Phase = phase, NamedIn = namedIn, CapturedIn = capturedIn, Kind = kind, Before = before, After = after, Voyage = false };
        }

        private static SiteRow V(BlackBoxStepEnum step, string file, string before, string after)
        {
            return new SiteRow { Step = step, Phase = BlackBoxPhaseEnum.Voyage, NamedIn = file, CapturedIn = file, Kind = null, Before = before, After = after, Voyage = true };
        }

        private static async Task AssertRowAsync(SiteRow row)
        {
            AssertEqual(row.Phase, BlackBoxTaxonomy.PhaseOf(row.Step), "taxonomy phase of " + row.Step);

            string root = RepoRoot();
            string named = File.ReadAllText(Path.Combine(root, row.NamedIn));
            AssertTrue(named.Contains("BlackBoxStepEnum." + row.Step, StringComparison.Ordinal), row.Step + " is named in " + row.NamedIn);
            string captured = File.ReadAllText(Path.Combine(root, row.CapturedIn));
            AssertTrue(captured.Contains(row.Voyage ? "RecordVoyageFailureAsync(" : "Record", StringComparison.Ordinal)
                && (captured.Contains("RecordMissionFailureAsync(", StringComparison.Ordinal) || captured.Contains("RecordFailureAsync(", StringComparison.Ordinal) || captured.Contains("RecordVoyageFailureAsync(", StringComparison.Ordinal)),
                row.CapturedIn + " calls the capture helper");

            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver);
            string callerFile = Path.Combine(root, row.CapturedIn);
            string? id;
            if (row.Voyage)
            {
                Voyage voyage = await t.Driver.Voyages.CreateAsync(new Voyage("row " + row.Step) { Status = Enum.Parse<VoyageStatusEnum>(row.After) }).ConfigureAwait(false);
                id = await bb.Capture.RecordVoyageFailureAsync(voyage, Enum.Parse<VoyageStatusEnum>(row.Before), row.Step, "row", null, CancellationToken.None, "Site", callerFile, 42).ConfigureAwait(false);
            }
            else
            {
                Mission mission = await t.Driver.Missions.CreateAsync(new Mission("row " + row.Step)
                {
                    Status = Enum.Parse<MissionStatusEnum>(row.After), FailureKind = row.Kind, FailureReason = "row " + row.Step
                }).ConfigureAwait(false);
                id = await bb.Capture.RecordMissionFailureAsync(mission, Enum.Parse<MissionStatusEnum>(row.Before), row.Step, null, CancellationToken.None, "Site", callerFile, 42).ConfigureAwait(false);
            }

            AssertNotNull(id, "captured");
            BlackBoxEntry entry = (await t.Driver.BlackBoxEntries.ReadAsync(id!).ConfigureAwait(false))!;
            AssertEqual(row.Phase, entry.Phase); AssertEqual(row.Step, entry.Step);
            AssertEqual(row.Before, entry.StatusBefore); AssertEqual(row.After, entry.StatusAfter);
            if (!row.Voyage) AssertEqual(row.Kind, entry.FailureKind);
            BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
            AssertEqual(row.CapturedIn, record.Site!.SourceFile, "site file");
            AssertEqual(42, record.Site.SourceLine);
            bool external = row.Step == BlackBoxStepEnum.OperatorStop || row.Phase == BlackBoxPhaseEnum.External || row.Kind == MissionFailureKindEnum.OperatorAction || row.Step == BlackBoxStepEnum.Cancelled;
            AssertEqual(external ? BlackBoxTriageEnum.External : BlackBoxTriageEnum.Unknown, entry.Triage, "triage of " + row.Step);
        }

        private static async Task AssertExitAsync(RuntimeFailureKindEnum kind, int exitCode, int redispatchAttempts, BlackBoxStepEnum expected)
        {
            using TestDatabase t = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
            ArmadaSettings settings = Settings();
            settings.MaxRecoveryAttempts = 0;
            using BlackBoxRuntime bb = BlackBoxTestKit.Runtime(t.Driver, settings);
            Services s = Services.Create(t.Driver, settings, bb.Capture, new StubGitService());
            int pid = 900000 + Random.Shared.Next(1, 90000);
            Captain captain = await t.Driver.Captains.CreateAsync(new Captain("bb-exit-" + Short()) { State = CaptainStateEnum.Working, ProcessId = pid }).ConfigureAwait(false);
            Mission mission = await t.Driver.Missions.CreateAsync(new Mission("exit " + kind) { Status = MissionStatusEnum.InProgress, CaptainId = captain.Id, ProcessId = pid, StartedUtc = DateTime.UtcNow.AddMinutes(-3), RedispatchAttempts = redispatchAttempts }).ConfigureAwait(false);
            captain.CurrentMissionId = mission.Id;
            await t.Driver.Captains.UpdateAsync(captain).ConfigureAwait(false);

            await s.Admiral.HandleProcessExitAsync(pid, new RuntimeExitInfo { ExitCode = exitCode, FailureKind = kind }, captain.Id, mission.Id).ConfigureAwait(false);
            Mission after = (await t.Driver.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
            AssertEqual(MissionStatusEnum.Failed, after.Status, after.FailureReason);
            BlackBoxEntry entry = await CurrentAsync(t.Driver, mission.Id).ConfigureAwait(false);
            AssertEqual(expected, entry.Step);
            AssertEqual(BlackBoxTaxonomy.PhaseOf(expected), entry.Phase);
            AssertEqual("InProgress", entry.StatusBefore); AssertEqual("Failed", entry.StatusAfter);
            AssertEqual(after.FailureKind, entry.FailureKind);
            AssertEqual(captain.Id, entry.CaptainId);
            BlackBoxRecord record = await bb.Pipeline.LoadRecordAsync(entry).ConfigureAwait(false);
            AssertEqual(exitCode, record.Mechanism!.ExitCode);
            AssertEqual(kind, record.Mechanism.RuntimeFailureKind);
            AssertEqual(AdmiralFile, record.Site!.SourceFile);
            AssertEqual("HandleTerminalProcessExitFailureAsync", record.Site.Method);
        }

        private static void AssertEntry(BlackBoxEntry entry, BlackBoxPhaseEnum phase, BlackBoxStepEnum step, MissionFailureKindEnum? kind, string before, string after)
        {
            AssertEqual(phase, entry.Phase, "phase");
            AssertEqual(step, entry.Step, "step");
            AssertEqual(kind, entry.FailureKind, "kind");
            AssertEqual(before, entry.StatusBefore, "status before");
            AssertEqual(after, entry.StatusAfter, "status after");
        }

        private static async Task<BlackBoxEntry> CurrentAsync(DatabaseDriver db, string missionId)
        {
            BlackBoxEntry? entry = await db.BlackBoxEntries.ReadCurrentByMissionAsync(missionId).ConfigureAwait(false);
            AssertNotNull(entry, "mission " + missionId + " has a current black box entry");
            return entry!;
        }

        private static async Task<long> CountAsync(DatabaseDriver db, string missionId)
        {
            EnumerationResult<BlackBoxEntry> page = await db.BlackBoxEntries.EnumerateByMissionAsync(missionId, null, new EnumerationQuery { PageSize = 50 }).ConfigureAwait(false);
            return page.TotalRecords;
        }

        private static async Task<List<ArmadaEvent>> StatusEventsAsync(DatabaseDriver db, string missionId)
        {
            EnumerationResult<ArmadaEvent> page = await db.Events.EnumerateAsync(new EnumerationQuery { MissionId = missionId, EventType = "mission.status_changed", PageSize = 50 }).ConfigureAwait(false);
            return page.Objects;
        }

        private static async Task<Vessel> VesselAsync(DatabaseDriver db)
        {
            return await db.Vessels.CreateAsync(new Vessel("bb-sites-" + Short(), "https://github.com/test/repo.git")
            {
                LocalPath = TestTemp.NewDirectory("bb_sites_bare"),
                WorkingDirectory = TestTemp.NewDirectory("bb_sites_work")
            }).ConfigureAwait(false);
        }

        private static ArmadaSettings Settings()
        {
            ArmadaSettings settings = BlackBoxTestKit.Settings();
            settings.LogDirectory = TestTemp.NewDirectory("bb_sites_logs");
            settings.DocksDirectory = TestTemp.NewDirectory("bb_sites_docks");
            settings.ReposDirectory = TestTemp.NewDirectory("bb_sites_repos");
            return settings;
        }

        private static string Short()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "Armada.sln"))) dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("The repository root (src/Armada.sln) was not found above " + AppContext.BaseDirectory);
            return dir.FullName;
        }

        private static async Task InvokeAsync(object target, string method, params object?[] args)
        {
            await InvokeAsync<object>(target, method, args).ConfigureAwait(false);
        }

        private static async Task<T> InvokeAsync<T>(object target, string method, params object?[] args)
        {
            MethodInfo info = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .First(m => m.Name == method && m.GetParameters().Length == args.Length);
            Task task = (Task)info.Invoke(target, args)!;
            await task.ConfigureAwait(false);
            PropertyInfo? result = task.GetType().GetProperty("Result");
            return result != null && result.PropertyType != typeof(void) && typeof(T) != typeof(object) ? (T)result.GetValue(task)! : default!;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }

        #endregion

        #region Nested-Types

        private sealed class SiteRow
        {
            public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;
            public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;
            public string NamedIn { get; set; } = "";
            public string CapturedIn { get; set; } = "";
            public MissionFailureKindEnum? Kind { get; set; } = null;
            public string Before { get; set; } = "";
            public string After { get; set; } = "";
            public bool Voyage { get; set; } = false;
        }

        private sealed class Services
        {
            public DockService Docks { get; private set; } = null!;
            public CaptainService Captains { get; private set; } = null!;
            public MissionService Missions { get; private set; } = null!;
            public AdmiralService Admiral { get; private set; } = null!;

            public static Services Create(DatabaseDriver db, ArmadaSettings settings, IBlackBoxCapture? capture, IGitService git)
            {
                LoggingModule logging = BlackBoxTestKit.Logging();
                Services s = new Services();
                s.Docks = new DockService(logging, db, settings, git);
                s.Captains = new CaptainService(logging, db, settings, git, s.Docks) { BlackBox = capture };
                s.Missions = new MissionService(logging, db, settings, s.Docks, s.Captains, null, git) { BlackBox = capture };
                VoyageService voyages = new VoyageService(logging, db) { BlackBox = capture };
                s.Admiral = new AdmiralService(logging, db, settings, s.Captains, s.Missions, voyages, s.Docks) { BlackBox = capture };
                return s;
            }
        }

        private sealed class ThrowingCapture : IBlackBoxCapture
        {
            public Task<string?> CaptureMissionFailureAsync(BlackBoxCaptureContext context, CancellationToken token = default) => throw new InvalidOperationException("scripted capture failure");

            public Task<string?> CaptureVoyageFailureAsync(BlackBoxVoyageCaptureContext context, CancellationToken token = default) => throw new InvalidOperationException("scripted capture failure");
        }

        private sealed class StarvedProbe : ISystemResourceProbe
        {
            public long GetAvailablePhysicalBytes() => 1;

            public long GetTotalPhysicalBytes() => 8L * 1024 * 1024 * 1024;
        }

        #endregion
    }
}
