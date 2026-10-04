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
                AssertTrue(sw.Elapsed < TimeSpan.FromSeconds(40), "timed out promptly instead of waiting for sleep 60");
                AssertEqual(FleetActionRunStatusEnum.CompletedWithFailures, done!.Status);
                FleetActionRunTarget target = (await h.TargetsAsync(run.Id).ConfigureAwait(false)).Single();
                AssertEqual(FleetActionTargetStatusEnum.TimedOut, target.Status);
                AssertEqual(FleetActionReasonCodes.Timeout, target.FailureReason);
                AssertNull(target.ExitCode, "no exit code on timeout");
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
                int first = await seeder.SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false);
                AssertEqual(5, first, "five built-ins created");
                AssertEqual(0, await seeder.SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false), "second seed creates nothing");

                FleetAction? prune = await h.Db.Driver.FleetActions.ReadByBuiltInKeyAsync(Constants.DefaultTenantId, FleetActionSeedService.PruneMergedBranchesKey).ConfigureAwait(false);
                AssertNotNull(prune, "prune built-in");
                await h.Service.DeleteActionAsync(h.Admin, prune!.Id).ConfigureAwait(false);

                AssertEqual(0, await new FleetActionSeedService(h.Db.Driver, h.Logging).SeedTenantAsync(Constants.DefaultTenantId).ConfigureAwait(false), "deleted built-in not re-seeded");
                FleetAction? after = await h.Db.Driver.FleetActions.ReadByBuiltInKeyAsync(Constants.DefaultTenantId, FleetActionSeedService.PruneMergedBranchesKey).ConfigureAwait(false);
                AssertFalse(after!.Active, "soft-deleted");

                EnumerationResult<FleetAction> visible = await h.Service.EnumerateActionsAsync(h.Admin, new FleetActionEnumerateRequest { PageSize = 100 }).ConfigureAwait(false);
                AssertEqual(4, visible.Objects.Count(a => a.IsBuiltIn), "four active built-ins listed");
                EnumerationResult<FleetAction> all = await h.Service.EnumerateActionsAsync(h.Admin, new FleetActionEnumerateRequest { PageSize = 100, IncludeInactive = true }).ConfigureAwait(false);
                AssertEqual(5, all.Objects.Count(a => a.IsBuiltIn), "five with inactive");

                foreach (FleetAction builtIn in FleetActionSeedService.BuildDefaults(false).Concat(FleetActionSeedService.BuildDefaults(true)))
                {
                    FleetActionTemplateRenderer.Validate(builtIn.CommandText);
                    FleetActionTemplateRenderer.Validate(builtIn.PromptTemplate);
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

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Fleet Action Runner",
                cases: cases);
        }

        #endregion

        #region Private-Methods

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
