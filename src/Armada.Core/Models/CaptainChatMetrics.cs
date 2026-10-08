namespace Armada.Core.Models
{
    /// <summary>
    /// Timing and token statistics for a single captain chat turn (Ask Armada, captain chat, planning), surfaced in
    /// the dashboard, the mobile app, and the TUI so an operator can see how the model performed. Durations are
    /// measured by the server on the monotonic clock (see <see cref="Armada.Core.Services.ChatTurnTelemetryRecorder"/>);
    /// token counts and cost come from the runtime's own usage report where it has one (Claude Code's stream-json
    /// result, Codex's turn.completed usage, OpenCode's step_finish), and completion tokens are otherwise estimated
    /// from the reply length (<see cref="TokensEstimated"/>).
    /// </summary>
    public class CaptainChatMetrics
    {
        #region Public-Members

        /// <summary>
        /// Approximate time to first token in milliseconds. Derived from the provider's prompt-eval
        /// duration when reported; otherwise the wall-clock time until the response began.
        /// </summary>
        public double? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Time spent generating (streaming) the completion, in milliseconds. From the provider's eval
        /// duration when reported.
        /// </summary>
        public double? StreamingMs { get; set; } = null;

        /// <summary>
        /// Total wall-clock time for the turn in milliseconds (request sent to response complete).
        /// </summary>
        public double? TotalMs { get; set; } = null;

        /// <summary>
        /// Number of prompt (input) tokens, when reported by the provider.
        /// </summary>
        public int? PromptTokens { get; set; } = null;

        /// <summary>
        /// Number of completion (output) tokens, when reported by the provider.
        /// </summary>
        public int? CompletionTokens { get; set; } = null;

        /// <summary>
        /// Total tokens (prompt + completion), when reported by the provider.
        /// </summary>
        public int? TotalTokens { get; set; } = null;

        /// <summary>
        /// Output tokens per second over the generation window, when both the completion token count and
        /// the generation duration are known.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

        /// <summary>
        /// Time from the start of the turn to the first visible reply text, in milliseconds, when it differs from
        /// <see cref="TimeToFirstTokenMs"/> (the first output of any kind: reasoning, a tool call, or reply text).
        /// Null when no reply text streamed.
        /// </summary>
        public double? TimeToFirstTextMs { get; set; } = null;

        /// <summary>
        /// Cache-read input tokens (a subset of <see cref="PromptTokens"/>), when reported by the runtime.
        /// </summary>
        public int? CachedTokens { get; set; } = null;

        /// <summary>
        /// Cost of the turn in US dollars, when reported by the runtime (Claude Code's total_cost_usd, OpenCode's
        /// per-step cost).
        /// </summary>
        public double? CostUsd { get; set; } = null;

        /// <summary>
        /// True when <see cref="CompletionTokens"/> is an estimate from the reply length (the runtime reported no
        /// output token count); false when the runtime reported it. Null when unknown.
        /// </summary>
        public bool? TokensEstimated { get; set; } = null;

        /// <summary>
        /// Number of tool calls the captain completed during the turn, when the turn was observed by the server.
        /// </summary>
        public int? ToolCallCount { get; set; } = null;

        /// <summary>
        /// Total time spent in the turn's tool calls in milliseconds (the sum of each completed call's time).
        /// </summary>
        public double? ToolTimeMs { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainChatMetrics()
        {
        }

        #endregion
    }
}
