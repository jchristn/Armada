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
        /// <returns>The voyage. The server replies with <c>{ Voyage, Missions }</c>; this returns the voyage (use
        /// <see cref="GetVoyageDetailAsync"/> for the missions too).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task<Voyage?> GetVoyageAsync(string id, CancellationToken token = default)
        {
            VoyageDetail? detail = await GetVoyageDetailAsync(id, token).ConfigureAwait(false);
            return detail?.Voyage;
        }

        /// <summary>
        /// GET `/api/v1/voyages/${id}` read as the server's actual shape, <c>{ Voyage, Missions }</c> (the dashboard's
        /// <c>getVoyage</c> unwraps it the same way).
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The voyage and its missions.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VoyageDetail?> GetVoyageDetailAsync(string id, CancellationToken token = default)
        {
            return GetAsync<VoyageDetail>($"/api/v1/voyages/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVoyageStatus</c>. The dashboard reads <c>/api/v1/voyages/{id}/status</c>, which the server does
        /// not have; this returns the voyage with its missions (GET /api/v1/voyages/{id}, <c>{ Voyage, Missions }</c>) as raw JSON.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ArmadaRawJson?> GetVoyageStatusAsync(string id, CancellationToken token = default)
        {
            return GetAsync<ArmadaRawJson>($"/api/v1/voyages/{E(id)}", null, token);
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
