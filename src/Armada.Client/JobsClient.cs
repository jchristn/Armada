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
    /// Jobs API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class JobsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal JobsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listJobs</c>: GET '/api/v1/jobs'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listJobs")]
        public Task<EnumerationResult<Job>?> ListAsync(CancellationToken token = default)
        {
            // One page of 100 (the route always pages).
            return _Transport.GetAsync<EnumerationResult<Job>>("/api/v1/jobs?pageNumber=1&pageSize=100", null, token);
        }

        /// <summary>
        /// POST /api/v1/jobs/enumerate: one page of the background jobs visible to the caller, newest first, with the total.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Job>?> EnumerateAsync(JobQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<Job>>("/api/v1/jobs/enumerate", query ?? new JobQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getJob</c>: GET `/api/v1/jobs/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getJob")]
        public Task<Job?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Job>($"/api/v1/jobs/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelJob</c>: POST `/api/v1/jobs/${id}/cancel`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("cancelJob")]
        public Task<Job?> CancelAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<Job>($"/api/v1/jobs/{ArmadaQueryString.Escape(id)}/cancel", null, null, token);
        }

        #endregion
    }
}
