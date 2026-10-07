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
    /// Vessels API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listVessels</c>: GET `/api/v1/vessels${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<Vessel>?> ListVesselsAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<Vessel>>($"/api/v1/vessels{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVessel</c>: GET `/api/v1/vessels/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Vessel?> GetVesselAsync(string id, CancellationToken token = default)
        {
            return GetAsync<Vessel>($"/api/v1/vessels/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createVessel</c>: POST '/api/v1/vessels'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Vessel?> CreateVesselAsync(Vessel data, CancellationToken token = default)
        {
            return PostAsync<Vessel>("/api/v1/vessels", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateVessel</c>: PUT `/api/v1/vessels/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Vessel?> UpdateVesselAsync(string id, Vessel data, CancellationToken token = default)
        {
            return PutAsync<Vessel>($"/api/v1/vessels/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteVessel</c>: DEL `/api/v1/vessels/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteVesselAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/vessels/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>buildVesselContext</c>: POST /api/v1/vessels/{id}/build-context (15 minute timeout; a captain analyzes the repository).
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="data">Captain and notes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<Vessel?> BuildVesselContextAsync(string id, BuildVesselContextRequest data, CancellationToken token = default)
        {
            return PostAsync<Vessel>($"/api/v1/vessels/{E(id)}/build-context", data, ArmadaRequestOptions.WithTimeout(900000), token);
        }

        /// <summary>
        /// Dashboard <c>getVesselReadiness</c>: GET /api/v1/vessels/{id}/readiness.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="query">Optional filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselReadinessResult?> GetVesselReadinessAsync(string id, VesselReadinessQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<VesselReadinessResult>($"/api/v1/vessels/{E(id)}/readiness" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselLandingPreview</c>: GET /api/v1/vessels/{id}/landing-preview.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="sourceBranch">Source branch, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<LandingPreviewResult?> GetVesselLandingPreviewAsync(string id, string? sourceBranch = null, CancellationToken token = default)
        {
            return GetAsync<LandingPreviewResult>($"/api/v1/vessels/{E(id)}/landing-preview" + (String.IsNullOrEmpty(sourceBranch) ? "" : "?sourceBranch=" + E(sourceBranch)), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselGitStatus</c>: GET /api/v1/vessels/{id}/git-status.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselGitStatusResult?> GetVesselGitStatusAsync(string id, CancellationToken token = default)
        {
            return GetAsync<VesselGitStatusResult>($"/api/v1/vessels/{E(id)}/git-status", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselBranches</c>: GET /api/v1/vessels/{id}/branches.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselBranchesResult?> GetVesselBranchesAsync(string id, CancellationToken token = default)
        {
            return GetAsync<VesselBranchesResult>($"/api/v1/vessels/{E(id)}/branches", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselCommitActivity</c>: GET /api/v1/vessels/{id}/history/activity. Per-day commit counts
        /// for the history heatmap.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="query">Optional branch, date range, and UTC offset.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselCommitActivity?> GetVesselCommitActivityAsync(string id, VesselCommitActivityQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<VesselCommitActivity>($"/api/v1/vessels/{E(id)}/history/activity" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselCommits</c>: GET /api/v1/vessels/{id}/history/commits. One page of commit history,
        /// newest first; follow NextCursor for older pages.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="query">Optional branch, before date, cursor, and limit.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<VesselCommitPage?> GetVesselCommitsAsync(string id, VesselCommitQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<VesselCommitPage>($"/api/v1/vessels/{E(id)}/history/commits" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// Dashboard <c>pushVesselBranch</c>: POST /api/v1/vessels/{id}/branches/push.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="branch">Branch to push.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PushBranchResult?> PushVesselBranchAsync(string id, string branch, CancellationToken token = default)
        {
            return PostAsync<PushBranchResult>($"/api/v1/vessels/{E(id)}/branches/push", new { Branch = branch }, null, token);
        }

        /// <summary>
        /// Dashboard <c>mergeVesselBranch</c>: POST /api/v1/vessels/{id}/branches/merge.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="source">Source branch.</param>
        /// <param name="target">Target branch.</param>
        /// <param name="push">Push the target after merging.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<MergeBranchResult?> MergeVesselBranchAsync(string id, string source, string target, bool push, CancellationToken token = default)
        {
            return PostAsync<MergeBranchResult>($"/api/v1/vessels/{E(id)}/branches/merge", new { Source = source, Target = target, Push = push }, null, token);
        }

        #endregion
    }
}
