namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
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
    /// Descriptors for stage outcomes and failure-cause rules (migration 92, MODEL_HETEROGENEITY.md Part 4): the pure
    /// classifier (rule order, disabled rules, Unknown fallback, a Judge's own rejection, invalid and timed-out patterns,
    /// rule validation), the disposition decided from the mission before and after an exit, built-in rule seeding,
    /// recording an attempt with its provenance, manual classification, and re-applying rules to past outcomes.
    /// </summary>
    public sealed class StageOutcomeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.StageOutcomes";
        private static readonly DateTime _Fixed = new DateTime(2026, 10, 26, 8, 0, 0, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("classify_clean_completion_is_none", "A completed stage with no failure and a passed check is not classified", TestTags.Positive, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Completed);
                outcome.StageCheckStatus = StageCheckStatusEnum.Passed;
                FailureCauseClassification result = FailureCauseClassifier.Classify(outcome, FailureCauseRuleCatalog.Build());
                AssertEqual(FailureCauseEnum.None, result.Cause);
                AssertEqual(FailureCauseSourceEnum.None, result.Source);
                AssertNull(result.RuleId, "rule id");
            }));

            cases.Add(Case("classify_skipped_check_is_not_shortfall", "A completed stage whose check was skipped is not a shortfall", TestTags.Positive, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Completed);
                outcome.StageCheckStatus = StageCheckStatusEnum.Skipped;
                AssertFalse(FailureCauseClassifier.IsShortfall(outcome), "skipped check");
            }));

            cases.Add(Case("classify_could_not_run_is_shortfall", "A completed stage whose check could not run is a shortfall", TestTags.Negative, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Completed);
                outcome.StageCheckStatus = StageCheckStatusEnum.CouldNotRun;
                AssertTrue(FailureCauseClassifier.IsShortfall(outcome), "could not run");
            }));

            cases.Add(Case("classify_pending_is_not_shortfall", "A pending disposition is never classified", TestTags.Positive, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Pending);
                outcome.FailureKind = MissionFailureKindEnum.Compile;
                AssertFalse(FailureCauseClassifier.IsShortfall(outcome), "pending");
            }));

            cases.Add(Case("classify_builtin_compile_is_capability", "A compile failure maps to Capability through the built-in rule", TestTags.Positive, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                outcome.FailureKind = MissionFailureKindEnum.Compile;
                List<FailureCauseRule> rules = FailureCauseRuleCatalog.Build();
                FailureCauseClassification result = FailureCauseClassifier.Classify(outcome, rules);
                AssertEqual(FailureCauseEnum.Capability, result.Cause);
                AssertEqual(FailureCauseSourceEnum.Rule, result.Source);
                AssertEqual(rules.Single(r => r.Name == "Build or tests failed").Id, result.RuleId);
            }));

            cases.Add(Case("classify_lowest_priority_wins", "When several rules match, the lowest priority rule is used", TestTags.Positive, () =>
            {
                // Infra failure that is also Interrupted: priority 10 (NotTheModel by kind) wins over 30.
                StageOutcome outcome = Outcome(StageDispositionEnum.Interrupted);
                outcome.FailureKind = MissionFailureKindEnum.Infra;
                FailureCauseRule late = CustomRule("late", 5000, FailureCauseEnum.Capability, m => m.FailureKinds.Add(MissionFailureKindEnum.Infra));
                FailureCauseRule early = CustomRule("early", 1, FailureCauseEnum.Mixed, m => m.Dispositions.Add(StageDispositionEnum.Interrupted));
                FailureCauseClassification result = FailureCauseClassifier.Classify(outcome, new[] { late, early });
                AssertEqual(FailureCauseEnum.Mixed, result.Cause);
                AssertEqual(early.Id, result.RuleId);
            }));

            cases.Add(Case("classify_priority_tie_breaks_by_name", "Rules with the same priority are ordered by name", TestTags.Positive, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                outcome.FailureKind = MissionFailureKindEnum.TestFail;
                FailureCauseRule b = CustomRule("b rule", 10, FailureCauseEnum.Context, m => m.FailureKinds.Add(MissionFailureKindEnum.TestFail));
                FailureCauseRule a = CustomRule("A rule", 10, FailureCauseEnum.ToolUse, m => m.FailureKinds.Add(MissionFailureKindEnum.TestFail));
                AssertEqual(FailureCauseEnum.ToolUse, FailureCauseClassifier.Classify(outcome, new[] { b, a }).Cause);
            }));

            cases.Add(Case("classify_disabled_rule_ignored_falls_back_to_unknown", "A disabled rule is ignored and an unmatched shortfall is Unknown", TestTags.Negative, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                outcome.FailureKind = MissionFailureKindEnum.Compile;
                FailureCauseRule disabled = CustomRule("off", 1, FailureCauseEnum.Capability, m => m.FailureKinds.Add(MissionFailureKindEnum.Compile));
                disabled.Enabled = false;
                FailureCauseClassification result = FailureCauseClassifier.Classify(outcome, new[] { disabled });
                AssertEqual(FailureCauseEnum.Unknown, result.Cause);
                AssertEqual(FailureCauseSourceEnum.None, result.Source);
                AssertNull(result.RuleId, "rule id");
            }));

            cases.Add(Case("classify_no_rules_is_unknown", "A shortfall with no rules at all is Unknown", TestTags.Negative, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Escalated);
                AssertEqual(FailureCauseEnum.Unknown, FailureCauseClassifier.Classify(outcome, new List<FailureCauseRule>()).Cause);
            }));

            cases.Add(Case("classify_null_arguments_throw", "Classify rejects a null outcome or rule list", TestTags.Negative, () =>
            {
                AssertThrows<ArgumentNullException>(() => FailureCauseClassifier.Classify(null!, new List<FailureCauseRule>()), "null outcome");
                AssertThrows<ArgumentNullException>(() => FailureCauseClassifier.Classify(Outcome(StageDispositionEnum.Failed), null!), "null rules");
                AssertThrows<ArgumentNullException>(() => FailureCauseClassifier.IsShortfall(null!), "null shortfall");
            }));

            cases.Add(Case("classify_judge_rejection_is_not_judge_shortfall", "A Judge that returned FAIL or NEEDS_REVISION did not fall short", TestTags.Positive, () =>
            {
                foreach (string verdict in new[] { nameof(JudgeVerdictEnum.Fail), nameof(JudgeVerdictEnum.NeedsRevision) })
                {
                    StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                    outcome.Persona = PersonaCatalog.Judge;
                    outcome.FailureKind = MissionFailureKindEnum.JudgeRejected;
                    outcome.JudgeVerdict = verdict;
                    AssertFalse(FailureCauseClassifier.IsShortfall(outcome), verdict);
                }
            }));

            cases.Add(Case("classify_judge_rejected_without_verdict_is_shortfall", "JudgeRejected with no parsed verdict is still a shortfall", TestTags.Negative, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                outcome.Persona = PersonaCatalog.Judge;
                outcome.FailureKind = MissionFailureKindEnum.JudgeRejected;
                outcome.JudgeVerdict = nameof(JudgeVerdictEnum.None);
                AssertTrue(FailureCauseClassifier.IsShortfall(outcome), "no verdict");
            }));

            cases.Add(Case("match_every_criterion_must_hold", "A rule matches only when every criterion it sets matches", TestTags.Negative, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                outcome.FailureKind = MissionFailureKindEnum.Compile;
                outcome.Runtime = AgentRuntimeEnum.Codex;
                outcome.Persona = "Worker";
                FailureCauseRuleMatch match = new FailureCauseRuleMatch();
                match.FailureKinds.Add(MissionFailureKindEnum.Compile);
                match.Personas.Add(" worker ");
                AssertTrue(FailureCauseClassifier.Matches(match, outcome), "kind and persona (trimmed, any case)");
                match.Runtimes.Add(AgentRuntimeEnum.ClaudeCode);
                AssertFalse(FailureCauseClassifier.Matches(match, outcome), "runtime differs");
                outcome.Runtime = null;
                AssertFalse(FailureCauseClassifier.Matches(match, outcome), "runtime missing");
            }));

            cases.Add(Case("match_empty_or_null_never_matches", "A match with no criteria, or a null match or outcome, matches nothing", TestTags.Negative, () =>
            {
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                AssertFalse(FailureCauseClassifier.Matches(new FailureCauseRuleMatch(), outcome), "empty");
                AssertFalse(FailureCauseClassifier.Matches(null, outcome), "null match");
                FailureCauseRuleMatch match = new FailureCauseRuleMatch();
                match.Dispositions.Add(StageDispositionEnum.Failed);
                AssertFalse(FailureCauseClassifier.Matches(match, null), "null outcome");
                match.TextPattern = "   ";
                AssertTrue(FailureCauseClassifier.Matches(match, outcome), "whitespace pattern is no criterion");
            }));

            cases.Add(Case("match_text_pattern", "A text pattern matches the failure reason case-insensitively and needs a reason", TestTags.Positive, () =>
            {
                FailureCauseRuleMatch match = new FailureCauseRuleMatch();
                match.TextPattern = "maximum context length";
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                AssertFalse(FailureCauseClassifier.Matches(match, outcome), "no reason");
                outcome.FailureReason = "Error: This model's MAXIMUM CONTEXT LENGTH is 32768 tokens";
                AssertTrue(FailureCauseClassifier.Matches(match, outcome), "matches");
                AssertEqual(FailureCauseEnum.Context, FailureCauseClassifier.Classify(outcome, FailureCauseRuleCatalog.Build()).Cause);
            }));

            cases.Add(Case("match_invalid_pattern_never_matches", "An invalid stored pattern matches nothing instead of throwing", TestTags.Negative, () =>
            {
                FailureCauseRuleMatch match = new FailureCauseRuleMatch();
                match.TextPattern = "([unclosed";
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                outcome.FailureReason = "([unclosed";
                AssertFalse(FailureCauseClassifier.Matches(match, outcome), "invalid pattern");
            }));

            cases.Add(Case("match_pattern_timeout_never_matches", "A catastrophic pattern times out and matches nothing", TestTags.Negative, () =>
            {
                FailureCauseRuleMatch match = new FailureCauseRuleMatch();
                match.TextPattern = "^(a+)+$";
                StageOutcome outcome = Outcome(StageDispositionEnum.Failed);
                outcome.FailureReason = new string('a', 5000) + "!";
                DateTime start = DateTime.UtcNow;
                AssertFalse(FailureCauseClassifier.Matches(match, outcome), "timed out");
                AssertTrue(DateTime.UtcNow - start < TimeSpan.FromSeconds(10), "bounded by the pattern timeout");
            }));

            cases.Add(Case("validate_rules", "Validate accepts a good rule and names what is wrong with a bad one", TestTags.Negative, () =>
            {
                FailureCauseRule good = CustomRule("ok", 1, FailureCauseEnum.Protocol, m => m.TextPattern = "marker");
                AssertNull(FailureCauseClassifier.Validate(good), "valid");
                AssertNotNull(FailureCauseClassifier.Validate(null), "null rule");

                FailureCauseRule noName = CustomRule(" ", 1, FailureCauseEnum.Protocol, m => m.TextPattern = "x");
                AssertContains("Name", FailureCauseClassifier.Validate(noName)!);

                FailureCauseRule longName = CustomRule(new string('n', 201), 1, FailureCauseEnum.Protocol, m => m.TextPattern = "x");
                AssertContains("200", FailureCauseClassifier.Validate(longName)!);

                FailureCauseRule noCriteria = CustomRule("empty", 1, FailureCauseEnum.Protocol, m => { });
                AssertContains("criterion", FailureCauseClassifier.Validate(noCriteria)!);

                FailureCauseRule none = CustomRule("none", 1, FailureCauseEnum.None, m => m.TextPattern = "x");
                AssertContains("None", FailureCauseClassifier.Validate(none)!);

                FailureCauseRule undefinedCause = CustomRule("undefined", 1, (FailureCauseEnum)999, m => m.TextPattern = "x");
                AssertContains("known", FailureCauseClassifier.Validate(undefinedCause)!);

                FailureCauseRule badPattern = CustomRule("bad", 1, FailureCauseEnum.Protocol, m => m.TextPattern = "(");
                AssertContains("regular expression", FailureCauseClassifier.Validate(badPattern)!);

                FailureCauseRule longPattern = CustomRule("long", 1, FailureCauseEnum.Protocol, m => m.TextPattern = new string('a', 1001));
                AssertContains("1000", FailureCauseClassifier.Validate(longPattern)!);
            }));

            cases.Add(Case("catalog_rules_are_valid_and_unique", "Every built-in rule validates, is built in, and has a unique name", TestTags.Positive, () =>
            {
                List<FailureCauseRule> rules = FailureCauseRuleCatalog.Build();
                AssertTrue(rules.Count > 0, "has rules");
                foreach (FailureCauseRule rule in rules)
                {
                    AssertNull(FailureCauseClassifier.Validate(rule), rule.Name);
                    AssertTrue(rule.IsBuiltIn, rule.Name + " built in");
                    AssertNull(rule.TenantId, rule.Name + " tenant");
                }
                AssertEqual(rules.Count, rules.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "unique names");
            }));

            cases.Add(Case("disposition_from_mission_state", "The disposition reflects what Armada did with the attempt", TestTags.Positive, () =>
            {
                AssertEqual(StageDispositionEnum.Cancelled, Decide(MissionStatusEnum.Cancelled));
                AssertEqual(StageDispositionEnum.Failed, Decide(MissionStatusEnum.Failed));
                AssertEqual(StageDispositionEnum.Failed, Decide(MissionStatusEnum.LandingFailed));
                AssertEqual(StageDispositionEnum.Completed, Decide(MissionStatusEnum.WorkProduced));
                AssertEqual(StageDispositionEnum.Completed, Decide(MissionStatusEnum.Complete));
                AssertEqual(StageDispositionEnum.Relaunched, Decide(MissionStatusEnum.Pending, exitCode: 1));
                AssertEqual(StageDispositionEnum.Interrupted, Decide(MissionStatusEnum.Pending, exitCode: -1));
                AssertEqual(StageDispositionEnum.OutputRepair, Decide(MissionStatusEnum.InProgress, outputRepairs: 1));
                AssertEqual(StageDispositionEnum.Interrupted, Decide(MissionStatusEnum.Assigned, exitCode: -1, redispatches: 1));
            }));

            cases.Add(Case("disposition_failed_wins_over_repair_and_escalation", "A failed mission is Failed even when other counters moved", TestTags.Negative, () =>
            {
                AssertEqual(StageDispositionEnum.Failed, Decide(MissionStatusEnum.Failed, outputRepairs: 1, nextCaptain: "cpt_other"));
            }));

            cases.Add(Case("disposition_escalated_records_next_captain", "A mission now held by another captain is Escalated with that captain", TestTags.Positive, () =>
            {
                StageAttemptCapture capture = Capture(exitCode: 1);
                Mission after = new Mission("m", "d") { Status = MissionStatusEnum.Assigned, CaptainId = "cpt_strong" };
                StageOutcome outcome = new StageOutcome();
                StageOutcomeService.DecideDisposition(capture, after, outcome);
                AssertEqual(StageDispositionEnum.Escalated, outcome.Disposition);
                AssertEqual("cpt_strong", outcome.NextCaptainId);
            }));

            cases.Add(Case("disposition_failure_reason_from_blocking_check", "The failure reason falls back to a blocking stage check message, truncated", TestTags.Negative, () =>
            {
                StageAttemptCapture capture = Capture(exitCode: 0);
                capture.StageCheck = new StageCheckResult { Status = StageCheckStatusEnum.Failed, FailureMessage = new string('x', StageOutcomeService.MaxFailureReasonLength + 50) };
                Mission after = new Mission("m", "d") { Status = MissionStatusEnum.InProgress, CaptainId = "cpt_self" };
                StageOutcome outcome = new StageOutcome();
                StageOutcomeService.DecideDisposition(capture, after, outcome);
                if (capture.StageCheck.IsBlocking)
                {
                    AssertNotNull(outcome.FailureReason, "reason");
                    AssertEqual(StageOutcomeService.MaxFailureReasonLength, outcome.FailureReason!.Length, "truncated");
                }

                after.FailureReason = "mission reason";
                StageOutcomeService.DecideDisposition(capture, after, outcome);
                AssertEqual("mission reason", outcome.FailureReason, "mission reason preferred");
            }));

            cases.Add(CaseAsync("seed_builtin_rules_is_idempotent_and_keeps_edits", "Seeding adds missing built-in rules once and leaves edited ones alone", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging());
                int expected = FailureCauseRuleCatalog.Build().Count;

                AssertEqual(expected, await service.EnsureBuiltInRulesAsync().ConfigureAwait(false), "first seed");
                AssertEqual(0, await service.EnsureBuiltInRulesAsync().ConfigureAwait(false), "second seed");

                List<FailureCauseRule> stored = await db.FailureCauseRules.EnumerateAsync(null).ConfigureAwait(false);
                FailureCauseRule edited = stored.Single(r => r.Name == "Build or tests failed");
                edited.Cause = FailureCauseEnum.Mixed;
                edited.Enabled = false;
                await db.FailureCauseRules.UpdateAsync(edited).ConfigureAwait(false);

                FailureCauseRule deleted = stored.Single(r => r.Name == "Context exhausted");
                AssertTrue(await db.FailureCauseRules.DeleteAsync(deleted.Id).ConfigureAwait(false), "deleted");

                AssertEqual(1, await service.EnsureBuiltInRulesAsync().ConfigureAwait(false), "restores the deleted rule only");
                FailureCauseRule? reread = await db.FailureCauseRules.ReadAsync(edited.Id).ConfigureAwait(false);
                AssertNotNull(reread, "edited rule");
                AssertEqual(FailureCauseEnum.Mixed, reread!.Cause, "edit kept");
                AssertFalse(reread.Enabled, "disable kept");
                AssertEqual(expected, (await db.FailureCauseRules.EnumerateAsync(null).ConfigureAwait(false)).Count, "no duplicates");
            }));

            cases.Add(CaseAsync("rule_roundtrip_and_tenant_listing", "A rule round-trips its match criteria and tenant listing includes global rules", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                FailureCauseRule rule = CustomRule("tenant rule", 7, FailureCauseEnum.ToolUse, m =>
                {
                    m.Personas.Add("Worker");
                    m.Runtimes.Add(AgentRuntimeEnum.Codex);
                    m.FailureKinds.Add(MissionFailureKindEnum.Compile);
                    m.RuntimeFailureKinds.Add(RuntimeFailureKindEnum.UsageLimit);
                    m.StageCheckStatuses.Add(StageCheckStatusEnum.Failed);
                    m.StageCheckReasons.Add(StageCheckReasonEnum.HarborDisconnected);
                    m.Dispositions.Add(StageDispositionEnum.Escalated);
                    m.TextPattern = "tool";
                });
                rule.TenantId = "ten_a";
                rule.Description = "desc";
                rule.CreatedByUserId = "usr_a";
                await db.FailureCauseRules.CreateAsync(rule).ConfigureAwait(false);
                FailureCauseRule global = CustomRule("global rule", 8, FailureCauseEnum.Context, m => m.TextPattern = "ctx");
                await db.FailureCauseRules.CreateAsync(global).ConfigureAwait(false);
                FailureCauseRule other = CustomRule("other tenant", 9, FailureCauseEnum.Context, m => m.TextPattern = "ctx");
                other.TenantId = "ten_b";
                await db.FailureCauseRules.CreateAsync(other).ConfigureAwait(false);

                FailureCauseRule? read = await db.FailureCauseRules.ReadAsync(rule.Id).ConfigureAwait(false);
                AssertNotNull(read, "read");
                AssertEqual("ten_a", read!.TenantId);
                AssertEqual("tenant rule", read.Name);
                AssertEqual("desc", read.Description);
                AssertEqual(7, read.Priority);
                AssertEqual(FailureCauseEnum.ToolUse, read.Cause);
                AssertEqual("usr_a", read.CreatedByUserId);
                AssertEqual("Worker", read.Match.Personas.Single());
                AssertEqual(AgentRuntimeEnum.Codex, read.Match.Runtimes.Single());
                AssertEqual(MissionFailureKindEnum.Compile, read.Match.FailureKinds.Single());
                AssertEqual(RuntimeFailureKindEnum.UsageLimit, read.Match.RuntimeFailureKinds.Single());
                AssertEqual(StageCheckStatusEnum.Failed, read.Match.StageCheckStatuses.Single());
                AssertEqual(StageCheckReasonEnum.HarborDisconnected, read.Match.StageCheckReasons.Single());
                AssertEqual(StageDispositionEnum.Escalated, read.Match.Dispositions.Single());
                AssertEqual("tool", read.Match.TextPattern);

                List<string> forA = (await db.FailureCauseRules.EnumerateAsync("ten_a").ConfigureAwait(false)).Select(r => r.Name).ToList();
                AssertTrue(forA.Contains("tenant rule") && forA.Contains("global rule"), "tenant and global");
                AssertFalse(forA.Contains("other tenant"), "other tenant excluded");
                AssertNull(await db.FailureCauseRules.ReadAsync("fcr_missing").ConfigureAwait(false), "missing rule");
                AssertFalse(await db.FailureCauseRules.DeleteAsync("fcr_missing").ConfigureAwait(false), "delete missing");
            }));

            cases.Add(CaseAsync("record_attempt_provenance_and_classification", "Recording an attempt stores provenance, disposition, attempt number and a rule cause", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging());
                await service.EnsureBuiltInRulesAsync().ConfigureAwait(false);

                Captain captain = new Captain("qwen worker", AgentRuntimeEnum.Codex);
                captain.Model = "qwen3-coder:30b";
                await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                Mission mission = await CreateMissionAsync(db, MissionStatusEnum.Failed, MissionFailureKindEnum.Compile, "error CS1026: ) expected").ConfigureAwait(false);

                StageAttemptCapture capture = Capture(exitCode: 1);
                capture.MissionId = mission.Id;
                capture.Captain = captain;
                capture.StartedUtc = _Fixed;
                capture.EndedUtc = _Fixed.AddMinutes(3);
                capture.StageCheck = new StageCheckResult { Status = StageCheckStatusEnum.Failed, Reason = StageCheckReasonEnum.None };

                StageOutcome? first = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertNotNull(first, "recorded");
                AssertEqual(1, first!.Attempt, "first attempt");
                AssertEqual(StageDispositionEnum.Failed, first.Disposition);
                AssertEqual(FailureCauseEnum.Capability, first.SuspectedCause);
                AssertEqual(FailureCauseSourceEnum.Rule, first.CauseSource);
                AssertNotNull(first.CauseRuleId, "rule id");
                AssertEqual(180000L, first.DurationMs, "duration");
                AssertEqual("Worker", first.Persona, "default persona");
                AssertNotNull(first.PromptVersion, "prompt version");
                AssertNotNull(first.ArmadaVersion, "armada version");

                StageOutcome? read = await db.StageOutcomes.ReadAsync(first.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertEqual(mission.Id, read!.MissionId);
                AssertEqual(mission.VesselId, read.VesselId);
                AssertEqual(mission.VoyageId, read.VoyageId);
                AssertEqual(captain.Id, read.CaptainId);
                AssertEqual("qwen worker", read.CaptainName);
                AssertEqual(AgentRuntimeEnum.Codex, read.Runtime);
                AssertEqual("qwen3-coder:30b", read.Model);
                AssertEqual(1, read.ExitCode);
                AssertEqual(StageCheckStatusEnum.Failed, read.StageCheckStatus);
                AssertEqual(MissionFailureKindEnum.Compile, read.FailureKind);
                AssertEqual("error CS1026: ) expected", read.FailureReason);
                AssertEqual(FailureCauseEnum.Capability, read.SuspectedCause);
                AssertEqual(first.CauseRuleId, read.CauseRuleId);
                AssertFalse(read.IsEvaluation, "not an evaluation");

                StageOutcome? second = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual(2, second!.Attempt, "second attempt");
                AssertEqual(2, await db.StageOutcomes.CountByMissionAsync(mission.Id).ConfigureAwait(false), "count");

                EnumerationResult<StageOutcome> byCause = await db.StageOutcomes.EnumerateAsync(new StageOutcomeQuery { MissionId = mission.Id, SuspectedCause = FailureCauseEnum.Capability }).ConfigureAwait(false);
                AssertEqual(2, byCause.Objects.Count, "filter by cause");
                EnumerationResult<StageOutcome> none = await db.StageOutcomes.EnumerateAsync(new StageOutcomeQuery { MissionId = mission.Id, SuspectedCause = FailureCauseEnum.Context }).ConfigureAwait(false);
                AssertEqual(0, none.Objects.Count, "filter excludes");

                AssertEqual(2, await db.StageOutcomes.DeleteByMissionAsync(mission.Id).ConfigureAwait(false), "delete by mission");
                AssertEqual(0, await db.StageOutcomes.CountByMissionAsync(mission.Id).ConfigureAwait(false), "deleted");
            }));

            cases.Add(CaseAsync("record_attempt_skips_unclaimed_and_missing", "Unclaimed captures, missing captains and missing missions record nothing", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging());
                Captain captain = new Captain("c");

                StageAttemptCapture unclaimed = Capture(exitCode: 0);
                unclaimed.Claimed = false;
                unclaimed.MissionId = "msn_x";
                unclaimed.Captain = captain;
                AssertNull(await service.RecordAttemptAsync(unclaimed).ConfigureAwait(false), "unclaimed");

                StageAttemptCapture noCaptain = Capture(exitCode: 0);
                noCaptain.MissionId = "msn_x";
                AssertNull(await service.RecordAttemptAsync(noCaptain).ConfigureAwait(false), "no captain");

                StageAttemptCapture missing = Capture(exitCode: 0);
                missing.MissionId = "msn_does_not_exist";
                missing.Captain = captain;
                AssertNull(await service.RecordAttemptAsync(missing).ConfigureAwait(false), "missing mission");

                await AssertThrowsAsync<ArgumentNullException>(() => service.RecordAttemptAsync(null!), "null capture").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("record_attempt_contract_repair_kind", "An attempt records the latest contract repair written since it started; earlier, unreadable or unbounded repairs are not attributed", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging());
                Captain captain = new Captain("repaired worker");
                await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                Mission mission = await CreateMissionAsync(db, MissionStatusEnum.WorkProduced, null, null).ConfigureAwait(false);

                // A repair of an earlier attempt is outside this attempt's window.
                await CreateRepairEventAsync(db, mission, ContractRepairKindEnum.VerdictNonCanonical, _Fixed.AddMinutes(-5)).ConfigureAwait(false);
                StageAttemptCapture capture = Capture(exitCode: 0);
                capture.MissionId = mission.Id;
                capture.Captain = captain;
                capture.StartedUtc = _Fixed;
                capture.EndedUtc = _Fixed.AddMinutes(3);
                StageOutcome? clean = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertNotNull(clean, "recorded without a repair");
                AssertNull(clean!.ContractRepairKind, "an earlier attempt's repair is not attributed");
                AssertNull((await db.StageOutcomes.ReadAsync(clean.Id).ConfigureAwait(false))!.ContractRepairKind, "null round-trips");

                // Two repairs in one attempt: the last one recorded is kept on the outcome.
                await CreateRepairEventAsync(db, mission, ContractRepairKindEnum.CommitMissingRecoveredFromRepo, _Fixed.AddMinutes(1)).ConfigureAwait(false);
                await CreateRepairEventAsync(db, mission, ContractRepairKindEnum.CompletionLineMissing, _Fixed.AddMinutes(2)).ConfigureAwait(false);
                StageOutcome? repaired = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual(ContractRepairKindEnum.CompletionLineMissing, repaired!.ContractRepairKind, "latest repair of the attempt");
                StageOutcome? read = await db.StageOutcomes.ReadAsync(repaired.Id).ConfigureAwait(false);
                AssertEqual(ContractRepairKindEnum.CompletionLineMissing, read!.ContractRepairKind, "stored and read back");

                // Without a start time the attempt window cannot be bounded, so nothing is attributed.
                StageAttemptCapture unbounded = Capture(exitCode: 0);
                unbounded.MissionId = mission.Id;
                unbounded.Captain = captain;
                StageOutcome? noStart = await service.RecordAttemptAsync(unbounded).ConfigureAwait(false);
                AssertNotNull(noStart, "still recorded");
                AssertNull(noStart!.ContractRepairKind, "no start time, no attribution");

                // An unreadable latest repair event leaves the kind null and the outcome is still recorded.
                ArmadaEvent broken = new ArmadaEvent(ContractRepair.EventType, "broken");
                broken.MissionId = mission.Id;
                broken.EntityType = "mission";
                broken.EntityId = mission.Id;
                broken.Payload = "{not json";
                broken.CreatedUtc = _Fixed.AddMinutes(4);
                await db.Events.CreateAsync(broken).ConfigureAwait(false);
                StageOutcome? unreadable = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertNotNull(unreadable, "recorded despite an unreadable repair event");
                AssertNull(unreadable!.ContractRepairKind, "unreadable payload is not attributed");
            }));

            cases.Add(CaseAsync("contract_repair_kind_persists_and_serializes", "ContractRepairKind round-trips every kind and null through the store, and defaults to null from minimal JSON", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Mission mission = await CreateMissionAsync(db, MissionStatusEnum.WorkProduced, null, null).ConfigureAwait(false);

                StageOutcome outcome = new StageOutcome { MissionId = mission.Id, TenantId = mission.TenantId };
                StageOutcome created = await db.StageOutcomes.CreateAsync(outcome).ConfigureAwait(false);
                AssertNull((await db.StageOutcomes.ReadAsync(created.Id).ConfigureAwait(false))!.ContractRepairKind, "default null stored");
                foreach (ContractRepairKindEnum kind in Enum.GetValues(typeof(ContractRepairKindEnum)))
                {
                    created.ContractRepairKind = kind;
                    await db.StageOutcomes.UpdateAsync(created).ConfigureAwait(false);
                    AssertEqual(kind, (await db.StageOutcomes.ReadAsync(created.Id).ConfigureAwait(false))!.ContractRepairKind, "round-trips " + kind);
                }
                created.ContractRepairKind = null;
                await db.StageOutcomes.UpdateAsync(created).ConfigureAwait(false);
                AssertNull((await db.StageOutcomes.ReadAsync(created.Id).ConfigureAwait(false))!.ContractRepairKind, "cleared back to null");

                StageOutcome minimal = JsonSerializer.Deserialize<StageOutcome>("{\"MissionId\":\"msn_x\"}")!;
                AssertNull(minimal.ContractRepairKind, "minimal JSON defaults to null");
                string json = JsonSerializer.Serialize(new StageOutcome { ContractRepairKind = ContractRepairKindEnum.PlanBlockNonCanonical });
                AssertContains("\"ContractRepairKind\":\"PlanBlockNonCanonical\"", json, "serialized by name");
                AssertEqual(ContractRepairKindEnum.PlanBlockNonCanonical, JsonSerializer.Deserialize<StageOutcome>(json)!.ContractRepairKind, "deserialized by name");
            }));

            cases.Add(CaseAsync("manual_classification_survives_reclassify", "A manual cause is kept by re-application, and clearing it hands it back to the rules", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging());
                await service.EnsureBuiltInRulesAsync().ConfigureAwait(false);
                StageOutcome stored = await db.StageOutcomes.CreateAsync(StoredOutcome("msn_manual", MissionFailureKindEnum.Compile)).ConfigureAwait(false);

                StageOutcome? manual = await service.ClassifyManuallyAsync(stored.Id, FailureCauseEnum.Context, new string('n', 2100), "usr_admin").ConfigureAwait(false);
                AssertNotNull(manual, "classified");
                AssertEqual(FailureCauseEnum.Context, manual!.SuspectedCause);
                AssertEqual(FailureCauseSourceEnum.Manual, manual.CauseSource);
                AssertNull(manual.CauseRuleId, "no rule");
                AssertEqual("usr_admin", manual.ClassifiedByUserId);
                AssertEqual(StageOutcomeService.MaxFailureReasonLength, manual.CauseNote!.Length, "note truncated");

                StageOutcomeReclassifyResult result = await service.ReclassifyAsync(new StageOutcomeQuery { MissionId = "msn_manual" }).ConfigureAwait(false);
                AssertEqual(1, result.Examined);
                AssertEqual(1, result.SkippedManual);
                AssertEqual(0, result.Changed);
                AssertEqual(FailureCauseEnum.Context, (await db.StageOutcomes.ReadAsync(stored.Id).ConfigureAwait(false))!.SuspectedCause, "kept");

                StageOutcome? cleared = await service.ClassifyManuallyAsync(stored.Id, null, null, "usr_admin").ConfigureAwait(false);
                AssertEqual(FailureCauseEnum.Capability, cleared!.SuspectedCause, "rules re-applied");
                AssertEqual(FailureCauseSourceEnum.Rule, cleared.CauseSource);
                AssertNull(cleared.ClassifiedByUserId, "user cleared");

                AssertNull(await service.ClassifyManuallyAsync("sto_missing", FailureCauseEnum.Context, null, null).ConfigureAwait(false), "missing outcome");
                await AssertThrowsAsync<ArgumentNullException>(() => service.ClassifyManuallyAsync("", FailureCauseEnum.Context, null, null), "empty id").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("reclassify_applies_new_rule_to_past_outcomes", "A new rule re-applied to past Unknown outcomes changes them, and a second pass changes nothing", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging());

                for (int i = 0; i < 3; i++)
                {
                    StageOutcome o = StoredOutcome("msn_re", null);
                    o.Disposition = StageDispositionEnum.Escalated;
                    o.FailureReason = "lost track of the task after compaction";
                    o.SuspectedCause = FailureCauseEnum.Unknown;
                    await db.StageOutcomes.CreateAsync(o).ConfigureAwait(false);
                }

                FailureCauseRule rule = CustomRule("compaction", 5, FailureCauseEnum.Context, m => m.TextPattern = "compaction");
                await db.FailureCauseRules.CreateAsync(rule).ConfigureAwait(false);

                StageOutcomeReclassifyResult first = await service.ReclassifyAsync(new StageOutcomeQuery { MissionId = "msn_re", SuspectedCause = FailureCauseEnum.Unknown }).ConfigureAwait(false);
                AssertEqual(3, first.Examined, "examined all, despite the filter on the cause being changed");
                AssertEqual(3, first.Changed);
                EnumerationResult<StageOutcome> after = await db.StageOutcomes.EnumerateAsync(new StageOutcomeQuery { MissionId = "msn_re" }).ConfigureAwait(false);
                AssertTrue(after.Objects.All(o => o.SuspectedCause == FailureCauseEnum.Context && o.CauseRuleId == rule.Id), "all Context");

                StageOutcomeReclassifyResult second = await service.ReclassifyAsync(new StageOutcomeQuery { MissionId = "msn_re" }).ConfigureAwait(false);
                AssertEqual(0, second.Changed, "idempotent");
                await AssertThrowsAsync<ArgumentNullException>(() => service.ReclassifyAsync(null!), "null filter").ConfigureAwait(false);
            }));

            cases.Add(Case("disposition_escalated_from_capture_while_pending", "An exit the handler escalated is Escalated with the new captain and the escalated commit, though the mission is Pending", TestTags.Positive, () =>
            {
                StageAttemptCapture capture = Capture(exitCode: 1);
                Mission after = new Mission("m", "d") { Status = MissionStatusEnum.Pending, CaptainId = null, RequestedCaptainId = "cpt_strong" };
                StageOutcome outcome = new StageOutcome();
                StageOutcomeService.DecideDisposition(capture, after, outcome);
                AssertEqual(StageDispositionEnum.Relaunched, outcome.Disposition, "without the capture's escalation a Pending mission reads as relaunched");
                AssertNull(outcome.CommitHash, "no commit when not escalated or completed");

                capture.EscalatedToCaptainId = "cpt_strong";
                capture.EscalatedCommit = "abc123";
                StageOutcomeService.DecideDisposition(capture, after, outcome);
                AssertEqual(StageDispositionEnum.Escalated, outcome.Disposition);
                AssertEqual("cpt_strong", outcome.NextCaptainId);
                AssertEqual("abc123", outcome.CommitHash);

                Mission done = new Mission("m", "d") { Status = MissionStatusEnum.Complete, CaptainId = "cpt_self", CommitHash = "def456" };
                StageOutcome completed = new StageOutcome();
                StageOutcomeService.DecideDisposition(Capture(exitCode: 0), done, completed);
                AssertEqual(StageDispositionEnum.Completed, completed.Disposition);
                AssertEqual("def456", completed.CommitHash, "a completed attempt records its produced commit");
            }));

            cases.Add(CaseAsync("provenance_recorded_and_cached_per_captain_and_harbor", "Runtime version and model digest are recorded, and a cached value is reused per captain and Harbor", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelEndpoint endpoint = new ModelEndpoint { Name = "ollama", Provider = ModelProviderEnum.Ollama, BaseUrl = "http://ollama.invalid:11434", Model = "qwen3:8b" };
                await db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
                Captain captain = new Captain("mux worker", AgentRuntimeEnum.Mux);
                captain.ModelEndpointId = endpoint.Id;
                await db.Captains.CreateAsync(captain).ConfigureAwait(false);

                int versionCalls = 0;
                int digestCalls = 0;
                List<string?> harbors = new List<string?>();
                StageProvenanceProbe probe = new StageProvenanceProbe(CreateLogging());
                probe.RuntimeVersionProbe = (c, harborId, ep, ct) =>
                {
                    Interlocked.Increment(ref versionCalls);
                    lock (harbors) harbors.Add(harborId);
                    return Task.FromResult<string?>("mux v1.5.0\nbuild 42");
                };
                probe.ModelDigestProbe = (ep, model, harborId, ct) =>
                {
                    Interlocked.Increment(ref digestCalls);
                    return Task.FromResult<string?>("sha256-" + model);
                };
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging(), probe);

                Mission mission = await CreateMissionAsync(db, MissionStatusEnum.InProgress, null, null).ConfigureAwait(false);
                StageAttemptCapture capture = Capture(exitCode: 1);
                capture.MissionId = mission.Id;
                capture.Captain = captain;

                StageOutcome? first = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertNotNull(first, "recorded");
                AssertEqual("mux v1.5.0", first!.RuntimeVersion, "first line of the version");
                AssertEqual("sha256-qwen3:8b", first.ModelDigest, "endpoint model digest");
                StageOutcome? read = await db.StageOutcomes.ReadAsync(first.Id).ConfigureAwait(false);
                AssertEqual("mux v1.5.0", read!.RuntimeVersion, "stored version");
                AssertEqual("sha256-qwen3:8b", read.ModelDigest, "stored digest");

                StageOutcome? second = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual("mux v1.5.0", second!.RuntimeVersion, "cached version");
                AssertEqual(1, versionCalls, "version probed once for the captain on this host");
                AssertEqual(1, digestCalls, "digest probed once");

                Dock harborDock = new Dock(mission.VesselId!) { HarborId = "hbr_laptop" };
                await db.Docks.CreateAsync(harborDock).ConfigureAwait(false);
                capture.DockId = harborDock.Id;
                StageOutcome? third = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual("hbr_laptop", third!.HarborId, "harbor");
                AssertEqual(2, versionCalls, "a different Harbor is probed separately");
                AssertEqual("hbr_laptop", harbors[1], "the probe is told the Harbor");

                probe.CacheDuration = TimeSpan.Zero;
                await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual(3, versionCalls, "an expired value is probed again");
            }));

            cases.Add(CaseAsync("provenance_cancelled_wait_retains_background_result", "Cancelling a wait does not cancel the shared probe or lose its late result", TestTags.Negative, async () =>
            {
                StageProvenanceProbe probe = new StageProvenanceProbe(CreateLogging());
                TaskCompletionSource<string?> result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                TaskCompletionSource<bool> entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                int calls = 0;
                CancellationToken backgroundToken = default;
                probe.RuntimeVersionProbe = (c, h, ep, ct) =>
                {
                    Interlocked.Increment(ref calls);
                    backgroundToken = ct;
                    entered.TrySetResult(true);
                    return result.Task;
                };
                Captain captain = new Captain("cancelled waiter", AgentRuntimeEnum.Mux);
                using CancellationTokenSource caller = new CancellationTokenSource();
                Task<string?> waiting = probe.GetRuntimeVersionAsync(captain, null, null, caller.Token);
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    caller.Cancel();
                    AssertNull(await waiting.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false), "cancelled wait returns null promptly");
                    AssertEqual(false, backgroundToken.IsCancellationRequested, "caller cannot cancel the shared probe");
                    AssertNull(await probe.GetRuntimeVersionAsync(captain, null, null).ConfigureAwait(false), "in-flight probe is not waited for twice");
                    AssertEqual(1, calls, "in-flight probe reused");
                    result.TrySetResult("mux late-version");
                    string? cached = null;
                    Stopwatch deadline = Stopwatch.StartNew();
                    while (cached == null && deadline.Elapsed < TimeSpan.FromSeconds(3))
                    {
                        cached = await probe.GetRuntimeVersionAsync(captain, null, null).ConfigureAwait(false);
                        if (cached == null) await Task.Delay(10).ConfigureAwait(false);
                    }
                    AssertEqual("mux late-version", cached, "late result becomes available in cache");
                    AssertEqual(1, calls, "late value reused without another probe");
                    probe.ClearCache();
                    AssertEqual("mux late-version", await probe.GetRuntimeVersionAsync(captain, null, null).ConfigureAwait(false), "explicit invalidation re-probes");
                    AssertEqual(2, calls, "clear cache invalidates the completed probe");
                }
                finally
                {
                    result.TrySetResult(null);
                }
            }));

            cases.Add(CaseAsync("provenance_digest_cache_isolates_model_endpoint_and_host", "Digest cache separates model, endpoint URL and Harbor and bounds returned values", TestTags.Positive, async () =>
            {
                StageProvenanceProbe probe = new StageProvenanceProbe(CreateLogging());
                int calls = 0;
                probe.ModelDigestProbe = (ep, model, h, ct) =>
                {
                    Interlocked.Increment(ref calls);
                    return Task.FromResult<string?>(new string('a', StageProvenanceProbe.MaxValueLength + 20) + "\nignored");
                };
                ModelEndpoint endpoint = new ModelEndpoint { Provider = ModelProviderEnum.Ollama, BaseUrl = "http://ollama.invalid" };
                string expected = new string('a', StageProvenanceProbe.MaxValueLength);
                AssertEqual(expected, await probe.GetModelDigestAsync(endpoint, "model-a", null).ConfigureAwait(false), "value length bounded");
                AssertEqual(expected, await probe.GetModelDigestAsync(endpoint, "model-a", null).ConfigureAwait(false), "cache reused");
                AssertEqual(1, calls, "same key uses cache");
                await probe.GetModelDigestAsync(endpoint, "model-b", null).ConfigureAwait(false);
                await probe.GetModelDigestAsync(endpoint, "model-a", "hbr_other").ConfigureAwait(false);
                endpoint.BaseUrl = "http://replacement.invalid";
                await probe.GetModelDigestAsync(endpoint, "model-a", null).ConfigureAwait(false);
                AssertEqual(4, calls, "different models, hosts and endpoint URLs cannot share a cached digest");
                foreach (string? absent in new string?[] { null, "", " ", "\t\n" })
                    AssertNull(await probe.GetModelDigestAsync(endpoint, absent, null).ConfigureAwait(false), "absent model is unavailable");
                AssertEqual(4, calls, "absent models never probe");
            }));

            cases.Add(CaseAsync("provenance_probe_failures_record_nulls", "A probe that throws, times out or answers nothing leaves the values null and the outcome is still recorded promptly", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelEndpoint endpoint = new ModelEndpoint { Name = "ollama", Provider = ModelProviderEnum.Ollama, BaseUrl = "http://ollama.invalid:11434", Model = "qwen3:8b" };
                await db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
                Captain captain = new Captain("api worker", AgentRuntimeEnum.ApiEndpoint);
                captain.ModelEndpointId = endpoint.Id;
                await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                Mission mission = await CreateMissionAsync(db, MissionStatusEnum.InProgress, null, null).ConfigureAwait(false);
                StageAttemptCapture capture = Capture(exitCode: 1);
                capture.MissionId = mission.Id;
                capture.Captain = captain;

                // Throws (synchronously and asynchronously).
                StageProvenanceProbe throwing = new StageProvenanceProbe(CreateLogging());
                throwing.RuntimeVersionProbe = (c, h, ep, ct) => throw new InvalidOperationException("boom");
                throwing.ModelDigestProbe = async (ep, m, h, ct) => { await Task.Yield(); throw new System.Net.Http.HttpRequestException("refused"); };
                StageOutcome? thrown = await new StageOutcomeService(db, CreateLogging(), throwing).RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertNotNull(thrown, "recorded despite throwing probes");
                AssertNull(thrown!.RuntimeVersion, "version null");
                AssertNull(thrown.ModelDigest, "digest null");
                AssertNotNull(await db.StageOutcomes.ReadAsync(thrown.Id).ConfigureAwait(false), "stored");

                // Hangs: the exit waits at most the wait timeout, and a later exit does not wait for the same probe.
                int hangingCalls = 0;
                StageProvenanceProbe hanging = new StageProvenanceProbe(CreateLogging());
                hanging.WaitTimeout = TimeSpan.FromMilliseconds(200);
                hanging.RuntimeVersionProbe = async (c, h, ep, ct) => { Interlocked.Increment(ref hangingCalls); await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); return "never"; };
                hanging.ModelDigestProbe = async (ep, m, h, ct) => { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); return "never"; };
                StageOutcomeService hangingService = new StageOutcomeService(db, CreateLogging(), hanging);
                Stopwatch watch = Stopwatch.StartNew();
                StageOutcome? slow = await hangingService.RecordAttemptAsync(capture).ConfigureAwait(false);
                long firstMs = watch.ElapsedMilliseconds;
                AssertNotNull(slow, "recorded despite a hanging probe");
                AssertNull(slow!.RuntimeVersion, "version null on timeout");
                AssertNull(slow.ModelDigest, "digest null on timeout");
                AssertTrue(firstMs < 3000, "bounded by the wait timeout, took " + firstMs + " ms");
                watch.Restart();
                StageOutcome? again = await hangingService.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertNull(again!.RuntimeVersion, "still null");
                AssertTrue(watch.ElapsedMilliseconds < 150, "a probe already running is not waited for again, took " + watch.ElapsedMilliseconds + " ms");
                AssertEqual(1, hangingCalls, "the running probe is not started twice");

                // Answers nothing: null is remembered for the failure cache duration, then tried again.
                int emptyCalls = 0;
                StageProvenanceProbe empty = new StageProvenanceProbe(CreateLogging());
                empty.RuntimeVersionProbe = (c, h, ep, ct) => { Interlocked.Increment(ref emptyCalls); return Task.FromResult<string?>("   \n"); };
                empty.ModelDigestProbe = (ep, m, h, ct) => Task.FromResult<string?>(null);
                StageOutcomeService emptyService = new StageOutcomeService(db, CreateLogging(), empty);
                AssertNull((await emptyService.RecordAttemptAsync(capture).ConfigureAwait(false))!.RuntimeVersion, "blank output is null");
                await emptyService.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual(1, emptyCalls, "a failure is remembered");
                empty.FailureCacheDuration = TimeSpan.Zero;
                await emptyService.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual(2, emptyCalls, "and retried after the failure cache duration");

                // No probe configured: both stay null.
                StageOutcome? plain = await new StageOutcomeService(db, CreateLogging()).RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertNull(plain!.RuntimeVersion, "no probe, no version");
                AssertNull(plain.ModelDigest, "no probe, no digest");
            }));

            cases.Add(CaseAsync("provenance_cli_version_runs_on_the_attempt_host", "The default runtime probe runs the runtime's CLI with --version on the attempt's host and reads the first line", TestTags.Positive, async () =>
            {
                List<string?> hosts = new List<string?>();
                RecordingCommandExecutor executor = new RecordingCommandExecutor(new HostCommandResult { ExitCode = 0, StandardOutput = "\n2.1.3 (Claude Code)\nextra\n" });
                StageProvenanceProbe probe = new StageProvenanceProbe(CreateLogging(), harborId => { hosts.Add(harborId); return harborId == "hbr_gone" ? null : executor; });
                Captain claude = new Captain("claude", AgentRuntimeEnum.ClaudeCode);

                AssertEqual("2.1.3 (Claude Code)", await probe.ProbeRuntimeVersionAsync(claude, "hbr_laptop", null).ConfigureAwait(false));
                AssertEqual("claude", executor.Requests[0].Executable, "executable");
                AssertEqual("--version", String.Join(" ", executor.Requests[0].Arguments), "arguments");
                AssertEqual("hbr_laptop", hosts[0], "ran on the attempt's Harbor");

                AssertEqual("codex-cli 0.46.0", await new StageProvenanceProbe(CreateLogging(), h => new RecordingCommandExecutor(new HostCommandResult { ExitCode = 0, StandardError = "codex-cli 0.46.0" }))
                    .ProbeRuntimeVersionAsync(new Captain("codex", AgentRuntimeEnum.Codex), null, null).ConfigureAwait(false), "stderr when stdout is empty");
                AssertNull(await new StageProvenanceProbe(CreateLogging(), h => new RecordingCommandExecutor(new HostCommandResult { ExitCode = 127, StandardError = "not found" }))
                    .ProbeRuntimeVersionAsync(claude, null, null).ConfigureAwait(false), "a failed command is null");
                AssertNull(await new StageProvenanceProbe(CreateLogging(), h => new RecordingCommandExecutor(new HostCommandResult { ExitCode = 0, TimedOut = true, StandardOutput = "partial" }))
                    .ProbeRuntimeVersionAsync(claude, null, null).ConfigureAwait(false), "a timed-out command is null");
                AssertNull(await probe.ProbeRuntimeVersionAsync(claude, "hbr_gone", null).ConfigureAwait(false), "an unreachable Harbor is null");
                AssertNull(await probe.ProbeRuntimeVersionAsync(new Captain("api", AgentRuntimeEnum.ApiEndpoint), null, null).ConfigureAwait(false), "no CLI and no endpoint is null");
                AssertNull(await probe.ProbeRuntimeVersionAsync(new Captain("api", AgentRuntimeEnum.ApiEndpoint), null, new ModelEndpoint { Provider = ModelProviderEnum.OpenAI, BaseUrl = "http://127.0.0.1:1" }).ConfigureAwait(false), "no version for a non-Ollama endpoint");
                AssertEqual(1, executor.Requests.Count, "no command for an API-endpoint captain or an unreachable Harbor");
                await AssertThrowsAsync<ArgumentNullException>(() => probe.ProbeRuntimeVersionAsync(null!, null, null), "null captain").ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("provenance_ollama_requests_against_fixture", "Ollama version and digest are read over HTTP with the endpoint's credential, and every error response is null", TestTags.Positive, async () =>
            {
                using OllamaFixture fixture = new OllamaFixture();
                fixture.Bodies["/api/version"] = "{\"version\":\"0.12.3\"}";
                fixture.Bodies["/api/tags"] = "{\"models\":[{\"name\":\"qwen3:8b\",\"model\":\"qwen3:8b\",\"digest\":\"500a1f067a9f\",\"size\":1},{\"name\":\"llama3:latest\",\"model\":\"llama3:latest\",\"digest\":\"365c0bd3c000\"}]}";
                ModelEndpoint endpoint = new ModelEndpoint { Name = "fixture", Provider = ModelProviderEnum.Ollama, BaseUrl = fixture.BaseUrl + "/v1/", Model = "qwen3:8b", ApiKey = "fixture-secret" };
                StageProvenanceProbe probe = new StageProvenanceProbe(CreateLogging());
                Captain api = new Captain("api", AgentRuntimeEnum.ApiEndpoint);

                AssertEqual("Ollama 0.12.3", await probe.ProbeRuntimeVersionAsync(api, null, endpoint).ConfigureAwait(false), "server version");
                AssertEqual("500a1f067a9f", await probe.ProbeModelDigestAsync(endpoint, "qwen3:8b", null).ConfigureAwait(false), "exact name");
                AssertEqual("365c0bd3c000", await probe.ProbeModelDigestAsync(endpoint, "Llama3", null).ConfigureAwait(false), "untagged name matches :latest");
                AssertNull(await probe.ProbeModelDigestAsync(endpoint, "mistral:7b", null).ConfigureAwait(false), "unknown model");
                AssertEqual(4, fixture.Requests.Count, "four requests");
                foreach (string request in fixture.Requests)
                    AssertStartsWith("GET ", request, "method");
                AssertEqual("GET /api/version Bearer fixture-secret", fixture.Requests[0], "url and credential");
                AssertEqual("GET /api/tags Bearer fixture-secret", fixture.Requests[1], "tags url (the /v1 suffix is dropped)");

                // A Harbor attempt on a loopback endpoint is not read by the Admiral (it would reach another server).
                AssertNull(await probe.ProbeModelDigestAsync(endpoint, "qwen3:8b", "hbr_laptop").ConfigureAwait(false), "loopback on a Harbor");
                AssertNull(await probe.ProbeRuntimeVersionAsync(api, "hbr_laptop", endpoint).ConfigureAwait(false), "loopback version on a Harbor");
                AssertEqual(4, fixture.Requests.Count, "no request for a Harbor's loopback endpoint");

                fixture.Status = 500;
                AssertNull(await probe.ProbeModelDigestAsync(endpoint, "qwen3:8b", null).ConfigureAwait(false), "HTTP 500");
                AssertNull(await probe.ProbeRuntimeVersionAsync(api, null, endpoint).ConfigureAwait(false), "HTTP 500 version");
                fixture.Status = 200;
                fixture.Bodies["/api/tags"] = "";
                AssertNull(await probe.ProbeModelDigestAsync(endpoint, "qwen3:8b", null).ConfigureAwait(false), "empty body");
                fixture.Bodies["/api/tags"] = "{\"models\":[]}";
                AssertNull(await probe.ProbeModelDigestAsync(endpoint, "qwen3:8b", null).ConfigureAwait(false), "empty list");
                fixture.Bodies["/api/tags"] = "{}";
                AssertNull(await probe.ProbeModelDigestAsync(endpoint, "qwen3:8b", null).ConfigureAwait(false), "no models property");
                fixture.Bodies["/api/version"] = "{}";
                AssertNull(await probe.ProbeRuntimeVersionAsync(api, null, endpoint).ConfigureAwait(false), "no version property");

                ModelEndpoint openAi = new ModelEndpoint { Name = "openai", Provider = ModelProviderEnum.OpenAICompatible, BaseUrl = fixture.BaseUrl, Model = "qwen3:8b" };
                int before = fixture.Requests.Count;
                AssertNull(await probe.ProbeModelDigestAsync(openAi, "qwen3:8b", null).ConfigureAwait(false), "not Ollama");
                AssertNull(await probe.GetModelDigestAsync(openAi, "qwen3:8b", null).ConfigureAwait(false), "not Ollama, cached path");
                AssertNull(await probe.GetModelDigestAsync(endpoint, null, null).ConfigureAwait(false), "no model");
                AssertEqual(before, fixture.Requests.Count, "no request for another provider or no model");

                ModelEndpoint refused = new ModelEndpoint { Name = "refused", Provider = ModelProviderEnum.Ollama, BaseUrl = "http://127.0.0.1:1" };
                await AssertThrowsAsync<System.Net.Http.HttpRequestException>(() => probe.ProbeModelDigestAsync(refused, "qwen3:8b", null), "the uncached probe reports a refused connection").ConfigureAwait(false);
                AssertNull(await probe.GetModelDigestAsync(refused, "qwen3:8b", null).ConfigureAwait(false), "the cached path turns it into null");
            }));

            cases.Add(CaseAsync("provenance_ollama_end_to_end_cached", "Recording an Ollama-backed attempt stores the server version and digest read from the fixture, and the next exit reuses them", TestTags.Positive, async () =>
            {
                using OllamaFixture fixture = new OllamaFixture();
                fixture.Bodies["/api/version"] = "{\"version\":\"0.12.3\"}";
                fixture.Bodies["/api/tags"] = "{\"models\":[{\"name\":\"qwen3:8b\",\"digest\":\"500a1f067a9f\"}]}";
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelEndpoint endpoint = new ModelEndpoint { Name = "fixture", Provider = ModelProviderEnum.Ollama, BaseUrl = fixture.BaseUrl, Model = "qwen3:8b" };
                await db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
                Captain captain = new Captain("api worker", AgentRuntimeEnum.ApiEndpoint);
                captain.ModelEndpointId = endpoint.Id;
                await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                Mission mission = await CreateMissionAsync(db, MissionStatusEnum.InProgress, null, null).ConfigureAwait(false);
                StageAttemptCapture capture = Capture(exitCode: 1);
                capture.MissionId = mission.Id;
                capture.Captain = captain;

                StageOutcomeService service = new StageOutcomeService(db, CreateLogging(), new StageProvenanceProbe(CreateLogging()));
                StageOutcome? first = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual("Ollama 0.12.3", first!.RuntimeVersion, "version");
                AssertEqual("500a1f067a9f", first.ModelDigest, "digest");
                AssertEqual(2, fixture.Requests.Count, "one version and one tags request");
                AssertTrue(fixture.Requests.Contains("GET /api/version -") && fixture.Requests.Contains("GET /api/tags -"), "no credential configured, none sent: " + String.Join(", ", fixture.Requests));

                StageOutcome? second = await service.RecordAttemptAsync(capture).ConfigureAwait(false);
                AssertEqual("500a1f067a9f", second!.ModelDigest, "cached digest");
                AssertEqual(2, fixture.Requests.Count, "cached: no new request");
            }));

            cases.Add(CaseAsync("escalation_record_links_finisher_and_kept_or_replaced", "When an escalated mission completes, the escalated attempt names the finishing attempt and captain and whether its work was kept", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                string repo = Path.Combine(Path.GetTempPath(), "armada_sto_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(repo);
                try
                {
                    Git(repo, "init", "-q", "-b", "main");
                    string root = Commit(repo, "root.txt", "root");
                    string escalatedWork = Commit(repo, "work.txt", "weak captain");
                    string keptFinal = Commit(repo, "work.txt", "strong captain fixed it");
                    Git(repo, "checkout", "-q", "-b", "rewrite", root);
                    string replacedFinal = Commit(repo, "other.txt", "strong captain started over");

                    LoggingModule logging = CreateLogging();
                    DockHostResolver hosts = new DockHostResolver(new GitService(logging), logging, null);
                    StageOutcomeService service = new StageOutcomeService(db, logging, null, () => hosts);

                    Vessel vessel = new Vessel("Escalation Vessel", "https://github.com/test/escalation") { LocalPath = repo };
                    await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);

                    StageOutcome kept = await EscalateThenFinishAsync(db, service, vessel, escalatedWork, keptFinal).ConfigureAwait(false);
                    AssertEqual<bool?>(true, kept.PriorWorkKept, "final commit descends from the escalated work: kept");

                    StageOutcome replaced = await EscalateThenFinishAsync(db, service, vessel, escalatedWork, replacedFinal).ConfigureAwait(false);
                    AssertEqual<bool?>(false, replaced.PriorWorkKept, "final commit does not contain the escalated work: replaced");

                    StageOutcome unknown = await EscalateThenFinishAsync(db, service, vessel, null, keptFinal).ConfigureAwait(false);
                    AssertNull(unknown.PriorWorkKept, "no escalated commit: unknown");
                    AssertEqual("cpt_strong", unknown.FinishedByCaptainId, "still linked");

                    StageOutcome noGit = await EscalateThenFinishAsync(db, new StageOutcomeService(db, logging), vessel, escalatedWork, keptFinal).ConfigureAwait(false);
                    AssertNull(noGit.PriorWorkKept, "no git access: unknown");
                    AssertNotNull(noGit.FinishedByOutcomeId, "still linked without git");

                    StageOutcome unchanged = await EscalateThenFinishAsync(db, new StageOutcomeService(db, logging), vessel, escalatedWork, escalatedWork).ConfigureAwait(false);
                    AssertEqual<bool?>(true, unchanged.PriorWorkKept, "identical commits prove work kept even without git access");
                    AssertEqual("cpt_strong", unchanged.FinishedByCaptainId, "unchanged work still names the finishing captain");
                    AssertNotNull(unchanged.FinishedByOutcomeId, "unchanged work still names the finishing attempt");
                }
                finally
                {
                    TryDeleteDirectory(repo);
                }
            }));

            cases.Add(CaseAsync("escalation_record_not_linked_by_failure_or_twice", "A failed attempt after an escalation does not finish it, and a finished escalation keeps its first finisher", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging());
                Mission mission = await CreateMissionAsync(db, MissionStatusEnum.Pending, null, null).ConfigureAwait(false);

                StageAttemptCapture weak = Capture(exitCode: 1);
                weak.MissionId = mission.Id;
                weak.EscalatedToCaptainId = "cpt_strong";
                weak.EscalatedCommit = "aaaa";
                StageOutcome? escalated = await service.RecordAttemptAsync(weak).ConfigureAwait(false);
                AssertEqual(StageDispositionEnum.Escalated, escalated!.Disposition);

                mission.Status = MissionStatusEnum.Failed;
                await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                StageAttemptCapture strong = Capture(exitCode: 1);
                strong.MissionId = mission.Id;
                strong.Captain = new Captain("strong") { Id = "cpt_strong" };
                StageOutcome? failed = await service.RecordAttemptAsync(strong).ConfigureAwait(false);
                AssertEqual(StageDispositionEnum.Failed, failed!.Disposition);
                StageOutcome? afterFailure = await db.StageOutcomes.ReadAsync(escalated.Id).ConfigureAwait(false);
                AssertNull(afterFailure!.FinishedByOutcomeId, "a failure does not finish the escalation");
                AssertNull(afterFailure.PriorWorkKept, "nothing compared");

                mission.Status = MissionStatusEnum.Complete;
                mission.CommitHash = "aaaa";
                await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                StageOutcome? done = await service.RecordAttemptAsync(strong).ConfigureAwait(false);
                StageOutcome? linked = await db.StageOutcomes.ReadAsync(escalated.Id).ConfigureAwait(false);
                AssertEqual(done!.Id, linked!.FinishedByOutcomeId, "linked to the completing attempt");
                AssertEqual<bool?>(true, linked.PriorWorkKept, "the same commit is kept even without git");

                StageOutcome? later = await service.RecordAttemptAsync(strong).ConfigureAwait(false);
                AssertEqual(done.Id, (await db.StageOutcomes.ReadAsync(escalated.Id).ConfigureAwait(false))!.FinishedByOutcomeId, "first finisher kept, not " + later!.Id);
            }));

            cases.Add(CaseAsync("escalation_record_links_every_page", "A mission with more escalations than one page links every escalated attempt to the finisher", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                StageOutcomeService service = new StageOutcomeService(db, CreateLogging());
                Mission mission = await CreateMissionAsync(db, MissionStatusEnum.Complete, null, null).ConfigureAwait(false);
                Mission other = await CreateMissionAsync(db, MissionStatusEnum.Complete, null, null).ConfigureAwait(false);
                const int escalations = 201;
                for (int i = 0; i < escalations; i++)
                {
                    StageOutcome prior = StoredOutcome(mission.Id, null);
                    prior.Disposition = StageDispositionEnum.Escalated;
                    prior.Attempt = i + 1;
                    await db.StageOutcomes.CreateAsync(prior).ConfigureAwait(false);
                }

                StageOutcome otherEscalated = StoredOutcome(other.Id, null);
                otherEscalated.Disposition = StageDispositionEnum.Escalated;
                await db.StageOutcomes.CreateAsync(otherEscalated).ConfigureAwait(false);

                StageAttemptCapture strong = Capture(exitCode: 0);
                strong.MissionId = mission.Id;
                strong.Captain = new Captain("strong") { Id = "cpt_strong" };
                StageOutcome? done = await service.RecordAttemptAsync(strong).ConfigureAwait(false);
                AssertNotNull(done, "finishing attempt recorded");

                List<StageOutcome> linked = new List<StageOutcome>();
                int page = 1;
                while (true)
                {
                    StageOutcomeQuery query = new StageOutcomeQuery { MissionId = mission.Id, Disposition = StageDispositionEnum.Escalated, PageNumber = page, PageSize = 100 };
                    EnumerationResult<StageOutcome> batch = await db.StageOutcomes.EnumerateAsync(query).ConfigureAwait(false);
                    if (batch.Objects == null || batch.Objects.Count == 0) break;
                    linked.AddRange(batch.Objects);
                    page++;
                }

                AssertEqual(escalations, linked.Count, "every escalated attempt read back");
                AssertEqual(escalations, linked.Count(o => o.FinishedByOutcomeId == done!.Id && o.FinishedByCaptainId == "cpt_strong"), "every escalated attempt names the finisher");
                StageOutcome? untouched = await db.StageOutcomes.ReadAsync(otherEscalated.Id).ConfigureAwait(false);
                AssertNull(untouched!.FinishedByOutcomeId, "another mission's escalation is not linked");
            }));

            cases.Add(CaseAsync("purge_mission_deletes_its_outcomes", "Deleting a mission by any of its delete paths deletes its stage outcomes and no one else's", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Mission plain = await CreateMissionAsync(db, MissionStatusEnum.Complete, null, null).ConfigureAwait(false);
                Mission tenant = await CreateMissionAsync(db, MissionStatusEnum.Complete, null, null).ConfigureAwait(false);
                Mission owned = await CreateMissionAsync(db, MissionStatusEnum.Complete, null, null).ConfigureAwait(false);
                Mission keep = await CreateMissionAsync(db, MissionStatusEnum.Complete, null, null).ConfigureAwait(false);
                foreach (Mission m in new[] { plain, tenant, owned, keep })
                {
                    await db.StageOutcomes.CreateAsync(StoredOutcome(m.Id, null)).ConfigureAwait(false);
                    await db.StageOutcomes.CreateAsync(StoredOutcome(m.Id, null)).ConfigureAwait(false);
                }

                await db.Missions.DeleteAsync(plain.Id).ConfigureAwait(false);
                AssertNull(await db.Missions.ReadAsync(plain.Id).ConfigureAwait(false), "mission deleted");
                AssertEqual(0, await db.StageOutcomes.CountByMissionAsync(plain.Id).ConfigureAwait(false), "outcomes deleted with the mission");

                await db.Missions.DeleteAsync("ten_other", tenant.Id).ConfigureAwait(false);
                AssertNotNull(await db.Missions.ReadAsync(tenant.Id).ConfigureAwait(false), "another tenant cannot delete it");
                AssertEqual(2, await db.StageOutcomes.CountByMissionAsync(tenant.Id).ConfigureAwait(false), "outcomes kept when the mission is not deleted");
                await db.Missions.DeleteAsync(tenant.TenantId!, tenant.Id).ConfigureAwait(false);
                AssertNull(await db.Missions.ReadAsync(tenant.Id).ConfigureAwait(false), "tenant-scoped delete");
                AssertEqual(0, await db.StageOutcomes.CountByMissionAsync(tenant.Id).ConfigureAwait(false), "outcomes deleted by the tenant-scoped delete");

                await db.Missions.DeleteAsync(owned.TenantId!, "usr_other", owned.Id).ConfigureAwait(false);
                AssertEqual(2, await db.StageOutcomes.CountByMissionAsync(owned.Id).ConfigureAwait(false), "another user's delete keeps them");
                await db.Missions.DeleteAsync(owned.TenantId!, owned.UserId!, owned.Id).ConfigureAwait(false);
                AssertNull(await db.Missions.ReadAsync(owned.Id).ConfigureAwait(false), "user-scoped delete");
                AssertEqual(0, await db.StageOutcomes.CountByMissionAsync(owned.Id).ConfigureAwait(false), "outcomes deleted by the user-scoped delete");

                AssertEqual(2, await db.StageOutcomes.CountByMissionAsync(keep.Id).ConfigureAwait(false), "another mission's outcomes untouched");
            }));

            cases.Add(CaseAsync("retention_purge_deletes_outcomes", "The retention sweep deletes the stage outcomes of the missions it removes and keeps those of recent missions", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                Mission old = new Mission("Old standalone") { Status = MissionStatusEnum.Complete, CreatedUtc = DateTime.UtcNow.AddDays(-61), CompletedUtc = DateTime.UtcNow.AddDays(-60) };
                await db.Missions.CreateAsync(old).ConfigureAwait(false);
                Voyage oldVoyage = new Voyage("Old voyage") { Status = VoyageStatusEnum.Complete, CompletedUtc = DateTime.UtcNow.AddDays(-60) };
                await db.Voyages.CreateAsync(oldVoyage).ConfigureAwait(false);
                Mission oldInVoyage = new Mission("Old in voyage") { VoyageId = oldVoyage.Id, Status = MissionStatusEnum.Complete, CompletedUtc = DateTime.UtcNow.AddDays(-60) };
                await db.Missions.CreateAsync(oldInVoyage).ConfigureAwait(false);
                Mission recent = new Mission("Recent") { Status = MissionStatusEnum.Complete, CompletedUtc = DateTime.UtcNow.AddDays(-2) };
                await db.Missions.CreateAsync(recent).ConfigureAwait(false);
                foreach (Mission m in new[] { old, oldInVoyage, recent })
                    await db.StageOutcomes.CreateAsync(StoredOutcome(m.Id, null)).ConfigureAwait(false);

                DataExpiryService expiry = new DataExpiryService(CreateLogging(), TestDatabaseConfig.Type, testDb.ConnectionString, 30);
                await expiry.PurgeExpiredDataAsync().ConfigureAwait(false);

                AssertNull(await db.Missions.ReadAsync(old.Id).ConfigureAwait(false), "old mission purged");
                AssertNull(await db.Missions.ReadAsync(oldInVoyage.Id).ConfigureAwait(false), "old voyage mission purged");
                AssertEqual(0, await db.StageOutcomes.CountByMissionAsync(old.Id).ConfigureAwait(false), "standalone mission's outcomes purged");
                AssertEqual(0, await db.StageOutcomes.CountByMissionAsync(oldInVoyage.Id).ConfigureAwait(false), "voyage mission's outcomes purged");
                AssertNotNull(await db.Missions.ReadAsync(recent.Id).ConfigureAwait(false), "recent mission kept");
                AssertEqual(1, await db.StageOutcomes.CountByMissionAsync(recent.Id).ConfigureAwait(false), "recent mission's outcomes kept");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Stage Outcomes and Failure Causes",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static StageOutcome Outcome(StageDispositionEnum disposition)
        {
            return new StageOutcome { MissionId = "msn_test", Persona = "Worker", Disposition = disposition };
        }

        private static StageOutcome StoredOutcome(string missionId, MissionFailureKindEnum? kind)
        {
            return new StageOutcome
            {
                MissionId = missionId,
                Persona = "Worker",
                Attempt = 1,
                Disposition = StageDispositionEnum.Failed,
                FailureKind = kind,
                StartedUtc = _Fixed,
                EndedUtc = _Fixed.AddMinutes(1)
            };
        }

        private static FailureCauseRule CustomRule(string name, int priority, FailureCauseEnum cause, Action<FailureCauseRuleMatch> configure)
        {
            FailureCauseRule rule = new FailureCauseRule { Name = name, Priority = priority, Cause = cause, Enabled = true };
            configure(rule.Match);
            return rule;
        }

        private static StageAttemptCapture Capture(int? exitCode)
        {
            return new StageAttemptCapture
            {
                Claimed = true,
                Captain = new Captain("self") { Id = "cpt_self" },
                ExitCode = exitCode,
                OutputRepairAttemptsBefore = 0,
                RedispatchAttemptsBefore = 0
            };
        }

        private static StageDispositionEnum Decide(MissionStatusEnum status, int? exitCode = 0, int outputRepairs = 0, int redispatches = 0, string? nextCaptain = null)
        {
            StageAttemptCapture capture = Capture(exitCode);
            Mission after = new Mission("m", "d")
            {
                Status = status,
                CaptainId = nextCaptain ?? "cpt_self",
                OutputRepairAttempts = outputRepairs,
                RedispatchAttempts = redispatches
            };
            StageOutcome outcome = new StageOutcome();
            StageOutcomeService.DecideDisposition(capture, after, outcome);
            return outcome.Disposition;
        }

        private static async Task CreateRepairEventAsync(DatabaseDriver db, Mission mission, ContractRepairKindEnum kind, DateTime createdUtc)
        {
            ArmadaEvent evt = new ArmadaEvent(ContractRepair.EventType, "Contract repaired (" + kind + ")");
            evt.TenantId = mission.TenantId;
            evt.EntityType = "mission";
            evt.EntityId = mission.Id;
            evt.MissionId = mission.Id;
            evt.Payload = JsonSerializer.Serialize(new ContractRepairRecord { MissionId = mission.Id, Persona = "Worker", RepairKind = kind, Detail = "test" });
            evt.CreatedUtc = createdUtc;
            await db.Events.CreateAsync(evt).ConfigureAwait(false);
        }

        private static async Task<Mission> CreateMissionAsync(DatabaseDriver db, MissionStatusEnum status, MissionFailureKindEnum? kind, string? reason)
        {
            Fleet fleet = new Fleet("Outcome Fleet " + Guid.NewGuid().ToString("N"));
            await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);
            Vessel vessel = new Vessel("Outcome Vessel " + Guid.NewGuid().ToString("N"), "https://github.com/test/outcomes");
            vessel.FleetId = fleet.Id;
            await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            Voyage voyage = new Voyage("Outcome Voyage", "desc");
            await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);

            Mission mission = new Mission("Outcome Mission", "desc");
            mission.VesselId = vessel.Id;
            mission.VoyageId = voyage.Id;
            mission.Status = status;
            mission.FailureKind = kind;
            mission.FailureReason = reason;
            mission.TenantId = Constants.DefaultTenantId;
            mission.UserId = Constants.DefaultUserId;
            await db.Missions.CreateAsync(mission).ConfigureAwait(false);
            return mission;
        }

        private static async Task<StageOutcome> EscalateThenFinishAsync(DatabaseDriver db, StageOutcomeService service, Vessel vessel, string? escalatedCommit, string finalCommit)
        {
            Mission mission = new Mission("Escalated Mission", "desc") { VesselId = vessel.Id, Status = MissionStatusEnum.Pending };
            await db.Missions.CreateAsync(mission).ConfigureAwait(false);

            StageAttemptCapture weak = Capture(exitCode: 1);
            weak.MissionId = mission.Id;
            weak.EscalatedToCaptainId = "cpt_strong";
            weak.EscalatedCommit = escalatedCommit;
            StageOutcome? escalated = await service.RecordAttemptAsync(weak).ConfigureAwait(false);
            AssertNotNull(escalated, "escalated attempt recorded");
            AssertEqual(StageDispositionEnum.Escalated, escalated!.Disposition, "escalated");
            AssertNull(escalated.FinishedByOutcomeId, "not finished yet");

            mission.Status = MissionStatusEnum.Complete;
            mission.CommitHash = finalCommit;
            await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
            StageAttemptCapture strong = Capture(exitCode: 0);
            strong.MissionId = mission.Id;
            strong.Captain = new Captain("strong") { Id = "cpt_strong" };
            StageOutcome? finished = await service.RecordAttemptAsync(strong).ConfigureAwait(false);
            AssertNotNull(finished, "finishing attempt recorded");
            AssertEqual(StageDispositionEnum.Completed, finished!.Disposition, "completed");
            AssertEqual(finalCommit, finished.CommitHash, "finishing commit");

            StageOutcome? linked = await db.StageOutcomes.ReadAsync(escalated.Id).ConfigureAwait(false);
            AssertNotNull(linked, "escalated outcome");
            AssertEqual(finished.Id, linked!.FinishedByOutcomeId, "finished by the completing attempt");
            AssertEqual("cpt_strong", linked.FinishedByCaptainId, "finished by the strong captain");
            AssertEqual(escalatedCommit, linked.CommitHash, "escalated commit stored");
            return linked;
        }

        private static string Git(string dir, params string[] args)
        {
            ProcessStartInfo info = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("user.name=Armada Test");
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("user.email=test@armada.invalid");
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("commit.gpgsign=false");
            foreach (string arg in args) info.ArgumentList.Add(arg);
            using Process process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("git " + String.Join(" ", args) + " failed: " + error);
            return output.Trim();
        }

        private static string Commit(string repo, string file, string content)
        {
            File.WriteAllText(Path.Combine(repo, file), content);
            Git(repo, "add", file);
            Git(repo, "commit", "-q", "-m", content);
            return Git(repo, "rev-parse", "HEAD");
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(path, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return CaseAsync(caseId, displayName, tag, () =>
            {
                body();
                return Task.CompletedTask;
            });
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

        private sealed class RecordingCommandExecutor : IHostCommandExecutor
        {
            public List<HostCommandRequest> Requests { get; } = new List<HostCommandRequest>();

            private readonly HostCommandResult _Result;

            public RecordingCommandExecutor(HostCommandResult result)
            {
                _Result = result;
            }

            public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
            {
                lock (Requests) Requests.Add(request);
                return Task.FromResult(_Result);
            }
        }

        /// <summary>
        /// A local HTTP stand-in for an Ollama server: serves a body per path with a configurable status and records
        /// each request as "METHOD path credential" (the Authorization header, or "-").
        /// </summary>
        private sealed class OllamaFixture : IDisposable
        {
            public string BaseUrl { get; }
            public int Status { get; set; } = 200;
            public Dictionary<string, string> Bodies { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public List<string> Requests
            {
                get { lock (_Requests) return new List<string>(_Requests); }
            }

            private readonly HttpListener _Listener;
            private readonly List<string> _Requests = new List<string>();
            private readonly Task _Loop;

            public OllamaFixture()
            {
                _Listener = TestPorts.StartHttpListener(p => new[] { "http://127.0.0.1:" + p + "/" }, out int port);
                BaseUrl = "http://127.0.0.1:" + port;
                _Loop = Task.Run(LoopAsync);
            }

            public void Dispose()
            {
                try { _Listener.Stop(); } catch (ObjectDisposedException) { }
                _Listener.Close();
                try { _Loop.Wait(2000); } catch (AggregateException) { }
            }

            private async Task LoopAsync()
            {
                while (_Listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await _Listener.GetContextAsync().ConfigureAwait(false); }
                    catch (Exception) { return; }

                    string path = context.Request.Url!.AbsolutePath;
                    string credential = context.Request.Headers["Authorization"] ?? "-";
                    lock (_Requests) _Requests.Add(context.Request.HttpMethod + " " + path + " " + credential);

                    string? body;
                    int status = Status;
                    if (!Bodies.TryGetValue(path, out body)) status = 404;
                    byte[] bytes = Encoding.UTF8.GetBytes(status == 200 ? body ?? String.Empty : "{\"error\":\"fixture\"}");
                    context.Response.StatusCode = status;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    context.Response.Close();
                }
            }
        }

        #endregion
    }
}
