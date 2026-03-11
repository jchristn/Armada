namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for creating a captain.
    /// </summary>
    public class CaptainCreateArgs
    {
        /// <summary>
        /// Captain display name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Agent runtime: ClaudeCode, Codex.
        /// </summary>
        public string? Runtime { get; set; }

        /// <summary>
        /// Maximum number of concurrent missions (default 1, minimum 1).
        /// </summary>
        public int? MaxParallelism { get; set; }
    }
}
