namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for updating a captain.
    /// </summary>
    public class CaptainUpdateArgs
    {
        /// <summary>
        /// Captain ID (cpt_ prefix).
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// New display name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// New agent runtime: ClaudeCode, Codex.
        /// </summary>
        public string? Runtime { get; set; }

        /// <summary>
        /// New system instructions for this captain.
        /// </summary>
        public string? SystemInstructions { get; set; }

    }
}
