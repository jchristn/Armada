namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Services.Health;
    using Armada.Core.Settings;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for repository-shape detection: per-ecosystem test infrastructure detectors (.NET, Node, Python,
    /// Go, Rust) over fixture repositories, grading against the latest test check run, CI configuration, license and
    /// readme detection, primary language, and the exclude list.
    /// </summary>
    public sealed class VesselHealthTestInfraSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.VesselHealthTestInfra";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("dotnet_test_project_detected", ".NET: a project referencing xunit is a test project", TestTags.Positive, () =>
            {
                string root = Fixture(new Dictionary<string, string>
                {
                    { "src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Serilog\" Version=\"3.0.0\" /></ItemGroup></Project>" },
                    { "src/App.Tests/App.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"xunit.v3\" Version=\"1.0.0\" /></ItemGroup></Project>" },
                    { "src/App/Program.cs", "class P {}" }
                });
                TestDetectionResult detection = Detect(root);
                AssertEqual(2, detection.RecognizedProjects);
                AssertEqual(1, detection.TestIndicators);
                AssertTrue(detection.EcosystemsWithTests.Contains("DotNet"));
                AssertEqual("CSharp", PrimaryLanguageDetector.Detect(RepositoryFileInventory.FromDirectory(root, null)));
            }));

            cases.Add(Case("dotnet_touchstone_and_sdk_detected", ".NET: Touchstone and Microsoft.NET.Test.Sdk count; plain projects do not", TestTags.Positive, () =>
            {
                AssertTrue(TestInfrastructureDetector.IsDotNetTestProject("T.csproj", "<PackageReference Include=\"Touchstone.Core\" />"));
                AssertTrue(TestInfrastructureDetector.IsDotNetTestProject("T.csproj", "<PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17\" />"));
                AssertTrue(TestInfrastructureDetector.IsDotNetTestProject("T.csproj", "<PackageReference Include=\"MSTest.TestFramework\" />"));
                AssertTrue(TestInfrastructureDetector.IsDotNetTestProject("T.csproj", "<PackageReference Include=\"NUnit\" />"));
                AssertTrue(TestInfrastructureDetector.IsDotNetTestProject("T.csproj", "<PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup>"));
                AssertFalse(TestInfrastructureDetector.IsDotNetTestProject("Tester.csproj", "<PackageReference Include=\"Newtonsoft.Json\" />"));
            }));

            cases.Add(Case("dotnet_without_tests_fails", ".NET without test projects grades Fail (NoTestsFound)", TestTags.Negative, () =>
            {
                string root = Fixture(new Dictionary<string, string> { { "App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />" } });
                VesselHealthCriterionResult result = TestInfrastructureCriterion.Grade(Detect(root), null);
                AssertEqual(VesselHealthStatusEnum.Fail, result.Status);
                AssertEqual(VesselHealthDetailCodes.NoTestsFound, result.DetailCode);
                AssertEqual(1L, result.ValueA!.Value);
            }));

            cases.Add(Case("node_jest_detected_placeholder_ignored", "Node: jest in devDependencies counts; npm's placeholder test script does not", TestTags.Positive, () =>
            {
                string withJest = Fixture(new Dictionary<string, string> { { "package.json", "{ \"name\": \"a\", \"devDependencies\": { \"jest\": \"^29.0.0\" } }" } });
                AssertEqual(1, Detect(withJest).TestIndicators);

                string withScript = Fixture(new Dictionary<string, string> { { "package.json", "{ \"name\": \"b\", \"scripts\": { \"test\": \"node --test\" } }" } });
                AssertEqual(1, Detect(withScript).TestIndicators);

                string placeholder = Fixture(new Dictionary<string, string> { { "package.json", "{ \"name\": \"c\", \"scripts\": { \"test\": \"echo \\\"Error: no test specified\\\" && exit 1\" } }" } });
                TestDetectionResult detection = Detect(placeholder);
                AssertEqual(1, detection.RecognizedProjects);
                AssertEqual(0, detection.TestIndicators);

                string playwright = Fixture(new Dictionary<string, string> { { "package.json", "{ \"devDependencies\": { \"@playwright/test\": \"1.0.0\" } }" } });
                AssertEqual(1, Detect(playwright).TestIndicators);
            }));

            cases.Add(Case("node_modules_excluded", "package.json files under node_modules are never scanned", TestTags.Positive, () =>
            {
                string root = Fixture(new Dictionary<string, string>
                {
                    { "package.json", "{ \"name\": \"app\" }" },
                    { "node_modules/jest/package.json", "{ \"name\": \"jest\", \"devDependencies\": { \"mocha\": \"1\" } }" }
                });
                RepositoryFileInventory inventory = RepositoryFileInventory.FromDirectory(root, new VesselImportSettings().ExcludedDirectoryNames);
                AssertEqual(1, inventory.FindByFileName("package.json").Count);
                TestDetectionResult detection = TestInfrastructureDetector.Detect(inventory);
                AssertEqual(0, detection.TestIndicators);
            }));

            cases.Add(Case("python_detectors", "Python: pytest.ini, [tool.pytest] in pyproject, or a tests directory", TestTags.Positive, () =>
            {
                AssertTrue(Detect(Fixture(new Dictionary<string, string> { { "setup.py", "" }, { "pytest.ini", "[pytest]" } })).TestIndicators > 0);
                AssertTrue(Detect(Fixture(new Dictionary<string, string> { { "pyproject.toml", "[tool.pytest.ini_options]\nminversion = \"6.0\"" } })).TestIndicators > 0);
                AssertTrue(Detect(Fixture(new Dictionary<string, string> { { "requirements.txt", "requests" }, { "tests/test_app.py", "def test_x(): pass" } })).TestIndicators > 0);
                TestDetectionResult none = Detect(Fixture(new Dictionary<string, string> { { "pyproject.toml", "[project]\nname = \"x\"" }, { "app.py", "" } }));
                AssertEqual(1, none.RecognizedProjects);
                AssertEqual(0, none.TestIndicators);
            }));

            cases.Add(Case("go_detector", "Go: any *_test.go file", TestTags.Positive, () =>
            {
                TestDetectionResult with = Detect(Fixture(new Dictionary<string, string> { { "go.mod", "module x" }, { "main.go", "package main" }, { "pkg/a_test.go", "package pkg" } }));
                AssertEqual(1, with.TestIndicators);
                AssertTrue(with.EcosystemsWithTests.Contains("Go"));
                TestDetectionResult without = Detect(Fixture(new Dictionary<string, string> { { "go.mod", "module x" }, { "main.go", "package main" } }));
                AssertEqual(0, without.TestIndicators);
            }));

            cases.Add(Case("rust_detectors", "Rust: a tests directory or #[cfg(test)] in source", TestTags.Positive, () =>
            {
                AssertEqual(1, Detect(Fixture(new Dictionary<string, string> { { "Cargo.toml", "[package]" }, { "tests/it.rs", "#[test] fn a() {}" } })).TestIndicators);
                AssertEqual(1, Detect(Fixture(new Dictionary<string, string> { { "Cargo.toml", "[package]" }, { "src/lib.rs", "#[cfg(test)]\nmod tests {}" } })).TestIndicators);
                AssertEqual(0, Detect(Fixture(new Dictionary<string, string> { { "Cargo.toml", "[package]" }, { "src/main.rs", "fn main() {}" } })).TestIndicators);
            }));

            cases.Add(Case("no_recognized_project_not_applicable", "A repository without a recognized project grades NotApplicable", TestTags.Positive, () =>
            {
                string root = Fixture(new Dictionary<string, string> { { "README.md", "# docs" }, { "notes/a.txt", "x" } });
                VesselHealthCriterionResult result = TestInfrastructureCriterion.Grade(Detect(root), CheckRunStatusEnum.Passed);
                AssertEqual(VesselHealthStatusEnum.NotApplicable, result.Status);
                AssertEqual(VesselHealthDetailCodes.NoRecognizedProject, result.DetailCode);
            }));

            cases.Add(Case("grading_uses_latest_check_run", "Tests found: passing run Pass, failed run Warn, no run Warn", TestTags.Positive, () =>
            {
                TestDetectionResult detection = new TestDetectionResult { RecognizedProjects = 2, TestIndicators = 1 };
                VesselHealthCriterionResult passed = TestInfrastructureCriterion.Grade(detection, CheckRunStatusEnum.Passed);
                AssertEqual(VesselHealthStatusEnum.Pass, passed.Status);
                AssertEqual(VesselHealthDetailCodes.TestsPassing, passed.DetailCode);
                VesselHealthCriterionResult failed = TestInfrastructureCriterion.Grade(detection, CheckRunStatusEnum.Failed);
                AssertEqual(VesselHealthStatusEnum.Warn, failed.Status);
                AssertEqual(VesselHealthDetailCodes.LastTestRunFailed, failed.DetailCode);
                VesselHealthCriterionResult none = TestInfrastructureCriterion.Grade(detection, null);
                AssertEqual(VesselHealthStatusEnum.Warn, none.Status);
                AssertEqual(VesselHealthDetailCodes.NoTestRun, none.DetailCode);
            }));

            cases.Add(Case("ci_license_readme_detection", "CI configuration, license, and readme are detected", TestTags.Positive, () =>
            {
                RepositoryFileInventory inventory = RepositoryFileInventory.FromTrackedFiles(new List<string>
                {
                    ".github/workflows/build.yml", ".github/workflows/notes.md", "azure-pipelines.yml", "LICENSE.md", "README.md", "docs/README.md"
                }, new VesselImportSettings().ExcludedDirectoryNames);
                AssertEqual(2, ContinuousIntegrationCriterion.FindCiFiles(inventory).Count, ".github is scanned even though it starts with a dot");

                RepositoryFileInventory none = RepositoryFileInventory.FromTrackedFiles(new List<string> { "src/a.cs", ".circleci/config.yml" }, null);
                AssertEqual(0, ContinuousIntegrationCriterion.FindCiFiles(none).Count);
            }));

            cases.Add(CaseAsync("ci_criterion_writes_columns", "ContinuousIntegration writes HasCiConfig, HasLicense, and HasReadme", TestTags.Positive, async () =>
            {
                string root = Fixture(new Dictionary<string, string> { { "Jenkinsfile", "pipeline {}" }, { "COPYING", "GPL" }, { "src/README.txt", "nested readme does not count" } });
                Armada.Core.Models.Vessel vessel = new Armada.Core.Models.Vessel("ci", "https://example.com/ci.git");
                SyslogLogging.LoggingModule logging = new SyslogLogging.LoggingModule();
                logging.Settings.EnableConsole = false;
                VesselHealthContext context = new VesselHealthContext(vessel, Armada.Core.Constants.DefaultTenantId, new RepositoryHealthSettings(), null, new Armada.Core.Services.GitService(logging));
                context.EvaluatedPath = root;
                context.RepositoryAvailable = true;
                VesselHealthCriterionResult result = await new ContinuousIntegrationCriterion().EvaluateAsync(context).ConfigureAwait(false);
                AssertEqual(VesselHealthStatusEnum.Pass, result.Status);
                AssertEqual(VesselHealthDetailCodes.CiConfigured, result.DetailCode);
                AssertEqual(true, context.Health.HasCiConfig);
                AssertEqual(true, context.Health.HasLicense);
                AssertEqual(false, context.Health.HasReadme);
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Vessel Health Test Infrastructure",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string Fixture(Dictionary<string, string> files)
        {
            string root = TestTemp.NewDirectory("health_shape");
            foreach (KeyValuePair<string, string> file in files)
            {
                string full = Path.Combine(root, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, file.Value);
            }

            return root;
        }

        private static TestDetectionResult Detect(string root)
        {
            return TestInfrastructureDetector.Detect(RepositoryFileInventory.FromDirectory(root, new VesselImportSettings().ExcludedDirectoryNames));
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
