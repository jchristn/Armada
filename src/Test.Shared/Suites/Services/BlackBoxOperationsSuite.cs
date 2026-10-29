namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Black box operations (plan 13.4): runbook linkage of recovery paths, recovery outcome tracking, per-path success
    /// rates, and the advisor's success-rate tie-breaker.
    /// </summary>
    public sealed class BlackBoxOperationsSuite : IArmadaTestSuite
    {
        private const string SuiteId = "Services.BlackBoxOperations";
        private const string PullRequestPath = "bbx-land-001.pull-request";
        private const string StashPath = "bbx-land-001.stash-retry";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("runbook_link_scoring", "A runbook tag naming the path beats one naming the rule, which beats the step or the kind; a title naming the rule or step matches; inactive runbooks never match", TestTags.Positive, () =>
            {
                List<BlackBoxRecoveryPath> paths = new List<BlackBoxRecoveryPath>
                {
                    new BlackBoxRecoveryPath { Id = PullRequestPath, RuleId = "BBX-LAND-001", Title = "Open a pull request" },
                    new BlackBoxRecoveryPath { Id = StashPath, RuleId = "BBX-LAND-001", Title = "Stash and retry" },
                    new BlackBoxRecoveryPath { Id = "bbx-exec-001.wait", RuleId = "BBX-EXEC-001", Title = "Wait" }
                };
                List<Runbook> runbooks = new List<Runbook>
                {
                    NewRunbook("pbk_rule", "Landing runbook", "blackbox: BBX-LAND-001"),
                    NewRunbook("pbk_path", "PR runbook", "Steps.\nblackbox: " + PullRequestPath + ", DirtyCheckout"),
                    NewRunbook("pbk_inactive", "Inactive", "blackbox: bbx-exec-001.wait", false)
                };
                BlackBoxRunbookLinker.Link(paths, runbooks, BlackBoxStepEnum.DirtyCheckout, MissionFailureKindEnum.Infra);
                AssertEqual("pbk_path", paths[0].RunbookId, "path tag wins");
                AssertEqual("PR runbook", paths[0].RunbookTitle);
                AssertEqual("pbk_rule", paths[1].RunbookId, "the rule tag outranks the step tag (DirtyCheckout)");
                AssertEqual("pbk_path", paths[2].RunbookId, "the step tag applies to every path of the entry; the inactive runbook naming this path is skipped");

                List<BlackBoxRecoveryPath> titled = new List<BlackBoxRecoveryPath> { new BlackBoxRecoveryPath { Id = "bbx-x.y", RuleId = "BBX-X", Title = "t" } };
                BlackBoxRunbookLinker.Link(titled, new List<Runbook> { NewRunbook("pbk_title", "What to do on DirtyCheckout", null) }, BlackBoxStepEnum.DirtyCheckout, null);
                AssertEqual("pbk_title", titled[0].RunbookId, "title names the step");
                BlackBoxRunbookLinker.Link(titled, new List<Runbook> { NewRunbook("pbk_none", "Unrelated", "blackbox: Push") }, BlackBoxStepEnum.DirtyCheckout, null);
                AssertNull(titled[0].RunbookId, "a link is dropped when no runbook matches any more");
            }));

            cases.Add(Case("runbook_linked_path_starts_the_runbook", "A path linked to a runbook plans and runs start_runbook_execution (with the entry and path in its notes) instead of its tool steps", TestTags.Positive, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync().ConfigureAwait(false);
                f.Runtime.Insights.Runbooks.RunbooksOverride = e => new List<Runbook> { NewRunbook("pbk_dirty", "Dirty checkout runbook", "blackbox: " + PullRequestPath) };
                await f.Service.ReanalyzeAsync(f.Admin, f.EntryId).ConfigureAwait(false);
                BlackBoxRecord record = await f.Service.GetRecordAsync(f.Admin, f.EntryId).ConfigureAwait(false);
                AssertEqual("pbk_dirty", record.Analysis!.RecoveryPaths.Single(p => p.Id == PullRequestPath).RunbookId, "linked on analysis");
                AssertNull(record.Analysis.RecoveryPaths.Single(p => p.Id == StashPath).RunbookId, "other paths unchanged");

                BlackBoxRecoveryRunResult dry = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, new BlackBoxRecoveryRunRequest()).ConfigureAwait(false);
                BlackBoxRecoveryStep step = dry.Plan.Single();
                AssertEqual(BlackBoxRunbookLinker.RunbookTool, step.Action!.Tool);
                AssertEqual("pbk_dirty", step.Action.Arguments["runbookId"]);
                AssertContains(f.EntryId, step.Action.Arguments["notes"]);
                AssertEqual("POST /api/v1/runbooks/pbk_dirty/executions", step.RestCommand);
                BlackBoxRecoveryRunResult run = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(dry.ConfirmToken)).ConfigureAwait(false);
                AssertTrue(run.Succeeded, run.Message);
                AssertEqual("start_runbook_execution", String.Join(",", f.Executor.Tools), "only the runbook started");

                await AssertThrowsAsync<UnauthorizedAccessException>(() => f.Service.RunRecoveryAsync(BlackBoxTestKit.User("ten_bb", "usr_bb"), f.EntryId, PullRequestPath, new BlackBoxRecoveryRunRequest()), "starting a runbook needs a tenant admin").ConfigureAwait(false);
            }));

            cases.Add(Case("outcomes_tracked_and_settled", "A confirmed run records a Pending outcome; the mission completing settles it Succeeded with a System note; a failed step is RunFailed at once; stats group and scope runs", TestTags.Positive, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync().ConfigureAwait(false);
                BlackBoxRecoveryRunResult dry = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, new BlackBoxRecoveryRunRequest()).ConfigureAwait(false);
                AssertEqual(0L, (await f.Db.BlackBoxRecoveryOutcomes.EnumerateByEntryAsync(f.EntryId, new EnumerationQuery()).ConfigureAwait(false)).TotalRecords, "dry runs are not tracked");
                await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, PullRequestPath, Confirm(dry.ConfirmToken)).ConfigureAwait(false);
                BlackBoxRecoveryOutcome pending = (await f.Db.BlackBoxRecoveryOutcomes.EnumerateByEntryAsync(f.EntryId, new EnumerationQuery()).ConfigureAwait(false)).Objects.Single();
                AssertEqual(BlackBoxRecoveryOutcomeEnum.Pending, pending.Outcome); AssertEqual(PullRequestPath, pending.PathId); AssertEqual("BBX-LAND-001", pending.RuleId); AssertTrue(pending.RunSucceeded);
                AssertEqual(f.Admin.UserId, pending.UserId);

                AssertEqual(0, await f.Runtime.Insights.Outcomes.SettleAsync().ConfigureAwait(false), "nothing terminal yet");

                // A failed step settles at once. It runs while the landing is still failed: retry_landing on a mission that
                // is no longer LandingFailed is refused as an unmet precondition and never runs.
                f.Executor.FailTools.Add("retry_landing");
                BlackBoxRecoveryRunResult dry2 = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, StashPath, new BlackBoxRecoveryRunRequest()).ConfigureAwait(false);
                BlackBoxRecoveryRunResult failedRun = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, StashPath, Confirm(dry2.ConfirmToken)).ConfigureAwait(false);
                AssertFalse(failedRun.Succeeded);
                BlackBoxRecoveryOutcome runFailed = (await f.Db.BlackBoxRecoveryOutcomes.EnumerateByEntryAsync(f.EntryId, new EnumerationQuery()).ConfigureAwait(false)).Objects.Single(o => o.PathId == StashPath);
                AssertEqual(BlackBoxRecoveryOutcomeEnum.RunFailed, runFailed.Outcome, "a failed step settles at once");

                Mission mission = (await f.Db.Missions.ReadAsync(f.MissionId).ConfigureAwait(false))!;
                mission.Status = MissionStatusEnum.Complete;
                mission.CompletedUtc = DateTime.UtcNow.AddSeconds(1);
                mission.LastUpdateUtc = DateTime.UtcNow.AddSeconds(1);
                await f.Db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                AssertEqual(1, await f.Runtime.Insights.Outcomes.SettleAsync().ConfigureAwait(false), "settled");
                BlackBoxRecoveryOutcome settled = (await f.Db.BlackBoxRecoveryOutcomes.ReadAsync(pending.Id).ConfigureAwait(false))!;
                AssertEqual(BlackBoxRecoveryOutcomeEnum.Succeeded, settled.Outcome); AssertEqual("Complete", settled.OutcomeStatus); AssertNotNull(settled.ResolvedUtc);
                List<BlackBoxNote> notes = (await f.Service.EnumerateNotesAsync(f.Admin, f.EntryId, null).ConfigureAwait(false)).Objects;
                AssertTrue(notes.Any(n => n.AuthorKind == BlackBoxNoteAuthorKindEnum.System && n.Body.Contains("worked", StringComparison.Ordinal)), "outcome noted on the entry");
                BlackBoxRecoveryRunResult refused = await f.Service.RunRecoveryAsync(f.Admin, f.EntryId, StashPath, new BlackBoxRecoveryRunRequest()).ConfigureAwait(false);
                AssertTrue(refused.Refused, "retry_landing on the Complete mission is refused");

                EnumerationResult<BlackBoxRecoveryStat> stats = await f.Runtime.Insights.Service.EnumerateRecoveryStatsAsync(f.Admin, null, null, null).ConfigureAwait(false);
                AssertEqual(2L, stats.TotalRecords, "one row per path");
                BlackBoxRecoveryStat pr = stats.Objects.Single(s => s.PathId == PullRequestPath);
                AssertEqual(1L, pr.Runs); AssertEqual(1L, pr.Succeeded); AssertEqual(1.0, pr.SuccessRate);
                BlackBoxRecoveryStat stash = stats.Objects.Single(s => s.PathId == StashPath);
                AssertEqual(1L, stash.Failed); AssertEqual(0.0, stash.SuccessRate);
                AssertEqual(1L, (await f.Runtime.Insights.Service.EnumerateRecoveryStatsAsync(f.Admin, "BBX-LAND-001", null, new EnumerationQuery { PageSize = 1 }).ConfigureAwait(false)).Objects.Count, "paged");
                AssertEqual(0L, (await f.Runtime.Insights.Service.EnumerateRecoveryStatsAsync(f.Admin, "BBX-NONE", null, null).ConfigureAwait(false)).TotalRecords, "rule filter");
                AssertEqual(0L, (await f.Runtime.Insights.Service.EnumerateRecoveryStatsAsync(BlackBoxTestKit.User("ten_bb", "usr_someone_else"), null, null, null).ConfigureAwait(false)).TotalRecords, "a user sees their own runs");
                AssertEqual(0L, (await f.Runtime.Insights.Service.EnumerateRecoveryStatsAsync(BlackBoxTestKit.User("ten_other", "usr_x", true), null, null, null).ConfigureAwait(false)).TotalRecords, "another tenant sees nothing");
                await AssertThrowsAsync<UnauthorizedAccessException>(() => f.Runtime.Insights.Service.EnumerateRecoveryStatsAsync(new AuthContext(), null, null, null), "unauthenticated").ConfigureAwait(false);
            }));

            cases.Add(Case("outcomes_failed_or_expired", "A new failure capture after the run settles Failed; a run nothing follows expires after the watch window", TestTags.Negative, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync().ConfigureAwait(false);
                BlackBoxRecoveryOutcomeTracker tracker = f.Runtime.Insights.Outcomes;
                BlackBoxEntry entry = (await f.Db.BlackBoxEntries.ReadAsync(f.EntryId).ConfigureAwait(false))!;
                BlackBoxRecoveryPath path = new BlackBoxRecoveryPath { Id = PullRequestPath, RuleId = "BBX-LAND-001" };
                await tracker.OnRecoveryRunAsync(f.Admin, entry, path, new BlackBoxRecoveryRunResult { Succeeded = true }).ConfigureAwait(false);
                Mission mission = (await f.Db.Missions.ReadAsync(f.MissionId).ConfigureAwait(false))!;
                mission.TenantId = "ten_bb"; mission.UserId = "usr_bb";
                await Task.Delay(20).ConfigureAwait(false);
                string? again = await f.Runtime.Capture.RecordMissionFailureAsync(mission, MissionStatusEnum.WorkProduced, BlackBoxStepEnum.DirtyCheckout).ConfigureAwait(false);
                AssertNotNull(again, "captured again");
                AssertEqual(1, await tracker.SettleAsync().ConfigureAwait(false));
                AssertEqual(BlackBoxRecoveryOutcomeEnum.Failed, (await f.Db.BlackBoxRecoveryOutcomes.EnumerateByEntryAsync(f.EntryId, new EnumerationQuery()).ConfigureAwait(false)).Objects.Single().Outcome, "captured again means it did not work");

                BlackBoxRecoveryOutcome old = new BlackBoxRecoveryOutcome { EntryId = f.EntryId, MissionId = "msn_gone", PathId = StashPath, CreatedUtc = DateTime.UtcNow.AddDays(-1) };
                await f.Db.BlackBoxRecoveryOutcomes.CreateAsync(old).ConfigureAwait(false);
                BlackBoxRecoveryOutcome stale = new BlackBoxRecoveryOutcome { EntryId = f.EntryId, VoyageId = "vyg_gone_too", PathId = StashPath, CreatedUtc = DateTime.UtcNow.AddDays(-1) };
                await f.Db.BlackBoxRecoveryOutcomes.CreateAsync(stale).ConfigureAwait(false);
                BlackBoxRecoveryOutcome watched = new BlackBoxRecoveryOutcome { EntryId = f.EntryId, MissionId = f.MissionId, PathId = StashPath, CreatedUtc = DateTime.UtcNow.AddDays(-(BlackBoxInsightLimits.OutcomeWatchDays + 1)) };
                await f.Db.BlackBoxRecoveryOutcomes.CreateAsync(watched).ConfigureAwait(false);
                await tracker.SettleAsync().ConfigureAwait(false);
                AssertEqual(BlackBoxRecoveryOutcomeEnum.Expired, (await f.Db.BlackBoxRecoveryOutcomes.ReadAsync(old.Id).ConfigureAwait(false))!.Outcome, "a deleted mission expires");
                AssertEqual(BlackBoxRecoveryOutcomeEnum.Expired, (await f.Db.BlackBoxRecoveryOutcomes.ReadAsync(stale.Id).ConfigureAwait(false))!.Outcome, "a deleted voyage expires");
                BlackBoxRecoveryOutcome watchedAfter = (await f.Db.BlackBoxRecoveryOutcomes.ReadAsync(watched.Id).ConfigureAwait(false))!;
                AssertTrue(watchedAfter.Outcome == BlackBoxRecoveryOutcomeEnum.Expired || watchedAfter.Outcome == BlackBoxRecoveryOutcomeEnum.Failed, "past the watch window it settles");
            }));

            cases.Add(Case("success_rate_breaks_ties", "Among paths of equally weighted rules a known success rate decides the order; without rates the catalog order stands; the cache needs three settled runs", TestTags.Positive, async () =>
            {
                using BlackBoxRecoveryFixture f = await BlackBoxRecoveryFixture.CreateAsync().ConfigureAwait(false);
                BlackBoxRecord record = await f.Service.GetRecordAsync(f.Admin, f.EntryId).ConfigureAwait(false);
                List<string> plain = BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default).RecoveryPaths.Select(p => p.Id).ToList();
                AssertTrue(plain.IndexOf(StashPath) < plain.IndexOf(PullRequestPath), "catalog order: stash before pull request");
                FixedBlackBoxPathStats stats = new FixedBlackBoxPathStats();
                stats.Rates[StashPath] = 0.1;
                stats.Rates[PullRequestPath] = 0.9;
                List<BlackBoxRecoveryPath> ranked = BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default, null, stats).RecoveryPaths;
                AssertTrue(ranked.FindIndex(p => p.Id == PullRequestPath) < ranked.FindIndex(p => p.Id == StashPath), "the better rate first");
                AssertEqual(1, ranked.First(p => p.Id == PullRequestPath).Rank < ranked.First(p => p.Id == StashPath).Rank ? 1 : 0, "ranks follow");
                AssertEqual(0.9, ranked.First(p => p.Id == PullRequestPath).SuccessRate); AssertEqual(5, ranked.First(p => p.Id == PullRequestPath).TrackedRuns);
                AssertEqual(String.Join(",", plain), String.Join(",", BlackBoxAdvisor.Analyze(record, BlackBoxRuleCatalog.Default, null, new FixedBlackBoxPathStats()).RecoveryPaths.Select(p => p.Id)), "no rates, catalog order");

                BlackBoxPathStatsCache cache = f.Runtime.Insights.PathStats;
                for (int i = 0; i < 2; i++) await f.Db.BlackBoxRecoveryOutcomes.CreateAsync(new BlackBoxRecoveryOutcome { EntryId = f.EntryId, PathId = PullRequestPath, RuleId = "BBX-LAND-001", Outcome = BlackBoxRecoveryOutcomeEnum.Succeeded }).ConfigureAwait(false);
                await cache.RefreshAsync().ConfigureAwait(false);
                AssertFalse(cache.TryGetRate(PullRequestPath, out _, out _), "two settled runs are not enough");
                await f.Db.BlackBoxRecoveryOutcomes.CreateAsync(new BlackBoxRecoveryOutcome { EntryId = f.EntryId, PathId = PullRequestPath, RuleId = "BBX-LAND-001", Outcome = BlackBoxRecoveryOutcomeEnum.Failed }).ConfigureAwait(false);
                await f.Db.BlackBoxRecoveryOutcomes.CreateAsync(new BlackBoxRecoveryOutcome { EntryId = f.EntryId, PathId = PullRequestPath, RuleId = "BBX-LAND-001", Outcome = BlackBoxRecoveryOutcomeEnum.Pending }).ConfigureAwait(false);
                await cache.RefreshAsync().ConfigureAwait(false);
                AssertTrue(cache.TryGetRate(PullRequestPath, out double rate, out int settled), "three settled runs");
                AssertEqual(3, settled, "pending runs do not count"); AssertTrue(Math.Abs(rate - 2.0 / 3.0) < 0.0001, "rate");
                AssertTrue(f.Runtime.Advisor.PathStats == cache, "the runtime wires the cache into the advisor");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Black box operations: runbook linkage, recovery outcomes, path success rates", cases: cases);
        }

        private static Runbook NewRunbook(string id, string title, string? description, bool active = true)
        {
            Runbook r = new Runbook();
            r.Id = id;
            r.Title = title;
            r.Description = description;
            r.Active = active;
            return r;
        }

        private static BlackBoxRecoveryRunRequest Confirm(string? token)
        {
            return new BlackBoxRecoveryRunRequest { DryRun = false, ConfirmToken = token };
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => body(), tags: new List<string> { tag });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(suiteId: SuiteId, caseId: caseId, displayName: displayName, executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; }, tags: new List<string> { tag });
        }
    }
}
