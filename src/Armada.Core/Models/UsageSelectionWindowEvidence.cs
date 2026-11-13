namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>One normalized window and the separate conservative estimates evaluated at selection time.</summary>
    public sealed class UsageSelectionWindowEvidence
    {
        /// <summary>Sanitized observed provider window; never mutated by selection.</summary>
        public UsageWindow Window { get; set; } = new UsageWindow();

        /// <summary>Already admitted launch headroom, in percentage points.</summary>
        public double ActiveReservationPercent { get; set; } = 0;

        /// <summary>Estimated consumption awaiting a sufficiently recent capture.</summary>
        public double UnsettledPercent { get; set; } = 0;

        /// <summary>Whether a non-null provider scope has a verified match.</summary>
        public bool ScopeApplies { get; set; } = true;

        /// <summary>
        /// Whether the window's labels are intact (not sanitized or truncated) and unique within the observation, so its
        /// window key is a reliable identity. This is not a reset-generation proof: all v1.3.0 accounting uses the
        /// continuous generation, and fixed-window rollover is never inferred.
        /// </summary>
        public bool WindowIdentityConfirmed { get; set; } = true;
    }
}
