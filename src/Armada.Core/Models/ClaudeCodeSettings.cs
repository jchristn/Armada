namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Represents the structure of a Claude Code settings.json file.
    /// Preserves unknown properties for round-trip serialization.
    /// </summary>
    public class ClaudeCodeSettings
    {
        #region Public-Members

        /// <summary>
        /// MCP server configurations keyed by server name.
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public Dictionary<string, McpServerEntry>? McpServers { get; set; }

        /// <summary>
        /// Additional properties not explicitly modeled, preserved for round-trip serialization.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, object>? AdditionalProperties { get; set; }

        #endregion
    }
}
