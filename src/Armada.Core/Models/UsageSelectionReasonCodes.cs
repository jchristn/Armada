namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Stable semantic selection reason codes. Codes are persisted in the ledger and used as translation keys by
    /// every client, so values must never be renamed. Clients localize display text; Armada never stores it.
    /// </summary>
    public static class UsageSelectionReasonCodes
    {
        /// <summary>A captain was selected with sufficient headroom.</summary>
        public const string SelectedForHeadroom = "SelectedForHeadroom";

        /// <summary>Observe admitted the captain existing routing chose; the configured strategy only recorded its own choice.</summary>
        public const string SelectedByRouting = "SelectedByRouting";

        /// <summary>An administrator's recovery permission let an eligible captain run instead of the preferred one.</summary>
        public const string AuthorizedFallback = "AuthorizedFallback";

        /// <summary>The candidate has sufficient launch headroom.</summary>
        public const string SufficientHeadroom = "SufficientHeadroom";

        /// <summary>The candidate is selectable only through an authorized soft-reserve exception.</summary>
        public const string AuthorizedReserveException = "AuthorizedReserveException";

        /// <summary>No candidate was supplied for evaluation.</summary>
        public const string NoEligibleCaptain = "NoEligibleCaptain";

        /// <summary>More candidates than MaximumCandidates; nothing is selected from a truncated set.</summary>
        public const string CandidateLimitExceeded = "CandidateLimitExceeded";

        /// <summary>The captain has no free mission slot.</summary>
        public const string CaptainConcurrencyLimit = "CaptainConcurrencyLimit";

        /// <summary>The candidate is outside the mission's allowed routing preference buckets.</summary>
        public const string RoutingPreferenceExcluded = "RoutingPreferenceExcluded";

        /// <summary>An authoritative quota hold or a proven exhausted window blocks the account.</summary>
        public const string AccountQuotaExhausted = "AccountQuotaExhausted";

        /// <summary>A classified provider cooldown blocks the account.</summary>
        public const string AccountCooldown = "AccountCooldown";

        /// <summary>Headroom is below the protected reserve plus the per-attempt reservation.</summary>
        public const string BelowReserve = "BelowReserve";

        /// <summary>The captain's account identity is not verified.</summary>
        public const string UsageIdentityUnknown = "UsageIdentityUnknown";

        /// <summary>The capture configuration changed since the latest evidence.</summary>
        public const string UsageConfigurationChanged = "UsageConfigurationChanged";

        /// <summary>No usage observation exists for the account.</summary>
        public const string UsageMissing = "UsageMissing";

        /// <summary>The observation is timestamped too far in the future.</summary>
        public const string FutureObservation = "FutureObservation";

        /// <summary>The latest observation is older than the effective freshness horizon.</summary>
        public const string UsageStale = "UsageStale";

        /// <summary>The latest observation failed, was derived or has an unsupported source.</summary>
        public const string UsageNotAuthoritative = "UsageNotAuthoritative";

        /// <summary>Unsettled debt aged past MaximumUnsettledSeconds.</summary>
        public const string UsageUncertainDebt = "UsageUncertainDebt";

        /// <summary>No applicable window could be evaluated.</summary>
        public const string NoApplicableWindows = "NoApplicableWindows";

        /// <summary>A window or estimate was malformed, non-finite or negative.</summary>
        public const string UsageEvidenceInvalid = "UsageEvidenceInvalid";

        /// <summary>A model-scoped window exists but the captain's model is unknown.</summary>
        public const string UsageModelUnknown = "UsageModelUnknown";

        /// <summary>A window has a scope that is not proven to apply.</summary>
        public const string UsageScopeUnknown = "UsageScopeUnknown";

        /// <summary>A window reset passed or its identity is unconfirmed; a fresh capture is required.</summary>
        public const string AwaitingFreshUsage = "AwaitingFreshUsage";

        /// <summary>The decision ledger could not be written; no process is started.</summary>
        public const string SelectionPersistenceUnavailable = "SelectionPersistenceUnavailable";

        /// <summary>The chosen captain is waiting on dock or host placement.</summary>
        public const string PlacementWaiting = "PlacementWaiting";

        /// <summary>The mission is not pending.</summary>
        public const string MissionNotPending = "MissionNotPending";

        /// <summary>The mission has no tenant scope.</summary>
        public const string MissionScopeUnavailable = "MissionScopeUnavailable";

        /// <summary>A launch retry backoff is pending.</summary>
        public const string LaunchRetryPending = "LaunchRetryPending";

        /// <summary>The mission's voyage is complete, cancelled or failed.</summary>
        public const string VoyageNotRunnable = "VoyageNotRunnable";

        /// <summary>The mission's vessel no longer exists.</summary>
        public const string VesselMissing = "VesselMissing";

        /// <summary>The vessel shares its local and working directory.</summary>
        public const string VesselMisconfigured = "VesselMisconfigured";

        /// <summary>A preferred captain cannot run and fallback needs an administrator's recovery permission.</summary>
        public const string FallbackPermissionRequired = "FallbackPermissionRequired";

        /// <summary>
        /// Waiting reasons that do not clear on their own and need an operator: account identity, capture configuration
        /// or capture failures, the candidate limit, and a needed fallback permission. Reserve, quota, cooldown and capacity
        /// waits are excluded because time or running work resolves them.
        /// </summary>
        public static IReadOnlyList<string> OperatorActionable { get; } = new List<string>
        {
            UsageIdentityUnknown, UsageConfigurationChanged, UsageMissing, UsageStale, UsageNotAuthoritative,
            UsageEvidenceInvalid, UsageModelUnknown, UsageScopeUnknown, CandidateLimitExceeded, FallbackPermissionRequired,
            "UsageHostMismatch", "UsageHostUnresolved"
        }.AsReadOnly();
    }
}
