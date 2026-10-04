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
    /// ProjectProfiles API calls (dashboard <c>api/client.ts</c> parity).
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listProjectProfiles</c>: GET `/api/v1/project-profiles${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ProjectProfile>?> ListProjectProfilesAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return GetAsync<EnumerationResult<ProjectProfile>>($"/api/v1/project-profiles{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getProjectProfile</c>: GET `/api/v1/project-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ProjectProfile?> GetProjectProfileAsync(string id, CancellationToken token = default)
        {
            return GetAsync<ProjectProfile>($"/api/v1/project-profiles/{E(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createProjectProfile</c>: POST '/api/v1/project-profiles'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ProjectProfile?> CreateProjectProfileAsync(ProjectProfile data, CancellationToken token = default)
        {
            return PostAsync<ProjectProfile>("/api/v1/project-profiles", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateProjectProfile</c>: PUT `/api/v1/project-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ProjectProfile?> UpdateProjectProfileAsync(string id, ProjectProfile data, CancellationToken token = default)
        {
            return PutAsync<ProjectProfile>($"/api/v1/project-profiles/{E(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteProjectProfile</c>: DEL `/api/v1/project-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task DeleteProjectProfileAsync(string id, CancellationToken token = default)
        {
            return SendNoResultAsync(HttpMethod.Delete, $"/api/v1/project-profiles/{E(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>validateProjectProfile</c>: POST '/api/v1/project-profiles/validate'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ProjectProfileValidationResult?> ValidateProjectProfileAsync(ProjectProfile data, CancellationToken token = default)
        {
            return PostAsync<ProjectProfileValidationResult>("/api/v1/project-profiles/validate", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>resolveProjectProfileForVessel</c>: GET /api/v1/project-profiles/resolve/vessels/{vesselId}.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="projectProfileId">Profile id, or null for the vessel default.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ProjectProfileResolutionResult?> ResolveProjectProfileForVesselAsync(string vesselId, string? projectProfileId = null, CancellationToken token = default)
        {
            return GetAsync<ProjectProfileResolutionResult>($"/api/v1/project-profiles/resolve/vessels/{E(vesselId)}" + (String.IsNullOrEmpty(projectProfileId) ? "" : "?projectProfileId=" + E(projectProfileId)), null, token);
        }

        /// <summary>
        /// Dashboard <c>previewPersonaPrompt</c>: GET `/api/v1/project-profiles/${encodeURIComponent(profileId)}/persona-preview/${encodeURIComponent(persona)}`.
        /// </summary>
        /// <param name="profileId">profileId.</param>
        /// <param name="persona">persona.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<PersonaPromptPreview?> PreviewPersonaPromptAsync(string profileId, string persona, CancellationToken token = default)
        {
            return GetAsync<PersonaPromptPreview>($"/api/v1/project-profiles/{E(profileId)}/persona-preview/{E(persona)}", null, token);
        }

        #endregion
    }
}
