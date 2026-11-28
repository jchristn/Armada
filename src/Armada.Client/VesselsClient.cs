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
    public sealed class VesselsClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal VesselsClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listVessels</c>: GET `/api/v1/vessels${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listVessels")]
        public Task<EnumerationResult<Vessel>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<Vessel>>($"/api/v1/vessels{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVessel</c>: GET `/api/v1/vessels/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVessel")]
        public Task<Vessel?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<Vessel>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createVessel</c>: POST '/api/v1/vessels'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createVessel")]
        public Task<Vessel?> CreateAsync(Vessel data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Vessel>("/api/v1/vessels", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateVessel</c>: PUT `/api/v1/vessels/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateVessel")]
        public Task<Vessel?> UpdateAsync(string id, Vessel data, CancellationToken token = default)
        {
            return _Transport.PutAsync<Vessel>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateVesselContext</c>: PATCH `/api/v1/vessels/${id}/context`.
        /// Writes project context, style guide, and model context. A null property leaves the stored value in place.
        /// Send an empty string to clear a text field.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="data">Context fields to write.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated vessel.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateVesselContext")]
        public Task<Vessel?> UpdateContextAsync(string id, Vessel data, CancellationToken token = default)
        {
            return _Transport.PatchAsync<Vessel>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/context", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteVessel</c>: DEL `/api/v1/vessels/${id}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteVessel")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/vessels/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>buildVesselContext</c>: POST /api/v1/vessels/{id}/build-context (15 minute timeout; a captain analyzes the repository).
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="data">Captain and notes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("buildVesselContext")]
        public Task<Vessel?> BuildContextAsync(string id, BuildVesselContextRequest data, CancellationToken token = default)
        {
            return _Transport.PostAsync<Vessel>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/build-context", data, ArmadaRequestOptions.WithTimeout(900000), token);
        }

        /// <summary>
        /// Dashboard <c>getVesselReadiness</c>: GET /api/v1/vessels/{id}/readiness.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="query">Optional filters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselReadiness")]
        public Task<VesselReadinessResult?> GetReadinessAsync(string id, VesselReadinessQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<VesselReadinessResult>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/readiness" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselLandingPreview</c>: GET /api/v1/vessels/{id}/landing-preview.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="sourceBranch">Source branch, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselLandingPreview")]
        public Task<LandingPreviewResult?> GetLandingPreviewAsync(string id, string? sourceBranch = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<LandingPreviewResult>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/landing-preview" + (String.IsNullOrEmpty(sourceBranch) ? "" : "?sourceBranch=" + ArmadaQueryString.Escape(sourceBranch)), null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselCleanupInventory</c>: GET /api/v1/vessels/{id}/cleanup. Read-only inventory of the vessel's
        /// docks, orphaned worktrees, and Armada-managed branches, classified Safe, InUse, or Unknown.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The inventory.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselCleanupInventory")]
        public Task<VesselCleanupInventory?> GetCleanupInventoryAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<VesselCleanupInventory>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/cleanup", ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>listVesselCleanupResources</c>: POST /api/v1/vessels/{id}/cleanup/resources/enumerate. One page of
        /// the vessel's cleanup inventory (the inventory read carries only counts).
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="query">Paging (page size 1 to 1000); Status filters by disposition (Safe, InUse, Unknown). Null is the first page of 10.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listVesselCleanupResources")]
        public Task<EnumerationResult<VesselCleanupResource>?> ListCleanupResourcesAsync(string id, EnumerationQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<VesselCleanupResource>>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/cleanup/resources/enumerate", query ?? new EnumerationQuery(), ArmadaRequestOptions.WithTimeout(300000), token);
        }

        /// <summary>
        /// Dashboard <c>runVesselCleanup</c>: POST /api/v1/vessels/{id}/cleanup. Removes the selected inventory resources
        /// that are still safe after server-side revalidation.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="keys">Resource keys from the inventory.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Per-item outcomes and the refreshed inventory.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("runVesselCleanup")]
        public Task<VesselCleanupResult?> RunCleanupAsync(string id, List<string> keys, CancellationToken token = default)
        {
            return _Transport.PostAsync<VesselCleanupResult>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/cleanup", new VesselCleanupRequest { Keys = keys ?? new List<string>() }, ArmadaRequestOptions.WithTimeout(600000), token);
        }

        /// <summary>
        /// Dashboard <c>getVesselGitStatus</c>: GET /api/v1/vessels/{id}/git-status.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselGitStatus")]
        public Task<VesselGitStatusResult?> GetGitStatusAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<VesselGitStatusResult>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/git-status", null, token);
        }

        /// <summary>
        /// Dashboard <c>getVesselBranches</c>: GET /api/v1/vessels/{id}/branches.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselBranches")]
        public async Task<VesselBranchesResult?> GetBranchesAsync(string id, CancellationToken token = default)
        {
            VesselBranchesResult result = new VesselBranchesResult { VesselId = id };
            bool first = true;
            List<Armada.Core.Models.BranchInfo> all = await ArmadaPaging.ReadAllAsync<Armada.Core.Models.BranchInfo>(async (pageNumber, pageToken) =>
            {
                BranchEnumerationResult? page = await _Transport.PostAsync<BranchEnumerationResult>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/branches/enumerate",
                    new { PageNumber = pageNumber, PageSize = 1000 }, null, pageToken).ConfigureAwait(false);
                if (first && page != null)
                {
                    first = false;
                    result.DefaultBranch = page.DefaultBranch;
                    result.Error = page.Error;
                }
                return page;
            }, ArmadaPaging.DefaultMaxPages, token).ConfigureAwait(false);
            result.Branches = all;
            result.BranchCount = all.Count;
            return result;
        }

        /// <summary>
        /// Dashboard <c>getVesselBranchCount</c>: POST /api/v1/vessels/{id}/branches/enumerate for a one-branch page,
        /// returning TotalRecords. The server measures ahead/behind only for the branches on the page, so this costs
        /// one git command instead of one per branch.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of branches (0 when they could not be read).</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getVesselBranchCount")]
        public async Task<int> GetBranchCountAsync(string id, CancellationToken token = default)
        {
            BranchEnumerationResult? page = await _Transport.PostAsync<BranchEnumerationResult>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/branches/enumerate",
                new { PageNumber = 1, PageSize = 1 }, null, token).ConfigureAwait(false);
            return page == null ? 0 : (int)Math.Min(page.TotalRecords, Int32.MaxValue);
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
        [DashboardExport("getVesselCommitActivity")]
        public Task<VesselCommitActivity?> GetCommitActivityAsync(string id, VesselCommitActivityQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<VesselCommitActivity>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/history/activity" + ArmadaQueryString.FromObject(query), null, token);
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
        [DashboardExport("getVesselCommits")]
        public Task<VesselCommitPage?> GetCommitsAsync(string id, VesselCommitQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<VesselCommitPage>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/history/commits" + ArmadaQueryString.FromObject(query), null, token);
        }

        /// <summary>
        /// Dashboard <c>pushVesselBranch</c>: POST /api/v1/vessels/{id}/branches/push.
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <param name="branch">Branch to push.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("pushVesselBranch")]
        public Task<PushBranchResult?> PushBranchAsync(string id, string branch, CancellationToken token = default)
        {
            return _Transport.PostAsync<PushBranchResult>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/branches/push", new { Branch = branch }, null, token);
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
        [DashboardExport("mergeVesselBranch")]
        public Task<MergeBranchResult?> MergeBranchAsync(string id, string source, string target, bool push, CancellationToken token = default)
        {
            return _Transport.PostAsync<MergeBranchResult>($"/api/v1/vessels/{ArmadaQueryString.Escape(id)}/branches/merge", new { Source = source, Target = target, Push = push }, null, token);
        }

        #endregion
    }
}
