namespace Armada.Runtimes
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using PolyPrompt.Models;

    /// <summary>
    /// What <see cref="ApiTurnLoop"/> needs from its host: the inference call, tool execution, and where output,
    /// diagnostics, tool events and provider errors go. Every member must be set before the loop runs.
    /// </summary>
    public sealed class ApiTurnLoopCallbacks
    {
        /// <summary>
        /// Send one turn (with the host's retry, streaming and timeout handling) and return the response.
        /// </summary>
        public Func<ChatRequest, CancellationToken, Task<ChatResponse>> InferAsync { get; set; } = (r, t) => throw new InvalidOperationException("InferAsync is not set.");

        /// <summary>
        /// Build the provider error for a failed response, given the fallback reason.
        /// </summary>
        public Func<ChatResponse, string, RuntimeProviderError> InferenceFailure { get; set; } = (r, reason) => new RuntimeProviderError { ErrorType = "inference_error", Reason = reason };

        /// <summary>
        /// Execute a permitted tool call (built-in, MCP or the native submit tool) and return the tool result text.
        /// </summary>
        public Func<ToolCall, CancellationToken, Task<string>> ExecuteToolAsync { get; set; } = (c, t) => throw new InvalidOperationException("ExecuteToolAsync is not set.");

        /// <summary>
        /// Visible captain output.
        /// </summary>
        public Action<string> Emit { get; set; } = text => { };

        /// <summary>
        /// Diagnostic line of the given kind.
        /// </summary>
        public Action<ApiRuntimeDiagnosticKindEnum, string> Diagnostic { get; set; } = (kind, text) => { };

        /// <summary>
        /// Typed tool event.
        /// </summary>
        public Action<ApiRuntimeToolEvent> ToolEvent { get; set; } = toolEvent => { };

        /// <summary>
        /// Typed provider error that ends the run.
        /// </summary>
        public Action<RuntimeProviderError> ProviderError { get; set; } = error => { };
    }
}
