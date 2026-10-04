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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listEnvironments</c>: GET `/api/v1/environments${buildEnvironmentQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<DeploymentEnvironment>?> ListEnvironmentsAsync(DeploymentEnvironmentQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<DeploymentEnvironment>>($"/api/v1/environments{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateEnvironments</c>: POST '/api/v1/environments/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<DeploymentEnvironment>?> EnumerateEnvironmentsAsync(DeploymentEnvironmentQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<DeploymentEnvironment>>("/api/v1/environments/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getEnvironment</c>: GET `/api/v1/environments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<DeploymentEnvironment?> GetEnvironmentAsync(string id, CancellationToken token = default)
        {
            return GetAsync<DeploymentEnvironment>($"/api/v1/environments/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createEnvironment</c>: POST '/api/v1/environments'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<DeploymentEnvironment?> CreateEnvironmentAsync(DeploymentEnvironmentUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<DeploymentEnvironment>("/api/v1/environments", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateEnvironment</c>: PUT `/api/v1/environments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<DeploymentEnvironment?> UpdateEnvironmentAsync(string id, DeploymentEnvironmentUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<DeploymentEnvironment>($"/api/v1/environments/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteEnvironment</c>: DEL `/api/v1/environments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteEnvironmentAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/environments/{E(id)}", null, null, token);
        }

        #endregion
    }
}
