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
    public sealed class MissionsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal MissionsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listMissions</c>: GET `/api/v1/missions${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listMissions")]
        public Task<EnumerationResult<Mission>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Mission>>($"/api/v1/missions{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>listMissionSummaries</c>: GET `/api/v1/missions/summaries${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listMissionSummaries")]
        public Task<EnumerationResult<MissionSummary>?> ListSummariesAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<MissionSummary>>($"/api/v1/missions/summaries{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMission</c>: GET `/api/v1/missions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMission")]
        public Task<Mission?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Mission>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionHistory</c>: GET /api/v1/missions/history.
        /// </summary>
        /// <param name="query">Range and filters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMissionHistory")]
        public Task<MissionHistorySummaryResult?> GetHistoryAsync(MissionHistoryFilter? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<MissionHistorySummaryResult>("/api/v1/missions/history" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionLandingPreview</c>: GET `/api/v1/missions/${encodeURIComponent(id)}/landing-preview`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMissionLandingPreview")]
        public Task<LandingPreviewResult?> GetLandingPreviewAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<LandingPreviewResult>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/landing-preview", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionGitHubPullRequest</c>: GET `/api/v1/missions/${encodeURIComponent(id)}/github/pull-request`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMissionGitHubPullRequest")]
        public Task<GitHubPullRequestDetail?> GetGitHubPullRequestAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<GitHubPullRequestDetail>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/github/pull-request", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateMissionPullRequestReviews</c>: POST `/api/v1/missions/${encodeURIComponent(id)}/github/pull-request/reviews/enumerate`.
        /// One page of the pull request's reviews, paged by GitHub (page size 1 to 100).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="query">Paging (page number, size, order), or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateMissionPullRequestReviews")]
        public Task<EnumerationResult<GitHubPullRequestReview>?> EnumeratePullRequestReviewsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<GitHubPullRequestReview>>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/github/pull-request/reviews/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateMissionPullRequestComments</c>: POST `/api/v1/missions/${encodeURIComponent(id)}/github/pull-request/comments/enumerate`.
        /// One page of the pull request's comments, paged by GitHub (page size 1 to 100).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="query">Paging (page number, size, order), or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateMissionPullRequestComments")]
        public Task<EnumerationResult<GitHubPullRequestComment>?> EnumeratePullRequestCommentsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<GitHubPullRequestComment>>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/github/pull-request/comments/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateMissionPullRequestChecks</c>: POST `/api/v1/missions/${encodeURIComponent(id)}/github/pull-request/checks/enumerate`.
        /// One page of the pull request's checks, paged by GitHub (page size 1 to 100).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="query">Paging (page number, size, order), or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateMissionPullRequestChecks")]
        public Task<EnumerationResult<GitHubPullRequestCheck>?> EnumeratePullRequestChecksAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<GitHubPullRequestCheck>>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/github/pull-request/checks/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>createMission</c>: POST '/api/v1/missions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created mission. Servers before v1.3.0 wrapped a mission no captain could take yet as
        /// <c>{ Mission, Warning }</c>; this method reads both shapes (use <see cref="DispatchAsync"/> to keep the warning).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createMission")]
        public async Task<Mission?> CreateAsync(Mission data, CancellationToken token = default)
        {
            ArmadaRawJson? raw = await _Transport.PostAsync<ArmadaRawJson>("/api/v1/missions", data, null, token).ConfigureAwait(false);
            return MissionDispatchResult.Parse(raw?.Json)?.Mission;
        }

        /// <summary>
        /// Dashboard <c>updateMission</c>: PUT `/api/v1/missions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateMission")]
        public Task<Mission?> UpdateAsync(string id, Mission data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Mission>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteMission</c>: DEL `/api/v1/missions/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteMission")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/missions/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>purgeMission</c>: DEL `/api/v1/missions/${id}/purge`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("purgeMission")]
        public Task PurgeAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/missions/{ArmadaQueryString.Escape(id)}/purge", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>dispatchMission</c>: POST '/api/v1/missions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created mission and, when no captain could take it yet (status Pending), a warning. Both the
        /// current bare Mission and the pre-v1.3.0 <c>{ Mission, Warning }</c> reply are read.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("dispatchMission")]
        public async Task<MissionDispatchResult?> DispatchAsync(DispatchRequest data, CancellationToken token = default)
        {
            ArmadaRawJson? raw = await _Transport.PostAsync<ArmadaRawJson>("/api/v1/missions", data, null, token).ConfigureAwait(false);
            return MissionDispatchResult.Parse(raw?.Json);
        }

        /// <summary>
        /// Dashboard <c>restartMission</c>: POST `/api/v1/missions/${id}/restart`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("restartMission")]
        public Task<Mission?> RestartAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<Mission>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/restart", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>retryMissionLanding</c>: POST `/api/v1/missions/${id}/retry-landing`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("retryMissionLanding")]
        public Task<ArmadaRawJson?> RetryLandingAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<ArmadaRawJson>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/retry-landing", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>transitionMission</c>: PUT `/api/v1/missions/${id}/status`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("transitionMission")]
        public Task<Mission?> TransitionAsync(string id, TransitionRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Mission>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/status", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>approveMissionReview</c>: POST /api/v1/missions/{id}/review/approve.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="options">Comment and conditional flag, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("approveMissionReview")]
        public Task<Mission?> ApproveReviewAsync(string id, MissionReviewApproveRequest? options = null, CancellationToken token = default)
        {
            MissionReviewApproveRequest body = new MissionReviewApproveRequest();
            if (options != null && !String.IsNullOrEmpty(options.Comment)) body.Comment = options.Comment;
            if (options != null && options.Conditional == true) body.Conditional = true;
            return _Transport.PostAsync<Mission>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/review/approve", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>denyMissionReview</c>: POST /api/v1/missions/{id}/review/deny.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="options">Comment and deny action, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("denyMissionReview")]
        public Task<Mission?> DenyReviewAsync(string id, MissionReviewDenyRequest? options = null, CancellationToken token = default)
        {
            MissionReviewDenyRequest body = new MissionReviewDenyRequest();
            if (options != null && !String.IsNullOrEmpty(options.Comment)) body.Comment = options.Comment;
            if (options != null && !String.IsNullOrEmpty(options.Action)) body.Action = options.Action;
            return _Transport.PostAsync<Mission>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/review/deny", body, null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionDiff</c>: GET `/api/v1/missions/${id}/diff`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMissionDiff")]
        public Task<DiffResult?> GetDiffAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<DiffResult>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/diff", ArmadaRequestOptions.WithTimeout(30000), token);
        }

        /// <summary>
        /// Dashboard <c>getMissionLog</c>: GET `/api/v1/missions/${id}/log?lines=${lines}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="lines">lines.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMissionLog")]
        public Task<LogResult?> GetLogAsync(string id, int lines = 500, CancellationToken token = default)
        {
            return _Transport.GetAsync<LogResult>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/log?lines={lines}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getMissionInstructions</c>: GET `/api/v1/missions/${id}/instructions`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getMissionInstructions")]
        public Task<InstructionsResult?> GetInstructionsAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<InstructionsResult>($"/api/v1/missions/{ArmadaQueryString.Escape(id)}/instructions", null, token);
        }

        #endregion
    }
}
