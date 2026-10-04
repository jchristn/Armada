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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listDeployments</c>: GET `/api/v1/deployments${buildDeploymentQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Deployment>?> ListDeploymentsAsync(DeploymentQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Deployment>>($"/api/v1/deployments{ArmadaQueryString.FromObject(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateDeployments</c>: POST '/api/v1/deployments/enumerate'.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Deployment>?> EnumerateDeploymentsAsync(DeploymentQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<Deployment>>("/api/v1/deployments/enumerate", query, null, token);
        }

        /// <summary>
        /// Dashboard <c>getDeployment</c>: GET `/api/v1/deployments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Deployment?> GetDeploymentAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Deployment>($"/api/v1/deployments/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createDeployment</c>: POST '/api/v1/deployments'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Deployment?> CreateDeploymentAsync(DeploymentUpsertRequest data, CancellationToken token = default)
        {
            return PostAsync<Deployment>("/api/v1/deployments", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateDeployment</c>: PUT `/api/v1/deployments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Deployment?> UpdateDeploymentAsync(string id, DeploymentUpsertRequest data, CancellationToken token = default)
        {
            return PutAsync<Deployment>($"/api/v1/deployments/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>syncGitHubActions</c>: POST '/api/v1/check-runs/sync/github-actions'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<GitHubActionsSyncResult?> SyncGitHubActionsAsync(GitHubActionsSyncRequest data, CancellationToken token = default)
        {
            return PostAsync<GitHubActionsSyncResult>("/api/v1/check-runs/sync/github-actions", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>approveDeployment</c>: POST /api/v1/deployments/{id}/approve.
        /// </summary>
        /// <param name="id">Deployment id.</param>
        /// <param name="comment">Optional comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Deployment?> ApproveDeploymentAsync(string id, string? comment = null, CancellationToken token = default)
        {
            return PostAsync<Deployment>($"/api/v1/deployments/{E(id)}/approve", new DecisionCommentRequest { Comment = String.IsNullOrEmpty(comment) ? null : comment }, null, token);
        }

        /// <summary>
        /// Dashboard <c>denyDeployment</c>: POST /api/v1/deployments/{id}/deny.
        /// </summary>
        /// <param name="id">Deployment id.</param>
        /// <param name="comment">Optional comment.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Deployment?> DenyDeploymentAsync(string id, string? comment = null, CancellationToken token = default)
        {
            return PostAsync<Deployment>($"/api/v1/deployments/{E(id)}/deny", new DecisionCommentRequest { Comment = String.IsNullOrEmpty(comment) ? null : comment }, null, token);
        }

        /// <summary>
        /// Dashboard <c>verifyDeployment</c>: POST `/api/v1/deployments/${encodeURIComponent(id)}/verify`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Deployment?> VerifyDeploymentAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Deployment>($"/api/v1/deployments/{E(id)}/verify", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>rollbackDeployment</c>: POST `/api/v1/deployments/${encodeURIComponent(id)}/rollback`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Deployment?> RollbackDeploymentAsync(string id, CancellationToken token = default)
        {
            return PostAsync<Deployment>($"/api/v1/deployments/{E(id)}/rollback", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteDeployment</c>: DEL `/api/v1/deployments/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteDeploymentAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/deployments/{E(id)}", null, null, token);
        }

        #endregion
    }
}
