namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Bounded usage chart and completion drilldown query. TenantId must be supplied by the server.</summary>
    public class UsageQuery : UsageSnapshotQuery
    {
        /// <summary>HostId filter or query option.</summary>
        public string? HostId { get; set; } = null;

        /// <summary>Window filter or query option.</summary>
        public string? Window { get; set; } = null;

        /// <summary>Model filter or query option.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Metric filter or query option.</summary>
        public string Metric { get; set; } = "limits";

        /// <summary>GroupBy filter or query option.</summary>
        public string GroupBy { get; set; } = "account";

        /// <summary>BucketMinutes filter or query option.</summary>
        public int BucketMinutes { get; set; } = 5;

        /// <summary>SortBy filter or query option.</summary>
        public string SortBy { get; set; } = "totalTokens";

        /// <summary>SortDirection filter or query option.</summary>
        public string SortDirection { get; set; } = "descending";

    }
}
