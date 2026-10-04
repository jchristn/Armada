namespace Armada.Server.Ask
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The message carried by a Claude Code stream-json assistant or user event.
    /// </summary>
    public class ClaudeStreamMessage
    {
        #region Public-Members

        /// <summary>
        /// Model name, or null.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Content blocks, or null when the content is a plain string.
        /// </summary>
        [JsonPropertyName("content")]
        public List<ClaudeStreamContentBlock>? Content { get; set; } = null;

        #endregion
    }
}
