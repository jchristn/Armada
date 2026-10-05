namespace Armada.Server.RuntimeTools
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The part of a runtime JSON config file (Claude Code, Gemini CLI, Cursor) that lists MCP servers. All other members
    /// of the file are ignored.
    /// </summary>
    public class RuntimeJsonMcpConfigFile
    {
        #region Public-Members

        /// <summary>
        /// Configured MCP servers keyed by name, or null when the file lists none.
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public Dictionary<string, RuntimeJsonMcpServerEntry>? McpServers { get; set; } = null;

        #endregion
    }
}
