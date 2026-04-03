namespace Armada.Test.Unit.Suites.Services
{
    using System.Collections.Generic;
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
            await RunTest("Release Surfaces Stay In Lockstep At V050", () =>
            {
                string repositoryRoot = FindRepositoryRoot();
                string expectedVersion = ReadSingleCapturedVersion(
                    repositoryRoot,
                    "src/Directory.Build.props",
                    @"<Version>\s*(?<version>[^<]+)\s*</Version>",
                    "the shared build version");

                AssertTrue(expectedVersion == "0.5.0", "Directory.Build.props should pin the v0.5.0 release");
                AssertTrue(Constants.ProductVersion == expectedVersion, "Constants.ProductVersion should match Directory.Build.props");

                AssertAllCapturedVersionsMatch(
                    repositoryRoot,
                    "src/Armada.Core/Constants.cs",
                    @"ProductVersion\s*=\s*""(?<version>\d+\.\d+\.\d+)""",
                    expectedVersion,
                    "the product version constant");

                AssertAllCapturedVersionsMatch(
                    repositoryRoot,
                    "src/Armada.Helm/Armada.Helm.csproj",
                    @"<Version>\s*(?<version>[^<]+)\s*</Version>",
                    expectedVersion,
                    "the Helm package version");

                AssertAllCapturedVersionsMatch(
                    repositoryRoot,
                    "src/Armada.Dashboard/package.json",
                    @"""version"":\s*""(?<version>\d+\.\d+\.\d+)""",
                    expectedVersion,
                    "the dashboard package version");

                AssertAllCapturedVersionsMatch(
                    repositoryRoot,
                    "docker/compose.yaml",
                    @"image:\s+jchristn77/armada-(?:server|dashboard):v(?<version>\d+\.\d+\.\d+)",
                    expectedVersion,
                    "the Docker image tag");

                AssertAllCapturedVersionsMatch(
                    repositoryRoot,
                    "docs/REST_API.md",
                    @"(?:\*\*Version:\*\*\s*|""Version"":\s*"")(?<version>\d+\.\d+\.\d+)",
                    expectedVersion,
                    "the REST API release literal");

                AssertAllCapturedVersionsMatch(
                    repositoryRoot,
                    "docs/MCP_API.md",
                    @"\*\*Version:\*\*\s*(?<version>\d+\.\d+\.\d+)",
                    expectedVersion,
                    "the MCP API release literal");

                AssertPostmanCollectionVersionsMatch(
                    repositoryRoot,
                    "Armada.postman_collection.json",
                    expectedVersion);
            });

            await RunTest("ReadSingleCapturedVersion Rejects Duplicate Shared Build Versions", () =>
            {
                WithTemporaryRepositoryRoot(tempRoot =>
                {
                    WriteRepositoryFile(
                        tempRoot,
                        "src/Directory.Build.props",
                        "<Project>\n  <PropertyGroup>\n    <Version>0.5.0</Version>\n    <Version>0.2.0</Version>\n  </PropertyGroup>\n</Project>\n");

                    Exception exception = CaptureException(() => ReadSingleCapturedVersion(
                        tempRoot,
                        "src/Directory.Build.props",
                        @"<Version>\s*(?<version>[^<]+)\s*</Version>",
                        "the shared build version"));

                    AssertContains("exactly one match", exception.Message, "duplicate shared build versions should be rejected");
                });
            });

            await RunTest("AssertAllCapturedVersionsMatch Rejects Stale Rest Release Literals", () =>
            {
                WithTemporaryRepositoryRoot(tempRoot =>
                {
                    WriteRepositoryFile(
                        tempRoot,
                        "docs/REST_API.md",
                        "**Version:** 0.5.0\n\n{\n  \"Version\": \"0.2.0\"\n}\n");

                    Exception exception = CaptureException(() => AssertAllCapturedVersionsMatch(
                        tempRoot,
                        "docs/REST_API.md",
                        @"(?:\*\*Version:\*\*\s*|""Version"":\s*"")(?<version>\d+\.\d+\.\d+)",
                        "0.5.0",
                        "the REST API release literal"));

                    AssertContains("but found 0.2.0", exception.Message, "stale REST release literals should be rejected");
                });
            });

            await RunTest("AssertPostmanCollectionVersionsMatch Rejects Split Embedded Versions", () =>
            {
                WithTemporaryRepositoryRoot(tempRoot =>
                {
                    string postmanContents = JsonSerializer.Serialize(new
                    {
                        info = new
                        {
                            description = "Version: 0.5.0"
                        },
                        item = new object[]
                        {
                            new
                            {
                                response = new object[]
                                {
                                    new
                                    {
                                        body = "{\n  \"Version\": \"0.2.0\"\n}"
                                    }
                                }
                            }
                        }
                    });

                    WriteRepositoryFile(tempRoot, "Armada.postman_collection.json", postmanContents);

                    Exception exception = CaptureException(() => AssertPostmanCollectionVersionsMatch(
                        tempRoot,
                        "Armada.postman_collection.json",
                        "0.5.0"));

                    AssertContains("but found 0.2.0", exception.Message, "split Postman embedded versions should be rejected");
                });
            });
        }

        private void AssertAllCapturedVersionsMatch(
            string repositoryRoot,
            string relativePath,
            string pattern,
            string expectedVersion,
            string description)
        {
            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
            MatchCollection matches = Regex.Matches(contents, pattern);

            AssertTrue(matches.Count > 0, relativePath + " should contain " + description);

            foreach (Match match in matches)
            {
                string actualVersion = match.Groups["version"].Value.Trim();
                AssertTrue(
                    actualVersion == expectedVersion,
                    relativePath + " should keep " + description + " in lockstep with " + expectedVersion + ", but found " + actualVersion);
            }
        }

        private void AssertPostmanCollectionVersionsMatch(
            string repositoryRoot,
            string relativePath,
            string expectedVersion)
        {
            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
            using JsonDocument document = JsonDocument.Parse(contents);

            List<string> versions = new List<string>();
            Regex descriptionVersionPattern = new Regex(@"Version:\s*(?<version>\d+\.\d+\.\d+)");
            Regex responseVersionPattern = new Regex("\"Version\":\\s*\"(?<version>\\d+\\.\\d+\\.\\d+)\"");

            CollectJsonStringMatches(document.RootElement, descriptionVersionPattern, versions);
            CollectJsonStringMatches(document.RootElement, responseVersionPattern, versions);

            AssertTrue(versions.Count > 0, relativePath + " should contain release version literals");

            foreach (string actualVersion in versions)
            {
                AssertTrue(
                    actualVersion == expectedVersion,
                    relativePath + " should keep embedded release literals in lockstep with " + expectedVersion + ", but found " + actualVersion);
            }
        }

        private static void CollectJsonStringMatches(JsonElement element, Regex pattern, List<string> versions)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        CollectJsonStringMatches(property.Value, pattern, versions);
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (JsonElement child in element.EnumerateArray())
                    {
                        CollectJsonStringMatches(child, pattern, versions);
                    }
                    break;

                case JsonValueKind.String:
                    string value = element.GetString() ?? string.Empty;
                    MatchCollection matches = pattern.Matches(value);

                    foreach (Match match in matches)
                    {
                        versions.Add(match.Groups["version"].Value.Trim());
                    }
                    break;
            }
        }

        private string ReadSingleCapturedVersion(
            string repositoryRoot,
            string relativePath,
            string pattern,
            string description)
        {
            string contents = File.ReadAllText(GetRepositoryPath(repositoryRoot, relativePath));
            MatchCollection matches = Regex.Matches(contents, pattern);

            AssertTrue(matches.Count == 1, relativePath + " should contain exactly one match for " + description);
            return matches[0].Groups["version"].Value.Trim();
        }

        private static string GetRepositoryPath(string repositoryRoot, string relativePath)
        {
            return Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static Exception CaptureException(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                return exception;
            }

            throw new Exception("Assertion failed: expected an exception but no exception was thrown");
        }

        private static void WithTemporaryRepositoryRoot(Action<string> action)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "armada-release-version-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                action(tempRoot);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static void WriteRepositoryFile(string repositoryRoot, string relativePath, string contents)
        {
            string path = GetRepositoryPath(repositoryRoot, relativePath);
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, contents);
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
