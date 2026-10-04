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
    /// VesselImport API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>browseVesselImport</c>: GET /api/v1/vessels/import/browse (path sent base64url-encoded; null lists the allowed roots).
        /// </summary>
        /// <param name="path">Directory on the Admiral host, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselBrowseResult?> BrowseVesselImportAsync(string? path = null, CancellationToken token = default)
        {
            return GetAsync<VesselBrowseResult>("/api/v1/vessels/import/browse" + (String.IsNullOrEmpty(path) ? "" : "?path=" + ArmadaQueryString.Base64Url(path)), null, token);
        }

        /// <summary>
        /// Dashboard <c>discoverVesselImport</c>: POST '/api/v1/vessels/import/discover'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselImportDiscoverResponse?> DiscoverVesselImportAsync(VesselDiscoveryRequest data, CancellationToken token = default)
        {
            return PostAsync<VesselImportDiscoverResponse>("/api/v1/vessels/import/discover", data, ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>importVessels</c>: POST '/api/v1/vessels/import'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselImportResponse?> ImportVesselsAsync(VesselImportRequest data, CancellationToken token = default)
        {
            return PostAsync<VesselImportResponse>("/api/v1/vessels/import", data, ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>enumerateVesselImportBatches</c>: POST /api/v1/vessels/import/batches/enumerate.
        /// </summary>
        /// <param name="query">Query, or null for page 1 of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<VesselImportBatch>?> EnumerateVesselImportBatchesAsync(VesselImportBatchEnumerateQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<VesselImportBatch>>("/api/v1/vessels/import/batches/enumerate", query ?? new VesselImportBatchEnumerateQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselImportBatch</c>: GET `/api/v1/vessels/import/batches/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselImportBatchDetail?> GetVesselImportBatchAsync(string id, CancellationToken token = default)
        {
            return GetAsync<VesselImportBatchDetail>($"/api/v1/vessels/import/batches/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetCategorizationDefaultPrompt</c>: GET '/api/v1/vessels/import/categorization/default-prompt'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetCategorizationDefaultPrompt?> GetFleetCategorizationDefaultPromptAsync(CancellationToken token = default)
        {
            return GetAsync<FleetCategorizationDefaultPrompt>("/api/v1/vessels/import/categorization/default-prompt", null, token);
        }

        /// <summary>
        /// Dashboard <c>categorizeVesselImport</c>: POST /api/v1/vessels/import/batches/{id}/categorize (omitted fields reuse the previous run).
        /// </summary>
        /// <param name="id">Batch id.</param>
        /// <param name="data">Captain and instructions, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselImportBatch?> CategorizeVesselImportAsync(string id, VesselImportCategorizationRequest? data = null, CancellationToken token = default)
        {
            return PostAsync<VesselImportBatch>($"/api/v1/vessels/import/batches/{E(id)}/categorize", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>applyFleetRecommendations</c>: POST `/api/v1/vessels/import/batches/${encodeURIComponent(id)}/fleet-recommendations/apply`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetRecommendationApplyResult?> ApplyFleetRecommendationsAsync(string id, FleetRecommendationApplyRequest data, CancellationToken token = default)
        {
            return PostAsync<FleetRecommendationApplyResult>($"/api/v1/vessels/import/batches/{E(id)}/fleet-recommendations/apply", data, null, token);
        }

        #endregion
    }
}
