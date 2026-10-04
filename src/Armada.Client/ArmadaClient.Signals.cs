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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listSignals</c>: GET `/api/v1/signals${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Signal>?> ListSignalsAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Signal>>($"/api/v1/signals{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getSignal</c>: GET `/api/v1/signals/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Signal?> GetSignalAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Signal>($"/api/v1/signals/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>sendSignal</c>: POST '/api/v1/signals'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Signal?> SendSignalAsync(SendSignalRequest data, CancellationToken token = default)
        {
            return PostAsync<Signal>("/api/v1/signals", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>markSignalRead</c>: PUT `/api/v1/signals/${id}/read`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task MarkSignalReadAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Put, $"/api/v1/signals/{E(id)}/read", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteSignalsBatch</c>: POST '/api/v1/signals/delete/multiple'.
        /// </summary>
        /// <param name="ids">ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<BatchDeleteResult?> DeleteSignalsBatchAsync(List<string> ids, CancellationToken token = default)
        {
            return PostAsync<BatchDeleteResult>("/api/v1/signals/delete/multiple", new { Ids = ids }, null, token);
        }

        #endregion
    }
}
