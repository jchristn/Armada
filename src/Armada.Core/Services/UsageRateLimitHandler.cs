namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Passive rate-limit header capture on the inference transport. It sits outermost in the HTTP handler
    /// chain built by <see cref="ModelEndpointAuth.CreateHttpClient(ModelEndpoint, Action{UsageHeaderObservation}?)"/>
    /// and hands the allowlisted response headers to a sink the moment the response headers arrive: before
    /// the provider client reads (or buffers) any body, so a streamed response that later ends early, a
    /// streamed error response and a non-success status all still report their headers. It never issues a
    /// request of its own, never reads the body, never copies a request header (credentials stay out), and a
    /// failing sink never fails the inference call.
    /// </summary>
    public class UsageRateLimitHandler : DelegatingHandler
    {
        #region Public-Members

        /// <summary>
        /// Exact header names captured in addition to the prefixes in <see cref="AllowedPrefixes"/>.
        /// </summary>
        public static IReadOnlyList<string> AllowedNames { get; } = new List<string> { "retry-after" };

        /// <summary>
        /// Header name prefixes captured: Anthropic and OpenAI-style rate-limit headers.
        /// </summary>
        public static IReadOnlyList<string> AllowedPrefixes { get; } = new List<string> { "anthropic-ratelimit-", "x-ratelimit-" };

        /// <summary>
        /// Most headers kept from one response; anything beyond is dropped.
        /// </summary>
        public const int MaximumHeaders = 32;

        /// <summary>
        /// Longest header value kept, in characters; longer values are dropped, never truncated into a
        /// different number.
        /// </summary>
        public const int MaximumValueChars = 128;

        #endregion

        #region Private-Members

        private readonly Action<UsageHeaderObservation> _Sink;
        private readonly Func<DateTime> _Clock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate around an inner handler.
        /// </summary>
        /// <param name="sink">Receives one observation per response that carried at least one allowlisted header, or a non-success status.</param>
        /// <param name="innerHandler">The next handler in the chain (authentication or the shared transport).</param>
        /// <param name="clock">Optional UTC clock seam for tests.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sink"/> or <paramref name="innerHandler"/> is null.</exception>
        public UsageRateLimitHandler(Action<UsageHeaderObservation> sink, HttpMessageHandler innerHandler, Func<DateTime>? clock = null)
            : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
        {
            _Sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _Clock = clock ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a header name is in the rate-limit allowlist.
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>True when the header may be captured.</returns>
        public static bool IsAllowed(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) return false;
            string lower = name.Trim().ToLowerInvariant();
            foreach (string exact in AllowedNames)
            {
                if (String.Equals(lower, exact, StringComparison.Ordinal)) return true;
            }

            foreach (string prefix in AllowedPrefixes)
            {
                if (lower.StartsWith(prefix, StringComparison.Ordinal) && lower.Length > prefix.Length) return true;
            }

            return false;
        }

        /// <summary>
        /// Build the allowlisted observation for a response. Exposed for tests and for transports that
        /// receive a response through another seam.
        /// </summary>
        /// <param name="response">The response whose headers arrived.</param>
        /// <param name="observedUtc">Observation time.</param>
        /// <returns>The observation; never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="response"/> is null.</exception>
        public static UsageHeaderObservation Observe(HttpResponseMessage response, DateTime observedUtc)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            UsageHeaderObservation observation = new UsageHeaderObservation
            {
                StatusCode = (int)response.StatusCode,
                ObservedUtc = observedUtc,
                RequestHost = response.RequestMessage?.RequestUri?.Host
            };

            Collect(response.Headers, observation);
            if (response.Content != null) Collect(response.Content.Headers, observation);
            return observation;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Forward the request and capture the response's allowlisted headers as soon as they arrive.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The unmodified response.</returns>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            try
            {
                UsageHeaderObservation observation = Observe(response, _Clock());
                if (observation.Headers.Count > 0 || !response.IsSuccessStatusCode) _Sink(observation);
            }
            catch (Exception)
            {
                // Capture is passive and best effort: the inference call is never failed by it.
            }

            return response;
        }

        #endregion

        #region Private-Methods

        private static void Collect(HttpHeaders headers, UsageHeaderObservation observation)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
            {
                if (observation.Headers.Count >= MaximumHeaders) return;
                if (!IsAllowed(header.Key)) continue;
                string value = String.Join(",", header.Value).Trim();
                if (value.Length == 0 || value.Length > MaximumValueChars) continue;
                observation.Headers[header.Key.Trim().ToLowerInvariant()] = value;
            }
        }

        #endregion
    }
}
