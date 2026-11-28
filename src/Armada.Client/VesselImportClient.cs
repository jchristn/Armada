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
    public sealed class VesselImportClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal VesselImportClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>browseVesselImport</c>: GET /api/v1/vessels/import/browse (path sent base64url-encoded; null lists the allowed roots).
        /// </summary>
        /// One page of the directory (TotalRecords, TotalPages, and Truncated describe the rest); use
        /// <see cref="BrowseAllAsync"/> to read every entry.
        /// <param name="path">Directory on the Admiral host, or null.</param>
        /// <param name="page">Paging (page size 1 to 1000); null is the server default (first page of 10).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("browseVesselImport")]
        public Task<VesselBrowseResult?> BrowseAsync(string? path = null, EnumerationQuery? page = null, CancellationToken token = default)
        {
            List<string> parts = new List<string>();
            if (!String.IsNullOrEmpty(path)) parts.Add("path=" + ArmadaQueryString.Base64Url(path));
            if (page != null)
            {
                parts.Add("pageNumber=" + page.PageNumber);
                parts.Add("pageSize=" + page.PageSize);
            }

            return _Transport.GetAsync<VesselBrowseResult>("/api/v1/vessels/import/browse" + (parts.Count == 0 ? "" : "?" + String.Join("&", parts)), null, token);
        }

        /// <summary>
        /// Dashboard <c>browseAllVesselImport</c>: every entry of one browse directory, read in explicit pages of 1000
        /// (at most 100 pages). Truncated is set when the server examined fewer entries than the directory holds or
        /// the page cap was reached.
        /// </summary>
        /// <param name="path">Directory on the Admiral host, or null for the allowed roots.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The first page's fields with every entry, or null.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("browseAllVesselImport")]
        public async Task<VesselBrowseResult?> BrowseAllAsync(string? path = null, CancellationToken token = default)
        {
            VesselBrowseResult? first = await BrowseAsync(path, new EnumerationQuery { PageNumber = 1, PageSize = 1000 }, token).ConfigureAwait(false);
            if (first == null) return null;
            int pages = Math.Min(first.TotalPages, 100);
            for (int pageNumber = 2; pageNumber <= pages; pageNumber++)
            {
                token.ThrowIfCancellationRequested();
                VesselBrowseResult? next = await BrowseAsync(path, new EnumerationQuery { PageNumber = pageNumber, PageSize = 1000 }, token).ConfigureAwait(false);
                if (next == null || next.Entries.Count == 0) break;
                first.Entries.AddRange(next.Entries);
            }

            if (first.TotalPages > pages) first.Truncated = true;
            return first;
        }

        /// <summary>
        /// Dashboard <c>discoverVesselImport</c>: POST '/api/v1/vessels/import/discover'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("discoverVesselImport")]
        public Task<VesselImportDiscoverResponse?> DiscoverAsync(VesselDiscoveryRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<VesselImportDiscoverResponse>("/api/v1/vessels/import/discover", data, ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>importVessels</c>: POST '/api/v1/vessels/import'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("importVessels")]
        public Task<VesselImportResponse?> ImportAsync(VesselImportRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<VesselImportResponse>("/api/v1/vessels/import", data, ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>enumerateVesselImportBatches</c>: POST /api/v1/vessels/import/batches/enumerate.
        /// </summary>
        /// <param name="query">Query, or null for page 1 of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateVesselImportBatches")]
        public Task<EnumerationResult<VesselImportBatch>?> EnumerateBatchesAsync(VesselImportBatchEnumerateQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<VesselImportBatch>>("/api/v1/vessels/import/batches/enumerate", query ?? new VesselImportBatchEnumerateQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselImportBatch</c>: GET `/api/v1/vessels/import/batches/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselImportBatch")]
        public Task<VesselImportBatchDetail?> GetBatchAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<VesselImportBatchDetail>($"/api/v1/vessels/import/batches/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetCategorizationDefaultPrompt</c>: GET '/api/v1/vessels/import/categorization/default-prompt'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getFleetCategorizationDefaultPrompt")]
        public Task<FleetCategorizationDefaultPrompt?> GetFleetCategorizationDefaultPromptAsync(CancellationToken token = default)
        {
            return _Transport.GetAsync<FleetCategorizationDefaultPrompt>("/api/v1/vessels/import/categorization/default-prompt", null, token);
        }

        /// <summary>
        /// Dashboard <c>categorizeVesselImport</c>: POST /api/v1/vessels/import/batches/{id}/categorize (omitted fields reuse the previous run).
        /// </summary>
        /// <param name="id">Batch id.</param>
        /// <param name="data">Captain and instructions, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("categorizeVesselImport")]
        public Task<VesselImportBatch?> CategorizeAsync(string id, VesselImportCategorizationRequest? data = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<VesselImportBatch>($"/api/v1/vessels/import/batches/{ArmadaQueryString.Escape(id)}/categorize", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>applyFleetRecommendations</c>: POST `/api/v1/vessels/import/batches/${encodeURIComponent(id)}/fleet-recommendations/apply`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("applyFleetRecommendations")]
        public Task<FleetRecommendationApplyResult?> ApplyFleetRecommendationsAsync(string id, FleetRecommendationApplyRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<FleetRecommendationApplyResult>($"/api/v1/vessels/import/batches/{ArmadaQueryString.Escape(id)}/fleet-recommendations/apply", data, null, token);
        }

        #endregion
    }
}
