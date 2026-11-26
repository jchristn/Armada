namespace Armada.Runtimes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Interfaces;
    using Armada.Runtimes.Mcp;
    using Armada.Runtimes.Tools;
    using Armada.Runtimes.Tools.Tasks;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;
    using ArmadaToolDefinition = Armada.Runtimes.Tools.ToolDefinition;
    using PolyToolDefinition = PolyPrompt.Models.ToolDefinition;

    /// <summary>
    /// An agent runtime that drives a configured inference <see cref="ModelEndpoint"/> as a captain. Instead of
    /// spawning a CLI harness, it runs an in-process tool-calling loop (via PolyPrompt) over the Armada
    /// built-in coding tools, executing every tool against the mission's working directory. It surfaces the
    /// same <see cref="IAgentRuntime"/> events as a process-backed runtime (started, output, exited), so the
    /// agent lifecycle handler treats it identically. Processless: it uses a synthetic process id and
    /// cooperative cancellation keyed by that id.
    /// </summary>
    public class ApiAgentRuntime : IAgentRuntime
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name => _Endpoint != null ? "ApiEndpoint:" + _Endpoint.Name : "ApiEndpoint";

        /// <inheritdoc />
        public bool SupportsResume => false;

        /// <summary>
        /// Retries of one inference call after a retryable failure (no HTTP status, 408, 429, 5xx, 529, or a transport
        /// exception) before the run gives up, from 0 through 5. A model call has no side effects, so retrying it keeps
        /// the conversation instead of ending the run on a single transient blip.
        /// </summary>
        public int InferenceRetries
        {
            get => _InferenceRetries;
            set => _InferenceRetries = value < 0 ? 0 : (value > 5 ? 5 : value);
        }

        /// <summary>
        /// Delay before the first inference retry; each further retry doubles it, with jitter.
        /// </summary>
        public TimeSpan InferenceRetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// A 502 or 504 that arrives after at least this long is reported as a proxy deadline (with what to change) rather
        /// than as a bare gateway status.
        /// </summary>
        public TimeSpan GatewayDeadlineHint { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Passive rate-limit header capture for this runtime's inference calls. The production client factory
        /// attaches it to every completion client's transport (including the provider-default path), so each
        /// response's allowlisted rate-limit headers are observed as soon as they arrive, before any body is
        /// read; nothing is ever requested to obtain them.
        /// </summary>
        public Armada.Runtimes.Usage.HeaderUsageCapture UsageHeaders { get; } = new Armada.Runtimes.Usage.HeaderUsageCapture();

        /// <summary>
        /// While one model call is still in flight, a progress line is written this often, so a long generation on a slow
        /// model is not mistaken for a stalled captain (stall detection watches output). The call stays bounded by its own
        /// deadlines: the request timeout, or with streaming the first-response and idle deadlines.
        /// </summary>
        public TimeSpan LivenessInterval { get; set; } = TimeSpan.FromSeconds(60);

        /// <inheritdoc />
        public bool SupportsPlanningSessions => Armada.Core.Services.AgentRuntimeCapabilities.SupportsPlanningSessions(Armada.Core.Enums.AgentRuntimeEnum.ApiEndpoint);

        /// <inheritdoc />
        public event Action<int, string>? OnOutputReceived;

        /// <inheritdoc />
        public event Action<int, string>? OnStdoutReceived;

        /// <inheritdoc />
        public event Action<int>? OnProcessStarted;

        /// <inheritdoc />
        public event Action<int, int?>? OnProcessExited;

        /// <inheritdoc />
        public event Action<int, RuntimeProviderError>? OnProviderError;

        /// <summary>
        /// Raised for each tool call phase (started, completed). This is the typed channel for tool activity:
        /// tool events are never written in-band on <see cref="OnStdoutReceived"/>, so model text cannot spoof a
        /// tool card. Arguments: synthetic process id, event.
        /// </summary>
        public event Action<int, ApiRuntimeToolEvent>? OnToolEvent;

        /// <inheritdoc />
        public event Action<int, RuntimeActivity>? OnActivity;

        /// <summary>
        /// Raised for each runtime diagnostic (MCP status, tool call/result echoes, warnings, errors, cancellation).
        /// Diagnostics also go to the readable output log and <see cref="OnOutputReceived"/>, but never to
        /// <see cref="OnStdoutReceived"/>, which carries only the model's reply text. Arguments: synthetic process id,
        /// diagnostic.
        /// </summary>
        public event Action<int, ApiRuntimeDiagnostic>? OnDiagnostic;

        /// <summary>
        /// CLI tool permission policy for the built-in shell tool (run_process), or null to run it without a gate (the
        /// behavior before CLI tool permissions; direct captain chat and planning). Refuse refuses every run_process
        /// call; ApproveInArmada asks <see cref="PermissionPrompt"/> first; Bypass runs it.
        /// </summary>
        public Armada.Core.Enums.CliPermissionPolicyEnum? ShellPolicy { get; set; } = null;

        /// <summary>
        /// Answers a permission prompt for a run_process call under ApproveInArmada: (tool name, arguments JSON,
        /// cancellation) to the outcome. Null under ApproveInArmada refuses the call.
        /// </summary>
        public Func<string, string, CancellationToken, Task<Armada.Core.Models.CliPermissionPromptOutcome>>? PermissionPrompt { get; set; } = null;

        /// <summary>
        /// RS-7.8: reads the context window the model server runs (endpoint snapshot, model, cancellation) before the first
        /// turn when the endpoint configures none, updating the snapshot's discovered fields. It runs on the host that
        /// serves the run: the Admiral stores what it finds, a Harbor reports it. Null skips discovery; a window already
        /// discovered still applies.
        /// </summary>
        public Func<ModelEndpoint, string?, CancellationToken, Task>? ContextDiscovery { get; set; } = null;

        /// <summary>
        /// Name of the built-in shell tool gated by <see cref="ShellPolicy"/>.
        /// </summary>
        public const string ShellToolName = "run_process";

        #endregion

        #region Private-Members

        // Synthetic process ids start well above any real OS process id so a stray Process.GetProcessById does
        // not collide with an unrelated live process.
        // In-process ids start at a different point on each Admiral run (seconds since the epoch, bounded), so an id a
        // record from the previous run still holds is not handed out again after a restart (L6).
        private static int _PidCounter = 2_000_000_000 + (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100_000_000);
        private int _InferenceRetries = 2;

        // RS-5.4: the longest Retry-After the runtime waits out inside one run; longer waits return to the Admiral.
        private const int MaxInRunRetryAfterSeconds = 30;
        // Outer bound on one streamed call; the idle and first-response watchdog is what normally ends a stalled stream.
        private const int StreamingTotalTimeoutMs = 3600000;
        private static readonly ConcurrentDictionary<int, CancellationTokenSource> _Running = new ConcurrentDictionary<int, CancellationTokenSource>();
        private static readonly ConcurrentDictionary<int, Task> _Loops = new ConcurrentDictionary<int, Task>();
        // How long a stop waits for the run's loop to finish its current step, so a relaunch into the same dock never
        // overlaps a loop that is still writing files.
        private static readonly TimeSpan _StopWaitLimit = TimeSpan.FromSeconds(15);

        private readonly ModelEndpoint? _Endpoint;
        private readonly Func<string, ModelEndpoint?>? _EndpointResolver;
        private readonly LoggingModule _Logging;
        private readonly int _MaxIterations;
        private readonly Func<ModelEndpoint, LoggingModule, CompletionClientBase> _ClientFactory;
        private readonly Func<string, string?, LoggingModule, McpToolClient> _McpClientFactory;
        private readonly string _Header = "[ApiAgentRuntime] ";
        private StreamWriter? _LogWriter;
        private readonly object _LogLock = new object();
        // A complete think block is <think> or <think ...> through </think>. The name must end at the tag
        // boundary, so a longer name and an unmatched tag stay in the text.
        private static readonly Regex _CompleteThinkBlock = new Regex(
            @"<think(?:\s[^>]*)?>.*?</think\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a specific inference endpoint.
        /// </summary>
        /// <param name="endpoint">The inference model endpoint to drive.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="maxIterations">Maximum tool-call iterations before the loop stops. Clamped to 1..1000.</param>
        /// <param name="clientFactory">Optional inference-client factory seam for testing; defaults to the
        /// production <see cref="ModelEndpointClientFactory"/>.</param>
        /// <param name="mcpClientFactory">Optional MCP-client factory seam for testing (endpoint URL, session token,
        /// logging); defaults to a network <see cref="McpToolClient"/>.</param>
        public ApiAgentRuntime(
            ModelEndpoint endpoint,
            LoggingModule logging,
            int maxIterations = 1000,
            Func<ModelEndpoint, LoggingModule, CompletionClientBase>? clientFactory = null,
            Func<string, string?, LoggingModule, McpToolClient>? mcpClientFactory = null)
        {
            _McpClientFactory = mcpClientFactory ?? ((url, sessionToken, log) => new McpToolClient(url, sessionToken, null, log));
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _MaxIterations = Math.Clamp(maxIterations, 1, 1000);
            _ClientFactory = clientFactory ?? ((ep, log) => ModelEndpointClientFactory.CreateCompletion(ep, log, UsageHeaders.Record));
        }

        /// <summary>
        /// Instantiate with a resolver that maps a captain's endpoint id to a <see cref="ModelEndpoint"/> at
        /// launch time. Used in production where the endpoint is chosen per captain.
        /// </summary>
        /// <param name="endpointResolver">Resolves a model-endpoint id to the endpoint, or null when not found.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="maxIterations">Maximum tool-call iterations before the loop stops. Clamped to 1..1000.</param>
        /// <param name="clientFactory">Optional inference-client factory seam for testing.</param>
        /// <param name="mcpClientFactory">Optional MCP-client factory seam for testing (endpoint URL, session token,
        /// logging); defaults to a network <see cref="McpToolClient"/>.</param>
        public ApiAgentRuntime(
            Func<string, ModelEndpoint?> endpointResolver,
            LoggingModule logging,
            int maxIterations = 1000,
            Func<ModelEndpoint, LoggingModule, CompletionClientBase>? clientFactory = null,
            Func<string, string?, LoggingModule, McpToolClient>? mcpClientFactory = null)
        {
            _McpClientFactory = mcpClientFactory ?? ((url, sessionToken, log) => new McpToolClient(url, sessionToken, null, log));
            _EndpointResolver = endpointResolver ?? throw new ArgumentNullException(nameof(endpointResolver));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _MaxIterations = Math.Clamp(maxIterations, 1, 1000);
            _ClientFactory = clientFactory ?? ((ep, log) => ModelEndpointClientFactory.CreateCompletion(ep, log, UsageHeaders.Record));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an API-endpoint captain loop is currently running under the given synthetic process id.
        /// </summary>
        /// <param name="processId">Synthetic process id.</param>
        /// <returns>True when the loop is tracked and alive.</returns>
        public static bool IsTracked(int processId)
        {
            return _Running.ContainsKey(processId);
        }

        /// <inheritdoc />
        public Task<int> StartAsync(
            string workingDirectory,
            string prompt,
            Dictionary<string, string>? environment = null,
            string? logFilePath = null,
            string? finalMessageFilePath = null,
            string? model = null,
            Captain? captain = null,
            bool isolateLaunch = false,
            int mcpPort = 0,
            bool showThinking = false,
            CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentNullException(nameof(workingDirectory));

            ModelEndpoint? endpoint = _Endpoint;
            if (endpoint == null && _EndpointResolver != null && captain != null && !String.IsNullOrEmpty(captain.ModelEndpointId))
                endpoint = _EndpointResolver(captain.ModelEndpointId);

            if (endpoint == null)
                throw new InvalidOperationException("API-endpoint captain has no resolvable inference endpoint. Set the captain's model endpoint to a configured Inference endpoint.");

            if (!endpoint.Enabled || endpoint.Kind != Armada.Core.Enums.ModelEndpointKindEnum.Inference)
                throw new InvalidOperationException("API captains require an enabled Inference endpoint.");
            endpoint.InferenceOptions.Validate();
            // Snapshot configuration so edits cannot change restrictions midway through a run.
            ModelEndpoint snapshot = JsonSerializer.Deserialize<ModelEndpoint>(JsonSerializer.Serialize(endpoint))!;
            snapshot.ApiKey = endpoint.ApiKey;
            snapshot.Headers = endpoint.Headers.Select(h => new ModelEndpointHeader(h.Name, h.Value)).ToList();
            endpoint = snapshot;

            // Optional MCP tool access: when the caller supplies an MCP endpoint (and, typically, a
            // short-lived per-user session token) the in-process loop also exposes that server's tools to
            // the model alongside the built-in coding tools. This is what lets an Ask Armada chat backed by
            // an inference endpoint actually drive Armada's own orchestration tools, scoped to the caller.
            string? mcpUrl = null;
            string? mcpToken = null;
            if (environment != null)
            {
                environment.TryGetValue("ARMADA_MCP_URL", out mcpUrl);
                environment.TryGetValue("ARMADA_MCP_TOKEN", out mcpToken);
            }

            // The Admiral states whether this launch must end with the completion line; the prompt is never searched for it.
            bool requiresCompletionLine = environment != null
                && environment.TryGetValue(CompletionLine.RequiredEnvironmentVariable, out string? requiredFlag)
                && String.Equals(requiredFlag, "1", StringComparison.Ordinal);

            // The Admiral names the Armada MCP tools this persona is shown up front; the rest stay callable through discovery.
            HashSet<string>? toolScope = null;
            if (environment != null && environment.TryGetValue(PersonaToolScope.EnvironmentVariable, out string? scopeValue))
                toolScope = PersonaToolScope.Parse(scopeValue);

            // RS-6.3: the Admiral names the submit tool this stage calls; the runtime offers it natively.
            string? submitToolName = null;
            string? submitPersona = null;
            if (environment != null)
            {
                environment.TryGetValue(StageSubmissionTools.ToolEnvironmentVariable, out submitToolName);
                environment.TryGetValue(StageSubmissionTools.PersonaEnvironmentVariable, out submitPersona);
            }

            int processId = Interlocked.Increment(ref _PidCounter);
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _Running[processId] = cts;

            OpenLog(logFilePath, prompt, endpoint);
            OnProcessStarted?.Invoke(processId);

            // Run the loop in the background so StartAsync returns the pid promptly, mirroring a process launch.
            NativeSubmitTool? submitTool = NativeSubmitTool.FromEnvironment(submitToolName, submitPersona, processId);
            Task loop = Task.Run(() => RunLoopAsync(processId, endpoint, workingDirectory, prompt, model, captain?.ReasoningEffort, finalMessageFilePath, mcpUrl, mcpToken, requiresCompletionLine, toolScope, submitTool, cts));
            _Loops[processId] = loop;
            _ = loop.ContinueWith(_ => _Loops.TryRemove(processId, out Task? _), TaskScheduler.Default);

            return Task.FromResult(processId);
        }

        /// <inheritdoc />
        public async Task StopAsync(int processId, CancellationToken token = default)
        {
            if (_Running.TryGetValue(processId, out CancellationTokenSource? cts))
            {
                try { cts.Cancel(); } catch { }
            }

            // Wait (bounded) for the loop to leave its current model call or tool, so the caller can relaunch into the
            // same working directory without two loops editing it at once.
            if (_Loops.TryGetValue(processId, out Task? loop))
            {
                Task finished = await Task.WhenAny(loop, Task.Delay(_StopWaitLimit, token)).ConfigureAwait(false);
                if (finished != loop) _Logging.Warn(_Header + "process " + processId + " did not finish within " + _StopWaitLimit.TotalSeconds + " s of the stop request");
            }
        }

        /// <inheritdoc />
        public Task<bool> IsRunningAsync(int processId, CancellationToken token = default)
        {
            return Task.FromResult(_Running.ContainsKey(processId));
        }

        #endregion

        #region Private-Methods

        private async Task<T> WithInferenceRetryAsync<T>(Func<Task<T>> call, Func<T, bool> succeeded, Func<T, int?> status, Func<T, string?> error, int processId, StrongBox<long> lastCallMs, CancellationToken token, Func<int?>? retryAfterSeconds = null)
        {
            for (int attempt = 0; ; attempt++)
            {
                T result;
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    Task<T> pending = call();
                    while (!pending.IsCompleted && !token.IsCancellationRequested)
                    {
                        Task tick = Task.Delay(LivenessInterval, token);
                        if (await Task.WhenAny(pending, tick).ConfigureAwait(false) == pending || token.IsCancellationRequested) break;
                        EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Inference, "[inference] waiting for the model ("
                            + (watch.ElapsedMilliseconds / 1000) + " s so far); the call's own deadlines still apply");
                    }
                    result = await pending.ConfigureAwait(false);
                    lastCallMs.Value = watch.ElapsedMilliseconds;
                }
                catch (Exception ex) when (attempt < _InferenceRetries && !token.IsCancellationRequested
                    && (ex is System.Net.Http.HttpRequestException || ex is IOException || ex is TaskCanceledException || ex is TimeoutException))
                {
                    await DelayBeforeInferenceRetryAsync(attempt, processId, ex.Message, token).ConfigureAwait(false);
                    continue;
                }

                if (succeeded(result) || attempt >= _InferenceRetries || !IsRetryableInferenceStatus(status(result))) return result;
                // RS-5.4: honor the server's Retry-After. A wait longer than the in-run cap goes back to the Admiral, which
                // resumes the captain after it instead of holding an HTTP retry loop open.
                int? waitSeconds = retryAfterSeconds?.Invoke();
                if (waitSeconds.HasValue && waitSeconds.Value > MaxInRunRetryAfterSeconds) return result;
                await DelayBeforeInferenceRetryAsync(attempt, processId, (status(result).HasValue ? "HTTP " + status(result) + " " : String.Empty) + (error(result) ?? "no detail"), token, waitSeconds).ConfigureAwait(false);
            }
        }

        private static async Task<ToolChatResponse> StreamToolChatAsync(CompletionClientBase client, ToolChatRequest request, int firstResponseMs, int idleMs, CancellationToken token)
        {
            using CancellationTokenSource watchdog = CancellationTokenSource.CreateLinkedTokenSource(token);
            watchdog.CancelAfter(firstResponseMs);
            bool received = false;
            try
            {
                ToolChatStreamingResponse stream = await client.ToolChatStreamingAsync(request, watchdog.Token).ConfigureAwait(false);
                if (!stream.Success) return new ToolChatResponse { Success = false, StatusCode = stream.StatusCode, Error = stream.Error, Model = stream.Model };
                await foreach (ToolChatStreamingChunk chunk in stream.Chunks.WithCancellation(watchdog.Token).ConfigureAwait(false))
                {
                    received = true;
                    watchdog.CancelAfter(idleMs);
                }

                // Partial output is never acted on: tools run only after the provider signalled a complete response.
                if (stream.State != StreamingState.Completed)
                    return new ToolChatResponse { Success = false, Model = stream.Model, Error = "The response stream ended before the model finished (" + stream.State + "): " + (stream.StreamError ?? "no detail") };

                return new ToolChatResponse
                {
                    Success = true,
                    StatusCode = stream.StatusCode,
                    Model = stream.Model,
                    ProviderState = stream.ProviderState,
                    OverallRuntimeMs = stream.OverallRuntimeMs,
                    Text = stream.Text,
                    Reasoning = stream.Reasoning,
                    ToolCalls = stream.ToolCalls,
                    ResponseId = stream.ResponseId,
                    FinishReason = stream.FinishReason,
                    Usage = stream.Usage
                };
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException(StreamTimeoutMessage(received, firstResponseMs, idleMs));
            }
        }

        private static async Task<ChatResponse> StreamChatAsync(CompletionClientBase client, string prompt, CompletionOptions options, int firstResponseMs, int idleMs, CancellationToken token)
        {
            using CancellationTokenSource watchdog = CancellationTokenSource.CreateLinkedTokenSource(token);
            watchdog.CancelAfter(firstResponseMs);
            bool received = false;
            try
            {
                ChatStreamingResponse stream = await client.ChatStreamingAsync(prompt, options, watchdog.Token).ConfigureAwait(false);
                if (!stream.Success) return new ChatResponse { Success = false, StatusCode = stream.StatusCode, Error = stream.Error, Model = stream.Model };
                System.Text.StringBuilder text = new System.Text.StringBuilder();
                await foreach (ChatStreamingChunk chunk in stream.Chunks.WithCancellation(watchdog.Token).ConfigureAwait(false))
                {
                    received = true;
                    watchdog.CancelAfter(idleMs);
                    if (!String.IsNullOrEmpty(chunk.Text)) text.Append(chunk.Text);
                }

                if (stream.State != StreamingState.Completed)
                    return new ChatResponse { Success = false, Model = stream.Model, Error = "The response stream ended before the model finished (" + stream.State + "): " + (stream.StreamError ?? "no detail") };

                return new ChatResponse
                {
                    Success = true,
                    StatusCode = stream.StatusCode,
                    Model = stream.Model,
                    ProviderState = stream.ProviderState,
                    OverallRuntimeMs = stream.OverallRuntimeMs,
                    Text = text.ToString(),
                    Reasoning = stream.Reasoning,
                    ResponseId = stream.ResponseId,
                    FinishReason = stream.FinishReason,
                    Usage = stream.Usage
                };
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException(StreamTimeoutMessage(received, firstResponseMs, idleMs));
            }
        }

        private RuntimeProviderError InferenceFailure(int? status, string? error, string fallbackReason, long lastCallMs, bool streaming, int? retryAfterSeconds = null)
        {
            RuntimeProviderError? failure = status.HasValue ? RuntimeProviderErrorParser.FromHttpStatus(status.Value, error, retryAfterSeconds) : null;
            if (failure == null) return new RuntimeProviderError { ErrorType = "inference_error", Reason = fallbackReason };

            // A gateway error that arrives only after a long wait is a proxy or load balancer giving up before the model
            // answered, not the model failing. Retrying cannot help while the cause remains, so name it.
            if ((status == 502 || status == 504) && lastCallMs >= GatewayDeadlineHint.TotalMilliseconds)
            {
                string seconds = (lastCallMs / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                failure.Reason = "HTTP " + status + " from a gateway after " + seconds + " s with no response from the model. A proxy or load balancer in front of the endpoint "
                    + "is cutting the request before the model replies. "
                    + (streaming
                        ? "The response was streamed, so the model sent nothing in that time: usually a cold model load or long prompt evaluation. Raise the proxy's time-to-first-byte limit or keep the model loaded."
                        : "Enable streaming for this endpoint so headers arrive with the first token, and raise the proxy's time-to-first-byte limit if cold model loads take longer than it.");
            }
            return failure;
        }

        private static string StreamTimeoutMessage(bool received, int firstResponseMs, int idleMs)
        {
            return received
                ? "The model stopped sending in the middle of a response; nothing arrived for " + idleMs + " ms (the endpoint request timeout)."
                : "The model did not start responding within " + firstResponseMs + " ms (first-response deadline: model load and prompt evaluation).";
        }

        private static int? LastRetryAfterSeconds(CompletionClientBase client)
        {
            List<CallDetail> calls = client.CallDetails;
            if (calls.Count == 0) return null;
            CallDetail last = calls[calls.Count - 1];
            if (last.ResponseHeaders == null) return null;
            foreach (KeyValuePair<string, string> header in last.ResponseHeaders)
            {
                if (!String.Equals(header.Key, "Retry-After", StringComparison.OrdinalIgnoreCase)) continue;
                return ParseRetryAfterSeconds(header.Value, DateTime.UtcNow);
            }
            return null;
        }

        /// <summary>
        /// Parse an HTTP Retry-After value: delta seconds or an HTTP date.
        /// </summary>
        /// <param name="value">Header value.</param>
        /// <param name="nowUtc">Current time, for an HTTP date.</param>
        /// <returns>Whole seconds to wait (at least 1), or null when the value is missing or unreadable.</returns>
        public static int? ParseRetryAfterSeconds(string? value, DateTime nowUtc)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string text = value.Trim();
            if (Int32.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int seconds))
                return Math.Max(1, seconds);
            if (DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset date))
            {
                double delta = (date.UtcDateTime - nowUtc).TotalSeconds;
                return delta <= 0 ? 1 : (int)Math.Min(Int32.MaxValue, Math.Ceiling(delta));
            }
            return null;
        }

        private static bool IsRetryableInferenceStatus(int? status)
        {
            if (!status.HasValue) return true;
            int code = status.Value;
            return code == 408 || code == 429 || code == 500 || code == 502 || code == 503 || code == 504 || code == 529;
        }

        private async Task DelayBeforeInferenceRetryAsync(int attempt, int processId, string reason, CancellationToken token, int? retryAfterSeconds = null)
        {
            double baseMs = Math.Max(0, InferenceRetryBaseDelay.TotalMilliseconds) * Math.Pow(2, attempt);
            double jittered = baseMs * (0.75 + Random.Shared.NextDouble() * 0.5);
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value * 1000.0 > jittered) jittered = retryAfterSeconds.Value * 1000.0;
            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Error, "[retry] inference call failed (" + reason + "); retry " + (attempt + 1) + " of " + _InferenceRetries + " in " + (jittered / 1000).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s");
            if (jittered > 0) await Task.Delay(TimeSpan.FromMilliseconds(jittered), token).ConfigureAwait(false);
        }

        private async Task RunLoopAsync(
            int processId,
            ModelEndpoint endpoint,
            string workingDirectory,
            string prompt,
            string? model,
            Armada.Core.Enums.ReasoningEffortEnum? reasoningEffort,
            string? finalMessageFilePath,
            string? mcpUrl,
            string? mcpToken,
            bool requiresCompletionLine,
            HashSet<string>? toolScope,
            NativeSubmitTool? submitTool,
            CancellationTokenSource cts)
        {
            int exitCode = 0;
            string finalText = String.Empty;
            CancellationToken token = cts.Token;
            McpToolClient? mcpClient = null;

            try
            {
                if (ContextDiscovery != null && !endpoint.InferenceOptions.ContextLength.HasValue) await ContextDiscovery(endpoint, model, token).ConfigureAwait(false);
                if (ModelContextDiscovery.ApplyWindow(endpoint))
                    EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Inference, "[context] window " + endpoint.InferenceOptions.ContextLength + " tokens, discovered from " + endpoint.DiscoveredContextSource);
                using CompletionClientBase client = _ClientFactory(endpoint, _Logging);
                if (!String.IsNullOrWhiteSpace(model)) client.Model = model;
                // Only the last call's response headers are read (Retry-After); retaining more holds request bodies in memory.
                client.MaxCallDetails = 2;

                InferenceOptions settings = endpoint.InferenceOptions;
                // Without streaming the client timeout bounds the whole call, so the first-response deadline (cold model
                // loads, long prompt evaluation) has to extend it. With streaming the watchdog in StreamToolChatAsync and
                // StreamChatAsync enforces both deadlines and the client timeout is only an outer bound.
                int firstResponseMs = Math.Max(endpoint.TimeoutMs, settings.FirstResponseTimeoutMs ?? 0);
                int idleMs = endpoint.TimeoutMs;
                StrongBox<long> lastCallMs = new StrongBox<long>(0);
                client.TimeoutMs = settings.Streaming ? StreamingTotalTimeoutMs : firstResponseMs;
                CompletionOptions completionOptions = new CompletionOptions
                {
                    Model = String.IsNullOrWhiteSpace(model) ? endpoint.Model : model,
                    MaxTokens = settings.MaxTokens,
                    Temperature = settings.Temperature,
                    TopP = settings.TopP,
                    ReasoningEffort = reasoningEffort switch
                    {
                        Armada.Core.Enums.ReasoningEffortEnum.Off => ReasoningEffort.Minimal,
                        Armada.Core.Enums.ReasoningEffortEnum.Minimal => ReasoningEffort.Minimal,
                        Armada.Core.Enums.ReasoningEffortEnum.Low => ReasoningEffort.Low,
                        Armada.Core.Enums.ReasoningEffortEnum.Medium => ReasoningEffort.Medium,
                        Armada.Core.Enums.ReasoningEffortEnum.High => ReasoningEffort.High,
                        _ => null
                    }
                };
                // Plain chat does not send tool definitions to providers/models without tool support.
                async Task RunPlainChatAsync()
                {
                    completionOptions.SystemPrompt = BuildSystemPrompt(workingDirectory);
                    ChatResponse reply = await WithInferenceRetryAsync(() => settings.Streaming ? StreamChatAsync(client, prompt, completionOptions, firstResponseMs, idleMs, token) : client.ChatAsync(prompt, completionOptions, token), r => r.Success, r => r.StatusCode, r => r.Error, processId, lastCallMs, token, () => LastRetryAfterSeconds(client)).ConfigureAwait(false);
                    EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Inference, "[inference] " + JsonSerializer.Serialize(new { iteration = 1, streamed = settings.Streaming, reply.Success, reply.FinishReason, reply.Usage, textLength = reply.Text?.Length ?? 0, toolCalls = 0 }));
                    if (!reply.Success)
                    {
                        exitCode = 1;
                        EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Error, reply.Error ?? "Inference request failed.");
                        RaiseProviderError(processId, InferenceFailure(reply.StatusCode, reply.Error, "Inference request failed: " + (reply.Error ?? "the endpoint returned failure without detail"), lastCallMs.Value, settings.Streaming, LastRetryAfterSeconds(client)));
                        WriteFinalMessage(finalMessageFilePath, String.Empty);
                        return;
                    }
                    finalText = RemoveCompleteThinkBlocks(reply.Text);
                    if (!IsCompleteResponse(finalText, reply.FinishReason, requiresCompletionLine))
                    {
                        exitCode = 1;
                        RaiseProviderError(processId, new RuntimeProviderError { ErrorType = IsTokenLimit(reply.FinishReason) ? "output_token_limit_reached" : "incomplete_completion", Reason = IsTokenLimit(reply.FinishReason) ? "The inference response hit its output-token limit (finish reason: " + reply.FinishReason + "). The mission did not finish." : "Plain inference did not produce a valid mission completion." });
                    }
                    if (finalText.Length > 0) Emit(processId, finalText);
                    WriteFinalMessage(finalMessageFilePath, finalText);
                }
                if (!settings.ToolsEnabled || settings.AllowedTools?.Count == 0)
                {
                    await RunPlainChatAsync().ConfigureAwait(false);
                    return;
                }

                TaskPlan taskPlan = new TaskPlan();
                BuiltInToolRegistry registry = new BuiltInToolRegistry(taskPlan, ToolLimits.ForContextWindow(settings.ContextLength, settings.ToolOutputWindowPercent));
                List<PolyToolDefinition> tools = BuildToolDefinitions(registry)
                    .Where(t => (!settings.ReadOnlyTools || registry.GetMutationKind(t.Name) == ToolMutationKind.ReadOnly)
                        && (settings.AllowedTools == null || settings.AllowedTools.Contains(t.Name, StringComparer.Ordinal))).ToList();

                // Merge in the tools advertised by the optional MCP endpoint. Built-in tool names always win
                // on a collision so a remote server can never shadow the local file/process tools. The
                // routing map records which tool names must be dispatched to the MCP client rather than the
                // local registry.
                Dictionary<string, McpToolClient> mcpRouting = new Dictionary<string, McpToolClient>(StringComparer.Ordinal);
                DeferredMcpToolCatalog? deferredCatalog = null;
                if (settings.McpToolsEnabled && !settings.ReadOnlyTools && !String.IsNullOrWhiteSpace(mcpUrl))
                {
                    HashSet<string> builtInNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (PolyToolDefinition builtIn in BuildToolDefinitions(registry)) builtInNames.Add(builtIn.Name);

                    try
                    {
                        mcpClient = _McpClientFactory(mcpUrl!, mcpToken, _Logging);
                        await mcpClient.InitializeAsync(token).ConfigureAwait(false);
                        List<McpRemoteTool> remoteTools = await mcpClient.ListToolsAsync(token).ConfigureAwait(false);

                        List<PolyToolDefinition> remoteDefinitions = new List<PolyToolDefinition>();
                        foreach (McpRemoteTool remote in remoteTools)
                        {
                            if (settings.AllowedTools != null && !settings.AllowedTools.Contains(remote.Name, StringComparer.Ordinal)) continue;
                            if (builtInNames.Contains(remote.Name) || mcpRouting.ContainsKey(remote.Name)) continue;
                            Dictionary<string, object> parameters = SchemaJsonToDictionary(remote.InputSchemaJson);
                            remoteDefinitions.Add(PolyToolDefinition.Function(remote.Name, remote.Description, parameters));
                            mcpRouting[remote.Name] = mcpClient;
                        }

                        // Explicit allowlists and small catalogs keep eager exposure. Large default catalogs
                        // remain callable under the same policy, with schemas discovered only when needed.
                        // A mission persona's declared scope is shown up front and the rest is discovered.
                        // A real remote tool with the discovery name keeps its original meaning.
                        bool discoveryNameFree = !mcpRouting.ContainsKey(DeferredMcpToolCatalog.DiscoveryName)
                            && !builtInNames.Contains(DeferredMcpToolCatalog.DiscoveryName);
                        List<PolyToolDefinition> scoped = toolScope == null ? new List<PolyToolDefinition>()
                            : remoteDefinitions.Where(t => toolScope.Contains(t.Name)).ToList();
                        List<PolyToolDefinition> unscoped = toolScope == null ? new List<PolyToolDefinition>()
                            : remoteDefinitions.Where(t => !toolScope.Contains(t.Name)).ToList();
                        if (settings.AllowedTools == null && toolScope != null && unscoped.Count > 0 && discoveryNameFree)
                        {
                            tools.AddRange(scoped);
                            deferredCatalog = new DeferredMcpToolCatalog(unscoped, tools);
                            tools.Add(DeferredMcpToolCatalog.Definition());
                        }
                        else if (settings.AllowedTools == null && remoteDefinitions.Count > DeferredMcpToolCatalog.EagerCatalogLimit && discoveryNameFree)
                        {
                            deferredCatalog = new DeferredMcpToolCatalog(remoteDefinitions, tools);
                            tools.Add(DeferredMcpToolCatalog.Definition());
                        }
                        else tools.AddRange(remoteDefinitions);

                        EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.McpStatus, "[mcp] connected to " + mcpUrl + "; " + remoteDefinitions.Count + " tool(s) available"
                            + (deferredCatalog == null ? String.Empty : "; schemas deferred, use " + DeferredMcpToolCatalog.DiscoveryName));
                    }
                    catch (Exception ex)
                    {
                        EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.McpStatus, "[mcp] tool access unavailable (" + ex.Message + "); continuing with built-in tools only");
                        try { mcpClient?.Dispose(); } catch { }
                        mcpClient = null;
                        mcpRouting.Clear();
                    }
                }

                if (tools.Count == 0)
                {
                    await RunPlainChatAsync().ConfigureAwait(false);
                    return;
                }

                // The native submit tool replaces an MCP tool of the same name; a valid call is forwarded to that MCP tool.
                McpToolClient? submitForward = null;
                if (submitTool != null)
                {
                    if (mcpRouting.TryGetValue(submitTool.Name, out McpToolClient? routed)) submitForward = routed;
                    mcpRouting.Remove(submitTool.Name);
                    tools.RemoveAll(t => String.Equals(t.Name, submitTool.Name, StringComparison.Ordinal));
                    tools.Add(submitTool.Definition());
                }
                int forcedSubmits = 0;
                bool forceSubmitNext = false;

                EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Inference, "[tools] " + tools.Count + " tool schema(s) sent, about "
                    + ApiContextCompactor.EstimateToolTokens(tools) + " input tokens per turn" + (toolScope == null ? String.Empty : "; persona scope applied"));

                List<ChatMessage> messages = new List<ChatMessage>();
                messages.Add(ChatMessage.System(BuildSystemPrompt(workingDirectory)
                    + (deferredCatalog == null ? String.Empty : " Additional Armada MCP tools are available through "
                        + DeferredMcpToolCatalog.DiscoveryName + ". Search by tool name or task keywords to load the required schemas before calling them.")));
                messages.Add(ChatMessage.User(prompt));

                HashSet<string> permittedTools = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
                // Deferral is a context optimization, not an authorization change. A permitted tool named
                // directly in mission instructions can still be called without a discovery round trip.
                permittedTools.UnionWith(mcpRouting.Keys);
                int maxIterations = Math.Min(_MaxIterations, settings.MaxIterations);
                int incompleteResponses = 0;
                RuntimeProviderError? tokenLimitFailure = null;
                bool requiresCompletion = requiresCompletionLine;
                long lastPromptTokens = 0;
                for (int iteration = 0; iteration < maxIterations; iteration++)
                {
                    token.ThrowIfCancellationRequested();

                    // Keep the conversation inside the window before it is sent: the provider's count can lag or undercount
                    // (Ollama does not count cached prompt tokens), so the local estimate also decides.
                    // Tool schemas are resent on every turn, so they count toward the window too (discovery can add more).
                    long pressure = Math.Max(lastPromptTokens, ApiContextCompactor.EstimateTokens(messages) + ApiContextCompactor.EstimateToolTokens(tools));
                    if (ApiContextCompactor.ShouldCompact(pressure, settings.ContextLength))
                    {
                        long removed = ApiContextCompactor.Compact(messages);
                        if (removed > 0)
                        {
                            lastPromptTokens = 0;
                            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Inference, "[context] about " + pressure + " of " + settings.ContextLength
                                + " tokens in use; replaced earlier tool output (about " + (removed / 4) + " tokens) with short notes to stay inside the window");
                        }
                    }

                    ToolChatRequest request = new ToolChatRequest();
                    request.Messages = messages;
                    request.Tools = tools;
                    request.Options = completionOptions;
                    if (forceSubmitNext && submitTool != null)
                    {
                        // RS-6.3: only the submit tool is offered and a tool call is required.
                        request.Tools = tools.Where(t => String.Equals(t.Name, submitTool.Name, StringComparison.Ordinal)).ToList();
                        request.ToolChoice = "required";
                        forceSubmitNext = false;
                    }

                    ToolChatResponse response = await WithInferenceRetryAsync(() => settings.Streaming ? StreamToolChatAsync(client, request, firstResponseMs, idleMs, token) : client.ToolChatAsync(request, token), r => r.Success, r => r.StatusCode, r => r.Error, processId, lastCallMs, token, () => LastRetryAfterSeconds(client)).ConfigureAwait(false);

                    EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Inference, "[inference] " + JsonSerializer.Serialize(new { iteration = iteration + 1, maxIterations, streamed = settings.Streaming, response.Success, response.ResponseId, response.FinishReason, response.Usage, reasoningLength = response.Reasoning?.Length ?? 0, textLength = response.Text?.Length ?? 0, toolCalls = response.ToolCalls?.Count ?? 0 }));

                    if (!response.Success)
                    {
                        string reason = String.IsNullOrEmpty(response.Error) ? "the inference endpoint reported failure without detail" : response.Error!;
                        EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Error, "[error] inference call failed: " + reason);
                        RaiseProviderError(processId, InferenceFailure(response.StatusCode, response.Error, "Inference request failed: " + reason, lastCallMs.Value, settings.Streaming, LastRetryAfterSeconds(client)));
                        exitCode = 1;
                        break;
                    }

                    // A prompt that fills the configured window means the server has started (or is about to start) dropping
                    // the oldest turns, which hold the mission brief. Stop with a typed error rather than act on a partial view.
                    RuntimeProviderError? contextFailure = ContextLimitFailure(settings, response.Usage);
                    if (response.Usage?.PromptTokens != null) lastPromptTokens = response.Usage.PromptTokens.Value;
                    // Still below the window, nothing has been dropped yet: make room and carry on rather than stop.
                    if (contextFailure != null && response.Usage!.PromptTokens!.Value < settings.ContextLength!.Value)
                    {
                        long removed = ApiContextCompactor.Compact(messages);
                        if (removed > 0)
                        {
                            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Inference, "[context] prompt reached " + response.Usage.PromptTokens + " of " + settings.ContextLength
                                + " tokens; replaced earlier tool output (about " + (removed / 4) + " tokens) with short notes and continued");
                            lastPromptTokens = 0;
                            contextFailure = null;
                        }
                    }
                    if (contextFailure != null)
                    {
                        EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Error, "[error] " + contextFailure.Reason);
                        RaiseProviderError(processId, contextFailure);
                        exitCode = 1;
                        break;
                    }

                    string visibleText = RemoveCompleteThinkBlocks(response.Text);
                    if (!String.IsNullOrEmpty(visibleText))
                    {
                        finalText = visibleText;
                        Emit(processId, visibleText);
                    }

                    messages.Add(response.ToAssistantMessage());

                    if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                    {
                        if (IsTokenLimit(response.FinishReason)) tokenLimitFailure = new RuntimeProviderError
                        {
                            ErrorType = "output_token_limit_reached",
                            Reason = "The inference response hit the output-token limit (finish reason: " + response.FinishReason
                                + "; configured Max Output Tokens: " + (settings.MaxTokens?.ToString() ?? "provider default")
                                + "; completion tokens: " + response.Usage?.CompletionTokens + "). The mission did not finish."
                        };
                        // Only this terminal response can finish the run; earlier narration is not a result.
                        finalText = RemoveCompleteThinkBlocks(response.Text);

                        // RS-6.3: a stage with a submit tool that stopped without submitting, and whose reply does not meet the
                        // text contract either, gets a forced submit turn (at most two) instead of a failed stage.
                        if (submitTool != null && !submitTool.Submitted && forcedSubmits < 2 && iteration + 1 < maxIterations
                            && !submitTool.TextMeetsContract(finalText) && !IsTokenLimit(response.FinishReason))
                        {
                            forcedSubmits++;
                            forceSubmitNext = true;
                            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Warning, "[warning] the captain stopped without calling " + submitTool.Name + "; requiring it (" + forcedSubmits + "/2).");
                            messages.Add(ChatMessage.User(submitTool.ForcePrompt()));
                            continue;
                        }

                        bool complete = (submitTool != null && submitTool.Submitted && !String.IsNullOrWhiteSpace(finalText))
                            || IsCompleteResponse(finalText, response.FinishReason, requiresCompletion);
                        if (complete) break;
                        incompleteResponses++;
                        if (incompleteResponses <= 2 && iteration + 1 < maxIterations)
                        {
                            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Warning, "[warning] incomplete terminal response; asking the captain to continue (" + incompleteResponses + "/2).");
                            messages.Add(ChatMessage.User("Your last response did not complete the task. Continue implementing and validating the mission using tools. Do not repeat an introductory plan. When finished, provide a substantive summary" + (requiresCompletion ? " and the required standalone [ARMADA:RESULT] COMPLETE line." : ".")));
                            continue;
                        }
                        exitCode = 1;
                        RaiseProviderError(processId, tokenLimitFailure ?? new RuntimeProviderError { ErrorType = "incomplete_completion",
                            Reason = "The endpoint stopped without a nonempty final response and the required mission completion marker after bounded continuation attempts." });
                        EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Error, "[error] incomplete completion; the mission is not finished.");
                        break;
                    }

                    finalText = String.Empty;
                    foreach (ToolCall call in response.ToolCalls)
                    {
                        token.ThrowIfCancellationRequested();
                        string resultContent;
                        if (!permittedTools.Contains(call.Name))
                        {
                            resultContent = JsonSerializer.Serialize(new { error = "tool_not_allowed", message = "This tool is disabled by the endpoint's inference policy." });
                            EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = false, PermissionDenied = true, Result = resultContent });
                        }
                        else if (submitTool != null && String.Equals(call.Name, submitTool.Name, StringComparison.Ordinal))
                            resultContent = await ExecuteSubmitToolAsync(processId, submitTool, submitForward, call, token).ConfigureAwait(false);
                        else resultContent = await ExecuteToolAsync(processId, registry, mcpRouting, call, workingDirectory, deferredCatalog, token).ConfigureAwait(false);
                        messages.Add(ChatMessage.ToolResult(call.Id, call.Name, resultContent));
                    }

                    if (iteration == maxIterations - 1)
                    {
                        exitCode = 1;
                        RaiseProviderError(processId, new RuntimeProviderError { ErrorType = "max_iterations_reached", Reason = "API endpoint captain stopped at the configured " + maxIterations + " model-turn limit." });
                        EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Warning, "[warning] reached the maximum of " + maxIterations + " model turns; stopping.");
                    }
                }

                WriteFinalMessage(finalMessageFilePath, finalText);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // -1 is the interruption sentinel: only a real stop (StopAsync or the caller's token) produces it. An
                // OperationCanceledException without that, such as an HTTP client timeout against the inference
                // endpoint, is a failed run and falls through to the generic handler below (exit code 1).
                exitCode = -1;
                EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Cancelled, "[cancelled] the captain run was stopped.");
            }
            catch (System.Net.Http.HttpRequestException e)
            {
                exitCode = 1;
                _Logging.Warn(_Header + "loop error for process " + processId + ": " + e.ToString());
                Emit(processId, "[error] " + e.Message);
                RaiseProviderError(processId, e.StatusCode.HasValue ? RuntimeProviderErrorParser.FromHttpStatus((int)e.StatusCode.Value, e.Message)
                    : new RuntimeProviderError { ErrorType = "llm_connection_error", Reason = "Unable to reach the inference endpoint: " + e.Message });
            }
            catch (Exception e)
            {
                exitCode = 1;
                _Logging.Warn(_Header + "loop error for process " + processId + ": " + e.ToString());
                RaiseProviderError(processId, new RuntimeProviderError { ErrorType = e is OperationCanceledException || e is TimeoutException ? "llm_timeout" : "runtime_error",
                    Reason = e is TimeoutException ? e.Message
                        : e is OperationCanceledException ? "Inference request exceeded the endpoint request timeout of " + endpoint.TimeoutMs + " ms."
                        : "API captain runtime failed: " + e.Message });
                EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.Error, "[error] " + e.Message);
            }
            finally
            {
                try { mcpClient?.Dispose(); } catch { }
                CloseLog();
                _Running.TryRemove(processId, out CancellationTokenSource? _);
                try { cts.Dispose(); } catch { }
                OnProcessExited?.Invoke(processId, exitCode);
            }
        }

        private async Task<string> ExecuteSubmitToolAsync(int processId, NativeSubmitTool submitTool, McpToolClient? forward, ToolCall call, CancellationToken token)
        {
            string argsJson = String.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;
            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolCall, "[tool] " + call.Name + " " + Truncate(argsJson, 500));
            EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Started, Id = call.Id, Name = call.Name, Arguments = Truncate(argsJson, 4000) });
            long startTicks = Environment.TickCount64;
            bool before = submitTool.Submitted;
            int rejectedBefore = submitTool.RejectedCalls;
            string result = await submitTool.HandleAsync(argsJson, forward, text => Emit(processId, text), token).ConfigureAwait(false);
            bool ok = submitTool.RejectedCalls == rejectedBefore && (submitTool.Submitted || before);
            long elapsed = Environment.TickCount64 - startTicks;
            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolResult, "[tool:result] " + call.Name + " " + (ok ? "ok" : "failed") + " " + Truncate(result, 500));
            EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = ok, ElapsedMs = elapsed, Result = Truncate(result, 16000) });
            return result;
        }

        private async Task<string> ExecuteToolAsync(
            int processId,
            BuiltInToolRegistry registry,
            Dictionary<string, McpToolClient> mcpRouting,
            ToolCall call,
            string workingDirectory,
            DeferredMcpToolCatalog? deferredCatalog,
            CancellationToken token)
        {
            string argsJson = String.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;
            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolCall, "[tool] " + call.Name + " " + Truncate(argsJson, 500));
            EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Started, Id = call.Id, Name = call.Name, Arguments = Truncate(argsJson, 4000) });

            long startTicks = Environment.TickCount64;

            // Route to the MCP endpoint when the tool belongs to it; otherwise run the built-in local tool.
            if (mcpRouting.TryGetValue(call.Name, out McpToolClient? mcpClient) && mcpClient != null)
            {
                try
                {
                    // Success comes from the protocol's isError flag, never from the result text.
                    McpToolCallResult mcpResult = await mcpClient.CallToolResultAsync(call.Name, argsJson, token).ConfigureAwait(false);
                    string mcpText = McpToolClient.RenderResultText(mcpResult);
                    bool ok = !mcpResult.IsError;
                    long elapsed = Environment.TickCount64 - startTicks;
                    EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolResult, "[tool:result] " + call.Name + " " + (ok ? "ok" : "failed") + " " + Truncate(mcpText, 500));
                    EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = ok, ElapsedMs = elapsed, Result = Truncate(mcpText, 16000) });
                    return mcpText;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    long elapsed = Environment.TickCount64 - startTicks;
                    string message = "MCP tool call failed: " + ex.Message;
                    EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolResult, "[tool:result] " + call.Name + " failed " + message);
                    EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = false, ElapsedMs = elapsed, Result = message });
                    return JsonSerializer.Serialize(new { error = "mcp_tool_failed", message });
                }
            }

            // CLI tool permissions for the built-in shell tool (the equivalent of a CLI captain's Bash tool).
            if (String.Equals(call.Name, ShellToolName, StringComparison.Ordinal) && ShellPolicy.HasValue && ShellPolicy.Value != Armada.Core.Enums.CliPermissionPolicyEnum.Bypass)
            {
                string? refusal = null;
                if (ShellPolicy.Value == Armada.Core.Enums.CliPermissionPolicyEnum.Refuse || PermissionPrompt == null)
                {
                    refusal = "This command requires approval: the CLI tool permission policy for this session is Refuse.";
                }
                else
                {
                    Armada.Core.Models.CliPermissionPromptOutcome outcome = await PermissionPrompt(call.Name, argsJson, token).ConfigureAwait(false);
                    if (!outcome.Allowed) refusal = String.IsNullOrWhiteSpace(outcome.Message) ? "Denied in Armada." : outcome.Message;
                }

                if (refusal != null)
                {
                    long refusedElapsed = Environment.TickCount64 - startTicks;
                    EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolResult, "[tool:result] " + call.Name + " refused " + refusal);
                    EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = false, ElapsedMs = refusedElapsed, Result = refusal, PermissionDenied = true });
                    return JsonSerializer.Serialize(new { error = "permission_denied", message = refusal });
                }
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(argsJson);
                ToolResult result = deferredCatalog != null && call.Name == DeferredMcpToolCatalog.DiscoveryName
                    ? deferredCatalog.Discover(call.Id ?? String.Empty, document.RootElement)
                    : await registry.ExecuteAsync(call.Id ?? String.Empty, call.Name, document.RootElement, workingDirectory, token).ConfigureAwait(false);
                long elapsed = Environment.TickCount64 - startTicks;
                EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolResult, "[tool:result] " + call.Name + " " + (result.Success ? "ok" : "failed") + " " + Truncate(result.Content, 500));
                EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = result.Success, ElapsedMs = elapsed, Result = Truncate(result.Content, 16000) });
                return result.Content ?? String.Empty;
            }
            catch (JsonException)
            {
                string message = "Tool arguments were not valid JSON: " + Truncate(argsJson, 200);
                EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolResult, "[tool:result] " + call.Name + " failed " + message);
                EmitToolEvent(processId, new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = false, Result = message });
                return JsonSerializer.Serialize(new { error = "invalid_arguments", message });
            }
        }

        private List<PolyToolDefinition> BuildToolDefinitions(BuiltInToolRegistry registry)
        {
            List<PolyToolDefinition> definitions = new List<PolyToolDefinition>();
            foreach (ArmadaToolDefinition tool in registry.GetToolDefinitions())
            {
                Dictionary<string, object> parameters = SchemaToDictionary(tool.ParametersSchema);
                definitions.Add(PolyToolDefinition.Function(tool.Name, tool.Description, parameters));
            }

            return definitions;
        }

        private static Dictionary<string, object> SchemaToDictionary(object schema)
        {
            try
            {
                string json = JsonSerializer.Serialize(schema);
                Dictionary<string, object>? parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                return parsed ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static Dictionary<string, object> SchemaJsonToDictionary(string? schemaJson)
        {
            if (String.IsNullOrWhiteSpace(schemaJson))
                return new Dictionary<string, object> { ["type"] = "object" };

            try
            {
                Dictionary<string, object>? parsed = JsonSerializer.Deserialize<Dictionary<string, object>>(schemaJson);
                return parsed ?? new Dictionary<string, object> { ["type"] = "object" };
            }
            catch
            {
                return new Dictionary<string, object> { ["type"] = "object" };
            }
        }

        private string BuildSystemPrompt(string workingDirectory)
        {
            return
                "You are an autonomous coding agent operating as an Armada captain. You are working inside a git " +
                "worktree at " + workingDirectory + ". Use the provided tools to inspect and modify files and to run " +
                "commands in order to complete the mission described by the user. Make focused changes, verify your " +
                "work, and when the mission is complete stop calling tools and reply with a concise summary of what " +
                "you changed. Work efficiently: inspect only relevant files, make the smallest complete change, then validate. Stay within the assigned scope and implement incrementally instead of repeatedly rediscovering the same interfaces. If the mission instructions define [ARMADA:...] signals, emit them as plain text lines.";
        }

        private void RaiseProviderError(int processId, RuntimeProviderError? error)
        {
            if (error == null) return;
            try { OnProviderError?.Invoke(processId, error); }
            catch (Exception ex) { _Logging.Warn(_Header + "error in OnProviderError handler for process " + processId + ": " + ex.Message); }
        }

        private void Emit(int processId, string line)
        {
            if (String.IsNullOrEmpty(line)) return;
            WriteLog(line);
            try { OnOutputReceived?.Invoke(processId, line); } catch { }
            try { OnStdoutReceived?.Invoke(processId, line); } catch { }
        }

        /// <summary>
        /// Emit a diagnostic: written to the log and the general output channel (mission logs, heartbeats) and raised on
        /// the typed <see cref="OnDiagnostic"/> channel, but never on the stdout (reply text) channel.
        /// </summary>
        private void EmitDiagnostic(int processId, ApiRuntimeDiagnosticKindEnum kind, string message)
        {
            if (String.IsNullOrEmpty(message)) return;
            WriteLog(message);
            try { OnOutputReceived?.Invoke(processId, message); } catch { }
            try { OnDiagnostic?.Invoke(processId, new ApiRuntimeDiagnostic { Kind = kind, Message = message }); } catch { }
        }

        /// <summary>
        /// Raise a tool-activity event on the typed <see cref="OnToolEvent"/> channel.
        /// </summary>
        private void EmitToolEvent(int processId, ApiRuntimeToolEvent toolEvent)
        {
            // General output is forwarded over Harbor, preserving lifecycle evidence without
            // putting diagnostics in the captain's final reply or depending on Admiral-local process IDs.
            EmitDiagnostic(processId, ApiRuntimeDiagnosticKindEnum.ToolCall, "[tool-lifecycle] " +
                JsonSerializer.Serialize(new { id = toolEvent.Id, started = toolEvent.Phase == ApiRuntimeToolPhaseEnum.Started }));
            try { OnToolEvent?.Invoke(processId, toolEvent); } catch { }
            if (toolEvent.Phase == ApiRuntimeToolPhaseEnum.Started && !String.IsNullOrWhiteSpace(toolEvent.Name))
            {
                RuntimeActivity activity = new RuntimeActivity
                {
                    Kind = Armada.Core.Enums.RuntimeActivityKindEnum.ToolCall,
                    ToolName = toolEvent.Name,
                    Detail = Armada.Core.Services.RuntimeActivityParser.FirstLine(toolEvent.Arguments, Armada.Core.Services.RuntimeActivityParser.MaxDetailChars),
                    Summary = "Calling tool " + toolEvent.Name,
                    TimestampUtc = DateTime.UtcNow
                };
                try { OnActivity?.Invoke(processId, activity); } catch { }
            }
        }

        private static string Truncate(string? value, int max)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            string single = value!.Replace("\r", " ").Replace("\n", " ");
            return single.Length <= max ? single : single.Substring(0, max) + "...";
        }

        private void OpenLog(string? logFilePath, string prompt, ModelEndpoint endpoint)
        {
            if (String.IsNullOrEmpty(logFilePath)) return;
            try
            {
                lock (_LogLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
                    _LogWriter = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
                    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    _LogWriter.WriteLine("[" + timestamp + "] API-endpoint captain starting: " + endpoint.Name + " (" + endpoint.Provider + "/" + (endpoint.Model ?? "default") + ")");
                    if (!String.IsNullOrEmpty(prompt)) _LogWriter.WriteLine(prompt);
                    _LogWriter.WriteLine(String.Empty);
                }
            }
            catch
            {
                _LogWriter = null;
            }
        }

        private void WriteLog(string data)
        {
            lock (_LogLock)
            {
                try { _LogWriter?.WriteLine(data); }
                catch (ObjectDisposedException) { }
                catch { }
            }
        }

        private void CloseLog()
        {
            lock (_LogLock)
            {
                try { _LogWriter?.Flush(); } catch { }
                try { _LogWriter?.Dispose(); } catch { }
                _LogWriter = null;
            }
        }

        private static RuntimeProviderError? ContextLimitFailure(InferenceOptions settings, TokenUsage? usage)
        {
            if (!settings.ContextLength.HasValue || usage?.PromptTokens == null) return null;
            int window = settings.ContextLength.Value;
            long prompt = usage.PromptTokens.Value;
            if (prompt * 100 < (long)window * 95) return null;
            return new RuntimeProviderError
            {
                ErrorType = "context_limit_exceeded",
                Reason = "The conversation reached the configured context window (prompt tokens: " + prompt + " of " + window
                    + "). Further turns would run on truncated history, so the run stopped."
            };
        }

        private static bool IsTokenLimit(string? reason) => String.Equals(reason, "length", StringComparison.OrdinalIgnoreCase) || String.Equals(reason, "max_tokens", StringComparison.OrdinalIgnoreCase) || String.Equals(reason, "max_output_tokens", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Remove every complete think block from assistant text before that text is emitted or stored.
        /// A block opens with a think tag whose name ends at the tag boundary and closes with the matching end tag.
        /// A longer name, an unmatched tag, and text with no complete block stay as they were.
        /// </summary>
        /// <param name="text">Assistant text from the inference client.</param>
        /// <returns>The text with complete think blocks removed.</returns>
        private static string RemoveCompleteThinkBlocks(string? text)
        {
            if (String.IsNullOrEmpty(text)) return String.Empty;
            return _CompleteThinkBlock.Replace(text, String.Empty);
        }

        private static bool IsCompleteResponse(string text, string? finishReason, bool requiresCompletion)
        {
            if (String.IsNullOrWhiteSpace(text) || IsTokenLimit(finishReason)) return false;
            if (!requiresCompletion) return true;
            string[] lines = text.Replace("\r", "").Split('\n');
            // The same decoration-tolerant matcher Core uses. A verifier that found a blocking product defect ends with the
            // blocked outcome instead; that is also a finished reply.
            bool IsEnding(string line) => CompletionLine.IsCompletionLine(line)
                || String.Equals(ProtocolLines.StripDecoration(line), VerifierContract.BlockedMarker, StringComparison.Ordinal);
            return lines.Any(IsEnding) && lines.Any(line => !String.IsNullOrWhiteSpace(line) && !IsEnding(line));
        }

        private void WriteFinalMessage(string? finalMessageFilePath, string finalText)
        {
            if (String.IsNullOrEmpty(finalMessageFilePath)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(finalMessageFilePath)!);
                File.WriteAllText(finalMessageFilePath, finalText);
            }
            catch (Exception e)
            {
                _Logging.Debug(_Header + "could not write final message file: " + e.Message);
            }
        }

        #endregion
    }
}
