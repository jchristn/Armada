namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.OpenModel;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The open-model simulator (RELIABILITY_SIMPLICITY_PLAN.md RS-1.1): each scripted behavior is served in the wire
    /// format the real servers use, on Ollama and OpenAI-compatible routes, streamed and not; and the API runtime run
    /// against every behavior ends (with any exit code) instead of hanging. Later plan items add cases that assert the
    /// runtime recovers from each behavior.
    /// </summary>
    public sealed class OpenModelSimulatorSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Runtimes.OpenModelSimulator";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the open-model simulator suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("ollama_native_tool_call", "Ollama native tool call arrives as an arguments object in message.tool_calls", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator();
                sim.Enqueue(SimulatedTurn.Call(SimulatedTurnKindEnum.NativeToolCall, "read_file", "{\"path\":\"README.md\"}"));
                string body = await PostAsync(sim.BaseUrl + "/api/chat", "{\"model\":\"m\",\"stream\":false}").ConfigureAwait(false);
                AssertContains("\"tool_calls\":[{\"function\":{\"name\":\"read_file\",\"arguments\":{\"path\":\"README.md\"}}}]", body);
                AssertContains("\"done\":true", body);
            }));

            cases.Add(Case("openai_native_tool_call", "OpenAI-compatible native tool call carries arguments as a JSON string and finish_reason tool_calls", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator();
                sim.Enqueue(SimulatedTurn.Call(SimulatedTurnKindEnum.NativeToolCall, "read_file", "{\"path\":\"README.md\"}"));
                string body = await PostAsync(sim.BaseUrl + "/v1/chat/completions", "{\"model\":\"m\"}").ConfigureAwait(false);
                AssertContains("\"arguments\":\"{\\u0022path\\u0022:\\u0022README.md\\u0022}\"", body);
                AssertContains("\"finish_reason\":\"tool_calls\"", body);
            }));

            cases.Add(Case("text_tool_calls_are_plain_content", "Hermes, function-tag, and fenced JSON tool calls are plain assistant text with no tool_calls field", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator();
                sim.Enqueue(
                    SimulatedTurn.Call(SimulatedTurnKindEnum.HermesTextToolCall, "read_file", "{\"path\":\"a\"}"),
                    SimulatedTurn.Call(SimulatedTurnKindEnum.FunctionTagTextToolCall, "read_file", "{\"path\":\"a\"}"),
                    SimulatedTurn.Call(SimulatedTurnKindEnum.FencedJsonTextToolCall, "read_file", "{\"path\":\"a\"}"));
                string hermes = await PostAsync(sim.BaseUrl + "/api/chat", "{\"stream\":false}").ConfigureAwait(false);
                string function = await PostAsync(sim.BaseUrl + "/api/chat", "{\"stream\":false}").ConfigureAwait(false);
                string fenced = await PostAsync(sim.BaseUrl + "/api/chat", "{\"stream\":false}").ConfigureAwait(false);
                AssertContains("\\u003Ctool_call\\u003E", hermes);
                AssertContains("\\u003Cfunction=read_file\\u003E", function);
                AssertContains("\\u0060\\u0060\\u0060json", fenced);
                foreach (string body in new[] { hermes, function, fenced }) AssertFalse(body.Contains("\"tool_calls\"", StringComparison.Ordinal), "text tool calls have no native field");
            }));

            cases.Add(Case("server_failures_use_real_status_and_text", "Server failures return the status codes and error text the real servers send", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator();
                sim.Enqueue(
                    SimulatedTurn.Of(SimulatedTurnKindEnum.OllamaToolCallParse500),
                    SimulatedTurn.Of(SimulatedTurnKindEnum.Backpressure429),
                    SimulatedTurn.Of(SimulatedTurnKindEnum.QuotaExhausted429),
                    SimulatedTurn.Of(SimulatedTurnKindEnum.RunnerTerminated500),
                    SimulatedTurn.Of(SimulatedTurnKindEnum.OutOfMemory500),
                    SimulatedTurn.Of(SimulatedTurnKindEnum.ContextOverflow400));
                using HttpClient http = new HttpClient();
                HttpResponseMessage parse = await RawPostAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                HttpResponseMessage busy = await RawPostAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                HttpResponseMessage quota = await RawPostAsync(http, sim.BaseUrl + "/v1/chat/completions").ConfigureAwait(false);
                HttpResponseMessage runner = await RawPostAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                HttpResponseMessage memory = await RawPostAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                HttpResponseMessage overflow = await RawPostAsync(http, sim.BaseUrl + "/v1/chat/completions").ConfigureAwait(false);
                AssertEqual(500, (int)parse.StatusCode); AssertContains("error parsing tool call", await parse.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(429, (int)busy.StatusCode); AssertTrue(busy.Headers.Contains("Retry-After"), "backpressure sends Retry-After");
                AssertContains("maximum pending requests exceeded", await busy.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(429, (int)quota.StatusCode); AssertContains("insufficient_quota", await quota.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(500, (int)runner.StatusCode); AssertContains("llama runner process has terminated", await runner.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(500, (int)memory.StatusCode); AssertContains("model requires more system memory", await memory.Content.ReadAsStringAsync().ConfigureAwait(false));
                AssertEqual(400, (int)overflow.StatusCode); AssertContains("context_length_exceeded", await overflow.Content.ReadAsStringAsync().ConfigureAwait(false));
            }));

            cases.Add(Case("streaming_formats", "Streaming uses NDJSON on Ollama and server-sent events ending in [DONE] on OpenAI-compatible routes", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator();
                sim.Enqueue(SimulatedTurn.Say("a reply long enough to be split into several chunks"), SimulatedTurn.Say("a reply long enough to be split into several chunks"));
                string ollama = await PostAsync(sim.BaseUrl + "/api/chat", "{}").ConfigureAwait(false);
                string openAi = await PostAsync(sim.BaseUrl + "/v1/chat/completions", "{\"stream\":true}").ConfigureAwait(false);
                string[] lines = ollama.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                AssertTrue(lines.Length > 3, "Ollama streams by default, one JSON object per line");
                AssertContains("\"done\":true", lines[lines.Length - 1]);
                AssertContains("data: [DONE]", openAi);
                AssertContains("chat.completion.chunk", openAi);
            }));

            cases.Add(Case("length_reasoning_and_prefix", "Length cuts report length; leaked reasoning ends in a bare close tag; prefixed proxy routes and metadata answer", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator { ContextLength = 262144, ModelName = "qwen-sim" };
                sim.Enqueue(SimulatedTurn.Of(SimulatedTurnKindEnum.LengthCut), SimulatedTurn.Of(SimulatedTurnKindEnum.ReasoningWithoutOpenTag));
                string cut = await PostAsync(sim.BaseUrl + "/v1.0/api/qwen/api/chat", "{\"stream\":false}").ConfigureAwait(false);
                string leaked = await PostAsync(sim.BaseUrl + "/v1.0/api/qwen/v1/chat/completions", "{}").ConfigureAwait(false);
                AssertContains("\"done_reason\":\"length\"", cut);
                AssertContains("\\u003C/think\\u003E", leaked);
                AssertFalse(leaked.Contains("\\u003Cthink\\u003E", StringComparison.Ordinal), "the opening tag is missing");
                AssertContains("262144", await PostAsync(sim.BaseUrl + "/v1.0/api/qwen/api/show", "{}").ConfigureAwait(false));
                using HttpClient http = new HttpClient();
                AssertContains("\"max_model_len\":262144", await http.GetStringAsync(sim.BaseUrl + "/v1/models").ConfigureAwait(false));
                AssertEqual(4, sim.Requests.Count);
            }));

            cases.Add(Case("hang_closes_without_reply", "A hang sends no reply until the configured delay, then drops the connection", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator { HangMs = 300 };
                sim.Enqueue(SimulatedTurn.Of(SimulatedTurnKindEnum.Hang));
                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                bool replied = false;
                try
                {
                    using HttpResponseMessage response = await RawPostAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    replied = response.IsSuccessStatusCode && text.Length > 0;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                AssertTrue(watch.ElapsedMilliseconds >= 250, "nothing arrives before the hang ends");
                AssertFalse(replied, "the client never gets a successful reply");
            }));

            cases.Add(Case("cold_load_and_proxy_timeout_are_transient", "A cold runner start and a proxy first-byte 504 classify as Transient, not a task or model failure (RS-7.16)", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator();
                sim.Enqueue(SimulatedTurn.Of(SimulatedTurnKindEnum.ModelLoading500), SimulatedTurn.Of(SimulatedTurnKindEnum.ProxyFirstByteTimeout504));
                using HttpClient http = new HttpClient();
                RuntimeProviderError loading = await ProviderErrorAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                RuntimeProviderError proxy = await ProviderErrorAsync(http, sim.BaseUrl + "/v1/chat/completions").ConfigureAwait(false);
                AssertEqual(500, loading.HttpStatusCode ?? 0);
                AssertEqual(OpenModelErrorPatterns.ModelLoading, loading.ErrorType);
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, loading));
                AssertEqual(504, proxy.HttpStatusCode ?? 0);
                AssertEqual(OpenModelErrorPatterns.ProxyTimeout, proxy.ErrorType);
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, proxy));

                AssertEqual(OpenModelErrorPatterns.ModelLoading, OpenModelErrorPatterns.Match(503, "Model is still loading, please retry"));
                AssertEqual(OpenModelErrorPatterns.ProxyTimeout, OpenModelErrorPatterns.Match(502, "upstream request timeout"));
                AssertEqual(OpenModelErrorPatterns.ProxyTimeout, OpenModelErrorPatterns.Match(504, "{\"error\":\"timeout\"}"));
                AssertEqual(RuntimeFailureKindEnum.ModelUnavailable, RuntimeFailureClassifier.Classify(1, RuntimeProviderErrorParser.FromHttpStatus(500, "model requires more system memory (21.3 GiB) than is available (12.1 GiB)")),
                    "an out-of-memory load is still not transient");
                AssertFalse(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(500, "timed out waiting for llama runner to start - progress 0.00 - "), "a cold load is not a degenerate generation");
                AssertFalse(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(504, "504 Gateway Time-out"), "a proxy timeout is not a degenerate generation");
            }));

            cases.Add(Case("token_repeat_limit_is_degenerate_task_failure", "Ollama's token repeat limit 500 is a degenerate-output task failure and, with the tool-call parse 500, a malformed or degenerate generation (RS-7.17)", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator();
                sim.Enqueue(SimulatedTurn.Of(SimulatedTurnKindEnum.TokenRepeatLimit500), SimulatedTurn.Of(SimulatedTurnKindEnum.OllamaToolCallParse500), SimulatedTurn.Of(SimulatedTurnKindEnum.RunnerTerminated500));
                using HttpClient http = new HttpClient();
                RuntimeProviderError repeat = await ProviderErrorAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                RuntimeProviderError parse = await ProviderErrorAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                RuntimeProviderError runner = await ProviderErrorAsync(http, sim.BaseUrl + "/api/chat").ConfigureAwait(false);
                AssertEqual(OpenModelErrorPatterns.DegenerateOutput, repeat.ErrorType);
                AssertEqual(RuntimeFailureKindEnum.TaskFailure, RuntimeFailureClassifier.Classify(1, repeat), "a repetition loop is the model's task failure, not a transient server fault");
                AssertTrue(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(repeat.HttpStatusCode, repeat.Message), "token repeat limit");
                AssertTrue(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(parse.HttpStatusCode, parse.Message), "tool-call parse 500");
                AssertFalse(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(runner.HttpStatusCode, runner.Message), "a runner crash is retried as is");

                AssertTrue(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(500, "failed to parse JSON for tool call arguments: invalid character '}' looking for beginning of value"));
                AssertTrue(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(null, "token repeat limit reached"), "an unknown status is judged from the body");
                AssertFalse(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(500, "invalid character 'x' in request body"), "a JSON failure that names no tool call");
                AssertFalse(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(200, "token repeat limit reached"), "a success status never counts");
                AssertFalse(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(429, "error parsing tool call"), "a 429 is backpressure");
                AssertFalse(OpenModelErrorPatterns.IsMalformedOrDegenerateGeneration(500, null));
            }));

            cases.Add(Case("ollama_warmup_sends_keep_alive", "The Ollama warm-up sends a prompt-less /api/generate load with keep_alive; other providers and bad URLs are skipped; failures are results (RS-7.16)", async () =>
            {
                using OpenModelSimulator sim = new OpenModelSimulator { ModelName = "qwen-sim" };
                ModelEndpoint ollama = new ModelEndpoint { Name = "sim", Kind = ModelEndpointKindEnum.Inference, Provider = ModelProviderEnum.Ollama, Model = "qwen-sim", BaseUrl = sim.BaseUrl + "/v1" };
                AssertTrue(OllamaModelWarmup.Applies(ollama));
                OllamaWarmupResult warmed = await OllamaModelWarmup.WarmAsync(ollama, keepAlive: "45m", timeoutSeconds: 10).ConfigureAwait(false);
                AssertTrue(warmed.Attempted && warmed.Succeeded, "the load request succeeds: " + warmed.Reason);
                AssertEqual(200, warmed.HttpStatusCode ?? 0);
                SimulatorRequest load = sim.Requests.Single();
                AssertEqual("POST", load.Method);
                AssertEqual("/api/generate", load.Path);
                AssertContains("\"model\":\"qwen-sim\"", load.Body);
                AssertContains("\"keep_alive\":\"45m\"", load.Body);
                AssertFalse(load.Body.Contains("prompt", StringComparison.Ordinal), "no prompt, so nothing is generated");

                ModelEndpoint openAi = new ModelEndpoint { Name = "oa", Kind = ModelEndpointKindEnum.Inference, Provider = ModelProviderEnum.OpenAI, Model = "m", BaseUrl = sim.BaseUrl + "/v1" };
                AssertFalse(OllamaModelWarmup.Applies(openAi));
                OllamaWarmupResult skipped = await OllamaModelWarmup.WarmAsync(openAi, timeoutSeconds: 10).ConfigureAwait(false);
                AssertFalse(skipped.Attempted, "only Ollama endpoints are warmed");
                AssertEqual(1, sim.Requests.Count);

                ModelEndpoint unreachable = new ModelEndpoint { Name = "down", Kind = ModelEndpointKindEnum.Inference, Provider = ModelProviderEnum.Ollama, Model = "m", BaseUrl = "http://127.0.0.1:" + TestPorts.Reserve(1)[0] };
                OllamaWarmupResult failed = await OllamaModelWarmup.WarmAsync(unreachable, timeoutSeconds: 10).ConfigureAwait(false);
                AssertTrue(failed.Attempted && !failed.Succeeded, "an unreachable server is a failed result, not an exception");
                AssertNotNull(failed.Reason);
                AssertThrows<ArgumentOutOfRangeException>(() => OllamaModelWarmup.WarmAsync(ollama, timeoutSeconds: 0).GetAwaiter().GetResult());
            }));

            foreach (SimulatedTurnKindEnum kind in Enum.GetValues(typeof(SimulatedTurnKindEnum)).Cast<SimulatedTurnKindEnum>().Where(k => k != SimulatedTurnKindEnum.RawResponse))
            {
                SimulatedTurnKindEnum captured = kind;
                cases.Add(Case("api_runtime_ends_on_" + captured.ToString().ToLowerInvariant(), "The API runtime run against " + captured + " ends instead of hanging", () => RuntimeEndsAsync(captured)));
            }

            cases.Add(Case("retry_after_is_honored", "A 429 with a short Retry-After is retried in the run; a long one ends the run as Transient carrying the wait (RS-5.4)", async () =>
            {
                RuntimeProviderError? shortWait = await RunBackpressureAsync("1", 1).ConfigureAwait(false);
                AssertNull(shortWait, "a one-second Retry-After is waited out inside the run, which then finishes");

                RuntimeProviderError? longWait = await RunBackpressureAsync("120", 3).ConfigureAwait(false);
                AssertNotNull(longWait, "a two-minute Retry-After goes back to the Admiral");
                AssertEqual(429, longWait!.HttpStatusCode ?? 0);
                AssertEqual(120, longWait.RetryAfterSeconds ?? 0, "the wait travels with the provider error");
                AssertEqual(RuntimeFailureKindEnum.Transient, RuntimeFailureClassifier.Classify(1, longWait), "backpressure does not quarantine");
                AssertEqual(30, Armada.Runtimes.ApiAgentRuntime.ParseRetryAfterSeconds("30", DateTime.UtcNow) ?? 0);
                AssertEqual(1, Armada.Runtimes.ApiAgentRuntime.ParseRetryAfterSeconds("0", DateTime.UtcNow) ?? 0);
                AssertEqual(60, Armada.Runtimes.ApiAgentRuntime.ParseRetryAfterSeconds(new DateTime(2026, 1, 1, 0, 1, 0, DateTimeKind.Utc).ToString("R"), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)) ?? 0);
                AssertNull(Armada.Runtimes.ApiAgentRuntime.ParseRetryAfterSeconds("soon", DateTime.UtcNow));
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Open-model simulator", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static async Task RuntimeEndsAsync(SimulatedTurnKindEnum kind)
        {
            string dir = TestTemp.NewDirectory("open_model_sim");
            try
            {
                File.WriteAllText(Path.Combine(dir, "a.txt"), "alpha");
                using OpenModelSimulator sim = new OpenModelSimulator { HangMs = 2000 };
                SimulatedTurn first = kind switch
                {
                    SimulatedTurnKindEnum.NativeToolCall or SimulatedTurnKindEnum.HermesTextToolCall or SimulatedTurnKindEnum.FunctionTagTextToolCall
                        or SimulatedTurnKindEnum.FencedJsonTextToolCall or SimulatedTurnKindEnum.MalformedToolArguments => SimulatedTurn.Call(kind, "read_file", kind == SimulatedTurnKindEnum.MalformedToolArguments ? "{\"path\": \"a.txt\"" : "{\"path\":\"a.txt\"}"),
                    SimulatedTurnKindEnum.UnknownToolName => SimulatedTurn.Call(kind, "open_file_in_editor", "{\"path\":\"a.txt\"}"),
                    _ => SimulatedTurn.Of(kind)
                };
                // After the scripted behavior, the model finishes; a runtime that retries or nudges sees the fallback reply.
                sim.FallbackText = "DONE";
                sim.Enqueue(first);

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "sim",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.Ollama,
                    Model = sim.ModelName,
                    BaseUrl = sim.BaseUrl,
                    InferenceOptions = new InferenceOptions { McpToolsEnabled = false, MaxIterations = 8, ContextLength = 32768 }
                };
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ApiAgentRuntime runtime = new ApiAgentRuntime(endpoint, logging, 8);
                using ManualResetEventSlim exited = new ManualResetEventSlim(false);
                runtime.OnProcessExited += (pid, value) => exited.Set();
                await runtime.StartAsync(dir, "Read a.txt with read_file, then reply with exactly: DONE", new Dictionary<string, string>()).ConfigureAwait(false);
                AssertTrue(exited.Wait(TimeSpan.FromSeconds(90)), kind + ": the runtime must end, successfully or not, within 90 seconds");
                AssertTrue(sim.Requests.Count >= 1, kind + ": the runtime called the model");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static async Task<RuntimeProviderError?> RunBackpressureAsync(string retryAfter, int busyTurns)
        {
            string dir = Path.Combine(Path.GetTempPath(), "armada_sim_429_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                using OpenModelSimulator sim = new OpenModelSimulator();
                sim.FallbackText = "DONE";
                for (int i = 0; i < busyTurns; i++)
                {
                    SimulatedTurn busy = SimulatedTurn.Of(SimulatedTurnKindEnum.Backpressure429);
                    busy.Headers["Retry-After"] = retryAfter;
                    sim.Enqueue(busy);
                }

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "sim",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.Ollama,
                    Model = sim.ModelName,
                    BaseUrl = sim.BaseUrl,
                    InferenceOptions = new InferenceOptions { StreamingMode = InferenceStreamingModeEnum.Off, McpToolsEnabled = false, MaxIterations = 4, ContextLength = 32768 }
                };
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                ApiAgentRuntime runtime = new ApiAgentRuntime(endpoint, logging, 8);
                runtime.InferenceRetryBaseDelay = TimeSpan.Zero;
                RuntimeProviderError? reported = null;
                runtime.OnProviderError += (pid, error) => reported = error;
                using ManualResetEventSlim exited = new ManualResetEventSlim(false);
                runtime.OnProcessExited += (pid, value) => exited.Set();
                await runtime.StartAsync(dir, "Reply with exactly: DONE", new Dictionary<string, string>()).ConfigureAwait(false);
                AssertTrue(exited.Wait(TimeSpan.FromSeconds(60)), "the runtime ends within 60 seconds");
                return reported;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static async Task<string> PostAsync(string url, string json)
        {
            using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.PostAsync(url, content).ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private static async Task<RuntimeProviderError> ProviderErrorAsync(HttpClient http, string url)
        {
            using HttpResponseMessage response = await RawPostAsync(http, url).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            RuntimeProviderError? error = RuntimeProviderErrorParser.FromHttpStatus((int)response.StatusCode, body);
            AssertNotNull(error, "an error status yields a provider error");
            return error!;
        }

        private static async Task<HttpResponseMessage> RawPostAsync(HttpClient http, string url)
        {
            StringContent content = new StringContent("{\"stream\":false}", Encoding.UTF8, "application/json");
            return await http.PostAsync(url, content).ConfigureAwait(false);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { TestTags.Reliability });
        }

        #endregion
    }
}
