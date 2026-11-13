namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// The caller-scoped API over workflow profiles, shared by REST and MCP so both enforce the same rules. A global
    /// admin sees every tenant; everyone else sees their own tenant's tenant-wide profiles plus their own user-specific
    /// profiles. Editing a tenant-wide profile needs a tenant admin; a user may edit their own user-specific profiles.
    /// Errors are typed exceptions: <see cref="KeyNotFoundException"/> (missing or outside the caller's scope),
    /// <see cref="ArgumentException"/> (invalid input or a definition that fails validation) and
    /// <see cref="UnauthorizedAccessException"/> (not allowed).
    /// </summary>
    public class WorkflowProfileAccessService
    {
        #region Public-Members

        /// <summary>
        /// Message used when a workflow profile is missing or outside the caller's scope.
        /// </summary>
        public const string NotFoundMessage = "Workflow profile not found";

        /// <summary>
        /// Message used when a vessel is missing or outside the caller's scope.
        /// </summary>
        public const string VesselNotFoundMessage = "Vessel not found";

        /// <summary>
        /// Message used when no workflow profile resolves for a vessel.
        /// </summary>
        public const string NotResolvedMessage = "No workflow profile could be resolved for this vessel";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly WorkflowProfileService _Service;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="service">Workflow profile service (validation and resolution).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public WorkflowProfileAccessService(DatabaseDriver database, WorkflowProfileService service)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Service = service ?? throw new ArgumentNullException(nameof(service));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read one workflow profile visible to the caller.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Workflow profile identifier (wfp_ prefix).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The profile.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the profile is missing or not visible to the caller.</exception>
        public async Task<WorkflowProfile> GetAsync(AuthContext caller, string? id, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrWhiteSpace(id)) throw new KeyNotFoundException(NotFoundMessage);

            WorkflowProfile? profile = await _Database.WorkflowProfiles.ReadAsync(id.Trim(), BuildScopedReadQuery(caller), token).ConfigureAwait(false);
            if (profile == null || !ScopedVisibility.CanView(caller, profile.OwnershipScope, profile.TenantId, profile.UserId))
                throw new KeyNotFoundException(NotFoundMessage);
            return profile;
        }

        /// <summary>
        /// Validate a workflow-profile definition without saving it. A caller other than a global admin validates
        /// against their own tenant.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="profile">Definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Validation result with command previews and available check types.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller or profile is null.</exception>
        /// <exception cref="ArgumentException">Thrown when fleetId or vesselId names a fleet or vessel outside the caller's tenant.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when a list on the profile holds more than <see cref="CollectionLimits.MaxConfigItems"/> entries.</exception>
        public async Task<WorkflowProfileValidationResult> ValidateAsync(AuthContext caller, WorkflowProfile profile, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            CollectionBounds.Validate(profile);
            if (!caller.IsAdmin) profile.TenantId = caller.TenantId;
            await RequireVisibleReferencesAsync(caller, profile, token).ConfigureAwait(false);
            return await _Service.ValidateAsync(profile, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a workflow profile owned by the caller. A global admin may name a tenant; everyone else creates in
        /// their own tenant. Setting IsDefault clears the flag on the other profiles at the same scope.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="profile">Definition.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created profile.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller or profile is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the definition fails validation or references a fleet or vessel outside the caller's tenant.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when a list on the profile holds more than <see cref="CollectionLimits.MaxConfigItems"/> entries.</exception>
        public async Task<WorkflowProfile> CreateAsync(AuthContext caller, WorkflowProfile profile, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            profile.TenantId = caller.IsAdmin ? (NormalizeEmpty(profile.TenantId) ?? caller.TenantId) : caller.TenantId;
            profile.UserId = caller.UserId;
            profile.OwnershipScope = ScopedVisibility.ResolveCreateScope(caller, profile.OwnershipScope);

            await RequireVisibleReferencesAsync(caller, profile, token).ConfigureAwait(false);
            await RequireValidAsync(profile, token).ConfigureAwait(false);
            await EnsureUniqueDefaultAsync(profile, token).ConfigureAwait(false);
            return await _Database.WorkflowProfiles.CreateAsync(profile, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Update a workflow profile the caller may edit. <paramref name="apply"/> changes the stored profile in place;
        /// its second argument is true when the caller may change the ownership scope (global or tenant admin). The
        /// result is validated before it is saved, and an edited profile is no longer replaced by Armada's defaults.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Workflow profile identifier.</param>
        /// <param name="apply">Applies the requested changes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated profile.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller or apply is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the profile is missing or not visible to the caller.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller may not edit the profile.</exception>
        /// <exception cref="ArgumentException">Thrown when the updated definition fails validation or references a fleet or vessel outside the caller's tenant.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when a list on the updated profile holds more than <see cref="CollectionLimits.MaxConfigItems"/> entries.</exception>
        public async Task<WorkflowProfile> UpdateAsync(AuthContext caller, string? id, Action<WorkflowProfile, bool> apply, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (apply == null) throw new ArgumentNullException(nameof(apply));

            WorkflowProfile existing = await GetAsync(caller, id, token).ConfigureAwait(false);
            if (!ScopedVisibility.CanEdit(caller, existing.OwnershipScope, existing.TenantId, existing.UserId))
                throw new UnauthorizedAccessException("You may only modify your own workflow profiles; a tenant-wide profile requires a tenant admin.");

            apply(existing, caller.IsAdmin || caller.IsTenantAdmin);
            // Once a person edits a profile Armada created, it is theirs: Armada never replaces it automatically.
            existing.SourceTemplate = null;
            existing.LastUpdateUtc = DateTime.UtcNow;

            await RequireVisibleReferencesAsync(caller, existing, token).ConfigureAwait(false);
            await RequireValidAsync(existing, token).ConfigureAwait(false);
            await EnsureUniqueDefaultAsync(existing, token).ConfigureAwait(false);
            return await _Database.WorkflowProfiles.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Replace every editable field of <paramref name="existing"/> with the values in <paramref name="incoming"/>
        /// (the REST PUT semantics). Blank commands become null.
        /// </summary>
        /// <param name="existing">Stored profile, changed in place.</param>
        /// <param name="incoming">Requested definition.</param>
        /// <param name="canChangeOwnership">True when the ownership scope may change.</param>
        /// <exception cref="ArgumentNullException">Thrown when existing or incoming is null.</exception>
        public static void ApplyReplacement(WorkflowProfile existing, WorkflowProfile incoming, bool canChangeOwnership)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            existing.Name = incoming.Name;
            existing.Description = incoming.Description;
            existing.Scope = incoming.Scope;
            if (canChangeOwnership) existing.OwnershipScope = incoming.OwnershipScope;
            existing.FleetId = NormalizeEmpty(incoming.FleetId);
            existing.VesselId = NormalizeEmpty(incoming.VesselId);
            existing.IsDefault = incoming.IsDefault;
            existing.Active = incoming.Active;
            existing.LanguageHints = incoming.LanguageHints ?? new List<string>();
            existing.LintCommand = NormalizeEmpty(incoming.LintCommand);
            existing.BuildCommand = NormalizeEmpty(incoming.BuildCommand);
            existing.UnitTestCommand = NormalizeEmpty(incoming.UnitTestCommand);
            existing.AbbreviatedUnitTestCommand = NormalizeEmpty(incoming.AbbreviatedUnitTestCommand);
            existing.IntegrationTestCommand = NormalizeEmpty(incoming.IntegrationTestCommand);
            existing.E2ETestCommand = NormalizeEmpty(incoming.E2ETestCommand);
            existing.MigrationCommand = NormalizeEmpty(incoming.MigrationCommand);
            existing.SecurityScanCommand = NormalizeEmpty(incoming.SecurityScanCommand);
            existing.PerformanceCommand = NormalizeEmpty(incoming.PerformanceCommand);
            existing.PackageCommand = NormalizeEmpty(incoming.PackageCommand);
            existing.DeploymentVerificationCommand = NormalizeEmpty(incoming.DeploymentVerificationCommand);
            existing.RollbackVerificationCommand = NormalizeEmpty(incoming.RollbackVerificationCommand);
            existing.PublishArtifactCommand = NormalizeEmpty(incoming.PublishArtifactCommand);
            existing.ReleaseVersioningCommand = NormalizeEmpty(incoming.ReleaseVersioningCommand);
            existing.ChangelogGenerationCommand = NormalizeEmpty(incoming.ChangelogGenerationCommand);
            existing.RequiredInputs = incoming.RequiredInputs ?? new List<WorkflowInputReference>();
            existing.ExpectedArtifacts = incoming.ExpectedArtifacts ?? new List<string>();
            existing.Environments = incoming.Environments ?? new List<WorkflowEnvironmentProfile>();
        }

        /// <summary>
        /// Delete a workflow profile the caller may edit.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="id">Workflow profile identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The deleted profile.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the profile is missing or not visible to the caller.</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when the caller may not delete the profile.</exception>
        public async Task<WorkflowProfile> DeleteAsync(AuthContext caller, string? id, CancellationToken token = default)
        {
            WorkflowProfile existing = await GetAsync(caller, id, token).ConfigureAwait(false);
            if (!ScopedVisibility.CanEdit(caller, existing.OwnershipScope, existing.TenantId, existing.UserId))
                throw new UnauthorizedAccessException("You may only delete your own workflow profiles; a tenant-wide profile requires a tenant admin.");

            await _Database.WorkflowProfiles.DeleteAsync(existing.Id, BuildScopedReadQuery(caller), token).ConfigureAwait(false);
            return existing;
        }

        /// <summary>
        /// Delete several workflow profiles. Ids that are blank, missing, outside the caller's scope or not editable by
        /// the caller are skipped with a reason instead of failing the batch.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="ids">Workflow profile identifiers.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deleted count and skipped entries.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="ArgumentException">Thrown when ids is null or empty.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when ids holds more than <see cref="CollectionLimits.MaxRequestItems"/> entries.</exception>
        public async Task<DeleteMultipleResult> DeleteMultipleAsync(AuthContext caller, List<string>? ids, CancellationToken token = default)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (ids == null || ids.Count == 0) throw new ArgumentException("Ids is required and must not be empty", nameof(ids));
            CollectionLimits.RequireRequestItems(ids, "Ids");

            DeleteMultipleResult result = new DeleteMultipleResult();
            foreach (string id in ids)
            {
                if (String.IsNullOrWhiteSpace(id))
                {
                    result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                    continue;
                }

                try
                {
                    await DeleteAsync(caller, id, token).ConfigureAwait(false);
                    result.Deleted++;
                }
                catch (KeyNotFoundException)
                {
                    result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                }
                catch (UnauthorizedAccessException)
                {
                    result.Skipped.Add(new DeleteMultipleSkipped(id, "Not permitted"));
                }
            }

            result.ResolveStatus();
            return result;
        }

        /// <summary>
        /// Resolve the workflow profile that applies to a vessel (vessel, then fleet, then global precedence), or the
        /// named profile when <paramref name="workflowProfileId"/> is given.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="workflowProfileId">Optional explicit workflow profile override.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolved profile.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the vessel is not visible or nothing resolves.</exception>
        public async Task<WorkflowProfile> ResolveAsync(AuthContext caller, string? vesselId, string? workflowProfileId, CancellationToken token = default)
        {
            Vessel vessel = await ReadAccessibleVesselAsync(caller, vesselId, token).ConfigureAwait(false);
            WorkflowProfile? profile = await _Service.ResolveForVesselAsync(caller, vessel, NormalizeEmpty(workflowProfileId), token).ConfigureAwait(false);
            if (profile == null) throw new KeyNotFoundException(NotResolvedMessage);
            return profile;
        }

        /// <summary>
        /// Preview the fully resolved command set, available check types and resolution mode for a vessel.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="vesselId">Vessel identifier.</param>
        /// <param name="workflowProfileId">Optional explicit workflow profile override.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The preview.</returns>
        /// <exception cref="ArgumentNullException">Thrown when caller is null.</exception>
        /// <exception cref="KeyNotFoundException">Thrown when the vessel is not visible or nothing resolves.</exception>
        public async Task<WorkflowProfileResolutionPreviewResult> PreviewAsync(AuthContext caller, string? vesselId, string? workflowProfileId, CancellationToken token = default)
        {
            Vessel vessel = await ReadAccessibleVesselAsync(caller, vesselId, token).ConfigureAwait(false);
            WorkflowProfileResolutionPreviewResult? preview = await _Service.PreviewForVesselAsync(caller, vessel, NormalizeEmpty(workflowProfileId), token).ConfigureAwait(false);
            if (preview == null || preview.ResolvedProfile == null) throw new KeyNotFoundException(NotResolvedMessage);
            return preview;
        }

        /// <summary>
        /// Trim a value and turn blank into null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Trimmed value or null.</returns>
        public static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion

        #region Private-Methods

        private static WorkflowProfileQuery BuildScopedReadQuery(AuthContext caller)
        {
            WorkflowProfileQuery query = new WorkflowProfileQuery();
            if (!caller.IsAdmin) query.TenantId = caller.TenantId;
            return query;
        }

        private async Task RequireVisibleReferencesAsync(AuthContext caller, WorkflowProfile profile, CancellationToken token)
        {
            // Another tenant's fleet or vessel is refused exactly like a missing one, so the answer never reveals that it
            // exists. Global admins may reference any tenant; the validator still checks the profile's own tenant.
            if (caller.IsAdmin) return;

            string? fleetId = NormalizeEmpty(profile.FleetId);
            if (fleetId != null)
            {
                Fleet? fleet = await _Database.Fleets.ReadAsync(fleetId, token).ConfigureAwait(false);
                if (fleet == null || !String.Equals(fleet.TenantId, caller.TenantId, StringComparison.Ordinal))
                    throw new ArgumentException("Fleet not found: " + fleetId);
            }

            string? vesselId = NormalizeEmpty(profile.VesselId);
            if (vesselId != null)
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
                if (vessel == null || !String.Equals(vessel.TenantId, caller.TenantId, StringComparison.Ordinal))
                    throw new ArgumentException("Vessel not found: " + vesselId);
            }
        }

        private async Task RequireValidAsync(WorkflowProfile profile, CancellationToken token)
        {
            CollectionBounds.Validate(profile);
            WorkflowProfileValidationResult validation = await _Service.ValidateAsync(profile, token).ConfigureAwait(false);
            if (!validation.IsValid) throw new ArgumentException(String.Join(" ", validation.Errors));
        }

        private async Task EnsureUniqueDefaultAsync(WorkflowProfile profile, CancellationToken token)
        {
            if (!profile.IsDefault) return;

            WorkflowProfileQuery query = new WorkflowProfileQuery
            {
                TenantId = profile.TenantId,
                Scope = profile.Scope,
                PageNumber = 1,
                PageSize = 1000
            };

            if (profile.Scope == WorkflowProfileScopeEnum.Fleet)
                query.FleetId = profile.FleetId;
            if (profile.Scope == WorkflowProfileScopeEnum.Vessel)
                query.VesselId = profile.VesselId;

            List<WorkflowProfile> peers = await _Database.WorkflowProfiles.EnumerateAllAsync(query, token).ConfigureAwait(false);
            foreach (WorkflowProfile peer in peers.Where(item => item.IsDefault && !String.Equals(item.Id, profile.Id, StringComparison.Ordinal)))
            {
                peer.IsDefault = false;
                await _Database.WorkflowProfiles.UpdateAsync(peer, token).ConfigureAwait(false);
            }
        }

        private async Task<Vessel> ReadAccessibleVesselAsync(AuthContext caller, string? vesselId, CancellationToken token)
        {
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            if (String.IsNullOrWhiteSpace(vesselId)) throw new KeyNotFoundException(VesselNotFoundMessage);
            string id = vesselId.Trim();

            Vessel? vessel;
            if (caller.IsAdmin)
                vessel = await _Database.Vessels.ReadAsync(id, token).ConfigureAwait(false);
            else if (caller.IsTenantAdmin)
                vessel = await _Database.Vessels.ReadAsync(caller.TenantId!, id, token).ConfigureAwait(false);
            else
                vessel = await _Database.Vessels.ReadAsync(caller.TenantId!, caller.UserId!, id, token).ConfigureAwait(false);

            if (vessel == null) throw new KeyNotFoundException(VesselNotFoundMessage);
            return vessel;
        }

        #endregion
    }
}
