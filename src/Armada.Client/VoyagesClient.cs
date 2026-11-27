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
    public sealed class VoyagesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal VoyagesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listVoyages</c>: GET `/api/v1/voyages${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listVoyages")]
        public Task<EnumerationResult<Voyage>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Voyage>>($"/api/v1/voyages{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVoyage</c>: GET `/api/v1/voyages/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The voyage. The server replies with the voyage and its mission counts (<see cref="VoyageProgress"/>);
        /// this returns the voyage (use <see cref="GetDetailAsync"/> for the counts too).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVoyage")]
        public async Task<Voyage?> GetAsync(string id, CancellationToken token = default)
        {
            VoyageProgress? detail = await GetDetailAsync(id, token).ConfigureAwait(false);
            return detail?.Voyage;
        }

        /// <summary>
        /// GET `/api/v1/voyages/${id}` read as the server's shape: the voyage with counts of its missions. The missions
        /// themselves are read one page at a time with <see cref="MissionsClient.ListAsync"/> filtered by voyageId.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The voyage and its mission counts.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VoyageProgress?> GetDetailAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<VoyageProgress>($"/api/v1/voyages/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateVoyageProgress</c>: POST '/api/v1/voyages/progress/enumerate'. One page of voyages with
        /// their mission counts; with no status filter, the active voyages (InProgress, then Open).
        /// </summary>
        /// <param name="query">Paging and filters (status, order), or null for the first page of active voyages.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateVoyageProgress")]
        public Task<EnumerationResult<VoyageProgress>?> EnumerateProgressAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<VoyageProgress>>("/api/v1/voyages/progress/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVoyageStatus</c>: GET /api/v1/voyages/{id}, the voyage with its mission counts, as raw JSON.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVoyageStatus")]
        public Task<ArmadaRawJson?> GetStatusAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<ArmadaRawJson>($"/api/v1/voyages/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createVoyage</c>: POST '/api/v1/voyages'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createVoyage")]
        public Task<Voyage?> CreateAsync(VoyageCreateRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Voyage>("/api/v1/voyages", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelVoyage</c>: DEL `/api/v1/voyages/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The cancelled voyage with CancelledMissions and NotCancelledMissions counts.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("cancelVoyage")]
        public Task<VoyageCancelResult?> CancelAsync(string id, CancellationToken token = default)
        {
            return _Transport.DeleteAsync<VoyageCancelResult>($"/api/v1/voyages/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>purgeVoyage</c>: DEL `/api/v1/voyages/${id}/purge`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("purgeVoyage")]
        public Task PurgeAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/voyages/{ArmadaQueryString.Escape(id)}/purge", null, null, token);
        }

        #endregion
    }
}
