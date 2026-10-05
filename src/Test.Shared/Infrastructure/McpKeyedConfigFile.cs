namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The keyed MCP config file (Claude Code, Gemini, Cursor) as written by ArmadaMcpConfigBuilder.
    /// </summary>
    public class McpKeyedConfigFile
    {
        #region Public-Members

        /// <summary>
        /// Servers by key.
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public Dictionary<string, McpKeyedServerEntry>? McpServers { get; set; } = null;

        #endregion
    }
}
