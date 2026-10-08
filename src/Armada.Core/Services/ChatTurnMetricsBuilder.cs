namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Builds per-turn chat metrics (time-to-first-token, streaming time, completion tokens, tokens/sec,
    /// total time) identically for every conversational surface -- Ask Armada and planning sessions -- so
    /// the shared metrics popover shows consistent, sensible numbers regardless of runtime.
    /// </summary>
    public static class ChatTurnMetricsBuilder
    {
        #region Private-Members

        // Approximate characters per token (~3.5) used to estimate completion tokens from the reply text
        // when a runtime does not report a real completion-token count. This is deliberately based on the
        // REPLY only: some runtimes (e.g. Mux) report a whole-context token estimate (system prompt plus
        // every MCP tool schema, which can be tens of thousands of tokens) that must never be shown as the
        // size of the reply.
        private const double _CharsPerToken = 3.5;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the metrics for one assistant turn.
        /// </summary>
        /// <param name="totalMs">Wall-clock duration of the whole turn, in milliseconds.</param>
        /// <param name="timeToFirstTokenMs">Time from turn start to the first streamed output token, in
        /// milliseconds; null when nothing streamed (no first-token timestamp was observed).</param>
        /// <param name="reply">The final assistant reply text, used to estimate completion tokens when a
        /// real count is not supplied.</param>
        /// <param name="reportedCompletionTokens">A real completion-token count reported by the runtime
        /// (for example Claude Code's output_tokens). When null the count is estimated from the reply.</param>
        /// <returns>The populated metrics.</returns>
        public static CaptainChatMetrics Build(
            double totalMs,
            double? timeToFirstTokenMs,
            string? reply,
            int? reportedCompletionTokens = null)
        {
            CaptainChatMetrics metrics = new CaptainChatMetrics();
            metrics.TotalMs = totalMs;
            metrics.TimeToFirstTokenMs = timeToFirstTokenMs;

            double? streamingMs = timeToFirstTokenMs.HasValue
                ? Math.Max(0.0, totalMs - timeToFirstTokenMs.Value)
                : (double?)null;
            metrics.StreamingMs = streamingMs;

            bool reported = reportedCompletionTokens.HasValue && reportedCompletionTokens.Value >= 0;
            int completionTokens = reported
                ? reportedCompletionTokens!.Value
                : (int)Math.Round((reply?.Length ?? 0) / _CharsPerToken);
            metrics.CompletionTokens = completionTokens;
            metrics.TokensEstimated = !reported;

            // Generation throughput: completion tokens produced across the streaming (post-first-token)
            // window, not the whole turn -- otherwise long reasoning/tool time before the first token
            // deflates the rate and a whole-context token estimate inflates it.
            if (streamingMs.HasValue && streamingMs.Value > 0.0)
            {
                metrics.TokensPerSecond = completionTokens / (streamingMs.Value / 1000.0);
            }

            return metrics;
        }

        #endregion
    }
}
