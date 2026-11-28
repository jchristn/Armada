namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// ModelEndpoints API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class ModelEndpointsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal ModelEndpointsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listModelEndpoints</c>: GET '/api/v1/model-endpoints'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listModelEndpoints")]
        public Task<List<ModelEndpoint>?> ListAsync(CancellationToken token = default)
        {
            return _Transport.EnumerateAllAsync<ModelEndpoint>("/api/v1/model-endpoints/enumerate", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateModelEndpoints</c>: POST /api/v1/model-endpoints/enumerate, one page of the endpoints visible
        /// to the caller with the exact total.
        /// </summary>
        /// <param name="query">Paging (PageSize 1 to 1000) and created-time filters, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateModelEndpoints")]
        public Task<EnumerationResult<ModelEndpoint>?> EnumerateAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<ModelEndpoint>>("/api/v1/model-endpoints/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getModelEndpoint</c>: GET `/api/v1/model-endpoints/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getModelEndpoint")]
        public Task<ModelEndpoint?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<ModelEndpoint>($"/api/v1/model-endpoints/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createModelEndpoint</c>: POST /api/v1/model-endpoints. The API key is write-only and never returned.
        /// </summary>
        /// <param name="data">Endpoint.</param>
        /// <param name="apiKey">API key or secret, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createModelEndpoint")]
        public Task<ModelEndpoint?> CreateAsync(ModelEndpoint data, string? apiKey = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<ModelEndpoint>("/api/v1/model-endpoints", WithApiKey(data, apiKey), null, token);
        }

        /// <summary>Create an edited copy of an endpoint, inheriting omitted credentials server-side.</summary>
        /// <param name="sourceId">Editable source endpoint.</param>
        /// <param name="data">Complete edited configuration for the new endpoint.</param>
        /// <param name="apiKey">Replacement key; null inherits the source key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created endpoint; secret values are never returned.</returns>
        [DashboardExport("duplicateModelEndpoint")]
        public Task<ModelEndpoint?> DuplicateAsync(string sourceId, ModelEndpoint data, string? apiKey = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<ModelEndpoint>($"/api/v1/model-endpoints/{ArmadaQueryString.Escape(sourceId)}/duplicate", WithApiKey(data, apiKey), null, token);
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
        [DashboardExport("updateModelEndpoint")]
        public Task<ModelEndpoint?> UpdateAsync(string id, ModelEndpoint data, string? apiKey = null, CancellationToken token = default)
        {
            return _Transport.PutAsync<ModelEndpoint>($"/api/v1/model-endpoints/{ArmadaQueryString.Escape(id)}", WithApiKey(data, apiKey), null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteModelEndpoint</c>: DEL `/api/v1/model-endpoints/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteModelEndpoint")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/model-endpoints/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>validateModelEndpoint</c>: POST `/api/v1/model-endpoints/${encodeURIComponent(id)}/validate`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("validateModelEndpoint")]
        public Task<ModelEndpointProbeResult?> ValidateAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<ModelEndpointProbeResult>($"/api/v1/model-endpoints/{ArmadaQueryString.Escape(id)}/validate", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>healthCheckModelEndpoints</c>: POST '/api/v1/model-endpoints/health-check'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("healthCheckModelEndpoints")]
        public Task<ModelEndpointHealthSweepResponse?> HealthCheckAsync(CancellationToken token = default)
        {
            return _Transport.PostAsync<ModelEndpointHealthSweepResponse>("/api/v1/model-endpoints/health-check", null, null, token);
        }

        #endregion

        #region Private-Methods

        private static JsonObject WithApiKey(ModelEndpoint data, string? apiKey)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            JsonNode? node = JsonSerializer.SerializeToNode(data, ArmadaJson.Options);
            JsonObject obj = node as JsonObject ?? new JsonObject();
            if (apiKey != null) obj["apiKey"] = apiKey;
            // A newly constructed update that does not mention inference settings must preserve the stored policy.
            // Also honor callers that mutate a nondefault setting through the options getter.
            if (!data.InferenceOptionsSpecified && !data.InferenceOptions.HasNonDefaultSettings) obj.Remove("inferenceOptions");

            // Header values are write-only, so the model never serializes them: send the list (values included)
            // only when the caller set it, and otherwise leave it out so the server keeps the stored headers.
            obj.Remove("headers");
            if (data.HeadersSpecified)
            {
                JsonArray headers = new JsonArray();
                foreach (ModelEndpointHeader header in data.Headers)
                {
                    JsonObject entry = new JsonObject();
                    entry["name"] = header.Name;
                    entry["value"] = header.Value;
                    headers.Add(entry);
                }

                obj["headers"] = headers;
            }

            return obj;
        }

        #endregion
    }
}
