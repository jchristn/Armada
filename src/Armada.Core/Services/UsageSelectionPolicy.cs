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
        public const string AlgorithmVersion = "usage-bands-v2";

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
                evaluation.ReasonCode = UsageSelectionReasonCodes.CandidateLimitExceeded;
                return evaluation;
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (UsageSelectionCandidate candidate in candidates)
            {
                if (candidate == null || String.IsNullOrWhiteSpace(candidate.CaptainId) || !ids.Add(candidate.CaptainId))
                    throw new ArgumentException("Selection candidates must have distinct nonempty captain IDs.", nameof(candidates));
                evaluation.Candidates.Add(EvaluateCandidate(candidate, settings, nowUtc, allowSoftReserveException));
            }

            List<UsageSelectionCandidateResult> ordered = Order(evaluation.Candidates, settings.Strategy);
            evaluation.Candidates = ordered;
            evaluation.SelectedCaptainId = ordered.FirstOrDefault(r => r.CanSelect)?.Candidate.CaptainId;
            if (evaluation.SelectedCaptainId != null) evaluation.ReasonCode = UsageSelectionReasonCodes.SelectedForHeadroom;
            else if (ordered.Count == 0) evaluation.ReasonCode = UsageSelectionReasonCodes.NoEligibleCaptain;
            else evaluation.ReasonCode = ordered[0].ReasonCode;
            return evaluation;
        }

        private static List<UsageSelectionCandidateResult> Order(List<UsageSelectionCandidateResult> results, CaptainSelectionStrategyEnum strategy)
        {
            bool firstAvailable = strategy == CaptainSelectionStrategyEnum.FirstAvailable;
            bool mostUsed = strategy == CaptainSelectionStrategyEnum.MostUsedAllowance;

            // A pairwise comparable-window comparator can be non-transitive when a third provider is present.
            // Compare percentages only inside a whole equal-bucket/band group with one common semantic key.
            IEnumerable<IEnumerable<UsageSelectionCandidateResult>> groups = results
                .GroupBy(r => new { r.Candidate.RoutingBucket, Band = BandOrder(r.Band, strategy) })
                .OrderBy(g => g.Key.RoutingBucket)
                .ThenBy(g => g.Key.Band);

            List<UsageSelectionCandidateResult> ordered = new List<UsageSelectionCandidateResult>();
            foreach (IEnumerable<UsageSelectionCandidateResult> group in groups)
            {
                List<UsageSelectionCandidateResult> rows = group.ToList();
                string? key = rows[0].ComparableWindowKey;
                bool comparable = !firstAvailable && key != null && rows.All(r => r.ComparableWindowKey == key && r.AvailablePercent.HasValue);

                IOrderedEnumerable<UsageSelectionCandidateResult> sorted = rows
                    .OrderBy(r => firstAvailable ? r.Candidate.FirstAvailableRank : 0)
                    .ThenBy(r => comparable ? (mostUsed ? r.AvailablePercent : -r.AvailablePercent) : null)
                    .ThenBy(r => firstAvailable ? 0 : Load(r.Candidate))
                    .ThenBy(r => r.Candidate.LastAssignedUtc ?? DateTime.MaxValue)
                    .ThenBy(r => r.Candidate.CaptainId, StringComparer.Ordinal);
                ordered.AddRange(sorted);
            }
            return ordered;
        }

        private static int BandOrder(UsageSelectionBandEnum band, CaptainSelectionStrategyEnum strategy)
        {
            if (strategy == CaptainSelectionStrategyEnum.FirstAvailable)
            {
                if (band == UsageSelectionBandEnum.Blocked) return 2;
                if (band == UsageSelectionBandEnum.Unknown) return 1;
                return 0;
            }
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

            string? gate = HardGate(candidate, nowUtc);
            if (gate != null)
            {
                DateTime? next = gate == UsageSelectionReasonCodes.AccountCooldown ? candidate.CooldownUntilUtc : null;
                return Block(result, gate, next);
            }

            string? unknownEvidence = EvidenceGate(candidate, settings, nowUtc);
            if (unknownEvidence != null) return Unknown(result, unknownEvidence, settings, nowUtc);

            UsageSelectionWindowSummary windows = SummarizeWindows(candidate, nowUtc);
            result.AvailablePercent = windows.Available;
            DateTime? reset = windows.UnknownReset ? null : windows.NextReset;
            if (windows.Exhausted) return Block(result, UsageSelectionReasonCodes.AccountQuotaExhausted, reset);
            if (windows.Unknown || !windows.Available.HasValue) return Unknown(result, windows.UnknownReason, settings, nowUtc);

            double required = (exception ? 0 : settings.ReservePercent) + settings.PerAttemptReservationPercent;
            if (windows.Available.Value < required) return Block(result, UsageSelectionReasonCodes.BelowReserve, reset);

            result.Band = windows.Available.Value >= settings.ComfortableHeadroomPercent
                ? UsageSelectionBandEnum.Comfortable
                : UsageSelectionBandEnum.Constrained;
            result.CanSelect = true;
            result.ReasonCode = exception ? UsageSelectionReasonCodes.AuthorizedReserveException : UsageSelectionReasonCodes.SufficientHeadroom;
            result.ComparableWindowKey = String.IsNullOrWhiteSpace(candidate.Provider)
                ? null
                : candidate.Provider.Trim().ToLowerInvariant() + ":" + String.Join("|", windows.ComparableKeys.OrderBy(k => k, StringComparer.Ordinal));
            return result;
        }

        // Gates that block regardless of evidence quality. Order matters: the first match is the reported reason.
        private static string? HardGate(UsageSelectionCandidate candidate, DateTime nowUtc)
        {
            if (!String.IsNullOrEmpty(candidate.IneligibilityReason)) return candidate.IneligibilityReason;
            if (candidate.Capacity < 1 || candidate.OccupiedSlots < 0 || candidate.OccupiedSlots >= candidate.Capacity)
                return UsageSelectionReasonCodes.CaptainConcurrencyLimit;
            if (candidate.HasAuthoritativeQuotaHold) return UsageSelectionReasonCodes.AccountQuotaExhausted;
            if (candidate.CooldownUntilUtc > nowUtc) return UsageSelectionReasonCodes.AccountCooldown;
            return null;
        }

        // Account-level evidence checks; any failure makes the candidate Unknown rather than blocked.
        private static string? EvidenceGate(UsageSelectionCandidate candidate, UsageSelectionSettings settings, DateTime nowUtc)
        {
            if (!candidate.IdentityVerified) return UsageSelectionReasonCodes.UsageIdentityUnknown;
            if (!candidate.ConfigurationMatches) return UsageSelectionReasonCodes.UsageConfigurationChanged;
            if (!candidate.CapturedUtc.HasValue) return UsageSelectionReasonCodes.UsageMissing;
            if (candidate.CapturedUtc.Value > nowUtc + UsageQuotaPolicy.MaximumFutureSkew) return UsageSelectionReasonCodes.FutureObservation;

            TimeSpan horizon = UsageQuotaPolicy.FreshnessHorizon(candidate.EffectiveCaptureIntervalSeconds);
            if (horizon.TotalSeconds > settings.MaximumFreshnessSeconds) horizon = TimeSpan.FromSeconds(settings.MaximumFreshnessSeconds);
            if (nowUtc - candidate.CapturedUtc.Value > horizon) return UsageSelectionReasonCodes.UsageStale;

            bool authoritative = candidate.Status == UsageSnapshotStatusEnum.Ok
                && candidate.Source != UsageCaptureSourceEnum.Derived
                && Enum.IsDefined(typeof(UsageCaptureSourceEnum), candidate.Source);
            return authoritative ? null : UsageSelectionReasonCodes.UsageNotAuthoritative;
        }

        private static UsageSelectionWindowSummary SummarizeWindows(UsageSelectionCandidate candidate, DateTime nowUtc)
        {
            UsageSelectionWindowSummary summary = new UsageSelectionWindowSummary
            {
                Unknown = candidate.HasUncertainDebt,
                UnknownReason = candidate.HasUncertainDebt ? UsageSelectionReasonCodes.UsageUncertainDebt : UsageSelectionReasonCodes.NoApplicableWindows
            };

            foreach (UsageSelectionWindowEvidence evidence in candidate.Windows ?? new List<UsageSelectionWindowEvidence>())
            {
                if (evidence == null || evidence.Window == null)
                {
                    summary.MarkUnknown(UsageSelectionReasonCodes.UsageEvidenceInvalid);
                    continue;
                }

                UsageWindow window = evidence.Window;
                if (!String.IsNullOrWhiteSpace(window.Model))
                {
                    if (String.IsNullOrWhiteSpace(candidate.Model))
                    {
                        summary.MarkUnknown(UsageSelectionReasonCodes.UsageModelUnknown);
                        continue;
                    }
                    // A window for another model does not apply to this captain.
                    if (!String.Equals(window.Model.Trim(), candidate.Model.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (!String.IsNullOrEmpty(window.Scope) && !evidence.ScopeApplies)
                {
                    summary.MarkUnknown(UsageSelectionReasonCodes.UsageScopeUnknown);
                    continue;
                }
                if (!evidence.WindowIdentityConfirmed || window.ResetsUtc <= nowUtc)
                {
                    summary.MarkUnknown(UsageSelectionReasonCodes.AwaitingFreshUsage);
                    continue;
                }

                double? percent = window.UsedPercent;
                if (!percent.HasValue && window.Used.HasValue && window.Limit > 0 && !String.IsNullOrWhiteSpace(window.Unit))
                    percent = (double)window.Used.Value / window.Limit.Value * 100;

                // Proven exhaustion is recorded before validity checks so a malformed estimate cannot hide it.
                if (percent.HasValue && FiniteNonnegative(percent.Value) && percent.Value >= 100)
                {
                    summary.Exhausted = true;
                    summary.RecordReset(window.ResetsUtc);
                }

                bool valid = percent.HasValue && FiniteNonnegative(percent.Value)
                    && FiniteNonnegative(evidence.ActiveReservationPercent) && FiniteNonnegative(evidence.UnsettledPercent)
                    && !(window.Limit < 0) && !(window.Used < 0);
                if (!valid)
                {
                    summary.MarkUnknown(UsageSelectionReasonCodes.UsageEvidenceInvalid);
                    continue;
                }

                double headroom = Math.Max(0, 100 - percent!.Value - evidence.ActiveReservationPercent - evidence.UnsettledPercent);
                summary.Available = summary.Available.HasValue ? Math.Min(summary.Available.Value, headroom) : headroom;
                summary.RecordReset(window.ResetsUtc);
                summary.ComparableKeys.Add(window.Name.Trim().ToLowerInvariant() + "/" + window.WindowMinutes + "/"
                    + window.Model?.Trim().ToLowerInvariant() + "/" + window.Scope + "/" + window.Unit);
            }
            return summary;
        }

        private static UsageSelectionCandidateResult Block(UsageSelectionCandidateResult result, string reason, DateTime? next)
        {
            result.Band = UsageSelectionBandEnum.Blocked;
            result.ReasonCode = reason;
            result.NextReconsiderUtc = next;
            result.CanSelect = false;
            return result;
        }

        private static UsageSelectionCandidateResult Unknown(UsageSelectionCandidateResult result, string reason, UsageSelectionSettings settings, DateTime nowUtc)
        {
            result.Band = UsageSelectionBandEnum.Unknown;
            result.ReasonCode = reason;
            result.CanSelect = settings.UnknownUsageBehavior == UsageUnknownBehaviorEnum.AllowDeprioritized;
            result.NextReconsiderUtc = nowUtc.AddSeconds(settings.UnknownRetrySeconds);
            return result;
        }

        private static bool FiniteNonnegative(double number) => !Double.IsNaN(number) && !Double.IsInfinity(number) && number >= 0;
    }
}
