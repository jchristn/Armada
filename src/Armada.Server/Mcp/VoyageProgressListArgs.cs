namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for list_voyage_progress.
    /// </summary>
    public class VoyageProgressListArgs
    {
        /// <summary>
        /// Page number (1-based). Default 1.
        /// </summary>
        public int? PageNumber { get; set; }

        /// <summary>
        /// Page size, 1 to 1000 (out-of-range values are normalized). Default 10.
        /// </summary>
        public int? PageSize { get; set; }

        /// <summary>
        /// Optional voyage status. Null for the active voyages (InProgress, then Open).
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// CreatedDescending (default) or CreatedAscending.
        /// </summary>
        public string? Order { get; set; }
    }
}
