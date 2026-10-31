namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
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
    /// Full Evaluate (docs/MODEL_HETEROGENEITY.md, Phase 5 item 4): evaluator selection (a different family first, a
    /// same-family fallback with a warning, none available), validation of the evaluator's narrative against the
    /// mechanical counts, the whole run through a synthetic battery pack (report, subjective grades, advisory profile
    /// revision, evaluation-flagged missions), cancellation and timeout producing an incomplete report and releasing the
    /// open battery mission, and the per-inference-host concurrency limit.
    /// </summary>
    public sealed class ModelEvaluationSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ModelEvaluation";
        private const string PackId = "evalsyn";
        private const string OtherPackId = "evalsyn2";
        private const string EvaluatorLabel = "Evaluator grading";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("model_family", "The model family drops provider prefixes, sizes and versions, maps aliases, and falls back to the vendor runtime", TestTags.Positive, () =>
            {
                AssertEqual("qwen", ModelFamily.FromModel("qwen3.8:27b"));
                AssertEqual("gpt", ModelFamily.FromModel("openai/gpt-oss-20b"));
                AssertEqual("gpt", ModelFamily.FromModel("o3-mini"));
                AssertEqual("claude", ModelFamily.FromModel("claude-opus-5-5"));
                AssertEqual("deepseek", ModelFamily.FromModel("DeepSeek-V4.1"));
                AssertNull(ModelFamily.FromModel("  "));
                AssertNull(ModelFamily.FromModel("7b"));
                AssertEqual("claude", ModelFamily.Of(new Captain("c") { Runtime = AgentRuntimeEnum.ClaudeCode }, null));
                AssertEqual("gpt", ModelFamily.Of(new Captain("c") { Runtime = AgentRuntimeEnum.Codex }, null));
                AssertEqual("qwen", ModelFamily.Of(new Captain("c") { Runtime = AgentRuntimeEnum.ApiEndpoint }, new ModelEndpoint { Model = "qwen3:8b" }));
                return Task.CompletedTask;
            }));

            cases.Add(Case("evaluator_different_family", "The evaluator is the highest-tier idle captain of a different model family; the candidate, busy and quarantined captains are skipped", TestTags.Positive, () =>
            {
                List<Captain> idle = new List<Captain>
                {
                    Idle("cpt_cand", "qwen3:32b", CaptainTierEnum.Premium),
                    Idle("cpt_qwen_premium", "qwen3.8:27b", CaptainTierEnum.Premium),
                    Idle("cpt_gpt_economy", "gpt-oss:20b", CaptainTierEnum.Economy),
                    Idle("cpt_claude_standard", "claude-sonnet", null),
                    Idle("cpt_gemini_busy", "gemini-2", CaptainTierEnum.Premium, CaptainStateEnum.Working),
                    Idle("cpt_deepseek_quarantined", "deepseek-v4", CaptainTierEnum.Premium, quarantined: true)
                };
                EvaluatorSelection selection = EvaluatorSelector.Select("qwen", "cpt_cand", idle, c => null);
                AssertEqual("cpt_claude_standard", selection.Captain?.Id, "highest tier of another family (no tier ranks as Standard)");
                AssertEqual("claude", selection.Family, "family");
                AssertNull(selection.Warning, "no warning");
                AssertNull(selection.SkippedReason, "not skipped");
                AssertFalse(selection.Explicit, "default selection");
                return Task.CompletedTask;
            }));

            cases.Add(Case("evaluator_same_family_fallback", "With only same-family captains idle the highest-tier one is used and the selection carries a warning; a named same-family evaluator warns too", TestTags.Negative, () =>
            {
                List<Captain> idle = new List<Captain>
                {
                    Idle("cpt_cand", "qwen3:32b", CaptainTierEnum.Premium),
                    Idle("cpt_qwen_b", "qwen2.5-coder:7b", CaptainTierEnum.Economy),
                    Idle("cpt_qwen_a", "qwen3.8:27b", CaptainTierEnum.Standard),
                    Idle("cpt_claude_busy", "claude-opus", CaptainTierEnum.Premium, CaptainStateEnum.Working)
                };
                EvaluatorSelection selection = EvaluatorSelector.Select("qwen", "cpt_cand", idle, c => null);
                AssertEqual("cpt_qwen_a", selection.Captain?.Id, "highest-tier same-family captain, never the candidate");
                AssertNotNull(selection.Warning, "warning");
                AssertContains("same model family (qwen)", selection.Warning!);
                AssertContains("no idle captain of another family", selection.Warning!);

                EvaluatorSelection named = EvaluatorSelector.Named("qwen", idle[1], null);
                AssertTrue(named.Explicit, "named");
                AssertContains("named by the administrator", named.Warning ?? "");
                AssertNull(EvaluatorSelector.Named("claude", idle[1], null).Warning, "a named evaluator of another family carries no warning");
                return Task.CompletedTask;
            }));

            cases.Add(Case("evaluator_none_available", "With no idle captain the selection is empty and says why", TestTags.Negative, () =>
            {
                List<Captain> idle = new List<Captain>
                {
                    Idle("cpt_cand", "qwen3:32b", CaptainTierEnum.Premium),
                    Idle("cpt_busy", "claude-opus", CaptainTierEnum.Premium, CaptainStateEnum.Working)
                };
                EvaluatorSelection selection = EvaluatorSelector.Select("qwen", "cpt_cand", idle, c => null);
                AssertNull(selection.Captain, "no evaluator");
                AssertContains("No idle captain", selection.SkippedReason ?? "");
                AssertNull(EvaluatorSelector.Select("qwen", null, new List<Captain>(), c => null).Captain, "empty list");
                return Task.CompletedTask;
            }));

            cases.Add(Case("narrative_accepts_matching_counts", "A reply whose findings repeat the mechanical counts exactly is accepted with its grades, problems and note", TestTags.Positive, () =>
            {
                ModelEvaluationReport report = SampleReport();
                string reply = JsonSerializer.Serialize(new
                {
                    grades = new[] { new { resultId = "btr_plan1", passed = true, note = "Plan is sound." } },
                    strengths = new[] { new { key = "task:evalsyn/review", passed = 3, total = 3, text = "Strong: seeded reviews (3 of 3)." } },
                    weaknesses = new[] { new { key = "task:evalsyn/edit", passed = 0, total = 3, text = "Weak: coordinated two-place edits (0 of 3); output contract probe passed (1 of 1)." } },
                    observedProblems = new[] { "Forgets to register the new test." },
                    howToWork = "Keep each mission to one file."
                });
                ModelEvaluationNarrative narrative = EvaluationNarrativeValidator.Validate("Here is my grading:\n" + reply + "\nThanks.", report, new List<string> { "btr_plan1" });
                AssertEqual(ModelEvaluationNarrativeStatusEnum.Accepted, narrative.Status, "accepted: " + narrative.Reason);
                AssertEqual(1, narrative.Strengths.Count, "strength");
                AssertEqual(3, narrative.Strengths[0].Passed, "passed copied from the count");
                AssertEqual(3, narrative.Weaknesses[0].Total, "total copied from the count");
                AssertEqual("btr_plan1", narrative.SubjectiveGrades.Single().ResultId, "grade");
                AssertEqual("Keep each mission to one file.", narrative.HowToWork, "note");
                AssertEqual(1, narrative.ObservedProblems.Count, "problem");
                return Task.CompletedTask;
            }));

            cases.Add(Case("narrative_rejects_wrong_counts", "A reply that misquotes a count, cites an N of M that is no count, names an unknown count or attempt, is not JSON or is not ASCII is rejected with the reason", TestTags.Negative, () =>
            {
                ModelEvaluationReport report = SampleReport();
                List<string> shown = new List<string> { "btr_plan1" };

                ModelEvaluationNarrative wrongNumbers = EvaluationNarrativeValidator.Validate(JsonSerializer.Serialize(new
                {
                    strengths = new[] { new { key = "task:evalsyn/review", passed = 2, total = 3, text = "Strong: reviews (2 of 3)." } }
                }), report, shown);
                AssertEqual(ModelEvaluationNarrativeStatusEnum.Rejected, wrongNumbers.Status, "wrong structured count");
                AssertContains("as 2 of 3 but the count is 3 of 3", wrongNumbers.Reason ?? "");
                AssertNotNull(wrongNumbers.RejectedOutput, "the rejected reply is kept for the administrator");
                AssertEqual(0, wrongNumbers.Strengths.Count, "nothing of a rejected reply is kept");

                ModelEvaluationNarrative wrongText = EvaluationNarrativeValidator.Validate(JsonSerializer.Serialize(new
                {
                    strengths = new[] { new { key = "task:evalsyn/review", passed = 3, total = 3, text = "Strong: reviews (5 of 7)." } }
                }), report, shown);
                AssertEqual(ModelEvaluationNarrativeStatusEnum.Rejected, wrongText.Status, "text cites a count that does not exist");
                AssertContains("'5 of 7'", wrongText.Reason ?? "");

                ModelEvaluationNarrative wrongProblem = EvaluationNarrativeValidator.Validate(JsonSerializer.Serialize(new
                {
                    strengths = new[] { new { key = "task:evalsyn/review", passed = 3, total = 3, text = "Strong: reviews (3 of 3)." } },
                    observedProblems = new[] { "Reports often need repair (2 of 9)." }
                }), report, shown);
                AssertEqual(ModelEvaluationNarrativeStatusEnum.Rejected, wrongProblem.Status, "an observed problem cites a count that does not exist");

                ModelEvaluationNarrative unknownKey = EvaluationNarrativeValidator.Validate(JsonSerializer.Serialize(new
                {
                    strengths = new[] { new { key = "task:evalsyn/nope", passed = 3, total = 3, text = "Strong (3 of 3)." } }
                }), report, shown);
                AssertContains("unknown count 'task:evalsyn/nope'", unknownKey.Reason ?? "");

                ModelEvaluationNarrative unknownGrade = EvaluationNarrativeValidator.Validate(JsonSerializer.Serialize(new
                {
                    grades = new[] { new { resultId = "btr_other", passed = true } },
                    strengths = new[] { new { key = "task:evalsyn/review", passed = 3, total = 3, text = "Strong (3 of 3)." } }
                }), report, shown);
                AssertContains("was not given to the evaluator", unknownGrade.Reason ?? "");

                AssertContains("no JSON object", EvaluationNarrativeValidator.Validate("I think it did well.", report, shown).Reason ?? "");
                AssertContains("not valid JSON", EvaluationNarrativeValidator.Validate("{ strengths: [ }", report, shown).Reason ?? "");
                AssertContains("no output", EvaluationNarrativeValidator.Validate("", report, shown).Reason ?? "");
                AssertContains("non-ASCII", EvaluationNarrativeValidator.Validate(JsonSerializer.Serialize(new
                {
                    strengths = new[] { new { key = "task:evalsyn/review", passed = 3, total = 3, text = "Strong — reviews (3 of 3)." } }
                }), report, shown).Reason ?? "");
                AssertContains("no strengths or weaknesses", EvaluationNarrativeValidator.Validate("{}", report, shown).Reason ?? "");
                return Task.CompletedTask;
            }));

            cases.Add(DbCase("full_evaluate_report_and_profile", "Full runs the probes and the battery, the evaluator grades the pending plan and writes counts-based strengths, and the report becomes an advisory profile revision; nothing about routing changes", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using TempDir dir = new TempDir();
                DatabaseDriver db = testDb.Driver;
                Harness h = await HarnessAsync(db, dir, new ScriptedDispatcher(), EvaluatorExecutor.Correct()).ConfigureAwait(false);
                Captain candidate = await SaveCaptainAsync(db, "cpt_cand_full", "qwen3:32b", CaptainTierEnum.Economy, allowed: "[\"Judge\",\"Architect\",\"Worker\"]").ConfigureAwait(false);
                await SaveCaptainAsync(db, "cpt_eval_claude", "claude-opus", CaptainTierEnum.Premium).ConfigureAwait(false);
                await SaveCaptainAsync(db, "cpt_eval_qwen", "qwen3.8:27b", CaptainTierEnum.Premium).ConfigureAwait(false);

                ModelProbeRun queued = await h.Probes.StartCaptainAsync(candidate, null, ModelProbeDepthEnum.Full, new ModelEvaluationOptions { Packs = new List<string> { PackId }, RunCount = 1 }, null, null).ConfigureAwait(false);
                AssertEqual(ModelProbeDepthEnum.Full, queued.Depth, "depth");
                ModelProbeRun run = (await h.Probes.WaitAsync(queued.Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Completed, run.Status, "completed: " + run.FailureReason);
                AssertEqual(6, run.Results.Count, "the Quick probes ran first");
                ModelEvaluationReport report = run.Report!;
                AssertNotNull(report, "report");
                AssertTrue(report.Complete, "complete: " + report.IncompleteReason);
                AssertEqual(run.BatteryRunId, report.BatteryRunId, "battery run recorded");
                AssertEqual("qwen", report.CandidateFamily, "candidate family");
                AssertEqual("cpt_eval_claude", report.EvaluatorCaptainId, "evaluator of another family");
                AssertEqual(0, report.Warnings.Count, "no same-family warning");
                AssertEqual(6, report.Probes.Count, "one count per probe");

                ModelEvaluationPersonaOutcome judge = report.PersonaOutcomes.Single(o => o.Persona == "Judge");
                AssertEqual(1, judge.Totals.Passed, "seeded review passed mechanically");
                AssertEqual("task:evalsyn/review", judge.Tasks.Single().Key, "task key");
                ModelEvaluationPersonaOutcome architect = report.PersonaOutcomes.Single(o => o.Persona == "Architect");
                AssertEqual(1, architect.Totals.PendingEvaluation, "the plan waits for the evaluator");
                AssertEqual(0, architect.Totals.Graded, "a subjective grade never changes the mechanical counts");

                AssertEqual(ModelEvaluationNarrativeStatusEnum.Accepted, report.Narrative.Status, "narrative accepted: " + report.Narrative.Reason);
                AssertTrue(report.Narrative.Strengths.Count > 0, "strengths written");
                AssertEqual(1, report.Narrative.SubjectiveGrades.Count, "the pending plan was graded");
                ModelProbeExchangeRequest graded = h.Executor.Requests.Single(r => r.Label == EvaluatorLabel);
                AssertEqual("cpt_eval_claude", graded.Captain.Id, "the evaluator ran the grading");
                AssertContains("rubric: Grade the plan.", graded.Prompt);
                AssertContains("task:evalsyn/review | ", graded.Prompt);

                // Only the selected personas' tasks were dispatched, and every one as evaluation work.
                AssertEqual(2, h.Dispatcher.Requests.Count, "two tasks x one run");
                BatteryRun battery = (await db.BatteryRuns.ReadAsync(run.BatteryRunId!).ConfigureAwait(false))!;
                AssertTrue(battery.IsEvaluation, "battery run flagged isEvaluation");
                AssertEqual(BatteryRunStatusEnum.Completed, battery.Status, "battery completed");

                // The report is an advisory profile revision, compatible with the observed one; routing is untouched.
                AssertNotNull(report.ProfileRevisionId, "revision recorded");
                ModelProfileRevision revision = (await db.ModelProfiles.ReadRevisionAsync(report.ProfileRevisionId!).ConfigureAwait(false))!;
                AssertEqual(run.Id, revision.EvaluationRunId, "revision names the run");
                AssertNotNull(revision.EvaluationReport, "revision carries the report");
                AssertEqual(report.Narrative.Strengths.Count, revision.EvaluationReport!.Narrative.Strengths.Count, "report round-trips");
                AssertEqual(1, revision.CompatibleWith.Count, "pooled with the observed revision");
                Captain after = (await db.Captains.ReadAsync(candidate.Id).ConfigureAwait(false))!;
                AssertEqual(candidate.Tier, after.Tier, "tier unchanged");
                AssertEqual(candidate.AllowedPersonas, after.AllowedPersonas, "allowed personas unchanged");
                AssertEqual(0, h.Probes.Gate.ActiveCount, "slot released");
            }));

            cases.Add(DbCase("full_evaluate_persona_filter_and_default_packs", "Personas default to those the captain is allowed and filter the battery; packs default to the repository languages with a fallback note; a disallowed persona is rejected", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using TempDir dir = new TempDir();
                DatabaseDriver db = testDb.Driver;
                Harness h = await HarnessAsync(db, dir, new ScriptedDispatcher(), EvaluatorExecutor.Correct()).ConfigureAwait(false);
                Captain judgeOnly = await SaveCaptainAsync(db, "cpt_judge_only", "qwen3:32b", CaptainTierEnum.Standard, allowed: "[\"Judge\"]").ConfigureAwait(false);

                ModelProbeRun run = (await h.Probes.WaitAsync((await h.Probes.StartCaptainAsync(judgeOnly, null, ModelProbeDepthEnum.Full, new ModelEvaluationOptions { RunCount = 1 }, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Completed, run.Status, "completed: " + run.FailureReason);
                AssertEqual("Judge", String.Join(",", run.Report!.Personas), "personas default to those allowed");
                AssertEqual(PackId, String.Join(",", run.Report.Packs), "fallback pack");
                AssertTrue(run.Report.Notes.Any(n => n.Contains("No repository language matched")), "fallback noted");
                AssertTrue(h.Dispatcher.Requests.All(r => r.Persona == "Judge"), "only Judge tasks dispatched");
                AssertEqual(ModelEvaluationNarrativeStatusEnum.Skipped, run.Report.Narrative.Status, "no other idle captain");
                AssertContains("No idle captain", run.Report.Narrative.Reason ?? "");
                AssertTrue(run.Report.Complete, "a skipped narrative leaves the mechanical report complete");

                await AssertThrowsAsync<ArgumentException>(() => h.Probes.StartCaptainAsync(judgeOnly, null, ModelProbeDepthEnum.Full,
                    new ModelEvaluationOptions { Personas = new List<string> { "Architect" } }, null, null), "a persona the captain is not allowed").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => h.Probes.StartCaptainAsync(judgeOnly, null, ModelProbeDepthEnum.Full,
                    new ModelEvaluationOptions { Packs = new List<string> { "missing" } }, null, null), "an unknown pack").ConfigureAwait(false);
                await AssertThrowsAsync<ArgumentException>(() => h.Probes.StartCaptainAsync(judgeOnly, null, ModelProbeDepthEnum.Full,
                    new ModelEvaluationOptions { RunCount = 0 }, null, null), "run count out of range").ConfigureAwait(false);
            }));

            cases.Add(DbCase("full_evaluate_wrong_counts_rejected", "An evaluator that cites wrong counts gets its narrative rejected with the reason; the mechanical report and the run stand; a same-family evaluator adds a warning", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using TempDir dir = new TempDir();
                DatabaseDriver db = testDb.Driver;
                Harness h = await HarnessAsync(db, dir, new ScriptedDispatcher(), EvaluatorExecutor.WrongCounts()).ConfigureAwait(false);
                Captain candidate = await SaveCaptainAsync(db, "cpt_cand_wrong", "qwen3:32b", CaptainTierEnum.Economy).ConfigureAwait(false);
                await SaveCaptainAsync(db, "cpt_eval_qwen_only", "qwen3.8:27b", CaptainTierEnum.Premium).ConfigureAwait(false);

                ModelProbeRun run = (await h.Probes.WaitAsync((await h.Probes.StartCaptainAsync(candidate, null, ModelProbeDepthEnum.Full, new ModelEvaluationOptions { Packs = new List<string> { PackId }, RunCount = 1 }, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Completed, run.Status, "the run does not fail");
                AssertTrue(run.Report!.Complete, "mechanical report complete");
                AssertEqual(ModelEvaluationNarrativeStatusEnum.Rejected, run.Report.Narrative.Status, "narrative rejected");
                AssertContains("but the count is", run.Report.Narrative.Reason ?? "");
                AssertEqual(1, run.Report.PersonaOutcomes.Single(o => o.Persona == "Judge").Totals.Passed, "mechanical counts kept");
                AssertEqual("cpt_eval_qwen_only", run.Report.EvaluatorCaptainId, "same-family fallback");
                AssertTrue(run.Report.Warnings.Any(w => w.Contains("same model family")), "warning carried in the report");
            }));

            cases.Add(DbCase("full_evaluate_cancel_incomplete", "Cancelling a Full Evaluate during the battery cancels the open evaluation mission, marks the battery and the run Cancelled and keeps a partial report marked incomplete; the slot is released", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using TempDir dir = new TempDir();
                DatabaseDriver db = testDb.Driver;
                IAdmiralService admiral = RecordingBatteryAdmiral.Create(db);
                RecordingBatteryAdmiral recorder = (RecordingBatteryAdmiral)(object)admiral;
                AdmiralBatteryTaskDispatcher dispatcher = new AdmiralBatteryTaskDispatcher(admiral, db, Quiet(), TimeSpan.FromMilliseconds(50));
                Harness h = await HarnessAsync(db, dir, dispatcher, EvaluatorExecutor.Correct()).ConfigureAwait(false);
                Captain candidate = await SaveCaptainAsync(db, "cpt_cand_cancel", "qwen3:32b", CaptainTierEnum.Economy).ConfigureAwait(false);

                ModelProbeRun queued = await h.Probes.StartCaptainAsync(candidate, null, ModelProbeDepthEnum.Full, new ModelEvaluationOptions { Packs = new List<string> { PackId }, RunCount = 1 }, null, null).ConfigureAwait(false);
                Mission open = await WaitForMissionAsync(db).ConfigureAwait(false);
                AssertTrue(open.IsEvaluation, "battery missions are flagged isEvaluation");
                AssertTrue(h.Probes.Cancel(queued.Id), "running here");

                ModelProbeRun run = (await h.Probes.WaitAsync(queued.Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Cancelled, run.Status, "run cancelled");
                AssertEqual(ModelProbeService.CancelledByUserReason, run.FailureReason, "reason");
                AssertFalse(run.Report!.Complete, "report incomplete");
                AssertContains("did not finish", run.Report.IncompleteReason ?? "");
                AssertEqual(ModelEvaluationNarrativeStatusEnum.Skipped, run.Report.Narrative.Status, "no grading after a cancel");
                AssertEqual(6, run.Report.Probes.Count, "probe counts kept in the partial report");
                AssertTrue(recorder.Cancelled.Contains(open.Id), "the open mission was cancelled (its dock and captain are released)");
                BatteryRun battery = (await db.BatteryRuns.ReadAsync(run.BatteryRunId!).ConfigureAwait(false))!;
                AssertEqual(BatteryRunStatusEnum.Cancelled, battery.Status, "battery cancelled");
                AssertNull(run.Report.ProfileRevisionId, "an incomplete report is not recorded on the profile");
                AssertFalse(h.Executor.Requests.Any(r => r.Label == EvaluatorLabel), "the evaluator never ran");
                AssertEqual(0, h.Probes.Gate.ActiveCount, "slot released");
                AssertFalse(h.Probes.Cancel(queued.Id), "a finished run is not running");
            }));

            cases.Add(DbCase("full_evaluate_timeout_incomplete", "A Full Evaluate that outlives its timeout stops like a cancel: the open mission is cancelled and the partial report says it timed out", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using TempDir dir = new TempDir();
                DatabaseDriver db = testDb.Driver;
                IAdmiralService admiral = RecordingBatteryAdmiral.Create(db);
                RecordingBatteryAdmiral recorder = (RecordingBatteryAdmiral)(object)admiral;
                AdmiralBatteryTaskDispatcher dispatcher = new AdmiralBatteryTaskDispatcher(admiral, db, Quiet(), TimeSpan.FromMilliseconds(50));
                Harness h = await HarnessAsync(db, dir, dispatcher, EvaluatorExecutor.Correct()).ConfigureAwait(false);
                h.Probes.FullRunTimeoutOverride = TimeSpan.FromSeconds(2);
                Captain candidate = await SaveCaptainAsync(db, "cpt_cand_timeout", "qwen3:32b", CaptainTierEnum.Economy).ConfigureAwait(false);

                ModelProbeRun queued = await h.Probes.StartCaptainAsync(candidate, null, ModelProbeDepthEnum.Full, new ModelEvaluationOptions { Packs = new List<string> { PackId }, RunCount = 1 }, null, null).ConfigureAwait(false);
                ModelProbeRun run = (await h.Probes.WaitAsync(queued.Id).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Cancelled, run.Status, "stopped");
                AssertContains("timed out", run.FailureReason ?? "");
                AssertFalse(run.Report!.Complete, "incomplete");
                AssertContains("timed out", run.Report.IncompleteReason ?? "");
                AssertEqual(1, recorder.Cancelled.Count, "the open mission was cancelled");
                AssertEqual(0, h.Probes.Gate.ActiveCount, "slot released");
            }));

            cases.Add(DbCase("full_evaluate_per_host_concurrency", "Two Full Evaluates of models on one inference host run one at a time; a model on another host runs alongside; cancelling a queued run never starts it", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using TempDir dir = new TempDir();
                DatabaseDriver db = testDb.Driver;
                ScriptedDispatcher dispatcher = new ScriptedDispatcher { Block = true };
                EvaluationSettings settings = new EvaluationSettings { MaxConcurrentRuns = 4, MaxConcurrentRunsPerHost = 1 };
                Harness h = await HarnessAsync(db, dir, dispatcher, EvaluatorExecutor.Correct(), settings).ConfigureAwait(false);
                ModelEndpoint gb10a = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "gb10-a", BaseUrl = "http://gb10.lan:11434", Model = "qwen3:32b", TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                ModelEndpoint gb10b = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "gb10-b", BaseUrl = "http://GB10.lan:11500", Model = "gpt-oss:20b", TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                ModelEndpoint other = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "other", BaseUrl = "http://other.lan:11434", Model = "deepseek-v4", TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);
                Captain a = await SaveCaptainAsync(db, "cpt_host_a", "qwen3:32b", CaptainTierEnum.Economy, endpointId: gb10a.Id).ConfigureAwait(false);
                Captain b = await SaveCaptainAsync(db, "cpt_host_b", "gpt-oss:20b", CaptainTierEnum.Economy, endpointId: gb10b.Id).ConfigureAwait(false);
                Captain c = await SaveCaptainAsync(db, "cpt_host_c", "deepseek-v4", CaptainTierEnum.Economy, endpointId: other.Id).ConfigureAwait(false);
                // Battery runs of one pack share its fixture lock, so the run on the other host uses another pack.
                ModelEvaluationOptions options = new ModelEvaluationOptions { Packs = new List<string> { PackId }, RunCount = 1 };
                ModelEvaluationOptions otherOptions = new ModelEvaluationOptions { Packs = new List<string> { OtherPackId }, RunCount = 1 };

                ModelProbeRun runA = await h.Probes.StartCaptainAsync(a, gb10a, ModelProbeDepthEnum.Full, options, null, null).ConfigureAwait(false);
                await dispatcher.WaitForRequestsAsync(1).ConfigureAwait(false);
                ModelProbeRun runB = await h.Probes.StartCaptainAsync(b, gb10b, ModelProbeDepthEnum.Full, options, null, null).ConfigureAwait(false);
                ModelProbeRun runC = await h.Probes.StartCaptainAsync(c, other, ModelProbeDepthEnum.Full, otherOptions, null, null).ConfigureAwait(false);
                await dispatcher.WaitForRequestsAsync(2).ConfigureAwait(false);
                await Task.Delay(300).ConfigureAwait(false);

                AssertEqual(runA.InferenceHost, runB.InferenceHost, "one machine, two ports, one host key");
                AssertEqual(1, h.Probes.Gate.ActiveOn("gb10.lan"), "one Evaluate on the shared host");
                AssertEqual(1, h.Probes.Gate.ActiveOn("other.lan"), "the other host runs alongside");
                AssertEqual(ModelProbeRunStatusEnum.Queued, (await db.ModelProbeRuns.ReadAsync(runB.Id).ConfigureAwait(false))!.Status, "the second model on the shared host waits");
                AssertFalse(dispatcher.Requests.Any(r => r.Captain.Id == b.Id), "nothing of the waiting run was dispatched");

                AssertTrue(h.Probes.Cancel(runB.Id), "cancel the queued run");
                ModelProbeRun cancelledB = (await h.Probes.WaitAsync(runB.Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Cancelled, cancelledB.Status, "cancelled while queued");
                AssertFalse(cancelledB.Report!.Complete, "incomplete report");
                AssertContains("before it started", cancelledB.Report.IncompleteReason ?? "");
                AssertEqual(0, cancelledB.Results.Count, "no probe ran");

                h.Probes.Cancel(runA.Id);
                h.Probes.Cancel(runC.Id);
                await h.Probes.WaitAsync(runA.Id).ConfigureAwait(false);
                await h.Probes.WaitAsync(runC.Id).ConfigureAwait(false);
                AssertEqual(0, h.Probes.Gate.ActiveCount, "every slot released");
            }));

            cases.Add(DbCase("full_evaluate_endpoint_without_captain", "A Full Evaluate of a model endpoint that no captain drives runs the probes and reports the battery as could not run", async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                using TempDir dir = new TempDir();
                DatabaseDriver db = testDb.Driver;
                Harness h = await HarnessAsync(db, dir, new ScriptedDispatcher(), EvaluatorExecutor.Correct()).ConfigureAwait(false);
                ModelEndpoint endpoint = await db.ModelEndpoints.CreateAsync(new ModelEndpoint { Name = "lonely", BaseUrl = "http://lonely.lan:11434", Model = "qwen3:8b", TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);

                ModelProbeRun run = (await h.Probes.WaitAsync((await h.Probes.StartEndpointAsync(endpoint, ModelProbeDepthEnum.Full, new ModelEvaluationOptions { Packs = new List<string> { PackId } }, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Completed, run.Status, "probes completed");
                AssertFalse(run.Report!.Complete, "incomplete without a battery");
                AssertContains("No captain drives model endpoint lonely", run.Report.IncompleteReason ?? "");
                AssertEqual(0, h.Dispatcher.Requests.Count, "nothing dispatched");
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Services: Full Evaluate (battery, evaluator, advisory profile)", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static Captain Idle(string id, string model, CaptainTierEnum? tier, CaptainStateEnum state = CaptainStateEnum.Idle, bool quarantined = false)
        {
            return new Captain(id)
            {
                Id = id,
                Runtime = AgentRuntimeEnum.ApiEndpoint,
                Model = model,
                Tier = tier,
                State = state,
                QuarantineUntilUtc = quarantined ? DateTime.UtcNow.AddHours(1) : null
            };
        }

        private static ModelEvaluationReport SampleReport()
        {
            ModelEvaluationReport report = new ModelEvaluationReport { RunId = "mpr_sample" };
            report.Probes.Add(new ModelEvaluationCount { Key = "probe:OutputContract", Label = "Probe: OutputContract", Passed = 1 });
            ModelEvaluationPersonaOutcome worker = new ModelEvaluationPersonaOutcome
            {
                Persona = "Worker",
                Totals = new ModelEvaluationCount { Key = "persona:Worker", Passed = 3, Failed = 3 }
            };
            worker.Tasks.Add(new ModelEvaluationCount { Key = "task:evalsyn/edit", Failed = 3 });
            worker.Tasks.Add(new ModelEvaluationCount { Key = "task:evalsyn/review", Passed = 3 });
            report.PersonaOutcomes.Add(worker);
            return report;
        }

        private static async Task<Harness> HarnessAsync(DatabaseDriver db, TempDir dir, IBatteryTaskDispatcher dispatcher, EvaluatorExecutor executor, EvaluationSettings? settings = null)
        {
            WritePack(dir, PackId);
            WritePack(dir, OtherPackId);
            if (await db.Pipelines.ReadByNameAsync(BatteryFixtureService.PipelineName).ConfigureAwait(false) == null)
                await db.Pipelines.CreateAsync(new Pipeline(BatteryFixtureService.PipelineName) { IsBuiltIn = true, TenantId = Constants.DefaultTenantId }).ConfigureAwait(false);

            EvaluationSettings effective = settings ?? new EvaluationSettings();
            LoggingModule logging = Quiet();
            ModelProbeService probes = new ModelProbeService(db, logging, executor, () => effective);
            BatteryRunService battery = new BatteryRunService(db, logging, new BatteryPackCatalog(() => Path.Combine(dir.Path, "packs")),
                new BatteryFixtureService(db, logging, Path.Combine(dir.Path, "data")), dispatcher, () => effective, probes.Gate);
            probes.Battery = battery;
            return new Harness(probes, executor, dispatcher as ScriptedDispatcher ?? new ScriptedDispatcher());
        }

        private static async Task<Captain> SaveCaptainAsync(DatabaseDriver db, string id, string model, CaptainTierEnum? tier, string? allowed = null, string? endpointId = null)
        {
            Captain captain = new Captain(id)
            {
                Id = id,
                TenantId = Constants.DefaultTenantId,
                Runtime = endpointId != null ? AgentRuntimeEnum.ApiEndpoint : AgentRuntimeEnum.Mux,
                Model = model,
                Tier = tier,
                AllowedPersonas = allowed,
                ModelEndpointId = endpointId
            };
            return await db.Captains.CreateAsync(captain).ConfigureAwait(false);
        }

        private static async Task<Mission> WaitForMissionAsync(DatabaseDriver db)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                List<Mission> missions = await db.Missions.EnumerateAsync().ConfigureAwait(false);
                if (missions.Count > 0) return missions[0];
                await Task.Delay(20).ConfigureAwait(false);
            }

            throw new TimeoutException("no battery mission was dispatched");
        }

        private static void WritePack(TempDir dir, string packId)
        {
            string root = "packs/" + packId + "/";
            dir.Write(root + "battery-pack.json", @"{
              ""schemaVersion"": 1, ""id"": """ + packId + @""", ""name"": ""Evaluate synthetic"", ""version"": 1, ""domain"": ""test"",
              ""fixture"": { ""path"": ""fixture"" },
              ""toolchain"": [ { ""name"": ""git"", ""executable"": ""git"", ""arguments"": [ ""--version"" ] } ],
              ""tasks"": [
                { ""id"": ""review"", ""title"": ""Review"", ""persona"": ""Judge"", ""toolchain"": [], ""prompt"": ""Review it."", ""attachments"": [ ""change.diff"" ],
                  ""grader"": { ""type"": ""seededSet"", ""expectedVerdicts"": [ ""FAIL"" ], ""blockingSections"": [ ""Affected Case"" ],
                    ""seeded"": [ { ""id"": ""obo"", ""keywords"": [ ""off-by-one"" ] } ] } },
                { ""id"": ""plan"", ""title"": ""Plan"", ""persona"": ""Architect"", ""toolchain"": [], ""prompt"": ""Plan it."",
                  ""grader"": { ""type"": ""rubric"", ""schema"": ""armada-plan"", ""rubric"": ""rubric.md"" } }
              ]
            }");
            dir.Write(root + "fixture/readme.txt", "fixture\n");
            dir.Write(root + "change.diff", "+        for (int i = 1; i < items.Count; i++)\n");
            dir.Write(root + "rubric.md", "Grade the plan.\n");
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

            throw new Exception("Expected " + typeof(T).Name + ": " + label);
        }

        private static LoggingModule Quiet()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor DbCase(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Positive, TestTags.Database });
        }

        #endregion

        #region Private-Classes

        private sealed class Harness
        {
            public ModelProbeService Probes { get; }
            public EvaluatorExecutor Executor { get; }
            public ScriptedDispatcher Dispatcher { get; }

            public Harness(ModelProbeService probes, EvaluatorExecutor executor, ScriptedDispatcher dispatcher)
            {
                Probes = probes;
                Executor = executor;
                Dispatcher = dispatcher;
            }
        }

        /// <summary>
        /// Answers every probe with plain text and the evaluator's grading with a reply built from the prompt's counts.
        /// </summary>
        private sealed class EvaluatorExecutor : IModelProbeExecutor
        {
            private readonly bool _WrongCounts;
            private readonly object _Lock = new object();

            public List<ModelProbeExchangeRequest> Requests { get; } = new List<ModelProbeExchangeRequest>();

            private EvaluatorExecutor(bool wrongCounts)
            {
                _WrongCounts = wrongCounts;
            }

            public static EvaluatorExecutor Correct()
            {
                return new EvaluatorExecutor(false);
            }

            public static EvaluatorExecutor WrongCounts()
            {
                return new EvaluatorExecutor(true);
            }

            public Task<ModelProbeExchange> RunAsync(ModelProbeExchangeRequest request, CancellationToken token = default)
            {
                lock (_Lock) Requests.Add(request);
                if (request.Label != EvaluatorLabel)
                    return Task.FromResult(new ModelProbeExchange { Output = "READY", FirstOutputMs = 10, DurationMs = 20 });
                return Task.FromResult(new ModelProbeExchange { Output = Reply(request.Prompt), DurationMs = 50 });
            }

            private string Reply(string prompt)
            {
                List<object> strengths = new List<object>();
                List<object> grades = new List<object>();
                bool inCounts = false;
                foreach (string line in prompt.Split('\n'))
                {
                    if (line.StartsWith("COUNTS", StringComparison.Ordinal)) { inCounts = true; continue; }
                    if (inCounts && line.Trim().Length == 0) inCounts = false;
                    if (inCounts && strengths.Count == 0)
                    {
                        string[] parts = line.Split(" | ");
                        int passed = Int32.Parse(parts[2]);
                        int total = Int32.Parse(parts[3]);
                        if (total == 0) continue;
                        int cited = _WrongCounts ? passed + 1 : passed;
                        strengths.Add(new { key = parts[0], passed = cited, total = total, text = parts[1] + " (" + cited + " of " + total + ")." });
                    }

                    if (line.StartsWith("--- resultId: ", StringComparison.Ordinal))
                        grades.Add(new { resultId = line.Substring("--- resultId: ".Length).Trim(), passed = true, note = "Meets the rubric." });
                }

                return JsonSerializer.Serialize(new { grades = grades, strengths = strengths, weaknesses = new object[0], observedProblems = new string[0], howToWork = "Give it one task at a time." });
            }
        }

        /// <summary>
        /// Answers each battery task with a mission whose output passes the seeded review and the plan schema, or blocks
        /// until cancelled (as the Admiral dispatcher does for a stopped Full Evaluate).
        /// </summary>
        private sealed class ScriptedDispatcher : IBatteryTaskDispatcher
        {
            private readonly object _Lock = new object();

            public List<BatteryDispatchRequest> Requests { get; } = new List<BatteryDispatchRequest>();
            public bool Block { get; set; } = false;

            public async Task<BatteryDispatchResult> RunAsync(BatteryDispatchRequest request, CancellationToken token = default)
            {
                lock (_Lock) Requests.Add(request);
                Mission mission = new Mission(request.Title, request.Description) { Persona = request.Persona, CaptainId = request.Captain.Id, IsEvaluation = true };
                if (Block)
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        mission.Status = MissionStatusEnum.Cancelled;
                        return new BatteryDispatchResult { VoyageId = "vyg_blocked", Mission = mission, Cancelled = true };
                    }
                }

                mission.Status = MissionStatusEnum.WorkProduced;
                mission.AgentOutput = request.Persona == "Judge"
                    ? "## Affected Case\nScenario: the loop is off-by-one and skips the first item.\n\n[ARMADA:VERDICT] FAIL"
                    : "```armada-plan\n{\"missions\":[{\"title\":\"Parser\",\"description\":\"parse\"}]}\n```";
                return new BatteryDispatchResult { VoyageId = "vyg_scripted", Mission = mission };
            }

            public async Task WaitForRequestsAsync(int count)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    lock (_Lock)
                    {
                        if (Requests.Count >= count) return;
                    }

                    await Task.Delay(20).ConfigureAwait(false);
                }

                throw new TimeoutException("expected " + count + " battery dispatches");
            }
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "armada-evaluate-" + Guid.NewGuid().ToString("N"));

            public TempDir()
            {
                Directory.CreateDirectory(Path);
            }

            public void Write(string relative, string content)
            {
                string full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, Encoding.UTF8.GetBytes(content));
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Path)) Directory.Delete(Path, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        #endregion
    }
}
