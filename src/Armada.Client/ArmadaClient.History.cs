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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listHistoryTimeline</c>: GET `/api/v1/history${buildHistoryQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<HistoricalTimelineEntry>?> ListHistoryTimelineAsync(HistoricalTimelineQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<HistoricalTimelineEntry>>($"/api/v1/history{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateHistoryTimeline</c>: POST '/api/v1/history/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<HistoricalTimelineEntry>?> EnumerateHistoryTimelineAsync(HistoricalTimelineQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<HistoricalTimelineEntry>>("/api/v1/history/enumerate", query, null, token);
        }

        #endregion
    }
}
