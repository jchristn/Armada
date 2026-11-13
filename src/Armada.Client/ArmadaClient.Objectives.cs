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
    /// Objectives API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listObjectives</c>: GET `/api/v1/objectives${buildObjectiveQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Objective>?> ListObjectivesAsync(ObjectiveQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Objective>>($"/api/v1/objectives{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateObjectives</c>: POST '/api/v1/objectives/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Objective>?> EnumerateObjectivesAsync(ObjectiveQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<Objective>>("/api/v1/objectives/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>reorderObjectives</c>: POST '/api/v1/objectives/reorder'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveReorderResult?> ReorderObjectivesAsync(ObjectiveReorderRequest data, CancellationToken token = default)
        {
            return PostAsync<ObjectiveReorderResult>("/api/v1/objectives/reorder", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>getObjective</c>: GET `/api/v1/objectives/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Objective?> GetObjectiveAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Objective>($"/api/v1/objectives/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createObjective</c>: POST '/api/v1/objectives'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Objective?> CreateObjectiveAsync(ObjectiveUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<Objective>("/api/v1/objectives", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateObjective</c>: PUT `/api/v1/objectives/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Objective?> UpdateObjectiveAsync(string id, ObjectiveUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<Objective>($"/api/v1/objectives/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteObjective</c>: DEL `/api/v1/objectives/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteObjectiveAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/objectives/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>importObjectiveFromGitHub</c>: POST '/api/v1/objectives/import/github'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Objective?> ImportObjectiveFromGitHubAsync(GitHubObjectiveImportRequest data, CancellationToken token = default)
        {
            return PostAsync<Objective>("/api/v1/objectives/import/github", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>listBacklog</c>: GET `/api/v1/backlog${buildObjectiveQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Objective>?> ListBacklogAsync(ObjectiveQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Objective>>($"/api/v1/backlog{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateBacklog</c>: POST '/api/v1/backlog/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Objective>?> EnumerateBacklogAsync(ObjectiveQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<Objective>>("/api/v1/backlog/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>reorderBacklog</c>: POST '/api/v1/backlog/reorder'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveReorderResult?> ReorderBacklogAsync(ObjectiveReorderRequest data, CancellationToken token = default)
        {
            return PostAsync<ObjectiveReorderResult>("/api/v1/backlog/reorder", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>getBacklogItem</c>: GET `/api/v1/backlog/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Objective?> GetBacklogItemAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Objective>($"/api/v1/backlog/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createBacklogItem</c>: POST '/api/v1/backlog'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Objective?> CreateBacklogItemAsync(ObjectiveUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<Objective>("/api/v1/backlog", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateBacklogItem</c>: PUT `/api/v1/backlog/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Objective?> UpdateBacklogItemAsync(string id, ObjectiveUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<Objective>($"/api/v1/backlog/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteBacklogItem</c>: DEL `/api/v1/backlog/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteBacklogItemAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/backlog/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>listObjectiveRefinementSessions</c>: POST `/api/v1/objectives/${objectiveId}/refinement-sessions/enumerate`.
        /// One page of the objective's refinement sessions, most recently updated first.
        /// </summary>
        /// <param name="objectiveId">Objective ID.</param>
        /// <param name="query">Paging, order, and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page with exact totals.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ObjectiveRefinementSession>?> ListObjectiveRefinementSessionsAsync(string objectiveId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<ObjectiveRefinementSession>>($"/api/v1/objectives/{E(objectiveId)}/refinement-sessions/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>listBacklogRefinementSessions</c>: POST `/api/v1/backlog/${objectiveId}/refinement-sessions/enumerate`.
        /// One page of the backlog item's refinement sessions, most recently updated first.
        /// </summary>
        /// <param name="objectiveId">Backlog item ID.</param>
        /// <param name="query">Paging, order, and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page with exact totals.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ObjectiveRefinementSession>?> ListBacklogRefinementSessionsAsync(string objectiveId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<ObjectiveRefinementSession>>($"/api/v1/backlog/{E(objectiveId)}/refinement-sessions/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>listObjectiveRefinementMessages</c>: POST `/api/v1/objective-refinement-sessions/${sessionId}/messages/enumerate`.
        /// One page of the session's transcript, newest first by default (CreatedAscending for oldest first).
        /// </summary>
        /// <param name="sessionId">Refinement session ID.</param>
        /// <param name="query">Paging and order, or null for the newest page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page with exact totals.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ObjectiveRefinementMessage>?> ListObjectiveRefinementMessagesAsync(string sessionId, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<ObjectiveRefinementMessage>>($"/api/v1/objective-refinement-sessions/{E(sessionId)}/messages/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>createObjectiveRefinementSession</c>: POST `/api/v1/objectives/${encodeURIComponent(objectiveId)}/refinement-sessions`.
        /// </summary>
        /// <param name="objectiveId">objectiveId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveRefinementSessionDetail?> CreateObjectiveRefinementSessionAsync(string objectiveId, ObjectiveRefinementSessionCreateRequest data, CancellationToken token = default)
        {
            return PostAsync<ObjectiveRefinementSessionDetail>($"/api/v1/objectives/{E(objectiveId)}/refinement-sessions", data, ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>createBacklogRefinementSession</c>: POST `/api/v1/backlog/${encodeURIComponent(objectiveId)}/refinement-sessions`.
        /// </summary>
        /// <param name="objectiveId">objectiveId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveRefinementSessionDetail?> CreateBacklogRefinementSessionAsync(string objectiveId, ObjectiveRefinementSessionCreateRequest data, CancellationToken token = default)
        {
            return PostAsync<ObjectiveRefinementSessionDetail>($"/api/v1/backlog/{E(objectiveId)}/refinement-sessions", data, ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>getObjectiveRefinementSession</c>: GET `/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}`.
        /// </summary>
        /// <param name="sessionId">sessionId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveRefinementSessionDetail?> GetObjectiveRefinementSessionAsync(string sessionId, CancellationToken token = default)
        {
            return GetAsync<ObjectiveRefinementSessionDetail>($"/api/v1/objective-refinement-sessions/{E(sessionId)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>sendObjectiveRefinementMessage</c>: POST `/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/messages`.
        /// </summary>
        /// <param name="sessionId">sessionId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveRefinementSessionDetail?> SendObjectiveRefinementMessageAsync(string sessionId, ObjectiveRefinementMessageRequest data, CancellationToken token = default)
        {
            return PostAsync<ObjectiveRefinementSessionDetail>($"/api/v1/objective-refinement-sessions/{E(sessionId)}/messages", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>summarizeObjectiveRefinementSession</c>: POST `/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/summarize`.
        /// </summary>
        /// <param name="sessionId">sessionId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveRefinementSummaryResponse?> SummarizeObjectiveRefinementSessionAsync(string sessionId, ObjectiveRefinementSummaryRequest? data = null, CancellationToken token = default)
        {
            return PostAsync<ObjectiveRefinementSummaryResponse>($"/api/v1/objective-refinement-sessions/{E(sessionId)}/summarize", data, ArmadaRequestOptions.WithTimeout(180000), token);
        }

        /// <summary>
        /// Dashboard <c>applyObjectiveRefinementSummary</c>: POST `/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/apply`.
        /// </summary>
        /// <param name="sessionId">sessionId.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveRefinementApplyResponse?> ApplyObjectiveRefinementSummaryAsync(string sessionId, ObjectiveRefinementApplyRequest? data = null, CancellationToken token = default)
        {
            return PostAsync<ObjectiveRefinementApplyResponse>($"/api/v1/objective-refinement-sessions/{E(sessionId)}/apply", data, ArmadaRequestOptions.WithTimeout(180000), token);
        }

        /// <summary>
        /// Dashboard <c>stopObjectiveRefinementSession</c>: POST `/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}/stop`.
        /// </summary>
        /// <param name="sessionId">sessionId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ObjectiveRefinementSessionDetail?> StopObjectiveRefinementSessionAsync(string sessionId, CancellationToken token = default)
        {
            return PostAsync<ObjectiveRefinementSessionDetail>($"/api/v1/objective-refinement-sessions/{E(sessionId)}/stop", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteObjectiveRefinementSession</c>: DEL `/api/v1/objective-refinement-sessions/${encodeURIComponent(sessionId)}`.
        /// </summary>
        /// <param name="sessionId">sessionId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteObjectiveRefinementSessionAsync(string sessionId, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/objective-refinement-sessions/{E(sessionId)}", null, null, token);
        }

        #endregion
    }
}
