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
    /// ModelEndpoints API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listModelEndpoints</c>: GET '/api/v1/model-endpoints'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<ModelEndpoint>?> ListModelEndpointsAsync(CancellationToken token = default)
        {
            return GetAsync<List<ModelEndpoint>>("/api/v1/model-endpoints", null, token);
        }

        /// <summary>
        /// Dashboard <c>getModelEndpoint</c>: GET `/api/v1/model-endpoints/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelEndpoint?> GetModelEndpointAsync(string id, CancellationToken token = default)
        {
            return GetAsync<ModelEndpoint>($"/api/v1/model-endpoints/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createModelEndpoint</c>: POST /api/v1/model-endpoints. The API key is write-only and never returned.
        /// </summary>
        /// <param name="data">Endpoint.</param>
        /// <param name="apiKey">API key or secret, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelEndpoint?> CreateModelEndpointAsync(ModelEndpoint data, string? apiKey = null, CancellationToken token = default)
        {
            return PostAsync<ModelEndpoint>("/api/v1/model-endpoints", WithApiKey(data, apiKey), null, token);
        }

        /// <summary>
        /// Dashboard <c>updateModelEndpoint</c>: PUT /api/v1/model-endpoints/{id}. A null API key keeps the stored key.
        /// </summary>
        /// <param name="id">Endpoint id.</param>
        /// <param name="data">Endpoint.</param>
        /// <param name="apiKey">New API key, or null to keep the stored one.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelEndpoint?> UpdateModelEndpointAsync(string id, ModelEndpoint data, string? apiKey = null, CancellationToken token = default)
        {
            return PutAsync<ModelEndpoint>($"/api/v1/model-endpoints/{E(id)}", WithApiKey(data, apiKey), null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteModelEndpoint</c>: DEL `/api/v1/model-endpoints/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteModelEndpointAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/model-endpoints/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>validateModelEndpoint</c>: POST `/api/v1/model-endpoints/${encodeURIComponent(id)}/validate`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelEndpointProbeResult?> ValidateModelEndpointAsync(string id, CancellationToken token = default)
        {
            return PostAsync<ModelEndpointProbeResult>($"/api/v1/model-endpoints/{E(id)}/validate", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>healthCheckModelEndpoints</c>: POST '/api/v1/model-endpoints/health-check'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelEndpointHealthSweepResponse?> HealthCheckModelEndpointsAsync(CancellationToken token = default)
        {
            return PostAsync<ModelEndpointHealthSweepResponse>("/api/v1/model-endpoints/health-check", null, null, token);
        }

        #endregion
    }
}
