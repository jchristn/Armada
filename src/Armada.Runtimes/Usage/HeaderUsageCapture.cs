namespace Armada.Runtimes.Usage
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Passive rate-limit capture for an inference transport (ApiEndpoint; Mux would use it only if its
    /// provider transport became observable). <see cref="Record"/> is the sink handed to
    /// <see cref="ModelEndpointClientFactory.CreateCompletion(ModelEndpoint, SyslogLogging.LoggingModule, Action{UsageHeaderObservation}?)"/>;
    /// it keeps the latest allowlisted observation, and <see cref="Latest"/> returns it parsed with the time it
    /// was actually observed (never a fresh timestamp). Nothing is ever requested to obtain headers.
    /// <see cref="Parse"/> is the version 1 header parser: Anthropic <c>anthropic-ratelimit-{requests,tokens,
    /// input-tokens,output-tokens}-{limit,remaining,reset}</c> with RFC 3339 resets, OpenAI-style
    /// <c>x-ratelimit-{limit,remaining,reset}-{requests,tokens}</c> with duration resets counted from the
    /// observation time, and <c>retry-after</c>. Absent headers are Unsupported (never AuthMissing); 401 and
    /// 403 are AuthMissing; 5xx is Failed with only its Retry-After kept; 429 keeps any windows plus the
    /// Retry-After; malformed numbers reject the whole observation so last-good data is kept.
    /// </summary>
    public class HeaderUsageCapture
    {
        #region Public-Members

        /// <summary>
        /// The latest observation recorded, or null.
        /// </summary>
        public UsageHeaderObservation? LatestObservation
        {
            get
            {
                lock (_Lock) return _Latest;
            }
        }

        /// <summary>
        /// How many observations were recorded.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_Lock) return _Count;
            }
        }

        #endregion

        #region Private-Members

        private static readonly string[] _AnthropicKinds = new string[] { "requests", "tokens", "input-tokens", "output-tokens" };
        private static readonly string[] _OpenAiKinds = new string[] { "requests", "tokens" };
        private readonly object _Lock = new object();
        private UsageHeaderObservation? _Latest = null;
        private int _Count = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HeaderUsageCapture()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record one observation (the transport sink). An observation older than the one already held is
        /// ignored, so a late response cannot replace a newer reading.
        /// </summary>
        /// <param name="observation">Observation; null is ignored.</param>
        public void Record(UsageHeaderObservation? observation)
        {
            if (observation == null) return;
            lock (_Lock)
            {
                _Count++;
                if (_Latest != null && observation.ObservedUtc < _Latest.ObservedUtc) return;
                _Latest = observation;
            }
        }

        /// <summary>
        /// The latest observation parsed, or null when nothing was observed yet.
        /// </summary>
        /// <returns>The parsed observation, or null.</returns>
        public UsageProbeResult? Latest()
        {
            UsageHeaderObservation? latest = LatestObservation;
            return latest == null ? null : Parse(latest);
        }

        /// <summary>
        /// Parse one observation.
        /// </summary>
        /// <param name="observation">Observation, or null.</param>
        /// <returns>The sanitized result; never null.</returns>
        public static UsageProbeResult Parse(UsageHeaderObservation? observation)
        {
            if (observation == null)
                return UsageProbeResult.Unsupported("HeadersAbsent", "no inference response has been observed");

            DateTime observed = observation.ObservedUtc;
            Dictionary<string, string> headers = observation.Headers;
            DateTime? retryAfter = UsageResetConverter.FromRetryAfter(Value(headers, "retry-after"), observed);

            if (observation.StatusCode == 401 || observation.StatusCode == 403)
                return Stamp(UsageProbeResult.AuthMissing("CredentialRejected", "the provider rejected the endpoint credential (HTTP " + observation.StatusCode + ")"), observed, null, null);
            if (observation.StatusCode >= 500)
                return Stamp(UsageProbeResult.Failed("ProviderUnavailable", "the provider answered HTTP " + observation.StatusCode + "; its rate-limit headers are not used"), observed, null, retryAfter);

            List<UsageWindow> windows = new List<UsageWindow>();
            string? provider = null;
            foreach (string kind in _AnthropicKinds)
            {
                string? failure = AddWindow(windows, kind.Replace('-', '_'), Unit(kind),
                    Value(headers, "anthropic-ratelimit-" + kind + "-limit"),
                    Value(headers, "anthropic-ratelimit-" + kind + "-remaining"),
                    UsageResetConverter.FromRfc3339(Value(headers, "anthropic-ratelimit-" + kind + "-reset"), observed),
                    Value(headers, "anthropic-ratelimit-" + kind + "-reset"));
                if (failure != null) return Stamp(UsageProbeResult.Failed("MalformedHeaders", failure), observed, "anthropic", retryAfter);
            }

            if (windows.Count > 0) provider = "anthropic";
            int before = windows.Count;
            foreach (string kind in _OpenAiKinds)
            {
                string? failure = AddWindow(windows, kind, Unit(kind),
                    Value(headers, "x-ratelimit-limit-" + kind),
                    Value(headers, "x-ratelimit-remaining-" + kind),
                    UsageResetConverter.FromDuration(Value(headers, "x-ratelimit-reset-" + kind), observed),
                    Value(headers, "x-ratelimit-reset-" + kind));
                if (failure != null) return Stamp(UsageProbeResult.Failed("MalformedHeaders", failure), observed, "openai-compatible", retryAfter);
            }

            if (provider == null && windows.Count > before) provider = "openai-compatible";

            if (windows.Count == 0)
            {
                if (observation.StatusCode == 429)
                    return Stamp(UsageProbeResult.Failed("ProviderRateLimited", "the provider rate-limited the request without reporting limit headers"), observed, null, retryAfter);
                return Stamp(UsageProbeResult.Unsupported("HeadersAbsent", "the provider response carried no rate-limit headers"), observed, null, retryAfter);
            }

            UsageProbeResult result = new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.Ok,
                Windows = windows
            };
            return Stamp(result, observed, provider, retryAfter);
        }

        #endregion

        #region Private-Methods

        private static string? AddWindow(List<UsageWindow> windows, string name, string unit, string? limitText, string? remainingText, DateTime? resets, string? resetText)
        {
            if (limitText == null && remainingText == null) return null;
            if (limitText == null || remainingText == null) return "the " + name + " rate-limit headers are incomplete";
            if (!Int64.TryParse(limitText, NumberStyles.None, CultureInfo.InvariantCulture, out long limit) || limit <= 0)
                return "the " + name + " limit header is not a positive integer";
            if (!Int64.TryParse(remainingText, NumberStyles.None, CultureInfo.InvariantCulture, out long remaining) || remaining > limit)
                return "the " + name + " remaining header is not an integer within its limit";
            if (resetText != null && !resets.HasValue)
                return "the " + name + " reset header is malformed or outside the accepted bounds";

            long used = limit - remaining;
            windows.Add(new UsageWindow
            {
                Name = name,
                Used = used,
                Limit = limit,
                UsedPercent = Math.Round(used * 100.0 / limit, 2),
                Unit = unit,
                ResetsUtc = resets,
                // Rate-limit headers describe replenishing buckets; the reset time is when capacity refills, not a window boundary.
                ResetKind = UsageWindowResetKindEnum.Rolling,
                Scope = "endpoint"
            });
            return null;
        }

        private static string Unit(string kind)
        {
            return kind == "requests" ? "requests" : "tokens";
        }

        private static string? Value(Dictionary<string, string> headers, string name)
        {
            if (headers == null) return null;
            return headers.TryGetValue(name, out string? value) && !String.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }

        private static UsageProbeResult Stamp(UsageProbeResult result, DateTime observedUtc, string? provider, DateTime? retryAfter)
        {
            result.Source = UsageCaptureSourceEnum.Headers;
            result.CapturedUtc = observedUtc;
            result.Provider = provider;
            result.RetryAfterUtc = retryAfter;
            return result;
        }

        #endregion
    }
}
