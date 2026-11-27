namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Filters, sort and paging of the reliability summary (GET /api/v1/reliability/summary).
    /// </summary>
    public class ReliabilitySummaryQuery
    {
        /// <summary>
        /// Longest window in days (90).
        /// </summary>
        public const int MaxWindowDays = 90;

        /// <summary>
        /// Most stage outcomes read for one summary (50000). A wider window is cut to the newest ones and the summary is
        /// marked truncated.
        /// </summary>
        public const int MaxOutcomes = 50000;

        /// <summary>
        /// Tenant filter (global admins only; ignored otherwise).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Model filter (exact, case-insensitive), or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Persona filter (exact, case-insensitive), or null.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Vessel filter (vsl_ prefix), or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Include battery and evaluation attempts. Default false (production attempts only).
        /// </summary>
        public bool IncludeEvaluation { get; set; } = false;

        /// <summary>
        /// Start of the window (attempts ended at or after), or null for 14 days before <see cref="ToUtc"/>.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// End of the window (attempts ended before), or null for now.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Sort field. Default Attempts.
        /// </summary>
        public ReliabilitySortEnum Sort { get; set; } = ReliabilitySortEnum.Attempts;

        /// <summary>
        /// True to sort descending (default true).
        /// </summary>
        public bool Descending { get; set; } = true;

        /// <summary>
        /// Page number (default 1).
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size (1 to 1000, default 50).
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReliabilitySummaryQuery()
        {
        }
    }
}
