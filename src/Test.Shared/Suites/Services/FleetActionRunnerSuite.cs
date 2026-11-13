namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="FleetActionRunner"/>, <see cref="FleetActionService"/> and
    /// <see cref="FleetActionSeedService"/> against a live database and real shell processes: Command success,
    /// non-zero exit, timeout, cancellation that kills the in-flight process, dirty-tree and missing-directory
    /// skips, output truncation, the global concurrency cap, restart recovery, Mission fan-out pacing with a stub
    /// dispatcher, dispatch rejection, Mission cancel, seeding idempotency, and service-level authorization and
    /// tenancy checks.
    /// </summary>
    public sealed class FleetActionRunnerSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.FleetActionRunner";
        private const int RunWaitMs = 60000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("command_success", "A successful command captures exit code and output", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateRepoVesselAsync("ok").ConfigureAwait(false);
                FleetActionRun run = await h.StartCommandAsync("echo hello-{{vessel.defaultBranch}}", new List<string> { vessel.Id }).ConfigureAwait(false);

                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertNotNull(done, "finished run");
                AssertEqual(FleetActionRunStatusEnum.Completed, done!.Status);
                AssertEqual(1, done.SucceededCount);
                AssertNotNull(done.StartedUtc, "StartedUtc");
                AssertNotNull(done.CompletedUtc, "CompletedUtc");

                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Succeeded, target.Status);
                AssertEqual(0, target.ExitCode!.Value);
                AssertContains("hello-main", target.OutputText ?? "");
                AssertEqual("echo hello-main", target.RenderedText);
                AssertNotNull(target.DurationMs, "DurationMs");
                AssertFalse(target.OutputTruncated, "not truncated");
            }));

            cases.Add(CaseAsync("command_nonzero_exit", "A non-zero exit fails the target with NonZeroExit", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateRepoVesselAsync("fail").ConfigureAwait(false);
                FleetActionRun run = await h.StartCommandAsync("exit 3", new List<string> { vessel.Id }).ConfigureAwait(false);

                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertNotNull(done, "finished run");
                AssertEqual(FleetActionRunStatusEnum.CompletedWithFailures, done!.Status);
                AssertEqual(1, done.FailedCount);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Failed, target.Status);
                AssertEqual(FleetActionReasonCodes.NonZeroExit, target.FailureReason);
                AssertEqual(3, target.ExitCode!.Value);
            }));

            cases.Add(CaseAsync("command_timeout", "A command exceeding the timeout is killed and marked TimedOut", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateRepoVesselAsync("slow").ConfigureAwait(false);
                Stopwatch sw = Stopwatch.StartNew();
                FleetActionRun run = await h.StartCommandAsync("sleep 60", new List<string> { vessel.Id }, timeoutSeconds: 5).ConfigureAwait(false);

                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertNotNull(done, "finished run");
                AssertTrue(sw.Elapsed < TimeSpan.FromSeconds(40), "timed out promptly instead of waiting for sleep 60");
                AssertEqual(FleetActionRunStatusEnum.CompletedWithFailures, done!.Status);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.TimedOut, target.Status);
                AssertEqual(FleetActionReasonCodes.Timeout, target.FailureReason);
                AssertNull(target.ExitCode, "no exit code on timeout");
            }));

            cases.Add(CaseAsync("wait_survives_wall_clock_jump", "WaitForRunAsync waits on a monotonic clock: a wall-clock jump (the host sleeping and waking) does not end the wait early with null", TestTags.Reliability, async () =>
            {
                // The flaky "command exceeding the timeout" case failed with a NullReferenceException (done! was null)
                // when the Mac slept during the 5 s command: the wall clock jumped past WaitForRunAsync's DateTime
                // deadline while the Stopwatch-measured test had run only ~6 s. Here every reading of the runner's wall
                // clock is 10 minutes after the previous one, so any wall-clock deadline is always already past.
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                DateTime origin = DateTime.UtcNow;
                long readings = 0;
                h.Runner.UtcNow = () => origin.AddMinutes(10 * Interlocked.Increment(ref readings));

                Vessel vessel = await h.CreateRepoVesselAsync("jump").ConfigureAwait(false);
                FleetActionRun run = await h.StartCommandAsync("sleep 60", new List<string> { vessel.Id }, timeoutSeconds: 1).ConfigureAwait(false);

                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertNotNull(done, "the wait returned the finished run instead of giving up on the jumped wall clock");
                AssertEqual(FleetActionRunStatusEnum.CompletedWithFailures, done!.Status);
                AssertTrue(Interlocked.Read(ref readings) > 0, "the runner read its wall clock");
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.TimedOut, target.Status);
                AssertEqual(FleetActionReasonCodes.Timeout, target.FailureReason);
            }));

            cases.Add(CaseAsync("wait_times_out_on_monotonic_clock", "WaitForRunAsync still gives up after its timeout for a run that does not finish", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateRepoVesselAsync("unfinished").ConfigureAwait(false);
                FleetActionRun run = await h.StartCommandAsync("sleep 60", new List<string> { vessel.Id }, timeoutSeconds: 120).ConfigureAwait(false);

                Stopwatch sw = Stopwatch.StartNew();
                FleetActionRun? notYet = await h.Runner.WaitForRunAsync(run.Id, 300).ConfigureAwait(false);
                AssertNull(notYet, "a running run is not returned");
                AssertTrue(sw.ElapsedMilliseconds >= 300, "waited the full timeout");

                await h.Service.CancelRunAsync(h.Admin, run.Id).ConfigureAwait(false);
                AssertNotNull(await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false), "cancelled run settled");
            }));

            cases.Add(CaseAsync("cancel_kills_and_cancels_pending", "Cancel mid-run kills the in-flight process and cancels pending targets", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                List<Vessel> vessels = new List<Vessel>();
                for (int i = 0; i < 3; i++) vessels.Add(await h.CreateRepoVesselAsync("cancel" + i).ConfigureAwait(false));

                FleetActionRun run = await h.StartCommandAsync("sleep 4; echo done > marker.txt", vessels.Select(v => v.Id).ToList(), concurrency: 1, requiresClean: false).ConfigureAwait(false);

                FleetActionRunTarget? running = null;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(30));
                while (running == null && !deadline.Passed)
                {
                    running = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).FirstOrDefault(t => t.Status == FleetActionTargetStatusEnum.Running);
                    if (running == null) await Task.Delay(50).ConfigureAwait(false);
                }
                AssertNotNull(running, "a target started");
                await Task.Delay(700).ConfigureAwait(false);

                FleetActionRun cancelled = await h.Service.CancelRunAsync(h.Admin, run.Id).ConfigureAwait(false);
                AssertEqual(FleetActionRunStatusEnum.Cancelled, cancelled.Status);

                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertNotNull(done, "run settled");
                AssertEqual(FleetActionRunStatusEnum.Cancelled, done!.Status);
                AssertEqual(3, done.CancelledCount);
                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                AssertTrue(targets.All(t => t.Status == FleetActionTargetStatusEnum.Cancelled), "every target cancelled");

                // The killed process must never reach its echo.
                await Task.Delay(5000).ConfigureAwait(false);
                Vessel inFlight = vessels.Single(v => v.Id == running!.VesselId);
                AssertFalse(File.Exists(Path.Combine(inFlight.WorkingDirectory!, "marker.txt")), "in-flight process was killed before writing its marker");

                await AssertThrowsAsync<InvalidOperationException>(async () =>
                {
                    FleetActionRun finished = await h.StartCommandAsync("echo x", new List<string> { vessels[0].Id }, requiresClean: false).ConfigureAwait(false);
                    await h.Runner.WaitForRunAsync(finished.Id, RunWaitMs).ConfigureAwait(false);
                    await h.Service.CancelRunAsync(h.Admin, finished.Id).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("dirty_tree_skipped", "A dirty working tree is skipped with DirtyTree", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateRepoVesselAsync("dirty").ConfigureAwait(false);
                File.WriteAllText(Path.Combine(vessel.WorkingDirectory!, "untracked.txt"), "x");

                FleetActionRun run = await h.StartCommandAsync("echo should-not-run", new List<string> { vessel.Id }).ConfigureAwait(false);
                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertEqual(FleetActionRunStatusEnum.Completed, done!.Status);
                AssertEqual(1, done.SkippedCount);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Skipped, target.Status);
                AssertEqual(FleetActionReasonCodes.DirtyTree, target.SkipReason);
                AssertFalse((target.OutputText ?? "").Contains("should-not-run"), "command did not run");
            }));

            cases.Add(CaseAsync("no_working_directory_skipped", "Missing or nonexistent working directories are skipped", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel none = await h.CreateVesselAsync("nowd", null).ConfigureAwait(false);
                Vessel missing = await h.CreateVesselAsync("gone", Path.Combine(Path.GetTempPath(), "armada-missing-" + Guid.NewGuid().ToString("N"))).ConfigureAwait(false);

                FleetActionRun run = await h.StartCommandAsync("echo x", new List<string> { none.Id, missing.Id }).ConfigureAwait(false);
                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertEqual(2, done!.SkippedCount);
                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                AssertTrue(targets.All(t => t.SkipReason == FleetActionReasonCodes.NoWorkingDirectory), "NoWorkingDirectory on both");
            }));

            cases.Add(CaseAsync("build_command_variable", "{{vessel.buildCommand}} renders, and is skipped with NoBuildCommand when empty", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel withBuild = await h.CreateRepoVesselAsync("build").ConfigureAwait(false);
                withBuild.DefinitionOfDoneBuildCommand = "echo built-ok";
                await h.Db.Driver.Vessels.UpdateAsync(withBuild).ConfigureAwait(false);
                Vessel without = await h.CreateRepoVesselAsync("nobuild").ConfigureAwait(false);

                FleetActionRun run = await h.StartCommandAsync("{{vessel.buildCommand}}", new List<string> { withBuild.Id, without.Id }, requiresClean: false).ConfigureAwait(false);
                await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                FleetActionRunTarget built = targets.Single(t => t.VesselId == withBuild.Id);
                FleetActionRunTarget skipped = targets.Single(t => t.VesselId == without.Id);
                AssertEqual(FleetActionTargetStatusEnum.Succeeded, built.Status);
                AssertContains("built-ok", built.OutputText ?? "");
                AssertEqual(FleetActionTargetStatusEnum.Skipped, skipped.Status);
                AssertEqual(FleetActionReasonCodes.NoBuildCommand, skipped.SkipReason);
            }));

            cases.Add(CaseAsync("output_truncated", "Output beyond MaxOutputBytes is truncated and flagged", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.FleetActions.MaxOutputBytes = 1024;
                Vessel vessel = await h.CreateRepoVesselAsync("big").ConfigureAwait(false);
                string payload = new string('A', 3000) + "END";

                FleetActionRun run = await h.StartCommandAsync("echo " + payload, new List<string> { vessel.Id }).ConfigureAwait(false);
                await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Succeeded, target.Status);
                AssertTrue(target.OutputTruncated, "OutputTruncated");
                AssertTrue(Encoding.UTF8.GetByteCount(target.OutputText ?? "") <= 1024, "output within limit");
                AssertContains("END", target.OutputText ?? "");
            }));

            cases.Add(Case("truncate_tail_never_splits", "TruncateTail keeps the tail without splitting characters", TestTags.Positive, () =>
            {
                string text = "abc" + new string('\u00e9', 10);
                string? result = FleetActionRunner.TruncateTail(text, 5, out bool truncated);
                AssertTrue(truncated, "truncated");
                AssertEqual(new string('\u00e9', 2), result);
                string? same = FleetActionRunner.TruncateTail("short", 100, out bool notTruncated);
                AssertFalse(notTruncated, "not truncated");
                AssertEqual("short", same);
            }));

            cases.Add(CaseAsync("global_cap_respected", "The global MaxConcurrency cap limits in-flight commands across a run", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.FleetActions.MaxConcurrency = 1;
                List<Vessel> vessels = new List<Vessel>();
                for (int i = 0; i < 3; i++) vessels.Add(await h.CreateRepoVesselAsync("cap" + i).ConfigureAwait(false));

                FleetActionRun run = await h.StartCommandAsync("sleep 1", vessels.Select(v => v.Id).ToList(), concurrency: 4, requiresClean: false).ConfigureAwait(false);
                int maxInFlight = 0;
                MonotonicDeadline deadline = MonotonicDeadline.After(TimeSpan.FromSeconds(45));
                while (!deadline.Passed)
                {
                    maxInFlight = Math.Max(maxInFlight, h.Runner.GlobalInFlight);
                    FleetActionRun? current = await h.Db.Driver.FleetActionRuns.ReadAsync(run.Id).ConfigureAwait(false);
                    if (current != null && FleetActionRunner.IsTerminal(current.Status) && current.CompletedUtc.HasValue) break;
                    await Task.Delay(20).ConfigureAwait(false);
                }

                AssertEqual(1, maxInFlight, "peak in-flight commands");
                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertEqual(3, done!.SucceededCount);
            }));

            cases.Add(CaseAsync("restart_recovery", "Restart recovery fails Running command targets as Interrupted and resumes pending ones", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel a = await h.CreateRepoVesselAsync("rec-a").ConfigureAwait(false);
                Vessel b = await h.CreateRepoVesselAsync("rec-b").ConfigureAwait(false);

                FleetActionRun run = await h.Db.Driver.FleetActionRuns.CreateAsync(new FleetActionRun
                {
                    TenantId = Constants.DefaultTenantId,
                    ActionName = "recovering",
                    Kind = FleetActionKindEnum.Command,
                    CommandText = "echo resumed",
                    RequiresCleanWorkingTree = false,
                    Status = FleetActionRunStatusEnum.Running,
                    StartedUtc = DateTime.UtcNow,
                    TargetCount = 2
                }).ConfigureAwait(false);
                await h.Db.Driver.FleetActionRunTargets.CreateAsync(new FleetActionRunTarget
                {
                    TenantId = Constants.DefaultTenantId, RunId = run.Id, VesselId = a.Id, VesselName = a.Name,
                    Status = FleetActionTargetStatusEnum.Running, StartedUtc = DateTime.UtcNow
                }).ConfigureAwait(false);
                await h.Db.Driver.FleetActionRunTargets.CreateAsync(new FleetActionRunTarget
                {
                    TenantId = Constants.DefaultTenantId, RunId = run.Id, VesselId = b.Id, VesselName = b.Name,
                    Status = FleetActionTargetStatusEnum.Pending
                }).ConfigureAwait(false);

                await h.RestartRunnerAsync().ConfigureAwait(false);
                FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                AssertNotNull(done, "run finished after recovery");
                AssertEqual(FleetActionRunStatusEnum.CompletedWithFailures, done!.Status);

                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                FleetActionRunTarget interrupted = targets.Single(t => t.VesselId == a.Id);
                FleetActionRunTarget resumed = targets.Single(t => t.VesselId == b.Id);
                AssertEqual(FleetActionTargetStatusEnum.Failed, interrupted.Status);
                AssertEqual(FleetActionReasonCodes.Interrupted, interrupted.FailureReason);
                AssertEqual(FleetActionTargetStatusEnum.Succeeded, resumed.Status);
                AssertContains("resumed", resumed.OutputText ?? "");
            }));

            cases.Add(CaseAsync("mission_run_approval_snapshot", "Fleet-action run persists and forwards approval choices without changing the action definition", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateVesselAsync("approve-run", null).ConfigureAwait(false);
                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest {
                    VesselIds = new List<string> { vessel.Id },
                    Definition = new FleetActionUpsertRequest { Name = "Timed mission", Kind = FleetActionKindEnum.Mission, PromptTemplate = "Do work" },
                    Overrides = new FleetActionRunOverrides { ApprovalOptions = new RunApprovalOptions { ApproveAll = true, TimeoutSeconds = 55, TimeoutDecision = ApprovalTimeoutDecisionEnum.Approve } }
                }).ConfigureAwait(false);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                FleetActionRun persisted = (await h.Db.Driver.FleetActionRuns.ReadAsync(run.Id).ConfigureAwait(false))!;
                AssertTrue(persisted.ApprovalOptions!.ApproveAll);
                AssertEqual(55, h.Dispatcher.ApprovalsByVessel[vessel.Id]!.TimeoutSeconds);
                AssertEqual(ApprovalTimeoutDecisionEnum.Approve, h.Dispatcher.ApprovalsByVessel[vessel.Id]!.TimeoutDecision);
                foreach (string voyageId in h.Dispatcher.RunningVoyages()) h.Dispatcher.SetOutcome(voyageId, FleetActionVoyageOutcomeEnum.Succeeded);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("mission_fanout_paced", "Mission fan-out never exceeds run concurrency and follows voyages to completion", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                List<Vessel> vessels = new List<Vessel>();
                for (int i = 0; i < 5; i++) vessels.Add(await h.CreateVesselAsync("m" + i, null).ConfigureAwait(false));

                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = vessels.Select(v => v.Id).ToList(),
                    Concurrency = 2,
                    Definition = new FleetActionUpsertRequest { Name = "fix", Kind = FleetActionKindEnum.Mission, PromptTemplate = "Fix {{vessel.name}}.\n{{health.summary}}" }
                }).ConfigureAwait(false);

                int rounds = 0;
                while (rounds < 50)
                {
                    rounds++;
                    await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                    AssertTrue(h.Dispatcher.ActiveCount <= 2, "never more than 2 active voyages");
                    List<string> running = h.Dispatcher.RunningVoyages();
                    if (running.Count == 0) break;
                    h.Dispatcher.SetOutcome(running[0], FleetActionVoyageOutcomeEnum.Succeeded);
                }

                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                AssertEqual(2, h.Dispatcher.MaxActive, "peak active voyages");
                AssertEqual(5, h.Dispatcher.Dispatched.Count, "one voyage per vessel");

                FleetActionRun? done = await h.Db.Driver.FleetActionRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertEqual(FleetActionRunStatusEnum.Completed, done!.Status);
                AssertEqual(5, done.SucceededCount);
                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                AssertTrue(targets.All(t => !String.IsNullOrEmpty(t.VoyageId)), "VoyageId stored");
                string prompt = h.Dispatcher.PromptsByVessel[vessels[0].Id];
                AssertContains("Fix " + vessels[0].Name + ".", prompt);
                AssertContains(FleetActionHealthSummaryBuilder.NoHealthDataText, prompt);
            }));

            cases.Add(CaseAsync("mission_persona_passed_to_dispatch", "A Mission action's Persona reaches the dispatcher", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateVesselAsync("persona", null).ConfigureAwait(false);

                await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { vessel.Id },
                    Definition = new FleetActionUpsertRequest { Name = "review", Kind = FleetActionKindEnum.Mission, PromptTemplate = "review {{vessel.name}}", Persona = "Judge" }
                }).ConfigureAwait(false);

                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                AssertTrue(h.Dispatcher.PersonasByVessel.ContainsKey(vessel.Id), "expected a dispatch");
                AssertEqual("Judge", h.Dispatcher.PersonasByVessel[vessel.Id], "the action's persona must be applied at dispatch");
            }));

            cases.Add(CaseAsync("mission_dispatch_rejected_and_failed", "Rejected dispatch is Skipped/DispatchRejected; a failed voyage fails the target", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel rejected = await h.CreateVesselAsync("rej", null).ConfigureAwait(false);
                Vessel failing = await h.CreateVesselAsync("failv", null).ConfigureAwait(false);
                h.Dispatcher.RejectVesselIds.Add(rejected.Id);

                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { rejected.Id, failing.Id },
                    Definition = new FleetActionUpsertRequest { Name = "m", Kind = FleetActionKindEnum.Mission, PromptTemplate = "do it" }
                }).ConfigureAwait(false);

                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                foreach (string voyageId in h.Dispatcher.RunningVoyages()) h.Dispatcher.SetOutcome(voyageId, FleetActionVoyageOutcomeEnum.Failed);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);

                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                FleetActionRunTarget r = targets.Single(t => t.VesselId == rejected.Id);
                FleetActionRunTarget f = targets.Single(t => t.VesselId == failing.Id);
                AssertEqual(FleetActionTargetStatusEnum.Skipped, r.Status);
                AssertEqual(FleetActionReasonCodes.DispatchRejected, r.SkipReason);
                AssertEqual(h.Dispatcher.RejectionMessage, r.ErrorText);
                AssertEqual(FleetActionTargetStatusEnum.Failed, f.Status);
                AssertEqual(FleetActionReasonCodes.VoyageFailed, f.FailureReason);

                FleetActionRun? done = await h.Db.Driver.FleetActionRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertEqual(FleetActionRunStatusEnum.CompletedWithFailures, done!.Status);
            }));

            cases.Add(CaseAsync("mission_cancel", "Cancelling a Mission run cancels unlanded voyages and pending targets", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                List<Vessel> vessels = new List<Vessel>();
                for (int i = 0; i < 3; i++) vessels.Add(await h.CreateVesselAsync("mc" + i, null).ConfigureAwait(false));

                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = vessels.Select(v => v.Id).ToList(),
                    Concurrency = 1,
                    Definition = new FleetActionUpsertRequest { Name = "m", Kind = FleetActionKindEnum.Mission, PromptTemplate = "do it" }
                }).ConfigureAwait(false);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                AssertEqual(1, h.Dispatcher.ActiveCount, "one voyage active");

                FleetActionRun cancelled = await h.Service.CancelRunAsync(h.Admin, run.Id).ConfigureAwait(false);
                AssertEqual(FleetActionRunStatusEnum.Cancelled, cancelled.Status);
                AssertNotNull(cancelled.CompletedUtc, "CompletedUtc");
                AssertEqual(3, cancelled.CancelledCount);
                AssertEqual(1, h.Dispatcher.CancelledVoyages.Count, "voyage cancelled through the dispatcher");

                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                AssertEqual(1, h.Dispatcher.Dispatched.Count, "nothing dispatched after cancel");
            }));

            cases.Add(CaseAsync("mission_cancel_survives_dispatcher_failure", "Cancelling a Mission run finishes every target even when a voyage cancel fails", TestTags.Reliability, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                List<Vessel> vessels = new List<Vessel>();
                for (int i = 0; i < 3; i++) vessels.Add(await h.CreateVesselAsync("mcf" + i, null).ConfigureAwait(false));

                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = vessels.Select(v => v.Id).ToList(),
                    Concurrency = 2,
                    Definition = new FleetActionUpsertRequest { Name = "m", Kind = FleetActionKindEnum.Mission, PromptTemplate = "do it" }
                }).ConfigureAwait(false);
                await h.Runner.SyncMissionRunsAsync().ConfigureAwait(false);
                AssertEqual(2, h.Dispatcher.ActiveCount, "two voyages active");

                h.Dispatcher.ThrowOnCancel = true;
                FleetActionRun cancelled = await h.Service.CancelRunAsync(h.Admin, run.Id).ConfigureAwait(false);
                AssertEqual(FleetActionRunStatusEnum.Cancelled, cancelled.Status);
                AssertNotNull(cancelled.CompletedUtc, "run finished");
                List<FleetActionRunTarget> targets = await h.TargetsAsync(run.Id).ConfigureAwait(false);
                AssertFalse(targets.Exists(t => t.Status == FleetActionTargetStatusEnum.Running || t.Status == FleetActionTargetStatusEnum.Pending), "no target left unfinished");
                AssertEqual(2, targets.Count(t => (t.ErrorText ?? "").Contains("Cancel the voyage directly")), "both running targets explain the failed voyage cancel");

                FleetActionRun again = await h.Service.CancelRunAsync(h.Admin, run.Id).ConfigureAwait(false);
                AssertEqual(FleetActionRunStatusEnum.Cancelled, again.Status, "cancelling again is a no-op");
            }));

            cases.Add(CaseAsync("seeding_idempotent", "Built-ins seed once per tenant and a soft-deleted built-in is not re-seeded", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                FleetActionSeedService seeder = new FleetActionSeedService(h.Db.Driver, h.Logging);
                int builtIns = FleetActionSeedService.BuildDefaults(OperatingSystem.IsWindows()).Count;
                int first = await seeder.SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertEqual(builtIns, first, "every built-in created");
                AssertEqual(0, await seeder.SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false), "second seed creates nothing");

                FleetAction? prune = await h.Db.Driver.FleetActions.ReadByBuiltInKeyAsync(Constants.DefaultTenantId, FleetActionSeedService.PruneMergedBranchesKey).ConfigureAwait(false);
                AssertNotNull(prune, "prune built-in");
                await h.Service.DeleteActionAsync(h.Admin, prune!.Id).ConfigureAwait(false);

                AssertEqual(0, await new FleetActionSeedService(h.Db.Driver, h.Logging).SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false), "deleted built-in not re-seeded");
                FleetAction? after = await h.Db.Driver.FleetActions.ReadByBuiltInKeyAsync(Constants.DefaultTenantId, FleetActionSeedService.PruneMergedBranchesKey).ConfigureAwait(false);
                AssertFalse(after!.Active, "soft-deleted");

                EnumerationResult<FleetAction> visible = await h.Service.EnumerateActionsAsync(h.Admin, new FleetActionEnumerateRequest { PageSize = 100 }).ConfigureAwait(false);
                AssertEqual(builtIns - 1, visible.Objects.Count(a => a.IsBuiltIn), "active built-ins listed");
                EnumerationResult<FleetAction> all = await h.Service.EnumerateActionsAsync(h.Admin, new FleetActionEnumerateRequest { PageSize = 100, IncludeInactive = true }).ConfigureAwait(false);
                AssertEqual(builtIns, all.Objects.Count(a => a.IsBuiltIn), "all with inactive");

                foreach (FleetAction builtIn in FleetActionSeedService.BuildDefaults(false).Concat(FleetActionSeedService.BuildDefaults(true)))
                {
                    List<string> inputNames = builtIn.Inputs.Select(i => i.Name).ToList();
                    FleetActionTemplateRenderer.Validate(builtIn.CommandText, inputNames);
                    FleetActionTemplateRenderer.Validate(builtIn.PromptTemplate, inputNames);
                }
            }));

            cases.Add(CaseAsync("prune_merged_builtin_safe", "The prune built-in deletes merged branches but never the current or default branch", TestTags.Positive, async () =>
            {
                if (OperatingSystem.IsWindows()) return;
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                Vessel vessel = await h.CreateRepoVesselAsync("prune").ConfigureAwait(false);
                string wd = vessel.WorkingDirectory!;
                await GitAsync(wd, "branch", "merged-feature").ConfigureAwait(false);
                await GitAsync(wd, "checkout", "-b", "current-work").ConfigureAwait(false);

                FleetAction prune = FleetActionSeedService.BuildDefaults(false).Single(a => a.BuiltInKey == FleetActionSeedService.PruneMergedBranchesKey);
                FleetActionRun run = await h.StartCommandAsync(prune.CommandText!, new List<string> { vessel.Id }).ConfigureAwait(false);
                await h.Runner.WaitForRunAsync(run.Id, RunWaitMs).ConfigureAwait(false);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Succeeded, target.Status, "prune target: " + target.ErrorText);

                string branches = await GitAsync(wd, "branch", "--format=%(refname:short)").ConfigureAwait(false);
                AssertFalse(branches.Contains("merged-feature"), "merged branch deleted");
                AssertContains("current-work", branches);
                AssertContains("main", branches);
            }));

            cases.Add(CaseAsync("service_authorization_and_tenancy", "Non-admins cannot create or run Command actions; cross-tenant vessels reject the run", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => h.Service.CreateActionAsync(h.Regular, new FleetActionUpsertRequest
                {
                    Name = "pull", Kind = FleetActionKindEnum.Command, CommandText = "git pull"
                })).ConfigureAwait(false);

                FleetAction mission = await h.Service.CreateActionAsync(h.Regular, new FleetActionUpsertRequest
                {
                    Name = "ask", Kind = FleetActionKindEnum.Mission, PromptTemplate = "Look at {{vessel.name}}"
                }).ConfigureAwait(false);
                AssertEqual(FleetActionKindEnum.Mission, mission.Kind);
                AssertFalse(mission.RequiresCleanWorkingTree, "Mission default clean-tree flag is false");

                await AssertThrowsAsync<FleetActionTemplateException>(() => h.Service.CreateActionAsync(h.Admin, new FleetActionUpsertRequest
                {
                    Name = "bad", Kind = FleetActionKindEnum.Command, CommandText = "echo {{vessel.secret}}"
                })).ConfigureAwait(false);

                Vessel local = await h.CreateVesselAsync("local", null).ConfigureAwait(false);
                await AssertThrowsAsync<UnauthorizedAccessException>(() => h.Service.StartRunAsync(h.Regular, null, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { local.Id },
                    Definition = new FleetActionUpsertRequest { Name = "x", Kind = FleetActionKindEnum.Command, CommandText = "echo x" }
                })).ConfigureAwait(false);

                TenantMetadata other = await h.Db.Driver.Tenants.CreateAsync(new TenantMetadata { Name = "fa-other-" + Guid.NewGuid().ToString("N").Substring(0, 6) }).ConfigureAwait(false);
                Vessel foreign = await h.CreateVesselAsync("foreign", null, other.Id).ConfigureAwait(false);
                await AssertThrowsAsync<KeyNotFoundException>(() => h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest
                {
                    VesselIds = new List<string> { local.Id, foreign.Id },
                    Definition = new FleetActionUpsertRequest { Name = "x", Kind = FleetActionKindEnum.Command, CommandText = "echo x" }
                })).ConfigureAwait(false);

                EnumerationResult<FleetActionRun> runs = await h.Service.EnumerateRunsAsync(h.Admin, new EnumerationQuery()).ConfigureAwait(false);
                AssertEqual(0L, runs.TotalRecords, "no run created for a rejected request");

                AuthContext otherAdmin = AuthContext.Authenticated(other.Id, "usr_other", false, true, "Test");
                await AssertThrowsAsync<KeyNotFoundException>(() => h.Service.ReadActionAsync(otherAdmin, mission.Id)).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("prune_expired_runs", "Retention pruning deletes old finished runs only", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.FleetActions.RunRetentionDays = 1;
                FleetActionRun old = await h.Db.Driver.FleetActionRuns.CreateAsync(new FleetActionRun
                {
                    TenantId = Constants.DefaultTenantId, ActionName = "old", Status = FleetActionRunStatusEnum.Completed,
                    CreatedUtc = DateTime.UtcNow.AddDays(-5), CompletedUtc = DateTime.UtcNow.AddDays(-5)
                }).ConfigureAwait(false);
                FleetActionRun oldUnfinished = await h.Db.Driver.FleetActionRuns.CreateAsync(new FleetActionRun
                {
                    TenantId = Constants.DefaultTenantId, ActionName = "old-pending", Kind = FleetActionKindEnum.Mission, Status = FleetActionRunStatusEnum.Running,
                    CreatedUtc = DateTime.UtcNow.AddDays(-5)
                }).ConfigureAwait(false);
                FleetActionRun recent = await h.Db.Driver.FleetActionRuns.CreateAsync(new FleetActionRun
                {
                    TenantId = Constants.DefaultTenantId, ActionName = "new", Status = FleetActionRunStatusEnum.Completed, CompletedUtc = DateTime.UtcNow
                }).ConfigureAwait(false);

                int deleted = await h.Runner.PruneExpiredRunsAsync().ConfigureAwait(false);
                AssertEqual(1, deleted);
                AssertNull(await h.Db.Driver.FleetActionRuns.ReadAsync(old.Id).ConfigureAwait(false), "old run pruned");
                AssertNotNull(await h.Db.Driver.FleetActionRuns.ReadAsync(oldUnfinished.Id).ConfigureAwait(false), "unfinished run kept");
                AssertNotNull(await h.Db.Driver.FleetActionRuns.ReadAsync(recent.Id).ConfigureAwait(false), "recent run kept");
            }));

            cases.Add(CaseAsync("parser_findings_and_exit_codes", "Parsed findings override non-zero exit, malformed output does not", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync();
                Vessel vessel = await h.CreateRepoVesselAsync("scan");
                foreach (var sample in new[] { ("[{\"RuleID\":\"test\",\"Secret\":\"secret-must-not-leak\"}]", FleetActionTargetStatusEnum.Findings), ("[{", FleetActionTargetStatusEnum.Failed), ("[]", FleetActionTargetStatusEnum.Failed) })
                {
                    FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest {
                        VesselIds = new() { vessel.Id }, Definition = new() { Name = "scan", CommandText = "printf '%s' '" + sample.Item1 + "'; printf '%s' 'secret-must-not-leak' >&2; exit 1", OutputParser = "gitleaks-json", RequiresCleanWorkingTree = false }
                    });
                    FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs);
                    FleetActionRunTarget target = (await h.TargetsAsync(run.Id)).Single();
                    AssertEqual(sample.Item2, target.Status);
                    AssertEqual(sample.Item2 == FleetActionTargetStatusEnum.Findings ? 1 : 0, done!.FindingsCount);
                    AssertEqual(sample.Item2 == FleetActionTargetStatusEnum.Failed ? 1 : 0, done.FailedCount);
                    AssertEqual(0, done.SucceededCount);
                    AssertFalse((target.OutputText + target.ErrorText + target.ScanSummary).Contains("secret-must-not-leak"), "raw output and diagnostics redacted");
                }
            }));

            cases.Add(CaseAsync("preflight_and_offline", "Missing tools and offline network actions skip before execution", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync();
                Vessel vessel = await h.CreateRepoVesselAsync("preflight");
                foreach (bool offline in new[] { false, true })
                {
                    h.Settings.FleetActions.Offline = offline;
                    FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new FleetActionRunRequest {
                        VesselIds = new() { vessel.Id }, Definition = new() { Name = "probe", CommandText = "exit 7", RequiredTools = new() { "armada-tool-that-does-not-exist-987654" }, RequiresNetwork = offline, RequiresCleanWorkingTree = false }
                    });
                    await h.Runner.WaitForRunAsync(run.Id, RunWaitMs);
                    FleetActionRunTarget target = (await h.TargetsAsync(run.Id)).Single();
                    AssertEqual(FleetActionTargetStatusEnum.Skipped, target.Status);
                    AssertEqual(offline ? "NetworkUnavailable" : "ToolMissing:armada-tool-that-does-not-exist-987654", target.SkipReason);
                    AssertNull(target.ExitCode, "command never executed");
                }
            }));

            cases.Add(CaseAsync("runtime_inputs_and_snapshot", "Inputs validate before writing runs and retain defaults and overrides", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync();
                Vessel vessel = await h.CreateRepoVesselAsync("inputs");
                FleetAction action = await h.Service.CreateActionAsync(h.Admin, new() { Name = "inputs", CommandText = "printf '%s' '{{input.version}} {{input.mode}}'", Inputs = new() {
                    new() { Name = "version", Type = "semver", Required = true }, new() { Name = "mode", Type = "enum", Default = "safe", AllowedValues = new() { "safe", "fast" } }
                }, RequiresCleanWorkingTree = false });
                foreach (var values in new[] { new Dictionary<string, string>(), new() { ["version"] = "bad" }, new() { ["version"] = "1.2.3", ["mode"] = "bad" }, new() { ["version"] = "1.2.3", ["unknown"] = "x" } })
                    await AssertThrowsAsync<ArgumentException>(() => h.Service.StartRunAsync(h.Admin, action.Id, new() { VesselIds = new() { vessel.Id }, InputValues = values }));
                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, action.Id, new() { VesselIds = new() { vessel.Id }, InputValues = new() { ["version"] = "1.2.3-rc.1+build" } });
                await h.Runner.WaitForRunAsync(run.Id, RunWaitMs);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id)).Single();
                AssertEqual(FleetActionTargetStatusEnum.Succeeded, target.Status);
                AssertContains("1.2.3-rc.1+build safe", target.OutputText!);
                FleetActionRun? read = await h.Db.Driver.FleetActionRuns.ReadAsync(run.Id);
                AssertEqual("safe", read!.InputValues["mode"]);
                AssertEqual("safe", read.Inputs.Single(i => i.Name == "mode").Default);
                await h.Service.UpdateActionAsync(h.Admin, action.Id, new() { Inputs = new() {
                    new() { Name = "version", Type = "semver", Required = true }, new() { Name = "mode", Type = "enum", Default = "fast", AllowedValues = new() { "safe", "fast" } }
                } });
                foreach (bool explicitOverride in new[] { false, true })
                {
                    Dictionary<string, string> values = new() { ["version"] = "2.0.0" };
                    if (explicitOverride) values["mode"] = "safe";
                    FleetActionRun next = await h.Service.StartRunAsync(h.Admin, action.Id, new() { VesselIds = new() { vessel.Id }, InputValues = values });
                    await h.Runner.WaitForRunAsync(next.Id, RunWaitMs);
                    AssertEqual(explicitOverride ? "safe" : "fast", next.InputValues["mode"]);
                }
                AssertEqual("safe", (await h.Db.Driver.FleetActionRuns.ReadAsync(run.Id))!.InputValues["mode"]);
                await AssertThrowsAsync<FleetActionTemplateException>(() => h.Service.CreateActionAsync(h.Admin, new() { Name = "bad", CommandText = "{{input.unknown}}" }));
            }));

            cases.Add(CaseAsync("scan_summary_source_and_absence", "Mission templates use the newest source scan for the same vessel or empty text", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync();
                Vessel vessel = await h.CreateRepoVesselAsync("summary");
                FleetAction scan = await h.Service.CreateActionAsync(h.Admin, new() { Name = "scan", CommandText = "printf '%s' '[{\"RuleID\":\"test\"}]'; exit 1", OutputParser = "gitleaks-json", RequiresCleanWorkingTree = false });
                FleetActionRun previous = await h.Service.StartRunAsync(h.Admin, scan.Id, new() { VesselIds = new() { vessel.Id } });
                await h.Runner.WaitForRunAsync(previous.Id, RunWaitMs);
                foreach (string source in new[] { scan.Id, "nonexistent" })
                {
                    FleetActionRun mission = await h.Service.StartRunAsync(h.Regular, null, new() { VesselIds = new() { vessel.Id }, Definition = new() { Name = "fix", Kind = FleetActionKindEnum.Mission, PromptTemplate = "Scan:{{scan.summary}}", ScanSourceActionKey = source } });
                    for (int attempt = 0; attempt < 100 && !h.Dispatcher.PromptsByVessel.ContainsKey(vessel.Id); attempt++) await Task.Delay(20);
                    // Sync dispatch is serialized behind the per-run gate.
                    await h.Runner.SyncMissionRunsAsync();
                    string prompt = (await h.TargetsAsync(mission.Id)).Single().RenderedText!;
                    if (source == scan.Id) AssertContains("Findings: 1 total", prompt); else AssertEqual("Scan:", prompt);
                }
            }));

            cases.Add(CaseAsync("parser_timeout_and_cancel_precedence", "Partial findings cannot override a timeout or cancellation", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync();
                Vessel vessel = await h.CreateRepoVesselAsync("partial");
                foreach (bool cancel in new[] { false, true })
                {
                    FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new() { VesselIds = new() { vessel.Id }, Definition = new() { Name = "slow", CommandText = "printf '%s' '[{\"RuleID\":\"test\",\"Secret\":\"sensitive\"}]'; sleep 60", OutputParser = "gitleaks-json", TimeoutSeconds = 5, RequiresCleanWorkingTree = false } });
                    if (cancel) { await Task.Delay(150); await h.Runner.CancelRunAsync(run.Id); }
                    FleetActionRun? done = await h.Runner.WaitForRunAsync(run.Id, RunWaitMs);
                    AssertEqual(0, done!.FindingsCount);
                    FleetActionRunTarget target = (await h.TargetsAsync(run.Id)).Single();
                    AssertEqual(cancel ? FleetActionTargetStatusEnum.Cancelled : FleetActionTargetStatusEnum.TimedOut, target.Status);
                    AssertFalse((target.OutputText ?? "").Contains("sensitive"), "partial secret redacted");
                }
            }));

            cases.Add(CaseAsync("cancel_and_timeout_during_parsing", "A target cancelled or timed out after its parser starts never records findings", TestTags.Negative, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync();
                Vessel vessel = await h.CreateRepoVesselAsync("parsing");
                foreach (bool cancel in new[] { true, false })
                {
                    BlockingFleetActionParser parser = new();
                    using FleetActionRunner runner = new(h.Db.Driver, h.Settings, h.Logging, h.Dispatcher, outputParserResolver: _ => parser);
                    await runner.StartAsync();
                    FleetActionService service = new(h.Db.Driver, h.Settings, runner, h.Seeder, h.Logging);
                    FleetActionRun run = await service.StartRunAsync(h.Admin, null, new() { VesselIds = new() { vessel.Id }, Definition = new() { Name = "parser budget", CommandText = "printf '%s' '[]'", OutputParser = "gitleaks-json", TimeoutSeconds = 5, RequiresCleanWorkingTree = false } });
                    await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    if (cancel) await runner.CancelRunAsync(run.Id);
                    await parser.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    for (int attempt = 0; attempt < 100; attempt++)
                    {
                        FleetActionRunTarget current = (await h.TargetsAsync(run.Id)).Single();
                        if (current.Status != FleetActionTargetStatusEnum.Running) break;
                        await Task.Delay(20);
                    }
                    FleetActionRun? done = await runner.WaitForRunAsync(run.Id, RunWaitMs);
                    FleetActionRunTarget target = (await h.TargetsAsync(run.Id)).Single();
                    AssertEqual(cancel ? FleetActionTargetStatusEnum.Cancelled : FleetActionTargetStatusEnum.TimedOut, target.Status);
                    AssertEqual(0, done!.FindingsCount); AssertNull(target.ScanSummary, "no completed parser result");
                }
            }));

            cases.Add(CaseAsync("offline_harbor_policy_is_live", "An offline Harbor skips network work even when the Admiral is online", TestTags.Positive, async () =>
            {
                using FleetActionTestHarness h = await FleetActionTestHarness.CreateAsync();
                Vessel vessel = await h.CreateRepoVesselAsync("offline-harbor");
                Harbor harbor = await h.Db.Driver.Harbors.CreateAsync(new Harbor { Name = "offline", TenantId = Constants.DefaultTenantId });
                vessel.PreferredHarborId = harbor.Id;
                await h.Db.Driver.Vessels.UpdateAsync(vessel);
                h.Settings.FleetActions.OfflineHarborIds.Add(harbor.Id);
                FleetActionRun run = await h.Service.StartRunAsync(h.Admin, null, new() { VesselIds = new() { vessel.Id }, Definition = new() { Name = "network", CommandText = "exit 9", RequiresNetwork = true } });
                await h.Runner.WaitForRunAsync(run.Id, RunWaitMs);
                AssertEqual("NetworkUnavailable", (await h.TargetsAsync(run.Id)).Single().SkipReason);
                h.Settings.FleetActions.OfflineHarborIds.Clear();
                FleetActionRun next = await h.Service.StartRunAsync(h.Admin, null, new() { VesselIds = new() { vessel.Id }, Definition = new() { Name = "network", CommandText = "exit 9", RequiresNetwork = true } });
                await h.Runner.WaitForRunAsync(next.Id, RunWaitMs);
                AssertEqual("HarborUnavailable", (await h.TargetsAsync(next.Id)).Single().SkipReason);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Fleet Action Runner",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private sealed class BlockingFleetActionParser : Armada.Core.Services.FleetActionOutputParsers.IFleetActionOutputParser
        {
            public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Armada.Core.Services.FleetActionOutputParsers.FleetActionParseResult Parse(string? output, CancellationToken token = default)
            {
                Entered.TrySetResult(true);
                try { token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); return new() { Parsed = true, Total = 1 }; }
                finally { Stopped.TrySetResult(true); }
            }
        }

        private static async Task<string> GitAsync(string workingDirectory, params string[] args)
        {
            HostCommandResult result = await new LocalHostCommandExecutor().RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = args.ToList()
            }).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new InvalidOperationException("git " + String.Join(" ", args) + " failed: " + result.StandardError);
            return result.StandardOutput;
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

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
