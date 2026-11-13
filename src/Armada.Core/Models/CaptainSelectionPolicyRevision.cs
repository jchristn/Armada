namespace Armada.Core.Models
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Core.Settings;

    /// <summary>Typed relational captain_selection_policy_revisions record; managed fields are never stored as a JSON document.</summary>
    public sealed class CaptainSelectionPolicyRevision : CaptainSelectionRecord
    {
        /// <summary>Fingerprint captured as fingerprint in the selection ledger.</summary>
        public string Fingerprint { get; set; } = String.Empty;
        /// <summary>AlgorithmVersion captured as algorithm_version in the selection ledger.</summary>
        public string AlgorithmVersion { get; set; } = String.Empty;
        /// <summary>Mode captured as mode in the selection ledger.</summary>
        public UsageSelectionModeEnum Mode { get; set; }
        /// <summary>Strategy captured as strategy in the selection ledger.</summary>
        public CaptainSelectionStrategyEnum Strategy { get; set; }
        /// <summary>UnknownUsageBehavior captured as unknown_usage_behavior in the selection ledger.</summary>
        public UsageUnknownBehaviorEnum UnknownUsageBehavior { get; set; }
        /// <summary>ReservePercent captured as reserve_percent in the selection ledger.</summary>
        public double ReservePercent { get; set; }
        /// <summary>ComfortableHeadroomPercent captured as comfortable_headroom_percent in the selection ledger.</summary>
        public double ComfortableHeadroomPercent { get; set; }
        /// <summary>PerAttemptReservationPercent captured as per_attempt_reservation_percent in the selection ledger.</summary>
        public double PerAttemptReservationPercent { get; set; }
        /// <summary>MaximumFreshnessSeconds captured as maximum_freshness_seconds in the selection ledger.</summary>
        public int MaximumFreshnessSeconds { get; set; }
        /// <summary>UnknownRetrySeconds captured as unknown_retry_seconds in the selection ledger.</summary>
        public int UnknownRetrySeconds { get; set; }
        /// <summary>ProviderCooldownSeconds captured as provider_cooldown_seconds in the selection ledger.</summary>
        public int ProviderCooldownSeconds { get; set; }
        /// <summary>SettlementLagSeconds captured as settlement_lag_seconds in the selection ledger.</summary>
        public int SettlementLagSeconds { get; set; }
        /// <summary>MaximumUnsettledSeconds captured as maximum_unsettled_seconds in the selection ledger.</summary>
        public int MaximumUnsettledSeconds { get; set; }
        /// <summary>DecisionRetentionDays captured as decision_retention_days in the selection ledger.</summary>
        public int DecisionRetentionDays { get; set; }
        /// <summary>MaximumCandidates captured as maximum_candidates in the selection ledger.</summary>
        public int MaximumCandidates { get; set; }
        /// <summary>Create an immutable effective policy snapshot and canonical fingerprint from validated settings.</summary>
        public static CaptainSelectionPolicyRevision FromSettings(string tenantId, UsageSelectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (String.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant scope is required.", nameof(tenantId));
            settings.Validate();
            return new CaptainSelectionPolicyRevision
            {
                TenantId = tenantId,
                AlgorithmVersion = UsageSelectionPolicy.AlgorithmVersion,
                Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(UsageSelectionPolicy.AlgorithmVersion + ":" + JsonSerializer.Serialize(settings)))).ToLowerInvariant(),
                Mode = settings.Mode,
                Strategy = settings.Strategy,
                UnknownUsageBehavior = settings.UnknownUsageBehavior,
                ReservePercent = settings.ReservePercent,
                ComfortableHeadroomPercent = settings.ComfortableHeadroomPercent,
                PerAttemptReservationPercent = settings.PerAttemptReservationPercent,
                MaximumFreshnessSeconds = settings.MaximumFreshnessSeconds,
                UnknownRetrySeconds = settings.UnknownRetrySeconds,
                ProviderCooldownSeconds = settings.ProviderCooldownSeconds,
                SettlementLagSeconds = settings.SettlementLagSeconds,
                MaximumUnsettledSeconds = settings.MaximumUnsettledSeconds,
                DecisionRetentionDays = settings.DecisionRetentionDays,
                MaximumCandidates = settings.MaximumCandidates,
            };
        }

        /// <summary>Reconstitute the typed effective settings, including the configured strategy.</summary>
        public UsageSelectionSettings ToSettings()
        {
            return new UsageSelectionSettings
            {
                Mode = Mode,
                Strategy = Strategy,
                UnknownUsageBehavior = UnknownUsageBehavior,
                ReservePercent = ReservePercent,
                ComfortableHeadroomPercent = ComfortableHeadroomPercent,
                PerAttemptReservationPercent = PerAttemptReservationPercent,
                MaximumFreshnessSeconds = MaximumFreshnessSeconds,
                UnknownRetrySeconds = UnknownRetrySeconds,
                ProviderCooldownSeconds = ProviderCooldownSeconds,
                SettlementLagSeconds = SettlementLagSeconds,
                MaximumUnsettledSeconds = MaximumUnsettledSeconds,
                DecisionRetentionDays = DecisionRetentionDays,
                MaximumCandidates = MaximumCandidates,
            };
        }

        /// <summary>Create an identified CaptainSelectionPolicyRevision record.</summary>
        public CaptainSelectionPolicyRevision() : base("csp_") { }
    }
}
