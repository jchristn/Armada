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
    public sealed class ReleasesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal ReleasesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listReleases</c>: GET `/api/v1/releases${buildReleaseQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listReleases")]
        public Task<EnumerationResult<Release>?> ListAsync(ReleaseQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Release>>($"/api/v1/releases{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateReleases</c>: POST '/api/v1/releases/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateReleases")]
        public Task<EnumerationResult<Release>?> EnumerateAsync(ReleaseQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<Release>>("/api/v1/releases/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getRelease</c>: GET `/api/v1/releases/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getRelease")]
        public Task<Release?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Release>($"/api/v1/releases/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createRelease</c>: POST '/api/v1/releases'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createRelease")]
        public Task<Release?> CreateAsync(ReleaseUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Release>("/api/v1/releases", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateRelease</c>: PUT `/api/v1/releases/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateRelease")]
        public Task<Release?> UpdateAsync(string id, ReleaseUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Release>($"/api/v1/releases/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>refreshRelease</c>: POST `/api/v1/releases/${encodeURIComponent(id)}/refresh`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("refreshRelease")]
        public Task<Release?> RefreshAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<Release>($"/api/v1/releases/{ArmadaQueryString.Escape(id)}/refresh", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteRelease</c>: DEL `/api/v1/releases/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteRelease")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/releases/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>getReleaseGitHubPullRequests</c>: POST `/api/v1/releases/{id}/github/pull-requests/enumerate`. One page
        /// of the release's pull requests (only that page is read from GitHub), with the total.
        /// </summary>
        /// <param name="id">Release id.</param>
        /// <param name="query">Paging, or null for the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getReleaseGitHubPullRequests")]
        public Task<EnumerationResult<GitHubPullRequestDetail>?> GetGitHubPullRequestsAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<GitHubPullRequestDetail>>($"/api/v1/releases/{ArmadaQueryString.Escape(id)}/github/pull-requests/enumerate", query ?? new EnumerationQuery(), null, token);
        }

        #endregion
    }
}
