namespace Armada.Server.Ask
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One newline-delimited event of Claude Code's <c>--output-format stream-json</c> output, reduced to the fields the
    /// chat service reads for tool activity (assistant tool_use blocks and user tool_result blocks).
    /// </summary>
    public class ClaudeStreamLine
    {
        #region Public-Members

        /// <summary>
        /// Event type (assistant, user, result, stream_event, system).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Message of an assistant or user event, or null.
        /// </summary>
        [JsonPropertyName("message")]
        public ClaudeStreamMessage? Message { get; set; } = null;

        #endregion
    }
}
