namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// The turn statistics of an Ask reply, row for row as the dashboard's ChatMetricsInfo popover and the mobile app's
    /// (i) panel show them (lib/chatMetrics): for a reply the Admiral recorded telemetry for, time to first token, time to
    /// first text (when it differs), streaming, tokens per second, output tokens ("~N" when estimated), input and cached
    /// tokens and cost (when the runtime reported them), total, tool calls, and tool time; for an older reply, its total
    /// and its tool calls.
    /// </summary>
    public static class AskTurnStatistics
    {
        #region Public-Methods

        /// <summary>
        /// The rows of a message; empty when it has nothing to show (user messages, cards, replies without timing).
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>Rows, in display order.</returns>
        public static List<AskTurnStatistic> Rows(AskMessage message)
        {
            List<AskTurnStatistic> rows = new List<AskTurnStatistic>();
            if (message == null) return rows;
            List<AskMessageToolCall> completed = (message.ToolCalls ?? new List<AskMessageToolCall>()).Where(IsCompleted).ToList();

            CaptainChatMetrics? m = message.Metrics;
            if (m == null)
            {
                if (message.DurationMs != null) rows.Add(new AskTurnStatistic("total", "total", FormatMs(message.DurationMs)));
                AddToolRows(rows, completed.Count, completed.Sum(c => (double)(c.ElapsedMs ?? 0)));
                return rows;
            }

            int? tokenCount = m.CompletionTokens ?? m.TotalTokens;
            bool estimated = m.TokensEstimated == true && tokenCount != null && tokenCount == m.CompletionTokens;
            bool hasInput = m.PromptTokens != null;
            string tilde = estimated ? "~" : "";

            rows.Add(new AskTurnStatistic("timeToFirstToken", "time to first token", FormatMs(m.TimeToFirstTokenMs)));
            if (m.TimeToFirstTextMs != null && FormatMs(m.TimeToFirstTextMs) != FormatMs(m.TimeToFirstTokenMs))
                rows.Add(new AskTurnStatistic("timeToFirstText", "time to first text", FormatMs(m.TimeToFirstTextMs)));
            rows.Add(new AskTurnStatistic("streaming", "streaming", FormatMs(m.StreamingMs)));
            rows.Add(new AskTurnStatistic("tokensPerSecond", "tokens/sec", m.TokensPerSecond != null ? tilde + m.TokensPerSecond.Value.ToString("0.0", CultureInfo.InvariantCulture) : "-"));
            rows.Add(new AskTurnStatistic("tokens", hasInput && m.CompletionTokens != null ? "output tokens" : "tokens", tokenCount != null ? tilde + tokenCount.Value.ToString(CultureInfo.InvariantCulture) : "-"));
            if (hasInput) rows.Add(new AskTurnStatistic("inputTokens", "input tokens", m.PromptTokens!.Value.ToString(CultureInfo.InvariantCulture)));
            if (m.CachedTokens != null) rows.Add(new AskTurnStatistic("cachedTokens", "cached tokens", m.CachedTokens.Value.ToString(CultureInfo.InvariantCulture)));
            if (m.CostUsd != null) rows.Add(new AskTurnStatistic("cost", "cost", FormatCost(m.CostUsd)));
            rows.Add(new AskTurnStatistic("total", "total", FormatMs(m.TotalMs ?? message.DurationMs)));
            if (completed.Count > 0) AddToolRows(rows, completed.Count, completed.Sum(c => (double)(c.ElapsedMs ?? 0)));
            else if (m.ToolCallCount != null && m.ToolCallCount.Value > 0) AddToolRows(rows, m.ToolCallCount.Value, m.ToolTimeMs ?? 0);
            return rows;
        }

        /// <summary>
        /// Milliseconds as the statistics show them: "-" when unknown, whole ms below a second, then seconds with two
        /// decimals.
        /// </summary>
        /// <param name="ms">Milliseconds, or null.</param>
        /// <returns>Text.</returns>
        public static string FormatMs(double? ms)
        {
            if (ms == null) return "-";
            if (ms.Value >= 1000) return (ms.Value / 1000.0).ToString("0.00", CultureInfo.InvariantCulture) + "s";
            return Math.Round(ms.Value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "ms";
        }

        /// <summary>
        /// A cost in US dollars: "-" when unknown, four decimals below a dollar, else two.
        /// </summary>
        /// <param name="usd">Dollars, or null.</param>
        /// <returns>Text.</returns>
        public static string FormatCost(double? usd)
        {
            if (usd == null) return "-";
            return "$" + usd.Value.ToString(usd.Value >= 1 ? "0.00" : "0.0000", CultureInfo.InvariantCulture);
        }

        #endregion

        #region Private-Methods

        private static void AddToolRows(List<AskTurnStatistic> rows, int count, double ms)
        {
            if (count < 1) return;
            rows.Add(new AskTurnStatistic("toolCalls", "tool calls", count.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new AskTurnStatistic("toolTime", "tool time", FormatMs(ms)));
        }

        private static bool IsCompleted(AskMessageToolCall call)
        {
            // As the dashboard's tool chips: a call is running until it has an outcome or a result.
            return call != null && (call.PermissionDenied == true || call.Ok != null || !String.IsNullOrEmpty(call.ResultText));
        }

        #endregion
    }
}
