namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The MCP server maps of a host user's agent CLI configuration file, read (never written) so a thread-scoped launch
    /// can find the user's existing Armada server entries. OpenCode keeps its servers under <c>mcp</c>; Cursor, Gemini, and
    /// Claude Code keep them under <c>mcpServers</c>. Each server definition is kept as raw JSON because only its name and
    /// its text are inspected.
    /// </summary>
    public class HostMcpConfigDocument
    {
        #region Public-Members

        /// <summary>
        /// OpenCode server map (<c>mcp</c>), keyed by server name, or null when absent.
        /// </summary>
        [JsonPropertyName("mcp")]
        public Dictionary<string, object?>? Mcp { get; set; } = null;

        /// <summary>
        /// Keyed server map used by Cursor, Gemini, and Claude Code (<c>mcpServers</c>), or null when absent.
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public Dictionary<string, object?>? McpServers { get; set; } = null;

        #endregion
    }
}
