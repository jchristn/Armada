namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Text.Json;
    using Armada.Core.Enums;

    /// <summary>Shared semantic English fallback catalog for web and native selection inspection. Thread safe.</summary>
    public static class UsageSelectionText
    {
        private static readonly IReadOnlyDictionary<string, string> _Messages = Load();
        /// <summary>Resolve a semantic key without exposing a missing key as a fabricated explanation.</summary>
        public static string Get(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return _Messages.TryGetValue(key, out string? message) ? message : key;
        }

        /// <summary>Resolve the stable localizable explanation key for an admission reason.</summary>
        public static string ReasonKey(string? code) => code switch
        {
            "PlacementWaiting" => "usageSelection.reason.placement",
            "LaunchRetryPending" => "usageSelection.reason.retry",
            "VoyageNotRunnable" => "usageSelection.reason.voyage",
            "SelectedForHeadroom" => "usageSelection.reason.selected",
            "SufficientHeadroom" => "usageSelection.reason.headroom",
            "BelowReserve" => "usageSelection.reason.reserve",
            "AccountQuotaExhausted" => "usageSelection.reason.exhausted",
            "AccountCooldown" => "usageSelection.reason.cooldown",
            "UsageIdentityUnknown" => "usageSelection.reason.identity",
            "UsageConfigurationChanged" => "usageSelection.reason.configuration",
            "UsageMissing" => "usageSelection.reason.missing",
            "FutureObservation" => "usageSelection.reason.future",
            "UsageStale" => "usageSelection.reason.stale",
            "UsageNotAuthoritative" => "usageSelection.reason.authority",
            "UsageUncertainDebt" => "usageSelection.reason.debt",
            "NoApplicableWindows" => "usageSelection.reason.windows",
            "UsageEvidenceInvalid" => "usageSelection.reason.invalid",
            "UsageModelUnknown" => "usageSelection.reason.model",
            "UsageScopeUnknown" => "usageSelection.reason.scope",
            "AwaitingFreshUsage" => "usageSelection.reason.reset",
            "NoEligibleCaptain" => "usageSelection.reason.eligible",
            "CaptainConcurrencyLimit" => "usageSelection.reason.capacity",
            "CandidateLimitExceeded" => "usageSelection.reason.limit",
            "SelectionPersistenceUnavailable" => "usageSelection.reason.persistence",
            "UsageHostMismatch" => "usageSelection.reason.host",
            "UsageHostUnresolved" => "usageSelection.reason.hostUnknown",
            "MissionScopeUnavailable" => "usageSelection.reason.missionScope",
            "MissionNotPending" => "usageSelection.reason.notPending",
            "VesselMissing" => "usageSelection.reason.vessel",
            "VesselMisconfigured" => "usageSelection.reason.vesselConfiguration",
            "RequestedCaptainInactive" => "usageSelection.reason.inactive",
            "CaptainInactive" => "usageSelection.reason.inactive",
            "CaptainQuarantined" => "usageSelection.reason.quarantine",
            "ReviewAuthorExcluded" => "usageSelection.reason.review",
            "CaptainScopeMismatch" => "usageSelection.reason.ownership",
            "WorkerEnvelopeIncompatible" => "usageSelection.reason.envelope",
            "RoutingPreferenceExcluded" => "usageSelection.reason.routing",
            "AuthorizedReserveException" => "usageSelection.reason.exception",
            _ => "usageSelection.reason.routing"
        };

        /// <summary>Resolve the English explanation used by CLI output; native UI translates ReasonKey instead.</summary>
        public static string ForReason(string? code) => Get(ReasonKey(code));

        /// <summary>Resolve a human-readable English strategy label.</summary>
        public static string ForStrategy(CaptainSelectionStrategyEnum strategy) => Get(strategy switch
        {
            CaptainSelectionStrategyEnum.MostRemainingAllowance => "usageSelection.mostRemaining",
            CaptainSelectionStrategyEnum.MostUsedAllowance => "usageSelection.mostUsed",
            _ => "usageSelection.firstAvailable"
        });

        private static IReadOnlyDictionary<string, string> Load()
        {
            using Stream resource = typeof(UsageSelectionText).Assembly.GetManifestResourceStream("Armada.Core.UsageSelectionMessages.json")
                ?? throw new InvalidOperationException("The shared usage selection English catalog is missing from Armada.Core.");
            Dictionary<string, string> messages = JsonSerializer.Deserialize<Dictionary<string, string>>(resource)
                ?? throw new InvalidOperationException("The shared usage selection English catalog is invalid.");
            return new ReadOnlyDictionary<string, string>(messages);
        }
    }
}
