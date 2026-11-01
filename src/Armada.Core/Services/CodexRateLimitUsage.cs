namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;

    /// <summary>
    /// Version 1 normalizer for a Codex app-server <c>account/rateLimits/read</c> result or an
    /// <c>account/rateLimits/updated</c> push. Every metered limit id becomes its own windows
    /// (<c>&lt;limitId&gt;:primary</c>, <c>&lt;limitId&gt;:secondary</c>); a window's duration comes from
    /// <c>windowDurationMins</c> and is never assumed. Null windows are skipped (a partial snapshot keeps the
    /// windows it has); a snapshot with no window at all answers Unsupported (an API-key account has no
    /// subscription windows), never zero. A percentage outside 0..100, a non-positive duration or a reset
    /// outside the accepted bounds rejects the whole observation as Failed so last-good data is kept.
    /// </summary>
    public static class CodexRateLimitUsage
    {
        #region Public-Members

        /// <summary>
        /// Most windows accepted from one response.
        /// </summary>
        public const int MaximumWindows = 32;

        /// <summary>
        /// Longest accepted limit id.
        /// </summary>
        public const int MaximumLimitIdChars = 64;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalize a read response.
        /// </summary>
        /// <param name="response">The typed response, or null.</param>
        /// <param name="observedUtc">Time the response was received.</param>
        /// <returns>Ok with windows, Unsupported when the account reports none, or Failed; never null.</returns>
        public static UsageProbeResult ToResult(CodexRateLimitsReadResponse? response, DateTime observedUtc)
        {
            if (response == null)
                return Stamp(UsageProbeResult.Failed("MalformedOutput", "the app-server answered without a rate-limit result"), observedUtc);

            // Per-limit snapshots come first; the backward-compatible single snapshot fills any window slot
            // they did not report (the same limit id can appear in both with different slots filled).
            List<KeyValuePair<string, CodexRateLimitSnapshot>> snapshots = new List<KeyValuePair<string, CodexRateLimitSnapshot>>();
            if (response.RateLimitsByLimitId != null)
            {
                foreach (KeyValuePair<string, CodexRateLimitSnapshot?> entry in response.RateLimitsByLimitId)
                {
                    if (entry.Value == null) continue;
                    snapshots.Add(new KeyValuePair<string, CodexRateLimitSnapshot>(LimitId(entry.Value.LimitId ?? entry.Key), entry.Value));
                }
            }

            if (response.RateLimits != null)
                snapshots.Add(new KeyValuePair<string, CodexRateLimitSnapshot>(LimitId(response.RateLimits.LimitId), response.RateLimits));

            return FromSnapshots(snapshots, observedUtc);
        }

        /// <summary>
        /// Normalize a passive <c>account/rateLimits/updated</c> push.
        /// </summary>
        /// <param name="parameters">The notification parameters, or null.</param>
        /// <param name="observedUtc">Time the notification was received.</param>
        /// <returns>Ok with windows, Unsupported when none, or Failed; never null.</returns>
        public static UsageProbeResult FromUpdate(CodexRateLimitsUpdatedParams? parameters, DateTime observedUtc)
        {
            if (parameters == null || parameters.RateLimits == null)
                return Stamp(UsageProbeResult.Failed("MalformedOutput", "the rate-limit update carried no snapshot"), observedUtc);
            List<KeyValuePair<string, CodexRateLimitSnapshot>> snapshots = new List<KeyValuePair<string, CodexRateLimitSnapshot>>
            {
                new KeyValuePair<string, CodexRateLimitSnapshot>(LimitId(parameters.RateLimits.LimitId), parameters.RateLimits)
            };
            return FromSnapshots(snapshots, observedUtc);
        }

        #endregion

        #region Private-Methods

        private static UsageProbeResult FromSnapshots(List<KeyValuePair<string, CodexRateLimitSnapshot>> snapshots, DateTime observedUtc)
        {
            List<UsageWindow> windows = new List<UsageWindow>();
            foreach (KeyValuePair<string, CodexRateLimitSnapshot> snapshot in snapshots)
            {
                if (snapshot.Key.Length > MaximumLimitIdChars)
                    return Stamp(UsageProbeResult.Failed("MalformedOutput", "a reported limit id exceeds the accepted length"), observedUtc);
                string? failure = AddWindow(windows, snapshot.Key, "primary", snapshot.Value.Primary, snapshot.Value.PlanType, observedUtc)
                    ?? AddWindow(windows, snapshot.Key, "secondary", snapshot.Value.Secondary, snapshot.Value.PlanType, observedUtc);
                if (failure != null) return Stamp(UsageProbeResult.Failed("MalformedOutput", failure), observedUtc);
                if (windows.Count > MaximumWindows)
                    return Stamp(UsageProbeResult.Failed("MalformedOutput", "the app-server reported more than " + MaximumWindows + " windows"), observedUtc);
            }

            if (windows.Count == 0)
                return Stamp(UsageProbeResult.Unsupported("NoRateLimitWindows", "the account reports no subscription rate-limit windows (for example an API-key login)"), observedUtc);

            return new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.Ok,
                Source = UsageCaptureSourceEnum.AppServer,
                Provider = "openai",
                CapturedUtc = observedUtc,
                Windows = windows
            };
        }

        private static string? AddWindow(List<UsageWindow> windows, string limitId, string slot, CodexRateLimitWindow? window, string? planType, DateTime observedUtc)
        {
            if (window == null) return null;
            if (!window.UsedPercent.HasValue) return null;
            string name = limitId + ":" + slot;
            if (windows.Exists(existing => String.Equals(existing.Name, name, StringComparison.Ordinal))) return null;
            double percent = window.UsedPercent.Value;
            if (Double.IsNaN(percent) || Double.IsInfinity(percent) || percent < 0 || percent > 100)
                return "a reported window percentage is outside 0..100";

            int? minutes = null;
            if (window.WindowDurationMins.HasValue)
            {
                if (window.WindowDurationMins.Value <= 0 || window.WindowDurationMins.Value > 60L * 24 * 366)
                    return "a reported window duration is not a positive bounded number of minutes";
                minutes = (int)window.WindowDurationMins.Value;
            }

            DateTime? resets = null;
            if (window.ResetsAt.HasValue)
            {
                resets = UsageResetConverter.FromEpochSeconds(window.ResetsAt, observedUtc);
                if (!resets.HasValue) return "a reported reset time is outside the accepted bounds";
            }

            windows.Add(new UsageWindow
            {
                Name = name,
                UsedPercent = percent,
                Unit = "percent",
                WindowMinutes = minutes,
                ResetsUtc = resets,
                Scope = "account",
                CoverageNote = String.IsNullOrWhiteSpace(planType) ? null : "plan " + Bounded(planType!.Trim(), 32)
            });
            return null;
        }

        private static string LimitId(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? "codex" : value.Trim();
        }

        private static string Bounded(string value, int maximum)
        {
            return value.Length <= maximum ? value : value.Substring(0, maximum);
        }

        private static UsageProbeResult Stamp(UsageProbeResult result, DateTime observedUtc)
        {
            result.Source = UsageCaptureSourceEnum.AppServer;
            result.Provider = "openai";
            result.CapturedUtc = observedUtc;
            return result;
        }

        #endregion
    }
}
