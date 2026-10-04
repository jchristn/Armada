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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listRunbooks</c>: GET `/api/v1/runbooks${buildRunbookQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Runbook>?> ListRunbooksAsync(RunbookQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Runbook>>($"/api/v1/runbooks{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateRunbooks</c>: POST '/api/v1/runbooks/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Runbook>?> EnumerateRunbooksAsync(RunbookQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<Runbook>>("/api/v1/runbooks/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getRunbook</c>: GET `/api/v1/runbooks/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Runbook?> GetRunbookAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Runbook>($"/api/v1/runbooks/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createRunbook</c>: POST '/api/v1/runbooks'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Runbook?> CreateRunbookAsync(RunbookUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<Runbook>("/api/v1/runbooks", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateRunbook</c>: PUT `/api/v1/runbooks/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Runbook?> UpdateRunbookAsync(string id, RunbookUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<Runbook>($"/api/v1/runbooks/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRunbook</c>: DEL `/api/v1/runbooks/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteRunbookAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/runbooks/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>listRunbookExecutions</c>: GET `/api/v1/runbook-executions${buildRunbookExecutionQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<RunbookExecution>?> ListRunbookExecutionsAsync(RunbookExecutionQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<RunbookExecution>>($"/api/v1/runbook-executions{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateRunbookExecutions</c>: POST '/api/v1/runbook-executions/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<RunbookExecution>?> EnumerateRunbookExecutionsAsync(RunbookExecutionQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<RunbookExecution>>("/api/v1/runbook-executions/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getRunbookExecution</c>: GET `/api/v1/runbook-executions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<RunbookExecution?> GetRunbookExecutionAsync(string id, CancellationToken token = default)
        {
            return GetAsync<RunbookExecution>($"/api/v1/runbook-executions/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>startRunbookExecution</c>: POST `/api/v1/runbooks/${encodeURIComponent(runbookId)}/executions`.
        /// </summary>
        /// <param name="runbookId">runbookId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<RunbookExecution?> StartRunbookExecutionAsync(string runbookId, RunbookExecutionStartRequest data, CancellationToken token = default)
        {
            return PostAsync<RunbookExecution>($"/api/v1/runbooks/{E(runbookId)}/executions", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateRunbookExecution</c>: PUT `/api/v1/runbook-executions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<RunbookExecution?> UpdateRunbookExecutionAsync(string id, RunbookExecutionUpdateRequest data, CancellationToken token = default)
        {
            return PutAsync<RunbookExecution>($"/api/v1/runbook-executions/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRunbookExecution</c>: DEL `/api/v1/runbook-executions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteRunbookExecutionAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/runbook-executions/{E(id)}", null, null, token);
        }

        #endregion
    }
}
