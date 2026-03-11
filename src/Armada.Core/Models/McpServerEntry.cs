namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Represents a single MCP server entry in Claude Code settings.
    /// </summary>
    public class McpServerEntry
    {
        #region Public-Members

        /// <summary>
        /// Transport type (e.g. "http", "stdio").
        /// </summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; set; }

        /// <summary>
        /// Server URL (for HTTP transport).
        /// </summary>
        [JsonPropertyName("url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Url { get; set; }

        /// <summary>
        /// Command path (for stdio transport).
        /// </summary>
        [JsonPropertyName("command")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Command { get; set; }

        /// <summary>
        /// Command arguments (for stdio transport).
        /// </summary>
        [JsonPropertyName("args")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Args { get; set; }

        /// <summary>
        /// Additional properties not explicitly modeled.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object>? AdditionalProperties { get; set; }

        #endregion
    }
}
