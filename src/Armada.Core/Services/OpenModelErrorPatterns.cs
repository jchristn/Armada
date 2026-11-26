namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Gives an open-model server's error a machine error type (RS-5.3). Ollama, vLLM, LM Studio, llama.cpp and LiteLLM
    /// report most failures as a plain message in the response body, with no error code; this is the one place that
    /// message is read, and only the provider error message is passed in, never mission output. The result is a type that
    /// <see cref="RuntimeFailureClassifier"/> knows. Thread safe.
    /// </summary>
    public static class OpenModelErrorPatterns
    {
        #region Public-Members

        /// <summary>The model server's runner process died (Ollama runner, vLLM engine, LM Studio model crash). Transient.</summary>
        public const string RunnerTerminated = "runner_terminated";

        /// <summary>The server could not fit the model in memory. The captain cannot work until the endpoint changes.</summary>
        public const string ModelOutOfMemory = "model_out_of_memory";

        /// <summary>The model or server cannot do tool calling as configured. The captain cannot work until that changes.</summary>
        public const string ToolCallingUnsupported = "tool_calling_unsupported";

        /// <summary>The model is still loading. Transient.</summary>
        public const string ModelLoading = "model_loading";

        /// <summary>The server could not parse the tool call the model produced. A task failure, not an endpoint failure.</summary>
        public const string ToolCallParseError = "tool_call_parse_error";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Match a provider error message to a machine error type.
        /// </summary>
        /// <param name="httpStatusCode">The HTTP status, when known. A request the server rejected (400) for its tool or
        /// schema shape is a <c>schema_validation_failed</c>; other statuses do not use that rule.</param>
        /// <param name="message">The provider error message or response body.</param>
        /// <returns>The error type, or null when the message names nothing known.</returns>
        public static string? Match(int? httpStatusCode, string? message)
        {
            if (String.IsNullOrWhiteSpace(message)) return null;
            string text = message.ToLowerInvariant();

            if (text.Contains("does not support tools")
                || text.Contains("tool choice requires --enable-auto-tool-choice")
                || text.Contains("does not support function calling")
                || text.Contains("tools are not supported"))
                return ToolCallingUnsupported;

            if (text.Contains("error parsing tool call")
                || text.Contains("failed to parse tool call")
                || text.Contains("could not parse tool call")
                || text.Contains("malformed tool call"))
                return ToolCallParseError;

            if (MentionsContextLimit(text)) return "context_limit_exceeded";

            if (text.Contains("out of memory")
                || text.Contains("requires more system memory")
                || text.Contains("cudamalloc failed")
                || text.Contains("insufficient system resources")
                || text.Contains("insufficient memory"))
                return ModelOutOfMemory;

            if (text.Contains("runner process has terminated")
                || text.Contains("runner process no longer running")
                || text.Contains("engine loop has died")
                || text.Contains("enginedeaderror")
                || text.Contains("enginecore encountered an issue")
                || text.Contains("model has crashed"))
                return RunnerTerminated;

            if (text.Contains("loading model")
                || text.Contains("model is loading")
                || text.Contains("is currently loading"))
                return ModelLoading;

            if (text.Contains("no models loaded")
                || text.Contains("not attached to this virtual model runner")
                || text.Contains("litellm.notfounderror")
                || (text.Contains("model") && (text.Contains("not found") || text.Contains("does not exist"))))
                return "model_not_found";

            if (text.Contains("litellm.authenticationerror")) return "authentication_error";
            if (text.Contains("litellm.apiconnectionerror")) return "llm_connection_error";
            if (text.Contains("litellm.timeout")) return "llm_timeout";
            if (text.Contains("litellm.serviceunavailableerror") || text.Contains("litellm.internalservererror")) return "server_error";

            if (httpStatusCode == 400
                && (text.Contains("schema") || text.Contains("tool") || text.Contains("function")))
                return "schema_validation_failed";

            return null;
        }

        /// <summary>
        /// Whether a provider message says the request exceeded the model's context window.
        /// </summary>
        /// <param name="message">The provider error message.</param>
        /// <returns>True when the message names the context or token limit.</returns>
        public static bool MentionsContextLimit(string? message)
        {
            if (String.IsNullOrWhiteSpace(message)) return false;
            string lower = message.ToLowerInvariant();
            return lower.Contains("context length")
                || lower.Contains("context window")
                || lower.Contains("context_length")
                || lower.Contains("maximum context")
                || lower.Contains("contextwindowexceeded")
                || lower.Contains("too many tokens")
                || lower.Contains("prompt is too long")
                || lower.Contains("prompt is longer than")
                || lower.Contains("num_ctx");
        }

        #endregion
    }
}
