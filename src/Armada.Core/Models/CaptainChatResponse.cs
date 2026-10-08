namespace Armada.Core.Models
{
    /// <summary>
    /// Result of a captain chat turn: the assistant reply plus the model that produced it and the
    /// per-turn timing/token metrics.
    /// </summary>
    public class CaptainChatResponse
    {
        #region Public-Members

        /// <summary>
        /// Whether the turn completed successfully.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// The assistant reply text (markdown). Empty on failure.
        /// </summary>
        public string Reply { get; set; } = string.Empty;

        /// <summary>
        /// The model that produced the reply (for example <c>gpt-oss:20b</c>).
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Per-turn timing and token statistics.
        /// </summary>
        public CaptainChatMetrics Metrics { get; set; } = new CaptainChatMetrics();

        /// <summary>
        /// Error message when <see cref="Success"/> is false.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Machine-readable reason for a failure a client can act on (no Harbor to run the captain, the CLI is not
        /// installed on the Admiral host, the Harbor could not start the captain), or null.
        /// </summary>
        public Armada.Core.Enums.CaptainChatErrorCodeEnum? ErrorCode { get; set; } = null;

        /// <summary>
        /// The model's captured reasoning ("thinking") for this turn, when the request set
        /// <see cref="CaptainChatRequest.ShowThinking"/> and the runtime surfaced it. Null otherwise.
        /// </summary>
        public string? Thinking { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainChatResponse()
        {
        }

        #endregion
    }
}
