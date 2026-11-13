namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Observed bucket, with nullable quota and cost values.</summary>
    public class UsageSeriesPoint
    {
        /// <summary>BucketStartUtc for this bucket.</summary>
        public DateTime BucketStartUtc { get; set; }

        /// <summary>BucketEndUtc for this bucket.</summary>
        public DateTime BucketEndUtc { get; set; }

        /// <summary>LastPercent for this bucket.</summary>
        public double? LastPercent { get; set; } = null;

        /// <summary>MinPercent for this bucket.</summary>
        public double? MinPercent { get; set; } = null;

        /// <summary>MaxPercent for this bucket.</summary>
        public double? MaxPercent { get; set; } = null;

        /// <summary>MeanPercent for this bucket.</summary>
        public double? MeanPercent { get; set; } = null;

        /// <summary>LastResetUtc for this bucket.</summary>
        public DateTime? LastResetUtc { get; set; } = null;

        /// <summary>ObservedUtc for this bucket.</summary>
        public DateTime? ObservedUtc { get; set; } = null;

        /// <summary>ObservationAgeSeconds for this bucket.</summary>
        public double? ObservationAgeSeconds { get; set; } = null;

        /// <summary>ObservationCount for this bucket.</summary>
        public int ObservationCount { get; set; } = 0;

        /// <summary>Status for this bucket.</summary>
        public string? Status { get; set; } = null;

        /// <summary>InputTokens for this bucket.</summary>
        public long InputTokens { get; set; } = 0;

        /// <summary>OutputTokens for this bucket.</summary>
        public long OutputTokens { get; set; } = 0;

        /// <summary>CachedTokens for this bucket.</summary>
        public long CachedTokens { get; set; } = 0;

        /// <summary>TotalTokens for this bucket.</summary>
        public long TotalTokens { get; set; } = 0;

        /// <summary>ReportedCost for this bucket.</summary>
        public double? ReportedCost { get; set; } = null;

        /// <summary>CostCurrency for this bucket.</summary>
        public string? CostCurrency { get; set; } = null;

        /// <summary>Estimated for this bucket.</summary>
        public bool Estimated { get; set; } = false;

    }
}
