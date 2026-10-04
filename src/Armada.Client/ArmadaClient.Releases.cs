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
    /// Releases API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listReleases</c>: GET `/api/v1/releases${buildReleaseQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Release>?> ListReleasesAsync(ReleaseQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Release>>($"/api/v1/releases{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateReleases</c>: POST '/api/v1/releases/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Release>?> EnumerateReleasesAsync(ReleaseQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<Release>>("/api/v1/releases/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getRelease</c>: GET `/api/v1/releases/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Release?> GetReleaseAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Release>($"/api/v1/releases/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createRelease</c>: POST '/api/v1/releases'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Release?> CreateReleaseAsync(ReleaseUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<Release>("/api/v1/releases", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateRelease</c>: PUT `/api/v1/releases/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Release?> UpdateReleaseAsync(string id, ReleaseUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<Release>($"/api/v1/releases/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>refreshRelease</c>: POST `/api/v1/releases/${encodeURIComponent(id)}/refresh`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Release?> RefreshReleaseAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Release>($"/api/v1/releases/{E(id)}/refresh", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRelease</c>: DEL `/api/v1/releases/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteReleaseAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/releases/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>getReleaseGitHubPullRequests</c>: GET `/api/v1/releases/${encodeURIComponent(id)}/github/pull-requests`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<List<GitHubPullRequestDetail>?> GetReleaseGitHubPullRequestsAsync(string id, CancellationToken token = default)
        {
            return GetAsync<List<GitHubPullRequestDetail>>($"/api/v1/releases/{E(id)}/github/pull-requests", null, token);
        }

        #endregion
    }
}
