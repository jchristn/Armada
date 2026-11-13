namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Validates and resolves workflow profiles for vessels and fleets.
    /// </summary>
    public class WorkflowProfileService
    {
        /// <summary>
        /// Most entries ValidateAsync puts in Errors or Warnings: at most one per validation rule.
        /// </summary>
        public const int MaxValidationMessages = 16;

        /// <summary>
        /// Most entries in AvailableCheckTypes: the 15 profile-level command types plus the 6 environment command types.
        /// </summary>
        public const int MaxAvailableCheckTypes = 21;

        /// <summary>
        /// Most command previews BuildCommandPreviews returns: 15 profile-level commands plus 6 per environment, with
        /// Environments limited to <see cref="CollectionLimits.MaxConfigItems"/> on every write path.
        /// </summary>
        public const int MaxCommandPreviews = 15 + 6 * CollectionLimits.MaxConfigItems;

        private readonly string _Header = "[WorkflowProfileService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public WorkflowProfileService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Validate a workflow profile and return preview information.
        /// </summary>
        public async Task<WorkflowProfileValidationResult> ValidateAsync(WorkflowProfile profile, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            WorkflowProfileValidationResult result = new WorkflowProfileValidationResult();

            if (String.IsNullOrWhiteSpace(profile.Name))
                result.Errors.Add("Name is required.");

            switch (profile.Scope)
            {
                case WorkflowProfileScopeEnum.Global:
                    break;
                case WorkflowProfileScopeEnum.Fleet:
                    if (String.IsNullOrWhiteSpace(profile.FleetId))
                    {
                        result.Errors.Add("Fleet-scoped profiles require a fleetId.");
                    }
                    else
                    {
                        Fleet? fleet = await _Database.Fleets.ReadAsync(profile.FleetId, token).ConfigureAwait(false);
                        if (fleet == null)
                        {
                            result.Errors.Add("Fleet not found for fleet-scoped profile.");
                        }
                        else if (!String.IsNullOrWhiteSpace(profile.TenantId)
                            && !String.Equals(fleet.TenantId, profile.TenantId, StringComparison.Ordinal))
                        {
                            result.Errors.Add("Fleet does not belong to the workflow profile tenant.");
                        }
                    }
                    break;
                case WorkflowProfileScopeEnum.Vessel:
                    if (String.IsNullOrWhiteSpace(profile.VesselId))
                    {
                        result.Errors.Add("Vessel-scoped profiles require a vesselId.");
                    }
                    else
                    {
                        Vessel? vessel = await _Database.Vessels.ReadAsync(profile.VesselId, token).ConfigureAwait(false);
                        if (vessel == null)
                        {
                            result.Errors.Add("Vessel not found for vessel-scoped profile.");
                        }
                        else if (!String.IsNullOrWhiteSpace(profile.TenantId)
                            && !String.Equals(vessel.TenantId, profile.TenantId, StringComparison.Ordinal))
                        {
                            result.Errors.Add("Vessel does not belong to the workflow profile tenant.");
                        }
                    }
                    break;
            }

            if (profile.Scope != WorkflowProfileScopeEnum.Fleet && !String.IsNullOrWhiteSpace(profile.FleetId))
                result.Warnings.Add("fleetId is set but the profile scope is not Fleet.");
            if (profile.Scope != WorkflowProfileScopeEnum.Vessel && !String.IsNullOrWhiteSpace(profile.VesselId))
                result.Warnings.Add("vesselId is set but the profile scope is not Vessel.");

            List<string> availableTypes = GetAvailableCheckTypeNames(profile);
            result.AvailableCheckTypes = availableTypes;
            result.CommandPreviews = BuildCommandPreviews(profile);

            if (availableTypes.Count == 0)
                result.Errors.Add("At least one build, test, release, deploy, verification, migration, security, or performance command is required.");

            if (profile.Environments.GroupBy(env => env.EnvironmentName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                result.Errors.Add("Environment names must be unique within a workflow profile.");

            List<WorkflowInputReference> inputs = profile.RequiredInputs ?? new List<WorkflowInputReference>();
            if (inputs.Any(input => String.IsNullOrWhiteSpace(input.Key)))
                result.Errors.Add("Required input references must include a key or path.");
            if (inputs.GroupBy(input => WorkflowInputReference.Serialize(input), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                result.Errors.Add("Required input references must be unique within a workflow profile.");
            List<string> knownEnvironmentNames = profile.Environments
                .Where(environment => !String.IsNullOrWhiteSpace(environment.EnvironmentName))
                .Select(environment => environment.EnvironmentName.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            List<string> unknownInputEnvironments = inputs
                .Where(input => !String.IsNullOrWhiteSpace(input.EnvironmentName))
                .Select(input => input.EnvironmentName!.Trim())
                .Where(environmentName => !knownEnvironmentNames.Contains(environmentName, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (unknownInputEnvironments.Count > 0)
            {
                result.Errors.Add("Required input references are scoped to unknown environments: " + String.Join(", ", unknownInputEnvironments) + ".");
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        /// <summary>
        /// Resolve the best matching workflow profile for a vessel.
        /// </summary>
        public async Task<WorkflowProfile?> ResolveForVesselAsync(
            AuthContext auth,
            Vessel vessel,
            string? explicitProfileId = null,
            CancellationToken token = default)
        {
            WorkflowProfileResolutionResult result = await ResolveWithModeForVesselAsync(auth, vessel, explicitProfileId, token).ConfigureAwait(false);
            return result.Profile;
        }

        /// <summary>
        /// Build a fully resolved preview for a target vessel.
        /// </summary>
        public async Task<WorkflowProfileResolutionPreviewResult?> PreviewForVesselAsync(
            AuthContext auth,
            Vessel vessel,
            string? explicitProfileId = null,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            WorkflowProfileResolutionResult resolved = await ResolveWithModeForVesselAsync(auth, vessel, explicitProfileId, token).ConfigureAwait(false);
            if (resolved.Profile == null)
                return null;

            return new WorkflowProfileResolutionPreviewResult
            {
                ResolvedProfile = resolved.Profile,
                ResolutionMode = resolved.Mode,
                AvailableCheckTypes = GetAvailableCheckTypeNames(resolved.Profile),
                CommandPreviews = BuildCommandPreviews(resolved.Profile)
            };
        }

        /// <summary>
        /// Build fully resolved command previews for a workflow profile.
        /// </summary>
        public static List<WorkflowProfileCommandPreview> BuildCommandPreviews(WorkflowProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            List<WorkflowProfileCommandPreview> results = new List<WorkflowProfileCommandPreview>();
            AddPreview(results, CheckRunTypeEnum.Lint, profile.LintCommand);
            AddPreview(results, CheckRunTypeEnum.Build, profile.BuildCommand);
            AddPreview(results, CheckRunTypeEnum.UnitTest, profile.UnitTestCommand);
            AddPreview(results, CheckRunTypeEnum.AbbreviatedUnitTest, profile.AbbreviatedUnitTestCommand);
            AddPreview(results, CheckRunTypeEnum.IntegrationTest, profile.IntegrationTestCommand);
            AddPreview(results, CheckRunTypeEnum.E2ETest, profile.E2ETestCommand);
            AddPreview(results, CheckRunTypeEnum.Migration, profile.MigrationCommand);
            AddPreview(results, CheckRunTypeEnum.SecurityScan, profile.SecurityScanCommand);
            AddPreview(results, CheckRunTypeEnum.Performance, profile.PerformanceCommand);
            AddPreview(results, CheckRunTypeEnum.Package, profile.PackageCommand);
            AddPreview(results, CheckRunTypeEnum.DeploymentVerification, profile.DeploymentVerificationCommand);
            AddPreview(results, CheckRunTypeEnum.RollbackVerification, profile.RollbackVerificationCommand);
            AddPreview(results, CheckRunTypeEnum.PublishArtifact, profile.PublishArtifactCommand);
            AddPreview(results, CheckRunTypeEnum.ReleaseVersioning, profile.ReleaseVersioningCommand);
            AddPreview(results, CheckRunTypeEnum.Changelog, profile.ChangelogGenerationCommand);

            foreach (WorkflowEnvironmentProfile environment in profile.Environments ?? new List<WorkflowEnvironmentProfile>())
            {
                string? environmentName = NullIfWhiteSpace(environment.EnvironmentName);
                AddPreview(results, CheckRunTypeEnum.Deploy, environment.DeployCommand, environmentName);
                AddPreview(results, CheckRunTypeEnum.Rollback, environment.RollbackCommand, environmentName);
                AddPreview(results, CheckRunTypeEnum.SmokeTest, environment.SmokeTestCommand, environmentName);
                AddPreview(results, CheckRunTypeEnum.HealthCheck, environment.HealthCheckCommand, environmentName);
                AddPreview(results, CheckRunTypeEnum.DeploymentVerification, environment.DeploymentVerificationCommand, environmentName);
                AddPreview(results, CheckRunTypeEnum.RollbackVerification, environment.RollbackVerificationCommand, environmentName);
            }

            return results;
        }

        /// <summary>
        /// Resolve the command for a specific check type.
        /// </summary>
        public string? ResolveCommand(WorkflowProfile profile, CheckRunTypeEnum type, string? environmentName = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            switch (type)
            {
                case CheckRunTypeEnum.Lint:
                    return NullIfWhiteSpace(profile.LintCommand);
                case CheckRunTypeEnum.Build:
                    return NullIfWhiteSpace(profile.BuildCommand);
                case CheckRunTypeEnum.UnitTest:
                    return NullIfWhiteSpace(profile.UnitTestCommand);
                case CheckRunTypeEnum.AbbreviatedUnitTest:
                    return UnitTestScopeDirective.SelectCommand(profile, UnitTestScopeEnum.Abbreviated);
                case CheckRunTypeEnum.IntegrationTest:
                    return NullIfWhiteSpace(profile.IntegrationTestCommand);
                case CheckRunTypeEnum.E2ETest:
                    return NullIfWhiteSpace(profile.E2ETestCommand);
                case CheckRunTypeEnum.Migration:
                    return NullIfWhiteSpace(profile.MigrationCommand);
                case CheckRunTypeEnum.SecurityScan:
                    return NullIfWhiteSpace(profile.SecurityScanCommand);
                case CheckRunTypeEnum.Performance:
                    return NullIfWhiteSpace(profile.PerformanceCommand);
                case CheckRunTypeEnum.Package:
                    return NullIfWhiteSpace(profile.PackageCommand);
                case CheckRunTypeEnum.DeploymentVerification:
                    return ResolveEnvironmentCommand(profile, environmentName, env => env.DeploymentVerificationCommand)
                        ?? NullIfWhiteSpace(profile.DeploymentVerificationCommand);
                case CheckRunTypeEnum.RollbackVerification:
                    return ResolveEnvironmentCommand(profile, environmentName, env => env.RollbackVerificationCommand)
                        ?? NullIfWhiteSpace(profile.RollbackVerificationCommand);
                case CheckRunTypeEnum.PublishArtifact:
                    return NullIfWhiteSpace(profile.PublishArtifactCommand);
                case CheckRunTypeEnum.ReleaseVersioning:
                    return NullIfWhiteSpace(profile.ReleaseVersioningCommand);
                case CheckRunTypeEnum.Changelog:
                    return NullIfWhiteSpace(profile.ChangelogGenerationCommand);
                case CheckRunTypeEnum.Deploy:
                    return ResolveEnvironmentCommand(profile, environmentName, env => env.DeployCommand);
                case CheckRunTypeEnum.Rollback:
                    return ResolveEnvironmentCommand(profile, environmentName, env => env.RollbackCommand);
                case CheckRunTypeEnum.SmokeTest:
                    return ResolveEnvironmentCommand(profile, environmentName, env => env.SmokeTestCommand);
                case CheckRunTypeEnum.HealthCheck:
                    return ResolveEnvironmentCommand(profile, environmentName, env => env.HealthCheckCommand);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Get available check type names for a profile.
        /// </summary>
        public static List<string> GetAvailableCheckTypeNames(WorkflowProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            List<string> results = new List<string>();

            AddIfPresent(results, CheckRunTypeEnum.Lint, profile.LintCommand);
            AddIfPresent(results, CheckRunTypeEnum.Build, profile.BuildCommand);
            AddIfPresent(results, CheckRunTypeEnum.UnitTest, profile.UnitTestCommand);
            AddIfPresent(results, CheckRunTypeEnum.AbbreviatedUnitTest, profile.AbbreviatedUnitTestCommand);
            AddIfPresent(results, CheckRunTypeEnum.IntegrationTest, profile.IntegrationTestCommand);
            AddIfPresent(results, CheckRunTypeEnum.E2ETest, profile.E2ETestCommand);
            AddIfPresent(results, CheckRunTypeEnum.Migration, profile.MigrationCommand);
            AddIfPresent(results, CheckRunTypeEnum.SecurityScan, profile.SecurityScanCommand);
            AddIfPresent(results, CheckRunTypeEnum.Performance, profile.PerformanceCommand);
            AddIfPresent(results, CheckRunTypeEnum.Package, profile.PackageCommand);
            AddIfPresent(results, CheckRunTypeEnum.DeploymentVerification, profile.DeploymentVerificationCommand);
            AddIfPresent(results, CheckRunTypeEnum.RollbackVerification, profile.RollbackVerificationCommand);
            AddIfPresent(results, CheckRunTypeEnum.PublishArtifact, profile.PublishArtifactCommand);
            AddIfPresent(results, CheckRunTypeEnum.ReleaseVersioning, profile.ReleaseVersioningCommand);
            AddIfPresent(results, CheckRunTypeEnum.Changelog, profile.ChangelogGenerationCommand);

            if (profile.Environments.Any(env => !String.IsNullOrWhiteSpace(env.DeployCommand)))
                results.Add(CheckRunTypeEnum.Deploy.ToString());
            if (profile.Environments.Any(env => !String.IsNullOrWhiteSpace(env.RollbackCommand)))
                results.Add(CheckRunTypeEnum.Rollback.ToString());
            if (profile.Environments.Any(env => !String.IsNullOrWhiteSpace(env.SmokeTestCommand)))
                results.Add(CheckRunTypeEnum.SmokeTest.ToString());
            if (profile.Environments.Any(env => !String.IsNullOrWhiteSpace(env.HealthCheckCommand)))
                results.Add(CheckRunTypeEnum.HealthCheck.ToString());
            if (profile.Environments.Any(env => !String.IsNullOrWhiteSpace(env.DeploymentVerificationCommand)))
                results.Add(CheckRunTypeEnum.DeploymentVerification.ToString());
            if (profile.Environments.Any(env => !String.IsNullOrWhiteSpace(env.RollbackVerificationCommand)))
                results.Add(CheckRunTypeEnum.RollbackVerification.ToString());

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddIfPresent(List<string> values, CheckRunTypeEnum type, string? command)
        {
            if (!String.IsNullOrWhiteSpace(command))
                values.Add(type.ToString());
        }

        private async Task<WorkflowProfileResolutionResult> ResolveWithModeForVesselAsync(
            AuthContext auth,
            Vessel vessel,
            string? explicitProfileId,
            CancellationToken token)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            WorkflowProfileQuery query = new WorkflowProfileQuery
            {
                TenantId = auth.IsAdmin ? vessel.TenantId : auth.TenantId,
                UserId = null,
                Active = true,
                PageNumber = 1,
                PageSize = 1000
            };

            if (!String.IsNullOrWhiteSpace(explicitProfileId))
            {
                WorkflowProfile? explicitProfile = await _Database.WorkflowProfiles.ReadAsync(explicitProfileId, query, token).ConfigureAwait(false);
                if (explicitProfile != null && explicitProfile.Active)
                {
                    return new WorkflowProfileResolutionResult
                    {
                        Profile = explicitProfile,
                        Mode = WorkflowProfileResolutionModeEnum.Explicit
                    };
                }

                return new WorkflowProfileResolutionResult
                {
                    Profile = null,
                    Mode = WorkflowProfileResolutionModeEnum.Explicit
                };
            }

            List<WorkflowProfile> candidates = await _Database.WorkflowProfiles.EnumerateAllAsync(query, token).ConfigureAwait(false);
            if (candidates.Count == 0)
            {
                return new WorkflowProfileResolutionResult
                {
                    Profile = null,
                    Mode = WorkflowProfileResolutionModeEnum.Global
                };
            }

            WorkflowProfile? match = ChooseBestMatch(
                candidates.Where(profile => profile.Scope == WorkflowProfileScopeEnum.Vessel
                    && String.Equals(profile.VesselId, vessel.Id, StringComparison.Ordinal)).ToList());
            if (match != null)
            {
                return new WorkflowProfileResolutionResult
                {
                    Profile = match,
                    Mode = WorkflowProfileResolutionModeEnum.Vessel
                };
            }

            if (!String.IsNullOrWhiteSpace(vessel.FleetId))
            {
                match = ChooseBestMatch(
                    candidates.Where(profile => profile.Scope == WorkflowProfileScopeEnum.Fleet
                        && String.Equals(profile.FleetId, vessel.FleetId, StringComparison.Ordinal)).ToList());
                if (match != null)
                {
                    return new WorkflowProfileResolutionResult
                    {
                        Profile = match,
                        Mode = WorkflowProfileResolutionModeEnum.Fleet
                    };
                }
            }

            match = ChooseBestMatch(candidates.Where(profile => profile.Scope == WorkflowProfileScopeEnum.Global).ToList());
            return new WorkflowProfileResolutionResult
            {
                Profile = match,
                Mode = WorkflowProfileResolutionModeEnum.Global
            };
        }

        private static void AddPreview(List<WorkflowProfileCommandPreview> values, CheckRunTypeEnum type, string? command, string? environmentName = null)
        {
            string? normalized = NullIfWhiteSpace(command);
            if (normalized == null)
                return;

            values.Add(new WorkflowProfileCommandPreview
            {
                CheckType = type,
                EnvironmentName = NullIfWhiteSpace(environmentName),
                Command = normalized
            });
        }

        private static WorkflowProfile? ChooseBestMatch(List<WorkflowProfile> profiles)
        {
            return profiles
                .Where(profile => profile.Active)
                .OrderByDescending(profile => profile.IsDefault)
                .ThenByDescending(profile => profile.LastUpdateUtc)
                .FirstOrDefault();
        }

        private string? ResolveEnvironmentCommand(
            WorkflowProfile profile,
            string? environmentName,
            Func<WorkflowEnvironmentProfile, string?> selector)
        {
            if (profile.Environments == null || profile.Environments.Count == 0)
                return null;

            WorkflowEnvironmentProfile? environment = null;
            if (!String.IsNullOrWhiteSpace(environmentName))
            {
                environment = profile.Environments.FirstOrDefault(env =>
                    String.Equals(env.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase));
            }

            environment ??= profile.Environments.Count == 1 ? profile.Environments[0] : null;
            if (environment == null)
            {
                _Logging.Debug(_Header + "environment command could not be resolved because no unique environment matched");
                return null;
            }

            return NullIfWhiteSpace(selector(environment));
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
