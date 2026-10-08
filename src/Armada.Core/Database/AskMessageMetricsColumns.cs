namespace Armada.Core.Database
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Maps the turn telemetry columns of <c>ask_messages</c> (migration v82: ttft_ms, first_text_ms, streaming_ms,
    /// tokens_per_second, input_tokens, output_tokens, cached_tokens, tokens_estimated, cost_usd, tool_call_count,
    /// tool_time_ms) to <see cref="AskMessage.Metrics"/>, identically for every database provider. The total time is the
    /// existing duration_ms column, and total tokens are derived (input plus output).
    /// </summary>
    public static class AskMessageMetricsColumns
    {
        #region Public-Methods

        /// <summary>
        /// Complete the metrics read from a row: null when the row has no telemetry (messages written before v82, user
        /// messages, cards), otherwise the stored values with the total time and total tokens filled in.
        /// </summary>
        /// <param name="stored">Values read from the telemetry columns.</param>
        /// <param name="durationMs">The row's duration_ms.</param>
        /// <returns>The metrics, or null.</returns>
        public static CaptainChatMetrics? FromColumns(CaptainChatMetrics stored, long? durationMs)
        {
            if (stored == null) throw new ArgumentNullException(nameof(stored));
            bool any = stored.TimeToFirstTokenMs.HasValue
                || stored.TimeToFirstTextMs.HasValue
                || stored.StreamingMs.HasValue
                || stored.TokensPerSecond.HasValue
                || stored.PromptTokens.HasValue
                || stored.CompletionTokens.HasValue
                || stored.CachedTokens.HasValue
                || stored.TokensEstimated.HasValue
                || stored.CostUsd.HasValue
                || stored.ToolCallCount.HasValue
                || stored.ToolTimeMs.HasValue;
            if (!any) return null;

            stored.TotalMs = durationMs.HasValue ? (double)durationMs.Value : (double?)null;
            if (stored.PromptTokens.HasValue && stored.CompletionTokens.HasValue)
            {
                long total = (long)stored.PromptTokens.Value + stored.CompletionTokens.Value;
                stored.TotalTokens = total > Int32.MaxValue ? Int32.MaxValue : (int)total;
            }

            return stored;
        }

        #endregion
    }
}
