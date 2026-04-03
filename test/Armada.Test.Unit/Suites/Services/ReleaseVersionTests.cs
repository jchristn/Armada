namespace Armada.Test.Unit.Suites.Services
{
    using System.IO;
    using System.Text.RegularExpressions;
    using Armada.Core;
    using Armada.Test.Common;

    public class ReleaseVersionTests : TestSuite
    {
        public override string Name => "Release Version";

        protected override async Task RunTestsAsync()
        {
            await RunTest("Directory Build Props Is Canonical Release Version", () =>
            {
                string releaseVersion = GetCanonicalReleaseVersion();

                AssertEqual("0.5.0", releaseVersion, "Directory.Build.props release version");
                AssertEqual(releaseVersion, Constants.ProductVersion, "Constants.ProductVersion should match Directory.Build.props");
            });

            await RunTest("Release Artifacts Stay In Lockstep With Directory Build Props", () =>
            {
                string releaseVersion = GetCanonicalReleaseVersion();
                string constantsContents = ReadRepositoryFile("src", "Armada.Core", "Constants.cs");
                string helmProjectContents = ReadRepositoryFile("src", "Armada.Helm", "Armada.Helm.csproj");
                string dashboardPackageContents = ReadRepositoryFile("src", "Armada.Dashboard", "package.json");
                string composeContents = ReadRepositoryFile("docker", "compose.yaml");

                AssertSingleMatchGroupValue(constantsContents, @"ProductVersion\s*=\s*""([^""]+)""", releaseVersion, "Constants.ProductVersion should match Directory.Build.props");
                AssertSingleMatchGroupValue(helmProjectContents, @"<Version>\s*([^<]+)\s*</Version>", releaseVersion, "Armada.Helm.csproj version should match Directory.Build.props");
                AssertSingleMatchGroupValue(dashboardPackageContents, @"""version""\s*:\s*""([^""]+)""", releaseVersion, "Armada.Dashboard/package.json version should match Directory.Build.props");

                MatchCollection composeVersionMatches = Regex.Matches(composeContents, @"jchristn77/armada-(?:server|dashboard):v([0-9]+\.[0-9]+\.[0-9]+)");
                AssertEqual(2, composeVersionMatches.Count, "docker/compose.yaml should pin both Armada images");
                foreach (Match composeVersionMatch in composeVersionMatches)
                {
                    AssertEqual(releaseVersion, composeVersionMatch.Groups[1].Value, "docker/compose.yaml image tag should match Directory.Build.props");
                }
            });

            await RunTest("Docs And Postman Samples Stay In Lockstep With Directory Build Props", () =>
            {
                string releaseVersion = GetCanonicalReleaseVersion();
                string restApiContents = ReadRepositoryFile("docs", "REST_API.md");
                string mcpApiContents = ReadRepositoryFile("docs", "MCP_API.md");
                string postmanContents = ReadRepositoryFile("Armada.postman_collection.json");

                AssertSingleMatchGroupValue(restApiContents, @"\*\*Version:\*\*\s*([0-9]+\.[0-9]+\.[0-9]+)", releaseVersion, "docs/REST_API.md version header should match Directory.Build.props");
                AssertSingleMatchGroupValue(mcpApiContents, @"\*\*Version:\*\*\s*([0-9]+\.[0-9]+\.[0-9]+)", releaseVersion, "docs/MCP_API.md version header should match Directory.Build.props");
                AssertSingleMatchGroupValue(postmanContents, @"Version:\s*([0-9]+\.[0-9]+\.[0-9]+)", releaseVersion, "Armada.postman_collection.json description version should match Directory.Build.props");
                AssertSingleMatchGroupValue(restApiContents, @"#### GET /api/v1/status/health[\s\S]*?""Version"":\s*""([^""]+)""", releaseVersion, "docs/REST_API.md health response sample should match Directory.Build.props");
                AssertAllMatchGroupValues(postmanContents, "\\\\\"Version\\\\\":\\s*\\\\\"([^\\\\\"]+)\\\\\"", releaseVersion, "Armada.postman_collection.json response bodies should use the canonical release version");
            });

            await RunTest("Embedded Version Match Helper Accepts Multiple Canonical Matches", () =>
            {
                const string escapedVersionPattern = "\\\\\"Version\\\\\":\\s*\\\\\"([^\\\\\"]+)\\\\\"";
                string sampleContents = "\\\"Version\\\": \\\"0.5.0\\\"\\n\\\"Version\\\": \\\"0.5.0\\\"";

                AssertAllMatchGroupValues(sampleContents, escapedVersionPattern, "0.5.0", "Escaped sample versions");
            });

            await RunTest("Version Match Helpers Reject Missing Duplicate And Stale Versions", () =>
            {
                const string versionPattern = @"Version:\s*([0-9]+\.[0-9]+\.[0-9]+)";
                const string escapedVersionPattern = "\\\\\"Version\\\\\":\\s*\\\\\"([^\\\\\"]+)\\\\\"";

                AssertThrows<Exception>(() => AssertSingleMatchGroupValue("No release version here", versionPattern, "0.5.0", "Release version sample"), "AssertSingleMatchGroupValue should reject missing matches");
                AssertThrows<Exception>(() => AssertSingleMatchGroupValue("Version: 0.5.0\nVersion: 0.5.0", versionPattern, "0.5.0", "Release version sample"), "AssertSingleMatchGroupValue should reject duplicate matches");
                AssertThrows<Exception>(() => AssertAllMatchGroupValues("\\\"Version\\\": \\\"0.5.0\\\"\\n\\\"Version\\\": \\\"0.4.0\\\"", escapedVersionPattern, "0.5.0", "Escaped sample versions"), "AssertAllMatchGroupValues should reject stale embedded versions");
            });

            await RunTest("Helm Program Uses ProductVersion Constant", () =>
            {
                string programContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Helm", "Program.cs"));

                AssertContains("\"v\" + Constants.ProductVersion", programContents, "Helm banner/help version should come from Constants.ProductVersion");
                AssertContains("AnsiConsole.MarkupLine(\"[dim]Multi-Agent Orchestration System  \" + _VersionLabel + \"[/]\");", programContents, "Helm subtitle should render the shared version label");
                AssertContains("config.SetApplicationVersion(Constants.ProductVersion);", programContents, "Helm CLI version should come from Constants.ProductVersion");
                AssertFalse(programContents.Contains("0.3.0"), "Helm entry point should not contain the stale 0.3.0 literal");
                AssertFalse(programContents.Contains("\"0.5.0\""), "Helm entry point should not contain a hard-coded release version literal");
                AssertFalse(programContents.Contains("\"v0.5.0\""), "Helm entry point should compose the prefixed version instead of hard-coding it");
                AssertFalse(programContents.Contains("SetApplicationVersion(\"0.5.0\")"), "Helm CLI version should not be hard-coded");
            });
        }

        private string GetCanonicalReleaseVersion()
        {
            string propsContents = ReadRepositoryFile("src", "Directory.Build.props");
            MatchCollection versionMatches = Regex.Matches(propsContents, @"<Version>\s*([^<]+)\s*</Version>");

            AssertEqual(1, versionMatches.Count, "Directory.Build.props should contain exactly one Version element");
            return versionMatches[0].Groups[1].Value.Trim();
        }

        private string ReadRepositoryFile(params string[] relativePathParts)
        {
            string path = FindRepositoryRoot();
            foreach (string relativePathPart in relativePathParts)
            {
                path = Path.Combine(path, relativePathPart);
            }

            return File.ReadAllText(path);
        }

        private void AssertSingleMatchGroupValue(string contents, string pattern, string expectedValue, string label)
        {
            MatchCollection matches = Regex.Matches(contents, pattern);
            AssertEqual(1, matches.Count, label + " should produce exactly one version match");
            AssertEqual(expectedValue, matches[0].Groups[1].Value.Trim(), label);
        }

        private void AssertAllMatchGroupValues(string contents, string pattern, string expectedValue, string label)
        {
            MatchCollection matches = Regex.Matches(contents, pattern);
            AssertTrue(matches.Count > 0, label + " should produce at least one version match");
            foreach (Match match in matches)
            {
                AssertEqual(expectedValue, match.Groups[1].Value.Trim(), label);
            }
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                    Directory.Exists(Path.Combine(current.FullName, "test")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
        }
    }
}
