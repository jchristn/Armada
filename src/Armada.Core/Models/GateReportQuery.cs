namespace Armada.Core.Models
{
    /// <summary>
    /// Paging for stored gate reports (POST /api/v1/reliability/gate-reports/enumerate).
    /// </summary>
    public class GateReportQuery
    {
        /// <summary>
        /// Page number, or null for 1.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size (1 to 100), or null for 10.
        /// </summary>
        public int? PageSize { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public GateReportQuery()
        {
        }
    }
}
