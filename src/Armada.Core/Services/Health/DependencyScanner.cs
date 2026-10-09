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

            // A checkout on a Harbor is scanned there, with the Harbor's dotnet and npm.
            DependencyToolRunner runner = context.Host != null ? new DependencyToolRunner(context.Host.Commands) : _Runner;

            foreach (string target in ResolveNuGetTargets(inventory, _MaxSolutionTargets, _MaxProjectTargets))
            {
                token.ThrowIfCancellationRequested();
                merged.HasTargets = true;
                List<string> args = new List<string>
                {
                    "list", Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar)), "package",
                    mode == DependencyScanModeEnum.Outdated ? "--outdated" : "--vulnerable", "--format", "json"
                };
                DependencyToolResult result = await runner.RunAsync(_DotnetExecutable, args, root, timeout, token).ConfigureAwait(false);
                List<string> covered = ResolveCoveredProjects(inventory, root, target);
                Func<string, bool>? assetsFileExists = null;
                if (context.Host != null)
                {
                    // Whether restore has run is decided from the Harbor's checkout, not the Admiral's disk.
                    HashSet<string> present = await FindAssetsFilesAsync(context.Host, root, DotnetListParser.RestoreCheckProjects(result, covered), token).ConfigureAwait(false);
                    assetsFileExists = project => present.Contains(project);
                }

                DependencyScanResult part = DotnetListParser.Interpret(result, mode, root, timeout, covered, assetsFileExists);
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
                DependencyToolResult result = await runner.RunAsync(_NpmExecutable, args, workingDirectory, timeout, token).ConfigureAwait(false);
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
        /// Absolute paths of the project files a dotnet list target covers: the target itself when it is a project file,
        /// otherwise every project file in the inventory under the solution's directory.
        /// </summary>
        /// <param name="inventory">Repository inventory.</param>
        /// <param name="root">Repository root (absolute).</param>
        /// <param name="target">Repository-relative target (solution or project).</param>
        /// <returns>Absolute project file paths.</returns>
        /// <exception cref="ArgumentNullException">Thrown when inventory, root, or target is null.</exception>
        public static List<string> ResolveCoveredProjects(RepositoryFileInventory inventory, string root, string target)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (target == null) throw new ArgumentNullException(nameof(target));

            List<string> projects = new List<string>();
            if (IsProjectFile(target))
            {
                projects.Add(Path.Combine(root, target.Replace('/', Path.DirectorySeparatorChar)));
                return projects;
            }

            string directory = RepositoryFileInventory.GetDirectory(target);
            string prefix = directory.Length == 0 ? String.Empty : directory + "/";
            foreach (string project in inventory.FindByExtension(".csproj").Concat(inventory.FindByExtension(".fsproj")).Concat(inventory.FindByExtension(".vbproj")))
            {
                if (prefix.Length > 0 && !project.StartsWith(prefix, StringComparison.Ordinal)) continue;
                projects.Add(Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar)));
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

        /// <summary>
        /// Of the given project files (absolute paths on the host), those whose <c>obj/project.assets.json</c> exists in
        /// the host's checkout. A project outside the checkout, or one the host cannot answer for, counts as present, so an
        /// unanswered question never reports RestoreRequired.
        /// </summary>
        private static async Task<HashSet<string>> FindAssetsFilesAsync(VesselHost host, string root, List<string> projects, CancellationToken token)
        {
            HashSet<string> present = new HashSet<string>(StringComparer.Ordinal);
            foreach (string project in projects)
            {
                token.ThrowIfCancellationRequested();
                string? directory = DotnetListParser.GetProjectDirectory(project);
                string? relative = directory == null ? null : MakeCheckoutRelative(root, directory, host.IsWindows());
                if (relative == null)
                {
                    present.Add(project);
                    continue;
                }

                string assets = (relative.Length == 0 ? String.Empty : relative + "/") + "obj/project.assets.json";
                try
                {
                    VesselCheckoutFileInfo info = await host.Files.StatAsync(assets, token).ConfigureAwait(false);
                    if (info.Exists && !info.IsDirectory) present.Add(project);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    present.Add(project);
                }
            }

            return present;
        }

        /// <summary>
        /// A host path made relative to the checkout root with forward slashes ("" for the root itself), or null when it
        /// is not inside the root. Works on the host's path form, which can differ from this machine's.
        /// </summary>
        private static string? MakeCheckoutRelative(string root, string path, bool ignoreCase)
        {
            string normalizedRoot = root.Replace('\\', '/').TrimEnd('/');
            string normalizedPath = path.Replace('\\', '/').TrimEnd('/');
            StringComparison comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (String.Equals(normalizedRoot, normalizedPath, comparison)) return String.Empty;
            if (!normalizedPath.StartsWith(normalizedRoot + "/", comparison)) return null;
            return normalizedPath.Substring(normalizedRoot.Length + 1);
        }

        private static void Absorb(DependencyScanResult merged, DependencyScanResult part)
        {
            merged.AddFailure(part.ErrorCode, part.ErrorValue);
            foreach (VesselDependency dependency in part.Dependencies) DotnetListParser.Merge(merged.Dependencies, dependency);
        }

        private static bool IsProjectFile(string path)
        {
            string extension = Path.GetExtension(path);
            return String.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".fsproj", StringComparison.OrdinalIgnoreCase)
                || String.Equals(extension, ".vbproj", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
