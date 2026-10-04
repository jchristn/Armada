namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for discovering vessel import candidates.
    /// </summary>
    public class DiscoverVesselsArgs
    {
        /// <summary>
        /// Explicit directories; a non-repository directory is scanned like a root.
        /// </summary>
        public List<string>? Directories { get; set; }

        /// <summary>
        /// Roots to scan breadth-first for git repositories.
        /// </summary>
        public List<string>? Roots { get; set; }

        /// <summary>
        /// Maximum scan depth (1-16), or null for the configured default.
        /// </summary>
        public int? MaxDepth { get; set; }
    }
}
