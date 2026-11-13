namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of list_battery_results.
    /// </summary>
    public class BatteryResultListArgs
    {
        /// <summary>
        /// Battery run identifier (bat_ prefix).
        /// </summary>
        public string? RunId { get; set; } = null;

        /// <summary>
        /// Page number (default 1).
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size (1 to 1000, default 10).
        /// </summary>
        public int? PageSize { get; set; } = null;

        /// <summary>
        /// Outcome filter: Passed, Failed, CouldNotRun or PendingEvaluation.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// CreatedDescending (default) or CreatedAscending.
        /// </summary>
        public string? Order { get; set; } = null;
    }
}
