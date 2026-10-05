namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP arguments for list_cli_permission_requests.
    /// </summary>
    public class CliPermissionRequestListArgs
    {
        /// <summary>
        /// Status filter (Pending, Allowed, Denied, Expired, Cancelled), or null for all.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Mission filter, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Ask thread filter, or null.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Captain filter, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Vessel filter, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Maximum rows (1-1000, default 100).
        /// </summary>
        public int? Limit { get; set; } = null;
    }
}
