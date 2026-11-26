namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
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
        #region Private-Members

        private readonly ApiTurnLoopCallbacks _Callbacks;
        private readonly ApiContextPolicy _Context;
        private readonly ApiTerminalPolicy _Terminal;
        private readonly NativeSubmitTool? _SubmitTool;
        private readonly CompletionOptions _Options;
        private readonly int _MaxIterations;
        private readonly bool _Streamed;

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
        /// <exception cref="ArgumentNullException">Thrown when callbacks, context, terminal or options is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxIterations is negative.</exception>
        public ApiTurnLoop(
            ApiTurnLoopCallbacks callbacks,
            ApiContextPolicy context,
            ApiTerminalPolicy terminal,
            NativeSubmitTool? submitTool,
            CompletionOptions options,
            int maxIterations,
            bool streamed)
        {
            _Callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            if (maxIterations < 0) throw new ArgumentOutOfRangeException(nameof(maxIterations));
            _SubmitTool = submitTool;
            _MaxIterations = maxIterations;
            _Streamed = streamed;
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
            for (int iteration = 0; iteration < _MaxIterations; iteration++)
            {
                token.ThrowIfCancellationRequested();

                string? compacted = _Context.BeforeTurn(messages, tools);
                if (compacted != null) _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Inference, compacted);

                ChatRequest request = new ChatRequest();
                request.Messages = messages;
                request.Tools = tools;
                request.Options = _Options;
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

                if (!response.Success)
                {
                    string reason = String.IsNullOrEmpty(response.Error) ? "the inference endpoint reported failure without detail" : response.Error!;
                    _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Error, "[error] inference call failed: " + reason);
                    _Callbacks.ProviderError(_Callbacks.InferenceFailure(response, "Inference request failed: " + reason));
                    result.ExitCode = 1;
                    break;
                }

                ApiContextCheck window = _Context.AfterResponse(messages, response.Usage);
                if (window.Note != null) _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Inference, window.Note);
                if (window.Failure != null)
                {
                    _Callbacks.Diagnostic(ApiRuntimeDiagnosticKindEnum.Error, "[error] " + window.Failure.Reason);
                    _Callbacks.ProviderError(window.Failure);
                    result.ExitCode = 1;
                    break;
                }

                string visibleText = ApiTerminalPolicy.StripThinkBlocks(response.Text);
                if (!String.IsNullOrEmpty(visibleText))
                {
                    result.FinalText = visibleText;
                    _Callbacks.Emit(visibleText);
                }

                messages.Add(response.ToAssistantMessage());

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
                await ExecuteToolCallsAsync(response.ToolCalls, messages, permittedTools, token).ConfigureAwait(false);

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
        /// The tool result returned for a call to a tool the captain may not use.
        /// </summary>
        /// <param name="call">The call.</param>
        /// <returns>The JSON tool result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when call is null.</exception>
        public static string DeniedToolResult(ToolCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            return JsonSerializer.Serialize(new { error = "tool_not_allowed", message = "This tool is disabled by the endpoint's inference policy." });
        }

        #endregion

        #region Private-Methods

        private async Task ExecuteToolCallsAsync(List<ToolCall> calls, List<ChatMessage> messages, ISet<string> permittedTools, CancellationToken token)
        {
            foreach (ToolCall call in calls)
            {
                token.ThrowIfCancellationRequested();
                string resultContent;
                if (!permittedTools.Contains(call.Name))
                {
                    resultContent = DeniedToolResult(call);
                    _Callbacks.ToolEvent(new ApiRuntimeToolEvent { Phase = ApiRuntimeToolPhaseEnum.Completed, Id = call.Id, Name = call.Name, Ok = false, PermissionDenied = true, Result = resultContent });
                }
                else resultContent = await _Callbacks.ExecuteToolAsync(call, token).ConfigureAwait(false);
                messages.Add(ChatMessage.ToolResult(call.Id, call.Name, resultContent));
            }
        }

        #endregion
    }
}
