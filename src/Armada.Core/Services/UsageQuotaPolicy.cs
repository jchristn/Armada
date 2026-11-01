namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Decision D6: which usage observations may affect routing. Only a fresh, authoritative (status Ok and
    /// not a Derived estimate) observation for the captain's own account can hold or release a captain.
    /// Estimated, stale, failed, unsupported and missing-auth observations never quarantine or release.
    /// A window that names a model applies only to captains that run that model; an account-wide window
    /// applies to every member. A window whose reset already passed is expired and cannot hold. A release
    /// requires every applicable reported window to be below its limit; it only ever clears a quota-owned
    /// hold (<see cref="UsageQuotaHold"/>). Pure: no I/O, no clock other than the supplied one.
    /// </summary>
    public static class UsageQuotaPolicy
    {
        #region Public-Members

        /// <summary>
        /// Floor of the freshness horizon: an observation older than max(2 x effective interval, this) is stale.
        /// </summary>
        public static readonly TimeSpan MinimumFreshness = TimeSpan.FromSeconds(120);

        /// <summary>
        /// Largest accepted clock skew for an observation dated in the future.
        /// </summary>
        public static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMinutes(5);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The freshness horizon for an effective capture interval.
        /// </summary>
        /// <param name="effectiveIntervalSeconds">Effective capture interval in seconds.</param>
        /// <returns>max(2 x interval, 120 seconds).</returns>
        public static TimeSpan FreshnessHorizon(int effectiveIntervalSeconds)
        {
            TimeSpan doubled = TimeSpan.FromSeconds(Math.Max(0, effectiveIntervalSeconds) * 2.0);
            return doubled > MinimumFreshness ? doubled : MinimumFreshness;
        }

        /// <summary>
        /// Evaluate one observation for one captain.
        /// </summary>
        /// <param name="captain">The captain the observation would route.</param>
        /// <param name="observation">The sanitized observation.</param>
        /// <param name="captainAccountKey">The account key the captain's active membership resolves to, or null when unknown.</param>
        /// <param name="effectiveIntervalSeconds">Effective capture interval of the captain's group.</param>
        /// <param name="nowUtc">Current UTC time.</param>
        /// <returns>The decision; never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="captain"/> or <paramref name="observation"/> is null.</exception>
        public static UsageQuotaDecision Evaluate(Captain captain, UsageProbeResult observation, string? captainAccountKey, int effectiveIntervalSeconds, DateTime nowUtc)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (observation == null) throw new ArgumentNullException(nameof(observation));

            DateTime observed = observation.CapturedUtc;
            if (observation.Status != UsageSnapshotStatusEnum.Ok)
                return UsageQuotaDecision.NoChange("NotAuthoritative", observed);
            if (observation.Source == UsageCaptureSourceEnum.Derived)
                return UsageQuotaDecision.NoChange("Estimated", observed);
            if (observed > nowUtc + MaximumFutureSkew)
                return UsageQuotaDecision.NoChange("FutureObservation", observed);
            if (nowUtc - observed > FreshnessHorizon(effectiveIntervalSeconds))
                return UsageQuotaDecision.NoChange("Stale", observed);
            if (!String.IsNullOrWhiteSpace(observation.AccountKey) && !String.IsNullOrWhiteSpace(captainAccountKey)
                && !String.Equals(observation.AccountKey, captainAccountKey, StringComparison.Ordinal))
                return UsageQuotaDecision.NoChange("WrongAccount", observed);

            List<string> exhausted = new List<string>();
            DateTime? until = null;
            bool anyApplicable = false;
            foreach (UsageWindow window in observation.Windows ?? new List<UsageWindow>())
            {
                if (window == null || !window.UsedPercent.HasValue) continue;
                double percent = window.UsedPercent.Value;
                if (Double.IsNaN(percent) || Double.IsInfinity(percent)) continue;
                if (!AppliesTo(window, captain)) continue;
                anyApplicable = true;
                if (percent < 100) continue;
                if (window.ResetsUtc.HasValue && window.ResetsUtc.Value <= nowUtc) continue;
                exhausted.Add(window.Name);
                if (window.ResetsUtc.HasValue && (!until.HasValue || window.ResetsUtc.Value > until.Value)) until = window.ResetsUtc.Value;
            }

            if (exhausted.Count > 0)
            {
                // Without a reported reset the hold lasts until the next fresh read is due; a passed reset
                // expires the hold and the next read decides again.
                return new UsageQuotaDecision
                {
                    Action = UsageQuotaActionEnum.Hold,
                    ReasonCode = "Exhausted",
                    ObservedUtc = observed,
                    HoldUntilUtc = until ?? nowUtc + FreshnessHorizon(effectiveIntervalSeconds),
                    ExhaustedWindows = exhausted
                };
            }

            if (!anyApplicable) return UsageQuotaDecision.NoChange("NoApplicableWindows", observed);
            return new UsageQuotaDecision { Action = UsageQuotaActionEnum.Release, ReasonCode = "BelowLimit", ObservedUtc = observed };
        }

        #endregion

        #region Private-Methods

        private static bool AppliesTo(UsageWindow window, Captain captain)
        {
            if (String.IsNullOrWhiteSpace(window.Model)) return true;
            if (String.IsNullOrWhiteSpace(captain.Model)) return false;
            return String.Equals(window.Model.Trim(), captain.Model!.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
