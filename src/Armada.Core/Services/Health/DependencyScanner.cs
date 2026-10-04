namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Runs the NuGet and npm dependency tools for a repository and merges their results. NuGet targets are the
    /// solution files (.sln, .slnx) when there are between one and <see cref="MaxSolutionTargets"/>, otherwise every
    /// non-test project file (up to <see cref="MaxProjectTargets"/>). npm targets are directories holding a package.json
    /// next to a package-lock.json or npm-shrinkwrap.json. Directories in the import exclude list are never searched.
    /// A failure of any invocation sets the result's error code; dependencies from successful invocations are kept.
    /// Thread-safe.
    /// </summary>
    public class DependencyScanner
    {
        #region Public-Members

        /// <summary>
        /// Largest number of solution files scanned individually. Default 5, minimum 1, maximum 50.
        /// </summary>
        public int MaxSolutionTargets
        {
            get => _MaxSolutionTargets;
            set => _MaxSolutionTargets = value < 1 ? 1 : (value > 50 ? 50 : value);
        }

        /// <summary>
        /// Largest number of project files scanned when solutions are not used. Default 50, minimum 1, maximum 500.
        /// </summary>
        public int MaxProjectTargets
        {
            get => _MaxProjectTargets;
            set => _MaxProjectTargets = value < 1 ? 1 : (value > 500 ? 500 : value);
        }

        /// <summary>
        /// Executable used for NuGet checks. Default "dotnet".
        /// </summary>
        public string DotnetExecutable
        {
            get => _DotnetExecutable;
            set => _DotnetExecutable = String.IsNullOrWhiteSpace(value) ? "dotnet" : value;
        }

        /// <summary>
        /// Executable used for npm checks. Default "npm" ("npm.cmd" on Windows).
        /// </summary>
        public string NpmExecutable
        {
            get => _NpmExecutable;
            set => _NpmExecutable = String.IsNullOrWhiteSpace(value) ? DependencyToolRunner.NpmExecutable() : value;
        }

        #endregion

        #region Private-Members

        private readonly DependencyToolRunner _Runner;
        private int _MaxSolutionTargets = 5;
        private int _MaxProjectTargets = 50;
        private string _DotnetExecutable = "dotnet";
        private string _NpmExecutable = DependencyToolRunner.NpmExecutable();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runner">Tool runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when runner is null.</exception>
        public DependencyScanner(DependencyToolRunner runner)
        {
            _Runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Scan the evaluated repository.
        /// </summary>
        /// <param name="context">Evaluation context (must point at a working directory).</param>
        /// <param name="mode">Outdated or vulnerable.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The merged result; HasTargets is false when there is nothing to scan.</returns>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public async Task<DependencyScanResult> ScanAsync(VesselHealthContext context, DependencyScanModeEnum mode, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            RepositoryFileInventory inventory = await context.Cache.GetInventoryAsync(context, token).ConfigureAwait(false);
            string root = context.EvaluatedPath!;
            int timeout = context.Settings.DependencyCommandTimeoutSeconds;
            DependencyScanResult merged = new DependencyScanResult();

            foreach (string target in ResolveNuGetTargets(inventory, _MaxSolutionTargets, _MaxProjectTargets))
            {
                token.ThrowIfCancellationRequested();
                merged.HasTargets = true;
                List<string> args = new List<string>
                {
                    "list", Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar)), "package",
                    mode == DependencyScanModeEnum.Outdated ? "--outdated" : "--vulnerable", "--format", "json"
                };
                DependencyToolResult result = await _Runner.RunAsync(_DotnetExecutable, args, root, timeout, token).ConfigureAwait(false);
                DependencyScanResult part = DotnetListParser.Interpret(result, mode, root, timeout);
                Absorb(merged, part);
                if (part.ErrorCode == VesselHealthDetailCodes.ToolMissing) break;
            }

            foreach (string manifest in ResolveNpmTargets(inventory))
            {
                token.ThrowIfCancellationRequested();
                merged.HasTargets = true;
                string directory = RepositoryFileInventory.GetDirectory(manifest);
                string workingDirectory = directory.Length == 0 ? root : Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar));
                List<string> args = mode == DependencyScanModeEnum.Outdated
                    ? new List<string> { "outdated", "--json" }
                    : new List<string> { "audit", "--json" };
                DependencyToolResult result = await _Runner.RunAsync(_NpmExecutable, args, workingDirectory, timeout, token).ConfigureAwait(false);
                DependencyScanResult part = mode == DependencyScanModeEnum.Outdated
                    ? NpmOutputParser.InterpretOutdated(result, manifest, timeout)
                    : NpmOutputParser.InterpretAudit(result, manifest, timeout);
                Absorb(merged, part);
                if (part.ErrorCode == VesselHealthDetailCodes.ToolMissing) break;
            }

            return merged;
        }

        /// <summary>
        /// Resolve NuGet targets: solutions when there are 1 to maxSolutions of them, otherwise non-test projects.
        /// </summary>
        /// <param name="inventory">Repository inventory.</param>
        /// <param name="maxSolutions">Largest solution count scanned individually.</param>
        /// <param name="maxProjects">Largest project count scanned.</param>
        /// <returns>Repository-relative target paths.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inventory is null.</exception>
        public static List<string> ResolveNuGetTargets(RepositoryFileInventory inventory, int maxSolutions, int maxProjects)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            List<string> solutions = inventory.FindByExtension(".sln").Concat(inventory.FindByExtension(".slnx")).Distinct().ToList();
            if (solutions.Count >= 1 && solutions.Count <= maxSolutions) return solutions;

            List<string> projects = new List<string>();
            foreach (string project in inventory.FindByExtension(".csproj").Concat(inventory.FindByExtension(".fsproj")).Concat(inventory.FindByExtension(".vbproj")))
            {
                if (TestInfrastructureDetector.IsDotNetTestProject(project, inventory.ReadText(project))) continue;
                projects.Add(project);
                if (projects.Count >= maxProjects) break;
            }

            return projects;
        }

        /// <summary>
        /// Resolve npm targets: package.json files with a package-lock.json or npm-shrinkwrap.json beside them.
        /// </summary>
        /// <param name="inventory">Repository inventory.</param>
        /// <returns>Repository-relative package.json paths.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inventory is null.</exception>
        public static List<string> ResolveNpmTargets(RepositoryFileInventory inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            HashSet<string> files = new HashSet<string>(inventory.Files, StringComparer.OrdinalIgnoreCase);
            List<string> targets = new List<string>();
            foreach (string manifest in inventory.FindByFileName("package.json"))
            {
                string directory = RepositoryFileInventory.GetDirectory(manifest);
                string prefix = directory.Length == 0 ? "" : directory + "/";
                if (files.Contains(prefix + "package-lock.json") || files.Contains(prefix + "npm-shrinkwrap.json")) targets.Add(manifest);
            }

            return targets;
        }

        #endregion

        #region Private-Methods

        private static void Absorb(DependencyScanResult merged, DependencyScanResult part)
        {
            merged.AddFailure(part.ErrorCode, part.ErrorValue);
            foreach (VesselDependency dependency in part.Dependencies) DotnetListParser.Merge(merged.Dependencies, dependency);
        }

        #endregion
    }
}
