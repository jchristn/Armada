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

        /// <summary>Exact originating work or synthetic batch identifier filter.</summary>
        public string? SourceId { get; set; } = null;

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
        /// Most models a summary breaks out by name, in <see cref="TokenUsageSummaryResult.ByModel"/> and in each
        /// bucket's Models. The remaining models are summed into an explicit OtherModels aggregate, never dropped.
        /// Default <see cref="DefaultModelLimit"/>, minimum 1, maximum <see cref="MaxModelLimit"/>; values outside the
        /// range are normalized to the nearest bound.
        /// </summary>
        public int ModelLimit
        {
            get => _ModelLimit;
            set => _ModelLimit = value < 1 ? 1 : (value > MaxModelLimit ? MaxModelLimit : value);
        }

        /// <summary>
        /// Default for <see cref="ModelLimit"/>.
        /// </summary>
        public const int DefaultModelLimit = 10;

        /// <summary>
        /// Largest <see cref="ModelLimit"/>.
        /// </summary>
        public const int MaxModelLimit = 100;

        /// <summary>
        /// Calculated zero-based offset.
        /// </summary>
        public int Offset => PageNumber <= 1 ? 0 : (PageNumber - 1) * PageSize;

        #endregion

        #region Private-Members

        private int _ModelLimit = DefaultModelLimit;

        #endregion
    }
}
