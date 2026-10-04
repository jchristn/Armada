namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Health;
    using Armada.Core.Settings;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the Dependencies and Vulnerabilities criteria: parsing of captured dotnet list package and npm
    /// JSON, the drift calculator, target resolution honoring the exclude list, and the rule that a failed tool run
    /// (restore failure, non-zero exit, missing executable, timeout, unparsable output) is Unknown and never Pass.
    /// </summary>
    public sealed class VesselHealthDependencySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselHealthDependencies";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("drift_calculator", "Drift: first higher component decides after stripping prerelease and build", TestTags.Positive, () =>
            {
                AssertEqual(DependencyDriftEnum.Major, VersionDriftCalculator.Compute("12.0.1", "13.0.4"));
                AssertEqual(DependencyDriftEnum.Minor, VersionDriftCalculator.Compute("3.0.0", "3.1.1"));
                AssertEqual(DependencyDriftEnum.Patch, VersionDriftCalculator.Compute("8.4.0", "8.4.2-beta.1+build"));
                AssertEqual(DependencyDriftEnum.None, VersionDriftCalculator.Compute("2.0.0", "1.9.9"));
                AssertEqual(DependencyDriftEnum.None, VersionDriftCalculator.Compute("1.2.3", "1.2.3"));
                AssertEqual(DependencyDriftEnum.None, VersionDriftCalculator.Compute("1.2.3-rc.1", "1.2.3"));
                AssertEqual(DependencyDriftEnum.Major, VersionDriftCalculator.Compute("1", "2.0"));
                AssertEqual(DependencyDriftEnum.Minor, VersionDriftCalculator.Compute("v1.2", "v1.10"));
                AssertEqual(DependencyDriftEnum.None, VersionDriftCalculator.Compute(null, "1.0.0"));
                AssertEqual(DependencyDriftEnum.None, VersionDriftCalculator.Compute("1.0.0", ""));
            }));

            cases.Add(Case("dotnet_outdated_parses_fixture", "dotnet list --outdated JSON parses into per-project rows with drift", TestTags.Positive, () =>
            {
                DependencyScanResult scan = DotnetListParser.Interpret(Completed(0, VesselHealthJsonFixtures.DotnetOutdated), DependencyScanModeEnum.Outdated, "/repo", 120);
                AssertNull(scan.ErrorCode, "no error");
                AssertEqual(3, scan.Dependencies.Count, "framework duplicates merge per project");
                VesselDependency newtonsoft = scan.Dependencies.Single(d => d.PackageName == "Newtonsoft.Json");
                AssertEqual("src/App/App.csproj", newtonsoft.ProjectPath);
                AssertEqual(DependencyDriftEnum.Major, newtonsoft.Drift);
                AssertEqual("12.0.1", newtonsoft.CurrentVersion);
                AssertEqual("13.0.4", newtonsoft.LatestVersion);
                AssertEqual("NuGet", newtonsoft.Ecosystem);
                AssertEqual(DependencyDriftEnum.Minor, scan.Dependencies.Single(d => d.PackageName == "Serilog").Drift);
                AssertEqual(DependencyDriftEnum.Patch, scan.Dependencies.Single(d => d.PackageName == "Polly").Drift);

                VesselHealth health = new VesselHealth();
                VesselHealthCriterionResult result = DependenciesCriterion.Grade(scan, health);
                AssertEqual(VesselHealthStatusEnum.Fail, result.Status, "major drift fails");
                AssertEqual(VesselHealthDetailCodes.OutdatedPackages, result.DetailCode);
                AssertEqual(3L, result.ValueA!.Value);
                AssertEqual(1L, result.ValueB!.Value);
                AssertEqual(3, health.OutdatedCount!.Value);
                AssertEqual(1, health.OutdatedMajorCount!.Value);
            }));

            cases.Add(Case("dotnet_outdated_minor_only_warns", "Only minor or patch drift grades Warn; nothing outdated grades Pass", TestTags.Positive, () =>
            {
                DependencyScanResult minor = new DependencyScanResult { HasTargets = true };
                minor.Dependencies.Add(new VesselDependency { Ecosystem = "NuGet", PackageName = "A", Drift = DependencyDriftEnum.Minor });
                AssertEqual(VesselHealthStatusEnum.Warn, DependenciesCriterion.Grade(minor, null).Status);

                DependencyScanResult none = DotnetListParser.Interpret(Completed(0, VesselHealthJsonFixtures.DotnetOutdatedNone), DependencyScanModeEnum.Outdated, "/repo", 120);
                VesselHealthCriterionResult pass = DependenciesCriterion.Grade(none, null);
                AssertEqual(VesselHealthStatusEnum.Pass, pass.Status);
                AssertEqual(VesselHealthDetailCodes.NoOutdatedPackages, pass.DetailCode);

                AssertEqual(VesselHealthStatusEnum.NotApplicable, DependenciesCriterion.Grade(new DependencyScanResult(), null).Status, "no targets is NotApplicable");
            }));

            cases.Add(Case("dotnet_vulnerable_parses_fixture", "dotnet list --vulnerable JSON parses severity and advisory URL", TestTags.Positive, () =>
            {
                DependencyScanResult scan = DotnetListParser.Interpret(Completed(0, VesselHealthJsonFixtures.DotnetVulnerable), DependencyScanModeEnum.Vulnerable, "/repo", 120);
                AssertNull(scan.ErrorCode, "no error");
                VesselDependency dependency = scan.Dependencies.Single();
                AssertTrue(dependency.IsVulnerable);
                AssertEqual(VulnerabilitySeverityEnum.High, dependency.Severity);
                AssertEqual("https://github.com/advisories/GHSA-5crp-9r3c-p9vr", dependency.AdvisoryUrl);

                VesselHealth health = new VesselHealth();
                VesselHealthCriterionResult result = VulnerabilitiesCriterion.Grade(scan, health);
                AssertEqual(VesselHealthStatusEnum.Fail, result.Status, "High fails");
                AssertEqual(VesselHealthDetailCodes.VulnerablePackages, result.DetailCode);
                AssertEqual(1L, result.ValueA!.Value);
                AssertEqual((long)VulnerabilitySeverityEnum.High, result.ValueB!.Value);
                AssertEqual(VulnerabilitySeverityEnum.High, health.MaxVulnerabilitySeverity);
            }));

            cases.Add(Case("failed_dotnet_list_is_never_pass", "A failed dotnet list (restore, exit code, missing tool, timeout, garbage) is Unknown, never Pass", TestTags.Negative, () =>
            {
                List<DependencyToolResult> failures = new List<DependencyToolResult>
                {
                    Completed(1, VesselHealthJsonFixtures.DotnetRestoreFailed),
                    Completed(1, "", "error: No assets file was found for '/repo/obj/project.assets.json'. Run a NuGet package restore."),
                    Completed(1, "", "Could not execute because the specified command or file was not found."),
                    Completed(0, "this is not json"),
                    Completed(0, "{ \"version\": 1 }"),
                    new DependencyToolResult { Outcome = DependencyToolOutcomeEnum.ToolMissing },
                    new DependencyToolResult { Outcome = DependencyToolOutcomeEnum.TimedOut }
                };
                string[] expected = new string[]
                {
                    VesselHealthDetailCodes.RestoreRequired, VesselHealthDetailCodes.RestoreRequired, VesselHealthDetailCodes.ToolFailed,
                    VesselHealthDetailCodes.ParseError, VesselHealthDetailCodes.ParseError, VesselHealthDetailCodes.ToolMissing, VesselHealthDetailCodes.Timeout
                };

                for (int i = 0; i < failures.Count; i++)
                {
                    foreach (DependencyScanModeEnum mode in new DependencyScanModeEnum[] { DependencyScanModeEnum.Outdated, DependencyScanModeEnum.Vulnerable })
                    {
                        DependencyScanResult scan = DotnetListParser.Interpret(failures[i], mode, "/repo", 45);
                        AssertEqual(expected[i], scan.ErrorCode, "case " + i + " " + mode);
                        VesselHealthCriterionResult graded = mode == DependencyScanModeEnum.Outdated
                            ? DependenciesCriterion.Grade(scan, null)
                            : VulnerabilitiesCriterion.Grade(scan, null);
                        AssertEqual(VesselHealthStatusEnum.Unknown, graded.Status, "case " + i + " " + mode + " must be Unknown");
                        AssertNotEqual(VesselHealthStatusEnum.Pass, graded.Status);
                    }
                }

                AssertEqual(45L, DotnetListParser.Interpret(failures[6], DependencyScanModeEnum.Outdated, "/repo", 45).ErrorValue!.Value, "timeout value");
            }));

            cases.Add(Case("npm_outdated_exit_one_is_normal", "npm outdated exit code 1 with JSON parses normally", TestTags.Positive, () =>
            {
                DependencyScanResult scan = NpmOutputParser.InterpretOutdated(Completed(1, VesselHealthJsonFixtures.NpmOutdated), "web/package.json", 120);
                AssertNull(scan.ErrorCode, "no error");
                AssertEqual(2, scan.Dependencies.Count, "current-equals-latest entries are not outdated");
                AssertEqual(DependencyDriftEnum.Patch, scan.Dependencies.Single(d => d.PackageName == "lodash").Drift);
                AssertEqual(DependencyDriftEnum.Major, scan.Dependencies.Single(d => d.PackageName == "react").Drift);
                AssertEqual("web/package.json", scan.Dependencies[0].ProjectPath);
                AssertEqual("npm", scan.Dependencies[0].Ecosystem);

                DependencyScanResult empty = NpmOutputParser.InterpretOutdated(Completed(0, ""), "package.json", 120);
                AssertNull(empty.ErrorCode, "empty output with exit 0 means nothing outdated");
                AssertEqual(VesselHealthStatusEnum.Pass, DependenciesCriterion.Grade(empty, null).Status);
            }));

            cases.Add(Case("npm_audit_parses_fixture", "npm audit JSON parses severities and ignores the mixed via array", TestTags.Positive, () =>
            {
                DependencyScanResult scan = NpmOutputParser.InterpretAudit(Completed(1, VesselHealthJsonFixtures.NpmAudit), "package.json", 120);
                AssertNull(scan.ErrorCode, "no error");
                AssertEqual(2, scan.Dependencies.Count);
                AssertEqual(VulnerabilitySeverityEnum.Critical, scan.Dependencies.Single(d => d.PackageName == "minimist").Severity);
                AssertEqual(VulnerabilitySeverityEnum.Moderate, scan.Dependencies.Single(d => d.PackageName == "nth-check").Severity);
                AssertEqual(VesselHealthStatusEnum.Fail, VulnerabilitiesCriterion.Grade(scan, null).Status);

                DependencyScanResult moderate = new DependencyScanResult { HasTargets = true };
                moderate.Dependencies.Add(scan.Dependencies.Single(d => d.PackageName == "nth-check"));
                AssertEqual(VesselHealthStatusEnum.Warn, VulnerabilitiesCriterion.Grade(moderate, null).Status, "moderate warns");

                DependencyScanResult clean = NpmOutputParser.InterpretAudit(Completed(0, VesselHealthJsonFixtures.NpmAuditClean), "package.json", 120);
                AssertEqual(VesselHealthStatusEnum.Pass, VulnerabilitiesCriterion.Grade(clean, null).Status);
            }));

            cases.Add(Case("npm_failures_are_unknown", "npm error envelopes, missing npm, and garbage output are Unknown", TestTags.Negative, () =>
            {
                AssertEqual(VesselHealthDetailCodes.RestoreRequired, NpmOutputParser.InterpretAudit(Completed(1, VesselHealthJsonFixtures.NpmErrorNoLock), "package.json", 120).ErrorCode);
                AssertEqual(VesselHealthDetailCodes.RestoreRequired, NpmOutputParser.InterpretOutdated(Completed(1, VesselHealthJsonFixtures.NpmErrorNoLock), "package.json", 120).ErrorCode);
                AssertEqual(VesselHealthDetailCodes.ToolMissing, NpmOutputParser.InterpretOutdated(new DependencyToolResult { Outcome = DependencyToolOutcomeEnum.ToolMissing }, "package.json", 120).ErrorCode);
                AssertEqual(VesselHealthDetailCodes.ToolFailed, NpmOutputParser.InterpretOutdated(Completed(254, "", "npm ERR! something broke"), "package.json", 120).ErrorCode);
                AssertEqual(VesselHealthDetailCodes.ParseError, NpmOutputParser.InterpretAudit(Completed(0, "{ \"advisories\": {} }"), "package.json", 120).ErrorCode);
            }));

            cases.Add(Case("nuget_targets_honor_solutions_and_excludes", "NuGet targets: 1-5 solutions, else non-test projects; excluded directories skipped", TestTags.Positive, () =>
            {
                RepositoryFileInventory withSolution = RepositoryFileInventory.FromTrackedFiles(new List<string>
                {
                    "src/App.sln", "src/App/App.csproj", "node_modules/pkg/Bad.sln", "bin/Other.sln"
                }, new VesselImportSettings().ExcludedDirectoryNames);
                List<string> solutionTargets = DependencyScanner.ResolveNuGetTargets(withSolution, 5, 50);
                AssertEqual(1, solutionTargets.Count);
                AssertEqual("src/App.sln", solutionTargets[0]);

                List<string> manySolutions = Enumerable.Range(0, 6).Select(i => "s" + i + "/S" + i + ".sln").ToList();
                manySolutions.Add("src/Lib/Lib.csproj");
                manySolutions.Add("src/Lib.Tests/Lib.Tests.csproj");
                RepositoryFileInventory many = RepositoryFileInventory.FromTrackedFiles(manySolutions, null);
                List<string> projectTargets = DependencyScanner.ResolveNuGetTargets(many, 5, 50);
                AssertEqual(1, projectTargets.Count, "test project excluded (by name without content)");
                AssertEqual("src/Lib/Lib.csproj", projectTargets[0]);

                RepositoryFileInventory npm = RepositoryFileInventory.FromTrackedFiles(new List<string>
                {
                    "package.json", "package-lock.json", "tools/package.json", "web/package.json", "web/npm-shrinkwrap.json"
                }, null);
                List<string> npmTargets = DependencyScanner.ResolveNpmTargets(npm);
                AssertEqual(2, npmTargets.Count, "only package.json files with a lockfile");
                AssertTrue(npmTargets.Contains("package.json") && npmTargets.Contains("web/package.json"));
            }));

            cases.Add(CaseAsync("scanner_runs_tools_and_reports_failure", "The scanner runs dotnet per target, merges results, and a failed target keeps the scan Unknown", TestTags.Negative, async () =>
            {
                string root = TestTemp.NewDirectory("health_deps");
                Directory.CreateDirectory(Path.Combine(root, "a"));
                Directory.CreateDirectory(Path.Combine(root, "b"));
                File.WriteAllText(Path.Combine(root, "a", "A.sln"), "");
                File.WriteAllText(Path.Combine(root, "b", "B.sln"), "");
                FakeHostCommandExecutor executor = new FakeHostCommandExecutor();
                executor.Handler = request => request.Arguments.Any(a => a.EndsWith("A.sln", StringComparison.Ordinal))
                    ? FakeHostCommandExecutor.Result(0, VesselHealthJsonFixtures.DotnetOutdated)
                    : FakeHostCommandExecutor.Result(1, VesselHealthJsonFixtures.DotnetRestoreFailed);
                DependencyScanner scanner = new DependencyScanner(new DependencyToolRunner(executor));
                VesselHealthContext context = CreateContext(root);

                DependencyScanResult scan = await scanner.ScanAsync(context, DependencyScanModeEnum.Outdated).ConfigureAwait(false);
                AssertEqual(2, executor.Requests.Count);
                AssertEqual("dotnet", executor.Requests[0].Executable);
                AssertTrue(executor.Requests[0].Arguments.Contains("--outdated"));
                AssertTrue(executor.Requests[0].Arguments.Contains("json"));
                AssertEqual(120000, executor.Requests[0].TimeoutMs, "timeout comes from DependencyCommandTimeoutSeconds");
                AssertEqual(VesselHealthDetailCodes.RestoreRequired, scan.ErrorCode);
                AssertEqual(3, scan.Dependencies.Count, "dependencies from the successful target are kept");

                DependenciesCriterion criterion = new DependenciesCriterion(scanner);
                VesselHealthCriterionResult result = await criterion.EvaluateAsync(CreateContext(root)).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Unknown, result.Status, "partial failure is still Unknown");
            }));

            cases.Add(CaseAsync("scanner_missing_tool_is_unknown", "A missing dotnet executable grades ToolMissing", TestTags.Negative, async () =>
            {
                string root = TestTemp.NewDirectory("health_deps_missing");
                File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
                FakeHostCommandExecutor executor = new FakeHostCommandExecutor();
                executor.Handler = request => null;
                DependencyScanner scanner = new DependencyScanner(new DependencyToolRunner(executor));
                VesselHealthCriterionResult outdated = await new DependenciesCriterion(scanner).EvaluateAsync(CreateContext(root)).ConfigureAwait(false);
                VesselHealthCriterionResult vulnerable = await new VulnerabilitiesCriterion(scanner).EvaluateAsync(CreateContext(root)).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Unknown, outdated.Status);
                AssertEqual(VesselHealthDetailCodes.ToolMissing, outdated.DetailCode);
                AssertEqual(VesselHealthStatusEnum.Unknown, vulnerable.Status);
                AssertEqual(VesselHealthDetailCodes.ToolMissing, vulnerable.DetailCode);
            }));

            cases.Add(CaseAsync("scanner_npm_runs_in_package_directory", "npm commands run in the package directory and parse", TestTags.Positive, async () =>
            {
                string root = TestTemp.NewDirectory("health_npm");
                Directory.CreateDirectory(Path.Combine(root, "web"));
                File.WriteAllText(Path.Combine(root, "web", "package.json"), "{ \"name\": \"web\" }");
                File.WriteAllText(Path.Combine(root, "web", "package-lock.json"), "{}");
                FakeHostCommandExecutor executor = new FakeHostCommandExecutor();
                executor.Handler = request => request.Arguments.Contains("outdated")
                    ? FakeHostCommandExecutor.Result(1, VesselHealthJsonFixtures.NpmOutdated)
                    : FakeHostCommandExecutor.Result(1, VesselHealthJsonFixtures.NpmAudit);
                DependencyScanner scanner = new DependencyScanner(new DependencyToolRunner(executor));
                VesselHealthContext context = CreateContext(root);
                VesselHealthCriterionResult outdated = await new DependenciesCriterion(scanner).EvaluateAsync(context).ConfigureAwait(false);
                VesselHealthCriterionResult audit = await new VulnerabilitiesCriterion(scanner).EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(Path.Combine(root, "web"), executor.Requests[0].WorkingDirectory);
                AssertEqual(VesselHealthStatusEnum.Fail, outdated.Status);
                AssertEqual(2L, outdated.ValueA!.Value);
                AssertEqual(VesselHealthStatusEnum.Fail, audit.Status);
                AssertEqual(2, context.VulnerableDependencies.Count);
            }));

            cases.Add(Case("manifest_hash_tracks_manifest_changes", "The manifest hash changes with manifest content and ignores other files", TestTags.Positive, () =>
            {
                string root = TestTemp.NewDirectory("health_hash");
                File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project />");
                File.WriteAllText(Path.Combine(root, "Program.cs"), "class P {}");
                string? first = ManifestHasher.Compute(RepositoryFileInventory.FromDirectory(root, null));
                AssertNotNull(first, "hash");
                File.WriteAllText(Path.Combine(root, "Program.cs"), "class Q {}");
                AssertEqual(first, ManifestHasher.Compute(RepositoryFileInventory.FromDirectory(root, null)), "source edits do not change the hash");
                File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project Sdk=\"x\" />");
                AssertNotEqual(first, ManifestHasher.Compute(RepositoryFileInventory.FromDirectory(root, null)), "manifest edits change the hash");

                string empty = TestTemp.NewDirectory("health_hash_empty");
                File.WriteAllText(Path.Combine(empty, "notes.txt"), "x");
                AssertNull(ManifestHasher.Compute(RepositoryFileInventory.FromDirectory(empty, null)), "no manifests yields null");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Health Dependencies",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static DependencyToolResult Completed(int exitCode, string stdout, string stderr = "")
        {
            return new DependencyToolResult { Outcome = DependencyToolOutcomeEnum.Completed, ExitCode = exitCode, StandardOutput = stdout, StandardError = stderr };
        }

        private static VesselHealthContext CreateContext(string root)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            Vessel vessel = new Vessel("deps-test", "https://example.com/deps.git");
            vessel.TenantId = Constants.DefaultTenantId;
            VesselHealthContext context = new VesselHealthContext(vessel, Constants.DefaultTenantId, new RepositoryHealthSettings(), new VesselImportSettings().ExcludedDirectoryNames, new GitService(logging));
            context.EvaluatedPath = root;
            context.RepositoryAvailable = true;
            return context;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
