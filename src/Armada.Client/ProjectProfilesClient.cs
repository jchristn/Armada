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
    public sealed class ProjectProfilesClient
    {
        #region Private-Members

        private readonly ArmadaTransport _Transport;

        #endregion

        #region Constructors-and-Factories

        internal ProjectProfilesClient(ArmadaTransport transport)
        {
            _Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dashboard <c>listProjectProfiles</c>: GET `/api/v1/project-profiles${buildQuery(params)}`.
        /// </summary>
        /// <param name="query">query.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("listProjectProfiles")]
        public Task<EnumerationResult<ProjectProfile>?> ListAsync(ArmadaPageQuery? query = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<EnumerationResult<ProjectProfile>>($"/api/v1/project-profiles{ArmadaQueryString.FromPage(query)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>getProjectProfile</c>: GET `/api/v1/project-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("getProjectProfile")]
        public Task<ProjectProfile?> GetAsync(string id, CancellationToken token = default)
        {
            return _Transport.GetAsync<ProjectProfile>($"/api/v1/project-profiles/{ArmadaQueryString.Escape(id)}", null, token);
        }

        /// <summary>
        /// Dashboard <c>createProjectProfile</c>: POST '/api/v1/project-profiles'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("createProjectProfile")]
        public Task<ProjectProfile?> CreateAsync(ProjectProfile data, CancellationToken token = default)
        {
            return _Transport.PostAsync<ProjectProfile>("/api/v1/project-profiles", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>updateProjectProfile</c>: PUT `/api/v1/project-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("updateProjectProfile")]
        public Task<ProjectProfile?> UpdateAsync(string id, ProjectProfile data, CancellationToken token = default)
        {
            return _Transport.PutAsync<ProjectProfile>($"/api/v1/project-profiles/{ArmadaQueryString.Escape(id)}", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>deleteProjectProfile</c>: DEL `/api/v1/project-profiles/${encodeURIComponent(id)}`.
        /// </summary>
        /// <param name="id">id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("deleteProjectProfile")]
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Transport.SendNoResultAsync(HttpMethod.Delete, $"/api/v1/project-profiles/{ArmadaQueryString.Escape(id)}", null, null, token);
        }

        /// <summary>
        /// Dashboard <c>validateProjectProfile</c>: POST '/api/v1/project-profiles/validate'.
        /// </summary>
        /// <param name="data">data.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("validateProjectProfile")]
        public Task<ProjectProfileValidationResult?> ValidateAsync(ProjectProfile data, CancellationToken token = default)
        {
            return _Transport.PostAsync<ProjectProfileValidationResult>("/api/v1/project-profiles/validate", data, null, token);
        }

        /// <summary>
        /// Dashboard <c>resolveProjectProfileForVessel</c>: GET /api/v1/project-profiles/resolve/vessels/{vesselId}.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="projectProfileId">Profile id, or null for the vessel default.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("resolveProjectProfileForVessel")]
        public Task<ProjectProfileResolutionResult?> ResolveForVesselAsync(string vesselId, string? projectProfileId = null, CancellationToken token = default)
        {
            return _Transport.GetAsync<ProjectProfileResolutionResult>($"/api/v1/project-profiles/resolve/vessels/{ArmadaQueryString.Escape(vesselId)}" + (String.IsNullOrEmpty(projectProfileId) ? "" : "?projectProfileId=" + ArmadaQueryString.Escape(projectProfileId)), null, token);
        }

        /// <summary>
        /// Dashboard <c>previewPersonaPrompt</c>: GET `/api/v1/project-profiles/${encodeURIComponent(profileId)}/persona-preview/${encodeURIComponent(persona)}`.
        /// </summary>
        /// <param name="profileId">profileId.</param>
        /// <param name="persona">persona.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        [DashboardExport("previewPersonaPrompt")]
        public Task<PersonaPromptPreview?> PreviewPersonaPromptAsync(string profileId, string persona, CancellationToken token = default)
        {
            return _Transport.GetAsync<PersonaPromptPreview>($"/api/v1/project-profiles/{ArmadaQueryString.Escape(profileId)}/persona-preview/{ArmadaQueryString.Escape(persona)}", null, token);
        }

        #endregion
    }
}
