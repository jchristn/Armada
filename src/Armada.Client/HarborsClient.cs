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
    public sealed class HarborsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal HarborsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listHarbors</c>: GET '/api/v1/harbors'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listHarbors")]
        public Task<List<Harbor>?> ListAsync(CancellationToken token = default)
        {
            return _Transport.EnumerateAllAsync<Harbor>("/api/v1/harbors/enumerate", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateHarbors</c>: POST /api/v1/harbors/enumerate, one page of the Harbors visible to the caller
        /// with the exact total.
        /// </summary>
        /// <param name="query">Paging (PageSize 1 to 1000) and created-time filters, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateHarbors")]
        public Task<EnumerationResult<Harbor>?> EnumerateAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<Harbor>>("/api/v1/harbors/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getHarbor</c>: GET `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getHarbor")]
        public Task<Harbor?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Harbor>($"/api/v1/harbors/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getHarborMetrics</c>: GET `/api/v1/harbors/${encodeURIComponent(id)}/metrics?range=${encodeURIComponent(range)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="range">Window: 1h, 24h (the default), or 7d.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getHarborMetrics")]
        public Task<HarborMetrics?> GetMetricsAsync(string id, string range = "24h", CancellationToken token = default)
        {
            return _Transport.GetAsync<HarborMetrics>($"/api/v1/harbors/{ArmadaQueryString.Escape(id)}/metrics?range={ArmadaQueryString.Escape(range)}", null, token);
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
        [DashboardExport("listHarborJobs")]
        public Task<EnumerationResult<HarborJobRecord>?> ListJobsAsync(string id, bool openOnly = false, ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            string page = ArmadaQueryString.FromPage(query);
            string open = openOnly ? (page.Length == 0 ? "?open=true" : "&open=true") : "";
            return _Transport.GetAsync<EnumerationResult<HarborJobRecord>>($"/api/v1/harbors/{ArmadaQueryString.Escape(id)}/jobs{page}{open}", null, token);
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
        public Task<EnumerationResult<HarborJobRecord>?> EnumerateJobsAsync(string id, bool openOnly = false, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<HarborJobRecord>>($"/api/v1/harbors/{ArmadaQueryString.Escape(id)}/jobs/enumerate{(openOnly ? "?open=true" : "")}", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>stopHarborJob</c>: POST `/api/v1/harbors/${encodeURIComponent(id)}/jobs/${encodeURIComponent(jobId)}/stop`.
        /// </summary>
        /// <param name="id">Harbor id.</param>
        /// <param name="jobId">Job id, as listed by <see cref="JobsClient.ListAsync"/>.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What happened.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response (404 not found, 409 Harbor not connected), timeout, or transport failure.</exception>
        [DashboardExport("stopHarborJob")]
        public Task<HarborJobStopResult?> StopJobAsync(string id, string jobId, CancellationToken token = default)
        {
            return _Transport.PostAsync<HarborJobStopResult>($"/api/v1/harbors/{ArmadaQueryString.Escape(id)}/jobs/{ArmadaQueryString.Escape(jobId)}/stop", new { }, null, token);
        }

        /// <summary>
        /// Dashboard <c>createHarbor</c>: POST '/api/v1/harbors'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createHarbor")]
        public Task<Harbor?> CreateAsync(Harbor data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Harbor>("/api/v1/harbors", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateHarbor</c>: PUT `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateHarbor")]
        public Task<Harbor?> UpdateAsync(string id, Harbor data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Harbor>($"/api/v1/harbors/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteHarbor</c>: DEL `/api/v1/harbors/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteHarbor")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/harbors/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>enableHarbor</c>: POST `/api/v1/harbors/${encodeURIComponent(id)}/enable`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enableHarbor")]
        public Task<Harbor?> EnableAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<Harbor>($"/api/v1/harbors/{ArmadaQueryString.Escape(id)}/enable", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>disableHarbor</c>: POST `/api/v1/harbors/${encodeURIComponent(id)}/disable`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("disableHarbor")]
        public Task<Harbor?> DisableAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<Harbor>($"/api/v1/harbors/{ArmadaQueryString.Escape(id)}/disable", null, null, token);
        }

        #endregion
    }
}
