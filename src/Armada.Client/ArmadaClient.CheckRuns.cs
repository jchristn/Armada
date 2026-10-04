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
    /// CheckRuns API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listCheckRuns</c>: GET `/api/v1/check-runs${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<CheckRun>?> ListCheckRunsAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<CheckRun>>($"/api/v1/check-runs{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getCheckRun</c>: GET `/api/v1/check-runs/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CheckRun?> GetCheckRunAsync(string id, CancellationToken token = default)
        {
            return GetAsync<CheckRun>($"/api/v1/check-runs/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>runCheck</c>: POST '/api/v1/check-runs'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CheckRun?> RunCheckAsync(CheckRunRequest data, CancellationToken token = default)
        {
            return PostAsync<CheckRun>("/api/v1/check-runs", data, ArmadaRequestOptions.WithTimeout(2100000), token);
        }

        /// <summary>
        /// Dashboard <c>importCheckRun</c>: POST '/api/v1/check-runs/import'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CheckRun?> ImportCheckRunAsync(CheckRunImportRequest data, CancellationToken token = default)
        {
            return PostAsync<CheckRun>("/api/v1/check-runs/import", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>retryCheckRun</c>: POST `/api/v1/check-runs/${encodeURIComponent(id)}/retry`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<CheckRun?> RetryCheckRunAsync(string id, CancellationToken token = default)
        {
            return PostAsync<CheckRun>($"/api/v1/check-runs/{E(id)}/retry", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteCheckRun</c>: DEL `/api/v1/check-runs/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteCheckRunAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/check-runs/{E(id)}", null, null, token);
        }

        #endregion
    }
}
