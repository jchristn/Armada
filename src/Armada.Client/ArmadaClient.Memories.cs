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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listMemories</c>: GET `/api/v1/memories${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Memory>?> ListMemoriesAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Memory>>($"/api/v1/memories{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMemory</c>: GET `/api/v1/memories/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Memory?> GetMemoryAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Memory>($"/api/v1/memories/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createMemory</c>: POST '/api/v1/memories'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Memory?> CreateMemoryAsync(Memory data, CancellationToken token = default)
        {
            return PostAsync<Memory>("/api/v1/memories", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateMemory</c>: PUT `/api/v1/memories/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Memory?> UpdateMemoryAsync(string id, Memory data, CancellationToken token = default)
        {
            return PutAsync<Memory>($"/api/v1/memories/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteMemory</c>: DEL `/api/v1/memories/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteMemoryAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/memories/{E(id)}", null, null, token);
        }

        #endregion
    }
}
