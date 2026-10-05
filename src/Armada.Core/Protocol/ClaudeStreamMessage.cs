namespace Armada.Core.Protocol
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
        /// Content blocks; a plain string content is read as one text block. Null when absent.
        /// </summary>
        [JsonPropertyName("content")]
        [JsonConverter(typeof(ClaudeContentListConverter))]
        public List<ClaudeStreamContentBlock>? Content { get; set; } = null;

        #endregion
    }
}
