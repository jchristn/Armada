namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The terminal "result" event of Claude Code's stream-json output, reduced to the fields the runtime failure
    /// classifier needs: whether the run ended in error, and the CLI's own result text (which, for an API failure,
    /// is the "API Error: status {json}" protocol line).
    /// </summary>
    public class ClaudeResultEvent
    {
        #region Public-Members

        /// <summary>
        /// Event type ("result" for the terminal event).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Result subtype (for example "success", "error_during_execution", "error_max_turns").
        /// </summary>
        [JsonPropertyName("subtype")]
        public string? Subtype { get; set; } = null;

        /// <summary>
        /// Whether the run ended in error.
        /// </summary>
        [JsonPropertyName("is_error")]
        public bool IsError { get; set; } = false;

        /// <summary>
        /// The final result text.
        /// </summary>
        [JsonPropertyName("result")]
        public string? Result { get; set; } = null;

        #endregion
    }
}
