namespace Armada.Runtimes.Mcp
{
    using System.Text.Json.Serialization;
    using Armada.Core.Protocol;

    /// <summary>
    /// One part of an MCP tool result's <c>content</c> array.
    /// </summary>
    public class McpToolContentPart
    {
        #region Public-Members

        /// <summary>
        /// Part type (for example <c>text</c>, <c>image</c>, <c>audio</c>, <c>resource</c>).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text of a <c>text</c> part, or null.
        /// </summary>
        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; } = null;

        /// <summary>
        /// Base64 data of an <c>image</c> or <c>audio</c> part, or null.
        /// </summary>
        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Data { get; set; } = null;

        /// <summary>
        /// MIME type of an <c>image</c> or <c>audio</c> part, or null.
        /// </summary>
        [JsonPropertyName("mimeType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MimeType { get; set; } = null;

        /// <summary>
        /// Embedded resource of a <c>resource</c> part as raw JSON, or null.
        /// </summary>
        [JsonPropertyName("resource")]
        [JsonConverter(typeof(RawJsonStringConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Resource { get; set; } = null;

        #endregion
    }
}
