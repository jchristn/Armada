namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the model_probe_runs and model_probe_results tables (migration 94): round trip of fully and
    /// minimally populated runs and results (including every ModelProbeMetrics field through metrics_json), updates,
    /// filtered and paged newest-first enumeration, the evaluation token sum overall and per inference host, result
    /// ordering, and deletes that take a run's results with it.
    /// </summary>
    public sealed class ModelProbePersistenceSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.ModelProbePersistence";
        private static readonly DateTime _Fixed = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("run_roundtrip_all_fields", "A fully populated run round-trips every persisted field", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun run = FullRun();
                await db.ModelProbeRuns.CreateAsync(run).ConfigureAwait(false);

                ModelProbeRun? read = await db.ModelProbeRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertTrue(read!.Id.StartsWith("mpr_", StringComparison.Ordinal), "mpr_ prefix: " + read.Id);
                AssertEqual("ten_full", read.TenantId);
                AssertEqual("usr_full", read.UserId);
                AssertEqual(ModelProbeTargetEnum.ModelEndpoint, read.TargetKind);
                AssertEqual("cpt_full", read.CaptainId);
                AssertEqual("Full captain", read.CaptainName);
                AssertEqual("mep_full", read.ModelEndpointId);
                AssertEqual(ModelProbeDepthEnum.Full, read.Depth);
                AssertEqual(ModelProbeRunStatusEnum.Completed, read.Status);
                AssertFalse(read.IsEvaluation, "is_evaluation false persisted");
                AssertEqual("quick-1", read.ProbeSetVersion);
                AssertEqual("ApiEndpoint", read.Runtime);
                AssertEqual("1.2.3", read.RuntimeVersion);
                AssertEqual("Ollama", read.ModelProvider);
                AssertEqual("qwen3:32b", read.Model);
                AssertEqual("sha256:abc", read.ModelDigest);
                AssertEqual((int?)32768, read.ConfiguredContextTokens);
                AssertEqual((int?)8192, read.ServedContextTokens);
                AssertEqual("hbr_full", read.HarborId);
                AssertEqual("gb10.local:11434", read.InferenceHost);
                AssertEqual(500000L, read.TokenBudget);
                AssertEqual(12345L, read.TokensUsed);
                AssertEqual(4, read.PassedCount);
                AssertEqual(1, read.FailedCount);
                AssertEqual(1, read.CouldNotRunCount);
                AssertEqual("served context mismatch", read.FailureReason);
                AssertSameInstant(_Fixed, read.CreatedUtc, "created");
                AssertSameInstant(_Fixed.AddSeconds(5), read.StartedUtc!.Value, "started");
                AssertSameInstant(_Fixed.AddMinutes(3), read.CompletedUtc!.Value, "completed");
                AssertEqual(0, read.Results.Count, "ReadAsync returns no results");

                AssertNull(await db.ModelProbeRuns.ReadAsync("mpr_missing").ConfigureAwait(false), "missing id");
            }));

            cases.Add(CaseAsync("run_full_report_roundtrip", "A Full run's battery run id and report (migration 109) round-trip on create and update; a Quick run keeps them null", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun quick = await db.ModelProbeRuns.CreateAsync(new ModelProbeRun()).ConfigureAwait(false);
                ModelProbeRun quickRead = (await db.ModelProbeRuns.ReadAsync(quick.Id).ConfigureAwait(false))!;
                AssertNull(quickRead.BatteryRunId, "no battery run");
                AssertNull(quickRead.Report, "no report");

                ModelProbeRun run = new ModelProbeRun { Depth = ModelProbeDepthEnum.Full, Status = ModelProbeRunStatusEnum.Cancelled, BatteryRunId = "bat_full" };
                run.Report = new ModelEvaluationReport { RunId = run.Id, Complete = false, IncompleteReason = "Cancelled by an administrator.", EvaluatorCaptainId = "cpt_eval" };
                run.Report.Warnings.Add("same family");
                run.Report.PersonaOutcomes.Add(new ModelEvaluationPersonaOutcome { Persona = "Worker", Totals = new ModelEvaluationCount { Key = "persona:Worker", Passed = 2, Failed = 1 } });
                run.Report.Narrative = new ModelEvaluationNarrative { Status = ModelEvaluationNarrativeStatusEnum.Rejected, Reason = "wrong count", RejectedOutput = "{}" };
                await db.ModelProbeRuns.CreateAsync(run).ConfigureAwait(false);

                ModelProbeRun read = (await db.ModelProbeRuns.ReadAsync(run.Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Cancelled, read.Status, "cancelled status");
                AssertEqual("bat_full", read.BatteryRunId, "battery run id");
                AssertNotNull(read.Report, "report");
                AssertFalse(read.Report!.Complete, "incomplete");
                AssertEqual("Cancelled by an administrator.", read.Report.IncompleteReason, "reason");
                AssertEqual("cpt_eval", read.Report.EvaluatorCaptainId, "evaluator");
                AssertEqual("same family", read.Report.Warnings.Single(), "warning");
                AssertEqual(3, read.Report.PersonaOutcomes.Single().Totals.Graded, "counts");
                AssertEqual(ModelEvaluationNarrativeStatusEnum.Rejected, read.Report.Narrative.Status, "narrative status");

                read.Report.Complete = true;
                read.Report.IncompleteReason = null;
                read.BatteryRunId = "bat_other";
                await db.ModelProbeRuns.UpdateAsync(read).ConfigureAwait(false);
                ModelProbeRun updated = (await db.ModelProbeRuns.ReadAsync(run.Id).ConfigureAwait(false))!;
                AssertTrue(updated.Report!.Complete, "report updated");
                AssertNull(updated.Report.IncompleteReason, "reason cleared");
                AssertEqual("bat_other", updated.BatteryRunId, "battery run id updated");
            }));

            cases.Add(CaseAsync("run_roundtrip_minimal", "A minimally populated run keeps defaults and nulls", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun run = new ModelProbeRun();
                await db.ModelProbeRuns.CreateAsync(run).ConfigureAwait(false);

                ModelProbeRun? read = await db.ModelProbeRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertNull(read!.TenantId, "tenant");
                AssertNull(read.UserId, "user");
                AssertEqual(ModelProbeTargetEnum.Captain, read.TargetKind);
                AssertNull(read.CaptainId, "captain");
                AssertNull(read.CaptainName, "captain name");
                AssertNull(read.ModelEndpointId, "endpoint");
                AssertEqual(ModelProbeDepthEnum.Quick, read.Depth);
                AssertEqual(ModelProbeRunStatusEnum.Queued, read.Status);
                AssertTrue(read.IsEvaluation, "evaluation by default");
                AssertEqual("", read.ProbeSetVersion);
                AssertNull(read.Runtime, "runtime");
                AssertNull(read.RuntimeVersion, "runtime version");
                AssertNull(read.ModelProvider, "provider");
                AssertNull(read.Model, "model");
                AssertNull(read.ModelDigest, "digest");
                AssertNull(read.ConfiguredContextTokens, "configured context");
                AssertNull(read.ServedContextTokens, "served context");
                AssertNull(read.HarborId, "harbor");
                AssertNull(read.InferenceHost, "host");
                AssertEqual(0L, read.TokenBudget);
                AssertEqual(0L, read.TokensUsed);
                AssertEqual(0, read.PassedCount);
                AssertEqual(0, read.FailedCount);
                AssertEqual(0, read.CouldNotRunCount);
                AssertNull(read.FailureReason, "failure reason");
                AssertNull(read.StartedUtc, "started");
                AssertNull(read.CompletedUtc, "completed");
                AssertSameInstant(run.CreatedUtc, read.CreatedUtc, "created");
            }));

            cases.Add(CaseAsync("result_roundtrip_all_fields", "A fully populated result round-trips every field including every metric", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun run = await db.ModelProbeRuns.CreateAsync(FullRun()).ConfigureAwait(false);
                ModelProbeResult result = new ModelProbeResult();
                result.RunId = run.Id;
                result.TenantId = "ten_full";
                result.Probe = ModelProbeKindEnum.Speed;
                result.Outcome = ModelProbeOutcomeEnum.Failed;
                result.Summary = "slow";
                result.Reason = "first token after 40s";
                result.Attempts = 2;
                result.TokensUsed = 4096;
                result.DurationMs = 61000;
                result.OutputExcerpt = "[ARMADA:RESULT] COMPLETE";
                result.CreatedUtc = _Fixed;
                result.Metrics = FullMetrics();
                await db.ModelProbeResults.CreateAsync(result).ConfigureAwait(false);

                List<ModelProbeResult> rows = await db.ModelProbeResults.ReadByRunAsync(run.Id).ConfigureAwait(false);
                AssertEqual(1, rows.Count, "one result");
                ModelProbeResult read = rows[0];
                AssertEqual(result.Id, read.Id);
                AssertTrue(read.Id.StartsWith("mpo_", StringComparison.Ordinal), "mpo_ prefix: " + read.Id);
                AssertEqual(run.Id, read.RunId);
                AssertEqual("ten_full", read.TenantId);
                AssertEqual(ModelProbeKindEnum.Speed, read.Probe);
                AssertEqual(ModelProbeOutcomeEnum.Failed, read.Outcome);
                AssertEqual("slow", read.Summary);
                AssertEqual("first token after 40s", read.Reason);
                AssertEqual(2, read.Attempts);
                AssertEqual(4096L, read.TokensUsed);
                AssertEqual(61000L, read.DurationMs);
                AssertEqual("[ARMADA:RESULT] COMPLETE", read.OutputExcerpt);
                AssertSameInstant(_Fixed, read.CreatedUtc, "created");

                ModelProbeMetrics m = read.Metrics;
                AssertEqual((int?)32768, m.ConfiguredContextTokens);
                AssertEqual((int?)8192, m.ServedContextTokens);
                AssertEqual((bool?)true, m.ContextMismatch);
                AssertEqual((int?)3, m.Compliant);
                AssertEqual((int?)4, m.Checked);
                AssertEqual((int?)1, m.RepairNeeded);
                AssertEqual((bool?)true, m.WorkerResultCompliant);
                AssertEqual((bool?)false, m.JudgeVerdictCompliant);
                AssertEqual((bool?)true, m.JudgeSectionsCompliant);
                AssertEqual((bool?)false, m.ArchitectPlanCompliant);
                AssertEqual((int?)7, m.Violations);
                AssertEqual((int?)2, m.EmDashes);
                AssertEqual((int?)3, m.SmartQuotes);
                AssertEqual((int?)1, m.AnsiCodes);
                AssertEqual((int?)1, m.OtherNonAscii);
                AssertEqual((int?)2, m.MalformedCalls);
                AssertEqual((bool?)true, m.ErrorRecovered);
                AssertEqual((bool?)false, m.Looping);
                AssertEqual((long?)1500L, m.FirstOutputMs4k);
                AssertEqual((double?)42.5, m.OutputTokensPerSecond4k);
                AssertEqual((long?)9000L, m.FirstOutputMs32k);
                AssertEqual((double?)17.25, m.OutputTokensPerSecond32k);
                AssertEqual((int?)600, m.SuggestedRequestTimeoutSeconds);
                AssertEqual((int?)120, m.SuggestedInactivityTimeoutSeconds);
                AssertEqual((int?)300, m.SuggestedStallTimeoutSeconds);
                AssertEqual((int?)2, m.DistinctOutputs);
                AssertEqual((bool?)true, m.AnswerAgreement);
                AssertEqual((double?)0.875, m.MinSimilarity);
            }));

            cases.Add(CaseAsync("result_roundtrip_minimal", "A minimally populated result keeps defaults, null strings and null metrics", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun run = await db.ModelProbeRuns.CreateAsync(new ModelProbeRun()).ConfigureAwait(false);
                ModelProbeResult result = new ModelProbeResult();
                result.RunId = run.Id;
                await db.ModelProbeResults.CreateAsync(result).ConfigureAwait(false);

                List<ModelProbeResult> rows = await db.ModelProbeResults.ReadByRunAsync(run.Id).ConfigureAwait(false);
                AssertEqual(1, rows.Count, "one result");
                ModelProbeResult read = rows[0];
                AssertNull(read.TenantId, "tenant");
                AssertEqual(ModelProbeKindEnum.ServedContext, read.Probe);
                AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, read.Outcome);
                AssertNull(read.Summary, "summary");
                AssertNull(read.Reason, "reason");
                AssertNull(read.OutputExcerpt, "excerpt");
                AssertEqual(0, read.Attempts);
                AssertEqual(0L, read.TokensUsed);
                AssertEqual(0L, read.DurationMs);
                AssertNotNull(read.Metrics, "metrics never null");
                AssertNull(read.Metrics.ConfiguredContextTokens, "metric configured context");
                AssertNull(read.Metrics.ContextMismatch, "metric mismatch");
                AssertNull(read.Metrics.OutputTokensPerSecond4k, "metric tps");
                AssertNull(read.Metrics.MinSimilarity, "metric similarity");
                AssertNull(read.Metrics.Looping, "metric looping");
            }));

            cases.Add(CaseAsync("run_update", "UpdateAsync changes mutable fields and keeps tenant, user and creation time", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun run = NewRun("ten_u", "cpt_u", null, _Fixed);
                run.UserId = "usr_u";
                await db.ModelProbeRuns.CreateAsync(run).ConfigureAwait(false);
                DateTime beforeUpdate = DateTime.UtcNow.AddSeconds(-1);

                run.TenantId = "ten_other";
                run.UserId = "usr_other";
                run.CreatedUtc = _Fixed.AddDays(10);
                run.Status = ModelProbeRunStatusEnum.Failed;
                run.FailureReason = "budget exhausted";
                run.TokensUsed = 999;
                run.PassedCount = 2;
                run.FailedCount = 3;
                run.CouldNotRunCount = 1;
                run.ServedContextTokens = 4096;
                run.InferenceHost = "host-b";
                run.RuntimeVersion = "9.9";
                run.ModelDigest = "sha256:new";
                run.StartedUtc = _Fixed.AddSeconds(1);
                run.CompletedUtc = _Fixed.AddSeconds(2);
                await db.ModelProbeRuns.UpdateAsync(run).ConfigureAwait(false);

                ModelProbeRun? read = await db.ModelProbeRuns.ReadAsync(run.Id).ConfigureAwait(false);
                AssertNotNull(read, "read back");
                AssertEqual("ten_u", read!.TenantId, "tenant kept");
                AssertEqual("usr_u", read.UserId, "user kept");
                AssertSameInstant(_Fixed, read.CreatedUtc, "created kept");
                AssertEqual(ModelProbeRunStatusEnum.Failed, read.Status);
                AssertEqual("budget exhausted", read.FailureReason);
                AssertEqual(999L, read.TokensUsed);
                AssertEqual(2, read.PassedCount);
                AssertEqual(3, read.FailedCount);
                AssertEqual(1, read.CouldNotRunCount);
                AssertEqual((int?)4096, read.ServedContextTokens);
                AssertEqual("host-b", read.InferenceHost);
                AssertEqual("9.9", read.RuntimeVersion);
                AssertEqual("sha256:new", read.ModelDigest);
                AssertSameInstant(_Fixed.AddSeconds(1), read.StartedUtc!.Value, "started");
                AssertSameInstant(_Fixed.AddSeconds(2), read.CompletedUtc!.Value, "completed");
                AssertTrue(read.LastUpdateUtc >= beforeUpdate, "last update stamped: " + read.LastUpdateUtc.ToString("o"));
            }));

            cases.Add(CaseAsync("enumerate_filters", "EnumerateAsync filters by tenant, captain, endpoint, status and fromUtc", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun a = NewRun("ten_a", "cpt_1", null, _Fixed);
                ModelProbeRun b = NewRun("ten_a", "cpt_2", "mep_1", _Fixed.AddMinutes(1));
                b.Status = ModelProbeRunStatusEnum.Completed;
                ModelProbeRun c = NewRun("ten_b", "cpt_1", "mep_1", _Fixed.AddMinutes(2));
                c.Status = ModelProbeRunStatusEnum.Completed;
                foreach (ModelProbeRun r in new ModelProbeRun[] { a, b, c }) await db.ModelProbeRuns.CreateAsync(r).ConfigureAwait(false);

                AssertIds(await ListAsync(db, new ModelProbeRunQuery()).ConfigureAwait(false), "all newest first", c.Id, b.Id, a.Id);
                AssertIds(await ListAsync(db, new ModelProbeRunQuery { TenantId = "ten_a" }).ConfigureAwait(false), "tenant", b.Id, a.Id);
                AssertIds(await ListAsync(db, new ModelProbeRunQuery { CaptainId = "cpt_1" }).ConfigureAwait(false), "captain", c.Id, a.Id);
                AssertIds(await ListAsync(db, new ModelProbeRunQuery { ModelEndpointId = "mep_1" }).ConfigureAwait(false), "endpoint", c.Id, b.Id);
                AssertIds(await ListAsync(db, new ModelProbeRunQuery { Status = ModelProbeRunStatusEnum.Completed }).ConfigureAwait(false), "status", c.Id, b.Id);
                AssertIds(await ListAsync(db, new ModelProbeRunQuery { Status = ModelProbeRunStatusEnum.Running }).ConfigureAwait(false), "status none");
                AssertIds(await ListAsync(db, new ModelProbeRunQuery { FromUtc = _Fixed.AddMinutes(1) }).ConfigureAwait(false), "from inclusive", c.Id, b.Id);
                AssertIds(await ListAsync(db, new ModelProbeRunQuery { TenantId = "ten_a", CaptainId = "cpt_1" }).ConfigureAwait(false), "combined", a.Id);
                AssertIds(await ListAsync(db, new ModelProbeRunQuery { TenantId = "ten_none" }).ConfigureAwait(false), "unknown tenant");

                EnumerationResult<ModelProbeRun> empty = await db.ModelProbeRuns.EnumerateAsync(new ModelProbeRunQuery { TenantId = "ten_none" }).ConfigureAwait(false);
                AssertEqual(0L, empty.TotalRecords, "empty total");
                AssertEqual(0, empty.TotalPages, "empty pages");
            }));

            cases.Add(CaseAsync("enumerate_paging_and_order", "EnumerateAsync pages newest first with totals; same-instant rows order by id descending; list rows have no results", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                List<ModelProbeRun> created = new List<ModelProbeRun>();
                for (int i = 0; i < 5; i++)
                {
                    ModelProbeRun run = NewRun("ten_p", "cpt_p", null, _Fixed.AddMinutes(i));
                    run.Id = "mpr_page" + i.ToString();
                    created.Add(await db.ModelProbeRuns.CreateAsync(run).ConfigureAwait(false));
                }

                ModelProbeRun tieLow = NewRun("ten_tie", null, null, _Fixed);
                tieLow.Id = "mpr_tie_a";
                ModelProbeRun tieHigh = NewRun("ten_tie", null, null, _Fixed);
                tieHigh.Id = "mpr_tie_b";
                await db.ModelProbeRuns.CreateAsync(tieLow).ConfigureAwait(false);
                await db.ModelProbeRuns.CreateAsync(tieHigh).ConfigureAwait(false);

                ModelProbeResult child = new ModelProbeResult();
                child.RunId = created[4].Id;
                await db.ModelProbeResults.CreateAsync(child).ConfigureAwait(false);

                EnumerationResult<ModelProbeRun> page1 = await db.ModelProbeRuns.EnumerateAsync(new ModelProbeRunQuery { TenantId = "ten_p", PageNumber = 1, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(5L, page1.TotalRecords, "total");
                AssertEqual(3, page1.TotalPages, "pages");
                AssertEqual(1, page1.PageNumber, "page number");
                AssertEqual(2, page1.PageSize, "page size");
                AssertIds(page1.Objects, "page 1", "mpr_page4", "mpr_page3");
                AssertEqual(0, page1.Objects[0].Results.Count, "list rows carry no results");

                EnumerationResult<ModelProbeRun> page3 = await db.ModelProbeRuns.EnumerateAsync(new ModelProbeRunQuery { TenantId = "ten_p", PageNumber = 3, PageSize = 2 }).ConfigureAwait(false);
                AssertIds(page3.Objects, "page 3", "mpr_page0");
                AssertEqual(5L, page3.TotalRecords, "total on last page");

                EnumerationResult<ModelProbeRun> beyond = await db.ModelProbeRuns.EnumerateAsync(new ModelProbeRunQuery { TenantId = "ten_p", PageNumber = 4, PageSize = 2 }).ConfigureAwait(false);
                AssertEqual(0, beyond.Objects.Count, "beyond last page");

                AssertIds(await ListAsync(db, new ModelProbeRunQuery { TenantId = "ten_tie" }).ConfigureAwait(false), "tie by id desc", "mpr_tie_b", "mpr_tie_a");
            }));

            cases.Add(CaseAsync("sum_tokens_since", "SumTokensUsedSinceAsync sums overall and per host from a cutoff and returns 0 when none", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                AssertEqual(0L, await db.ModelProbeRuns.SumTokensUsedSinceAsync(_Fixed.AddDays(-1), null).ConfigureAwait(false), "empty table");

                ModelProbeRun old = NewRun("ten_s", null, null, _Fixed.AddHours(-2));
                old.InferenceHost = "host-a";
                old.TokensUsed = 1000;
                ModelProbeRun a1 = NewRun("ten_s", null, null, _Fixed);
                a1.InferenceHost = "host-a";
                a1.TokensUsed = 200;
                ModelProbeRun a2 = NewRun("ten_s2", null, null, _Fixed.AddMinutes(5));
                a2.InferenceHost = "host-a";
                a2.TokensUsed = 30;
                ModelProbeRun b1 = NewRun("ten_s", null, null, _Fixed.AddMinutes(1));
                b1.InferenceHost = "host-b";
                b1.TokensUsed = 4;
                ModelProbeRun noHost = NewRun("ten_s", null, null, _Fixed.AddMinutes(2));
                noHost.TokensUsed = 5000;
                foreach (ModelProbeRun r in new ModelProbeRun[] { old, a1, a2, b1, noHost }) await db.ModelProbeRuns.CreateAsync(r).ConfigureAwait(false);

                AssertEqual(5234L, await db.ModelProbeRuns.SumTokensUsedSinceAsync(_Fixed, null).ConfigureAwait(false), "all hosts since cutoff (inclusive)");
                AssertEqual(6234L, await db.ModelProbeRuns.SumTokensUsedSinceAsync(_Fixed.AddHours(-3), null).ConfigureAwait(false), "all hosts all time");
                AssertEqual(230L, await db.ModelProbeRuns.SumTokensUsedSinceAsync(_Fixed, "host-a").ConfigureAwait(false), "host-a since cutoff");
                AssertEqual(1230L, await db.ModelProbeRuns.SumTokensUsedSinceAsync(_Fixed.AddHours(-3), "host-a").ConfigureAwait(false), "host-a all time");
                AssertEqual(4L, await db.ModelProbeRuns.SumTokensUsedSinceAsync(_Fixed, "host-b").ConfigureAwait(false), "host-b");
                AssertEqual(0L, await db.ModelProbeRuns.SumTokensUsedSinceAsync(_Fixed, "host-none").ConfigureAwait(false), "unknown host");
                AssertEqual(0L, await db.ModelProbeRuns.SumTokensUsedSinceAsync(_Fixed.AddDays(1), null).ConfigureAwait(false), "cutoff after every run");
            }));

            cases.Add(CaseAsync("results_ordered_by_creation", "ReadByRunAsync returns only the run's results, oldest first then by id", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun run = await db.ModelProbeRuns.CreateAsync(NewRun("ten_o", null, null, _Fixed)).ConfigureAwait(false);
                ModelProbeRun other = await db.ModelProbeRuns.CreateAsync(NewRun("ten_o", null, null, _Fixed)).ConfigureAwait(false);

                await db.ModelProbeResults.CreateAsync(NewResult(run.Id, "mpo_c", ModelProbeKindEnum.Determinism, _Fixed.AddSeconds(30))).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(run.Id, "mpo_b", ModelProbeKindEnum.ToolCalling, _Fixed.AddSeconds(10))).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(run.Id, "mpo_a", ModelProbeKindEnum.OutputContract, _Fixed.AddSeconds(10))).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(other.Id, "mpo_x", ModelProbeKindEnum.Speed, _Fixed)).ConfigureAwait(false);

                List<ModelProbeResult> rows = await db.ModelProbeResults.ReadByRunAsync(run.Id).ConfigureAwait(false);
                AssertEqual("mpo_a,mpo_b,mpo_c", String.Join(",", rows.Select(r => r.Id)), "order");
                AssertEqual(0, (await db.ModelProbeResults.ReadByRunAsync("mpr_missing").ConfigureAwait(false)).Count, "unknown run");
            }));

            cases.Add(CaseAsync("delete_run_cascades_results", "DeleteAsync removes the run and its results but not another run's", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun run = await db.ModelProbeRuns.CreateAsync(NewRun("ten_d", null, null, _Fixed)).ConfigureAwait(false);
                ModelProbeRun keep = await db.ModelProbeRuns.CreateAsync(NewRun("ten_d", null, null, _Fixed)).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(run.Id, "mpo_d1", ModelProbeKindEnum.Speed, _Fixed)).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(run.Id, "mpo_d2", ModelProbeKindEnum.AsciiFormat, _Fixed)).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(keep.Id, "mpo_k1", ModelProbeKindEnum.Speed, _Fixed)).ConfigureAwait(false);

                AssertTrue(await db.ModelProbeRuns.DeleteAsync(run.Id).ConfigureAwait(false), "deleted");
                AssertNull(await db.ModelProbeRuns.ReadAsync(run.Id).ConfigureAwait(false), "run gone");
                AssertEqual(0, (await db.ModelProbeResults.ReadByRunAsync(run.Id).ConfigureAwait(false)).Count, "results gone");
                AssertNotNull(await db.ModelProbeRuns.ReadAsync(keep.Id).ConfigureAwait(false), "other run kept");
                AssertEqual(1, (await db.ModelProbeResults.ReadByRunAsync(keep.Id).ConfigureAwait(false)).Count, "other results kept");
                AssertFalse(await db.ModelProbeRuns.DeleteAsync(run.Id).ConfigureAwait(false), "second delete reports nothing");
            }));

            cases.Add(CaseAsync("delete_by_run", "DeleteByRunAsync removes only that run's results and returns the count", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;

                ModelProbeRun run = await db.ModelProbeRuns.CreateAsync(NewRun("ten_r", null, null, _Fixed)).ConfigureAwait(false);
                ModelProbeRun keep = await db.ModelProbeRuns.CreateAsync(NewRun("ten_r", null, null, _Fixed)).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(run.Id, "mpo_r1", ModelProbeKindEnum.Speed, _Fixed)).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(run.Id, "mpo_r2", ModelProbeKindEnum.Speed, _Fixed)).ConfigureAwait(false);
                await db.ModelProbeResults.CreateAsync(NewResult(keep.Id, "mpo_r3", ModelProbeKindEnum.Speed, _Fixed)).ConfigureAwait(false);

                AssertEqual(2, await db.ModelProbeResults.DeleteByRunAsync(run.Id).ConfigureAwait(false), "two deleted");
                AssertEqual(0, (await db.ModelProbeResults.ReadByRunAsync(run.Id).ConfigureAwait(false)).Count, "none left");
                AssertNotNull(await db.ModelProbeRuns.ReadAsync(run.Id).ConfigureAwait(false), "run itself kept");
                AssertEqual(1, (await db.ModelProbeResults.ReadByRunAsync(keep.Id).ConfigureAwait(false)).Count, "other run untouched");
                AssertEqual(0, await db.ModelProbeResults.DeleteByRunAsync(run.Id).ConfigureAwait(false), "second delete is zero");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Database: model probe runs and results",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ModelProbeRun FullRun()
        {
            ModelProbeRun run = new ModelProbeRun();
            run.TenantId = "ten_full";
            run.UserId = "usr_full";
            run.TargetKind = ModelProbeTargetEnum.ModelEndpoint;
            run.CaptainId = "cpt_full";
            run.CaptainName = "Full captain";
            run.ModelEndpointId = "mep_full";
            run.Depth = ModelProbeDepthEnum.Full;
            run.Status = ModelProbeRunStatusEnum.Completed;
            run.IsEvaluation = false;
            run.ProbeSetVersion = "quick-1";
            run.Runtime = "ApiEndpoint";
            run.RuntimeVersion = "1.2.3";
            run.ModelProvider = "Ollama";
            run.Model = "qwen3:32b";
            run.ModelDigest = "sha256:abc";
            run.ConfiguredContextTokens = 32768;
            run.ServedContextTokens = 8192;
            run.HarborId = "hbr_full";
            run.InferenceHost = "gb10.local:11434";
            run.TokenBudget = 500000;
            run.TokensUsed = 12345;
            run.PassedCount = 4;
            run.FailedCount = 1;
            run.CouldNotRunCount = 1;
            run.FailureReason = "served context mismatch";
            run.CreatedUtc = _Fixed;
            run.StartedUtc = _Fixed.AddSeconds(5);
            run.CompletedUtc = _Fixed.AddMinutes(3);
            return run;
        }

        private static ModelProbeMetrics FullMetrics()
        {
            ModelProbeMetrics m = new ModelProbeMetrics();
            m.ConfiguredContextTokens = 32768;
            m.ServedContextTokens = 8192;
            m.ContextMismatch = true;
            m.Compliant = 3;
            m.Checked = 4;
            m.RepairNeeded = 1;
            m.WorkerResultCompliant = true;
            m.JudgeVerdictCompliant = false;
            m.JudgeSectionsCompliant = true;
            m.ArchitectPlanCompliant = false;
            m.Violations = 7;
            m.EmDashes = 2;
            m.SmartQuotes = 3;
            m.AnsiCodes = 1;
            m.OtherNonAscii = 1;
            m.MalformedCalls = 2;
            m.ErrorRecovered = true;
            m.Looping = false;
            m.FirstOutputMs4k = 1500;
            m.OutputTokensPerSecond4k = 42.5;
            m.FirstOutputMs32k = 9000;
            m.OutputTokensPerSecond32k = 17.25;
            m.SuggestedRequestTimeoutSeconds = 600;
            m.SuggestedInactivityTimeoutSeconds = 120;
            m.SuggestedStallTimeoutSeconds = 300;
            m.DistinctOutputs = 2;
            m.AnswerAgreement = true;
            m.MinSimilarity = 0.875;
            return m;
        }

        private static ModelProbeRun NewRun(string? tenantId, string? captainId, string? endpointId, DateTime created)
        {
            ModelProbeRun run = new ModelProbeRun();
            run.TenantId = tenantId;
            run.CaptainId = captainId;
            run.ModelEndpointId = endpointId;
            run.ProbeSetVersion = "quick-1";
            run.CreatedUtc = created;
            return run;
        }

        private static ModelProbeResult NewResult(string runId, string id, ModelProbeKindEnum probe, DateTime created)
        {
            ModelProbeResult result = new ModelProbeResult();
            result.Id = id;
            result.RunId = runId;
            result.Probe = probe;
            result.Outcome = ModelProbeOutcomeEnum.Passed;
            result.CreatedUtc = created;
            return result;
        }

        private static async Task<List<ModelProbeRun>> ListAsync(DatabaseDriver db, ModelProbeRunQuery query)
        {
            EnumerationResult<ModelProbeRun> result = await db.ModelProbeRuns.EnumerateAsync(query).ConfigureAwait(false);
            return result.Objects;
        }

        private static void AssertIds(List<ModelProbeRun> runs, string label, params string[] expected)
        {
            AssertEqual(String.Join(",", expected), String.Join(",", runs.Select(r => r.Id)), label);
        }

        private static void AssertSameInstant(DateTime expected, DateTime actual, string label)
        {
            double delta = Math.Abs((expected.ToUniversalTime() - actual.ToUniversalTime()).TotalMilliseconds);
            AssertTrue(delta < 1.0, label + ": expected " + expected.ToString("o") + " but was " + actual.ToString("o"));
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
