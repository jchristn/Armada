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

        /// <summary>Whether this observation belongs to a verified current window generation.</summary>
        public bool GenerationConfirmed { get; set; } = true;
    }
}
