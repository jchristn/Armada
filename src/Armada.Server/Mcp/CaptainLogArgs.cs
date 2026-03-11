namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for retrieving a captain's session log.
    /// </summary>
    public class CaptainLogArgs
    {
        /// <summary>
        /// Captain ID (cpt_ prefix).
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Number of lines to return (default 100).
        /// </summary>
        public int? Lines { get; set; }

        /// <summary>
        /// Line offset to start from (default 0).
        /// </summary>
        public int? Offset { get; set; }
    }
}
