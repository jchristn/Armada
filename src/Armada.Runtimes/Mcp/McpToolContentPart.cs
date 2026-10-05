namespace Armada.Runtimes.Mcp
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One part of an MCP tool result's <c>content</c> array.
    /// </summary>
    public class McpToolContentPart
    {
        #region Public-Members

        /// <summary>
        /// Part type (for example <c>text</c>).
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; } = null;

        /// <summary>
        /// Text of a <c>text</c> part, or null.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; } = null;

        #endregion
    }
}
