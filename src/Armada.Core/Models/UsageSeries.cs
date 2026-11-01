namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>One independent account/window limit or token currency series.</summary>
    public class UsageSeries
    {
        /// <summary>Provider identity or samples.</summary>
        public string? Provider { get; set; } = null;

        /// <summary>AccountKey identity or samples.</summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>HostId identity or samples.</summary>
        public string? HostId { get; set; } = null;

        /// <summary>Runtime identity or samples.</summary>
        public string? Runtime { get; set; } = null;

        /// <summary>Window identity or samples.</summary>
        public string? Window { get; set; } = null;

        /// <summary>Model identity or samples.</summary>
        public string? Model { get; set; } = null;

        /// <summary>CostCurrency identity or samples.</summary>
        public string? CostCurrency { get; set; } = null;

        /// <summary>Points identity or samples.</summary>
        public List<UsageSeriesPoint> Points { get; set; } = new List<UsageSeriesPoint>();

    }
}
