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
    /// RequestHistory API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listRequestHistory</c>: GET `/api/v1/request-history${buildRequestHistoryQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<RequestHistoryEntry>?> ListRequestHistoryAsync(RequestHistoryQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<RequestHistoryEntry>>($"/api/v1/request-history{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getRequestHistorySummary</c>: GET `/api/v1/request-history/summary${buildRequestHistoryQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<RequestHistorySummaryResult?> GetRequestHistorySummaryAsync(RequestHistoryQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<RequestHistorySummaryResult>($"/api/v1/request-history/summary{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getRequestHistoryEntry</c>: GET `/api/v1/request-history/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<RequestHistoryRecord?> GetRequestHistoryEntryAsync(string id, CancellationToken token = default)
        {
            return GetAsync<RequestHistoryRecord>($"/api/v1/request-history/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRequestHistoryEntry</c>: DEL `/api/v1/request-history/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteRequestHistoryEntryAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/request-history/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRequestHistoryEntries</c>: POST '/api/v1/request-history/delete/multiple'.
        /// </summary>
        /// <param name="ids">ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<BatchDeleteResult?> DeleteRequestHistoryEntriesAsync(List<string> ids, CancellationToken token = default)
        {
            return PostAsync<BatchDeleteResult>("/api/v1/request-history/delete/multiple", new { Ids = ids }, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRequestHistoryByFilter</c>: POST '/api/v1/request-history/delete/by-filter'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<BatchDeleteResult?> DeleteRequestHistoryByFilterAsync(RequestHistoryQuery query, CancellationToken token = default)
        {
            return PostAsync<BatchDeleteResult>("/api/v1/request-history/delete/by-filter", query, null, token);
        }

        #endregion
    }
}
