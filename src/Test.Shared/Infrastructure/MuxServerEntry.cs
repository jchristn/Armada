namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Typed view of one server in a Mux mcp-servers.json document.
    /// </summary>
    public sealed class MuxServerEntry
    {
        /// <summary>
        /// Server name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Transport ("http").
        /// </summary>
        public string? Transport { get; set; } = null;

        /// <summary>
        /// Base URL without the MCP path.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// MCP path appended to the base URL.
        /// </summary>
        public string? McpPath { get; set; } = null;
    }
}
