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
    /// Deployments API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class DeploymentsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal DeploymentsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listDeployments</c>: GET `/api/v1/deployments${buildDeploymentQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listDeployments")]
        public Task<EnumerationResult<Deployment>?> ListAsync(DeploymentQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Deployment>>($"/api/v1/deployments{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateDeployments</c>: POST '/api/v1/deployments/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateDeployments")]
        public Task<EnumerationResult<Deployment>?> EnumerateAsync(DeploymentQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<Deployment>>("/api/v1/deployments/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getDeployment</c>: GET `/api/v1/deployments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getDeployment")]
        public Task<Deployment?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Deployment>($"/api/v1/deployments/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createDeployment</c>: POST '/api/v1/deployments'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createDeployment")]
        public Task<Deployment?> CreateAsync(DeploymentUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Deployment>("/api/v1/deployments", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateDeployment</c>: PUT `/api/v1/deployments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateDeployment")]
        public Task<Deployment?> UpdateAsync(string id, DeploymentUpsertRequest data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Deployment>($"/api/v1/deployments/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>syncGitHubActions</c>: POST '/api/v1/check-runs/sync/github-actions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("syncGitHubActions")]
        public Task<GitHubActionsSyncResult?> SyncGitHubActionsAsync(GitHubActionsSyncRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<GitHubActionsSyncResult>("/api/v1/check-runs/sync/github-actions", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>approveDeployment</c>: POST /api/v1/deployments/{id}/approve.
        /// </summary>
        /// <param name="id">Deployment id.</param>
        /// <param name="comment">Optional comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("approveDeployment")]
        public Task<Deployment?> ApproveAsync(string id, string? comment = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<Deployment>($"/api/v1/deployments/{ArmadaQueryString.Escape(id)}/approve", new DecisionCommentRequest { Comment = String.IsNullOrEmpty(comment) ? null : comment }, null, token);
        }

        /// <summary>
        /// Dashboard <c>denyDeployment</c>: POST /api/v1/deployments/{id}/deny.
        /// </summary>
        /// <param name="id">Deployment id.</param>
        /// <param name="comment">Optional comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("denyDeployment")]
        public Task<Deployment?> DenyAsync(string id, string? comment = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<Deployment>($"/api/v1/deployments/{ArmadaQueryString.Escape(id)}/deny", new DecisionCommentRequest { Comment = String.IsNullOrEmpty(comment) ? null : comment }, null, token);
        }

        /// <summary>
        /// Dashboard <c>verifyDeployment</c>: POST `/api/v1/deployments/${encodeURIComponent(id)}/verify`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("verifyDeployment")]
        public Task<Deployment?> VerifyAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<Deployment>($"/api/v1/deployments/{ArmadaQueryString.Escape(id)}/verify", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>rollbackDeployment</c>: POST `/api/v1/deployments/${encodeURIComponent(id)}/rollback`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("rollbackDeployment")]
        public Task<Deployment?> RollbackAsync(string id, CancellationToken token = default)
        {
            return _Transport.PostAsync<Deployment>($"/api/v1/deployments/{ArmadaQueryString.Escape(id)}/rollback", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteDeployment</c>: DEL `/api/v1/deployments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteDeployment")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/deployments/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        #endregion
    }
}
