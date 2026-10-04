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
    /// Fleets API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listFleets</c>: GET `/api/v1/fleets${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Fleet>?> ListFleetsAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Fleet>>($"/api/v1/fleets{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleet</c>: GET `/api/v1/fleets/${id}` (returns the fleet with its vessels).
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetDetail?> GetFleetAsync(string id, CancellationToken token = default)
        {
            return GetAsync<FleetDetail>($"/api/v1/fleets/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createFleet</c>: POST '/api/v1/fleets'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Fleet?> CreateFleetAsync(Fleet data, CancellationToken token = default)
        {
            return PostAsync<Fleet>("/api/v1/fleets", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateFleet</c>: PUT `/api/v1/fleets/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Fleet?> UpdateFleetAsync(string id, Fleet data, CancellationToken token = default)
        {
            return PutAsync<Fleet>($"/api/v1/fleets/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteFleet</c>: DEL `/api/v1/fleets/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteFleetAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/fleets/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteFleetsBatch</c>: POST '/api/v1/fleets/delete/multiple'.
        /// </summary>
        /// <param name="ids">ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<BatchDeleteResult?> DeleteFleetsBatchAsync(List<string> ids, CancellationToken token = default)
        {
            return PostAsync<BatchDeleteResult>("/api/v1/fleets/delete/multiple", new { Ids = ids }, null, token);
        }

        #endregion
    }
}
