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
    /// FleetActions API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>enumerateFleetActions</c>: POST /api/v1/fleet-actions/enumerate.
        /// </summary>
        /// <param name="query">Query, or null for page 1 of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<FleetAction>?> EnumerateFleetActionsAsync(FleetActionEnumerateQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<FleetAction>>("/api/v1/fleet-actions/enumerate", query ?? new FleetActionEnumerateQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetAction</c>: GET `/api/v1/fleet-actions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetAction?> GetFleetActionAsync(string id, CancellationToken token = default)
        {
            return GetAsync<FleetAction>($"/api/v1/fleet-actions/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createFleetAction</c>: POST '/api/v1/fleet-actions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetAction?> CreateFleetActionAsync(FleetActionUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<FleetAction>("/api/v1/fleet-actions", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateFleetAction</c>: PUT `/api/v1/fleet-actions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetAction?> UpdateFleetActionAsync(string id, FleetActionUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<FleetAction>($"/api/v1/fleet-actions/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteFleetAction</c>: DEL `/api/v1/fleet-actions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteFleetActionAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/fleet-actions/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>runFleetAction</c>: POST `/api/v1/fleet-actions/${encodeURIComponent(id)}/run`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetActionRunStartResult?> RunFleetActionAsync(string id, FleetActionRunRequest data, CancellationToken token = default)
        {
            return PostAsync<FleetActionRunStartResult>($"/api/v1/fleet-actions/{E(id)}/run", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>runAdHocFleetAction</c>: POST '/api/v1/fleet-actions/run'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetActionRunStartResult?> RunAdHocFleetActionAsync(FleetActionRunRequest data, CancellationToken token = default)
        {
            return PostAsync<FleetActionRunStartResult>("/api/v1/fleet-actions/run", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateFleetActionRuns</c>: POST /api/v1/fleet-action-runs/enumerate.
        /// </summary>
        /// <param name="query">Query, or null for page 1 of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<FleetActionRun>?> EnumerateFleetActionRunsAsync(FleetActionRunEnumerateQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<FleetActionRun>>("/api/v1/fleet-action-runs/enumerate", query ?? new FleetActionRunEnumerateQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetActionRun</c>: GET `/api/v1/fleet-action-runs/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetActionRunDetail?> GetFleetActionRunAsync(string id, CancellationToken token = default)
        {
            return GetAsync<FleetActionRunDetail>($"/api/v1/fleet-action-runs/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateFleetActionRunTargets</c>: POST /api/v1/fleet-action-runs/{runId}/targets/enumerate.
        /// </summary>
        /// <param name="runId">Run id.</param>
        /// <param name="query">Query, or null for page 1 of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<FleetActionRunTargetSummary>?> EnumerateFleetActionRunTargetsAsync(string runId, FleetActionRunTargetEnumerateQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<FleetActionRunTargetSummary>>($"/api/v1/fleet-action-runs/{E(runId)}/targets/enumerate", query ?? new FleetActionRunTargetEnumerateQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetActionRunTarget</c>: GET `/api/v1/fleet-action-runs/${encodeURIComponent(runId)}/targets/${encodeURIComponent(targetId)}`.
        /// </summary>
        /// <param name="runId">runId.</param>
        /// <param name="targetId">targetId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetActionRunTarget?> GetFleetActionRunTargetAsync(string runId, string targetId, CancellationToken token = default)
        {
            return GetAsync<FleetActionRunTarget>($"/api/v1/fleet-action-runs/{E(runId)}/targets/{E(targetId)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelFleetActionRun</c>: POST `/api/v1/fleet-action-runs/${encodeURIComponent(id)}/cancel`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<FleetActionRun?> CancelFleetActionRunAsync(string id, CancellationToken token = default)
        {
            return PostAsync<FleetActionRun>($"/api/v1/fleet-action-runs/{E(id)}/cancel", null, null, token);
        }

        #endregion
    }
}
