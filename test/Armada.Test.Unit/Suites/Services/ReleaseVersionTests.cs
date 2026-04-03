namespace Armada.Test.Unit.Suites.Services
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core;
    using Armada.Test.Common;

    public class ReleaseVersionTests : TestSuite
    {
        private const string SemanticVersionPattern = @"([0-9]+\.[0-9]+\.[0-9]+)";

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
                AssertEqual(
                    expectedVersion,
                    GetSingleRegexMatchValue(
                        helmProjectContents,
                        @"<Version>\s*" + SemanticVersionPattern + @"\s*</Version>",
                        "Armada.Helm.csproj should contain exactly one package Version element"),
                    "Armada.Helm package version should match Constants.ProductVersion");

                string composeContents = File.ReadAllText(Path.Combine(repositoryRoot, "docker", "compose.yaml"));
                AssertEqual(
                    expectedVersion,
                    GetDockerServiceImageVersion(composeContents, "armada-server"),
                    "armada-server image tag should match Constants.ProductVersion");
                AssertEqual(
                    expectedVersion,
                    GetDockerServiceImageVersion(composeContents, "armada-dashboard"),
                    "armada-dashboard image tag should match Constants.ProductVersion");

                string postmanCollectionContents = File.ReadAllText(Path.Combine(repositoryRoot, "Armada.postman_collection.json"));
                AssertEqual(
                    expectedVersion,
                    GetPostmanCollectionVersion(
                        postmanCollectionContents,
                        "Armada.postman_collection.json should contain exactly one collection description version line"),
                    "Postman collection version should match Constants.ProductVersion");

                string restApiContents = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "REST_API.md"));
                AssertEqual(
                    expectedVersion,
                    GetMarkdownVersion(
                        restApiContents,
                        "docs/REST_API.md should contain exactly one top-level version header"),
                    "REST API reference version should match Constants.ProductVersion");

                string mcpApiContents = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "MCP_API.md"));
                AssertEqual(
                    expectedVersion,
                    GetMarkdownVersion(
                        mcpApiContents,
                        "docs/MCP_API.md should contain exactly one top-level version header"),
                    "MCP API reference version should match Constants.ProductVersion");
            });

            await RunTest("Release Surface Parsers Tolerate Formatting And Reject Invalid Layouts", () =>
            {
                AssertEqual(
                    "0.5.0",
                    GetSingleRegexMatchValue(
                        "<Version>\n  0.5.0\n</Version>",
                        @"<Version>\s*" + SemanticVersionPattern + @"\s*</Version>",
                        "XML sample should contain exactly one Version element"),
                    "XML version parsing should ignore harmless whitespace");

                AssertEqual(
                    "0.5.0",
                    GetDockerServiceImageVersion(
                        "services:\n  armada-server:\n    ports:\n      - \"7890:7890\"\n    image: \"registry.example.com/armada-server:v0.5.0\"   # pinned\n",
                        "armada-server"),
                    "Compose parsing should allow quoted image lines and trailing comments");

                AssertEqual(
                    "0.5.0",
                    GetPostmanCollectionVersion(
                        "{\"info\":{\"description\":\"Overview\\n\\nVersion: 0.5.0\\n\\nNotes\"}}",
                        "Postman sample should contain exactly one collection description version line"),
                    "Postman parsing should locate the version line within the description");

                AssertEqual(
                    "0.5.0",
                    GetMarkdownVersion(
                        "# Sample Reference\n\n**Version:** 0.5.0   \n",
                        "Markdown sample should contain exactly one top-level version header"),
                    "Markdown version parsing should ignore trailing whitespace");

                AssertThrows<Exception>(
                    () => GetSingleRegexMatchValue(
                        "<Version>0.5.0</Version>\n<Version>0.5.1</Version>",
                        @"<Version>\s*" + SemanticVersionPattern + @"\s*</Version>",
                        "XML sample should contain exactly one Version element"),
                    "XML version parsing should reject duplicate Version elements");

                AssertThrows<Exception>(
                    () => GetDockerServiceImageVersion(
                        "services:\n  armada-server:\n    image: repo:v0.5.0\n    image: repo:v0.5.1\n",
                        "armada-server"),
                    "Compose parsing should reject multiple image tags for one service");

                AssertThrows<Exception>(
                    () => AssertEqual(
                        Constants.ProductVersion,
                        GetMarkdownVersion(
                            "# Sample Reference\n\n**Version:** 0.5.1\n",
                            "Markdown sample should contain exactly one top-level version header"),
                        "Markdown version should match Constants.ProductVersion"),
                    "Markdown version parsing should surface version mismatches");

                AssertThrows<Exception>(
                    () => GetPostmanCollectionVersion(
                        "{\"info\":{\"description\":\"Overview only\"}}",
                        "Postman sample should contain exactly one collection description version line"),
                    "Postman parsing should reject missing version lines");
            });
        }

        private string GetPostmanCollectionVersion(string collectionContents, string countMessage)
        {
            using JsonDocument postmanCollection = JsonDocument.Parse(collectionContents);
            string postmanDescription = postmanCollection.RootElement.GetProperty("info").GetProperty("description").GetString() ?? string.Empty;

            return GetSingleRegexMatchValue(
                postmanDescription,
                @"^Version:\s*" + SemanticVersionPattern + @"\s*$",
                countMessage,
                RegexOptions.Multiline);
        }

        private string GetMarkdownVersion(string contents, string countMessage)
        {
            return GetSingleRegexMatchValue(
                contents,
                @"^\*\*Version:\*\*\s*" + SemanticVersionPattern + @"\s*$",
                countMessage,
                RegexOptions.Multiline);
        }

        private string GetDockerServiceImageVersion(string composeContents, string serviceName)
        {
            string[] lines = Regex.Split(composeContents, @"\r?\n");
            int serviceIndex = -1;
            int serviceIndent = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().Equals(serviceName + ":", StringComparison.Ordinal))
                {
                    serviceIndex = i;
                    serviceIndent = CountLeadingWhitespace(lines[i]);
                    break;
                }
            }

            AssertTrue(serviceIndex >= 0, "docker/compose.yaml should define the " + serviceName + " service");

            string? version = null;
            int imageLineCount = 0;
            for (int i = serviceIndex + 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int lineIndent = CountLeadingWhitespace(line);
                if (lineIndent <= serviceIndent)
                {
                    break;
                }

                string trimmedLine = line.Trim();
                if (!trimmedLine.StartsWith("image:", StringComparison.Ordinal))
                {
                    continue;
                }

                imageLineCount++;
                version = GetSingleRegexMatchValue(
                    trimmedLine,
                    @"^image:\s+[""']?.+:v" + SemanticVersionPattern + @"[""']?\s*(?:#.*)?$",
                    "docker/compose.yaml should define a single v-prefixed semver image tag for " + serviceName);
            }

            AssertTrue(imageLineCount == 1, "docker/compose.yaml should contain exactly one " + serviceName + " image tag");
            return version ?? string.Empty;
        }

        private string GetSingleRegexMatchValue(
            string contents,
            string pattern,
            string countMessage,
            RegexOptions options = RegexOptions.None)
        {
            MatchCollection matches = Regex.Matches(contents, pattern, options);
            AssertTrue(matches.Count == 1, countMessage);
            return matches[0].Groups[1].Value.Trim();
        }

        private static int CountLeadingWhitespace(string value)
        {
            int count = 0;
            while (count < value.Length && char.IsWhiteSpace(value[count]))
            {
                count++;
            }

            return count;
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
