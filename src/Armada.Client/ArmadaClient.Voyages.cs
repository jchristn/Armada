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
    /// Voyages API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listVoyages</c>: GET `/api/v1/voyages${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Voyage>?> ListVoyagesAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Voyage>>($"/api/v1/voyages{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVoyage</c>: GET `/api/v1/voyages/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Voyage?> GetVoyageAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Voyage>($"/api/v1/voyages/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVoyageStatus</c>: GET `/api/v1/voyages/${id}/status`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ArmadaRawJson?> GetVoyageStatusAsync(string id, CancellationToken token = default)
        {
            return GetAsync<ArmadaRawJson>($"/api/v1/voyages/{E(id)}/status", null, token);
        }

        /// <summary>
        /// Dashboard <c>createVoyage</c>: POST '/api/v1/voyages'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Voyage?> CreateVoyageAsync(VoyageCreateRequest data, CancellationToken token = default)
        {
            return PostAsync<Voyage>("/api/v1/voyages", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelVoyage</c>: DEL `/api/v1/voyages/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task CancelVoyageAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/voyages/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>purgeVoyage</c>: DEL `/api/v1/voyages/${id}/purge`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task PurgeVoyageAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/voyages/{E(id)}/purge", null, null, token);
        }

        #endregion
    }
}
