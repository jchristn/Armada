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
    public sealed class PipelinesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal PipelinesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPipelines</c>: GET `/api/v1/pipelines${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listPipelines")]
        public Task<EnumerationResult<Pipeline>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Pipeline>>($"/api/v1/pipelines{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getPipeline</c>: GET `/api/v1/pipelines/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getPipeline")]
        public Task<Pipeline?> GetAsync(string name, CancellationToken token = default)
        {
            return _Transport.GetAsync<Pipeline>($"/api/v1/pipelines/{ArmadaQueryString.Escape(name)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPipeline</c>: POST '/api/v1/pipelines'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createPipeline")]
        public Task<Pipeline?> CreateAsync(Pipeline data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Pipeline>("/api/v1/pipelines", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updatePipeline</c>: PUT `/api/v1/pipelines/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updatePipeline")]
        public Task<Pipeline?> UpdateAsync(string name, Pipeline data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Pipeline>($"/api/v1/pipelines/{ArmadaQueryString.Escape(name)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deletePipeline</c>: DEL `/api/v1/pipelines/${encodeURIComponent(name)}`.
        /// </summary>
        /// <param name="name">name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deletePipeline")]
        public Task DeleteAsync(string name, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/pipelines/{ArmadaQueryString.Escape(name)}", null, null, token);
        }

        #endregion
    }
}
