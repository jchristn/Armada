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
    /// Pipelines API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPipelines</c>: GET `/api/v1/pipelines${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Pipeline>?> ListPipelinesAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Pipeline>>($"/api/v1/pipelines{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getPipeline</c>: GET `/api/v1/pipelines/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Pipeline?> GetPipelineAsync(string name, CancellationToken token = default)
        {
            return GetAsync<Pipeline>($"/api/v1/pipelines/{E(name)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPipeline</c>: POST '/api/v1/pipelines'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Pipeline?> CreatePipelineAsync(Pipeline data, CancellationToken token = default)
        {
            return PostAsync<Pipeline>("/api/v1/pipelines", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updatePipeline</c>: PUT `/api/v1/pipelines/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Pipeline?> UpdatePipelineAsync(string name, Pipeline data, CancellationToken token = default)
        {
            return PutAsync<Pipeline>($"/api/v1/pipelines/{E(name)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deletePipeline</c>: DEL `/api/v1/pipelines/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeletePipelineAsync(string name, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/pipelines/{E(name)}", null, null, token);
        }

        #endregion
    }
}
