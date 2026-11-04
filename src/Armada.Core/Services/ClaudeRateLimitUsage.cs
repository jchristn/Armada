namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Protocol;

    /// <summary>
    /// Version 1 parser for Claude Code's passive <c>rate_limit_event</c> (the <c>rate_limit_info</c> object of
    /// <c>--output-format stream-json</c>): <c>status</c> in allowed / allowed_warning / rejected, a fraction
    /// <c>utilization</c> in 0..1, a window name in <c>rateLimitType</c> and absolute epoch-second
    /// <c>resetsAt</c>. The event only exists while a real mission runs, so this is a free harvest and never a
    /// poll. Anything outside version 1 (an unknown status, a utilization above 1, a malformed window name) is
    /// rejected as Failed rather than guessed at, and a null utilization stays null: a <c>rejected</c> status
    /// is never turned into an invented 100 percent.
    /// </summary>
    public static class ClaudeRateLimitUsage
    {
        #region Public-Members

        /// <summary>
        /// Statuses version 1 of the event uses.
        /// </summary>
        public static IReadOnlyList<string> KnownStatuses { get; } = new List<string> { "allowed", "allowed_warning", "rejected" };

        /// <summary>
        /// Longest accepted window name.
        /// </summary>
        public const int MaximumWindowNameChars = 64;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Convert one event into a sanitized observation.
        /// </summary>
        /// <param name="evt">The parsed event, or null.</param>
        /// <param name="observedUtc">Time the line was read from the stream.</param>
        /// <returns>An Ok observation with one window, or Failed with a stable error code; never null.</returns>
        public static UsageProbeResult ToResult(ClaudeRateLimitEvent? evt, DateTime observedUtc)
        {
            if (evt == null || evt.Info == null)
                return Stamp(UsageProbeResult.Failed("MalformedOutput", "the rate limit event carried no rate_limit_info"), observedUtc);

            ClaudeRateLimitInfo info = evt.Info;
            string? status = info.Status?.Trim().ToLowerInvariant();
            if (String.IsNullOrEmpty(status) || !Contains(KnownStatuses, status))
                return Stamp(UsageProbeResult.Failed("UnknownEventVersion", "the rate limit event reported a status outside version 1"), observedUtc);

            string? name = info.RateLimitType?.Trim().ToLowerInvariant();
            if (String.IsNullOrEmpty(name) || !IsWindowName(name))
                return Stamp(UsageProbeResult.Failed("MalformedOutput", "the rate limit event named no valid window"), observedUtc);

            double? percent = null;
            if (info.Utilization.HasValue)
            {
                double fraction = info.Utilization.Value;
                if (Double.IsNaN(fraction) || Double.IsInfinity(fraction) || fraction < 0 || fraction > 1)
                    return Stamp(UsageProbeResult.Failed("UnknownEventVersion", "the rate limit event reported a utilization outside the version 1 fraction range"), observedUtc);
                percent = Math.Round(fraction * 100.0, 2);
            }

            DateTime? resets = null;
            if (info.ResetsAt.HasValue)
            {
                resets = UsageResetConverter.FromEpochSeconds(info.ResetsAt, observedUtc);
                if (!resets.HasValue)
                    return Stamp(UsageProbeResult.Failed("MalformedOutput", "the rate limit event reported a reset outside the accepted bounds"), observedUtc);
            }

            UsageProbeResult result = new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.Ok,
                Source = UsageCaptureSourceEnum.HarnessEvent,
                Provider = "anthropic",
                CapturedUtc = observedUtc,
                Windows = new List<UsageWindow>
                {
                    new UsageWindow
                    {
                        Name = name,
                        UsedPercent = percent,
                        Unit = "percent",
                        ResetsUtc = resets,
                        ResetKind = resets.HasValue ? UsageWindowResetKindEnum.Fixed : UsageWindowResetKindEnum.Unknown,
                        Scope = "account",
                        CoverageNote = status
                    }
                }
            };
            return result;
        }

        #endregion

        #region Private-Methods

        private static UsageProbeResult Stamp(UsageProbeResult result, DateTime observedUtc)
        {
            result.Source = UsageCaptureSourceEnum.HarnessEvent;
            result.Provider = "anthropic";
            result.CapturedUtc = observedUtc;
            return result;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (string item in list)
            {
                if (String.Equals(item, value, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        private static bool IsWindowName(string name)
        {
            if (name.Length > MaximumWindowNameChars) return false;
            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }

            return true;
        }

        #endregion
    }
}
