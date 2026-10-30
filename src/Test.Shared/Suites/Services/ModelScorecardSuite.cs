namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The model scorecard (docs/MODEL_HETEROGENEITY.md Phase 5 item 2) computed from stage outcomes on the configured
    /// provider: the Wilson interval at 0 and n, each attribution rule with its removal count, Unknown and Mixed as their
    /// own buckets, Judge verdicts charged only to the slice the profile produced, the day and mission window (defaults
    /// from a minimally deserialized request, explicit overrides, range errors), revisions separate unless marked
    /// compatible, cost per accepted result across rework and fallback attempts, and metrics returned as unavailable.
    /// </summary>
    public sealed class ModelScorecardSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ModelScorecard";
        private const string Tenant = "ten_sc";
        private const string ModelName = "qwen3.8:27b";
        private static readonly DateTime _Now = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("wilson_interval_bounds", "The Wilson interval is exactly 0 at a count of 0, exactly 1 at a count of n, the whole range with no trials, and matches known values", TestTags.Positive, () =>
            {
                AssertEqual(0.0, WilsonScoreInterval.Lower(0, 10), "lower at 0 of 10");
                AssertEqual(0.2775, WilsonScoreInterval.Upper(0, 10), "upper at 0 of 10");
                AssertEqual(0.7225, WilsonScoreInterval.Lower(10, 10), "lower at 10 of 10");
                AssertEqual(1.0, WilsonScoreInterval.Upper(10, 10), "upper at 10 of 10");
                AssertEqual(0.0, WilsonScoreInterval.Lower(0, 0), "lower with no trials");
                AssertEqual(1.0, WilsonScoreInterval.Upper(0, 0), "upper with no trials");
                AssertEqual(0.1517, WilsonScoreInterval.Lower(4, 11), "lower at 4 of 11");
                AssertEqual(0.6462, WilsonScoreInterval.Upper(4, 11), "upper at 4 of 11");
                AssertEqual(0.0, WilsonScoreInterval.Lower(0, 1), "lower at 0 of 1");
                AssertEqual(1.0, WilsonScoreInterval.Upper(1, 1), "upper at 1 of 1");
                for (int k = 0; k <= 7; k++)
                    AssertEqual(WilsonScoreInterval.Lower(k, 7), Math.Round(1.0 - WilsonScoreInterval.Upper(7 - k, 7), 4), "symmetric at " + k + " of 7");
                AssertThrows<ArgumentOutOfRangeException>(() => WilsonScoreInterval.Lower(3, 2), "count above n");
                AssertThrows<ArgumentOutOfRangeException>(() => WilsonScoreInterval.Upper(-1, 2), "negative count");
                AssertThrows<ArgumentOutOfRangeException>(() => WilsonScoreInterval.Upper(0, -1), "negative n");
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("attribution_exclusions", "Each attribution rule removes its outcomes and reports how many; a hand classification keeps an Infra failure; other models never count", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, "1.4.1").ConfigureAwait(false);

                await AddAsync(db, Outcome("msn_eval", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 1, o => o.IsEvaluation = true)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_ntm", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 1, o => o.SuspectedCause = FailureCauseEnum.NotTheModel)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_infra", 1, StageDispositionEnum.Failed, null, 1, o => o.FailureKind = MissionFailureKindEnum.Infra)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_op", 1, StageDispositionEnum.Failed, null, 1, o => o.FailureKind = MissionFailureKindEnum.OperatorAction)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_cancel", 1, StageDispositionEnum.Cancelled, null, 1)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_int", 1, StageDispositionEnum.Interrupted, null, 1)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_usage", 1, StageDispositionEnum.Failed, null, 1, o => o.RuntimeFailureKind = RuntimeFailureKindEnum.UsageLimit)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_pend", 1, StageDispositionEnum.Pending, null, 1)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_te", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 1, o => o.Persona = PersonaCatalog.TestEngineer)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_manual", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 1, o =>
                {
                    o.FailureKind = MissionFailureKindEnum.Infra;
                    o.SuspectedCause = FailureCauseEnum.Capability;
                    o.CauseSource = FailureCauseSourceEnum.Manual;
                })).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_ok", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 1)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_other", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 1, o => o.Model = "gpt-oss:120b")).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_ctx", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 1, o => o.ContextTokens = 32768)).ConfigureAwait(false);

                ModelScorecard card = await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "worker" }).ConfigureAwait(false);
                AssertEqual(1, Removed(card, ModelScorecardService.RuleEvaluation), "evaluation");
                AssertEqual(1, Removed(card, ModelScorecardService.RuleNotTheModel), "not the model");
                AssertEqual(1, Removed(card, ModelScorecardService.RuleInfra), "infra");
                AssertEqual(2, Removed(card, ModelScorecardService.RuleOperatorAction), "operator action and cancelled");
                AssertEqual(2, Removed(card, ModelScorecardService.RuleRuntimeEnvironment), "interrupted and usage limit");
                AssertEqual(1, Removed(card, ModelScorecardService.RuleNotSettled), "pending");
                AssertEqual(1, Removed(card, ModelScorecardService.RuleOtherStage), "test engineer outcome charged to its own stage");
                AssertEqual(0, Removed(card, ModelScorecardService.RuleJudgeVerdictOtherCaptain), "no judge verdicts");
                AssertEqual(9, card.Exclusions.Count, "every rule is listed");
                AssertEqual(2, card.Missions, "manual-classified and clean missions remain");
                AssertEqual(2, card.Attempts, "attempts");
                ModelScorecardGroup group = card.Groups.Single();
                AssertEqual(1, Metric(group, "stageCheck.failed").Count, "the hand-classified failure counts against the model");
                AssertEqual(1, Metric(group, "cause.Capability").Count, "its cause");
                AssertEqual(1, Metric(group, "firstPassSuccess").Count, "first pass count");
                AssertEqual(2, Metric(group, "firstPassSuccess").Of, "first pass of");
            }));

            cases.Add(CaseAsync("unknown_and_mixed_buckets", "Unknown, Mixed and unclassified causes are their own buckets of the attempts that fell short", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, "1.4.1").ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_u", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 1, o => o.SuspectedCause = FailureCauseEnum.Unknown)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_m", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 2, o => o.SuspectedCause = FailureCauseEnum.Mixed)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_c", 1, StageDispositionEnum.Escalated, StageCheckStatusEnum.Failed, 3, o => o.SuspectedCause = FailureCauseEnum.Capability)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_n", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 4, o => o.FailureKind = MissionFailureKindEnum.TestFail)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_p", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 5)).ConfigureAwait(false);

                ModelScorecardGroup group = (await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker" }).ConfigureAwait(false)).Groups.Single();
                AssertRate(Metric(group, "cause.Unknown"), 1, 4);
                AssertRate(Metric(group, "cause.Mixed"), 1, 4);
                AssertRate(Metric(group, "cause.Capability"), 1, 4);
                AssertRate(Metric(group, "cause.Unclassified"), 1, 4);
                AssertRate(Metric(group, "cause.Protocol"), 0, 4);
                AssertEqual(0.0, Metric(group, "cause.Protocol").Lower, "lower at 0");
                AssertRate(Metric(group, "escalations.away"), 1, 5);
                AssertRate(Metric(group, "escalations.priorWorkUnknown"), 1, 1);
                AssertRate(Metric(group, "firstPassSuccess"), 1, 5);
            }));

            cases.Add(CaseAsync("judge_verdicts_charged_to_reviewed_slice", "A Judge rejection counts only when the reviewed work is this profile's attempt; verdicts on another captain's work and evaluation verdicts are removed", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, "1.4.1").ConfigureAwait(false);

                // Ours, rejected by the Judge.
                await AddAsync(db, Outcome("msn_w1", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 3)).ConfigureAwait(false);
                await AddAsync(db, Judge("msn_j1", "msn_w1", "NeedsRevision", 2.9)).ConfigureAwait(false);
                // Ours, passed, plus an evaluation Judge verdict on it that must not count.
                await AddAsync(db, Outcome("msn_w2", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 3)).ConfigureAwait(false);
                await AddAsync(db, Judge("msn_j2", "msn_w2", "Pass", 2.9)).ConfigureAwait(false);
                await AddAsync(db, Judge("msn_j2e", "msn_w2", "Fail", 2.8, o => o.IsEvaluation = true)).ConfigureAwait(false);
                // Escalated away; another model finished it, and the Judge rejected that captain's work.
                await AddAsync(db, Outcome("msn_w3", 1, StageDispositionEnum.Escalated, StageCheckStatusEnum.Failed, 3, o =>
                {
                    o.NextCaptainId = "cpt_big";
                    o.FinishedByOutcomeId = "sto_finish";
                    o.FinishedByCaptainId = "cpt_big";
                    o.PriorWorkKept = true;
                })).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_w3", 2, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 2.95, o =>
                {
                    o.Id = "sto_finish";
                    o.Model = "claude-opus";
                    o.Runtime = AgentRuntimeEnum.ClaudeCode;
                    o.ModelProvider = null;
                    o.ContextTokens = null;
                    o.CaptainId = "cpt_big";
                })).ConfigureAwait(false);
                await AddAsync(db, Judge("msn_j3", "msn_w3", "Fail", 2.9)).ConfigureAwait(false);

                ModelScorecard card = await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker" }).ConfigureAwait(false);
                ModelScorecardGroup group = card.Groups.Single();
                AssertEqual(1, Removed(card, ModelScorecardService.RuleJudgeVerdictOtherCaptain), "verdict on the finishing captain's work");
                AssertEqual(1, Removed(card, ModelScorecardService.RuleJudgeVerdictExcluded), "evaluation verdict");
                AssertRate(Metric(group, "judge.needsRevision"), 1, 2);
                AssertRate(Metric(group, "judge.pass"), 1, 2);
                AssertRate(Metric(group, "judge.fail"), 0, 2);
                AssertRate(Metric(group, "firstPassSuccess"), 1, 3);
                AssertRate(Metric(group, "acceptedResults"), 1, 3);
                AssertRate(Metric(group, "escalations.away"), 1, 3);
                AssertRate(Metric(group, "escalations.nextFinished"), 1, 1);
                AssertRate(Metric(group, "escalations.priorWorkKept"), 1, 1);
                AssertRate(Metric(group, "escalations.priorWorkReplaced"), 0, 1);
                ModelScorecardMetric categories = Metric(group, "judge.findingCategories");
                AssertFalse(categories.Available, "finding categories are not recorded");
                AssertNotNull(categories.UnavailableReason, "reason");
                AssertNull(categories.Count, "no invented count");
            }));

            cases.Add(CaseAsync("window_days_and_missions", "The window is the smaller of the day and mission limits: defaults from a minimal request, explicit overrides, and range errors", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, "1.4.1").ConfigureAwait(false);
                // One mission a day, 0.5 to 69.5 days ago: 30 in the default day window, 70 in all.
                for (int i = 0; i < 70; i++)
                    await AddAsync(db, Outcome("msn_d" + i.ToString("D2"), 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, i + 0.5)).ConfigureAwait(false);

                ModelScorecardRequest minimal = JsonSerializer.Deserialize<ModelScorecardRequest>("{\"persona\":\"Worker\"}", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                ModelScorecard byDefault = await ComputeAsync(db, profile, minimal).ConfigureAwait(false);
                AssertEqual(30, byDefault.WindowDays, "default days");
                AssertEqual(50, byDefault.WindowMissions, "default missions");
                AssertEqual(30, byDefault.Missions, "30 days hold 30 missions");
                AssertEqual("Days", byDefault.WindowLimitedBy, "days bound the default window");
                AssertEqual(_Now.AddDays(-30), byDefault.WindowStartUtc, "start");

                ModelScorecard fewMissions = await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker", WindowMissions = 10 }).ConfigureAwait(false);
                AssertEqual(10, fewMissions.Missions, "mission limit");
                AssertEqual("Missions", fewMissions.WindowLimitedBy, "missions bound the window");
                AssertTrue(fewMissions.OldestCountedUtc.HasValue && fewMissions.OldestCountedUtc.Value > _Now.AddDays(-10), "the newest ten missions are kept");

                ModelScorecard longWindow = await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker", WindowDays = 90 }).ConfigureAwait(false);
                AssertEqual(50, longWindow.Missions, "default mission limit applies in a 90 day window");
                AssertEqual("Missions", longWindow.WindowLimitedBy, "limited by missions");

                ModelScorecard shortWindow = await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker", WindowDays = 5, WindowMissions = 500 }).ConfigureAwait(false);
                AssertEqual(5, shortWindow.Missions, "five days");

                ModelScorecardService service = new ModelScorecardService(db, QuietLogging());
                await AssertThrowsAsync<ArgumentException>(() => service.ComputeAsync(profile, new ModelScorecardRequest(), _Now), "persona is required").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.ComputeAsync(profile, new ModelScorecardRequest { Persona = "Worker", WindowDays = 0 }, _Now), "windowDays 0").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.ComputeAsync(profile, new ModelScorecardRequest { Persona = "Worker", WindowDays = 366 }, _Now), "windowDays 366").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => service.ComputeAsync(profile, new ModelScorecardRequest { Persona = "Worker", WindowMissions = 501 }, _Now), "windowMissions 501").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("revisions_separate_unless_compatible", "Revisions are separate groups; marking them compatible pools them; an outcome matching no revision is its own group", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, "1.4.1").ConfigureAwait(false);
                ModelProfileRevision first = (await db.ModelProfiles.ReadRevisionsAsync(profile.Id).ConfigureAwait(false)).Single();
                ModelProfileRevision second = await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision
                {
                    ProfileId = profile.Id,
                    TenantId = Tenant,
                    RuntimeVersion = "2.0.0",
                    ModelDigest = "sha256:aaa",
                    FirstSeenUtc = _Now.AddDays(-10)
                }).ConfigureAwait(false);

                await AddAsync(db, Outcome("msn_r1a", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 20, o => o.RuntimeVersion = "1.4.2")).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_r1b", 1, StageDispositionEnum.Failed, StageCheckStatusEnum.Failed, 19, o => o.RuntimeVersion = "1.5.0")).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_r2", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 5, o => o.RuntimeVersion = "2.0.1")).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_r3", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 4, o => o.RuntimeVersion = "3.0.0")).ConfigureAwait(false);

                ModelScorecard separate = await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker" }).ConfigureAwait(false);
                AssertEqual(3, separate.Groups.Count, "two revisions plus unmatched");
                AssertEqual(first.Id, separate.Groups[0].RevisionIds.Single(), "oldest revision first");
                AssertFalse(separate.Groups[0].Pooled, "not pooled");
                AssertRate(Metric(separate.Groups[0], "firstPassSuccess"), 1, 2);
                AssertEqual(second.Id, separate.Groups[1].RevisionIds.Single(), "second revision");
                AssertRate(Metric(separate.Groups[1], "firstPassSuccess"), 1, 1);
                AssertEqual(0, separate.Groups[2].RevisionIds.Count, "unmatched group has no revisions");
                AssertEqual(1, separate.Groups[2].Missions, "the 3.x attempt matches no revision");

                await db.ModelProfiles.SetCompatibleWithAsync(first.Id, new List<string> { second.Id }).ConfigureAwait(false);
                ModelScorecard pooled = await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker" }).ConfigureAwait(false);
                AssertEqual(2, pooled.Groups.Count, "pooled revisions plus unmatched");
                AssertTrue(pooled.Groups[0].Pooled, "pooled");
                AssertEqual(2, pooled.Groups[0].RevisionIds.Count, "both revisions");
                AssertRate(Metric(pooled.Groups[0], "firstPassSuccess"), 2, 3);
                AssertEqual(3, pooled.Groups[0].Missions, "pooled missions");
            }));

            cases.Add(CaseAsync("cost_per_accepted_result", "Tokens and wall time per accepted result sum every attempt that led to it (fallback and rework rounds); missing sources are unavailable with a reason", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, "1.4.1").ConfigureAwait(false);

                // No outcomes at all: every rate is 0 of 0 with the whole interval, cost is unavailable.
                ModelScorecardGroup empty = (await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker" }).ConfigureAwait(false)).Groups.Single();
                ModelScorecardMetric emptyFirst = Metric(empty, "firstPassSuccess");
                AssertRate(emptyFirst, 0, 0);
                AssertNull(emptyFirst.Rate, "no rate without trials");
                AssertEqual((double?)0.0, emptyFirst.Lower, "lower");
                AssertEqual((double?)1.0, emptyFirst.Upper, "upper");
                AssertFalse(Metric(empty, "cost.tokensPerAcceptedResult").Available, "no accepted results");
                AssertEqual("No accepted results in the window.", Metric(empty, "cost.tokensPerAcceptedResult").UnavailableReason);

                // Fallback: another model tried first (1000 ms) and escalated; ours finished (2000 ms).
                await AddAsync(db, Outcome("msn_fb", 1, StageDispositionEnum.Escalated, StageCheckStatusEnum.Failed, 6, o =>
                {
                    o.Model = "small-model";
                    o.DurationMs = 1000;
                })).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_fb", 2, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 5.9, o => o.DurationMs = 2000)).ConfigureAwait(false);

                ModelScorecardGroup noTokens = (await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker" }).ConfigureAwait(false)).Groups.Single();
                AssertRate(Metric(noTokens, "acceptedResults"), 1, 1);
                AssertRate(Metric(noTokens, "firstPassSuccess"), 0, 0);
                ModelScorecardMetric missingTokens = Metric(noTokens, "cost.tokensPerAcceptedResult");
                AssertFalse(missingTokens.Available, "no token usage recorded");
                AssertEqual("No token usage is recorded for the accepted results.", missingTokens.UnavailableReason);
                ModelScorecardMetric fallbackTime = Metric(noTokens, "cost.wallTimePerAcceptedResult");
                AssertTrue(fallbackTime.Available, "timing recorded");
                AssertEqual((long?)3000, fallbackTime.Total, "fallback attempt included");

                // Rework: round 0 (ours, 10 s, 1000 tokens) rejected by the Judge; round 1 (ours, 20 s, 500 tokens) accepted.
                await AddAsync(db, Outcome("msn_w0", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 4, o => o.DurationMs = 10000)).ConfigureAwait(false);
                await AddAsync(db, Judge("msn_jr", "msn_w0", "NeedsRevision", 3.9)).ConfigureAwait(false);
                Mission rework = new Mission("Rework 1: slice") { Id = "msn_w1r", Persona = PersonaCatalog.Worker, ReworkRound = 1, DependsOnMissionId = "msn_jr" };
                await db.Missions.CreateAsync(rework).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_w1r", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 3, o =>
                {
                    o.ReworkRound = 1;
                    o.DurationMs = 20000;
                })).ConfigureAwait(false);
                await db.TokenUsage.CreateAsync(Usage("msn_w0", 1000)).ConfigureAwait(false);
                await db.TokenUsage.CreateAsync(Usage("msn_w1r", 400)).ConfigureAwait(false);
                await db.TokenUsage.CreateAsync(Usage("msn_w1r", 100)).ConfigureAwait(false);

                ModelScorecardGroup group = (await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker" }).ConfigureAwait(false)).Groups.Single();
                AssertRate(Metric(group, "acceptedResults"), 2, 3);
                AssertRate(Metric(group, "firstPassSuccess"), 0, 1);
                ModelScorecardMetric tokens = Metric(group, "cost.tokensPerAcceptedResult");
                AssertTrue(tokens.Available, "tokens available");
                AssertEqual((int?)1, tokens.Count, "accepted results with token usage");
                AssertEqual((int?)2, tokens.Of, "accepted results");
                AssertEqual((long?)1500, tokens.Total, "both rework rounds summed");
                AssertEqual((double?)1500.0, tokens.Value, "per accepted result");
                ModelScorecardMetric time = Metric(group, "cost.wallTimePerAcceptedResult");
                AssertEqual((int?)2, time.Count, "both accepted results have timing");
                AssertEqual((long?)33000, time.Total, "3000 for the fallback plus 30000 for the rework chain");
                AssertEqual((double?)16500.0, time.Value, "per accepted result");
                AssertEqual("ms", time.Unit);
                AssertFalse(Metric(group, "toolFailures.kinds").Available, "tool failure kinds are not recorded");
                AssertFalse(Metric(group, "stalls.recovered").Available, "recovered stalls are not recorded");
            }));

            cases.Add(CaseAsync("stage_checks_repairs_and_vessel", "Stage-check counts with skip reasons, protocol repairs, repairs that ran out, tool failures, resumes and stalls; the vessel filter", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProfile profile = await CreateProfileAsync(db, "1.4.1").ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_s1", 1, StageDispositionEnum.Relaunched, StageCheckStatusEnum.Failed, 5)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_s1", 2, StageDispositionEnum.Completed, StageCheckStatusEnum.Passed, 4.9)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_s2", 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Skipped, 4, o => o.StageCheckReason = StageCheckReasonEnum.NoCommands)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_s3", 1, StageDispositionEnum.OutputRepair, StageCheckStatusEnum.CouldNotRun, 3, o => o.ContractRepairKind = ContractRepairKindEnum.CompletionLineMissing)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_s3", 2, StageDispositionEnum.Failed, null, 2.9, o => o.FailureKind = MissionFailureKindEnum.InvalidOutput)).ConfigureAwait(false);
                await AddAsync(db, Outcome("msn_s4", 1, StageDispositionEnum.Failed, null, 2, o =>
                {
                    o.FailureKind = MissionFailureKindEnum.StallRecoveryExhausted;
                    o.SuspectedCause = FailureCauseEnum.ToolUse;
                    o.VesselId = "vsl_other";
                })).ConfigureAwait(false);

                ModelScorecardGroup group = (await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker" }).ConfigureAwait(false)).Groups.Single();
                AssertEqual(6, group.Attempts, "attempts");
                AssertRate(Metric(group, "stageCheck.passed"), 1, 6);
                AssertRate(Metric(group, "stageCheck.failed"), 1, 6);
                AssertRate(Metric(group, "stageCheck.skipped"), 1, 6);
                AssertRate(Metric(group, "stageCheck.skipped.NoCommands"), 1, 1);
                AssertRate(Metric(group, "stageCheck.couldNotRun"), 1, 6);
                AssertRate(Metric(group, "stageCheck.notRecorded"), 2, 6);
                AssertRate(Metric(group, "repairs.protocol"), 1, 6);
                AssertRate(Metric(group, "repairs.exhausted"), 1, 6);
                AssertRate(Metric(group, "toolFailures"), 1, 6);
                AssertRate(Metric(group, "resumesInPlace"), 1, 6);
                AssertRate(Metric(group, "stalls.exhausted"), 1, 6);
                AssertRate(Metric(group, "firstPassSuccess"), 0, 4);

                ModelScorecard vessel = await ComputeAsync(db, profile, new ModelScorecardRequest { Persona = "Worker", VesselId = "vsl_other" }).ConfigureAwait(false);
                AssertEqual("vsl_other", vessel.VesselId, "vessel echoed");
                AssertEqual(1, vessel.Missions, "only the other vessel's mission");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Services: model scorecard",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task<ModelProfile> CreateProfileAsync(DatabaseDriver db, string runtimeVersion)
        {
            ModelProfile profile = new ModelProfile
            {
                TenantId = Tenant,
                Identity = new ModelProfileIdentity { Runtime = "Mux", Provider = "Ollama", Model = ModelName, ServedContextTokens = 131072 }
            };
            profile = await db.ModelProfiles.CreateAsync(profile, ModelProfileRules.IdentityKey(Tenant, profile.Identity)).ConfigureAwait(false);
            await db.ModelProfiles.CreateRevisionAsync(new ModelProfileRevision
            {
                ProfileId = profile.Id,
                TenantId = Tenant,
                RuntimeVersion = runtimeVersion,
                ModelDigest = "sha256:aaa",
                FirstSeenUtc = _Now.AddDays(-100)
            }).ConfigureAwait(false);
            return profile;
        }

        private static StageOutcome Outcome(string missionId, int attempt, StageDispositionEnum disposition, StageCheckStatusEnum? check, double daysAgo, Action<StageOutcome>? change = null)
        {
            DateTime ended = _Now.AddDays(-daysAgo);
            StageOutcome outcome = new StageOutcome
            {
                TenantId = Tenant,
                MissionId = missionId,
                VesselId = "vsl_sc",
                CaptainId = "cpt_small",
                Persona = PersonaCatalog.Worker,
                Attempt = attempt,
                Runtime = AgentRuntimeEnum.Mux,
                ModelProvider = "Ollama",
                Model = ModelName,
                ContextTokens = 131072,
                RuntimeVersion = "1.4.1",
                ModelDigest = "sha256:aaa",
                Disposition = disposition,
                StageCheckStatus = check,
                StartedUtc = ended.AddMinutes(-5),
                EndedUtc = ended,
                CreatedUtc = ended,
                LastUpdateUtc = ended
            };
            change?.Invoke(outcome);
            return outcome;
        }

        private static StageOutcome Judge(string missionId, string reviewedMissionId, string verdict, double daysAgo, Action<StageOutcome>? change = null)
        {
            StageOutcome judge = Outcome(missionId, 1, StageDispositionEnum.Completed, StageCheckStatusEnum.Skipped, daysAgo, o =>
            {
                o.Persona = PersonaCatalog.Judge;
                o.Model = "claude-opus";
                o.Runtime = AgentRuntimeEnum.ClaudeCode;
                o.CaptainId = "cpt_judge";
                o.JudgeVerdict = verdict;
                o.ReviewedMissionId = reviewedMissionId;
                o.StageCheckReason = StageCheckReasonEnum.ReviewPersona;
            });
            change?.Invoke(judge);
            return judge;
        }

        private static TokenUsageRecord Usage(string missionId, long total)
        {
            return new TokenUsageRecord
            {
                TenantId = Tenant,
                Model = ModelName,
                Runtime = "Mux",
                Source = "mission",
                SourceId = missionId,
                InputTokens = total,
                OutputTokens = 0,
                TotalTokens = total,
                CreatedUtc = _Now.AddDays(-1)
            };
        }

        private static async Task AddAsync(DatabaseDriver db, StageOutcome outcome)
        {
            await db.StageOutcomes.CreateAsync(outcome).ConfigureAwait(false);
        }

        private static async Task<ModelScorecard> ComputeAsync(DatabaseDriver db, ModelProfile profile, ModelScorecardRequest request)
        {
            ModelScorecardService service = new ModelScorecardService(db, QuietLogging());
            return await service.ComputeAsync(profile, request, _Now).ConfigureAwait(false);
        }

        private static ModelScorecardMetric Metric(ModelScorecardGroup group, string key)
        {
            ModelScorecardMetric? metric = group.Metrics.FirstOrDefault(m => String.Equals(m.Key, key, StringComparison.Ordinal));
            AssertNotNull(metric, "metric " + key);
            return metric!;
        }

        private static int Removed(ModelScorecard card, string rule)
        {
            ModelScorecardExclusion? exclusion = card.Exclusions.FirstOrDefault(e => String.Equals(e.Rule, rule, StringComparison.Ordinal));
            AssertNotNull(exclusion, "rule " + rule);
            return exclusion!.Removed;
        }

        private static void AssertRate(ModelScorecardMetric metric, int count, int of)
        {
            AssertTrue(metric.Available, metric.Key + " available");
            AssertEqual((int?)count, metric.Count, metric.Key + " count");
            AssertEqual((int?)of, metric.Of, metric.Key + " of");
            AssertEqual((double?)WilsonScoreInterval.Lower(count, of), metric.Lower, metric.Key + " lower");
            AssertEqual((double?)WilsonScoreInterval.Upper(count, of), metric.Upper, metric.Key + " upper");
        }

        private static void AssertThrows<T>(Action action, string label) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            throw new AssertionException(label + ": expected " + typeof(T).Name);
        }

        private static async Task AssertThrowsAsync<T>(Func<Task> action, string label) where T : Exception
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (T)
            {
                return;
            }
            throw new AssertionException(label + ": expected " + typeof(T).Name);
        }

        private static LoggingModule QuietLogging()
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
                tags: new List<string> { tag, TestTags.Database });
        }

        #endregion
    }
}
