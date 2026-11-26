namespace Armada.Runtimes
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using PolyPrompt.Models;

    /// <summary>
    /// Keeps one API captain run inside the model's context window. Before each turn it compacts older tool output when
    /// the larger of the provider's last prompt count and the local estimate (messages plus tool schemas) reaches the
    /// compaction share; after each response it turns a prompt that filled the window into a typed failure, first trying
    /// one more compaction when the prompt is still below the window. Holds the last provider prompt count between turns.
    /// Not thread-safe: one instance serves one run.
    /// </summary>
    public sealed class ApiContextPolicy
    {
        #region Public-Members

        /// <summary>
        /// Configured context window in tokens, or null when it is unknown (nothing is compacted or stopped).
        /// </summary>
        public int? ContextLength
        {
            get { return _ContextLength; }
        }

        /// <summary>
        /// Prompt tokens the provider reported for the last response, or 0 after a compaction or before the first turn.
        /// </summary>
        public long LastPromptTokens
        {
            get { return _LastPromptTokens; }
        }

        #endregion

        #region Private-Members

        private readonly int? _ContextLength;
        private long _LastPromptTokens = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the policy for one run.
        /// </summary>
        /// <param name="contextLength">Configured context window in tokens, or null when unknown.</param>
        public ApiContextPolicy(int? contextLength)
        {
            _ContextLength = contextLength;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Compact the conversation before it is sent when it is near the window. The provider's count can lag or
        /// undercount (Ollama does not count cached prompt tokens), so the local estimate also decides. Tool schemas
        /// are resent on every turn, so they count toward the window too.
        /// </summary>
        /// <param name="messages">The conversation, compacted in place.</param>
        /// <param name="tools">Tool schemas sent with the next turn.</param>
        /// <returns>A diagnostic note when output was compacted, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when messages or tools is null.</exception>
        public string? BeforeTurn(List<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            long pressure = Math.Max(_LastPromptTokens, ApiContextCompactor.EstimateTokens(messages) + ApiContextCompactor.EstimateToolTokens(tools));
            if (!ApiContextCompactor.ShouldCompact(pressure, _ContextLength)) return null;
            long removed = ApiContextCompactor.Compact(messages);
            if (removed <= 0) return null;
            _LastPromptTokens = 0;
            return "[context] about " + pressure + " of " + _ContextLength
                + " tokens in use; replaced earlier tool output (about " + (removed / 4) + " tokens) with short notes to stay inside the window";
        }

        /// <summary>
        /// Check a response's prompt count against the window. A prompt that fills the window means the server has
        /// started (or is about to start) dropping the oldest turns, which hold the mission brief, so the run stops with a
        /// typed error; while the prompt is still below the window, older output is compacted and the run carries on.
        /// </summary>
        /// <param name="messages">The conversation, compacted in place when that avoids the failure.</param>
        /// <param name="usage">Token usage the provider reported, or null.</param>
        /// <returns>The failure to stop with (or null) and a diagnostic note (or null).</returns>
        /// <exception cref="ArgumentNullException">Thrown when messages is null.</exception>
        public ApiContextCheck AfterResponse(List<ChatMessage> messages, TokenUsage? usage)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            ApiContextCheck check = new ApiContextCheck();
            RuntimeProviderError? failure = ContextLimitFailure(_ContextLength, usage);
            if (usage?.PromptTokens != null) _LastPromptTokens = usage.PromptTokens.Value;
            if (failure != null && usage!.PromptTokens!.Value < _ContextLength!.Value)
            {
                long removed = ApiContextCompactor.Compact(messages);
                if (removed > 0)
                {
                    check.Note = "[context] prompt reached " + usage.PromptTokens + " of " + _ContextLength
                        + " tokens; replaced earlier tool output (about " + (removed / 4) + " tokens) with short notes and continued";
                    _LastPromptTokens = 0;
                    failure = null;
                }
            }
            check.Failure = failure;
            return check;
        }

        /// <summary>
        /// The typed failure for a prompt at or above 95 percent of the window, or null.
        /// </summary>
        /// <param name="contextLength">Context window in tokens, or null when unknown.</param>
        /// <param name="usage">Token usage the provider reported, or null.</param>
        /// <returns>A context_limit_exceeded error, or null.</returns>
        public static RuntimeProviderError? ContextLimitFailure(int? contextLength, TokenUsage? usage)
        {
            if (!contextLength.HasValue || usage?.PromptTokens == null) return null;
            int window = contextLength.Value;
            long prompt = usage.PromptTokens.Value;
            if (prompt * 100 < (long)window * 95) return null;
            return new RuntimeProviderError
            {
                ErrorType = "context_limit_exceeded",
                Reason = "The conversation reached the configured context window (prompt tokens: " + prompt + " of " + window
                    + "). Further turns would run on truncated history, so the run stopped."
            };
        }

        #endregion
    }
}
