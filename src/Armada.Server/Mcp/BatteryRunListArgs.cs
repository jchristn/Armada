namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of list_battery_runs.
    /// </summary>
    public class BatteryRunListArgs
    {
        /// <summary>
        /// Tenant filter (global admins only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Captain filter (cpt_ prefix).
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Status filter (Queued, Running, Completed, Failed or Cancelled).
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Page number (default 1).
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size (1-1000, default 25).
        /// </summary>
        public int? PageSize { get; set; } = null;
    }
}
