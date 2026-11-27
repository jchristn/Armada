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
    /// Environments API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class EnvironmentsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal EnvironmentsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listEnvironments</c>: GET `/api/v1/environments${buildEnvironmentQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listEnvironments")]
        public Task<EnumerationResult<DeploymentEnvironment>?> ListAsync(DeploymentEnvironmentQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<DeploymentEnvironment>>($"/api/v1/environments{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateEnvironments</c>: POST '/api/v1/environments/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateEnvironments")]
        public Task<EnumerationResult<DeploymentEnvironment>?> EnumerateAsync(DeploymentEnvironmentQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<DeploymentEnvironment>>("/api/v1/environments/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getEnvironment</c>: GET `/api/v1/environments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getEnvironment")]
        public Task<DeploymentEnvironment?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<DeploymentEnvironment>($"/api/v1/environments/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createEnvironment</c>: POST '/api/v1/environments'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createEnvironment")]
        public Task<DeploymentEnvironment?> CreateAsync(DeploymentEnvironmentUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<DeploymentEnvironment>("/api/v1/environments", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateEnvironment</c>: PUT `/api/v1/environments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateEnvironment")]
        public Task<DeploymentEnvironment?> UpdateAsync(string id, DeploymentEnvironmentUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<DeploymentEnvironment>($"/api/v1/environments/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteEnvironment</c>: DEL `/api/v1/environments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteEnvironment")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/environments/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}
