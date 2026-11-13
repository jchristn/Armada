namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Seeds the built-in fleet actions into each tenant as ordinary, editable rows identified by BuiltInKey. A key
    /// that already exists in a tenant (including a soft-deleted, inactive row) is never seeded again, so deleting a
    /// built-in is permanent. A built-in that still matches an earlier shipped version exactly (nobody edited it) is
    /// upgraded to the current definition; an edited one keeps its edits. Command bodies are chosen for the Admiral's platform at seed time: POSIX shell on Linux
    /// and macOS, PowerShell on Windows.
    /// Thread-safe.
    /// </summary>
    public class FleetActionSeedService
    {
        #region Public-Members

        /// <summary>
        /// Built-in key: fast-forward the default branch.
        /// </summary>
        public const string FastForwardKey = "ff-default-branch";

        /// <summary>
        /// Built-in key: prune local branches merged into the default branch.
        /// </summary>
        public const string PruneMergedBranchesKey = "prune-merged-branches";

        /// <summary>
        /// Built-in key: run the vessel's build command.
        /// </summary>
        public const string BuildKey = "build";

        /// <summary>
        /// Built-in key: Mission that updates outdated dependencies.
        /// </summary>
        public const string UpdateDependenciesKey = "update-dependencies";

        /// <summary>
        /// Built-in key: Mission that adds a test project.
        /// </summary>
        public const string AddTestProjectKey = "add-test-project";

        /// <summary>
        /// Built-in key: a read-only status snapshot of each checkout.
        /// </summary>
        public const string RepositoryStatusKey = "repo-status";

        /// <summary>
        /// Built-in key: update one named dependency where it is referenced.
        /// </summary>
        public const string UpdatePackageKey = "update-package";

        /// <summary>
        /// Built-in key: upgrade the runtime or framework version.
        /// </summary>
        public const string UpgradeRuntimeKey = "upgrade-runtime";

        /// <summary>
        /// Built-in key: add or align shared repository files.
        /// </summary>
        public const string StandardizeFilesKey = "standardize-files";

        /// <summary>
        /// Built-in key: bring the documentation in line with the code.
        /// </summary>
        public const string SyncDocsKey = "sync-docs";

        /// <summary>
        /// Built-in key: run the test command.
        /// </summary>
        public const string RunTestsKey = "run-tests";

        /// <summary>
        /// Built-in key: fix the failures from the latest Run tests result.
        /// </summary>
        public const string FixFailingTestsKey = "fix-failing-tests";

        /// <summary>
        /// Built-in key: add tests for untested behavior.
        /// </summary>
        public const string ImproveCoverageKey = "improve-coverage";

        /// <summary>
        /// Built-in key: run the lint command.
        /// </summary>
        public const string LintKey = "lint";

        /// <summary>
        /// Built-in key: fix the findings from the latest Lint result.
        /// </summary>
        public const string FixLintKey = "fix-lint";

        /// <summary>
        /// Built-in key: report-only code cleanliness review.
        /// </summary>
        public const string CleanlinessReviewKey = "cleanliness-review";

        /// <summary>
        /// Built-in key: remove dead code and unused dependencies.
        /// </summary>
        public const string RemoveDeadCodeKey = "remove-dead-code";

        /// <summary>
        /// Built-in key: report-only performance review.
        /// </summary>
        public const string PerformanceReviewKey = "performance-review";

        /// <summary>
        /// Built-in key: apply approved performance findings.
        /// </summary>
        public const string ApplyPerformanceFixesKey = "apply-performance-fixes";

        /// <summary>
        /// Built-in key: dependency vulnerability audit with parsed findings.
        /// </summary>
        public const string VulnerabilityAuditKey = "vulnerability-audit";

        /// <summary>
        /// Built-in key: upgrade the packages from the latest Vulnerability audit.
        /// </summary>
        public const string FixVulnerabilitiesKey = "fix-vulnerabilities";

        /// <summary>
        /// Built-in key: the package manager's outdated report.
        /// </summary>
        public const string OutdatedReportKey = "outdated-report";

        /// <summary>
        /// Built-in key: gitleaks secrets scan with parsed findings.
        /// </summary>
        public const string SecretsScanKey = "secrets-scan";

        /// <summary>
        /// Built-in key: report-only security review.
        /// </summary>
        public const string SecurityReviewKey = "security-review";

        /// <summary>
        /// Built-in key: license report checked against a policy.
        /// </summary>
        public const string LicenseComplianceKey = "license-compliance";

        #endregion

        #region Private-Members

        private readonly string _Header = "[FleetActionSeedService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, bool> _SeededTenants = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public FleetActionSeedService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed the built-ins into every tenant.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows created.</returns>
        public async Task<int> SeedAllTenantsAsync(CancellationToken token = default)
        {
            List<TenantMetadata> tenants = await _Database.Tenants.EnumerateAsync(token).ConfigureAwait(false);
            int created = 0;
            foreach (TenantMetadata tenant in tenants)
            {
                created += await SeedTenantAsync(tenant.Id, token).ConfigureAwait(false);
            }

            return created;
        }

        /// <summary>
        /// Seed the built-ins into one tenant once per process (later calls for the same tenant return 0 without
        /// touching the database).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows created.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantId"/> is null or empty.</exception>
        public async Task<int> EnsureSeededAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (_SeededTenants.ContainsKey(tenantId)) return 0;
            return await SeedTenantAsync(tenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Seed the built-ins into one tenant, creating only keys that do not exist (active or not).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows created.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tenantId"/> is null or empty.</exception>
        public async Task<int> SeedTenantAsync(string tenantId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            await _Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                bool windows = OperatingSystem.IsWindows();
                List<FleetAction> previous = FleetActionBuiltInCatalog.PreviousVersions(windows);
                int created = 0;
                int upgraded = 0;
                foreach (FleetAction builtIn in BuildDefaults(windows))
                {
                    FleetAction? existing = await _Database.FleetActions.ReadByBuiltInKeyAsync(tenantId, builtIn.BuiltInKey!, token).ConfigureAwait(false);
                    if (existing == null)
                    {
                        builtIn.TenantId = tenantId;
                        await _Database.FleetActions.CreateAsync(builtIn, token).ConfigureAwait(false);
                        created++;
                        continue;
                    }

                    // A built-in nobody edited (it still matches an earlier shipped version) takes the current definition;
                    // an edited or deleted one is left alone.
                    if (!existing.Active || MatchesDefinition(existing, builtIn)) continue;
                    if (!previous.Any(p => p.BuiltInKey == builtIn.BuiltInKey && MatchesDefinition(existing, p))) continue;
                    CopyDefinition(builtIn, existing);
                    existing.LastUpdateUtc = DateTime.UtcNow;
                    await _Database.FleetActions.UpdateAsync(existing, token).ConfigureAwait(false);
                    upgraded++;
                }

                _SeededTenants[tenantId] = true;
                if (created > 0) _Logging.Info(_Header + "seeded " + created + " built-in fleet action(s) for tenant " + tenantId);
                if (upgraded > 0) _Logging.Info(_Header + "upgraded " + upgraded + " unedited built-in fleet action(s) for tenant " + tenantId);
                return created;
            }
            finally
            {
                _Gate.Release();
            }
        }

        /// <summary>
        /// Build fresh (unsaved, tenant-less) instances of every built-in action (see <see cref="FleetActionBuiltInCatalog"/>).
        /// </summary>
        /// <param name="windows">True to use PowerShell command bodies, false for POSIX shell.</param>
        /// <returns>Built-in actions.</returns>
        public static List<FleetAction> BuildDefaults(bool windows)
        {
            return FleetActionBuiltInCatalog.Build(windows);
        }

        /// <summary>
        /// Whether a stored built-in still matches a shipped definition exactly in every field a tenant can edit.
        /// </summary>
        /// <param name="stored">Stored row.</param>
        /// <param name="shipped">Shipped definition.</param>
        /// <returns>True when nothing was edited.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static bool MatchesDefinition(FleetAction stored, FleetAction shipped)
        {
            if (stored == null) throw new ArgumentNullException(nameof(stored));
            if (shipped == null) throw new ArgumentNullException(nameof(shipped));
            return stored.Name == shipped.Name
                && stored.Description == shipped.Description
                && stored.Kind == shipped.Kind
                && stored.CommandText == shipped.CommandText
                && stored.CommandWindows == shipped.CommandWindows
                && stored.PromptTemplate == shipped.PromptTemplate
                && stored.PipelineId == shipped.PipelineId
                && stored.Persona == shipped.Persona
                && stored.TimeoutSeconds == shipped.TimeoutSeconds
                && stored.DefaultConcurrency == shipped.DefaultConcurrency
                && stored.RequiresCleanWorkingTree == shipped.RequiresCleanWorkingTree
                && stored.OutputParser == shipped.OutputParser
                && stored.ScanSourceActionKey == shipped.ScanSourceActionKey
                && stored.RequiresNetwork == shipped.RequiresNetwork
                && stored.ApplicabilityCommand == shipped.ApplicabilityCommand
                && stored.ApplicabilityCommandWindows == shipped.ApplicabilityCommandWindows
                && stored.MissionMode == shipped.MissionMode
                && stored.RequiredTools.SequenceEqual(shipped.RequiredTools, StringComparer.Ordinal)
                && JsonSerializer.Serialize(stored.Inputs) == JsonSerializer.Serialize(shipped.Inputs);
        }

        #endregion

        #region Private-Methods

        private static void CopyDefinition(FleetAction source, FleetAction target)
        {
            target.Name = source.Name;
            target.Description = source.Description;
            target.Kind = source.Kind;
            target.CommandText = source.CommandText;
            target.CommandWindows = source.CommandWindows;
            target.PromptTemplate = source.PromptTemplate;
            target.PipelineId = source.PipelineId;
            target.Persona = source.Persona;
            target.TimeoutSeconds = source.TimeoutSeconds;
            target.DefaultConcurrency = source.DefaultConcurrency;
            target.RequiresCleanWorkingTree = source.RequiresCleanWorkingTree;
            target.OutputParser = source.OutputParser;
            target.ScanSourceActionKey = source.ScanSourceActionKey;
            target.RequiresNetwork = source.RequiresNetwork;
            target.ApplicabilityCommand = source.ApplicabilityCommand;
            target.ApplicabilityCommandWindows = source.ApplicabilityCommandWindows;
            target.MissionMode = source.MissionMode;
            target.RequiredTools = new List<string>(source.RequiredTools);
            target.Inputs = new List<FleetActionInput>(source.Inputs);
        }

        #endregion
    }
}
