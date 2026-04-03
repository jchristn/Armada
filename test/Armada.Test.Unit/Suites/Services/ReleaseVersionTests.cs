namespace Armada.Test.Unit.Suites.Services
{
    using System.IO;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core;
    using Armada.Test.Common;

    public class ReleaseVersionTests : TestSuite
    {
        public override string Name => "Release Version";

        protected override async Task RunTestsAsync()
        {
            await RunTest("ProductVersion And Shared Build Props Match V050", () =>
            {
                string propsContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Directory.Build.props"));
                MatchCollection versionMatches = Regex.Matches(propsContents, @"<Version>\s*([^<]+)\s*</Version>");

                AssertTrue(versionMatches.Count == 1, "Directory.Build.props should contain exactly one Version element");
                Match versionMatch = versionMatches[0];
                AssertEqual("0.5.0", Constants.ProductVersion);
                AssertEqual(Constants.ProductVersion, versionMatch.Groups[1].Value.Trim());
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

            await RunTest("Release Surface Files Match ProductVersion", () =>
            {
                string repositoryRoot = FindRepositoryRoot();
                string expectedVersion = Constants.ProductVersion;

                string helmProjectContents = File.ReadAllText(Path.Combine(repositoryRoot, "src", "Armada.Helm", "Armada.Helm.csproj"));
                AssertSingleVersionMatch(
                    helmProjectContents,
                    @"<Version>\s*([^<]+)\s*</Version>",
                    expectedVersion,
                    "Armada.Helm.csproj should contain exactly one package Version element",
                    "Armada.Helm package version should match Constants.ProductVersion");

                string composeContents = File.ReadAllText(Path.Combine(repositoryRoot, "docker", "compose.yaml"));
                AssertSingleVersionMatch(
                    composeContents,
                    @"^\s*armada-server:\s*$.*?^\s*image:\s+\S+:v([0-9]+\.[0-9]+\.[0-9]+)\s*$",
                    expectedVersion,
                    "docker/compose.yaml should contain exactly one armada-server image tag",
                    "armada-server image tag should match Constants.ProductVersion",
                    RegexOptions.Multiline | RegexOptions.Singleline);
                AssertSingleVersionMatch(
                    composeContents,
                    @"^\s*armada-dashboard:\s*$.*?^\s*image:\s+\S+:v([0-9]+\.[0-9]+\.[0-9]+)\s*$",
                    expectedVersion,
                    "docker/compose.yaml should contain exactly one armada-dashboard image tag",
                    "armada-dashboard image tag should match Constants.ProductVersion",
                    RegexOptions.Multiline | RegexOptions.Singleline);

                using JsonDocument postmanCollection = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "Armada.postman_collection.json")));
                string postmanDescription = postmanCollection.RootElement.GetProperty("info").GetProperty("description").GetString() ?? string.Empty;
                AssertSingleVersionMatch(
                    postmanDescription,
                    @"^Version:\s*([0-9]+\.[0-9]+\.[0-9]+)\s*$",
                    expectedVersion,
                    "Armada.postman_collection.json should contain exactly one collection description version line",
                    "Postman collection version should match Constants.ProductVersion",
                    RegexOptions.Multiline);

                string restApiContents = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "REST_API.md"));
                AssertSingleVersionMatch(
                    restApiContents,
                    @"^\*\*Version:\*\*\s*([0-9]+\.[0-9]+\.[0-9]+)\s*$",
                    expectedVersion,
                    "docs/REST_API.md should contain exactly one top-level version header",
                    "REST API reference version should match Constants.ProductVersion",
                    RegexOptions.Multiline);

                string mcpApiContents = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "MCP_API.md"));
                AssertSingleVersionMatch(
                    mcpApiContents,
                    @"^\*\*Version:\*\*\s*([0-9]+\.[0-9]+\.[0-9]+)\s*$",
                    expectedVersion,
                    "docs/MCP_API.md should contain exactly one top-level version header",
                    "MCP API reference version should match Constants.ProductVersion",
                    RegexOptions.Multiline);
            });
        }

        private void AssertSingleVersionMatch(
            string contents,
            string pattern,
            string expectedVersion,
            string countMessage,
            string mismatchMessage,
            RegexOptions options = RegexOptions.None)
        {
            MatchCollection matches = Regex.Matches(contents, pattern, options);
            AssertTrue(matches.Count == 1, countMessage);
            AssertEqual(expectedVersion, matches[0].Groups[1].Value.Trim(), mismatchMessage);
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
