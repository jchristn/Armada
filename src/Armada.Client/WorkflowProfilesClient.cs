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
    /// WorkflowProfiles API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public sealed class WorkflowProfilesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal WorkflowProfilesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listWorkflowProfiles</c>: GET `/api/v1/workflow-profiles${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listWorkflowProfiles")]
        public Task<EnumerationResult<WorkflowProfile>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<WorkflowProfile>>($"/api/v1/workflow-profiles{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkflowProfile</c>: GET `/api/v1/workflow-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getWorkflowProfile")]
        public Task<WorkflowProfile?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<WorkflowProfile>($"/api/v1/workflow-profiles/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createWorkflowProfile</c>: POST '/api/v1/workflow-profiles'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createWorkflowProfile")]
        public Task<WorkflowProfile?> CreateAsync(WorkflowProfile data, CancellationToken token = default)
        {
            return _Transport.PostAsync<WorkflowProfile>("/api/v1/workflow-profiles", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateWorkflowProfile</c>: PUT `/api/v1/workflow-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateWorkflowProfile")]
        public Task<WorkflowProfile?> UpdateAsync(string id, WorkflowProfile data, CancellationToken token = default)
        {
            return _Transport.PutAsync<WorkflowProfile>($"/api/v1/workflow-profiles/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteWorkflowProfile</c>: DEL `/api/v1/workflow-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteWorkflowProfile")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/workflow-profiles/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>validateWorkflowProfile</c>: POST '/api/v1/workflow-profiles/validate'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("validateWorkflowProfile")]
        public Task<WorkflowProfileValidationResult?> ValidateAsync(WorkflowProfile data, CancellationToken token = default)
        {
            return _Transport.PostAsync<WorkflowProfileValidationResult>("/api/v1/workflow-profiles/validate", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>previewWorkflowProfileForVessel</c>: GET /api/v1/workflow-profiles/preview/vessels/{vesselId}.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="workflowProfileId">Profile id, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("previewWorkflowProfileForVessel")]
        public Task<WorkflowProfileResolutionPreviewResult?> PreviewForVesselAsync(string vesselId, string? workflowProfileId = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<WorkflowProfileResolutionPreviewResult>($"/api/v1/workflow-profiles/preview/vessels/{ArmadaQueryString.Escape(vesselId)}" + (String.IsNullOrEmpty(workflowProfileId) ? "" : "?workflowProfileId=" + ArmadaQueryString.Escape(workflowProfileId)), null, token);
        }

        /// <summary>
        /// Dashboard <c>resolveWorkflowProfile</c>: GET /api/v1/workflow-profiles/resolve/vessels/{vesselId}.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="workflowProfileId">Profile id, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("resolveWorkflowProfile")]
        public Task<WorkflowProfile?> ResolveAsync(string vesselId, string? workflowProfileId = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<WorkflowProfile>($"/api/v1/workflow-profiles/resolve/vessels/{ArmadaQueryString.Escape(vesselId)}" + (String.IsNullOrEmpty(workflowProfileId) ? "" : "?workflowProfileId=" + ArmadaQueryString.Escape(workflowProfileId)), null, token);
        }

        /// <summary>
        /// Dashboard <c>enumerateWorkflowProfiles</c>: POST '/api/v1/workflow-profiles/enumerate'.
        /// </summary>
        /// <param name="query">Filters (scope, fleetId, vesselId, search, active, dates) and paging, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("enumerateWorkflowProfiles")]
        public Task<EnumerationResult<WorkflowProfile>?> EnumerateAsync(WorkflowProfileQuery? query = null, CancellationToken token = default)
        {
            return _Transport.PostAsync<EnumerationResult<WorkflowProfile>>("/api/v1/workflow-profiles/enumerate", query ?? new WorkflowProfileQuery(), null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteWorkflowProfilesBatch</c>: POST '/api/v1/workflow-profiles/delete/multiple'.
        /// </summary>
        /// <param name="ids">ids.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteWorkflowProfilesBatch")]
        public Task<BatchDeleteResult?> DeleteBatchAsync(List<string> ids, CancellationToken token = default)
        {
            return _Transport.PostAsync<BatchDeleteResult>("/api/v1/workflow-profiles/delete/multiple", new { Ids = ids }, null, token);
        }

        #endregion
    }
}
