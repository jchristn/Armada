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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listJobs</c>: GET '/api/v1/jobs'.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Job>?> ListJobsAsync(CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Job>>("/api/v1/jobs", null, token);
        }

        /// <summary>
        /// Dashboard <c>getJob</c>: GET `/api/v1/jobs/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Job?> GetJobAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Job>($"/api/v1/jobs/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelJob</c>: POST `/api/v1/jobs/${id}/cancel`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Job?> CancelJobAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Job>($"/api/v1/jobs/{E(id)}/cancel", null, null, token);
        }

        #endregion
    }
}
