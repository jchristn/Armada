namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Armada's estimated reservation against one observed limit window of an account: conservative launch headroom
    /// held for running attempts (active) and for ended attempts not yet settled by a fresh capture (unsettled). It is
    /// an Armada estimate, never provider-observed usage, and is never added to the observed percentage.
    /// </summary>
    public sealed class UsageReservationEstimate
    {
        /// <summary>Observed window name this estimate applies to.</summary>
        public string WindowName { get; set; } = String.Empty;

        /// <summary>Observed window length in minutes, when reported.</summary>
        public int? WindowMinutes { get; set; } = null;

        /// <summary>Window model scope, when model-specific.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Percentage points reserved for running attempts.</summary>
        public double ActivePercent { get; set; } = 0;

        /// <summary>Percentage points estimated for ended attempts awaiting settlement.</summary>
        public double UnsettledPercent { get; set; } = 0;
    }
}
