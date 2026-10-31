namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
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
    /// Quick model probes (probe-only Evaluate): scoring with canned model answers, the evaluation budget and the
    /// per-host concurrency gate, the could-not-run path, and runs persisted on SQLite. No live model is called.
    /// </summary>
    public sealed class ModelProbeSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ModelProbe";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            AddScoringCases(cases);
            AddGateCases(cases);
            AddServiceCases(cases);
            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Services: Quick model probes (Evaluate)", cases: cases);
        }

        #endregion

        #region Scoring

        private static void AddScoringCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(Case("served_context_match", "Served context equal to the configured value passes", TestTags.Positive, () =>
            {
                ModelProbeResult result = ModelProbeScorer.ScoreServedContext(65536, 65536, null);
                AssertEqual(ModelProbeOutcomeEnum.Passed, result.Outcome);
                AssertEqual((bool?)false, result.Metrics.ContextMismatch);
                AssertEqual((int?)65536, result.Metrics.ServedContextTokens);
            }));

            cases.Add(Case("served_context_mismatch", "A served context different from the configured value is a configuration error", TestTags.Negative, () =>
            {
                ModelProbeResult result = ModelProbeScorer.ScoreServedContext(65536, 4096, null);
                AssertEqual(ModelProbeOutcomeEnum.Failed, result.Outcome);
                AssertEqual((bool?)true, result.Metrics.ContextMismatch);
                AssertContains("Configuration error", result.Summary!);
                AssertContains("4096", result.Summary!);
            }));

            cases.Add(Case("served_context_unconfigured", "A served context with nothing configured is recorded and passes", TestTags.Positive, () =>
            {
                ModelProbeResult result = ModelProbeScorer.ScoreServedContext(null, 8192, null);
                AssertEqual(ModelProbeOutcomeEnum.Passed, result.Outcome);
                AssertEqual((int?)8192, result.Metrics.ServedContextTokens);
                AssertNull(result.Metrics.ConfiguredContextTokens);
            }));

            cases.Add(Case("served_context_not_reported", "An endpoint that reports nothing is could-not-run, not failed", TestTags.Negative, () =>
            {
                ModelProbeResult result = ModelProbeScorer.ScoreServedContext(65536, null, null);
                AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, result.Outcome);
                AssertNotNull(result.Reason);
            }));

            cases.Add(Case("served_context_reasons", "Vendor CLIs, other providers and Harbor loopback endpoints get a could-not-run reason", TestTags.Negative, () =>
            {
                Captain cli = new Captain { Name = "claude", Runtime = AgentRuntimeEnum.ClaudeCode };
                AssertContains("vendor CLI", ModelProbeServedContext.CouldNotRunReason(cli, null, null)!);
                Captain api = new Captain { Name = "api", Runtime = AgentRuntimeEnum.ApiEndpoint };
                ModelEndpoint openAi = new ModelEndpoint { Name = "oa", Provider = ModelProviderEnum.OpenAI, BaseUrl = "https://api.example.com/v1" };
                AssertContains("only Ollama", ModelProbeServedContext.CouldNotRunReason(api, openAi, null)!);
                ModelEndpoint loopback = new ModelEndpoint { Name = "ol", Provider = ModelProviderEnum.Ollama, BaseUrl = "http://localhost:11434" };
                AssertContains("loopback", ModelProbeServedContext.CouldNotRunReason(api, loopback, "hbr_x")!);
                AssertNull(ModelProbeServedContext.CouldNotRunReason(api, loopback, null));
            }));

            cases.Add(Case("served_context_ps_body", "The /api/ps body is matched by model name with :latest normalization", TestTags.Positive, () =>
            {
                string body = "{\"models\":[{\"name\":\"qwen3:8b\",\"model\":\"qwen3:8b\",\"context_length\":40960},{\"name\":\"llama3:latest\",\"model\":\"llama3:latest\",\"context_length\":8192}]}";
                AssertEqual((int?)40960, ModelProbeServedContext.FindContext(body, "qwen3:8b"));
                AssertEqual((int?)8192, ModelProbeServedContext.FindContext(body, "LLAMA3"));
                AssertNull(ModelProbeServedContext.FindContext(body, "missing:1b"));
                AssertNull(ModelProbeServedContext.FindContext("{}", "qwen3:8b"));
                AssertNull(ModelProbeServedContext.FindContext("not json", "qwen3:8b"));
                AssertNull(ModelProbeServedContext.FindContext("{\"models\":[{\"name\":\"old:1b\"}]}", "old:1b"));
            }));

            cases.Add(Case("output_contract_all_compliant", "Exact Worker, Judge and Architect answers are all compliant", TestTags.Positive, () =>
            {
                ModelProbeResult result = ModelProbeScorer.ScoreOutputContract(GoodWorker(), GoodJudge(), GoodArchitect());
                AssertEqual(ModelProbeOutcomeEnum.Passed, result.Outcome, result.Summary);
                AssertEqual((int?)3, result.Metrics.Compliant);
                AssertEqual((int?)0, result.Metrics.RepairNeeded);
                AssertEqual((bool?)true, result.Metrics.WorkerResultCompliant);
                AssertEqual((bool?)true, result.Metrics.JudgeVerdictCompliant);
                AssertEqual((bool?)true, result.Metrics.JudgeSectionsCompliant);
                AssertEqual((bool?)true, result.Metrics.ArchitectPlanCompliant);
            }));

            cases.Add(Case("output_contract_inexact", "Inline or fenced markers, prose verdicts and prose plans need repair", TestTags.Negative, () =>
            {
                string worker = "Done. I printed [ARMADA:RESULT] COMPLETE inline.\n```\n[ARMADA:RESULT] COMPLETE\n```";
                string judge = "## Correctness\nfine\n## Blast Radius\nnone\n## Source Fidelity\nok\n## Verdict\nVerdict: PASS";
                string architect = "Mission 1: add the flag. Mission 2: test it.";
                ModelProbeResult result = ModelProbeScorer.ScoreOutputContract(worker, judge, architect);
                AssertEqual(ModelProbeOutcomeEnum.Failed, result.Outcome);
                AssertEqual((int?)0, result.Metrics.Compliant);
                AssertEqual((int?)3, result.Metrics.RepairNeeded);
                AssertEqual((bool?)false, result.Metrics.WorkerResultCompliant);
                AssertEqual((bool?)false, result.Metrics.JudgeVerdictCompliant);
                AssertEqual((bool?)true, result.Metrics.JudgeSectionsCompliant);
                AssertContains("Worker, Judge, Architect", result.Summary!);
            }));

            cases.Add(Case("output_contract_conflicting_verdicts", "Conflicting verdict lines and missing lenses need repair", TestTags.Negative, () =>
            {
                string judge = "## Correctness\nok\n[ARMADA:VERDICT] PASS\n[ARMADA:VERDICT] FAIL";
                ModelProbeResult result = ModelProbeScorer.ScoreOutputContract(null, judge, null);
                AssertEqual(ModelProbeOutcomeEnum.Failed, result.Outcome);
                AssertEqual((int?)1, result.Metrics.Checked);
                AssertEqual((bool?)false, result.Metrics.JudgeVerdictCompliant);
                AssertEqual((bool?)false, result.Metrics.JudgeSectionsCompliant);
                AssertNull(result.Metrics.WorkerResultCompliant);
            }));

            cases.Add(Case("output_contract_could_not_run", "No exchange at all is could-not-run with the reason", TestTags.Negative, () =>
            {
                ModelProbeResult result = ModelProbeScorer.ScoreOutputContract(null, null, null, "runtime missing");
                AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, result.Outcome);
                AssertEqual("runtime missing", result.Reason);
                AssertEqual(0, result.Attempts);
            }));

            cases.Add(Case("ascii_clean", "Plain ASCII output has no violations", TestTags.Positive, () =>
            {
                ModelProbeResult result = ModelProbeScorer.ScoreAscii("Cafe news -- \"great coffee\" from 2019 to 2024... 90 degrees. OK (shown green).");
                AssertEqual(ModelProbeOutcomeEnum.Passed, result.Outcome);
                AssertEqual((int?)0, result.Metrics.Violations);
            }));

            cases.Add(Case("ascii_violations", "Em dashes, smart quotes, other non-ASCII and ANSI codes are each counted", TestTags.Negative, () =>
            {
                string text = "Caf\u00e9 \u2014 \u201cgreat\u201d it\u2019s 2019\u20132024\u2026 \u001b[32mOK\u001b[0m and \\033[32mOK\\x1b[0m 90\u00b0 \U0001F600";
                ModelProbeResult result = ModelProbeScorer.ScoreAscii(text);
                AssertEqual(ModelProbeOutcomeEnum.Failed, result.Outcome);
                AssertEqual((int?)2, result.Metrics.EmDashes);
                AssertEqual((int?)3, result.Metrics.SmartQuotes);
                AssertEqual((int?)4, result.Metrics.AnsiCodes);
                AssertEqual((int?)4, result.Metrics.OtherNonAscii, "e-acute, ellipsis, degree sign, one emoji (counted once)");
                AssertEqual((int?)13, result.Metrics.Violations);
            }));

            cases.Add(Case("tool_calling_correct", "Correct hashes, the reported error and the large line pass", TestTags.Positive, () =>
            {
                ModelProbeToolTask task = FixedToolTask();
                ModelProbeResult result = ModelProbeScorer.ScoreToolCalling(GoodToolAnswer(task), task, false);
                AssertEqual(ModelProbeOutcomeEnum.Passed, result.Outcome, result.Summary);
                AssertEqual((int?)6, result.Metrics.Compliant);
                AssertEqual((int?)0, result.Metrics.MalformedCalls);
                AssertEqual((bool?)true, result.Metrics.ErrorRecovered);
                AssertEqual((bool?)false, result.Metrics.Looping);
            }));

            cases.Add(Case("tool_calling_fabricated_hash", "A hash computed without a tool (wrong) fails that step", TestTags.Negative, () =>
            {
                ModelProbeToolTask task = FixedToolTask();
                string answer = GoodToolAnswer(task).Replace(ModelProbeScorer.Sha256Hex(task.ParallelTextB), new string('a', 64));
                ModelProbeResult result = ModelProbeScorer.ScoreToolCalling(answer, task, false);
                AssertEqual(ModelProbeOutcomeEnum.Failed, result.Outcome);
                AssertEqual((int?)5, result.Metrics.Compliant);
                AssertContains("parallel B", result.Summary!);
            }));

            cases.Add(Case("tool_calling_malformed", "No answer block, invalid JSON and missing fields are malformed", TestTags.Negative, () =>
            {
                ModelProbeToolTask task = FixedToolTask();
                ModelProbeResult none = ModelProbeScorer.ScoreToolCalling("I could not do it.", task, false);
                AssertEqual(ModelProbeOutcomeEnum.Failed, none.Outcome);
                AssertEqual((int?)1, none.Metrics.MalformedCalls);
                AssertEqual((bool?)false, none.Metrics.ErrorRecovered);

                ModelProbeResult invalid = ModelProbeScorer.ScoreToolCalling("```armada-probe\n{\"single\": \n```", task, false);
                AssertEqual((int?)1, invalid.Metrics.MalformedCalls);

                string partial = "```armada-probe\n{\"single\":\"" + ModelProbeScorer.Sha256Hex(task.SingleText) + "\"}\n```";
                ModelProbeResult missing = ModelProbeScorer.ScoreToolCalling(partial, task, false);
                AssertEqual((int?)1, missing.Metrics.Compliant);
                AssertEqual((int?)5, missing.Metrics.MalformedCalls);
                AssertEqual((bool?)false, missing.Metrics.ErrorRecovered);
            }));

            cases.Add(Case("tool_calling_error_not_recovered", "Denying the tool error means no recovery", TestTags.Negative, () =>
            {
                ModelProbeToolTask task = FixedToolTask();
                string answer = GoodToolAnswer(task).Replace("\"missingFileError\":true", "\"missingFileError\":false");
                ModelProbeResult result = ModelProbeScorer.ScoreToolCalling(answer, task, false);
                AssertEqual(ModelProbeOutcomeEnum.Failed, result.Outcome);
                AssertEqual((bool?)false, result.Metrics.ErrorRecovered);
                AssertEqual((int?)5, result.Metrics.Compliant);
            }));

            cases.Add(Case("tool_calling_looping", "A timeout or a repeated line is recorded as looping and fails even with correct values", TestTags.Negative, () =>
            {
                ModelProbeToolTask task = FixedToolTask();
                ModelProbeResult timedOut = ModelProbeScorer.ScoreToolCalling(GoodToolAnswer(task), task, true);
                AssertEqual((bool?)true, timedOut.Metrics.Looping);
                AssertEqual(ModelProbeOutcomeEnum.Failed, timedOut.Outcome);

                string repeated = String.Concat(System.Linq.Enumerable.Repeat("Calling tool read_file again\n", ModelProbeScorer.LoopRepeatThreshold)) + GoodToolAnswer(task);
                ModelProbeResult loop = ModelProbeScorer.ScoreToolCalling(repeated, task, false);
                AssertEqual((bool?)true, loop.Metrics.Looping);
            }));

            cases.Add(Case("tool_calling_lenient_numbers", "A line count written as a string is still read", TestTags.Positive, () =>
            {
                ModelProbeToolTask task = FixedToolTask();
                string answer = GoodToolAnswer(task).Replace("\"largeLineCount\":2000", "\"largeLineCount\":\"2000\"");
                AssertEqual(ModelProbeOutcomeEnum.Passed, ModelProbeScorer.ScoreToolCalling(answer, task, false).Outcome);
            }));

            cases.Add(Case("speed_measured_and_advice", "Speed records first output, tokens per second and advised inference timeouts", TestTags.Positive, () =>
            {
                ModelProbeExchange small = new ModelProbeExchange { Output = new string('x', 4000), FirstOutputMs = 500, DurationMs = 10500 };
                ModelProbeExchange large = new ModelProbeExchange { Output = new string('x', 4000), FirstOutputMs = 8000, DurationMs = 28000 };
                ModelProbeResult result = ModelProbeScorer.ScoreSpeed(small, large);
                AssertEqual(ModelProbeOutcomeEnum.Passed, result.Outcome);
                AssertEqual(2, result.Attempts);
                AssertEqual((long?)500, result.Metrics.FirstOutputMs4k);
                AssertEqual((double?)100.0, result.Metrics.OutputTokensPerSecond4k);
                AssertEqual((long?)8000, result.Metrics.FirstOutputMs32k);
                AssertEqual((double?)50.0, result.Metrics.OutputTokensPerSecond32k);
                // 2 x 8 s first output + 4000 tokens at 50 tokens/s x 1.5 = 136 s.
                AssertEqual((int?)136, result.Metrics.SuggestedRequestTimeoutSeconds);
                AssertEqual((int?)30, result.Metrics.SuggestedInactivityTimeoutSeconds);
                AssertEqual((int?)120, result.Metrics.SuggestedStallTimeoutSeconds);
                AssertContains("inference timeouts", result.Summary!);
            }));

            cases.Add(Case("speed_non_streaming", "A runtime that prints the answer at once is measured over the whole duration", TestTags.Positive, () =>
            {
                ModelProbeExchange small = new ModelProbeExchange { Output = new string('x', 800), FirstOutputMs = null, DurationMs = 2000 };
                ModelProbeResult result = ModelProbeScorer.ScoreSpeed(small, ModelProbeExchange.NotStarted("context too small"));
                AssertEqual(ModelProbeOutcomeEnum.Passed, result.Outcome);
                AssertEqual((long?)2000, result.Metrics.FirstOutputMs4k);
                AssertEqual((double?)100.0, result.Metrics.OutputTokensPerSecond4k);
                AssertNull(result.Metrics.FirstOutputMs32k);
                AssertContains("context too small", result.Summary!);
            }));

            cases.Add(Case("speed_timeout_and_not_run", "A timed-out exchange fails; no exchange is could-not-run", TestTags.Negative, () =>
            {
                ModelProbeResult timedOut = ModelProbeScorer.ScoreSpeed(new ModelProbeExchange { Output = "", TimedOut = true, DurationMs = 600000 }, null);
                AssertEqual(ModelProbeOutcomeEnum.Failed, timedOut.Outcome);
                ModelProbeResult none = ModelProbeScorer.ScoreSpeed(ModelProbeExchange.NotStarted("no harbor"), ModelProbeExchange.NotStarted("no harbor"));
                AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, none.Outcome);
                AssertEqual("no harbor", none.Reason);
            }));

            cases.Add(Case("determinism_agreement", "The same ANSWER line in every repeat passes and records variance", TestTags.Positive, () =>
            {
                List<string> outputs = new List<string>
                {
                    "ANSWER: 101,103,107,109,113\nI checked divisibility.",
                    "ANSWER: 101, 103, 107, 109, 113\nI checked each by trial division.",
                    "**ANSWER: 101,103,107,109,113**\nI checked divisibility."
                };
                ModelProbeResult result = ModelProbeScorer.ScoreDeterminism(outputs);
                AssertEqual(ModelProbeOutcomeEnum.Passed, result.Outcome, result.Summary);
                AssertEqual((bool?)true, result.Metrics.AnswerAgreement);
                AssertEqual((int?)3, result.Metrics.DistinctOutputs);
                AssertTrue(result.Metrics.MinSimilarity!.Value < 1.0, "variance recorded");
            }));

            cases.Add(Case("determinism_variance", "Different answers fail; identical outputs have one distinct output", TestTags.Negative, () =>
            {
                ModelProbeResult differ = ModelProbeScorer.ScoreDeterminism(new List<string> { "ANSWER: 101,103", "ANSWER: 101,107", "no answer" });
                AssertEqual(ModelProbeOutcomeEnum.Failed, differ.Outcome);
                AssertEqual((bool?)false, differ.Metrics.AnswerAgreement);
                ModelProbeResult same = ModelProbeScorer.ScoreDeterminism(new List<string> { "ANSWER: 1", "ANSWER: 1", "ANSWER: 1" });
                AssertEqual((int?)1, same.Metrics.DistinctOutputs);
                AssertEqual((double?)1.0, same.Metrics.MinSimilarity);
                ModelProbeResult few = ModelProbeScorer.ScoreDeterminism(new List<string> { "ANSWER: 1" }, "budget");
                AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, few.Outcome);
                AssertEqual("budget", few.Reason);
            }));

            cases.Add(Case("settings_defaults_and_clamps", "Evaluation settings default, clamp and survive a minimal JSON body", TestTags.Positive, () =>
            {
                EvaluationSettings defaults = JsonSerializer.Deserialize<EvaluationSettings>("{}")!;
                AssertEqual(250000L, defaults.RunTokenBudget);
                AssertEqual(2000000L, defaults.DailyTokenBudget);
                AssertEqual(1000000L, defaults.DailyTokenBudgetPerHost);
                AssertEqual(2, defaults.MaxConcurrentRuns);
                AssertEqual(1, defaults.MaxConcurrentRunsPerHost);
                AssertEqual(600, defaults.ProbeTimeoutSeconds);
                EvaluationSettings clamped = new EvaluationSettings { RunTokenBudget = 1, DailyTokenBudget = -5, MaxConcurrentRuns = 0, MaxConcurrentRunsPerHost = 99, ProbeTimeoutSeconds = 1 };
                AssertEqual(1000L, clamped.RunTokenBudget);
                AssertEqual(0L, clamped.DailyTokenBudget);
                AssertEqual(1, clamped.MaxConcurrentRuns);
                AssertEqual(32, clamped.MaxConcurrentRunsPerHost);
                AssertEqual(30, clamped.ProbeTimeoutSeconds);
                ArmadaSettings armada = new ArmadaSettings { Evaluation = null! };
                AssertNotNull(armada.Evaluation);
                AssertEqual(1, armada.Evaluation.MaxConcurrentRunsPerHost);
            }));

            cases.Add(Case("inference_host_keys", "Endpoints on one machine share a host key whatever their port", TestTags.Positive, () =>
            {
                Captain api = new Captain { Name = "api", Runtime = AgentRuntimeEnum.ApiEndpoint };
                ModelEndpoint a = new ModelEndpoint { Name = "a", Provider = ModelProviderEnum.Ollama, BaseUrl = "http://GB10.lan:11434" };
                ModelEndpoint b = new ModelEndpoint { Name = "b", Provider = ModelProviderEnum.Ollama, BaseUrl = "http://gb10.lan:11435/v1" };
                AssertEqual(ModelProbeService.InferenceHostFor(api, a), ModelProbeService.InferenceHostFor(api, b));
                AssertEqual("gb10.lan", ModelProbeService.InferenceHostFor(api, a));
                AssertEqual("localhost", ModelProbeService.InferenceHostFor(api, new ModelEndpoint { Name = "l", BaseUrl = "http://127.0.0.1:11434" }));
                AssertEqual("vendor:Codex", ModelProbeService.InferenceHostFor(new Captain { Name = "c", Runtime = AgentRuntimeEnum.Codex }, null));
            }));
        }

        #endregion

        #region Gate

        private static void AddGateCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(CaseAsync("gate_per_host_and_overall", "The gate allows one run per host and caps the total, granting waiters in order", TestTags.Positive, async () =>
            {
                int overall = 2;
                EvaluationConcurrencyGate gate = new EvaluationConcurrencyGate(() => overall, () => 1);
                IDisposable a1 = await gate.AcquireAsync("gb10").ConfigureAwait(false);
                Task<IDisposable> a2 = gate.AcquireAsync("gb10");
                AssertFalse(a2.IsCompleted, "second run on the same host waits");
                IDisposable b1 = await gate.AcquireAsync("other").WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                AssertEqual(2, gate.ActiveCount);
                Task<IDisposable> c1 = gate.AcquireAsync("third");
                AssertFalse(c1.IsCompleted, "the overall limit holds a third host back");

                a1.Dispose();
                IDisposable a2Lease = await a2.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                AssertFalse(c1.IsCompleted, "the earlier waiter on gb10 took the freed slot");
                AssertEqual(1, gate.ActiveOn("gb10"));

                b1.Dispose();
                IDisposable c1Lease = await c1.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                AssertEqual(2, gate.ActiveCount);
                a2Lease.Dispose();
                a2Lease.Dispose();
                c1Lease.Dispose();
                AssertEqual(0, gate.ActiveCount, "double dispose releases once");
            }));

            cases.Add(CaseAsync("gate_live_limit_and_cancel", "A raised limit applies to the next acquire; a cancelled waiter is removed", TestTags.Reliability, async () =>
            {
                int perHost = 1;
                EvaluationConcurrencyGate gate = new EvaluationConcurrencyGate(() => 8, () => perHost);
                IDisposable first = await gate.AcquireAsync("gb10").ConfigureAwait(false);
                using (CancellationTokenSource cts = new CancellationTokenSource())
                {
                    Task<IDisposable> waiting = gate.AcquireAsync("gb10", cts.Token);
                    AssertEqual(1, gate.WaitingCount);
                    cts.Cancel();
                    bool cancelled = false;
                    try { await waiting.ConfigureAwait(false); }
                    catch (OperationCanceledException) { cancelled = true; }
                    AssertTrue(cancelled, "cancelled waiter throws");
                    AssertEqual(0, gate.WaitingCount);
                }

                perHost = 2;
                IDisposable second = await gate.AcquireAsync("gb10").WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                AssertEqual(2, gate.ActiveOn("gb10"));
                first.Dispose();
                second.Dispose();
            }));
        }

        #endregion

        #region Service

        private static void AddServiceCases(List<TestCaseDescriptor> cases)
        {
            cases.Add(CaseAsync("run_endpoint_all_probes", "A Quick run of an Ollama endpoint records six observations with provenance, as an evaluation", TestTags.Database, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelEndpoint endpoint = await db.ModelEndpoints.CreateAsync(OllamaEndpoint("ten_a", 65536)).ConfigureAwait(false);
                string before = JsonSerializer.Serialize(await db.ModelEndpoints.ReadAsync(endpoint.Id).ConfigureAwait(false));

                CannedExecutor executor = CannedExecutor.Good();
                ModelProbeService service = NewService(db, executor, new EvaluationSettings());
                service.ServedContextReader = (ep, model, ct) => Task.FromResult<int?>(65536);

                ModelProbeRun queued = await service.StartEndpointAsync(endpoint, ModelProbeDepthEnum.Quick, "ten_a", "usr_a").ConfigureAwait(false);
                AssertTrue(queued.Id.StartsWith("mpr_", StringComparison.Ordinal), queued.Id);
                ModelProbeRun run = (await service.WaitAsync(queued.Id).ConfigureAwait(false))!;

                AssertEqual(ModelProbeRunStatusEnum.Completed, run.Status, run.FailureReason);
                AssertTrue(run.IsEvaluation, "tagged as an evaluation");
                AssertEqual(ModelProbeTargetEnum.ModelEndpoint, run.TargetKind);
                AssertEqual(endpoint.Id, run.ModelEndpointId);
                AssertNull(run.CaptainId);
                AssertEqual("ten_a", run.TenantId);
                AssertEqual(ModelProbeCatalog.ProbeSetVersion, run.ProbeSetVersion);
                AssertEqual("ApiEndpoint", run.Runtime);
                AssertEqual("Ollama", run.ModelProvider);
                AssertEqual("qwen3:8b", run.Model);
                AssertEqual((int?)65536, run.ConfiguredContextTokens);
                AssertEqual((int?)65536, run.ServedContextTokens);
                AssertEqual("gb10.lan", run.InferenceHost);
                AssertEqual(250000L, run.TokenBudget);
                AssertTrue(run.TokensUsed > 0, "tokens counted");
                AssertEqual(6, run.Results.Count);
                AssertEqual(6, run.PassedCount, Describe(run));
                AssertEqual(0, run.FailedCount);
                AssertEqual(0, run.CouldNotRunCount);
                AssertNotNull(run.StartedUtc);
                AssertNotNull(run.CompletedUtc);
                foreach (ModelProbeResult result in run.Results)
                {
                    AssertEqual(run.Id, result.RunId);
                    AssertEqual("ten_a", result.TenantId);
                }

                // 3 contract + 1 ASCII + 1 tool + 2 speed + 3 determinism exchanges, all through the endpoint's transient captain.
                AssertEqual(10, executor.Requests.Count);
                foreach (ModelProbeExchangeRequest request in executor.Requests)
                {
                    AssertEqual(AgentRuntimeEnum.ApiEndpoint, request.Captain.Runtime);
                    AssertEqual(endpoint.Id, request.Captain.ModelEndpointId);
                    AssertEqual(run.Id, request.RunId);
                    AssertEqual(600000, request.TimeoutMs);
                }

                EnumerationResult<StageOutcome> outcomes = await db.StageOutcomes.EnumerateAsync(new StageOutcomeQuery()).ConfigureAwait(false);
                AssertEqual(0L, outcomes.TotalRecords, "an evaluation writes no stage outcome");
                AssertEqual(before, JsonSerializer.Serialize(await db.ModelEndpoints.ReadAsync(endpoint.Id).ConfigureAwait(false)), "nothing changes the endpoint");
            }));

            cases.Add(CaseAsync("run_vendor_cli_captain", "A vendor CLI captain is probed; served context is could-not-run with a reason, never failed", TestTags.Database, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                TenantMetadata tenant = await db.Tenants.CreateAsync(new TenantMetadata("Probe Tenant " + Guid.NewGuid().ToString("N").Substring(0, 6))).ConfigureAwait(false);
                Captain captain = await db.Captains.CreateAsync(new Captain { Name = "claude-probe", Runtime = AgentRuntimeEnum.ClaudeCode, TenantId = tenant.Id, Model = "opus" }).ConfigureAwait(false);
                string before = JsonSerializer.Serialize(await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false));
                CannedExecutor executor = CannedExecutor.Good();
                bool readerCalled = false;
                ModelProbeService service = NewService(db, executor, new EvaluationSettings());
                service.ServedContextReader = (ep, model, ct) => { readerCalled = true; return Task.FromResult<int?>(1); };

                ModelProbeRun queued = await service.StartCaptainAsync(captain, null, ModelProbeDepthEnum.Quick, "ten_other", "usr_a").ConfigureAwait(false);
                ModelProbeRun run = (await service.WaitAsync(queued.Id).ConfigureAwait(false))!;

                AssertEqual(ModelProbeRunStatusEnum.Completed, run.Status);
                AssertEqual(captain.Id, run.CaptainId);
                AssertEqual("claude-probe", run.CaptainName);
                AssertEqual(tenant.Id, run.TenantId, "the captain's tenant wins over the caller's");
                AssertEqual("ClaudeCode", run.Runtime);
                AssertEqual("opus", run.Model);
                AssertEqual("vendor:ClaudeCode", run.InferenceHost);
                AssertNull(run.ConfiguredContextTokens);
                AssertFalse(readerCalled, "no endpoint to ask");
                ModelProbeResult served = run.Results.Find(r => r.Probe == ModelProbeKindEnum.ServedContext)!;
                AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, served.Outcome);
                AssertContains("vendor CLI", served.Reason!);
                AssertEqual(5, run.PassedCount, Describe(run));
                AssertEqual(0, run.FailedCount);
                AssertEqual(1, run.CouldNotRunCount);
                // The run records the model profile (advisory, Phase 5.1) and points the captain at it; nothing else on the
                // captain changes (no tier, routing or settings).
                Captain after = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                AssertNotNull(after.ModelProfileId, "the captain references its model profile");
                ModelProfile profile = (await db.ModelProfiles.ReadAsync(after.ModelProfileId!).ConfigureAwait(false))!;
                AssertEqual("ClaudeCode", profile.Identity.Runtime, "profile runtime");
                AssertEqual(captain.Model, profile.Identity.Model, "profile model");
                after.ModelProfileId = null;
                AssertEqual(before, JsonSerializer.Serialize(after), "nothing else changes the captain");
            }));

            cases.Add(CaseAsync("run_could_not_launch", "A runtime that cannot launch makes every probe could-not-run with the reason, and the run completes", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                CannedExecutor executor = new CannedExecutor(r => ModelProbeExchange.NotStarted("The Codex CLI is not installed on this host."));
                ModelProbeService service = NewService(db, executor, new EvaluationSettings());
                Captain captain = new Captain { Name = "codex", Runtime = AgentRuntimeEnum.Codex, TenantId = "ten_a" };
                ModelProbeRun run = (await service.WaitAsync((await service.StartCaptainAsync(captain, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Completed, run.Status);
                AssertEqual(6, run.CouldNotRunCount, Describe(run));
                AssertEqual(0, run.FailedCount);
                AssertEqual(0, run.PassedCount);
                AssertEqual(0L, run.TokensUsed);
                foreach (ModelProbeResult result in run.Results)
                {
                    AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, result.Outcome, result.Probe.ToString());
                    AssertFalse(String.IsNullOrWhiteSpace(result.Reason), "reason recorded for " + result.Probe);
                }

                AssertContains("not installed", run.Results.Find(r => r.Probe == ModelProbeKindEnum.ToolCalling)!.Reason!);
            }));

            cases.Add(CaseAsync("run_executor_throws", "An executor that throws is could-not-run, not a failed run", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CannedExecutor executor = new CannedExecutor(r => throw new InvalidOperationException("boom"));
                ModelProbeService service = NewService(testDb.Driver, executor, new EvaluationSettings());
                Captain captain = new Captain { Name = "codex", Runtime = AgentRuntimeEnum.Codex };
                ModelProbeRun run = (await service.WaitAsync((await service.StartCaptainAsync(captain, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Completed, run.Status);
                AssertEqual(6, run.CouldNotRunCount);
                AssertContains("boom", run.Results[0].Reason!);
            }));

            cases.Add(CaseAsync("run_full_without_battery_rejected", "Depth Full without a configured fixture battery is rejected and creates no run", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelProbeService service = NewService(testDb.Driver, CannedExecutor.Good(), new EvaluationSettings());
                bool rejected = false;
                try
                {
                    await service.StartCaptainAsync(new Captain { Name = "c" }, null, ModelProbeDepthEnum.Full, null, null).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    rejected = true;
                    AssertContains("fixture battery", ex.Message);
                }

                AssertTrue(rejected, "Full rejected");
                EnumerationResult<ModelProbeRun> runs = await testDb.Driver.ModelProbeRuns.EnumerateAsync(new ModelProbeRunQuery()).ConfigureAwait(false);
                AssertEqual(0L, runs.TotalRecords);
            }));

            cases.Add(CaseAsync("budget_run_exhausted", "A run budget too small for any exchange records each model probe as could-not-run without calling the model", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CannedExecutor executor = CannedExecutor.Good();
                ModelProbeService service = NewService(testDb.Driver, executor, new EvaluationSettings { RunTokenBudget = 1000 });
                Captain captain = new Captain { Name = "codex", Runtime = AgentRuntimeEnum.Codex };
                ModelProbeRun run = (await service.WaitAsync((await service.StartCaptainAsync(captain, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(0, executor.Requests.Count, "no model call");
                AssertEqual(6, run.CouldNotRunCount);
                AssertEqual(1000L, run.TokenBudget);
                AssertContains("run token budget is exhausted", run.Results.Find(r => r.Probe == ModelProbeKindEnum.AsciiFormat)!.Reason!);
            }));

            cases.Add(CaseAsync("budget_partial", "A budget that covers only the first exchanges stops later probes, never overspending", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CannedExecutor executor = CannedExecutor.Good();
                ModelProbeService service = NewService(testDb.Driver, executor, new EvaluationSettings { RunTokenBudget = 6000 });
                Captain captain = new Captain { Name = "codex", Runtime = AgentRuntimeEnum.Codex };
                ModelProbeRun run = (await service.WaitAsync((await service.StartCaptainAsync(captain, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertTrue(executor.Requests.Count > 0 && executor.Requests.Count < 10, "some but not all exchanges ran: " + executor.Requests.Count);
                AssertTrue(run.TokensUsed <= 6000, "within budget: " + run.TokensUsed);
                ModelProbeResult speed = run.Results.Find(r => r.Probe == ModelProbeKindEnum.Speed)!;
                AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, speed.Outcome, "the 4K speed exchange alone exceeds what is left");
                AssertContains("budget", speed.Reason!);
            }));

            cases.Add(CaseAsync("budget_daily_per_host", "The daily per-host budget stops runs on a spent host while another host still runs", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.ModelProbeRuns.CreateAsync(new ModelProbeRun { InferenceHost = "gb10.lan", TokensUsed = 999000, Status = ModelProbeRunStatusEnum.Completed }).ConfigureAwait(false);
                await db.ModelProbeRuns.CreateAsync(new ModelProbeRun { InferenceHost = "gb10.lan", TokensUsed = 999000, Status = ModelProbeRunStatusEnum.Completed, CreatedUtc = DateTime.UtcNow.AddDays(-2) }).ConfigureAwait(false);
                CannedExecutor executor = CannedExecutor.Good();
                ModelProbeService service = NewService(db, executor, new EvaluationSettings { DailyTokenBudget = 0, DailyTokenBudgetPerHost = 1000000 });
                service.ServedContextReader = (ep, model, ct) => Task.FromResult<int?>(65536);

                ModelEndpoint spent = OllamaEndpoint("ten_a", 65536);
                ModelProbeRun blocked = (await service.WaitAsync((await service.StartEndpointAsync(spent, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(0, executor.Requests.Count, "spent host gets no model call");
                AssertContains("for gb10.lan", blocked.Results.Find(r => r.Probe == ModelProbeKindEnum.AsciiFormat)!.Reason!);

                ModelEndpoint fresh = OllamaEndpoint("ten_a", 65536);
                fresh.BaseUrl = "http://other.lan:11434";
                ModelProbeRun ran = (await service.WaitAsync((await service.StartEndpointAsync(fresh, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(6, ran.PassedCount, Describe(ran));
                AssertEqual(10, executor.Requests.Count);
            }));

            cases.Add(CaseAsync("budget_daily_overall", "The daily overall budget counts every host's runs in the last 24 hours", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                await db.ModelProbeRuns.CreateAsync(new ModelProbeRun { InferenceHost = "a", TokensUsed = 1500000 }).ConfigureAwait(false);
                await db.ModelProbeRuns.CreateAsync(new ModelProbeRun { InferenceHost = "b", TokensUsed = 499000 }).ConfigureAwait(false);
                CannedExecutor executor = CannedExecutor.Good();
                ModelProbeService service = NewService(db, executor, new EvaluationSettings { DailyTokenBudget = 2000000, DailyTokenBudgetPerHost = 0 });
                ModelProbeRun run = (await service.WaitAsync((await service.StartCaptainAsync(new Captain { Name = "c", Runtime = AgentRuntimeEnum.Codex }, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertEqual(0, executor.Requests.Count);
                AssertContains("daily evaluation token budget (2000000)", run.Results.Find(r => r.Probe == ModelProbeKindEnum.Determinism)!.Reason!);
            }));

            cases.Add(CaseAsync("small_context_and_tools_disabled", "A small configured context skips the 32K speed input; disabled tools make tool calling could-not-run", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CannedExecutor executor = CannedExecutor.Good();
                ModelProbeService service = NewService(testDb.Driver, executor, new EvaluationSettings());
                service.ServedContextReader = (ep, model, ct) => Task.FromResult<int?>(4096);
                ModelEndpoint endpoint = OllamaEndpoint("ten_a", 8192);
                endpoint.InferenceOptions.ToolsEnabled = false;
                ModelProbeRun run = (await service.WaitAsync((await service.StartEndpointAsync(endpoint, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id).ConfigureAwait(false))!;
                AssertFalse(executor.Requests.Exists(r => r.Label == "Speed (32K)"), "no 32K exchange");
                AssertFalse(executor.Requests.Exists(r => r.Label == "Tool calling"), "no tool exchange");
                ModelProbeResult speed = run.Results.Find(r => r.Probe == ModelProbeKindEnum.Speed)!;
                AssertEqual(ModelProbeOutcomeEnum.Passed, speed.Outcome);
                AssertNull(speed.Metrics.FirstOutputMs32k);
                AssertContains("below the 32K", speed.Summary!);
                AssertEqual(ModelProbeOutcomeEnum.CouldNotRun, run.Results.Find(r => r.Probe == ModelProbeKindEnum.ToolCalling)!.Outcome);
                ModelProbeResult served = run.Results.Find(r => r.Probe == ModelProbeKindEnum.ServedContext)!;
                AssertEqual(ModelProbeOutcomeEnum.Failed, served.Outcome, "8192 configured, 4096 served");
                AssertEqual((int?)4096, run.ServedContextTokens);
            }));

            cases.Add(CaseAsync("concurrency_one_per_host", "Runs on one inference host never probe at once; runs on different hosts do", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CannedExecutor executor = CannedExecutor.Good();
                executor.HoldMs = 15;
                ModelProbeService service = NewService(testDb.Driver, executor, new EvaluationSettings { MaxConcurrentRuns = 4, MaxConcurrentRunsPerHost = 1 });
                service.ServedContextReader = (ep, model, ct) => Task.FromResult<int?>(65536);

                List<string> ids = new List<string>();
                for (int i = 0; i < 3; i++)
                    ids.Add((await service.StartEndpointAsync(OllamaEndpoint("ten_a", 65536), ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id);
                foreach (string id in ids) await service.WaitAsync(id).ConfigureAwait(false);
                AssertEqual(1, executor.MaxConcurrentOnHost("gb10.lan"), "one run at a time on the shared host");

                CannedExecutor spread = CannedExecutor.Good();
                spread.HoldMs = 15;
                spread.Rendezvous = 2;
                ModelProbeService parallel = NewService(testDb.Driver, spread, new EvaluationSettings { MaxConcurrentRuns = 4, MaxConcurrentRunsPerHost = 1 });
                string first = (await parallel.StartCaptainAsync(new Captain { Name = "x", Runtime = AgentRuntimeEnum.Codex }, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id;
                string second = (await parallel.StartCaptainAsync(new Captain { Name = "y", Runtime = AgentRuntimeEnum.ClaudeCode }, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id;
                await parallel.WaitAsync(first).ConfigureAwait(false);
                await parallel.WaitAsync(second).ConfigureAwait(false);
                AssertEqual(2, spread.MaxConcurrentOverall, "different hosts probe at once");
            }));

            cases.Add(CaseAsync("concurrency_overall_limit", "The overall limit holds runs on different hosts back", TestTags.Reliability, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                CannedExecutor executor = CannedExecutor.Good();
                executor.HoldMs = 15;
                ModelProbeService service = NewService(testDb.Driver, executor, new EvaluationSettings { MaxConcurrentRuns = 1, MaxConcurrentRunsPerHost = 4 });
                string a = (await service.StartCaptainAsync(new Captain { Name = "x", Runtime = AgentRuntimeEnum.Codex }, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id;
                string b = (await service.StartCaptainAsync(new Captain { Name = "y", Runtime = AgentRuntimeEnum.ClaudeCode }, null, ModelProbeDepthEnum.Quick, null, null).ConfigureAwait(false)).Id;
                await service.WaitAsync(a).ConfigureAwait(false);
                await service.WaitAsync(b).ConfigureAwait(false);
                AssertEqual(1, executor.MaxConcurrentOverall);
            }));

            cases.Add(CaseAsync("recover_interrupted", "Runs left queued or running by an earlier process are marked failed at startup", TestTags.Database, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                DatabaseDriver db = testDb.Driver;
                ModelProbeRun queued = await db.ModelProbeRuns.CreateAsync(new ModelProbeRun { Status = ModelProbeRunStatusEnum.Queued }).ConfigureAwait(false);
                ModelProbeRun running = await db.ModelProbeRuns.CreateAsync(new ModelProbeRun { Status = ModelProbeRunStatusEnum.Running }).ConfigureAwait(false);
                ModelProbeRun done = await db.ModelProbeRuns.CreateAsync(new ModelProbeRun { Status = ModelProbeRunStatusEnum.Completed }).ConfigureAwait(false);
                ModelProbeService service = NewService(db, CannedExecutor.Good(), new EvaluationSettings());
                AssertEqual(2, await service.RecoverInterruptedAsync().ConfigureAwait(false));
                AssertEqual(ModelProbeRunStatusEnum.Failed, (await db.ModelProbeRuns.ReadAsync(queued.Id).ConfigureAwait(false))!.Status);
                ModelProbeRun after = (await db.ModelProbeRuns.ReadAsync(running.Id).ConfigureAwait(false))!;
                AssertEqual(ModelProbeRunStatusEnum.Failed, after.Status);
                AssertContains("restarted", after.FailureReason!);
                AssertEqual(ModelProbeRunStatusEnum.Completed, (await db.ModelProbeRuns.ReadAsync(done.Id).ConfigureAwait(false))!.Status);
                AssertEqual(0, await service.RecoverInterruptedAsync().ConfigureAwait(false));
            }));
        }

        #endregion

        #region Helpers

        private static ModelProbeService NewService(DatabaseDriver db, IModelProbeExecutor executor, EvaluationSettings settings)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ModelProbeService service = new ModelProbeService(db, logging, executor, () => settings);
            service.ToolTaskFactory = FixedToolTask;
            return service;
        }

        private static ModelEndpoint OllamaEndpoint(string tenantId, int context)
        {
            ModelEndpoint endpoint = new ModelEndpoint
            {
                Name = "gb10-qwen",
                TenantId = tenantId,
                Provider = ModelProviderEnum.Ollama,
                BaseUrl = "http://gb10.lan:11434",
                Model = "qwen3:8b"
            };
            endpoint.InferenceOptions.ContextLength = context;
            return endpoint;
        }

        private static string Describe(ModelProbeRun run)
        {
            List<string> parts = new List<string>();
            foreach (ModelProbeResult result in run.Results) parts.Add(result.Probe + "=" + result.Outcome + " (" + result.Summary + ")");
            return String.Join("; ", parts);
        }

        private static ModelProbeToolTask FixedToolTask()
        {
            return new ModelProbeToolTask
            {
                SingleText = "armada-single",
                SequentialText = "armada-seq",
                ParallelTextA = "armada-pa",
                ParallelTextB = "armada-pb",
                MissingFileName = "armada-probe-missing-x.txt",
                LargeMarker = "m1"
            };
        }

        private static string GoodWorker()
        {
            return "A git worktree is a second working directory attached to the same repository. It lets you check out another branch at once.\n[ARMADA:RESULT] COMPLETE\nExplained git worktrees.";
        }

        private static string GoodJudge()
        {
            return "## Correctness\nThe change replaces subtraction with addition, which the criterion requires.\n" +
                "## Blast Radius\nOnly Add changes.\n## Source Fidelity\nThe diff matches the report.\n## Verdict\nThe criterion is met.\n[ARMADA:VERDICT] PASS";
        }

        private static string GoodArchitect()
        {
            ArchitectPlan plan = new ArchitectPlan
            {
                ContractVersion = 3,
                Requirements = new List<string> { "REQ-1" },
                Decisions = new List<string> { "The flag prints the assembly version and exits 0." }
            };
            for (int i = 0; i < 2; i++) plan.Missions.Add(new ArchitectPlanMission
            {
                Title = i == 0 ? "Add the flag" : "Integration",
                Description = "Add --version to the tool.",
                Kind = i == 0 ? "implementation" : "integration",
                DependsOn = i == 0 ? null : 1,
                WaitForOtherMissions = i == 1,
                ValidationCommands = new List<string> { "dotnet test" },
                AcceptanceCriteria = new List<ArchitectAcceptanceCriterion> { new ArchitectAcceptanceCriterion
                {
                    Id = i == 0 ? "AC-1" : "AC-2",
                    RequirementIds = new List<string> { "REQ-1" },
                    DecisionIndexes = new List<int> { 1 },
                    Scenario = "Run the tool with --version",
                    Expected = "The version is printed and the exit code is 0",
                    Verification = "dotnet test",
                    NegativeCases = new List<string> { "An unknown flag exits non-zero" }
                } }
            });
            return "Plan:\n```armada-plan\n" + JsonSerializer.Serialize(plan) + "\n```";
        }

        private static string GoodToolAnswer(ModelProbeToolTask task)
        {
            return "I ran the commands.\n```armada-probe\n{" +
                "\"single\":\"" + ModelProbeScorer.Sha256Hex(task.SingleText) + "\"," +
                "\"sequential\":\"" + ModelProbeScorer.Sha256Hex(task.SequentialText).ToUpperInvariant() + "\"," +
                "\"parallelA\":\"" + ModelProbeScorer.Sha256Hex(task.ParallelTextA) + "\"," +
                "\"parallelB\":\"" + ModelProbeScorer.Sha256Hex(task.ParallelTextB) + "\"," +
                "\"missingFileError\":true,\"missingFileErrorText\":\"No such file or directory\"," +
                "\"largeLine\":\"line-" + task.LargeLineNumber + "-" + task.LargeMarker + "\",\"largeLineCount\":2000}\n```";
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

        private sealed class CannedExecutor : IModelProbeExecutor
        {
            private readonly Func<ModelProbeExchangeRequest, ModelProbeExchange> _Respond;
            private readonly object _Lock = new object();
            private readonly Dictionary<string, int> _ActiveByHost = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly ConcurrentDictionary<string, int> _MaxByHost = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            private readonly TaskCompletionSource<bool> _Met = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _Active = 0;

            public List<ModelProbeExchangeRequest> Requests { get; } = new List<ModelProbeExchangeRequest>();
            public int HoldMs { get; set; } = 0;
            public int Rendezvous { get; set; } = 0;
            public int MaxConcurrentOverall { get; private set; } = 0;

            public CannedExecutor(Func<ModelProbeExchangeRequest, ModelProbeExchange> respond)
            {
                _Respond = respond;
            }

            public static CannedExecutor Good()
            {
                return new CannedExecutor(request =>
                {
                    string output;
                    if (request.Label.Contains("Worker")) output = GoodWorker();
                    else if (request.Label.Contains("Judge")) output = GoodJudge();
                    else if (request.Label.Contains("Architect")) output = GoodArchitect();
                    else if (request.Label.StartsWith("ASCII", StringComparison.Ordinal)) output = "Cafe app 2.0 -- \"I love it\" from 2019 to 2024...";
                    else if (request.Label == "Tool calling") output = GoodToolAnswer(FixedToolTask());
                    else if (request.Label.StartsWith("Speed", StringComparison.Ordinal)) output = "READY\n" + new string('x', 400);
                    else output = "ANSWER: 101,103,107,109,113\nTrial division.";
                    return new ModelProbeExchange { Output = output, FirstOutputMs = 100, DurationMs = 1100, HarborId = null };
                });
            }

            public int MaxConcurrentOnHost(string host)
            {
                return _MaxByHost.TryGetValue(host, out int max) ? max : 0;
            }

            public async Task<ModelProbeExchange> RunAsync(ModelProbeExchangeRequest request, CancellationToken token = default)
            {
                string host = ModelProbeService.InferenceHostFor(request.Captain, request.ModelEndpoint);
                lock (_Lock)
                {
                    Requests.Add(request);
                    _Active++;
                    MaxConcurrentOverall = Math.Max(MaxConcurrentOverall, _Active);
                    int onHost = (_ActiveByHost.TryGetValue(host, out int n) ? n : 0) + 1;
                    _ActiveByHost[host] = onHost;
                    _MaxByHost.AddOrUpdate(host, onHost, (k, old) => Math.Max(old, onHost));
                    if (Rendezvous > 0 && _Active >= Rendezvous) _Met.TrySetResult(true);
                }

                try
                {
                    if (Rendezvous > 0) await Task.WhenAny(_Met.Task, Task.Delay(5000)).ConfigureAwait(false);
                    if (HoldMs > 0) await Task.Delay(HoldMs).ConfigureAwait(false);
                    return _Respond(request);
                }
                finally
                {
                    lock (_Lock)
                    {
                        _Active--;
                        _ActiveByHost[host] = _ActiveByHost[host] - 1;
                    }
                }
            }
        }

        #endregion
    }
}
