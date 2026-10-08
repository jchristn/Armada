namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Query and scope parameters for token-usage storage and APIs.
    /// </summary>
    public class TokenUsageQuery
    {
        #region Public-Members

        /// <summary>
        /// Tenant scope or filter.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User scope or filter.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Model filter (exact match).
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Runtime filter (exact match).
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Source filter ("mission", "chat", or "planning").
        /// </summary>
        public string? Source { get; set; } = null;

        /// <summary>
        /// Vessel filter.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Captain filter.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Harbor filter: records of work that ran on this Harbor.
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Lower bound on creation timestamp.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Upper bound on creation timestamp.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// One-based page number.
        /// </summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Page size.
        /// </summary>
        public int PageSize { get; set; } = 25;

        /// <summary>
        /// Summary bucket width in minutes. Fractional values are allowed (for example 0.5 for
        /// 30-second buckets).
        /// </summary>
        public double BucketMinutes { get; set; } = 15;

        /// <summary>
        /// Calculated zero-based offset.
        /// </summary>
        public int Offset => PageNumber <= 1 ? 0 : (PageNumber - 1) * PageSize;

        #endregion
    }
}
