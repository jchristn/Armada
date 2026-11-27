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
    public sealed class RequestHistoryClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal RequestHistoryClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listRequestHistory</c>: GET `/api/v1/request-history${buildRequestHistoryQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listRequestHistory")]
        public Task<EnumerationResult<RequestHistoryEntry>?> ListAsync(RequestHistoryQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<RequestHistoryEntry>>($"/api/v1/request-history{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// POST /api/v1/request-history/enumerate: one page of the captured REST requests visible to the caller, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<RequestHistoryEntry>?> EnumerateAsync(RequestHistoryQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<RequestHistoryEntry>>("/api/v1/request-history/enumerate", query ?? new RequestHistoryQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getRequestHistorySummary</c>: GET `/api/v1/request-history/summary${buildRequestHistoryQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getRequestHistorySummary")]
        public Task<RequestHistorySummaryResult?> GetSummaryAsync(RequestHistoryQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<RequestHistorySummaryResult>($"/api/v1/request-history/summary{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getRequestHistoryEntry</c>: GET `/api/v1/request-history/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getRequestHistoryEntry")]
        public Task<RequestHistoryRecord?> GetEntryAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<RequestHistoryRecord>($"/api/v1/request-history/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRequestHistoryEntry</c>: DEL `/api/v1/request-history/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteRequestHistoryEntry")]
        public Task DeleteEntryAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/request-history/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRequestHistoryEntries</c>: POST '/api/v1/request-history/delete/multiple'.
        /// </summary>
        /// <param name="ids">ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteRequestHistoryEntries")]
        public Task<BatchDeleteResult?> DeleteEntriesAsync(List<string> ids, CancellationToken token = default)
        {
            return _Transport.PostAsync<BatchDeleteResult>("/api/v1/request-history/delete/multiple", new { Ids = ids }, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRequestHistoryByFilter</c>: POST '/api/v1/request-history/delete/by-filter'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteRequestHistoryByFilter")]
        public Task<BatchDeleteResult?> DeleteByFilterAsync(RequestHistoryQuery query, CancellationToken token = default)
        {
            return _Transport.PostAsync<BatchDeleteResult>("/api/v1/request-history/delete/by-filter", query, null, token);
        }

        #endregion
    }
}
