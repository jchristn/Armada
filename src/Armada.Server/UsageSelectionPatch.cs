namespace Armada.Server
{
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// The members of a <c>UsageSelection</c> settings update that the caller actually sent. Omitted members keep their
    /// current values, so sending only <c>Mode</c> never resets the strategy or bounds.
    /// </summary>
    public sealed class UsageSelectionPatch
    {
        #region Public-Members

        /// <summary>Strategy, when sent.</summary>
        public CaptainSelectionStrategyEnum? Strategy { get; set; }

        /// <summary>Mode, when sent.</summary>
        public UsageSelectionModeEnum? Mode { get; set; }

        /// <summary>Unknown-usage behavior, when sent.</summary>
        public UsageUnknownBehaviorEnum? UnknownUsageBehavior { get; set; }

        /// <summary>Protected reserve percent, when sent.</summary>
        public double? ReservePercent { get; set; }

        /// <summary>Comfortable headroom percent, when sent.</summary>
        public double? ComfortableHeadroomPercent { get; set; }

        /// <summary>Per-attempt reservation percent, when sent.</summary>
        public double? PerAttemptReservationPercent { get; set; }

        /// <summary>Maximum evidence age in seconds, when sent.</summary>
        public int? MaximumFreshnessSeconds { get; set; }

        /// <summary>Unknown-usage retry seconds, when sent.</summary>
        public int? UnknownRetrySeconds { get; set; }

        /// <summary>Default provider cooldown seconds, when sent.</summary>
        public int? ProviderCooldownSeconds { get; set; }

        /// <summary>Settlement lag seconds, when sent.</summary>
        public int? SettlementLagSeconds { get; set; }

        /// <summary>Maximum unsettled seconds, when sent.</summary>
        public int? MaximumUnsettledSeconds { get; set; }

        /// <summary>Decision retention days, when sent.</summary>
        public int? DecisionRetentionDays { get; set; }

        /// <summary>Candidate bound, when sent.</summary>
        public int? MaximumCandidates { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>Return a copy of <paramref name="current"/> with every sent member applied.</summary>
        /// <param name="current">Current effective policy.</param>
        /// <returns>The merged policy.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="current"/> is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown when a sent number is outside its bounds.</exception>
        public UsageSelectionSettings ApplyTo(UsageSelectionSettings current)
        {
            if (current == null) throw new System.ArgumentNullException(nameof(current));
            UsageSelectionSettings merged = current.Copy();
            if (Strategy.HasValue) merged.Strategy = Strategy.Value;
            if (Mode.HasValue) merged.Mode = Mode.Value;
            if (UnknownUsageBehavior.HasValue) merged.UnknownUsageBehavior = UnknownUsageBehavior.Value;
            if (ReservePercent.HasValue) merged.ReservePercent = ReservePercent.Value;
            if (ComfortableHeadroomPercent.HasValue) merged.ComfortableHeadroomPercent = ComfortableHeadroomPercent.Value;
            if (PerAttemptReservationPercent.HasValue) merged.PerAttemptReservationPercent = PerAttemptReservationPercent.Value;
            if (MaximumFreshnessSeconds.HasValue) merged.MaximumFreshnessSeconds = MaximumFreshnessSeconds.Value;
            if (UnknownRetrySeconds.HasValue) merged.UnknownRetrySeconds = UnknownRetrySeconds.Value;
            if (ProviderCooldownSeconds.HasValue) merged.ProviderCooldownSeconds = ProviderCooldownSeconds.Value;
            if (SettlementLagSeconds.HasValue) merged.SettlementLagSeconds = SettlementLagSeconds.Value;
            if (MaximumUnsettledSeconds.HasValue) merged.MaximumUnsettledSeconds = MaximumUnsettledSeconds.Value;
            if (DecisionRetentionDays.HasValue) merged.DecisionRetentionDays = DecisionRetentionDays.Value;
            if (MaximumCandidates.HasValue) merged.MaximumCandidates = MaximumCandidates.Value;
            return merged;
        }

        #endregion
    }
}
