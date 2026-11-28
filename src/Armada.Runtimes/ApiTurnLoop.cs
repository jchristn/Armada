namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using PolyPrompt.Models;

    /// <summary>
    /// The tool-calling turn loop of an API captain run. Each turn it lets the <see cref="ApiContextPolicy"/> keep the
    /// conversation inside the window, sends the request through the host, stops on a failed response or a full window,
    /// emits visible text, and either runs the requested tool calls or hands a response without tool calls to the
    /// <see cref="ApiTerminalPolicy"/>. A turn limit ends the run with max_iterations_reached. Not thread-safe: one
    /// instance serves one run.
    /// </summary>
    public sealed class ApiTurnLoop
    {
        #region Public-Members

        /// <summary>Default number of identical consecutive tool calls that draws a corrective note (RS-7.5).</summary>
        public const int DefaultRepeatedToolCallLimit = 3;

        /// <summary>Most available tool names listed in an unknown_tool result.</summary>
        public const int MaxListedTools = 40;

        /// <summary>
        /// Identical consecutive tool calls (same name and arguments) after which each further repeat gets a corrective
        /// note in its result; twice this many ends the run as degenerate_output (RS-7.5). Minimum 2; default 3.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when set below 2.</exception>
        public int RepeatedToolCallLimit
        {
            get => _RepeatedToolCallLimit;
            set
            {
                if (value < 2) throw new ArgumentOutOfRangeException(nameof(RepeatedToolCallLimit));
                _RepeatedToolCallLimit = value;
            }
        }

        /// <summary>
        /// Tool names that exist but the endpoint's inference policy disables; a call to one gets tool_not_allowed rather
        /// than unknown_tool (RS-7.4). Null treats every name outside the permitted set as unknown.
        /// </summary>
        public ISet<string>? DisabledTools { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly ApiTurnLoopCallbacks _Callbacks;
        private readonly ApiContextPolicy _Context;
        private readonly ApiTerminalPolicy _Terminal;
        private readonly NativeSubmitTool? _SubmitTool;
        private readonly CompletionOptions _Options;
        private readonly int _MaxIterations;
        private readonly bool _Streamed;
        private readonly ApiRuntimeBehaviors _Behaviors;
        private int _RepeatedToolCallLimit = DefaultRepeatedToolCallLimit;
        private string? _LastCallKey = null;
        private int _RepeatCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the loop for one run.
        /// </summary>
        /// <param name="callbacks">Host callbacks.</param>
        /// <param name="context">Context-window policy.</param>
        /// <param name="terminal">Terminal-response policy.</param>
        /// <param name="submitTool">The stage's native submit tool, or null.</param>
        /// <param name="options">Completion options sent with every turn.</param>
        /// <param name="maxIterations">Model-turn limit; 0 sends no turn.</param>
        /// <param name="streamed">Whether turns are streamed (reported in diagnostics).</param>
        /// <param name="behaviors">Open-model behavior switches; null uses the defaults (all on).</param>
        /// <exception cref="ArgumentNullException">Thrown when callbacks, context, terminal or options is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxIterations is negative.</exception>
        public ApiTurnLoop(
            ApiTurnLoopCallbacks callbacks,
            ApiContextPolicy context,
            ApiTerminalPolicy terminal,
            NativeSubmitTool? submitTool,
            CompletionOptions options,
            int maxIterations,
            bool streamed,
            ApiRuntimeBehaviors? behaviors = null)
        {
            _Callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            if (maxIterations < 0) throw new ArgumentOutOfRangeException(nameof(maxIterations));
            _SubmitTool = submitTool;
            _MaxIterations = maxIterations;
            _Streamed = streamed;
            _Behaviors = behaviors ?? new ApiRuntimeBehaviors();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run turns until the run completes, fails, or reaches the turn limit.
        /// </summary>
        /// <param name="messages">The conversation (system prompt and mission brief first); appended to and compacted in place.</param>
        /// <param name="tools">Tool schemas offered each turn.</param>
        /// <param name="permittedTools">Tool names the captain may call; others get a denial result.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The exit code and final text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messages, tools or permittedTools is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is cancelled.</exception>
        public async Task<ApiTurnLoopResult> RunAsync(List<ChatMessage> messages, List<ToolDefinition> tools, ISet<string> permittedTools, CancellationToken token = default)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            if (permittedTools == null) throw new ArgumentNullException(nameof(permittedTools));

            ApiTurnLoopResult result = new ApiTurnLoopResult();
            bool forceSubmitNext = false;
            int corrections = 0;
            CompletionOptions? correctedOptions = null;
            for (int iteration = 0; iteration < _MaxIterations; iteration++)
            {
                token.ThrowIfCancellationRequested();

                string? compacted = _Context.BeforeTurn(messages, tools);
                if (compacted != null) _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Inference, compacted);

                ChatRequest request = new ChatRequest();
                request.Messages = messages;
                request.Tools = tools;
                request.Options = correctedOptions ?? _Options;
                correctedOptions = null;
                if (forceSubmitNext && _SubmitTool != null)
                {
                    // RS-6.3: only the submit tool is offered and a tool call is required.
                    request.Tools = tools.Where(t => String.Equals(t.Name, _SubmitTool.Name, StringComparison.Ordinal)).ToList();
                    request.ToolChoice = "required";
                    forceSubmitNext = false;
                }

                ChatResponse response = await _Callbacks.InferAsync(request, token).ConfigureAwait(false);
                result.Turns++;

                _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Inference, "[inference] " + JsonSerializer.Serialize(new { iteration = iteration + 1, maxIterations = _MaxIterations, streamed = _Streamed, response.Success, response.ResponseId, response.FinishReason, response.Usage, reasoningLength = response.Reasoning?.Length ?? 0, textLength = response.Text?.Length ?? 0, toolCalls = response.ToolCalls?.Count ?? 0 }));

                if (!response.Success && corrections < _Behaviors.MalformedCallRetries && iteration < _MaxIterations - 1 && MalformedCallCorrection.Applies(response))
                {
                    // RS-7.3: a malformed tool call or a repeat loop is not resent unchanged. The next request carries a
                    // corrective turn and a varied temperature; the provider error ends the run only after the retries.
                    corrections++;
                    correctedOptions = MalformedCallCorrection.Vary(_Options, corrections);
                    _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Warning, "[retry] malformed or degenerate generation ("
                        + (response.StatusCode.HasValue ? "HTTP " + response.StatusCode + " " : String.Empty) + (response.Error ?? "no detail")
                        + "); corrective turn " + corrections + " of " + _Behaviors.MalformedCallRetries + " at temperature "
                        + correctedOptions.Temperature?.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    messages.Add(ChatMessage.User(MalformedCallCorrection.Message(response.Error, request.Tools ?? tools)));
                    continue;
                }

                if (!response.Success)
                {
                    string reason = String.IsNullOrEmpty(response.Error) ? "the inference endpoint reported failure without detail" : response.Error!;
                    _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Error, "[error] inference call failed: " + reason);
                    _Callbacks.ProviderError(_Callbacks.InferenceFailure(response, "Inference request failed: " + reason));
                    result.ExitCode = 1;
                    break;
                }

                corrections = 0;
                ApiContextCheck window = _Context.AfterResponse(messages, response.Usage);
                if (window.Note != null) _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Inference, window.Note);
                if (window.Failure != null)
                {
                    _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Error, "[error] " + window.Failure.Reason);
                    _Callbacks.ProviderError(window.Failure);
                    result.ExitCode = 1;
                    break;
                }

                RecoverTextCalls(response, permittedTools, result);

                string visibleText = _Terminal.VisibleText(response.Text);
                if (!String.IsNullOrEmpty(visibleText))
                {
                    result.FinalText = visibleText;
                    _Callbacks.Emit(visibleText);
                }

                messages.Add(HistoryMessage(response));

                if (response.ToolCalls == null || response.ToolCalls.Count == 0)
                {
                    ApiTerminalDecision decision = _Terminal.Decide(response, iteration, _MaxIterations);
                    result.FinalText = decision.FinalText;
                    if (decision.Action == ApiTerminalActionEnum.Complete) break;
                    if (decision.Action == ApiTerminalActionEnum.Fail)
                    {
                        result.ExitCode = 1;
                        _Callbacks.ProviderError(decision.Failure!);
                        if (decision.ErrorDiagnostic != null) _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Error, decision.ErrorDiagnostic);
                        break;
                    }

                    if (decision.Action == ApiTerminalActionEnum.ForceSubmit) forceSubmitNext = true;
                    if (decision.Warning != null) _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Warning, decision.Warning);
                    messages.Add(ChatMessage.User(decision.FollowUp ?? String.Empty));
                    continue;
                }

                result.FinalText = String.Empty;
                RuntimeProviderError? repeated = await ExecuteToolCallsAsync(response.ToolCalls, messages, permittedTools, token).ConfigureAwait(false);
                if (repeated != null)
                {
                    result.ExitCode = 1;
                    _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Error, "[error] " + repeated.Reason);
                    _Callbacks.ProviderError(repeated);
                    break;
                }

                if (iteration == _MaxIterations - 1)
                {
                    result.ExitCode = 1;
                    _Callbacks.ProviderError(new RuntimeProviderError { ErrorType = "max_iterations_reached", Reason = "API endpoint captain stopped at the configured " + _MaxIterations + " model-turn limit." });
                    _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Warning, "[warning] reached the maximum of " + _MaxIterations + " model turns; stopping.");
                }
            }

            return result;
        }

        /// <summary>
        /// The tool result returned for a call to a tool the captain may not use (RS-7.4). A tool that exists but is
        /// disabled by the endpoint's inference policy gets tool_not_allowed; any other name gets unknown_tool with the
        /// names the captain can call, so a model that guessed a name can correct it.
        /// </summary>
        /// <param name="call">The call.</param>
        /// <param name="permittedTools">Tool names the captain may call.</param>
        /// <param name="disabledTools">Tool names that exist but are disabled by policy, or null.</param>
        /// <returns>The JSON tool result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when call or permittedTools is null.</exception>
        public static string DeniedToolResult(ToolCall call, ISet<string> permittedTools, ISet<string>? disabledTools)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            if (permittedTools == null) throw new ArgumentNullException(nameof(permittedTools));
            if (disabledTools != null && call.Name != null && disabledTools.Contains(call.Name))
                return JsonSerializer.Serialize(new { error = "tool_not_allowed", message = "This tool is disabled by the endpoint's inference policy." });

            List<string> names = permittedTools.OrderBy(n => n, StringComparer.Ordinal).ToList();
            string available = String.Join(", ", names.Take(MaxListedTools));
            if (names.Count > MaxListedTools) available += " and " + (names.Count - MaxListedTools) + " more";
            return JsonSerializer.Serialize(new { error = "unknown_tool", message = "unknown tool " + (call.Name ?? String.Empty) + "; available: " + available });
        }

        /// <summary>
        /// The key that makes two tool calls identical for the repetition guard: the name plus the arguments with
        /// top-level keys sorted and whitespace removed. Arguments that are not a JSON object compare as written.
        /// </summary>
        /// <param name="call">The call.</param>
        /// <returns>The comparison key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when call is null.</exception>
        public static string CallKey(ToolCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            string args = String.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson.Trim();
            try
            {
                Dictionary<string, object?>? parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(args);
                if (parsed != null) args = JsonSerializer.Serialize(new SortedDictionary<string, object?>(parsed, StringComparer.Ordinal));
            }
            catch (JsonException)
            {
            }
            return (call.Name ?? String.Empty) + "\n" + args;
        }

        #endregion

        #region Private-Methods

        private ChatMessage HistoryMessage(ChatResponse response)
        {
            ChatMessage message = response.ToAssistantMessage();
            // RS-7.6: leaked reasoning in assistant text is not sent back. Provider continuation state (thinking that a
            // provider requires to be replayed, signatures) lives in ProviderState and is kept unchanged.
            if (_Behaviors.ReasoningHygiene && ReasoningText.HasReasoning(message.Text))
            {
                string stripped = ReasoningText.Strip(message.Text);
                bool hasCalls = message.ToolCalls != null && message.ToolCalls.Count > 0;
                message.Text = stripped.Length == 0 && hasCalls ? null : stripped;
            }

            return message;
        }

        private void RecoverTextCalls(ChatResponse response, ISet<string> permittedTools, ApiTurnLoopResult result)
        {
            // RS-7.2: only a successful, complete response. A failed or incomplete stream arrives unsuccessful, and a
            // response cut at the token limit may hold a truncated call.
            if (!_Behaviors.TextToolCallRecovery || !response.Success) return;
            if (response.ToolCalls != null && response.ToolCalls.Count > 0) return;
            if (ApiTerminalPolicy.IsTokenLimit(response.FinishReason)) return;
            if (String.IsNullOrWhiteSpace(response.Text)) return;

            TextToolCallParseResult parsed = TextToolCallParser.Parse(_Terminal.VisibleText(response.Text), permittedTools);
            if (parsed.Calls.Count == 0) return;
            if (response.ToolCalls == null) response.ToolCalls = new List<ToolCall>();
            response.ToolCalls.AddRange(parsed.Calls);
            response.Text = parsed.RemainingText;
            result.TextToolCallRecoveries += parsed.Calls.Count;
            _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Inference, "[recovery] " + JsonSerializer.Serialize(new { textToolCalls = parsed.Calls.Count, format = parsed.Format, tools = parsed.Calls.Select(c => c.Name).ToList() }));
        }

        private async Task<RuntimeProviderError?> ExecuteToolCallsAsync(List<ToolCall> calls, List<ChatMessage> messages, ISet<string> permittedTools, CancellationToken token)
        {
            foreach (ToolCall call in calls)
            {
                token.ThrowIfCancellationRequested();
                string key = CallKey(call);
                if (String.Equals(key, _LastCallKey, StringComparison.Ordinal)) _RepeatCount++;
                else
                {
                    _LastCallKey = key;
                    _RepeatCount = 1;
                }

                // RS-7.5: twice the limit ends the run as a task failure, so escalation can pick another captain.
                if (_RepeatCount >= _RepeatedToolCallLimit * 2)
                {
                    return new RuntimeProviderError
                    {
                        ErrorType = "degenerate_output",
                        Reason = "API endpoint captain called " + call.Name + " with identical arguments " + _RepeatCount + " times in a row; stopping."
                    };
                }

                string resultContent;
                if (!permittedTools.Contains(call.Name))
                {
                    resultContent = DeniedToolResult(call, permittedTools, DisabledTools);
                    _Callbacks.ToolEvent(new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = false, PermissionDenied = true, Result = resultContent });
                }
                else resultContent = await _Callbacks.ExecuteToolAsync(call, token).ConfigureAwait(false);

                if (_RepeatCount >= _RepeatedToolCallLimit)
                {
                    resultContent += "\n\n[armada] You have called " + call.Name + " with identical arguments " + _RepeatCount
                        + " times in a row. The result will not change. Use it, try a different approach, or finish the task. The run stops at "
                        + (_RepeatedToolCallLimit * 2) + " identical calls.";
                    _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Warning, "[warning] " + call.Name + " called with identical arguments " + _RepeatCount + " times in a row; corrective note added.");
                }
                messages.Add(ChatMessage.ToolResult(call.Id, call.Name, resultContent));
            }
            return null;
        }

        #endregion
    }
}
