namespace Armada.Benchmark.Client
{
    using System.Text.Json.Serialization;

    /// <summary>The UsageSelection settings group, round-tripped whole because PUT replaces the supplied group.</summary>
    public sealed class BenchUsageSelection
    {
        /// <summary>Strategy name.</summary>
        [JsonPropertyName("strategy")]
        public string Strategy { get; set; } = "FirstAvailable";

        /// <summary>Mode name: Disabled, Observe or Enforce.</summary>
        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "Observe";

        /// <summary>Unknown-usage behavior name.</summary>
        [JsonPropertyName("unknownUsageBehavior")]
        public string UnknownUsageBehavior { get; set; } = "AllowDeprioritized";

        /// <summary>Protected reserve percent.</summary>
        [JsonPropertyName("reservePercent")]
        public double ReservePercent { get; set; }

        /// <summary>Comfortable headroom percent.</summary>
        [JsonPropertyName("comfortableHeadroomPercent")]
        public double ComfortableHeadroomPercent { get; set; }

        /// <summary>Per-attempt reservation percent.</summary>
        [JsonPropertyName("perAttemptReservationPercent")]
        public double PerAttemptReservationPercent { get; set; }

        /// <summary>Maximum evidence age in seconds.</summary>
        [JsonPropertyName("maximumFreshnessSeconds")]
        public int MaximumFreshnessSeconds { get; set; }

        /// <summary>Retry interval for unknown usage.</summary>
        [JsonPropertyName("unknownRetrySeconds")]
        public int UnknownRetrySeconds { get; set; }

        /// <summary>Default provider cooldown.</summary>
        [JsonPropertyName("providerCooldownSeconds")]
        public int ProviderCooldownSeconds { get; set; }

        /// <summary>Settlement lag.</summary>
        [JsonPropertyName("settlementLagSeconds")]
        public int SettlementLagSeconds { get; set; }

        /// <summary>Maximum unsettled age.</summary>
        [JsonPropertyName("maximumUnsettledSeconds")]
        public int MaximumUnsettledSeconds { get; set; }

        /// <summary>Decision retention.</summary>
        [JsonPropertyName("decisionRetentionDays")]
        public int DecisionRetentionDays { get; set; }

        /// <summary>Candidate bound.</summary>
        [JsonPropertyName("maximumCandidates")]
        public int MaximumCandidates { get; set; }
    }
}
