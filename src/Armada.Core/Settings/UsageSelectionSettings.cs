namespace Armada.Core.Settings
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Usage selection policy defaults and bounds. Observe is the safe rollout default; enforcement is explicit.
    /// Validate a complete settings object before using or publishing a new live revision.
    /// </summary>
    public sealed class UsageSelectionSettings
    {
        /// <summary>Selection strategy. Default FirstAvailable preserves existing routing preferences.</summary>
        public CaptainSelectionStrategyEnum Strategy { get; set; } = CaptainSelectionStrategyEnum.FirstAvailable;
        /// <summary>Rollout mode. Default Observe.</summary>
        public UsageSelectionModeEnum Mode { get; set; } = UsageSelectionModeEnum.Observe;
        /// <summary>Unknown evidence policy. Default AllowDeprioritized.</summary>
        public UsageUnknownBehaviorEnum UnknownUsageBehavior { get; set; } = UsageUnknownBehaviorEnum.AllowDeprioritized;
        /// <summary>Protected percentage points. Default 10; clamped to 0..95.</summary>
        public double ReservePercent { get => _ReservePercent; set => _ReservePercent = Bound(value, 0, 95, nameof(ReservePercent)); }
        /// <summary>Comfortable remaining percentage. Default 30; clamped to 1..100, greater than reserve.</summary>
        public double ComfortableHeadroomPercent { get => _ComfortableHeadroomPercent; set => _ComfortableHeadroomPercent = Bound(value, 1, 100, nameof(ComfortableHeadroomPercent)); }
        /// <summary>Launch estimate per window in percentage points. Default 2; clamped to 0.1..25.</summary>
        public double PerAttemptReservationPercent { get => _PerAttemptReservationPercent; set => _PerAttemptReservationPercent = Bound(value, 0.1, 25, nameof(PerAttemptReservationPercent)); }
        /// <summary>Freshness cap in seconds. Default 900; clamped to 120..86400; never loosens existing freshness.</summary>
        public int MaximumFreshnessSeconds { get => _MaximumFreshnessSeconds; set => _MaximumFreshnessSeconds = Math.Clamp(value, 120, 86400); }
        /// <summary>Retry delay for unknown evidence in seconds. Default 60; clamped to 5..3600.</summary>
        public int UnknownRetrySeconds { get => _UnknownRetrySeconds; set => _UnknownRetrySeconds = Math.Clamp(value, 5, 3600); }
        /// <summary>Fallback provider cooldown in seconds. Default 60; clamped to 5..3600.</summary>
        public int ProviderCooldownSeconds { get => _ProviderCooldownSeconds; set => _ProviderCooldownSeconds = Math.Clamp(value, 5, 3600); }
        /// <summary>Capture settlement lag after execution in seconds. Default 60; clamped to 0..3600.</summary>
        public int SettlementLagSeconds { get => _SettlementLagSeconds; set => _SettlementLagSeconds = Math.Clamp(value, 0, 3600); }
        /// <summary>Unsettled estimate age in seconds before it becomes uncertain. Default 3600; clamped to 60..86400.</summary>
        public int MaximumUnsettledSeconds { get => _MaximumUnsettledSeconds; set => _MaximumUnsettledSeconds = Math.Clamp(value, 60, 86400); }
        /// <summary>Decision retention in days. Default 90; clamped to 7..1825. Active attempts are always retained.</summary>
        public int DecisionRetentionDays { get => _DecisionRetentionDays; set => _DecisionRetentionDays = Math.Clamp(value, 7, 1825); }
        /// <summary>Complete candidate-set bound. Default 1000; clamped to 1..10000. Overflow never truncates admission.</summary>
        public int MaximumCandidates { get => _MaximumCandidates; set => _MaximumCandidates = Math.Clamp(value, 1, 10000); }

        private double _ReservePercent = 10;
        private double _ComfortableHeadroomPercent = 30;
        private double _PerAttemptReservationPercent = 2;
        private int _MaximumFreshnessSeconds = 900;
        private int _UnknownRetrySeconds = 60;
        private int _ProviderCooldownSeconds = 60;
        private int _SettlementLagSeconds = 60;
        private int _MaximumUnsettledSeconds = 3600;
        private int _DecisionRetentionDays = 90;
        private int _MaximumCandidates = 1000;

        /// <summary>Validate cross-field constraints and enum values before applying this policy.</summary>
        /// <exception cref="ArgumentException">The comfortable band does not exceed the protected reserve.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A mode or unknown policy value is unrecognized.</exception>
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(CaptainSelectionStrategyEnum), Strategy)) throw new ArgumentOutOfRangeException(nameof(Strategy), "Unknown captain selection strategy.");
            if (!Enum.IsDefined(typeof(UsageSelectionModeEnum), Mode)) throw new ArgumentOutOfRangeException(nameof(Mode), "Unknown usage selection mode.");
            if (!Enum.IsDefined(typeof(UsageUnknownBehaviorEnum), UnknownUsageBehavior)) throw new ArgumentOutOfRangeException(nameof(UnknownUsageBehavior), "Unknown usage evidence policy.");
            if (ComfortableHeadroomPercent <= ReservePercent) throw new ArgumentException("Comfortable headroom must exceed the protected reserve.", nameof(ComfortableHeadroomPercent));
        }

        /// <summary>Take a detached value-only snapshot for one evaluation/admission operation.</summary>
        public UsageSelectionSettings Copy()
        {
            UsageSelectionSettings copy = (UsageSelectionSettings)MemberwiseClone();
            copy.Validate();
            return copy;
        }

        private static double Bound(double value, double minimum, double maximum, string name)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "Usage settings require finite numeric values.");
            return Math.Clamp(value, minimum, maximum);
        }
    }
}
