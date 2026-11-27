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
    public sealed class FleetActionsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal FleetActionsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>enumerateFleetActions</c>: POST /api/v1/fleet-actions/enumerate.
        /// </summary>
        /// <param name="query">Query, or null for page 1 of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateFleetActions")]
        public Task<EnumerationResult<FleetAction>?> EnumerateAsync(FleetActionEnumerateQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<FleetAction>>("/api/v1/fleet-actions/enumerate", query ?? new FleetActionEnumerateQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetAction</c>: GET `/api/v1/fleet-actions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getFleetAction")]
        public Task<FleetAction?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<FleetAction>($"/api/v1/fleet-actions/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createFleetAction</c>: POST '/api/v1/fleet-actions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createFleetAction")]
        public Task<FleetAction?> CreateAsync(FleetActionUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<FleetAction>("/api/v1/fleet-actions", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateFleetAction</c>: PUT `/api/v1/fleet-actions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateFleetAction")]
        public Task<FleetAction?> UpdateAsync(string id, FleetActionUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<FleetAction>($"/api/v1/fleet-actions/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteFleetAction</c>: DEL `/api/v1/fleet-actions/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteFleetAction")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/fleet-actions/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>runFleetAction</c>: POST `/api/v1/fleet-actions/${encodeURIComponent(id)}/run`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("runFleetAction")]
        public Task<FleetActionRunStartResult?> RunAsync(string id, FleetActionRunRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<FleetActionRunStartResult>($"/api/v1/fleet-actions/{ArmadaQueryString.Escape(id)}/run", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>runAdHocFleetAction</c>: POST '/api/v1/fleet-actions/run'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("runAdHocFleetAction")]
        public Task<FleetActionRunStartResult?> RunAdHocAsync(FleetActionRunRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<FleetActionRunStartResult>("/api/v1/fleet-actions/run", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateFleetActionRuns</c>: POST /api/v1/fleet-action-runs/enumerate.
        /// </summary>
        /// <param name="query">Query, or null for page 1 of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateFleetActionRuns")]
        public Task<EnumerationResult<FleetActionRun>?> EnumerateRunsAsync(FleetActionRunEnumerateQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<FleetActionRun>>("/api/v1/fleet-action-runs/enumerate", query ?? new FleetActionRunEnumerateQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetActionRun</c>: GET `/api/v1/fleet-action-runs/${encodeURIComponent(id)}`. The run carries
        /// its target counts; the targets are not embedded, page them with <see cref="EnumerateRunTargetsAsync"/>.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getFleetActionRun")]
        public Task<FleetActionRunDetail?> GetRunAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<FleetActionRunDetail>($"/api/v1/fleet-action-runs/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateFleetActionRunTargets</c>: POST /api/v1/fleet-action-runs/{runId}/targets/enumerate.
        /// </summary>
        /// <param name="runId">Run id.</param>
        /// <param name="query">Query, or null for page 1 of 25.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateFleetActionRunTargets")]
        public Task<EnumerationResult<FleetActionRunTargetSummary>?> EnumerateRunTargetsAsync(string runId, FleetActionRunTargetEnumerateQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<FleetActionRunTargetSummary>>($"/api/v1/fleet-action-runs/{ArmadaQueryString.Escape(runId)}/targets/enumerate", query ?? new FleetActionRunTargetEnumerateQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getFleetActionRunTarget</c>: GET `/api/v1/fleet-action-runs/${encodeURIComponent(runId)}/targets/${encodeURIComponent(targetId)}`.
        /// </summary>
        /// <param name="runId">runId.</param>
        /// <param name="targetId">targetId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getFleetActionRunTarget")]
        public Task<FleetActionRunTarget?> GetRunTargetAsync(string runId, string targetId, CancellationToken token = default)
        {
            return _Transport.GetAsync<FleetActionRunTarget>($"/api/v1/fleet-action-runs/{ArmadaQueryString.Escape(runId)}/targets/{ArmadaQueryString.Escape(targetId)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>cancelFleetActionRun</c>: POST `/api/v1/fleet-action-runs/${encodeURIComponent(id)}/cancel`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("cancelFleetActionRun")]
        public Task<FleetActionRun?> CancelRunAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<FleetActionRun>($"/api/v1/fleet-action-runs/{ArmadaQueryString.Escape(id)}/cancel", null, null, token);
        }

        #endregion
    }
}
