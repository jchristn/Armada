namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Caller-scoped access to model profiles, shared by REST and MCP. Anyone signed in reads the profiles of their own
    /// tenant (a global admin reads every tenant); only a global admin or a tenant admin of the profile's tenant changes
    /// the mutable parts (capabilities, rendering, notes) or marks revisions compatible. Revisions themselves are never
    /// edited. Throws KeyNotFound (404), Argument (400) and UnauthorizedAccess (403) exceptions.
    /// </summary>
    public class ModelProfileAccessService
    {
        #region Private-Members

        private string _Header = "[ModelProfileAccessService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelProfileAccessService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// One page of profiles visible to the caller, newest first, without revisions.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="query">Filters and paging, or null for the defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The page.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not signed in or has no tenant.</exception>
        public async Task<EnumerationResult<ModelProfile>> ListAsync(AuthContext caller, ModelProfileQuery? query, CancellationToken token = default)
        {
            RequirePerson(caller);
            ModelProfileQuery source = query ?? new ModelProfileQuery();
            ModelProfileQuery effective = new ModelProfileQuery
            {
                TenantId = caller.IsAdmin ? Blank(source.TenantId) : caller.TenantId,
                Runtime = Blank(source.Runtime),
                Provider = Blank(source.Provider),
                ModelContains = Blank(source.ModelContains),
                PageNumber = source.PageNumber,
                PageSize = source.PageSize
            };
            return await _Database.ModelProfiles.EnumerateAsync(effective, token).ConfigureAwait(false);
        }

        /// <summary>
        /// One profile visible to the caller, with its revisions oldest first.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The profile.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the profile does not exist or is in another tenant.</exception>
        public async Task<ModelProfile> GetAsync(AuthContext caller, string? profileId, CancellationToken token = default)
        {
            RequirePerson(caller);
            ModelProfile profile = await ReadVisibleAsync(caller, profileId, token).ConfigureAwait(false);
            profile.Revisions = await _Database.ModelProfiles.ReadRevisionsAsync(profile.Id, token).ConfigureAwait(false);
            return profile;
        }

        /// <summary>
        /// Update the mutable parts of a profile. A section the request omits keeps its value.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="request">Update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated profile with its revisions.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the profile does not exist or is in another tenant.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin.</exception>
        /// <exception cref="ArgumentException">Thrown for an invalid value.</exception>
        public async Task<ModelProfile> UpdateAsync(AuthContext caller, string? profileId, ModelProfileUpdateRequest? request, CancellationToken token = default)
        {
            RequirePerson(caller);
            ModelProfile profile = await ReadVisibleAsync(caller, profileId, token).ConfigureAwait(false);
            RequireAdmin(caller);
            if (request == null) throw new ArgumentException("A request body is required.", nameof(request));

            if (request.Capabilities != null)
                profile.Capabilities = new ModelProfileCapabilities { ToolsAvailable = request.Capabilities.ToolsAvailable, McpAvailable = request.Capabilities.McpAvailable };
            if (request.Rendering != null)
            {
                string? template = Blank(request.Rendering.Template);
                if (template != null && template.Length > 256) throw new ArgumentException("Rendering.Template must be at most 256 characters.");
                profile.Rendering = new ModelProfileRendering
                {
                    Template = template,
                    RepeatContractAtEnd = request.Rendering.RepeatContractAtEnd,
                    MaxFilesPerMission = request.Rendering.MaxFilesPerMission
                };
            }

            if (request.Notes != null)
            {
                if (request.Notes.Length > 4000) throw new ArgumentException("Notes must be at most 4000 characters.");
                profile.Notes = String.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes;
            }

            await _Database.ModelProfiles.UpdateAsync(profile, token).ConfigureAwait(false);
            _Logging.Info(_Header + "profile " + profile.Id + " updated by " + caller.UserId);
            return await GetAsync(caller, profile.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Replace the revisions marked compatible with one revision of a profile (symmetric). Every listed revision must
        /// exist in the profile's tenant. The revision rows are not edited.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="profileId">Profile identifier.</param>
        /// <param name="revisionId">Revision identifier (of that profile).</param>
        /// <param name="request">The compatible revisions.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The revision with its compatibility marks.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the profile or revision does not exist or is not visible.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller is not an admin.</exception>
        /// <exception cref="ArgumentException">Thrown when the list is missing or names an unknown revision.</exception>
        public async Task<ModelProfileRevision> SetCompatibleWithAsync(AuthContext caller, string? profileId, string? revisionId, ModelProfileCompatibilityRequest? request, CancellationToken token = default)
        {
            RequirePerson(caller);
            ModelProfile profile = await ReadVisibleAsync(caller, profileId, token).ConfigureAwait(false);
            string? revisionKey = Blank(revisionId);
            ModelProfileRevision? revision = revisionKey == null ? null : await _Database.ModelProfiles.ReadRevisionAsync(revisionKey, token).ConfigureAwait(false);
            if (revision == null || !String.Equals(revision.ProfileId, profile.Id, StringComparison.Ordinal)) throw new KeyNotFoundException("Model profile revision not found.");
            RequireAdmin(caller);
            if (request == null || request.CompatibleWith == null) throw new ArgumentException("CompatibleWith is required (an empty list removes every mark).");

            List<string> ids = request.CompatibleWith.Select(Blank).Where(id => id != null).Select(id => id!)
                .Where(id => !String.Equals(id, revision.Id, StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            if (ids.Count > 100) throw new ArgumentException("CompatibleWith may list at most 100 revisions.");
            foreach (string id in ids)
            {
                ModelProfileRevision? other = await _Database.ModelProfiles.ReadRevisionAsync(id, token).ConfigureAwait(false);
                if (other == null || !String.Equals(other.TenantId ?? String.Empty, revision.TenantId ?? String.Empty, StringComparison.Ordinal))
                    throw new ArgumentException("Unknown model profile revision: " + id);
            }

            await _Database.ModelProfiles.SetCompatibleWithAsync(revision.Id, ids, token).ConfigureAwait(false);
            _Logging.Info(_Header + "revision " + revision.Id + " marked compatible with " + ids.Count + " revision(s) by " + caller.UserId);
            return (await _Database.ModelProfiles.ReadRevisionAsync(revision.Id, token).ConfigureAwait(false))!;
        }

        #endregion

        #region Private-Methods

        private async Task<ModelProfile> ReadVisibleAsync(AuthContext caller, string? profileId, CancellationToken token)
        {
            string? id = Blank(profileId);
            ModelProfile? profile = id == null ? null : await _Database.ModelProfiles.ReadAsync(id, token).ConfigureAwait(false);
            if (profile == null || !CanSeeTenant(caller, profile.TenantId)) throw new KeyNotFoundException("Model profile not found.");
            return profile;
        }

        private static void RequirePerson(AuthContext caller)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (!caller.IsAuthenticated) throw new UnauthorizedAccessException("Authentication required.");
            if (!caller.IsAdmin && String.IsNullOrEmpty(caller.TenantId)) throw new UnauthorizedAccessException("A tenant is required.");
        }

        private static void RequireAdmin(AuthContext caller)
        {
            if (!String.IsNullOrEmpty(caller.MissionId) || !String.IsNullOrEmpty(caller.AskThreadId))
                throw new UnauthorizedAccessException("A captain session cannot change model profiles.");
            if (!caller.IsAdmin && !caller.IsTenantAdmin) throw new UnauthorizedAccessException("Only an admin can change a model profile.");
        }

        private static bool CanSeeTenant(AuthContext caller, string? tenantId)
        {
            if (caller.IsAdmin) return true;
            return !String.IsNullOrEmpty(tenantId) && String.Equals(caller.TenantId, tenantId, StringComparison.Ordinal);
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
