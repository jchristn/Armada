namespace Armada.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Http;
    using Armada.Core.Models;

    /// <summary>
    /// Model profile calls (<c>/api/v1/model-profiles</c>): list and read the profiles of the models captains run, update
    /// their mutable parts, and mark revisions compatible. Profiles are advisory and change no routing.
    /// </summary>
    public partial class ArmadaClient
    {
        #region Public-Methods

        /// <summary>
        /// GET /api/v1/model-profiles: one page of the profiles visible to the caller, newest first, without revisions.
        /// </summary>
        /// <param name="query">Filters and paging, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ModelProfile>?> ListModelProfilesAsync(ModelProfileQuery? query = null, CancellationToken token = default)
        {
            string querystring = String.Empty;
            if (query != null)
            {
                querystring = ArmadaQueryString.FromPairs(new List<KeyValuePair<string, string?>>
                {
                    new KeyValuePair<string, string?>("tenantId", query.TenantId),
                    new KeyValuePair<string, string?>("runtime", query.Runtime),
                    new KeyValuePair<string, string?>("provider", query.Provider),
                    new KeyValuePair<string, string?>("model", query.ModelContains),
                    new KeyValuePair<string, string?>("pageNumber", query.PageNumber.ToString(CultureInfo.InvariantCulture)),
                    new KeyValuePair<string, string?>("pageSize", query.PageSize.ToString(CultureInfo.InvariantCulture))
                });
            }

            return GetAsync<EnumerationResult<ModelProfile>>($"/api/v1/model-profiles{querystring}", null, token);
        }

        /// <summary>
        /// POST /api/v1/model-profiles/enumerate: one page of the profiles visible to the caller, newest first.
        /// </summary>
        /// <param name="query">Paging and filters, or null for the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One page.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<EnumerationResult<ModelProfile>?> EnumerateModelProfilesAsync(ModelProfileQuery? query = null, CancellationToken token = default)
        {
            return PostAsync<EnumerationResult<ModelProfile>>("/api/v1/model-profiles/enumerate", query ?? new ModelProfileQuery(), null, token);
        }

        /// <summary>
        /// GET /api/v1/model-profiles/{id}: one profile with its revisions, oldest first.
        /// </summary>
        /// <param name="id">Profile id (mdl_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The profile.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelProfile?> GetModelProfileAsync(string id, CancellationToken token = default)
        {
            return GetAsync<ModelProfile>($"/api/v1/model-profiles/{E(id)}", null, token);
        }

        /// <summary>
        /// GET /api/v1/model-profiles/{id}/scorecard: the read-only scorecard of one (profile, persona), optionally one
        /// vessel, over the last WindowDays days or WindowMissions missions, whichever is smaller (defaults 30 and 50).
        /// </summary>
        /// <param name="id">Profile id (mdl_ prefix).</param>
        /// <param name="request">Persona (required), vessel and window.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scorecard.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id or request is null.</exception>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelScorecard?> GetModelScorecardAsync(string id, ModelScorecardRequest request, CancellationToken token = default)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));
            string querystring = ArmadaQueryString.FromPairs(new List<KeyValuePair<string, string?>>
            {
                new KeyValuePair<string, string?>("persona", request.Persona),
                new KeyValuePair<string, string?>("vesselId", request.VesselId),
                new KeyValuePair<string, string?>("windowDays", request.WindowDays?.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string?>("windowMissions", request.WindowMissions?.ToString(CultureInfo.InvariantCulture))
            });
            return GetAsync<ModelScorecard>($"/api/v1/model-profiles/{E(id)}/scorecard{querystring}", null, token);
        }

        /// <summary>
        /// PUT /api/v1/model-profiles/{id}: update capabilities, rendering overrides and notes (admins only). A section the
        /// request leaves null keeps its value.
        /// </summary>
        /// <param name="id">Profile id (mdl_ prefix).</param>
        /// <param name="request">Update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated profile with its revisions.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelProfile?> UpdateModelProfileAsync(string id, ModelProfileUpdateRequest request, CancellationToken token = default)
        {
            return PutAsync<ModelProfile>($"/api/v1/model-profiles/{E(id)}", request, null, token);
        }

        /// <summary>
        /// PUT /api/v1/model-profiles/{id}/revisions/{revisionId}/compatible-with: replace the revisions marked compatible
        /// with a revision (symmetric; an empty list removes every mark). Admins only; revisions are never edited.
        /// </summary>
        /// <param name="id">Profile id (mdl_ prefix).</param>
        /// <param name="revisionId">Revision id (mdv_ prefix) of that profile.</param>
        /// <param name="request">The compatible revisions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The revision with its compatibility marks.</returns>
        /// <exception cref="ArmadaApiException">Thrown for a non-success response, timeout, or transport failure.</exception>
        public Task<ModelProfileRevision?> SetModelProfileRevisionCompatibilityAsync(string id, string revisionId, ModelProfileCompatibilityRequest request, CancellationToken token = default)
        {
            return PutAsync<ModelProfileRevision>($"/api/v1/model-profiles/{E(id)}/revisions/{E(revisionId)}/compatible-with", request, null, token);
        }

        #endregion
    }
}
