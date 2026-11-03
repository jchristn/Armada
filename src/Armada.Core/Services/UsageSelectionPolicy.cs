namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Pure deterministic usage ranking inside precomputed routing buckets. No I/O, mutation, claims or probes.
    /// Thread safe when callers do not mutate the supplied settings/evidence during evaluation.
    /// </summary>
    public static class UsageSelectionPolicy
    {
        /// <summary>Algorithm identifier stored with every future durable decision.</summary>
        public const string AlgorithmVersion = "usage-bands-v1";

        /// <summary>Evaluate an entire bounded eligible-candidate set using the supplied UTC clock.</summary>
        /// <param name="candidates">Already scoped candidates with existing routing/safety fences.</param>
        /// <param name="settings">Validated effective policy.</param>
        /// <param name="nowUtc">Evaluation time; never read from the system clock here.</param>
        /// <param name="allowSoftReserveException">An already-authorized exception to the protected reserve only.</param>
        /// <returns>A hypothetical enforced outcome regardless of rollout mode.</returns>
        /// <exception cref="ArgumentNullException">Candidates or settings are null.</exception>
        /// <exception cref="ArgumentException">Input IDs are missing/duplicated or settings are inconsistent.</exception>
        public static UsageSelectionEvaluation Evaluate(IReadOnlyList<UsageSelectionCandidate> candidates,
            UsageSelectionSettings settings, DateTime nowUtc, bool allowSoftReserveException = false)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            UsageSelectionEvaluation evaluation = new UsageSelectionEvaluation();
            if (candidates.Count > settings.MaximumCandidates)
            {
                evaluation.CandidateLimitExceeded = true;
                evaluation.ReasonCode = "CandidateLimitExceeded";
                return evaluation;
            }
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (UsageSelectionCandidate candidate in candidates)
            {
                if (candidate == null || String.IsNullOrWhiteSpace(candidate.CaptainId) || !ids.Add(candidate.CaptainId))
                    throw new ArgumentException("Selection candidates must have distinct nonempty captain IDs.", nameof(candidates));
                evaluation.Candidates.Add(EvaluateCandidate(candidate, settings, nowUtc, allowSoftReserveException));
            }

            // A pairwise comparable-window comparator can be non-transitive when a third provider is present.
            // Compare percentages only inside a whole equal-priority/band group with one common semantic key.
            List<UsageSelectionCandidateResult> ordered = new List<UsageSelectionCandidateResult>();
            foreach (IEnumerable<UsageSelectionCandidateResult> group in evaluation.Candidates
                .GroupBy(r => new { r.Candidate.RoutingBucket, Band = BandOrder(r.Band, settings.Strategy) })
                .OrderBy(g => g.Key.RoutingBucket).ThenBy(g => g.Key.Band))
            {
                List<UsageSelectionCandidateResult> rows = group.ToList();
                string? key = rows[0].ComparableWindowKey;
                bool comparable = settings.Strategy != CaptainSelectionStrategyEnum.FirstAvailable && key != null && rows.All(r => r.ComparableWindowKey == key && r.AvailablePercent.HasValue);
                ordered.AddRange(rows.OrderBy(r => settings.Strategy == CaptainSelectionStrategyEnum.FirstAvailable ? r.Candidate.FirstAvailableRank : 0)
                    .ThenBy(r => comparable ? (settings.Strategy == CaptainSelectionStrategyEnum.MostUsedAllowance ? r.AvailablePercent : -r.AvailablePercent) : null)
                    .ThenBy(r => settings.Strategy == CaptainSelectionStrategyEnum.FirstAvailable ? 0 : Load(r.Candidate))
                    .ThenBy(r => r.Candidate.LastAssignedUtc ?? DateTime.MaxValue)
                    .ThenBy(r => r.Candidate.CaptainId, StringComparer.Ordinal));
            }
            evaluation.Candidates = ordered;
            evaluation.SelectedCaptainId = ordered.FirstOrDefault(r => r.CanSelect)?.Candidate.CaptainId;
            evaluation.ReasonCode = evaluation.SelectedCaptainId != null ? "SelectedForHeadroom"
                : ordered.Count == 0 ? "NoEligibleCaptain" : ordered[0].ReasonCode;
            return evaluation;
        }

        private static int BandOrder(UsageSelectionBandEnum band, CaptainSelectionStrategyEnum strategy)
        {
            if (strategy == CaptainSelectionStrategyEnum.FirstAvailable) return band == UsageSelectionBandEnum.Blocked ? 1 : 0;
            if (strategy == CaptainSelectionStrategyEnum.MostUsedAllowance)
            {
                if (band == UsageSelectionBandEnum.Constrained) return 0;
                if (band == UsageSelectionBandEnum.Comfortable) return 1;
            }
            return (int)band;
        }

        private static double Load(UsageSelectionCandidate candidate)
            => (double)Math.Max(0, candidate.OccupiedSlots) / Math.Max(1, candidate.Capacity);

        private static UsageSelectionCandidateResult EvaluateCandidate(UsageSelectionCandidate candidate,
            UsageSelectionSettings settings, DateTime nowUtc, bool exception)
        {
            UsageSelectionCandidateResult result = new UsageSelectionCandidateResult { Candidate = candidate };
            void Block(string reason, DateTime? next = null)
            {
                result.Band = UsageSelectionBandEnum.Blocked;
                result.ReasonCode = reason;
                result.NextReconsiderUtc = next;
                result.CanSelect = false;
            }
            void Unknown(string reason)
            {
                result.Band = UsageSelectionBandEnum.Unknown;
                result.ReasonCode = reason;
                result.CanSelect = settings.UnknownUsageBehavior == UsageUnknownBehaviorEnum.AllowDeprioritized;
                result.NextReconsiderUtc = nowUtc.AddSeconds(settings.UnknownRetrySeconds);
            }
            if (!String.IsNullOrEmpty(candidate.IneligibilityReason)) { Block(candidate.IneligibilityReason); return result; }
            if (candidate.Capacity < 1 || candidate.OccupiedSlots < 0 || candidate.OccupiedSlots >= candidate.Capacity)
            { Block("CaptainConcurrencyLimit"); return result; }
            if (candidate.HasAuthoritativeQuotaHold) { Block("AccountQuotaExhausted"); return result; }
            if (candidate.CooldownUntilUtc > nowUtc) { Block("AccountCooldown", candidate.CooldownUntilUtc); return result; }
            if (!candidate.IdentityVerified) { Unknown("UsageIdentityUnknown"); return result; }
            if (!candidate.ConfigurationMatches) { Unknown("UsageConfigurationChanged"); return result; }
            if (!candidate.CapturedUtc.HasValue) { Unknown("UsageMissing"); return result; }
            if (candidate.CapturedUtc.Value > nowUtc + UsageQuotaPolicy.MaximumFutureSkew) { Unknown("FutureObservation"); return result; }
            TimeSpan horizon = UsageQuotaPolicy.FreshnessHorizon(candidate.EffectiveCaptureIntervalSeconds);
            if (horizon.TotalSeconds > settings.MaximumFreshnessSeconds) horizon = TimeSpan.FromSeconds(settings.MaximumFreshnessSeconds);
            if (nowUtc - candidate.CapturedUtc.Value > horizon) { Unknown("UsageStale"); return result; }
            if (candidate.Status != UsageSnapshotStatusEnum.Ok || candidate.Source == UsageCaptureSourceEnum.Derived || !Enum.IsDefined(typeof(UsageCaptureSourceEnum), candidate.Source))
            { Unknown("UsageNotAuthoritative"); return result; }

            double? available = null;
            bool exhausted = false;
            bool unknown = candidate.HasUncertainDebt;
            string unknownReason = candidate.HasUncertainDebt ? "UsageUncertainDebt" : "NoApplicableWindows";
            DateTime? nextReset = null;
            bool unknownReset = false;
            List<string> comparableKeys = new List<string>();
            foreach (UsageSelectionWindowEvidence evidence in candidate.Windows ?? new List<UsageSelectionWindowEvidence>())
            {
                if (evidence == null || evidence.Window == null) { unknown = true; unknownReason = "UsageEvidenceInvalid"; continue; }
                UsageWindow window = evidence.Window;
                if (!String.IsNullOrWhiteSpace(window.Model))
                {
                    if (String.IsNullOrWhiteSpace(candidate.Model)) { unknown = true; unknownReason = "UsageModelUnknown"; continue; }
                    if (!String.Equals(window.Model.Trim(), candidate.Model.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (!String.IsNullOrEmpty(window.Scope) && !evidence.ScopeApplies)
                { unknown = true; unknownReason = "UsageScopeUnknown"; continue; }
                if (!evidence.GenerationConfirmed || window.ResetsUtc <= nowUtc)
                { unknown = true; unknownReason = "AwaitingFreshUsage"; continue; }
                double? percent = window.UsedPercent;
                if (!percent.HasValue && window.Used.HasValue && window.Limit > 0 && !String.IsNullOrWhiteSpace(window.Unit))
                    percent = (double)window.Used.Value / window.Limit.Value * 100;
                if (percent.HasValue && FiniteNonnegative(percent.Value) && percent.Value >= 100)
                {
                    exhausted = true;
                    if (!window.ResetsUtc.HasValue) unknownReset = true;
                    else if (!nextReset.HasValue || window.ResetsUtc > nextReset) nextReset = window.ResetsUtc;
                }
                if (!percent.HasValue || !FiniteNonnegative(percent.Value) || !FiniteNonnegative(evidence.ActiveReservationPercent)
                    || !FiniteNonnegative(evidence.UnsettledPercent) || window.Limit < 0 || window.Used < 0)
                { unknown = true; unknownReason = "UsageEvidenceInvalid"; continue; }
                double headroom = Math.Max(0, 100 - percent.Value - evidence.ActiveReservationPercent - evidence.UnsettledPercent);
                available = !available.HasValue ? headroom : Math.Min(available.Value, headroom);
                if (percent.Value >= 100) exhausted = true;
                if (!window.ResetsUtc.HasValue) unknownReset = true;
                else if (!nextReset.HasValue || window.ResetsUtc > nextReset) nextReset = window.ResetsUtc;
                comparableKeys.Add(window.Name.Trim().ToLowerInvariant() + "/" + window.WindowMinutes + "/"
                    + window.Model?.Trim().ToLowerInvariant() + "/" + window.Scope + "/" + window.Unit);
            }
            result.AvailablePercent = available;
            if (exhausted) { Block("AccountQuotaExhausted", unknownReset ? null : nextReset); return result; }
            if (unknown || !available.HasValue) { Unknown(unknownReason); return result; }
            if (available.Value < (exception ? 0 : settings.ReservePercent) + settings.PerAttemptReservationPercent)
            { Block("BelowReserve", unknownReset ? null : nextReset); return result; }
            result.Band = available.Value >= settings.ComfortableHeadroomPercent ? UsageSelectionBandEnum.Comfortable : UsageSelectionBandEnum.Constrained;
            result.CanSelect = true;
            result.ReasonCode = exception ? "AuthorizedReserveException" : "SufficientHeadroom";
            result.ComparableWindowKey = String.IsNullOrWhiteSpace(candidate.Provider) ? null
                : candidate.Provider.Trim().ToLowerInvariant() + ":" + String.Join("|", comparableKeys.OrderBy(k => k, StringComparer.Ordinal));
            return result;
        }

        private static bool FiniteNonnegative(double number) => !Double.IsNaN(number) && !Double.IsInfinity(number) && number >= 0;
    }
}
