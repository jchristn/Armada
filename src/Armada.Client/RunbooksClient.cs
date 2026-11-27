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
    /// Runbooks API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class RunbooksClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal RunbooksClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listRunbooks</c>: GET `/api/v1/runbooks${buildRunbookQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listRunbooks")]
        public Task<EnumerationResult<Runbook>?> ListAsync(RunbookQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Runbook>>($"/api/v1/runbooks{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateRunbooks</c>: POST '/api/v1/runbooks/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateRunbooks")]
        public Task<EnumerationResult<Runbook>?> EnumerateAsync(RunbookQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<Runbook>>("/api/v1/runbooks/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getRunbook</c>: GET `/api/v1/runbooks/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getRunbook")]
        public Task<Runbook?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Runbook>($"/api/v1/runbooks/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createRunbook</c>: POST '/api/v1/runbooks'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createRunbook")]
        public Task<Runbook?> CreateAsync(RunbookUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Runbook>("/api/v1/runbooks", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateRunbook</c>: PUT `/api/v1/runbooks/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateRunbook")]
        public Task<Runbook?> UpdateAsync(string id, RunbookUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Runbook>($"/api/v1/runbooks/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRunbook</c>: DEL `/api/v1/runbooks/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteRunbook")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/runbooks/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>listRunbookExecutions</c>: GET `/api/v1/runbook-executions${buildRunbookExecutionQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listRunbookExecutions")]
        public Task<EnumerationResult<RunbookExecution>?> ListExecutionsAsync(RunbookExecutionQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<RunbookExecution>>($"/api/v1/runbook-executions{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateRunbookExecutions</c>: POST '/api/v1/runbook-executions/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateRunbookExecutions")]
        public Task<EnumerationResult<RunbookExecution>?> EnumerateExecutionsAsync(RunbookExecutionQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<RunbookExecution>>("/api/v1/runbook-executions/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getRunbookExecution</c>: GET `/api/v1/runbook-executions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getRunbookExecution")]
        public Task<RunbookExecution?> GetExecutionAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<RunbookExecution>($"/api/v1/runbook-executions/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>startRunbookExecution</c>: POST `/api/v1/runbooks/${encodeURIComponent(runbookId)}/executions`.
        /// </summary>
        /// <param name="runbookId">runbookId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("startRunbookExecution")]
        public Task<RunbookExecution?> StartExecutionAsync(string runbookId, RunbookExecutionStartRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<RunbookExecution>($"/api/v1/runbooks/{ArmadaQueryString.Escape(runbookId)}/executions", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateRunbookExecution</c>: PUT `/api/v1/runbook-executions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateRunbookExecution")]
        public Task<RunbookExecution?> UpdateExecutionAsync(string id, RunbookExecutionUpdateRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<RunbookExecution>($"/api/v1/runbook-executions/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRunbookExecution</c>: DEL `/api/v1/runbook-executions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteRunbookExecution")]
        public Task DeleteExecutionAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/runbook-executions/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}
