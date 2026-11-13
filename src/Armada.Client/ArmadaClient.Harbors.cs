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
    /// Harbors API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listHarbors</c>: GET '/api/v1/harbors'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<Harbor>?> ListHarborsAsync(CancellationToken token = default)
        {
            return EnumerateAllAsync<Harbor>("/api/v1/harbors/enumerate", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateHarbors</c>: POST /api/v1/harbors/enumerate, one page of the Harbors visible to the caller
        /// with the exact total.
        /// </summary>
        /// <param name="query">Paging (PageSize 1 to 1000) and created-time filters, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Harbor>?> EnumerateHarborsAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<Harbor>>("/api/v1/harbors/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getHarbor</c>: GET `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> GetHarborAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Harbor>($"/api/v1/harbors/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getHarborMetrics</c>: GET `/api/v1/harbors/${encodeURIComponent(id)}/metrics?range=${encodeURIComponent(range)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="range">Window: 1h, 24h (the default), or 7d.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<HarborMetrics?> GetHarborMetricsAsync(string id, string range = "24h", CancellationToken token = default)
        {
            return GetAsync<HarborMetrics>($"/api/v1/harbors/{E(id)}/metrics?range={E(range)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>listHarborJobs</c>: GET `/api/v1/harbors/${encodeURIComponent(id)}/jobs`, one page, newest first.
        /// </summary>
        /// <param name="id">Harbor id.</param>
        /// <param name="openOnly">Only jobs that have not ended.</param>
        /// <param name="query">Paging, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<HarborJobRecord>?> ListHarborJobsAsync(string id, bool openOnly = false, ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            string page = ArmadaQueryString.FromPage(query);
            string open = openOnly ? (page.Length == 0 ? "?open=true" : "&open=true") : "";
            return GetAsync<EnumerationResult<HarborJobRecord>>($"/api/v1/harbors/{E(id)}/jobs{page}{open}", null, token);
        }

        /// <summary>
        /// POST /api/v1/harbors/{id}/jobs/enumerate: one page of the captain launches recorded on a Harbor, newest first.
        /// </summary>
        /// <param name="id">Harbor id (hbr_ prefix).</param>
        /// <param name="openOnly">Only jobs that have not ended.</param>
        /// <param name="query">Paging and date bounds, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<HarborJobRecord>?> EnumerateHarborJobsAsync(string id, bool openOnly = false, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<HarborJobRecord>>($"/api/v1/harbors/{E(id)}/jobs/enumerate{(openOnly ? "?open=true" : "")}", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>stopHarborJob</c>: POST `/api/v1/harbors/${encodeURIComponent(id)}/jobs/${encodeURIComponent(jobId)}/stop`.
        /// </summary>
        /// <param name="id">Harbor id.</param>
        /// <param name="jobId">Job id, as listed by <see cref="ListHarborJobsAsync"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What happened.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response (404 not found, 409 Harbor not connected), timeout, or transport failure.</exception>
        public Task<HarborJobStopResult?> StopHarborJobAsync(string id, string jobId, CancellationToken token = default)
        {
            return PostAsync<HarborJobStopResult>($"/api/v1/harbors/{E(id)}/jobs/{E(jobId)}/stop", new { }, null, token);
        }

        /// <summary>
        /// Dashboard <c>createHarbor</c>: POST '/api/v1/harbors'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> CreateHarborAsync(Harbor data, CancellationToken token = default)
        {
            return PostAsync<Harbor>("/api/v1/harbors", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateHarbor</c>: PUT `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> UpdateHarborAsync(string id, Harbor data, CancellationToken token = default)
        {
            return PutAsync<Harbor>($"/api/v1/harbors/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteHarbor</c>: DEL `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteHarborAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/harbors/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>enableHarbor</c>: POST `/api/v1/harbors/${encodeURIComponent(id)}/enable`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> EnableHarborAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Harbor>($"/api/v1/harbors/{E(id)}/enable", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>disableHarbor</c>: POST `/api/v1/harbors/${encodeURIComponent(id)}/disable`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Harbor?> DisableHarborAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Harbor>($"/api/v1/harbors/{E(id)}/disable", null, null, token);
        }

        #endregion
    }
}
