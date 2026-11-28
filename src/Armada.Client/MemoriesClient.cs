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
    /// Memories API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class MemoriesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal MemoriesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listMemories</c>: GET `/api/v1/memories${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listMemories")]
        public Task<EnumerationResult<Memory>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Memory>>($"/api/v1/memories{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateMemories</c>: POST '/api/v1/memories/enumerate'. Filtering, sorting, and paging run on
        /// the server. A null query uses the relevance order (salience, then newest first); otherwise set
        /// <see cref="MemoryEnumerateRequest.SortBy"/> and <see cref="EnumerationQuery.Order"/> (the direction).
        /// </summary>
        /// <param name="query">Filters, sort, and paging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateMemories")]
        public Task<EnumerationResult<Memory>?> EnumerateAsync(MemoryEnumerateRequest? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<Memory>>("/api/v1/memories/enumerate", query ?? new MemoryEnumerateRequest { SortBy = MemorySortEnum.Salience }, null, token);
        }

        /// <summary>
        /// Dashboard <c>getMemory</c>: GET `/api/v1/memories/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMemory")]
        public Task<Memory?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Memory>($"/api/v1/memories/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createMemory</c>: POST '/api/v1/memories'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createMemory")]
        public Task<Memory?> CreateAsync(Memory data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Memory>("/api/v1/memories", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateMemory</c>: PUT `/api/v1/memories/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateMemory")]
        public Task<Memory?> UpdateAsync(string id, Memory data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Memory>($"/api/v1/memories/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteMemory</c>: DEL `/api/v1/memories/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteMemory")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/memories/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}
