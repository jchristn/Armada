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
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listWorkflowProfiles</c>: GET `/api/v1/workflow-profiles${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<WorkflowProfile>?> ListWorkflowProfilesAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<WorkflowProfile>>($"/api/v1/workflow-profiles{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getWorkflowProfile</c>: GET `/api/v1/workflow-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkflowProfile?> GetWorkflowProfileAsync(string id, CancellationToken token = default)
        {
            return GetAsync<WorkflowProfile>($"/api/v1/workflow-profiles/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createWorkflowProfile</c>: POST '/api/v1/workflow-profiles'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkflowProfile?> CreateWorkflowProfileAsync(WorkflowProfile data, CancellationToken token = default)
        {
            return PostAsync<WorkflowProfile>("/api/v1/workflow-profiles", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateWorkflowProfile</c>: PUT `/api/v1/workflow-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkflowProfile?> UpdateWorkflowProfileAsync(string id, WorkflowProfile data, CancellationToken token = default)
        {
            return PutAsync<WorkflowProfile>($"/api/v1/workflow-profiles/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteWorkflowProfile</c>: DEL `/api/v1/workflow-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteWorkflowProfileAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/workflow-profiles/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>validateWorkflowProfile</c>: POST '/api/v1/workflow-profiles/validate'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkflowProfileValidationResult?> ValidateWorkflowProfileAsync(WorkflowProfile data, CancellationToken token = default)
        {
            return PostAsync<WorkflowProfileValidationResult>("/api/v1/workflow-profiles/validate", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>previewWorkflowProfileForVessel</c>: GET /api/v1/workflow-profiles/preview/vessels/{vesselId}.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="workflowProfileId">Profile id, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkflowProfileResolutionPreviewResult?> PreviewWorkflowProfileForVesselAsync(string vesselId, string? workflowProfileId = null, CancellationToken token = default)
        {
            return GetAsync<WorkflowProfileResolutionPreviewResult>($"/api/v1/workflow-profiles/preview/vessels/{E(vesselId)}" + (String.IsNullOrEmpty(workflowProfileId) ? "" : "?workflowProfileId=" + E(workflowProfileId)), null, token);
        }

        /// <summary>
        /// Dashboard <c>resolveWorkflowProfile</c>: GET /api/v1/workflow-profiles/resolve/vessels/{vesselId}.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="workflowProfileId">Profile id, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<WorkflowProfile?> ResolveWorkflowProfileAsync(string vesselId, string? workflowProfileId = null, CancellationToken token = default)
        {
            return GetAsync<WorkflowProfile>($"/api/v1/workflow-profiles/resolve/vessels/{E(vesselId)}" + (String.IsNullOrEmpty(workflowProfileId) ? "" : "?workflowProfileId=" + E(workflowProfileId)), null, token);
        }

        #endregion
    }
}
