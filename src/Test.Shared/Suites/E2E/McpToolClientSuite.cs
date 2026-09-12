namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Runtimes.Mcp;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// End-to-end coverage for the production <see cref="McpToolClient"/> -- the streamable-HTTP MCP client
    /// the in-process (ApiEndpoint) captain runtime uses to reach Armada's own MCP server. Where
    /// <see cref="McpAuthScopingSuite"/> drives the transport with hand-rolled HTTP to prove per-user auth,
    /// this suite exercises the actual client class end to end against the advertised /mcp endpoint:
    /// initialize + session handshake, tools/list discovery, and tools/call execution with round-tripped
    /// results. A green run proves the exact code path the runtime takes to expose Armada tools to a model.
    /// Cases run against the anonymous local caller (the additive default-admin fallback), so create and
    /// enumerate both succeed without a credential.
    /// </summary>
    public sealed class McpToolClientSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.McpToolClient";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the MCP tool-client suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("client_discovers_armada_tools", "The MCP client initializes and discovers Armada's tools", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);

                using (McpToolClient client = new McpToolClient(Endpoint(fx)))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    List<McpRemoteTool> tools = await client.ListToolsAsync().ConfigureAwait(false);

                    AssertTrue(tools.Count > 0, "Expected the MCP server to advertise at least one tool.");
                    AssertTrue(HasTool(tools, "create_fleet"), "Expected 'create_fleet' among advertised tools.");
                    AssertTrue(HasTool(tools, "enumerate"), "Expected 'enumerate' among advertised tools.");
                }
            }));

            cases.Add(CaseAsync("client_calls_create_and_enumerate", "The MCP client can call a tool and round-trip its result", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);

                using (McpToolClient client = new McpToolClient(Endpoint(fx)))
                {
                    await client.InitializeAsync().ConfigureAwait(false);

                    string fleetName = "mcp-client-fleet-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    string createJson = "{\"name\":\"" + fleetName + "\"}";
                    string createResult = await client.CallToolAsync("create_fleet", createJson).ConfigureAwait(false);

                    AssertFalse(String.IsNullOrWhiteSpace(createResult), "create_fleet returned no result text.");
                    Fleet fleet = JsonHelper.Deserialize<Fleet>(createResult);
                    AssertNotNull(fleet.Id, "Created fleet id");

                    string enumResult = await client.CallToolAsync("enumerate", "{\"entityType\":\"fleets\",\"pageSize\":100}").ConfigureAwait(false);
                    AssertTrue(EnumerateContainsId(enumResult, fleet.Id), "Expected the created fleet " + fleet.Id + " to be visible via enumerate.");
                }
            }));

            cases.Add(CaseAsync("client_reports_error_for_unknown_tool", "The MCP client surfaces a protocol error for an unknown tool", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);

                using (McpToolClient client = new McpToolClient(Endpoint(fx)))
                {
                    await client.InitializeAsync().ConfigureAwait(false);

                    bool threw = false;
                    try
                    {
                        await client.CallToolAsync("no_such_tool_xyz", "{}").ConfigureAwait(false);
                    }
                    catch (McpClientException)
                    {
                        threw = true;
                    }

                    AssertTrue(threw, "Expected an McpClientException for an unknown tool.");
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "MCP Tool Client",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string Endpoint(E2EServerFixture fx)
        {
            return "http://127.0.0.1:" + fx.McpPort + "/mcp";
        }

        private static bool HasTool(List<McpRemoteTool> tools, string name)
        {
            foreach (McpRemoteTool tool in tools)
            {
                if (String.Equals(tool.Name, name, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool EnumerateContainsId(string enumerateText, string id)
        {
            EnumerationResult<System.Text.Json.JsonElement> data = JsonHelper.Deserialize<EnumerationResult<System.Text.Json.JsonElement>>(enumerateText);
            if (data.Objects == null) return false;
            foreach (System.Text.Json.JsonElement obj in data.Objects)
            {
                if (obj.TryGetProperty("Id", out System.Text.Json.JsonElement idElement) && String.Equals(idElement.GetString(), id, StringComparison.Ordinal))
                    return true;
            }
            return false;
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
