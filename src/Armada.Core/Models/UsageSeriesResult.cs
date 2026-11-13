namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>Bounded chart series without invented zero utilization.</summary>
    public class UsageSeriesResult
    {
        /// <summary>FromUtc for this response.</summary>
        public DateTime FromUtc { get; set; }

        /// <summary>ToUtc for this response.</summary>
        public DateTime ToUtc { get; set; }

        /// <summary>BucketMinutes for this response.</summary>
        public int BucketMinutes { get; set; } = 5;

        /// <summary>Resolution for this response.</summary>
        public string Resolution { get; set; } = "raw";

        /// <summary>GroupBy for this response.</summary>
        public string GroupBy { get; set; } = "account";

        /// <summary>Series for this response.</summary>
        [CollectionBound(100)]
        public List<UsageSeries> Series { get; set; } = new List<UsageSeries>();

        /// <summary>Coverage for this response.</summary>
        public UsageCoverage Coverage { get; set; } = new UsageCoverage();

        /// <summary>DrilldownFilters for this response.</summary>
        public UsageQuery? DrilldownFilters { get; set; } = null;

    }
}
