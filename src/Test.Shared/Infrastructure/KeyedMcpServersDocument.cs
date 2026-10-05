namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// Typed view of a keyed MCP client configuration (Claude Code, Gemini, Cursor: "mcpServers"; OpenCode: "mcp").
    /// </summary>
    public sealed class KeyedMcpServersDocument
    {
        /// <summary>
        /// Servers keyed by name.
        /// </summary>
        public Dictionary<string, KeyedMcpServerEntry>? McpServers { get; set; } = null;

        /// <summary>
        /// Servers keyed by name under OpenCode's "mcp" object.
        /// </summary>
        public Dictionary<string, KeyedMcpServerEntry>? Mcp { get; set; } = null;
    }
}
