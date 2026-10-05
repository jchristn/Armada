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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listMergeQueue</c>: GET `/api/v1/merge-queue${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<MergeEntry>?> ListMergeQueueAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<MergeEntry>>($"/api/v1/merge-queue{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMergeEntry</c>: GET `/api/v1/merge-queue/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<MergeEntry?> GetMergeEntryAsync(string id, CancellationToken token = default)
        {
            return GetAsync<MergeEntry>($"/api/v1/merge-queue/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enqueueMerge</c>: POST '/api/v1/merge-queue'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<MergeEntry?> EnqueueMergeAsync(MergeEntry data, CancellationToken token = default)
        {
            return PostAsync<MergeEntry>("/api/v1/merge-queue", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteMergeEntry</c>: DEL `/api/v1/merge-queue/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteMergeEntryAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/merge-queue/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>processMergeEntry</c>: POST `/api/v1/merge-queue/${id}/process`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task ProcessMergeEntryAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, $"/api/v1/merge-queue/{E(id)}/process", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>processAllMergeQueue</c>. The dashboard posts to <c>/api/v1/merge-queue/process-all</c>, which the
        /// server does not have; this calls the server's route, POST /api/v1/merge-queue/process.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task ProcessAllMergeQueueAsync(CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Post, "/api/v1/merge-queue/process", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelMergeEntry</c>. The dashboard posts to <c>/api/v1/merge-queue/{id}/cancel</c>, which the
        /// server does not have; this calls the server's cancel route, DELETE /api/v1/merge-queue/{id} ("Delete or cancel a
        /// merge queue entry"), the same call as <see cref="DeleteMergeEntryAsync"/>.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task CancelMergeEntryAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/merge-queue/{E(id)}", null, null, token);
        }

        #endregion
    }
}
