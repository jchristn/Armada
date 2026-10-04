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
    /// Missions API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listMissions</c>: GET `/api/v1/missions${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Mission>?> ListMissionsAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Mission>>($"/api/v1/missions{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>listMissionSummaries</c>: GET `/api/v1/missions/summaries${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<MissionSummary>?> ListMissionSummariesAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<MissionSummary>>($"/api/v1/missions/summaries{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMission</c>: GET `/api/v1/missions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Mission?> GetMissionAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Mission>($"/api/v1/missions/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionHistory</c>: GET /api/v1/missions/history.
        /// </summary>
        /// <param name="query">Range and filters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<MissionHistorySummaryResult?> GetMissionHistoryAsync(MissionHistoryFilter? query = null, CancellationToken token = default)
        {
            return GetAsync<MissionHistorySummaryResult>("/api/v1/missions/history" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// Dashboard <c>getTokenUsage</c>: GET /api/v1/token-usage/summary.
        /// </summary>
        /// <param name="query">Range and filters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<TokenUsageSummaryResult?> GetTokenUsageAsync(TokenUsageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<TokenUsageSummaryResult>("/api/v1/token-usage/summary" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionLandingPreview</c>: GET `/api/v1/missions/${encodeURIComponent(id)}/landing-preview`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<LandingPreviewResult?> GetMissionLandingPreviewAsync(string id, CancellationToken token = default)
        {
            return GetAsync<LandingPreviewResult>($"/api/v1/missions/{E(id)}/landing-preview", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionGitHubPullRequest</c>: GET `/api/v1/missions/${encodeURIComponent(id)}/github/pull-request`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<GitHubPullRequestDetail?> GetMissionGitHubPullRequestAsync(string id, CancellationToken token = default)
        {
            return GetAsync<GitHubPullRequestDetail>($"/api/v1/missions/{E(id)}/github/pull-request", null, token);
        }

        /// <summary>
        /// Dashboard <c>createMission</c>: POST '/api/v1/missions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Mission?> CreateMissionAsync(Mission data, CancellationToken token = default)
        {
            return PostAsync<Mission>("/api/v1/missions", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateMission</c>: PUT `/api/v1/missions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Mission?> UpdateMissionAsync(string id, Mission data, CancellationToken token = default)
        {
            return PutAsync<Mission>($"/api/v1/missions/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteMission</c>: DEL `/api/v1/missions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteMissionAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/missions/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>purgeMission</c>: DEL `/api/v1/missions/${id}/purge`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task PurgeMissionAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/missions/{E(id)}/purge", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>dispatchMission</c>: POST '/api/v1/missions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Mission?> DispatchMissionAsync(DispatchRequest data, CancellationToken token = default)
        {
            return PostAsync<Mission>("/api/v1/missions", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>restartMission</c>: POST `/api/v1/missions/${id}/restart`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Mission?> RestartMissionAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Mission>($"/api/v1/missions/{E(id)}/restart", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>retryMissionLanding</c>: POST `/api/v1/missions/${id}/retry-landing`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ArmadaRawJson?> RetryMissionLandingAsync(string id, CancellationToken token = default)
        {
            return PostAsync<ArmadaRawJson>($"/api/v1/missions/{E(id)}/retry-landing", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>transitionMission</c>: PUT `/api/v1/missions/${id}/status`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Mission?> TransitionMissionAsync(string id, TransitionRequest data, CancellationToken token = default)
        {
            return PutAsync<Mission>($"/api/v1/missions/{E(id)}/status", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>approveMissionReview</c>: POST /api/v1/missions/{id}/review/approve.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="options">Comment and conditional flag, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Mission?> ApproveMissionReviewAsync(string id, MissionReviewApproveRequest? options = null, CancellationToken token = default)
        {
            MissionReviewApproveRequest body = new MissionReviewApproveRequest();
            if (options != null && !String.IsNullOrEmpty(options.Comment)) body.Comment = options.Comment;
            if (options != null && options.Conditional == true) body.Conditional = true;
            return PostAsync<Mission>($"/api/v1/missions/{E(id)}/review/approve", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>denyMissionReview</c>: POST /api/v1/missions/{id}/review/deny.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="options">Comment and deny action, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Mission?> DenyMissionReviewAsync(string id, MissionReviewDenyRequest? options = null, CancellationToken token = default)
        {
            MissionReviewDenyRequest body = new MissionReviewDenyRequest();
            if (options != null && !String.IsNullOrEmpty(options.Comment)) body.Comment = options.Comment;
            if (options != null && !String.IsNullOrEmpty(options.Action)) body.Action = options.Action;
            return PostAsync<Mission>($"/api/v1/missions/{E(id)}/review/deny", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionDiff</c>: GET `/api/v1/missions/${id}/diff`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<DiffResult?> GetMissionDiffAsync(string id, CancellationToken token = default)
        {
            return GetAsync<DiffResult>($"/api/v1/missions/{E(id)}/diff", ArmadaRequestOptions.WithTimeout(30000), token);
        }

        /// <summary>
        /// Dashboard <c>getMissionLog</c>: GET `/api/v1/missions/${id}/log?lines=${lines}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="lines">lines.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<LogResult?> GetMissionLogAsync(string id, int lines = 500, CancellationToken token = default)
        {
            return GetAsync<LogResult>($"/api/v1/missions/{E(id)}/log?lines={lines}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionInstructions</c>: GET `/api/v1/missions/${id}/instructions`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<InstructionsResult?> GetMissionInstructionsAsync(string id, CancellationToken token = default)
        {
            return GetAsync<InstructionsResult>($"/api/v1/missions/{E(id)}/instructions", null, token);
        }

        #endregion
    }
}
