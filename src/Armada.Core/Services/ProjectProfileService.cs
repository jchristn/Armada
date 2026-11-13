namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Validates and resolves project profiles for vessels. Resolution mirrors the workflow-profile
    /// precedent: an explicit id, else the best Vessel-scoped match, else Fleet-scoped, else Global.
    /// </summary>
    public class ProjectProfileService
    {
        #region Public-Members

        /// <summary>
        /// Most entries ValidateAsync puts in Errors or Warnings: at most one per validation rule.
        /// </summary>
        public const int MaxValidationMessages = 16;

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver. Required.</param>
        /// <param name="logging">Logging module. Required.</param>
        public ProjectProfileService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a project profile: scope/fleet/vessel consistency, referenced entity existence,
        /// and persona-override well-formedness.
        /// </summary>
        /// <param name="profile">The profile to validate.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The validation result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is null.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when PersonaOverrides or Skills holds more than
        /// <see cref="CollectionLimits.MaxConfigItems"/> entries. Every write path (create, update, validate) calls this
        /// method, so an over-limit profile is never stored.</exception>
        public async Task<ProjectProfileValidationResult> ValidateAsync(ProjectProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            CollectionBounds.Validate(profile);

            ProjectProfileValidationResult result = new ProjectProfileValidationResult();

            if (String.IsNullOrWhiteSpace(profile.Name))
                result.Errors.Add("Name is required.");

            switch (profile.Scope)
            {
                case ProjectProfileScopeEnum.Global:
                    break;
                case ProjectProfileScopeEnum.Fleet:
                    if (String.IsNullOrWhiteSpace(profile.FleetId))
                    {
                        result.Errors.Add("Fleet-scoped profiles require a fleetId.");
                    }
                    else
                    {
                        Fleet? fleet = await _Database.Fleets.ReadAsync(profile.FleetId, token).ConfigureAwait(false);
                        if (fleet == null)
                            result.Errors.Add("Fleet not found for fleet-scoped profile.");
                        else if (!String.IsNullOrWhiteSpace(profile.TenantId)
                            && !String.Equals(fleet.TenantId, profile.TenantId, StringComparison.Ordinal))
                            result.Errors.Add("Fleet does not belong to the project profile tenant.");
                    }
                    break;
                case ProjectProfileScopeEnum.Vessel:
                    if (String.IsNullOrWhiteSpace(profile.VesselId))
                    {
                        result.Errors.Add("Vessel-scoped profiles require a vesselId.");
                    }
                    else
                    {
                        Vessel? vessel = await _Database.Vessels.ReadAsync(profile.VesselId, token).ConfigureAwait(false);
                        if (vessel == null)
                            result.Errors.Add("Vessel not found for vessel-scoped profile.");
                        else if (!String.IsNullOrWhiteSpace(profile.TenantId)
                            && !String.Equals(vessel.TenantId, profile.TenantId, StringComparison.Ordinal))
                            result.Errors.Add("Vessel does not belong to the project profile tenant.");
                    }
                    break;
            }

            if (profile.Scope != ProjectProfileScopeEnum.Fleet && !String.IsNullOrWhiteSpace(profile.FleetId))
                result.Warnings.Add("fleetId is set but the profile scope is not Fleet.");
            if (profile.Scope != ProjectProfileScopeEnum.Vessel && !String.IsNullOrWhiteSpace(profile.VesselId))
                result.Warnings.Add("vesselId is set but the profile scope is not Vessel.");

            List<PersonaOverride> overrides = profile.PersonaOverrides ?? new List<PersonaOverride>();
            if (overrides.Any(item => item == null || String.IsNullOrWhiteSpace(item.PersonaName)))
                result.Errors.Add("Persona overrides must each specify a persona name.");
            if (overrides.Where(item => item != null && !String.IsNullOrWhiteSpace(item.PersonaName))
                    .GroupBy(item => item.PersonaName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1))
                result.Errors.Add("Persona overrides must reference each persona at most once.");

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        /// <summary>
        /// Resolve the best matching project profile for a vessel.
        /// </summary>
        /// <param name="auth">Authentication context. Required.</param>
        /// <param name="vessel">The target vessel. Required.</param>
        /// <param name="explicitProfileId">Optional explicit profile id override.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolved profile, or null when none matches.</returns>
        public async Task<ProjectProfile?> ResolveForVesselAsync(
            AuthContext auth,
            Vessel vessel,
            string? explicitProfileId = null,
            CancellationToken token = default)
        {
            ProjectProfileResolutionResult result = await ResolveWithModeForVesselAsync(auth, vessel, explicitProfileId, token).ConfigureAwait(false);
            return result.Profile;
        }

        /// <summary>
        /// Resolve the best matching project profile for a vessel, including the mode of selection.
        /// </summary>
        /// <param name="auth">Authentication context. Required.</param>
        /// <param name="vessel">The target vessel. Required.</param>
        /// <param name="explicitProfileId">Optional explicit profile id override.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolution result.</returns>
        public async Task<ProjectProfileResolutionResult> ResolveWithModeForVesselAsync(
            AuthContext auth,
            Vessel vessel,
            string? explicitProfileId = null,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            ProjectProfileQuery query = new ProjectProfileQuery
            {
                TenantId = auth.IsAdmin ? vessel.TenantId : auth.TenantId,
                UserId = null,
                Active = true,
                PageNumber = 1,
                PageSize = 1000
            };

            if (!String.IsNullOrWhiteSpace(explicitProfileId))
            {
                ProjectProfile? explicitProfile = await _Database.ProjectProfiles.ReadAsync(explicitProfileId, query, token).ConfigureAwait(false);
                return new ProjectProfileResolutionResult
                {
                    Profile = explicitProfile != null && explicitProfile.Active ? explicitProfile : null,
                    Mode = ProjectProfileResolutionModeEnum.Explicit
                };
            }

            List<ProjectProfile> candidates = await _Database.ProjectProfiles.EnumerateAllAsync(query, token).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                return new ProjectProfileResolutionResult
                {
                    Profile = null,
                    Mode = ProjectProfileResolutionModeEnum.None
                };
            }

            ProjectProfile? match = ChooseBestMatch(
                candidates.Where(profile => profile.Scope == ProjectProfileScopeEnum.Vessel
                    && String.Equals(profile.VesselId, vessel.Id, StringComparison.Ordinal)).ToList());
            if (match != null)
            {
                return new ProjectProfileResolutionResult
                {
                    Profile = match,
                    Mode = ProjectProfileResolutionModeEnum.Vessel
                };
            }

            if (!String.IsNullOrWhiteSpace(vessel.FleetId))
            {
                match = ChooseBestMatch(
                    candidates.Where(profile => profile.Scope == ProjectProfileScopeEnum.Fleet
                        && String.Equals(profile.FleetId, vessel.FleetId, StringComparison.Ordinal)).ToList());
                if (match != null)
                {
                    return new ProjectProfileResolutionResult
                    {
                        Profile = match,
                        Mode = ProjectProfileResolutionModeEnum.Fleet
                    };
                }
            }

            match = ChooseBestMatch(candidates.Where(profile => profile.Scope == ProjectProfileScopeEnum.Global).ToList());
            return new ProjectProfileResolutionResult
            {
                Profile = match,
                Mode = match != null ? ProjectProfileResolutionModeEnum.Global : ProjectProfileResolutionModeEnum.None
            };
        }

        /// <summary>
        /// Select the best matching project profile for a vessel from a pre-fetched candidate list,
        /// applying vessel -> fleet -> global precedence. Returns null when nothing matches. This is the
        /// auth-free variant used by the dispatch path, which already holds the vessel and an active
        /// candidate set.
        /// </summary>
        /// <param name="activeCandidates">Active project profiles to select from.</param>
        /// <param name="vessel">The target vessel.</param>
        /// <returns>The best matching profile, or null.</returns>
        public static ProjectProfile? SelectForVessel(List<ProjectProfile> activeCandidates, Vessel vessel)
        {
            if (activeCandidates == null || activeCandidates.Count == 0) return null;
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            ProjectProfile? match = ChooseBestMatch(
                activeCandidates.Where(profile => profile.Scope == ProjectProfileScopeEnum.Vessel
                    && String.Equals(profile.VesselId, vessel.Id, StringComparison.Ordinal)).ToList());
            if (match != null) return match;

            if (!String.IsNullOrWhiteSpace(vessel.FleetId))
            {
                match = ChooseBestMatch(
                    activeCandidates.Where(profile => profile.Scope == ProjectProfileScopeEnum.Fleet
                        && String.Equals(profile.FleetId, vessel.FleetId, StringComparison.Ordinal)).ToList());
                if (match != null) return match;
            }

            return ChooseBestMatch(activeCandidates.Where(profile => profile.Scope == ProjectProfileScopeEnum.Global).ToList());
        }

        /// <summary>
        /// Resolve the enabled persona override for a persona name within a profile, or null when the
        /// profile is null or has no enabled override for that persona.
        /// </summary>
        /// <param name="profile">The resolved project profile, or null.</param>
        /// <param name="personaName">The persona name to look up.</param>
        /// <returns>The matching enabled override, or null.</returns>
        public static PersonaOverride? ResolvePersonaOverride(ProjectProfile? profile, string? personaName)
        {
            if (profile == null || String.IsNullOrWhiteSpace(personaName)) return null;
            if (profile.PersonaOverrides == null) return null;

            return profile.PersonaOverrides.FirstOrDefault(item =>
                item != null
                && item.Enabled
                && !String.IsNullOrWhiteSpace(item.PersonaName)
                && String.Equals(item.PersonaName.Trim(), personaName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Build a before/after preview of a persona's prompt for a project profile: the base built-in
        /// prompt and the effective prompt after the profile's override is applied.
        /// </summary>
        /// <param name="profile">The resolved project profile, or null for a base-only preview.</param>
        /// <param name="personaName">The persona name to preview.</param>
        /// <param name="promptTemplates">Prompt-template service for rendering. Required.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The persona prompt preview.</returns>
        public static async Task<PersonaPromptPreview> BuildPersonaPreviewAsync(
            ProjectProfile? profile,
            string personaName,
            IPromptTemplateService promptTemplates,
            CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(personaName)) throw new ArgumentNullException(nameof(personaName));
            if (promptTemplates == null) throw new ArgumentNullException(nameof(promptTemplates));

            Dictionary<string, string> emptyParams = new Dictionary<string, string>();
            PersonaOverride? personaOverride = ResolvePersonaOverride(profile, personaName);

            string baseTemplateName = MissionPromptBuilder.GetPersonaTemplateName(personaName);
            string basePrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                personaName, emptyParams, promptTemplates, null, token).ConfigureAwait(false);
            string effectivePrompt = await MissionPromptBuilder.ResolvePersonaPromptAsync(
                personaName, new Dictionary<string, string>(), promptTemplates, personaOverride, token).ConfigureAwait(false);

            string effectiveTemplateName = baseTemplateName;
            if (personaOverride != null && !String.IsNullOrWhiteSpace(personaOverride.PromptTemplateName))
                effectiveTemplateName = personaOverride.PromptTemplateName!.Trim();

            return new PersonaPromptPreview
            {
                PersonaName = personaName,
                BaseTemplateName = baseTemplateName,
                EffectiveTemplateName = effectiveTemplateName,
                BasePrompt = basePrompt,
                EffectivePrompt = effectivePrompt,
                AdditionalInstructions = personaOverride?.AdditionalInstructions,
                IsOverridden = personaOverride != null
            };
        }

        #endregion

        #region Private-Methods

        private static ProjectProfile? ChooseBestMatch(List<ProjectProfile> profiles)
        {
            return profiles
                .Where(profile => profile.Active)
                .OrderByDescending(profile => profile.IsDefault)
                .ThenByDescending(profile => profile.LastUpdateUtc)
                .FirstOrDefault();
        }

        #endregion
    }
}
