namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Health.Json;

    /// <summary>
    /// Interprets dotnet list package --outdated|--vulnerable --format json output. Any failure (missing tool, timeout,
    /// restore problem, non-zero exit, unparsable output) yields a result with an error code, never an empty success.
    /// Stateless and thread-safe.
    /// </summary>
    public static class DotnetListParser
    {
        #region Public-Members

        /// <summary>
        /// Ecosystem label written on NuGet dependency rows.
        /// </summary>
        public const string Ecosystem = "NuGet";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Interpret one dotnet list package invocation.
        /// </summary>
        /// <param name="result">Tool result.</param>
        /// <param name="mode">Outdated or vulnerable.</param>
        /// <param name="repositoryRoot">Repository root used to make project paths relative, or null.</param>
        /// <param name="timeoutSeconds">Configured timeout, reported with a Timeout error.</param>
        /// <returns>The scan result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        public static DependencyScanResult Interpret(DependencyToolResult result, DependencyScanModeEnum mode, string? repositoryRoot, int timeoutSeconds)
        {
            return Interpret(result, mode, repositoryRoot, timeoutSeconds, null);
        }

        /// <summary>
        /// Interpret one dotnet list package invocation. A failed run is classified as RestoreRequired only when a project
        /// it covers has no <c>obj/project.assets.json</c> (the file restore writes); the tool's message text is never
        /// read for that decision.
        /// </summary>
        /// <param name="result">Tool result.</param>
        /// <param name="mode">Outdated or vulnerable.</param>
        /// <param name="repositoryRoot">Repository root used to make project paths relative, or null.</param>
        /// <param name="timeoutSeconds">Configured timeout, reported with a Timeout error.</param>
        /// <param name="projectFiles">Absolute paths of the project files the invocation covers (used, with the projects
        /// named in the report, for the restore check), or null.</param>
        /// <returns>The scan result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        public static DependencyScanResult Interpret(DependencyToolResult result, DependencyScanModeEnum mode, string? repositoryRoot, int timeoutSeconds, IEnumerable<string>? projectFiles)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (result.Outcome == DependencyToolOutcomeEnum.ToolMissing) return DependencyScanResult.Failed(VesselHealthDetailCodes.ToolMissing);
            if (result.Outcome == DependencyToolOutcomeEnum.TimedOut) return DependencyScanResult.Failed(VesselHealthDetailCodes.Timeout, timeoutSeconds);

            string? json = DependencyJson.ExtractObject(result.StandardOutput);
            DotnetListReport? report = null;
            if (json != null)
            {
                try
                {
                    report = JsonSerializer.Deserialize<DotnetListReport>(json, DependencyJson.Options);
                }
                catch (JsonException)
                {
                    report = null;
                }
            }

            if (report == null)
            {
                if (result.ExitCode != 0 && AnyAssetsFileMissing(projectFiles, null)) return DependencyScanResult.Failed(VesselHealthDetailCodes.RestoreRequired);
                if (result.ExitCode != 0) return DependencyScanResult.Failed(VesselHealthDetailCodes.ToolFailed, result.ExitCode);
                return DependencyScanResult.Failed(VesselHealthDetailCodes.ParseError);
            }

            DependencyScanResult scan = new DependencyScanResult();
            scan.HasTargets = true;

            List<DotnetListProblem> errors = (report.Problems ?? new List<DotnetListProblem>())
                .Where(p => p != null && String.Equals(p.Level, "error", StringComparison.OrdinalIgnoreCase))
                .ToList();
            bool failed = errors.Count > 0 || result.ExitCode != 0;
            if (failed && AnyAssetsFileMissing(projectFiles, report.Projects))
                scan.AddFailure(VesselHealthDetailCodes.RestoreRequired, null);
            else if (errors.Count > 0)
                scan.AddFailure(VesselHealthDetailCodes.ToolFailed, result.ExitCode != 0 ? result.ExitCode : 1);
            else if (result.ExitCode != 0)
                scan.AddFailure(VesselHealthDetailCodes.ToolFailed, (long?)result.ExitCode);
            else if (report.Projects == null)
                scan.AddFailure(VesselHealthDetailCodes.ParseError, null);

            foreach (DotnetListProject project in report.Projects ?? new List<DotnetListProject>())
            {
                if (project == null) continue;
                string? projectPath = MakeRelative(project.Path, repositoryRoot);
                foreach (DotnetListFramework framework in project.Frameworks ?? new List<DotnetListFramework>())
                {
                    if (framework == null) continue;
                    foreach (DotnetListPackage package in framework.TopLevelPackages ?? new List<DotnetListPackage>())
                    {
                        if (package == null || String.IsNullOrWhiteSpace(package.Id)) continue;
                        VesselDependency? dependency = mode == DependencyScanModeEnum.Outdated
                            ? BuildOutdated(package, projectPath)
                            : BuildVulnerable(package, projectPath);
                        if (dependency != null) Merge(scan.Dependencies, dependency);
                    }
                }
            }

            return scan;
        }

        /// <summary>
        /// Merge a dependency into a list keyed by ecosystem, project path, and package name, keeping the highest drift
        /// and severity.
        /// </summary>
        /// <param name="target">List to merge into.</param>
        /// <param name="dependency">Dependency to merge.</param>
        /// <exception cref="ArgumentNullException">Thrown when target or dependency is null.</exception>
        public static void Merge(List<VesselDependency> target, VesselDependency dependency)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (dependency == null) throw new ArgumentNullException(nameof(dependency));
            VesselDependency? existing = target.FirstOrDefault(d =>
                String.Equals(d.Ecosystem, dependency.Ecosystem, StringComparison.OrdinalIgnoreCase)
                && String.Equals(d.ProjectPath ?? "", dependency.ProjectPath ?? "", StringComparison.Ordinal)
                && String.Equals(d.PackageName, dependency.PackageName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                target.Add(dependency);
                return;
            }

            if (dependency.Drift > existing.Drift)
            {
                existing.Drift = dependency.Drift;
                existing.LatestVersion = dependency.LatestVersion;
            }

            if (existing.LatestVersion == null) existing.LatestVersion = dependency.LatestVersion;
            if (existing.CurrentVersion == null) existing.CurrentVersion = dependency.CurrentVersion;
            if (dependency.IsVulnerable) existing.IsVulnerable = true;
            if (dependency.Severity > existing.Severity) existing.Severity = dependency.Severity;
            if (existing.AdvisoryUrl == null) existing.AdvisoryUrl = dependency.AdvisoryUrl;
        }

        #endregion

        #region Private-Methods

        private static VesselDependency? BuildOutdated(DotnetListPackage package, string? projectPath)
        {
            string? current = package.ResolvedVersion ?? package.RequestedVersion;
            DependencyDriftEnum drift = VersionDriftCalculator.Compute(current, package.LatestVersion);
            if (drift == DependencyDriftEnum.None) return null;
            VesselDependency dependency = new VesselDependency();
            dependency.Ecosystem = Ecosystem;
            dependency.ProjectPath = projectPath;
            dependency.PackageName = package.Id!;
            dependency.CurrentVersion = current;
            dependency.LatestVersion = package.LatestVersion;
            dependency.Drift = drift;
            return dependency;
        }

        private static VesselDependency? BuildVulnerable(DotnetListPackage package, string? projectPath)
        {
            List<DotnetListVulnerability> vulnerabilities = package.Vulnerabilities ?? new List<DotnetListVulnerability>();
            if (vulnerabilities.Count == 0) return null;
            VesselDependency dependency = new VesselDependency();
            dependency.Ecosystem = Ecosystem;
            dependency.ProjectPath = projectPath;
            dependency.PackageName = package.Id!;
            dependency.CurrentVersion = package.ResolvedVersion ?? package.RequestedVersion;
            dependency.IsVulnerable = true;
            foreach (DotnetListVulnerability vulnerability in vulnerabilities)
            {
                if (vulnerability == null) continue;
                VulnerabilitySeverityEnum severity = DependencyJson.ParseSeverity(vulnerability.Severity);
                if (severity > dependency.Severity) dependency.Severity = severity;
                if (dependency.AdvisoryUrl == null && !String.IsNullOrWhiteSpace(vulnerability.AdvisoryUrl)) dependency.AdvisoryUrl = vulnerability.AdvisoryUrl;
            }

            if (dependency.Severity == VulnerabilitySeverityEnum.None) dependency.Severity = VulnerabilitySeverityEnum.Moderate;
            return dependency;
        }

        /// <summary>
        /// Whether any covered project lacks <c>obj/project.assets.json</c>. Projects come from the caller and from the
        /// report; relative or empty paths are skipped. False when no project is known (the cause cannot be decided).
        /// </summary>
        private static bool AnyAssetsFileMissing(IEnumerable<string>? projectFiles, List<DotnetListProject>? reportProjects)
        {
            List<string> paths = new List<string>();
            if (projectFiles != null) paths.AddRange(projectFiles);
            if (reportProjects != null)
            {
                foreach (DotnetListProject project in reportProjects)
                {
                    if (project != null && !String.IsNullOrWhiteSpace(project.Path)) paths.Add(project.Path!);
                }
            }

            foreach (string path in paths)
            {
                if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) continue;
                string? directory = Path.GetDirectoryName(path);
                if (String.IsNullOrEmpty(directory)) continue;
                if (!File.Exists(Path.Combine(directory, "obj", "project.assets.json"))) return true;
            }

            return false;
        }

        private static string? MakeRelative(string? path, string? root)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            if (String.IsNullOrWhiteSpace(root)) return path.Replace('\\', '/');
            try
            {
                if (!PathContainment.IsInside(root, path, allowRoot: true)) return path.Replace('\\', '/');
                string relative = Path.GetRelativePath(root, path);
                return relative.Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return path.Replace('\\', '/');
            }
        }

        #endregion
    }
}
