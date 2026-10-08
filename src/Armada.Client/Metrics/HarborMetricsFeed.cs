namespace Armada.Client.Metrics
{
    using System;
    using System.Globalization;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Metrics;
    using Armada.Core.Models;

    /// <summary>
    /// Asks the Admiral for one Harbor's own metrics (GET /api/v1/harbors/{id}/metrics) with the Harbor's credential and
    /// says, in plain language, why when there are none: no address, no Harbor ID, a refused (401) or insufficient (403)
    /// credential, a Harbor the Admiral does not know, an Admiral too old to have the endpoint, or an Admiral that cannot be
    /// reached. An older Admiral answers 404 for the unknown route; the feed then asks for the Harbor itself, and a Harbor
    /// that exists means the endpoint is what is missing. Thread-safe; each call uses its own client.
    /// </summary>
    public class HarborMetricsFeed
    {
        #region Private-Members

        private readonly Func<ArmadaClient?> _ClientFactory;
        private readonly Func<string?> _HarborId;
        private readonly Func<bool> _IsAdmiralLocal;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="clientFactory">Creates a client authorized as the Harbor, or returns null when there is no Admiral
        /// address. The feed disposes each client after use.</param>
        /// <param name="harborId">The Harbor's own ID (read on each call, so a changed setting takes effect).</param>
        /// <param name="isAdmiralLocal">True when the Admiral's files are on this machine (changes the 401 advice); null
        /// means remote.</param>
        public HarborMetricsFeed(Func<ArmadaClient?> clientFactory, Func<string?> harborId, Func<bool>? isAdmiralLocal = null)
        {
            _ClientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _HarborId = harborId ?? throw new ArgumentNullException(nameof(harborId));
            _IsAdmiralLocal = isAdmiralLocal ?? (() => false);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Load the metrics for a range.
        /// </summary>
        /// <param name="range">1h, 24h, or 7d.</param>
        /// <param name="token">Cancellation token. Cancelling throws <see cref="OperationCanceledException"/>.</param>
        /// <returns>The result; never null.</returns>
        public async Task<HarborMetricsLoadResult> LoadAsync(string range, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            string wire = HarborMetricsRanges.TryParse(range, out Armada.Core.Enums.HarborMetricsRangeEnum parsed)
                ? HarborMetricsRanges.ToWireName(parsed)
                : HarborMetricsRanges.DefaultWireName;
            string harborId = (_HarborId() ?? String.Empty).Trim();
            HarborMetricsLoadResult result = new HarborMetricsLoadResult();
            result.Range = wire;
            result.HarborId = harborId;
            if (harborId.Length == 0) return Fail(result, HarborMetricsLoadStateEnum.NoHarborId, 0, String.Empty);

            ArmadaClient? client = _ClientFactory();
            if (client == null) return Fail(result, HarborMetricsLoadStateEnum.NoAddress, 0, String.Empty);
            using (client)
            {
                try
                {
                    HarborMetrics? metrics = await client.GetHarborMetricsAsync(harborId, wire, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (metrics == null) return Fail(result, HarborMetricsLoadStateEnum.Failed, 200, "The Admiral returned an empty response");
                    result.State = HarborMetricsLoadStateEnum.Loaded;
                    result.Metrics = metrics;
                    result.ReceivedUtc = DateTime.UtcNow;
                    return result;
                }
                catch (ArmadaApiException ex) when (!token.IsCancellationRequested)
                {
                    if (ex.StatusCode == (int)HttpStatusCode.NotFound || ex.StatusCode == (int)HttpStatusCode.MethodNotAllowed || ex.StatusCode == (int)HttpStatusCode.NotImplemented)
                    {
                        HarborMetricsLoadStateEnum missing = await ClassifyMissingAsync(client, harborId, token).ConfigureAwait(false);
                        return Fail(result, missing, ex.StatusCode, ex.Message);
                    }

                    return Fail(result, Classify(ex), ex.StatusCode, ex.Message);
                }
            }
        }

        /// <summary>
        /// The plain-language message for an outcome.
        /// </summary>
        /// <param name="state">Outcome.</param>
        /// <param name="harborId">Harbor ID.</param>
        /// <param name="statusCode">HTTP status, or 0.</param>
        /// <param name="admiralLocal">True when the Admiral's files are on this machine.</param>
        /// <returns>Message; empty for Loaded.</returns>
        public static string MessageFor(HarborMetricsLoadStateEnum state, string harborId, int statusCode, bool admiralLocal)
        {
            switch (state)
            {
                case HarborMetricsLoadStateEnum.Loaded:
                    return String.Empty;
                case HarborMetricsLoadStateEnum.NoAddress:
                    return "The Admiral address is not a ws:// or wss:// address, so Harbor cannot ask the Admiral for its charts.";
                case HarborMetricsLoadStateEnum.NoHarborId:
                    return "This Harbor has no ID yet. Save its settings once to create one.";
                case HarborMetricsLoadStateEnum.Unauthorized:
                    return admiralLocal
                        ? "The Admiral did not accept Harbor's credential: this Admiral's settings.json has no API key Harbor can use. Set an access key in Settings > General."
                        : "The Admiral did not accept Harbor's credential. Set an access key (an Armada credential) in Settings > General.";
                case HarborMetricsLoadStateEnum.Forbidden:
                    return "Harbor's credential may not read this Harbor's charts. Use an access key of the user who owns this Harbor, or of a tenant administrator.";
                case HarborMetricsLoadStateEnum.NotRegistered:
                    return "The Admiral has no Harbor with ID " + harborId + " yet. Charts appear once this Harbor has connected to the Admiral.";
                case HarborMetricsLoadStateEnum.AdmiralOutdated:
                    return "The Admiral needs updating: it is older than this Harbor and does not have Harbor charts. Update the Admiral to see them.";
                case HarborMetricsLoadStateEnum.Offline:
                    return "Cannot reach the Admiral. The charts load again when it is back.";
                default:
                    return statusCode > 0
                        ? "The Admiral could not return the charts (HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ")."
                        : "The Admiral could not return the charts.";
            }
        }

        #endregion

        #region Private-Methods

        private static HarborMetricsLoadStateEnum Classify(ArmadaApiException ex)
        {
            if (ex.IsTransport) return HarborMetricsLoadStateEnum.Offline;
            if (ex.StatusCode == (int)HttpStatusCode.Unauthorized) return HarborMetricsLoadStateEnum.Unauthorized;
            if (ex.StatusCode == (int)HttpStatusCode.Forbidden) return HarborMetricsLoadStateEnum.Forbidden;
            if (ex.StatusCode == (int)HttpStatusCode.BadGateway || ex.StatusCode == (int)HttpStatusCode.ServiceUnavailable || ex.StatusCode == (int)HttpStatusCode.GatewayTimeout)
                return HarborMetricsLoadStateEnum.Offline;
            return HarborMetricsLoadStateEnum.Failed;
        }

        private static async Task<HarborMetricsLoadStateEnum> ClassifyMissingAsync(ArmadaClient client, string harborId, CancellationToken token)
        {
            // The metrics route answered "not found": either the Admiral does not know this Harbor (a current Admiral),
            // or it has no such route (an older Admiral). Asking for the Harbor itself tells them apart.
            try
            {
                Harbor? harbor = await client.GetHarborAsync(harborId, token).ConfigureAwait(false);
                return harbor != null ? HarborMetricsLoadStateEnum.AdmiralOutdated : HarborMetricsLoadStateEnum.NotRegistered;
            }
            catch (ArmadaApiException ex) when (!token.IsCancellationRequested)
            {
                if (ex.StatusCode == (int)HttpStatusCode.NotFound) return HarborMetricsLoadStateEnum.NotRegistered;
                return Classify(ex);
            }
        }

        private HarborMetricsLoadResult Fail(HarborMetricsLoadResult result, HarborMetricsLoadStateEnum state, int statusCode, string detail)
        {
            result.State = state;
            result.StatusCode = statusCode;
            result.Detail = detail ?? String.Empty;
            result.Message = MessageFor(state, result.HarborId, statusCode, _IsAdmiralLocal());
            result.ReceivedUtc = DateTime.UtcNow;
            return result;
        }

        #endregion
    }
}
