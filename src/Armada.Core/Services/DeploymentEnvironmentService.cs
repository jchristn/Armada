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
    /// Creates, updates, enumerates, and validates first-class deployment environments.
    /// </summary>
    public class DeploymentEnvironmentService
    {
        private readonly string _Header = "[DeploymentEnvironmentService] ";
        private readonly DatabaseDriver _Database;
        private readonly WorkflowProfileService _WorkflowProfiles;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DeploymentEnvironmentService(DatabaseDriver database, WorkflowProfileService workflowProfiles, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _WorkflowProfiles = workflowProfiles ?? throw new ArgumentNullException(nameof(workflowProfiles));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <summary>
        /// Ensure default deployment-environment records exist for all vessels on startup.
        /// Missing records are seeded from workflow-profile environments when available,
        /// otherwise a fallback Development environment is created.
        /// </summary>
        public async Task SeedDefaultsAsync(CancellationToken token = default)
        {
            List<Vessel> vessels = await _Database.Vessels.EnumerateAsync(token).ConfigureAwait(false);
            foreach (Vessel vessel in vessels)
            {
                await SeedDefaultsForVesselAsync(vessel, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Enumerate deployment environments within the caller scope.
        /// </summary>
        public async Task<EnumerationResult<DeploymentEnvironment>> EnumerateAsync(
            AuthContext auth,
            DeploymentEnvironmentQuery query,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));

            DeploymentEnvironmentQuery scopedQuery = query ?? new DeploymentEnvironmentQuery();
            ApplyScope(auth, scopedQuery);
            return await _Database.Environments.EnumerateAsync(scopedQuery, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Read one deployment environment within the caller scope.
        /// </summary>
        public async Task<DeploymentEnvironment?> ReadAsync(
            AuthContext auth,
            string id,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            return await _Database.Environments.ReadAsync(id, BuildScopeQuery(auth), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Create a deployment environment.
        /// </summary>
        public async Task<DeploymentEnvironment> CreateAsync(
            AuthContext auth,
            DeploymentEnvironmentUpsertRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (request == null) throw new ArgumentNullException(nameof(request));

            Vessel vessel = await ResolveAccessibleVesselAsync(auth, request.VesselId, token).ConfigureAwait(false);
            DeploymentEnvironment environment = new DeploymentEnvironment
            {
                TenantId = vessel.TenantId,
                UserId = auth.UserId,
                VesselId = vessel.Id,
                Name = NormalizeRequired(request.Name, nameof(request.Name)),
                Description = Normalize(request.Description),
                Kind = request.Kind ?? EnvironmentKindEnum.Development,
                ConfigurationSource = Normalize(request.ConfigurationSource),
                BaseUrl = Normalize(request.BaseUrl),
                HealthEndpoint = Normalize(request.HealthEndpoint),
                AccessNotes = Normalize(request.AccessNotes),
                DeploymentRules = Normalize(request.DeploymentRules),
                VerificationDefinitions = NormalizeVerificationDefinitions(request.VerificationDefinitions),
                RolloutMonitoringWindowMinutes = NormalizeMonitoringWindowMinutes(request.RolloutMonitoringWindowMinutes),
                RolloutMonitoringIntervalSeconds = NormalizeMonitoringIntervalSeconds(request.RolloutMonitoringIntervalSeconds),
                AlertOnRegression = request.AlertOnRegression ?? true,
                RequiresApproval = request.RequiresApproval ?? false,
                IsDefault = request.IsDefault ?? false,
                Active = request.Active ?? true,
                CreatedUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow
            };

            environment = await _Database.Environments.CreateAsync(environment, token).ConfigureAwait(false);
            if (environment.IsDefault)
                await ClearOtherDefaultsAsync(auth, environment.VesselId!, environment.Id, token).ConfigureAwait(false);

            _Logging.Info(_Header + "created environment " + environment.Id + " for vessel " + environment.VesselId);
            return environment;
        }

        /// <summary>
        /// Update a deployment environment.
        /// </summary>
        public async Task<DeploymentEnvironment> UpdateAsync(
            AuthContext auth,
            string id,
            DeploymentEnvironmentUpsertRequest request,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (request == null) throw new ArgumentNullException(nameof(request));

            DeploymentEnvironment environment = await ReadAsync(auth, id, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Environment not found.");

            string vesselId = Normalize(request.VesselId) ?? environment.VesselId
                ?? throw new InvalidOperationException("Environment must belong to a vessel.");
            Vessel vessel = await ResolveAccessibleVesselAsync(auth, vesselId, token).ConfigureAwait(false);

            environment.VesselId = vessel.Id;
            environment.Name = NormalizeRequired(request.Name ?? environment.Name, nameof(request.Name));
            environment.Description = Normalize(request.Description);
            environment.Kind = request.Kind ?? environment.Kind;
            environment.ConfigurationSource = Normalize(request.ConfigurationSource);
            environment.BaseUrl = Normalize(request.BaseUrl);
            environment.HealthEndpoint = Normalize(request.HealthEndpoint);
            environment.AccessNotes = Normalize(request.AccessNotes);
            environment.DeploymentRules = Normalize(request.DeploymentRules);
            environment.VerificationDefinitions = request.VerificationDefinitions != null
                ? NormalizeVerificationDefinitions(request.VerificationDefinitions)
                : environment.VerificationDefinitions;
            environment.RolloutMonitoringWindowMinutes = request.RolloutMonitoringWindowMinutes.HasValue
                ? NormalizeMonitoringWindowMinutes(request.RolloutMonitoringWindowMinutes)
                : environment.RolloutMonitoringWindowMinutes;
            environment.RolloutMonitoringIntervalSeconds = request.RolloutMonitoringIntervalSeconds.HasValue
                ? NormalizeMonitoringIntervalSeconds(request.RolloutMonitoringIntervalSeconds)
                : environment.RolloutMonitoringIntervalSeconds;
            environment.AlertOnRegression = request.AlertOnRegression ?? environment.AlertOnRegression;
            environment.RequiresApproval = request.RequiresApproval ?? environment.RequiresApproval;
            environment.IsDefault = request.IsDefault ?? environment.IsDefault;
            environment.Active = request.Active ?? environment.Active;
            environment.LastUpdateUtc = DateTime.UtcNow;

            environment = await _Database.Environments.UpdateAsync(environment, token).ConfigureAwait(false);
            if (environment.IsDefault)
                await ClearOtherDefaultsAsync(auth, environment.VesselId!, environment.Id, token).ConfigureAwait(false);

            _Logging.Info(_Header + "updated environment " + environment.Id);
            return environment;
        }

        /// <summary>
        /// Delete one deployment environment.
        /// </summary>
        public async Task DeleteAsync(
            AuthContext auth,
            string id,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            DeploymentEnvironment? existing = await ReadAsync(auth, id, token).ConfigureAwait(false);
            if (existing == null)
                throw new InvalidOperationException("Environment not found.");

            await _Database.Environments.DeleteAsync(id, BuildScopeQuery(auth), token).ConfigureAwait(false);
            _Logging.Info(_Header + "deleted environment " + id);
        }

        private async Task SeedDefaultsForVesselAsync(Vessel vessel, CancellationToken token)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (String.IsNullOrWhiteSpace(vessel.Id)) return;

            DeploymentEnvironmentQuery query = new DeploymentEnvironmentQuery
            {
                VesselId = vessel.Id
            };

            List<DeploymentEnvironment> environments = await _Database.Environments.EnumerateAllAsync(query, token).ConfigureAwait(false);
            Dictionary<string, DeploymentEnvironment> byName = new Dictionary<string, DeploymentEnvironment>(StringComparer.OrdinalIgnoreCase);
            foreach (DeploymentEnvironment existing in environments)
            {
                if (!String.IsNullOrWhiteSpace(existing.Name) && !byName.ContainsKey(existing.Name))
                    byName[existing.Name] = existing;
            }

            AuthContext seedAuth = BuildSeedAuth(vessel);
            WorkflowProfile? profile = await _WorkflowProfiles.ResolveForVesselAsync(seedAuth, vessel, null, token).ConfigureAwait(false);
            foreach (WorkflowEnvironmentProfile environmentProfile in profile?.Environments ?? new List<WorkflowEnvironmentProfile>())
            {
                string? environmentName = Normalize(environmentProfile.EnvironmentName);
                if (String.IsNullOrWhiteSpace(environmentName) || byName.ContainsKey(environmentName))
                    continue;

                EnvironmentKindEnum kind = InferKind(environmentName);
                DeploymentEnvironment created = new DeploymentEnvironment
                {
                    TenantId = vessel.TenantId,
                    UserId = vessel.UserId,
                    VesselId = vessel.Id,
                    Name = environmentName,
                    Description = "Auto-created from workflow profile environment definition.",
                    Kind = kind,
                    ConfigurationSource = "Startup seed from workflow profile " + profile!.Name,
                    DeploymentRules = kind == EnvironmentKindEnum.Production
                        ? "Seeded production environment. Approval required by default."
                        : "Seeded from workflow profile environment definition.",
                    VerificationDefinitions = new List<DeploymentVerificationDefinition>(),
                    RolloutMonitoringWindowMinutes = 0,
                    RolloutMonitoringIntervalSeconds = 300,
                    AlertOnRegression = true,
                    RequiresApproval = kind == EnvironmentKindEnum.Production,
                    IsDefault = false,
                    Active = true,
                    CreatedUtc = DateTime.UtcNow,
                    LastUpdateUtc = DateTime.UtcNow
                };

                created = await _Database.Environments.CreateAsync(created, token).ConfigureAwait(false);
                environments.Add(created);
                byName[created.Name] = created;
                _Logging.Debug(_Header + "seeded environment " + created.Name + " for vessel " + vessel.Id);
            }

            if (environments.Count == 0)
            {
                DeploymentEnvironment created = new DeploymentEnvironment
                {
                    TenantId = vessel.TenantId,
                    UserId = vessel.UserId,
                    VesselId = vessel.Id,
                    Name = "Development",
                    Description = "Auto-created default deployment environment.",
                    Kind = EnvironmentKindEnum.Development,
                    ConfigurationSource = "Startup seed default",
                    DeploymentRules = "Default startup-created environment.",
                    VerificationDefinitions = new List<DeploymentVerificationDefinition>(),
                    RolloutMonitoringWindowMinutes = 0,
                    RolloutMonitoringIntervalSeconds = 300,
                    AlertOnRegression = true,
                    RequiresApproval = false,
                    IsDefault = true,
                    Active = true,
                    CreatedUtc = DateTime.UtcNow,
                    LastUpdateUtc = DateTime.UtcNow
                };

                await _Database.Environments.CreateAsync(created, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "seeded fallback default environment for vessel " + vessel.Id);
                return;
            }

            if (!environments.Any(environment => environment.IsDefault))
            {
                DeploymentEnvironment defaultEnvironment = ChooseDefaultEnvironment(environments);
                defaultEnvironment.IsDefault = true;
                defaultEnvironment.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Environments.UpdateAsync(defaultEnvironment, token).ConfigureAwait(false);
                _Logging.Debug(_Header + "seeded default environment for vessel " + vessel.Id + " -> " + defaultEnvironment.Name);
            }
        }

        private async Task<Vessel> ResolveAccessibleVesselAsync(AuthContext auth, string? vesselId, CancellationToken token)
        {
            string normalizedVesselId = NormalizeRequired(vesselId, nameof(vesselId));
            Vessel? vessel;
            if (auth.IsAdmin)
                vessel = await _Database.Vessels.ReadAsync(normalizedVesselId, token).ConfigureAwait(false);
            else if (auth.IsTenantAdmin)
                vessel = await _Database.Vessels.ReadAsync(auth.TenantId!, normalizedVesselId, token).ConfigureAwait(false);
            else
                vessel = await _Database.Vessels.ReadAsync(auth.TenantId!, auth.UserId!, normalizedVesselId, token).ConfigureAwait(false);

            return vessel ?? throw new InvalidOperationException("Vessel not found or not accessible.");
        }

        private async Task ClearOtherDefaultsAsync(
            AuthContext auth,
            string vesselId,
            string keepId,
            CancellationToken token)
        {
            DeploymentEnvironmentQuery query = BuildScopeQuery(auth);
            query.VesselId = vesselId;
            List<DeploymentEnvironment> environments = await _Database.Environments.EnumerateAllAsync(query, token).ConfigureAwait(false);
            foreach (DeploymentEnvironment environment in environments.Where(item => item.IsDefault && !String.Equals(item.Id, keepId, StringComparison.Ordinal)))
            {
                environment.IsDefault = false;
                environment.LastUpdateUtc = DateTime.UtcNow;
                await _Database.Environments.UpdateAsync(environment, token).ConfigureAwait(false);
            }
        }

        private static DeploymentEnvironmentQuery BuildScopeQuery(AuthContext auth)
        {
            DeploymentEnvironmentQuery query = new DeploymentEnvironmentQuery();
            ApplyScope(auth, query);
            return query;
        }

        private static void ApplyScope(AuthContext auth, DeploymentEnvironmentQuery query)
        {
            if (auth.IsAdmin)
                return;

            query.TenantId = auth.TenantId;
            if (!auth.IsTenantAdmin)
                query.UserId = auth.UserId;
        }

        private static string? Normalize(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static AuthContext BuildSeedAuth(Vessel vessel)
        {
            string tenantId = !String.IsNullOrWhiteSpace(vessel.TenantId) ? vessel.TenantId! : "startup";
            string userId = !String.IsNullOrWhiteSpace(vessel.UserId) ? vessel.UserId! : "startup";
            return AuthContext.Authenticated(tenantId, userId, true, true, "StartupSeed", null, "Startup Seed");
        }

        private static DeploymentEnvironment ChooseDefaultEnvironment(List<DeploymentEnvironment> environments)
        {
            DeploymentEnvironment? preferred = environments.FirstOrDefault(environment =>
                String.Equals(environment.Name, "dev", StringComparison.OrdinalIgnoreCase)
                || String.Equals(environment.Name, "development", StringComparison.OrdinalIgnoreCase));
            if (preferred != null) return preferred;

            preferred = environments.FirstOrDefault(environment => environment.Kind == EnvironmentKindEnum.Development);
            if (preferred != null) return preferred;

            preferred = environments.FirstOrDefault(environment => environment.Active);
            if (preferred != null) return preferred;

            return environments[0];
        }

        private static EnvironmentKindEnum InferKind(string environmentName)
        {
            string normalized = environmentName.Trim().ToLowerInvariant();
            if (normalized == "dev" || normalized == "development")
                return EnvironmentKindEnum.Development;
            if (normalized == "test" || normalized == "qa")
                return EnvironmentKindEnum.Test;
            if (normalized == "stage" || normalized == "staging" || normalized == "preprod" || normalized == "pre-production")
                return EnvironmentKindEnum.Staging;
            if (normalized == "prod" || normalized == "production")
                return EnvironmentKindEnum.Production;
            if (normalized.Contains("customer", StringComparison.OrdinalIgnoreCase))
                return EnvironmentKindEnum.CustomerHosted;
            return EnvironmentKindEnum.Custom;
        }

        private static string NormalizeRequired(string? value, string parameterName)
        {
            string? normalized = Normalize(value);
            if (String.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException(parameterName + " is required.");
            return normalized;
        }

        private static List<DeploymentVerificationDefinition> NormalizeVerificationDefinitions(
            List<DeploymentVerificationDefinition>? definitions)
        {
            List<DeploymentVerificationDefinition> normalized = new List<DeploymentVerificationDefinition>();
            foreach (DeploymentVerificationDefinition definition in definitions ?? new List<DeploymentVerificationDefinition>())
            {
                if (definition == null)
                    continue;

                string? name = Normalize(definition.Name);
                string? path = Normalize(definition.Path);
                if (String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(path))
                    continue;

                DeploymentVerificationDefinition cleaned = new DeploymentVerificationDefinition
                {
                    Id = Normalize(definition.Id) ?? Constants.IdGenerator.GenerateKSortable("vrf_", 24),
                    Name = name,
                    Method = String.IsNullOrWhiteSpace(definition.Method) ? "GET" : definition.Method.Trim().ToUpperInvariant(),
                    Path = path,
                    RequestBody = Normalize(definition.RequestBody),
                    Headers = definition.Headers != null
                        ? new Dictionary<string, string>(definition.Headers
                            .Where(item => !String.IsNullOrWhiteSpace(item.Key))
                            .ToDictionary(item => item.Key.Trim(), item => item.Value ?? String.Empty, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                    ExpectedStatusCode = definition.ExpectedStatusCode <= 0 ? 200 : definition.ExpectedStatusCode,
                    MustContainText = Normalize(definition.MustContainText),
                    Active = definition.Active
                };
                normalized.Add(cleaned);
            }

            return normalized;
        }

        private static int NormalizeMonitoringWindowMinutes(int? minutes)
        {
            return Math.Clamp(minutes ?? 0, 0, 1440);
        }

        private static int NormalizeMonitoringIntervalSeconds(int? seconds)
        {
            return Math.Clamp(seconds ?? 300, 30, 3600);
        }
    }
}
