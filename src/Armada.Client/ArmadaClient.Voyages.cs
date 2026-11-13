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
        /// <returns>The voyage. The server replies with the voyage and its mission counts (<see cref="VoyageProgress"/>);
        /// this returns the voyage (use <see cref="GetVoyageDetailAsync"/> for the counts too).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public async Task<Voyage?> GetVoyageAsync(string id, CancellationToken token = default)
        {
            VoyageProgress? detail = await GetVoyageDetailAsync(id, token).ConfigureAwait(false);
            return detail?.Voyage;
        }

        /// <summary>
        /// GET `/api/v1/voyages/${id}` read as the server's shape: the voyage with counts of its missions. The missions
        /// themselves are read one page at a time with <see cref="ListMissionsAsync"/> filtered by voyageId.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The voyage and its mission counts.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VoyageProgress?> GetVoyageDetailAsync(string id, CancellationToken token = default)
        {
            return GetAsync<VoyageProgress>($"/api/v1/voyages/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateVoyageProgress</c>: POST '/api/v1/voyages/progress/enumerate'. One page of voyages with
        /// their mission counts; with no status filter, the active voyages (InProgress, then Open).
        /// </summary>
        /// <param name="query">Paging and filters (status, order), or null for the first page of active voyages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<VoyageProgress>?> EnumerateVoyageProgressAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<VoyageProgress>>("/api/v1/voyages/progress/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVoyageStatus</c>: GET /api/v1/voyages/{id}, the voyage with its mission counts, as raw JSON.
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
        /// <returns>The cancelled voyage with CancelledMissions and NotCancelledMissions counts.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VoyageCancelResult?> CancelVoyageAsync(string id, CancellationToken token = default)
        {
            return DeleteAsync<VoyageCancelResult>($"/api/v1/voyages/{E(id)}", null, token);
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
