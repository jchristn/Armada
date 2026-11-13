namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP get_captain_tools arguments: the captain plus paging and filtering of its named tools.
    /// </summary>
    public class CaptainToolsArgs
    {
        /// <summary>
        /// Captain ID (cpt_ prefix).
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// One-based page of tools, or null for 1.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Tools per page (1 to 1000), or null for 10.
        /// </summary>
        public int? PageSize { get; set; } = null;

        /// <summary>
        /// Only tools of this source kind (McpServer, RuntimeBuiltIn, or Internal), or null for every tool.
        /// </summary>
        public string? SourceKind { get; set; } = null;

        /// <summary>
        /// True to probe the runtime again instead of reusing a snapshot from the last 60 seconds.
        /// </summary>
        public bool? Refresh { get; set; } = null;
    }
}
