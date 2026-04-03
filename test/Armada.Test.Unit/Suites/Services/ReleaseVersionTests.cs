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
            await RunTest("Release artifacts stay in lockstep with Directory.Build.props", () =>
            {
                string canonicalVersion = GetCanonicalVersion();

                AssertEqual(canonicalVersion, Constants.ProductVersion, "Constants.ProductVersion should match Directory.Build.props");
                AssertEqual(
                    canonicalVersion,
                    ExtractSingleMatch(
                        ReadRepositoryFile("src", "Armada.Helm", "Armada.Helm.csproj"),
                        @"<Version>\s*([^<]+)\s*</Version>",
                        "Armada.Helm.csproj should contain exactly one Version element"),
                    "Armada.Helm.csproj version should match Directory.Build.props");
                AssertEqual(
                    canonicalVersion,
                    ExtractSingleMatch(
                        ReadRepositoryFile("src", "Armada.Dashboard", "package.json"),
                        @"""version""\s*:\s*""([^""]+)""",
                        "Armada.Dashboard/package.json should contain exactly one version field"),
                    "Armada.Dashboard/package.json version should match Directory.Build.props");
                AssertEqual(
                    canonicalVersion,
                    ExtractSingleMatch(
                        ReadRepositoryFile("docs", "REST_API.md"),
                        @"\*\*Version:\*\*\s*([0-9]+\.[0-9]+\.[0-9]+)",
                        "docs/REST_API.md should contain exactly one version header"),
                    "docs/REST_API.md version header should match Directory.Build.props");
                AssertEqual(
                    canonicalVersion,
                    ExtractSingleMatch(
                        ReadRepositoryFile("docs", "MCP_API.md"),
                        @"\*\*Version:\*\*\s*([0-9]+\.[0-9]+\.[0-9]+)",
                        "docs/MCP_API.md should contain exactly one version header"),
                    "docs/MCP_API.md version header should match Directory.Build.props");
                AssertEqual(
                    canonicalVersion,
                    ExtractSingleMatch(
                        ReadRepositoryFile("docker", "compose.yaml"),
                        @"image:\s+jchristn77/armada-server:v([0-9]+\.[0-9]+\.[0-9]+)",
                        "docker/compose.yaml should pin the armada-server image tag"),
                    "docker/compose.yaml armada-server image tag should match Directory.Build.props");
                AssertEqual(
                    canonicalVersion,
                    ExtractSingleMatch(
                        ReadRepositoryFile("docker", "compose.yaml"),
                        @"image:\s+jchristn77/armada-dashboard:v([0-9]+\.[0-9]+\.[0-9]+)",
                        "docker/compose.yaml should pin the armada-dashboard image tag"),
                    "docker/compose.yaml armada-dashboard image tag should match Directory.Build.props");

                using JsonDocument postmanDocument = JsonDocument.Parse(ReadRepositoryFile("Armada.postman_collection.json"));
                string? description = postmanDocument.RootElement.GetProperty("info").GetProperty("description").GetString();
                AssertNotNull(description, "Armada.postman_collection.json info.description");
                AssertEqual(
                    canonicalVersion,
                    ExtractSingleMatch(
                        description!,
                        @"Version:\s*([0-9]+\.[0-9]+\.[0-9]+)",
                        "Armada.postman_collection.json info.description should contain exactly one Version line"),
                    "Armada.postman_collection.json description version should match Directory.Build.props");
            });

            await RunTest("Embedded examples stay in lockstep with Directory.Build.props", () =>
            {
                string canonicalVersion = GetCanonicalVersion();

                AssertEqual(
                    canonicalVersion,
                    ExtractSingleMatch(
                        ReadRepositoryFile("docs", "REST_API.md"),
                        @"#### GET /api/v1/status/health.*?```json\s*\{.*?""Version"":\s*""([0-9]+\.[0-9]+\.[0-9]+)""",
                        "docs/REST_API.md health example should contain exactly one Version value",
                        RegexOptions.Singleline),
                    "docs/REST_API.md health example version should match Directory.Build.props");

                List<string> postmanExampleVersions = ExtractPostmanExampleVersions();
                AssertTrue(postmanExampleVersions.Count > 0, "Armada.postman_collection.json should include at least one example body with a Version field");
                foreach (string exampleVersion in postmanExampleVersions)
                {
                    AssertEqual(canonicalVersion, exampleVersion, "Armada.postman_collection.json example body version should match Directory.Build.props");
                }
            });
        }

        private string GetCanonicalVersion()
        {
            return ExtractSingleMatch(
                ReadRepositoryFile("src", "Directory.Build.props"),
                @"<Version>\s*([^<]+)\s*</Version>",
                "src/Directory.Build.props should contain exactly one Version element");
        }

        private string ReadRepositoryFile(params string[] pathSegments)
        {
            string[] fullPathSegments = new string[pathSegments.Length + 1];
            fullPathSegments[0] = FindRepositoryRoot();
            Array.Copy(pathSegments, 0, fullPathSegments, 1, pathSegments.Length);
            return File.ReadAllText(Path.Combine(fullPathSegments));
        }

        private string ExtractSingleMatch(string contents, string pattern, string failureMessage, RegexOptions options = RegexOptions.None)
        {
            MatchCollection matches = Regex.Matches(contents, pattern, options);
            AssertEqual(1, matches.Count, failureMessage);
            return matches[0].Groups[1].Value.Trim();
        }

        private List<string> ExtractPostmanExampleVersions()
        {
            using JsonDocument document = JsonDocument.Parse(ReadRepositoryFile("Armada.postman_collection.json"));
            List<string> versions = new List<string>();
            CollectPostmanExampleVersions(document.RootElement, versions);
            return versions;
        }

        private static void CollectPostmanExampleVersions(JsonElement element, List<string> versions)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (element.TryGetProperty("response", out JsonElement responses) && responses.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement response in responses.EnumerateArray())
                {
                    if (response.TryGetProperty("body", out JsonElement bodyElement) &&
                        bodyElement.ValueKind == JsonValueKind.String)
                    {
                        CollectJsonBodyVersions(bodyElement.GetString(), versions);
                    }
                }
            }

            if (element.TryGetProperty("item", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in items.EnumerateArray())
                {
                    CollectPostmanExampleVersions(item, versions);
                }
            }
        }

        private static void CollectJsonBodyVersions(string? body, List<string> versions)
        {
            if (String.IsNullOrWhiteSpace(body) || !body.Contains("\"Version\""))
            {
                return;
            }

            try
            {
                using JsonDocument bodyDocument = JsonDocument.Parse(body);
                CollectJsonVersionValues(bodyDocument.RootElement, versions);
            }
            catch (JsonException)
            {
                return;
            }
        }

        private static void CollectJsonVersionValues(JsonElement element, List<string> versions)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.NameEquals("Version") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        string? version = property.Value.GetString();
                        if (!String.IsNullOrWhiteSpace(version))
                        {
                            versions.Add(version!);
                        }
                    }

                    CollectJsonVersionValues(property.Value, versions);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    CollectJsonVersionValues(item, versions);
                }
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
