namespace Armada.Core.Models
{
    /// <summary>
    /// Generic query payload for focused tunnel requests.
    /// </summary>
    public class RemoteTunnelQueryRequest
    {
        #region Public-Members

        /// <summary>
        /// Optional fleet identifier.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Optional vessel identifier.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Optional mission identifier.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Optional voyage identifier.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Optional captain identifier.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Optional playbook identifier.
        /// </summary>
        public string? PlaybookId { get; set; } = null;

        /// <summary>
        /// Optional status filter.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Maximum number of rows to return for recent-activity style queries.
        /// </summary>
        public int Limit { get; set; } = 0;

        /// <summary>
        /// Starting offset for log pagination.
        /// </summary>
        public int Offset { get; set; } = 0;

        /// <summary>
        /// Number of log lines to return.
        /// </summary>
        public int Lines { get; set; } = 0;

        #endregion
    }
}
