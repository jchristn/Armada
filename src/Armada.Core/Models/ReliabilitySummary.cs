namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Pipeline reliability per model and persona over a window (GET /api/v1/reliability/summary, MCP
    /// get_reliability_summary): completion, failure kinds, repairs, escalations and the Unclassified rate, filtered,
    /// sorted and paged on the server.
    /// </summary>
    public class ReliabilitySummary
    {
        /// <summary>
        /// Start of the window.
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End of the window.
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Stage outcomes read.
        /// </summary>
        public int OutcomesRead { get; set; } = 0;

        /// <summary>
        /// True when the window held more than <see cref="ReliabilitySummaryQuery.MaxOutcomes"/> outcomes and only the
        /// newest were counted.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Totals over every row (Model and Persona are "(all)").
        /// </summary>
        public ReliabilitySummaryRow Totals { get; set; } = new ReliabilitySummaryRow();

        /// <summary>
        /// Page number.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size.
        /// </summary>
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// Rows across all pages.
        /// </summary>
        public int TotalRecords { get; set; } = 0;

        /// <summary>
        /// Pages.
        /// </summary>
        public int TotalPages { get; set; } = 0;

        /// <summary>
        /// This page's rows.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxPageSize, Reason = "One page of rows")]
        public List<ReliabilitySummaryRow> Rows { get; set; } = new List<ReliabilitySummaryRow>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReliabilitySummary()
        {
        }
    }
}
