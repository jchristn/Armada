namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for retrying a failed mission landing.
    /// </summary>
    public class MissionRetryLandingArgs
    {
        /// <summary>
        /// Mission ID (msn_ prefix) to retry landing for.
        /// </summary>
        public string MissionId { get; set; } = "";
    }
}
