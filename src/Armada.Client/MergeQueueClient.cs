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
    /// MergeQueue API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class MergeQueueClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal MergeQueueClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listMergeQueue</c>: GET `/api/v1/merge-queue${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listMergeQueue")]
        public Task<EnumerationResult<MergeEntry>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<MergeEntry>>($"/api/v1/merge-queue{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMergeEntry</c>: GET `/api/v1/merge-queue/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMergeEntry")]
        public Task<MergeEntry?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<MergeEntry>($"/api/v1/merge-queue/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enqueueMerge</c>: POST '/api/v1/merge-queue'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enqueueMerge")]
        public Task<MergeEntry?> EnqueueAsync(MergeEntry data, CancellationToken token = default)
        {
            return _Transport.PostAsync<MergeEntry>("/api/v1/merge-queue", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteMergeEntry</c>: DEL `/api/v1/merge-queue/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteMergeEntry")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/merge-queue/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>processMergeEntry</c>: POST `/api/v1/merge-queue/${id}/process`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("processMergeEntry")]
        public Task ProcessAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Post, $"/api/v1/merge-queue/{ArmadaQueryString.Escape(id)}/process", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>processAllMergeQueue</c>: POST /api/v1/merge-queue/process.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("processAllMergeQueue")]
        public Task ProcessAllAsync(CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Post, "/api/v1/merge-queue/process", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelMergeEntry</c>: DELETE /api/v1/merge-queue/{id} ("Delete or cancel a merge queue entry"), the
        /// same call as <see cref="DeleteAsync"/>.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("cancelMergeEntry")]
        public Task CancelAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/merge-queue/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}
