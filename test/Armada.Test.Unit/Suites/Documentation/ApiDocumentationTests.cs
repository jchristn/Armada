namespace Armada.Test.Unit.Suites.Documentation
{
    using System.Text.Json;
    using Armada.Test.Common;

    public class ApiDocumentationTests : TestSuite
    {
        public override string Name => "API Documentation";

        protected override async Task RunTestsAsync()
        {
            await RunTest("REST API docs include captain model and mission runtime fields", async () =>
            {
                string contents = await ReadRepositoryFileAsync(Path.Combine("docs", "REST_API.md")).ConfigureAwait(false);

                AssertContains("**Version:** 0.5.0", contents, "REST API version header");
                AssertContains("Register a new captain (AI agent). You can optionally set `Model` to override the runtime's default model selection. Invalid model values return `400 Bad Request`.", contents, "Captain create description");
                AssertContains("| `Model` | string? | no | Optional runtime-specific model override. Null uses the runtime default |", contents, "Captain create model field");
                AssertContains("Update a captain's name, runtime, or model. Operational fields (state, process, mission) are preserved. Invalid model values return `400 Bad Request`.", contents, "Captain update description");
                AssertContains("**Error:** `400` - Invalid model", contents, "Captain model validation error");
                AssertContains("\"Model\": null,", contents, "Captain example should include Model");
                AssertContains("| `Model` | string? | null | Optional runtime-specific model override. Null lets the runtime choose its default model |", contents, "Captain object definition should include Model");
                AssertContains("\"TotalRuntimeMs\": null,", contents, "Mission example should include TotalRuntimeMs");
                AssertContains("| `TotalRuntimeMs` | long? | null | Total runtime in milliseconds, computed when the mission completes |", contents, "Mission object definition should include TotalRuntimeMs");
            });

            await RunTest("MCP API docs include captain model parameters and mission runtime field", async () =>
            {
                string contents = await ReadRepositoryFileAsync(Path.Combine("docs", "MCP_API.md")).ConfigureAwait(false);

                AssertContains("**Version:** 0.5.0", contents, "MCP API version header");
                AssertContains("### armada_create_captain", contents, "Create captain tool heading");
                AssertContains("\"model\": { \"type\": [\"string\", \"null\"], \"description\": \"Optional runtime-specific model override. Omit it or pass null to use the runtime default\" }", contents, "Create captain tool model schema");
                AssertContains("| `model` | string \\| null | No | Optional runtime-specific model override. Omit it or pass null to use the runtime default |", contents, "Create captain tool model parameter");
                AssertContains("### armada_update_captain", contents, "Update captain tool heading");
                AssertContains("\"model\": { \"type\": [\"string\", \"null\"], \"description\": \"New runtime-specific model override. Omit it or pass null to use the runtime default\" }", contents, "Update captain tool model schema");
                AssertContains("| `model` | string \\| null | No | New runtime-specific model override. Omit it or pass null to use the runtime default |", contents, "Update captain tool model parameter");
                AssertContains("| `totalRuntimeMs` | long \\| null | Total runtime in milliseconds, computed when the mission completes |", contents, "Mission object definition should include totalRuntimeMs");
            });

            await RunTest("Postman collection examples include version, model, and total runtime updates", async () =>
            {
                string contents = await ReadRepositoryFileAsync("Armada.postman_collection.json").ConfigureAwait(false);

                using JsonDocument document = JsonDocument.Parse(contents);
                JsonElement root = document.RootElement;
                JsonElement info = root.GetProperty("info");
                string description = info.GetProperty("description").GetString() ?? String.Empty;
                AssertContains("Version: 0.5.0", description, "Collection description version");

                JsonElement healthCheck = FindItemByName(root, "Health Check");
                using (JsonDocument healthResponse = JsonDocument.Parse(GetFirstResponseBody(healthCheck)))
                {
                    AssertEqual("0.5.0", healthResponse.RootElement.GetProperty("Version").GetString(), "Health Check example version");
                }

                JsonElement createCaptain = FindItemByName(root, "Create Captain");
                using (JsonDocument createCaptainRequest = JsonDocument.Parse(GetRequestBody(createCaptain)))
                {
                    AssertEqual("gpt-5.4", createCaptainRequest.RootElement.GetProperty("Model").GetString(), "Create Captain request should include Model");
                }

                using (JsonDocument createCaptainResponse = JsonDocument.Parse(GetFirstResponseBody(createCaptain)))
                {
                    AssertEqual("gpt-5.4", createCaptainResponse.RootElement.GetProperty("Model").GetString(), "Create Captain response should include Model");
                }

                JsonElement updateCaptain = FindItemByName(root, "Update Captain");
                using (JsonDocument updateCaptainRequest = JsonDocument.Parse(GetRequestBody(updateCaptain)))
                {
                    AssertEqual("gpt-5.4", updateCaptainRequest.RootElement.GetProperty("Model").GetString(), "Update Captain request should include Model");
                }

                using (JsonDocument updateCaptainResponse = JsonDocument.Parse(GetFirstResponseBody(updateCaptain)))
                {
                    AssertEqual("gpt-5.4", updateCaptainResponse.RootElement.GetProperty("Model").GetString(), "Update Captain response should include Model");
                }

                JsonElement createMission = FindItemByName(root, "Create Mission");
                using (JsonDocument createMissionResponse = JsonDocument.Parse(GetFirstResponseBody(createMission)))
                {
                    AssertTrue(createMissionResponse.RootElement.TryGetProperty("TotalRuntimeMs", out JsonElement totalRuntimeMs), "Create Mission response should include TotalRuntimeMs");
                    AssertEqual(JsonValueKind.Null, totalRuntimeMs.ValueKind, "Create Mission response TotalRuntimeMs should default to null");
                }
            });
        }

        private static JsonElement FindItemByName(JsonElement element, string name)
        {
            if (TryFindItemByName(element, name, out JsonElement item))
            {
                return item;
            }

            throw new InvalidOperationException("Could not find Postman item named '" + name + "'.");
        }

        private static string GetFirstResponseBody(JsonElement item)
        {
            JsonElement responses = item.GetProperty("response");
            foreach (JsonElement response in responses.EnumerateArray())
            {
                if (response.TryGetProperty("body", out JsonElement bodyElement))
                {
                    return bodyElement.GetString() ?? String.Empty;
                }
            }

            throw new InvalidOperationException("No response body found for Postman item '" + (item.GetProperty("name").GetString() ?? String.Empty) + "'.");
        }

        private static string GetRequestBody(JsonElement item)
        {
            JsonElement body = item.GetProperty("request").GetProperty("body");
            return body.GetProperty("raw").GetString() ?? String.Empty;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "docs")) &&
                    File.Exists(Path.Combine(current.FullName, "Armada.postman_collection.json")) &&
                    Directory.Exists(Path.Combine(current.FullName, "src")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
        }

        private static async Task<string> ReadRepositoryFileAsync(string relativePath)
        {
            return await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), relativePath)).ConfigureAwait(false);
        }

        private static bool TryFindItemByName(JsonElement element, string name, out JsonElement item)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("name", out JsonElement nameElement) &&
                    String.Equals(nameElement.GetString(), name, StringComparison.Ordinal))
                {
                    item = element;
                    return true;
                }

                if (element.TryGetProperty("item", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement child in items.EnumerateArray())
                    {
                        if (TryFindItemByName(child, name, out item))
                        {
                            return true;
                        }
                    }
                }
            }

            item = default;
            return false;
        }
    }
}
