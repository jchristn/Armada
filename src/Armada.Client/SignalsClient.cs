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
    /// Signals API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class SignalsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal SignalsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listSignals</c>: GET `/api/v1/signals${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listSignals")]
        public Task<EnumerationResult<Signal>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Signal>>($"/api/v1/signals{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getSignal</c>: GET `/api/v1/signals/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getSignal")]
        public Task<Signal?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Signal>($"/api/v1/signals/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>sendSignal</c>: POST '/api/v1/signals'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("sendSignal")]
        public Task<Signal?> SendAsync(SendSignalRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Signal>("/api/v1/signals", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>markSignalRead</c>: PUT `/api/v1/signals/${id}/read`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("markSignalRead")]
        public Task MarkReadAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Put, $"/api/v1/signals/{ArmadaQueryString.Escape(id)}/read", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteSignalsBatch</c>: POST '/api/v1/signals/delete/multiple'.
        /// </summary>
        /// <param name="ids">ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteSignalsBatch")]
        public Task<BatchDeleteResult?> DeleteBatchAsync(List<string> ids, CancellationToken token = default)
        {
            return _Transport.PostAsync<BatchDeleteResult>("/api/v1/signals/delete/multiple", new { Ids = ids }, null, token);
        }

        #endregion
    }
}
