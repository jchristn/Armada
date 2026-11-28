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
    /// PlanningSessions API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class PlanningSessionsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal PlanningSessionsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPlanningSessions</c>: POST '/api/v1/planning-sessions/enumerate'. One page of the planning
        /// sessions visible to the caller, most recently updated first (page size 1 to 1000, default 10).
        /// </summary>
        /// <param name="query">Paging, order, and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page with exact totals.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listPlanningSessions")]
        public Task<EnumerationResult<PlanningSession>?> ListAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<PlanningSession>>("/api/v1/planning-sessions/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>listPlanningSessionMessages</c>: POST `/api/v1/planning-sessions/${id}/messages/enumerate`. One
        /// page of the session's transcript, newest first by default (CreatedAscending for oldest first).
        /// </summary>
        /// <param name="id">Planning session ID.</param>
        /// <param name="query">Paging and order, or null for the newest page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page with exact totals.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listPlanningSessionMessages")]
        public Task<EnumerationResult<PlanningSessionMessage>?> ListMessagesAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<PlanningSessionMessage>>($"/api/v1/planning-sessions/{ArmadaQueryString.Escape(id)}/messages/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getPlanningSession</c>: GET `/api/v1/planning-sessions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getPlanningSession")]
        public Task<PlanningSessionDetail?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<PlanningSessionDetail>($"/api/v1/planning-sessions/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPlanningSession</c>: POST '/api/v1/planning-sessions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createPlanningSession")]
        public Task<PlanningSessionDetail?> CreateAsync(PlanningSessionCreateRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<PlanningSessionDetail>("/api/v1/planning-sessions", data, ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>sendPlanningSessionMessage</c>: POST `/api/v1/planning-sessions/${id}/messages`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("sendPlanningSessionMessage")]
        public Task<PlanningSessionDetail?> SendMessageAsync(string id, PlanningSessionMessageRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<PlanningSessionDetail>($"/api/v1/planning-sessions/{ArmadaQueryString.Escape(id)}/messages", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>summarizePlanningSession</c>: POST `/api/v1/planning-sessions/${id}/summarize`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("summarizePlanningSession")]
        public Task<PlanningSessionSummaryResponse?> SummarizeAsync(string id, PlanningSessionSummaryRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<PlanningSessionSummaryResponse>($"/api/v1/planning-sessions/{ArmadaQueryString.Escape(id)}/summarize", data, ArmadaRequestOptions.WithTimeout(180000), token);
        }

        /// <summary>
        /// Dashboard <c>dispatchPlanningSession</c>: POST `/api/v1/planning-sessions/${id}/dispatch`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("dispatchPlanningSession")]
        public Task<Voyage?> DispatchAsync(string id, PlanningSessionDispatchRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Voyage>($"/api/v1/planning-sessions/{ArmadaQueryString.Escape(id)}/dispatch", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>stopPlanningSession</c>: POST `/api/v1/planning-sessions/${id}/stop`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("stopPlanningSession")]
        public Task<PlanningSessionDetail?> StopAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<PlanningSessionDetail>($"/api/v1/planning-sessions/{ArmadaQueryString.Escape(id)}/stop", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>stopPlanningTurn</c>: POST `/api/v1/planning-sessions/${id}/stop-turn`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("stopPlanningTurn")]
        public Task<PlanningSessionDetail?> StopTurnAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<PlanningSessionDetail>($"/api/v1/planning-sessions/{ArmadaQueryString.Escape(id)}/stop-turn", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deletePlanningSession</c>: DEL `/api/v1/planning-sessions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deletePlanningSession")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/planning-sessions/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}
