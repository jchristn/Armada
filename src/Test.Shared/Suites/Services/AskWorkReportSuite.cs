namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Ask;
    using Armada.Server.Ask;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Ask Armada reports the outcome of finished work on its own: the deterministic outcome block of the final milestone
    /// (passed mission, failed mission, check run with test results, pull request landing), the result built from typed
    /// rows, and the captain's report turn (runs once, waits for a running turn, skipped when the setting is off, when the
    /// user already posted after completion, and for archived or deleted threads).
    /// </summary>
    public sealed class AskWorkReportSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.AskWorkReport";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("outcome_passed_mission", "The outcome of a passed mission states its vessel, landing, runtime, elapsed time, and the captain's final message", TestTags.Positive, () =>
            {
                AskWorkSnapshot snapshot = VoyageSnapshot(AskTrackedWorkStateEnum.Succeeded, Row("msn_1", "Run Test.Automated", "Complete", "Landed"));
                AskWorkResult result = Result(192000, MissionResult("msn_1", "Run Test.Automated", "Complete", "Landed", 185000, "All 412 tests passed in DocConverter.\n\nNothing else changed."));
                string outcome = AskWorkOutcomeFormatter.FormatOutcome(snapshot, result);
                AssertEqual(
                    "**Outcome** (took 3m 12s)\n"
                    + "- Mission \"Run Test.Automated\" on DocConverter: complete, landed. Took 3m 05s.\n"
                    + "  - Captain's final message: \"All 412 tests passed in DocConverter. Nothing else changed.\"",
                    outcome);

                string milestone = AskWorkOutcomeFormatter.AppendOutcome("Voyage \"Tests\" finished (1 of 1 missions done).", snapshot, result);
                AssertStartsWith("Voyage \"Tests\" finished (1 of 1 missions done).\n\n**Outcome**", milestone, "outcome follows the milestone sentence");

                string longMessage = String.Join(" ", Enumerable.Repeat("word", 200));
                string? excerpt = AskWorkOutcomeFormatter.Excerpt(longMessage, AskWorkOutcomeFormatter.MaxExcerptChars);
                AssertTrue(excerpt!.Length <= AskWorkOutcomeFormatter.MaxExcerptChars + 3, "excerpt is short: " + excerpt.Length);
                AssertTrue(excerpt.EndsWith("word...", StringComparison.Ordinal), "cut at a word boundary: " + excerpt);
                return Task.CompletedTask;
            }));

            cases.Add(Case("outcome_failed_mission", "The outcome of failed work lists the failed mission first with its failure reason", TestTags.Negative, () =>
            {
                AskWorkSnapshot snapshot = VoyageSnapshot(AskTrackedWorkStateEnum.Failed,
                    Row("msn_ok", "Update docs", "Complete", "Landed"),
                    Row("msn_bad", "Fix parser", "Failed", null));
                AskWorkResult result = Result(65000,
                    MissionResult("msn_ok", "Update docs", "Complete", "Landed", 30000, null),
                    MissionResult("msn_bad", "Fix parser", "Failed", null, 61000, "I could not get the parser tests to pass."));
                result.Missions[1].FailureReason = "Captain exited with code 1";
                string outcome = AskWorkOutcomeFormatter.FormatOutcome(snapshot, result);
                string[] lines = outcome.Split('\n');
                AssertEqual("**Outcome** (took 1m 05s)", lines[0]);
                AssertEqual("- Mission \"Fix parser\" on DocConverter: failed: Captain exited with code 1. Took 1m 01s.", lines[1], "failed mission first");
                AssertEqual("  - Captain's final message: \"I could not get the parser tests to pass.\"", lines[2]);
                AssertEqual("- Mission \"Update docs\" on DocConverter: complete, landed. Took 30s.", lines[3]);

                string context = AskTurnCoordinator.BuildReportNote(snapshot, result);
                AssertContains("Voyage \"Tests\" (vyg_1): Failed", context, "the captain gets the state");
                AssertContains("Failure reason: Captain exited with code 1", context);
                AssertContains("I could not get the parser tests to pass.", context);
                AssertContains("Suggest one next step only if something failed", context, "report instructions");
                return Task.CompletedTask;
            }));

            cases.Add(Case("outcome_check_run_tests", "The outcome lists a finished check run with its exit code, duration, and test counts, or its summary", TestTags.Positive, () =>
            {
                AskWorkSnapshot snapshot = VoyageSnapshot(AskTrackedWorkStateEnum.Succeeded, Row("msn_1", "Run tests", "Complete", "Landed"));
                AskWorkMissionResult mission = MissionResult("msn_1", "Run tests", "Complete", "Landed", null, null);
                mission.Checks.Add(new AskWorkCheckResult { CheckRunId = "chk_1", Label = "Test.Automated", Type = "UnitTest", Status = "Passed", ExitCode = 0, DurationMs = 160000, TestsTotal = 415, TestsPassed = 412, TestsFailed = 0, TestsSkipped = 3 });
                mission.Checks.Add(new AskWorkCheckResult { CheckRunId = "chk_2", Type = "Lint", Status = "Failed", ExitCode = 2, Summary = "3 lint errors in src/Parser.cs" });
                mission.Checks.Add(new AskWorkCheckResult { CheckRunId = "chk_3", Type = "Build", Status = "Running" });
                string outcome = AskWorkOutcomeFormatter.FormatOutcome(snapshot, Result(null, mission));
                string[] lines = outcome.Split('\n');
                AssertEqual("**Outcome**", lines[0], "no elapsed time when unknown");
                AssertEqual("  - UnitTest check \"Test.Automated\" passed (exit code 0, 2m 40s): 412 passed, 0 failed, 3 skipped of 415 tests.", lines[2]);
                AssertEqual("  - Lint check failed (exit code 2): 3 lint errors in src/Parser.cs.", lines[3]);
                AssertEqual(4, lines.Length, "a running check is not an outcome: " + outcome);

                AskWorkSnapshot run = new AskWorkSnapshot { EntityType = AskTrackedEntityTypeEnum.FleetActionRun, EntityId = "far_1", Title = "Run tests everywhere", State = AskTrackedWorkStateEnum.Failed };
                run.Targets.Add(new AskWorkTargetSnapshot { Id = "fat_1", VesselId = "vsl_a", VesselName = "Alpha", Status = "Succeeded", ExitCode = 0 });
                run.Targets.Add(new AskWorkTargetSnapshot { Id = "fat_2", VesselId = "vsl_b", VesselName = "Beta", Status = "Failed", ExitCode = 1, Reason = "2 tests failed" });
                string targets = AskWorkOutcomeFormatter.FormatOutcome(run, Result(5000));
                AssertEqual("**Outcome** (took 5s)\n- Beta: failed (exit code 1): 2 tests failed.\n- Alpha: succeeded (exit code 0).", targets);
                return Task.CompletedTask;
            }));

            cases.Add(Case("outcome_pr_landing", "The outcome of a pull request landing names the pull request and whether it merged", TestTags.Positive, () =>
            {
                AskWorkSnapshot snapshot = VoyageSnapshot(AskTrackedWorkStateEnum.Succeeded, Row("msn_1", "Bump deps", "Complete", "PullRequestMerged"));
                AskWorkMissionResult merged = MissionResult("msn_1", "Bump deps", "Complete", "PullRequestMerged", 42000, null);
                merged.PrUrl = "https://github.com/acme/doc/pull/7";
                string outcome = AskWorkOutcomeFormatter.FormatOutcome(snapshot, Result(42000, merged));
                AssertContains("- Mission \"Bump deps\" on DocConverter: complete, pull request merged (https://github.com/acme/doc/pull/7). Took 42s.", outcome);

                // Snapshot data only (no result): still a deterministic outcome from the snapshot rows.
                AskWorkSnapshot open = VoyageSnapshot(AskTrackedWorkStateEnum.Succeeded, Row("msn_2", "Docs", "PullRequestOpen", "PullRequestOpen"));
                open.Missions[0].PrUrl = "https://github.com/acme/doc/pull/8";
                AssertEqual("**Outcome**\n- Mission \"Docs\": pull request open (https://github.com/acme/doc/pull/8).", AskWorkOutcomeFormatter.FormatOutcome(open, null));

                AskWorkSnapshot cancelled = VoyageSnapshot(AskTrackedWorkStateEnum.Cancelled, Row("msn_3", "X", "Cancelled", null));
                AssertEqual(String.Empty, AskWorkOutcomeFormatter.FormatOutcome(cancelled, null), "cancelled work has no outcome block");
                AskWorkSnapshot job = new AskWorkSnapshot { EntityType = AskTrackedEntityTypeEnum.Job, State = AskTrackedWorkStateEnum.Succeeded };
                AssertFalse(AskWorkOutcomeFormatter.HasOutcome(job), "jobs keep their milestone sentence");
                AssertEqual("45s", AskWorkOutcomeFormatter.FormatDuration(45000));
                AssertEqual("1h 04m", AskWorkOutcomeFormatter.FormatDuration(3840000));
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("result_and_final_milestone_from_typed_rows", "The final milestone carries the outcome built from typed rows and is not narrated", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.ReportResultsOnCompletion = false;
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_result");
                Captain captain = await db.Captains.CreateAsync(new Captain("Reporter") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AskFinishedVoyage v = await CreateFinishedVoyageAsync(h, thread, captain).ConfigureAwait(false);

                AskWorkSnapshot snapshot = await h.Threads.Snapshots.BuildAsync(v.Work).ConfigureAwait(false);
                AskWorkResult result = await h.Tracker.Results.BuildAsync(v.Work, snapshot).ConfigureAwait(false);
                AskWorkMissionResult row = result.Missions.Single();
                AssertEqual("DocConverter", row.VesselName);
                AssertEqual("All 412 tests passed.", row.FinalMessage, "the captain's final message");
                AssertEqual(185000L, row.DurationMs, "mission runtime");
                AssertEqual(1, row.FilesChanged, "files in the diff");
                AssertEqual(2, row.LinesAdded, "added lines");
                AssertEqual(1, row.LinesRemoved, "removed lines");
                AskWorkCheckResult check = row.Checks.Single();
                AssertEqual(412, check.TestsPassed);
                AssertEqual(0, check.ExitCode);
                AssertEqual(192000L, result.ElapsedMs, "elapsed from the first start to completion");

                h.Runner.Reply = "narrated";
                await h.Tracker.RefreshAsync(v.Work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                AskMessage final = (await MessagesAsync(h, owner, thread.Id).ConfigureAwait(false)).Last(m => m.Kind == AskMessageKindEnum.WorkUpdate);
                AssertEqual(AskMessageRoleEnum.System, final.Role, "the final milestone is deterministic, not narrated");
                AssertStartsWith("Voyage \"Run tests\" finished (1 of 1 missions done).\n\n**Outcome** (took 3m 12s)", final.ContentText);
                AssertContains("- Mission \"Run Test.Automated\" on DocConverter: complete, landed. Took 3m 05s.", final.ContentText);
                AssertContains("UnitTest check \"Test.Automated\" passed (exit code 0, 2m 40s): 412 passed, 0 failed of 412 tests.", final.ContentText);
                AssertContains("Captain's final message: \"All 412 tests passed.\"", final.ContentText);
                AssertFalse(h.Runner.Calls.Any(c => c.Prompt.Contains("Rewrite this status update", StringComparison.Ordinal) && c.Prompt.Contains("**Outcome**", StringComparison.Ordinal)), "the final milestone was not narrated");
                AssertEqual(0, (await MessagesAsync(h, owner, thread.Id).ConfigureAwait(false)).Count(m => m.Kind == AskMessageKindEnum.WorkReport), "no report while the setting is off");
            }));

            cases.Add(CaseAsync("report_runs_once", "A captain report follows the final milestone once, as a WorkReport reply linked to the work", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.NarrateMilestones = false;
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_report");
                Captain captain = await db.Captains.CreateAsync(new Captain("Reporter") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AskFinishedVoyage v = await CreateFinishedVoyageAsync(h, thread, captain).ConfigureAwait(false);

                h.Runner.Reply = "Done: all 412 tests passed in DocConverter (3m 12s).";
                await h.Tracker.RefreshAsync(v.Work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                AssertTrue(await WaitForReportsAsync(h, owner, thread.Id, 1).ConfigureAwait(false), "the report arrived");
                await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.Turns.ActiveTurnId(thread.Id) == null)).ConfigureAwait(false);

                List<AskMessage> messages = await MessagesAsync(h, owner, thread.Id).ConfigureAwait(false);
                AskMessage final = messages.Last(m => m.Kind == AskMessageKindEnum.WorkUpdate);
                AskMessage report = messages.Single(m => m.Kind == AskMessageKindEnum.WorkReport);
                AssertTrue(report.Sequence > final.Sequence, "the report follows the final milestone");
                AssertEqual(AskMessageRoleEnum.Assistant, report.Role);
                AssertEqual(captain.Id, report.CaptainId);
                AssertEqual(v.Work.Id, report.TrackedWorkId, "linked to the work");
                AssertEqual("Done: all 412 tests passed in DocConverter (3m 12s).", report.ContentText);

                CaptainChatTurnOptions call = h.Runner.Calls.Single(c => c.Prompt.Contains(AskTurnCoordinator.ReportInstructions, StringComparison.Ordinal));
                AssertContains("Work this conversation started has finished. Results:", call.Prompt);
                AssertContains("All 412 tests passed.", call.Prompt, "the captain's final message is in the context");
                AssertContains("412 passed, 0 failed of 412 tests", call.Prompt, "test results are in the context");
                AssertContains("diff 1 file(s) +2/-1", call.Prompt, "diff size is in the context");
                AssertNotNull(call.McpSessionToken, "a thread-scoped token, as for any turn");
                AssertTrue(h.EventsFor("usr_report", "ask.turn").Count >= 2, "the report turn is announced like any turn");

                // Never twice: the same work again, and a restarted coordinator that finds the persisted report.
                AskReportRequest again = new AskReportRequest(thread, v.Work, await h.Threads.Snapshots.BuildAsync(v.Work).ConfigureAwait(false), null, final.Sequence);
                AssertFalse(await h.Turns.ScheduleReportAsync(again).ConfigureAwait(false), "already reported");
                AskTurnCoordinator restarted = new AskTurnCoordinator(db, h.Threads, h.Runner, new Armada.Core.Services.SessionTokenService(), null, h.Settings, h.Logging);
                AssertTrue(await restarted.ScheduleReportAsync(again).ConfigureAwait(false), "a new coordinator queues it");
                await AskTestHarness.WaitUntilAsync(() => Task.FromResult(restarted.PendingReportCount(thread.Id) == 0 && restarted.ActiveTurnId(thread.Id) == null)).ConfigureAwait(false);
                AssertEqual(1, (await MessagesAsync(h, owner, thread.Id).ConfigureAwait(false)).Count(m => m.Kind == AskMessageKindEnum.WorkReport), "the persisted report is found; still one report");
            }));

            cases.Add(CaseAsync("report_waits_for_active_turn", "A report waits while a turn runs in the thread and starts when it ends", TestTags.Positive, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.NarrateMilestones = false;
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_wait");
                Captain captain = await db.Captains.CreateAsync(new Captain("Reporter") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AskFinishedVoyage v = await CreateFinishedVoyageAsync(h, thread, captain).ConfigureAwait(false);

                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                AskTurnStart start = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "How is it going?" }).ConfigureAwait(false);
                AssertEqual(202, start.StatusCode);

                await h.Tracker.RefreshAsync(v.Work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                AssertEqual(1, h.Turns.PendingReportCount(thread.Id), "the report waits for the running turn");
                AssertEqual(start.Response!.TurnId, h.Turns.ActiveTurnId(thread.Id), "the user's turn is still the active one");
                AssertEqual(0, (await MessagesAsync(h, owner, thread.Id).ConfigureAwait(false)).Count(m => m.Kind == AskMessageKindEnum.WorkReport), "no report yet");

                h.Runner.Reply = "The tests passed.";
                h.Runner.Gate.TrySetResult(true);
                AssertTrue(await WaitForReportsAsync(h, owner, thread.Id, 1).ConfigureAwait(false), "the report ran after the turn ended");
                AssertEqual(0, h.Turns.PendingReportCount(thread.Id), "nothing left waiting");
                AssertEqual(1, h.Runner.Calls.Count(c => c.Prompt.Contains(AskTurnCoordinator.ReportInstructions, StringComparison.Ordinal)), "one report turn");
            }));

            cases.Add(CaseAsync("report_skipped_when_setting_off", "With Ask.ReportResultsOnCompletion off only the enriched final milestone is posted", TestTags.Negative, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.NarrateMilestones = false;
                h.Settings.Ask.ReportResultsOnCompletion = false;
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_off");
                Captain captain = await db.Captains.CreateAsync(new Captain("Reporter") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AskFinishedVoyage v = await CreateFinishedVoyageAsync(h, thread, captain).ConfigureAwait(false);

                await h.Tracker.RefreshAsync(v.Work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                List<AskMessage> messages = await MessagesAsync(h, owner, thread.Id).ConfigureAwait(false);
                AssertContains("**Outcome**", messages.Last().ContentText, "the final milestone has the outcome");
                AssertEqual(0, messages.Count(m => m.Kind == AskMessageKindEnum.WorkReport), "no report");
                AssertEqual(0, h.Runner.Calls.Count, "the captain was not asked");
                AssertEqual(0, h.Turns.PendingReportCount(thread.Id), "nothing queued");
                AssertNull(h.Turns.ActiveTurnId(thread.Id), "no turn");
            }));

            cases.Add(CaseAsync("report_skipped_when_user_already_asked", "A waiting report is skipped when the user posted after the work finished", TestTags.Negative, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.NarrateMilestones = false;
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_asked");
                Captain captain = await db.Captains.CreateAsync(new Captain("Reporter") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                AskFinishedVoyage v = await CreateFinishedVoyageAsync(h, thread, captain).ConfigureAwait(false);

                // A summary is running when the work finishes, so the report waits; meanwhile the user asks.
                h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                AskTurnStart summary = await h.Turns.SummarizeAsync(owner, thread.Id).ConfigureAwait(false);
                AssertEqual(202, summary.StatusCode);
                await h.Tracker.RefreshAsync(v.Work).ConfigureAwait(false);
                await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                AssertEqual(1, h.Turns.PendingReportCount(thread.Id), "waiting");
                AskThread current = (await h.Threads.GetThreadAsync(owner, thread.Id).ConfigureAwait(false))!;
                await h.Threads.AppendMessageAsync(current, new AskMessage { Role = AskMessageRoleEnum.User, Kind = AskMessageKindEnum.Text, ContentText = "What was the result?" }, false).ConfigureAwait(false);

                h.Runner.Gate.TrySetResult(true);
                AssertTrue(await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.Turns.PendingReportCount(thread.Id) == 0 && h.Turns.ActiveTurnId(thread.Id) == null)).ConfigureAwait(false), "the queue drained");
                AssertEqual(0, (await MessagesAsync(h, owner, thread.Id).ConfigureAwait(false)).Count(m => m.Kind == AskMessageKindEnum.WorkReport), "no report after the user asked");
                AssertFalse(h.Runner.Calls.Any(c => c.Prompt.Contains(AskTurnCoordinator.ReportInstructions, StringComparison.Ordinal)), "the captain was not asked to report");
            }));

            cases.Add(CaseAsync("report_skipped_for_archived_or_deleted_thread", "A waiting report is skipped when its thread was archived or deleted", TestTags.Negative, async () =>
            {
                using AskTestHarness h = await AskTestHarness.CreateAsync().ConfigureAwait(false);
                h.Settings.Ask.NarrateMilestones = false;
                DatabaseDriver db = h.Db.Driver;
                AuthContext owner = AskTestHarness.User("usr_archived");
                Captain captain = await db.Captains.CreateAsync(new Captain("Reporter") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);

                foreach (bool delete in new[] { false, true })
                {
                    AskThread thread = await h.Threads.CreateThreadAsync(owner, new AskThreadCreateRequest { CaptainId = captain.Id }).ConfigureAwait(false);
                    AskFinishedVoyage v = await CreateFinishedVoyageAsync(h, thread, captain, delete ? "Deleted" : "Archived").ConfigureAwait(false);
                    h.Runner.Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    AskTurnStart start = await h.Turns.SendMessageAsync(owner, thread.Id, new AskMessageSendRequest { Content = "Still running?" }).ConfigureAwait(false);
                    AssertEqual(202, start.StatusCode);
                    await h.Tracker.RefreshAsync(v.Work).ConfigureAwait(false);
                    await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);
                    AssertEqual(1, h.Turns.PendingReportCount(thread.Id), "waiting");

                    if (delete) AssertTrue(await h.Threads.DeleteThreadAsync(owner, thread.Id).ConfigureAwait(false), "deleted");
                    else AssertNotNull(await h.Threads.UpdateThreadAsync(owner, thread.Id, new AskThreadUpdateRequest { Archived = true }).ConfigureAwait(false), "archived");

                    int before = h.Runner.Calls.Count(c => c.Prompt.Contains(AskTurnCoordinator.ReportInstructions, StringComparison.Ordinal));
                    h.Runner.Gate.TrySetResult(true);
                    AssertTrue(await AskTestHarness.WaitUntilAsync(() => Task.FromResult(h.Turns.PendingReportCount(thread.Id) == 0 && h.Turns.ActiveTurnId(thread.Id) == null)).ConfigureAwait(false), "the queue drained");
                    AssertEqual(before, h.Runner.Calls.Count(c => c.Prompt.Contains(AskTurnCoordinator.ReportInstructions, StringComparison.Ordinal)), (delete ? "deleted" : "archived") + " thread: no report turn");
                    h.Runner.Gate = null;
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Ask Work Report", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static AskWorkMissionSnapshot Row(string id, string title, string status, string? landing)
        {
            return new AskWorkMissionSnapshot { Id = id, Title = title, Status = status, LandingOutcome = landing, CaptainId = "cpt_1" };
        }

        private static AskWorkSnapshot VoyageSnapshot(AskTrackedWorkStateEnum state, params AskWorkMissionSnapshot[] rows)
        {
            AskWorkSnapshot snapshot = new AskWorkSnapshot();
            snapshot.EntityType = AskTrackedEntityTypeEnum.Voyage;
            snapshot.EntityId = "vyg_1";
            snapshot.Title = "Tests";
            snapshot.State = state;
            snapshot.Missions.AddRange(rows);
            return snapshot;
        }

        private static AskWorkMissionResult MissionResult(string id, string title, string status, string? landing, long? durationMs, string? finalMessage)
        {
            return new AskWorkMissionResult { MissionId = id, Title = title, Status = status, LandingOutcome = landing, VesselName = "DocConverter", DurationMs = durationMs, FinalMessage = finalMessage };
        }

        private static AskWorkResult Result(long? elapsedMs, params AskWorkMissionResult[] missions)
        {
            AskWorkResult result = new AskWorkResult();
            result.ElapsedMs = elapsedMs;
            result.Missions.AddRange(missions);
            return result;
        }

        /// <summary>
        /// A tracked voyage whose only mission ran tests on DocConverter and landed: final message, diff, check run with
        /// test results, runtime. The voyage is Complete, but the tracked row has only seen it active, so the next refresh
        /// posts the final milestone.
        /// </summary>
        private static async Task<AskFinishedVoyage> CreateFinishedVoyageAsync(AskTestHarness h, AskThread thread, Captain captain, string vesselName = "DocConverter")
        {
            DatabaseDriver db = h.Db.Driver;
            Vessel vessel = await db.Vessels.CreateAsync(new Vessel(vesselName, "https://example.com/" + vesselName + ".git") { TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
            Voyage voyage = await db.Voyages.CreateAsync(new Voyage("Run tests") { TenantId = Constants.DefaultTenantId, Status = VoyageStatusEnum.InProgress }).ConfigureAwait(false);
            DateTime started = DateTime.UtcNow.AddMinutes(-10);
            Mission mission = await db.Missions.CreateAsync(new Mission("Run Test.Automated") { TenantId = Constants.DefaultTenantId, VoyageId = voyage.Id, VesselId = vessel.Id, CaptainId = captain.Id, Status = MissionStatusEnum.InProgress, StartedUtc = started }).ConfigureAwait(false);

            AskTrackedWork work = await h.Threads.TrackWorkAsync(thread, new AskWorkLink(AskTrackedEntityTypeEnum.Voyage, voyage.Id, false)).ConfigureAwait(false);
            await h.Tracker.OnWorkLinkedAsync(work).ConfigureAwait(false);
            await h.Tracker.WaitForMilestonesAsync(thread.Id).ConfigureAwait(false);

            mission.Status = MissionStatusEnum.Complete;
            mission.CompletedUtc = started.AddSeconds(185);
            mission.AgentOutput = "  All 412 tests passed.\n";
            mission.DiffSnapshot = "diff --git a/README.md b/README.md\n--- a/README.md\n+++ b/README.md\n@@ -1,2 +1,3 @@\n-old\n+new\n+more\n context\n";
            await db.Missions.UpdateAsync(mission).ConfigureAwait(false);

            CheckRun check = new CheckRun();
            check.TenantId = Constants.DefaultTenantId;
            check.MissionId = mission.Id;
            check.VoyageId = voyage.Id;
            check.VesselId = vessel.Id;
            check.Label = "Test.Automated";
            check.Type = CheckRunTypeEnum.UnitTest;
            check.Status = CheckRunStatusEnum.Passed;
            check.Command = "dotnet run --project src/Test.Automated";
            check.ExitCode = 0;
            check.DurationMs = 160000;
            check.TestSummary = new CheckRunTestSummary { Total = 412, Passed = 412, Failed = 0, Skipped = 0 };
            await db.CheckRuns.CreateAsync(check).ConfigureAwait(false);

            voyage.Status = VoyageStatusEnum.Complete;
            voyage.CompletedUtc = started.AddSeconds(192);
            await db.Voyages.UpdateAsync(voyage).ConfigureAwait(false);

            AskFinishedVoyage result = new AskFinishedVoyage();
            result.Work = work;
            result.MissionId = mission.Id;
            return result;
        }

        private static async Task<List<AskMessage>> MessagesAsync(AskTestHarness h, AuthContext owner, string threadId)
        {
            AskMessagePage page = (await h.Threads.EnumerateMessagesAsync(owner, threadId, new AskMessageEnumerateRequest { PageSize = 200 }).ConfigureAwait(false))!;
            return page.Messages;
        }

        private static async Task<bool> WaitForReportsAsync(AskTestHarness h, AuthContext owner, string threadId, int count)
        {
            return await AskTestHarness.WaitUntilAsync(async () =>
            {
                List<AskMessage> messages = await MessagesAsync(h, owner, threadId).ConfigureAwait(false);
                return messages.Count(m => m.Kind == AskMessageKindEnum.WorkReport && m.ContentText.Length > 0) >= count;
            }).ConfigureAwait(false);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return CaseAsync(caseId, displayName, tag, body);
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
    }
}
