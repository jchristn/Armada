namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Seeds the built-in fleet actions into each tenant as ordinary, editable rows identified by BuiltInKey. A key
    /// that already exists in a tenant (including a soft-deleted, inactive row) is never seeded again, so deleting a
    /// built-in is permanent. Command bodies are chosen for the Admiral's platform at seed time: POSIX shell on Linux
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
                int created = 0;
                foreach (FleetAction builtIn in BuildDefaults(OperatingSystem.IsWindows()))
                {
                    FleetAction? existing = await _Database.FleetActions.ReadByBuiltInKeyAsync(tenantId, builtIn.BuiltInKey!, token).ConfigureAwait(false);
                    if (existing != null) continue;

                    builtIn.TenantId = tenantId;
                    await _Database.FleetActions.CreateAsync(builtIn, token).ConfigureAwait(false);
                    created++;
                }

                _SeededTenants[tenantId] = true;
                if (created > 0) _Logging.Info(_Header + "seeded " + created + " built-in fleet action(s) for tenant " + tenantId);
                return created;
            }
            finally
            {
                _Gate.Release();
            }
        }

        /// <summary>
        /// Build fresh (unsaved, tenant-less) instances of the five built-in actions.
        /// </summary>
        /// <param name="windows">True to use PowerShell command bodies, false for POSIX shell.</param>
        /// <returns>Built-in actions.</returns>
        public static List<FleetAction> BuildDefaults(bool windows)
        {
            List<FleetAction> actions = new List<FleetAction>();

            actions.Add(new FleetAction
            {
                Name = "Fast-forward default branch",
                Description = "Runs git pull --ff-only in each working directory. Skips vessels with uncommitted changes; never creates merge commits.",
                Kind = FleetActionKindEnum.Command,
                CommandText = "git pull --ff-only",
                TimeoutSeconds = 300,
                DefaultConcurrency = 4,
                RequiresCleanWorkingTree = true,
                IsBuiltIn = true,
                BuiltInKey = FastForwardKey
            });

            actions.Add(new FleetAction
            {
                Name = "Prune merged branches",
                Description = "Fetches with --prune, then deletes local branches already merged into the vessel's default branch with git branch -d. Never deletes the current branch or the default branch.",
                Kind = FleetActionKindEnum.Command,
                CommandText = windows ? PruneMergedPowerShell : PruneMergedPosix,
                TimeoutSeconds = 300,
                DefaultConcurrency = 4,
                RequiresCleanWorkingTree = true,
                IsBuiltIn = true,
                BuiltInKey = PruneMergedBranchesKey
            });

            actions.Add(new FleetAction
            {
                Name = "Build",
                Description = "Runs the vessel's definition-of-done build command in its working directory. Vessels without a build command are skipped with reason NoBuildCommand.",
                Kind = FleetActionKindEnum.Command,
                CommandText = "{{vessel.buildCommand}}",
                TimeoutSeconds = 1800,
                DefaultConcurrency = 2,
                RequiresCleanWorkingTree = false,
                IsBuiltIn = true,
                BuiltInKey = BuildKey
            });

            actions.Add(new FleetAction
            {
                Name = "Update outdated dependencies",
                Description = "Dispatches one voyage per vessel asking a captain to update the outdated and vulnerable packages listed in the vessel's health findings.",
                Kind = FleetActionKindEnum.Mission,
                PromptTemplate =
                    "Update the outdated dependencies in the {{vessel.name}} repository (default branch {{vessel.defaultBranch}})." + "\n\n"
                    + "Current health findings for this repository:" + "\n"
                    + "{{health.summary}}" + "\n\n"
                    + "Update each outdated or vulnerable package to its latest compatible version. Prefer patch and minor updates; "
                    + "take a major upgrade only when you can adapt the code safely, and explain any major upgrade you skip. "
                    + "Build the solution and run the existing tests after updating, and fix anything the updates break. "
                    + "Do not change unrelated code.",
                DefaultConcurrency = 2,
                RequiresCleanWorkingTree = false,
                IsBuiltIn = true,
                BuiltInKey = UpdateDependenciesKey
            });

            actions.Add(new FleetAction
            {
                Name = "Add a test project",
                Description = "Dispatches one voyage per vessel asking a captain to add an automated test project that follows the repository's conventions.",
                Kind = FleetActionKindEnum.Mission,
                PromptTemplate =
                    "Add automated tests to the {{vessel.name}} repository if it does not already have a test project." + "\n\n"
                    + "Follow the conventions of the repository's primary language and build system. Add a test project (or test "
                    + "directory) wired into the existing build so the standard test command runs it, and write a small number of "
                    + "meaningful tests that cover existing behavior. Make sure the build and the new tests pass. "
                    + "Do not change production code except where a test exposes a real bug, and describe any such change." + "\n\n"
                    + "Current health findings for this repository:" + "\n"
                    + "{{health.summary}}",
                DefaultConcurrency = 2,
                RequiresCleanWorkingTree = false,
                IsBuiltIn = true,
                BuiltInKey = AddTestProjectKey
            });

            return actions;
        }

        #endregion

        #region Private-Members

        private static readonly string PruneMergedPosix =
            "git fetch --prune || echo \"git fetch failed; pruning against local refs\"" + "\n"
            + "default_branch=\"{{vessel.defaultBranch}}\"" + "\n"
            + "current_branch=\"$(git symbolic-ref --quiet --short HEAD || true)\"" + "\n"
            + "base=\"origin/$default_branch\"" + "\n"
            + "git rev-parse --verify --quiet \"$base\" >/dev/null || base=\"$default_branch\"" + "\n"
            + "git for-each-ref --format='%(refname:short)' --merged \"$base\" refs/heads | while IFS= read -r branch; do" + "\n"
            + "  if [ \"$branch\" != \"$current_branch\" ] && [ \"$branch\" != \"$default_branch\" ]; then" + "\n"
            + "    git branch -d -- \"$branch\" || echo \"could not delete $branch\"" + "\n"
            + "  fi" + "\n"
            + "done";

        private static readonly string PruneMergedPowerShell =
            "git fetch --prune; if ($LASTEXITCODE -ne 0) { Write-Output 'git fetch failed; pruning against local refs' }; "
            + "$defaultBranch = '{{vessel.defaultBranch}}'; "
            + "$currentBranch = git symbolic-ref --quiet --short HEAD; "
            + "$base = 'origin/' + $defaultBranch; "
            + "git rev-parse --verify --quiet $base *> $null; if ($LASTEXITCODE -ne 0) { $base = $defaultBranch }; "
            + "git for-each-ref --format='%(refname:short)' --merged $base refs/heads | ForEach-Object { "
            + "if ($_ -ne $currentBranch -and $_ -ne $defaultBranch) { git branch -d -- $_; if ($LASTEXITCODE -ne 0) { Write-Output ('could not delete ' + $_) } } }; "
            + "exit 0";

        #endregion
    }
}
