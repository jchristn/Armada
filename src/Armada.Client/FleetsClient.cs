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
    public sealed class FleetsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal FleetsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listFleets</c>: GET `/api/v1/fleets${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listFleets")]
        public Task<EnumerationResult<Fleet>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Fleet>>($"/api/v1/fleets{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleet</c>: GET `/api/v1/fleets/${id}` (returns the fleet and its vessel count).
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getFleet")]
        public Task<FleetDetail?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<FleetDetail>($"/api/v1/fleets/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetDetail</c>: GET `/api/v1/fleets/${id}` (the fleet and its vessel count). Same as
        /// <see cref="GetAsync"/>.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getFleetDetail")]
        public Task<FleetDetail?> GetDetailAsync(string id, CancellationToken token = default)
        {
            return GetAsync(id, token);
        }

        /// <summary>
        /// Dashboard <c>createFleet</c>: POST '/api/v1/fleets'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createFleet")]
        public Task<Fleet?> CreateAsync(Fleet data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Fleet>("/api/v1/fleets", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateFleet</c>: PUT `/api/v1/fleets/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateFleet")]
        public Task<Fleet?> UpdateAsync(string id, Fleet data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Fleet>($"/api/v1/fleets/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteFleet</c>: DEL `/api/v1/fleets/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteFleet")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/fleets/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteFleetsBatch</c>: POST '/api/v1/fleets/delete/multiple'.
        /// </summary>
        /// <param name="ids">ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteFleetsBatch")]
        public Task<BatchDeleteResult?> DeleteBatchAsync(List<string> ids, CancellationToken token = default)
        {
            return _Transport.PostAsync<BatchDeleteResult>("/api/v1/fleets/delete/multiple", new { Ids = ids }, null, token);
        }

        #endregion
    }
}
