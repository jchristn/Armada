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
    public sealed class CheckRunsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal CheckRunsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listCheckRuns</c>: GET `/api/v1/check-runs${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listCheckRuns")]
        public Task<EnumerationResult<CheckRun>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<CheckRun>>($"/api/v1/check-runs{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getCheckRun</c>: GET `/api/v1/check-runs/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getCheckRun")]
        public Task<CheckRun?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<CheckRun>($"/api/v1/check-runs/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>runCheck</c>: POST '/api/v1/check-runs'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("runCheck")]
        public Task<CheckRun?> RunAsync(CheckRunRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<CheckRun>("/api/v1/check-runs", data, ArmadaRequestOptions.WithTimeout(2100000), token);
        }

        /// <summary>
        /// Dashboard <c>importCheckRun</c>: POST '/api/v1/check-runs/import'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("importCheckRun")]
        public Task<CheckRun?> ImportAsync(CheckRunImportRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<CheckRun>("/api/v1/check-runs/import", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>retryCheckRun</c>: POST `/api/v1/check-runs/${encodeURIComponent(id)}/retry`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("retryCheckRun")]
        public Task<CheckRun?> RetryAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<CheckRun>($"/api/v1/check-runs/{ArmadaQueryString.Escape(id)}/retry", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteCheckRun</c>: DEL `/api/v1/check-runs/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteCheckRun")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/check-runs/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}
