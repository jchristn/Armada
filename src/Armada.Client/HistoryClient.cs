namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// History API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class HistoryClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal HistoryClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listHistoryTimeline</c>: GET `/api/v1/history${buildHistoryQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listHistoryTimeline")]
        public Task<EnumerationResult<HistoricalTimelineEntry>?> ListTimelineAsync(HistoricalTimelineQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<HistoricalTimelineEntry>>($"/api/v1/history{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateHistoryTimeline</c>: POST '/api/v1/history/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateHistoryTimeline")]
        public Task<EnumerationResult<HistoricalTimelineEntry>?> EnumerateTimelineAsync(HistoricalTimelineQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<HistoricalTimelineEntry>>("/api/v1/history/enumerate", query, null, token);
        }

        #endregion
    }
}
