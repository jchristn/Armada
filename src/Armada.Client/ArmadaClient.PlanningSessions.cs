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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listPlanningSessions</c>: POST '/api/v1/planning-sessions/enumerate'. One page of the planning
        /// sessions visible to the caller, most recently updated first (page size 1 to 1000, default 10).
        /// </summary>
        /// <param name="query">Paging, order, and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page with exact totals.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<PlanningSession>?> ListPlanningSessionsAsync(EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<PlanningSession>>("/api/v1/planning-sessions/enumerate", query ?? new EnumerationQuery(), null, token);
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
        public Task<EnumerationResult<PlanningSessionMessage>?> ListPlanningSessionMessagesAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<PlanningSessionMessage>>($"/api/v1/planning-sessions/{E(id)}/messages/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>getPlanningSession</c>: GET `/api/v1/planning-sessions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PlanningSessionDetail?> GetPlanningSessionAsync(string id, CancellationToken token = default)
        {
            return GetAsync<PlanningSessionDetail>($"/api/v1/planning-sessions/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createPlanningSession</c>: POST '/api/v1/planning-sessions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PlanningSessionDetail?> CreatePlanningSessionAsync(PlanningSessionCreateRequest data, CancellationToken token = default)
        {
            return PostAsync<PlanningSessionDetail>("/api/v1/planning-sessions", data, ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>sendPlanningSessionMessage</c>: POST `/api/v1/planning-sessions/${id}/messages`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PlanningSessionDetail?> SendPlanningSessionMessageAsync(string id, PlanningSessionMessageRequest data, CancellationToken token = default)
        {
            return PostAsync<PlanningSessionDetail>($"/api/v1/planning-sessions/{E(id)}/messages", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>summarizePlanningSession</c>: POST `/api/v1/planning-sessions/${id}/summarize`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PlanningSessionSummaryResponse?> SummarizePlanningSessionAsync(string id, PlanningSessionSummaryRequest data, CancellationToken token = default)
        {
            return PostAsync<PlanningSessionSummaryResponse>($"/api/v1/planning-sessions/{E(id)}/summarize", data, ArmadaRequestOptions.WithTimeout(180000), token);
        }

        /// <summary>
        /// Dashboard <c>dispatchPlanningSession</c>: POST `/api/v1/planning-sessions/${id}/dispatch`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Voyage?> DispatchPlanningSessionAsync(string id, PlanningSessionDispatchRequest data, CancellationToken token = default)
        {
            return PostAsync<Voyage>($"/api/v1/planning-sessions/{E(id)}/dispatch", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>stopPlanningSession</c>: POST `/api/v1/planning-sessions/${id}/stop`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PlanningSessionDetail?> StopPlanningSessionAsync(string id, CancellationToken token = default)
        {
            return PostAsync<PlanningSessionDetail>($"/api/v1/planning-sessions/{E(id)}/stop", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>stopPlanningTurn</c>: POST `/api/v1/planning-sessions/${id}/stop-turn`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PlanningSessionDetail?> StopPlanningTurnAsync(string id, CancellationToken token = default)
        {
            return PostAsync<PlanningSessionDetail>($"/api/v1/planning-sessions/{E(id)}/stop-turn", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deletePlanningSession</c>: DEL `/api/v1/planning-sessions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeletePlanningSessionAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/planning-sessions/{E(id)}", null, null, token);
        }

        #endregion
    }
}
