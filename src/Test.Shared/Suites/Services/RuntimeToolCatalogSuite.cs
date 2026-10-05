namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Runtimes.Mcp;
    using Armada.Server.RuntimeTools;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for captain runtime tool discovery: typed JSON-RPC handling in <see cref="McpServerToolProbe"/> (the
    /// reply is selected by id from SSE and stdio streams that also carry notifications, multi-line SSE data is joined,
    /// JSON-RPC errors surface as <see cref="McpClientException"/> with the error code, and <c>tools/list</c> pages are
    /// followed through typed results), and proof that the test server routes every host touch point of
    /// <c>/api/v1/captains/{id}/tools</c> through the fake <see cref="RecordingRuntimeToolDiscoverySource"/>.
    /// </summary>
    public sealed class RuntimeToolCatalogSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.RuntimeToolCatalog";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("sse_notification_before_response_selects_response_by_id", "An SSE body with a notification frame before the reply yields the reply to the request id", TestTags.Positive, () =>
            {
                string body =
                    "event: message\n" +
                    "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progress\":1,\"total\":2}}\n" +
                    "\n" +
                    "event: message\n" +
                    "data: {\"jsonrpc\":\"2.0\",\"id\":2,\"result\":{\"tools\":[{\"name\":\"alpha\",\"description\":\"First\",\"inputSchema\":{\"type\":\"object\"}}]}}\n" +
                    "\n";

                JsonRpcResponse<RuntimeMcpListToolsResult> response = McpServerToolProbe.ReadHttpResponse<RuntimeMcpListToolsResult>(body, "2");

                AssertEqual("2", response.Id, "response id");
                AssertNotNull(response.Result, "result");
                AssertEqual(1, response.Result!.Tools.Count, "tool count");
                AssertEqual("alpha", response.Result.Tools[0].Name, "tool name");
                AssertEqual("First", response.Result.Tools[0].Description, "tool description");
                AssertNull(response.Result.EffectiveNextCursor, "next cursor");
            }));

            cases.Add(Case("sse_multiline_data_event_is_joined", "An SSE event whose JSON spans several data lines is joined before deserialization", TestTags.Positive, () =>
            {
                string body =
                    "data: {\"jsonrpc\":\"2.0\",\n" +
                    "data: \"id\":3,\n" +
                    "data: \"result\":{\"tools\":[{\"name\":\"beta\"}],\"nextCursor\":\"c-2\"}}\n" +
                    "\n";

                JsonRpcResponse<RuntimeMcpListToolsResult> response = McpServerToolProbe.ReadHttpResponse<RuntimeMcpListToolsResult>(body, "3");

                AssertNotNull(response.Result, "result");
                AssertEqual("beta", response.Result!.Tools[0].Name, "tool name");
                AssertEqual("c-2", response.Result.EffectiveNextCursor, "next cursor");
            }));

            cases.Add(Case("sse_without_reply_for_id_throws_typed", "An SSE body that only answers another id fails with McpClientException and no JSON-RPC code", TestTags.Negative, () =>
            {
                string body = "data: {\"jsonrpc\":\"2.0\",\"id\":9,\"result\":{\"tools\":[]}}\n\n";
                McpClientException? failure = null;
                try
                {
                    McpServerToolProbe.ReadHttpResponse<RuntimeMcpListToolsResult>(body, "2");
                }
                catch (McpClientException ex)
                {
                    failure = ex;
                }

                AssertNotNull(failure, "expected McpClientException");
                AssertNull(failure!.JsonRpcCode, "JSON-RPC code");
            }));

            cases.Add(CaseAsync("stdio_json_line_skips_notifications_and_server_requests", "A JSON-line stdio stream with log output, a notification and a server request before the reply yields the reply", TestTags.Positive, async () =>
            {
                string stream =
                    "server starting on stdio\n" +
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{\"level\":\"info\",\"data\":\"ready\"}}\n" +
                    "\n" +
                    "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"roots/list\"}\n" +
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[{\"name\":\"stale\"}]}}\n" +
                    "{\"jsonrpc\":\"2.0\",\"id\":\"2\",\"result\":{\"tools\":[{\"name\":\"gamma\"},{\"name\":\"delta\"}],\"next_cursor\":\"snake\"}}\n";

                using (MemoryStream output = new MemoryStream(Encoding.UTF8.GetBytes(stream)))
                {
                    JsonRpcResponse<RuntimeMcpListToolsResult> response = await McpServerToolProbe.ReadStdioResponseAsync<RuntimeMcpListToolsResult>(
                        output, "2", McpStdioFramingEnum.JsonLine, CancellationToken.None).ConfigureAwait(false);

                    AssertTrue(response.IsResponse, "selected message is a response");
                    AssertEqual("2", response.Id, "response id");
                    AssertEqual(2, response.Result!.Tools.Count, "tool count");
                    AssertEqual("gamma", response.Result.Tools[0].Name, "first tool");
                    AssertEqual("delta", response.Result.Tools[1].Name, "second tool");
                    AssertEqual("snake", response.Result.EffectiveNextCursor, "snake_case next cursor");
                }
            }));

            cases.Add(CaseAsync("stdio_content_length_skips_notification", "A Content-Length framed stdio stream with a notification before the reply yields the reply", TestTags.Positive, async () =>
            {
                string notification = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progress\":1}}";
                string reply = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-03-26\"}}";
                string stream = Frame(notification) + Frame(reply);

                using (MemoryStream output = new MemoryStream(Encoding.UTF8.GetBytes(stream)))
                {
                    JsonRpcResponse<JsonRpcEmptyResult> response = await McpServerToolProbe.ReadStdioResponseAsync<JsonRpcEmptyResult>(
                        output, "1", McpStdioFramingEnum.ContentLength, CancellationToken.None).ConfigureAwait(false);

                    AssertEqual("1", response.Id, "response id");
                    AssertNotNull(response.Result, "result");
                    AssertNull(response.Error, "error");
                }
            }));

            cases.Add(CaseAsync("stdio_jsonrpc_error_is_typed", "A JSON-RPC error reply on stdio fails with McpClientException carrying the error code", TestTags.Negative, async () =>
            {
                string stream =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{}}\n" +
                    "{\"jsonrpc\":\"2.0\",\"id\":2,\"error\":{\"code\":-32601,\"message\":\"Method not found\",\"data\":{\"method\":\"tools/list\"}}}\n";

                McpClientException? failure = null;
                using (MemoryStream output = new MemoryStream(Encoding.UTF8.GetBytes(stream)))
                {
                    try
                    {
                        await McpServerToolProbe.ReadStdioResponseAsync<RuntimeMcpListToolsResult>(
                            output, "2", McpStdioFramingEnum.JsonLine, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (McpClientException ex)
                    {
                        failure = ex;
                    }
                }

                AssertNotNull(failure, "expected McpClientException");
                AssertEqual<int?>(-32601, failure!.JsonRpcCode, "JSON-RPC code");
                AssertNull(failure.HttpStatusCode, "HTTP status");
            }));

            cases.Add(Case("http_jsonrpc_error_is_typed", "A JSON-RPC error reply over HTTP fails with McpClientException carrying the error code", TestTags.Negative, () =>
            {
                string body = "{\"jsonrpc\":\"2.0\",\"id\":2,\"error\":{\"code\":-32602,\"message\":\"Invalid params\"}}";
                McpClientException? failure = null;
                try
                {
                    McpServerToolProbe.ReadHttpResponse<RuntimeMcpListToolsResult>(body, "2");
                }
                catch (McpClientException ex)
                {
                    failure = ex;
                }

                AssertNotNull(failure, "expected McpClientException");
                AssertEqual<int?>(-32602, failure!.JsonRpcCode, "JSON-RPC code");
            }));

            cases.Add(CaseAsync("http_probe_follows_pages_with_session", "The HTTP probe initializes over SSE, then follows tools/list cursors to the last page with the session id", TestTags.Positive, async () =>
            {
                McpStubServerHandler handler = new McpStubServerHandler
                {
                    Pages = new List<List<string>>
                    {
                        new List<string> { "one", "two" },
                        new List<string> { "three" },
                        new List<string> { "four" }
                    }
                };

                List<McpRemoteTool> tools;
                using (HttpClient client = new HttpClient(handler))
                {
                    McpServerToolProbe probe = new McpServerToolProbe(client);
                    tools = await probe.ListHttpToolsAsync(StubServer(), CancellationToken.None).ConfigureAwait(false);
                }

                AssertEqual("one,two,three,four", String.Join(",", tools.Select(t => t.Name)), "tool names across pages");
                AssertEqual("{\"type\":\"object\"}", tools[0].InputSchemaJson, "input schema");

                List<McpStubRequest> requests = handler.Requests.ToList();
                AssertEqual("initialize,notifications/initialized,tools/list,tools/list,tools/list", String.Join(",", requests.Select(r => r.Method)), "request methods");
                List<McpStubRequest> listRequests = requests.Where(r => r.Method == "tools/list").ToList();
                AssertNull(listRequests[0].Params?.Cursor, "first page cursor");
                AssertEqual("page-1", listRequests[1].Params?.Cursor, "second page cursor");
                AssertEqual("page-2", listRequests[2].Params?.Cursor, "third page cursor");
                AssertEqual("2", listRequests[0].Id, "first tools/list id");
                AssertEqual("4", listRequests[2].Id, "last tools/list id");
                AssertTrue(listRequests.All(r => r.SessionId == handler.SessionId), "session id sent on every tools/list request");
            }));

            cases.Add(CaseAsync("http_probe_jsonrpc_error_is_typed", "A tools/list JSON-RPC error over HTTP fails the probe with the code and, for a 4xx, the status", TestTags.Negative, async () =>
            {
                McpStubServerHandler handler = new McpStubServerHandler
                {
                    Pages = new List<List<string>> { new List<string> { "one" } },
                    ToolsListErrorCode = -32603,
                    ToolsListErrorStatus = HttpStatusCode.BadRequest
                };

                McpClientException? failure = null;
                using (HttpClient client = new HttpClient(handler))
                {
                    McpServerToolProbe probe = new McpServerToolProbe(client);
                    try
                    {
                        await probe.ListHttpToolsAsync(StubServer(), CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (McpClientException ex)
                    {
                        failure = ex;
                    }
                }

                AssertNotNull(failure, "expected McpClientException");
                AssertEqual<int?>(-32603, failure!.JsonRpcCode, "JSON-RPC code");
                AssertEqual<int?>(400, failure.HttpStatusCode, "HTTP status");
            }));

            cases.Add(CaseAsync("test_server_uses_fake_discovery_source", "GET /api/v1/captains/{id}/tools on the test server reads only the fake discovery source", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                RecordingRuntimeToolDiscoverySource fake = fx.RuntimeToolDiscovery;
                string claudeConfigPath = Path.Combine(fake.ProfileDirectory, ".claude.json");
                fake.ConfigFiles[claudeConfigPath] =
                    "{\"numStartups\":4,\"mcpServers\":{" +
                    "\"fixture\":{\"type\":\"http\",\"url\":\"http://fixture.invalid/mcp\",\"headers\":{\"X-Token\":\"t\",\"X-Count\":3}}," +
                    "\"offline\":{\"command\":\"fixture-offline\",\"args\":[\"--stdio\"],\"env\":{\"A\":\"1\",\"B\":2,\"C\":\"3\"}}}}";
                fake.ServerTools["fixture"] = new List<McpRemoteTool>
                {
                    new McpRemoteTool { Name = "fixture_echo", Description = "Echoes", InputSchemaJson = "{\"type\":\"object\"}" },
                    new McpRemoteTool { Name = "", Description = "nameless tools are dropped" }
                };

                Captain created;
                using (StringContent content = JsonHelper.ToJsonContent(new { Name = "runtime-tools-" + Guid.NewGuid().ToString("N").Substring(0, 8), Runtime = "ClaudeCode" }))
                using (HttpResponseMessage createResponse = await fx.AuthClient.PostAsync("/api/v1/captains", content).ConfigureAwait(false))
                {
                    createResponse.EnsureSuccessStatusCode();
                    created = await JsonHelper.DeserializeAsync<Captain>(createResponse).ConfigureAwait(false);
                }

                CaptainToolAccessResult result;
                using (HttpResponseMessage toolsResponse = await fx.AuthClient.GetAsync("/api/v1/captains/" + created.Id + "/tools").ConfigureAwait(false))
                {
                    AssertStatusCode(HttpStatusCode.OK, toolsResponse, "tools status");
                    result = await JsonHelper.DeserializeAsync<CaptainToolAccessResult>(toolsResponse).ConfigureAwait(false);
                }

                AssertEqual("claude-code-mcp-probe", result.AvailabilitySource, "availability source");
                AssertEqual(2, result.ConfiguredServerCount, "configured servers");
                AssertEqual(1, result.ReachableServerCount, "reachable servers");
                AssertEqual(1, result.Tools.Count, "tools");
                AssertEqual("fixture_echo", result.Tools[0].Name, "tool name");
                AssertEqual("fixture", result.Tools[0].RegistrationSource, "tool source");
                AssertEqual("McpServer", result.Tools[0].SourceKind, "tool source kind");

                CaptainToolServerSummary? fixtureServer = result.Servers.FirstOrDefault(s => s.Name == "fixture");
                CaptainToolServerSummary? offlineServer = result.Servers.FirstOrDefault(s => s.Name == "offline");
                AssertNotNull(fixtureServer, "fixture server summary");
                AssertNotNull(offlineServer, "offline server summary");
                AssertEqual("streamable_http", fixtureServer!.Transport, "fixture transport");
                AssertTrue(fixtureServer.Reachable, "fixture reachable");
                AssertEqual(1, fixtureServer.HeaderCount, "string-valued headers only");
                AssertEqual("stdio", offlineServer!.Transport, "offline transport");
                AssertFalse(offlineServer.Reachable, "offline reachable");
                AssertEqual(2, offlineServer.EnvironmentVariableCount, "string-valued env vars only");

                AssertTrue(fake.ConfigReads.Contains(claudeConfigPath), "server read the Claude config through the fake");
                AssertTrue(fake.InventoryReads.Contains(AgentRuntimeEnum.ClaudeCode), "server read the built-in inventory through the fake");
                List<string> probed = fake.ServerProbes.ToList();
                AssertTrue(probed.Contains("fixture") && probed.Contains("offline"), "server probed both configured servers through the fake");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Runtime Tool Catalog",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static RuntimeMcpServerDefinition StubServer()
        {
            return new RuntimeMcpServerDefinition
            {
                Name = "stub",
                TransportType = "streamable_http",
                Url = "http://stub.invalid/mcp"
            };
        }

        private static string Frame(string json)
        {
            return "Content-Length: " + Encoding.UTF8.GetByteCount(json) + "\r\n\r\n" + json;
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
