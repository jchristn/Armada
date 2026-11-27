namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of get_reliability_summary.
    /// </summary>
    public class ReliabilitySummaryArgs
    {
        /// <summary>
        /// Tenant filter (global admins only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Model filter.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Persona filter.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Vessel filter.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Include battery and evaluation attempts.
        /// </summary>
        public bool? IncludeEvaluation { get; set; } = null;

        /// <summary>
        /// Window start (ISO 8601 UTC).
        /// </summary>
        public string? FromUtc { get; set; } = null;

        /// <summary>
        /// Window end (ISO 8601 UTC).
        /// </summary>
        public string? ToUtc { get; set; } = null;

        /// <summary>
        /// Sort field.
        /// </summary>
        public string? Sort { get; set; } = null;

        /// <summary>
        /// Sort descending.
        /// </summary>
        public bool? Descending { get; set; } = null;

        /// <summary>
        /// Page number.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size.
        /// </summary>
        public int? PageSize { get; set; } = null;
    }
}
