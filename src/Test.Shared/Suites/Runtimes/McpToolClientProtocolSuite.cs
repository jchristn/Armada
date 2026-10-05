namespace Test.Shared.Suites.Runtimes
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Runtimes.Mcp;
    using McpToolCallResult = Armada.Runtimes.Mcp.McpToolCallResult;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for the typed MCP client protocol: JSON-RPC envelopes are deserialized into typed classes, the SSE
    /// frame answering a request is selected by id (notifications on the same stream are skipped), failures carry the
    /// HTTP status and JSON-RPC code, and the tool result keeps the server's <c>isError</c> flag.
    /// </summary>
    public sealed class McpToolClientProtocolSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string _SuiteId = "Runtimes.McpToolClientProtocol";
        private const string _Endpoint = "http://stub.invalid/mcp";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("sse_response_selected_by_id", "The SSE frame whose id matches the request is the result; earlier notification and stale frames are skipped", TestTags.Negative, async () =>
            {
                ToolClientStubHandler handler = new ToolClientStubHandler();
                handler.Respond = request =>
                {
                    if (request.Method != "tools/call") return ToolClientStubHandler.Json(request.Id, "{}");
                    string body =
                        "event: message\n" +
                        "data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progress\":1,\"total\":2}}\n\n" +
                        ": keep-alive comment\n\n" +
                        "data: {\"jsonrpc\":\"2.0\",\"id\":999,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"stale\"}],\"isError\":false}}\n\n" +
                        "event: message\n" +
                        "data: {\"jsonrpc\":\"2.0\",\n" +
                        "data: \"id\":" + request.Id + ",\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"denied\"}],\"isError\":true}}\n\n";
                    return ToolClientStubHandler.Sse(body);
                };

                using (McpToolClient client = new McpToolClient(_Endpoint, handler))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    McpToolCallResult result = await client.CallToolResultAsync("remote_tool", "{\"a\":1}").ConfigureAwait(false);
                    AssertTrue(result.IsError, "isError comes from the response frame, not the notification or the stale frame");
                    AssertEqual("denied", result.Text);
                    AssertEqual("denied", await client.CallToolAsync("remote_tool", null).ConfigureAwait(false));
                }

                ToolClientStubRequest call = handler.Requests.Find(r => r.Method == "tools/call")!;
                AssertEqual("remote_tool", call.Params!.Name);
                AssertEqual("{\"a\":1}", call.Params.Arguments);
            }));

            cases.Add(CaseAsync("jsonrpc_error_has_typed_code", "A JSON-RPC error raises McpClientException with the JSON-RPC code", TestTags.Negative, async () =>
            {
                ToolClientStubHandler handler = new ToolClientStubHandler();
                handler.Respond = request => request.Method == "tools/call"
                    ? ToolClientStubHandler.Raw(HttpStatusCode.OK, "{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"error\":{\"code\":-32602,\"message\":\"bad params\",\"data\":{\"field\":\"x\"}}}")
                    : ToolClientStubHandler.Json(request.Id, "{}");

                using (McpToolClient client = new McpToolClient(_Endpoint, handler))
                {
                    McpClientException? caught = null;
                    try { await client.CallToolResultAsync("remote_tool", "{}").ConfigureAwait(false); }
                    catch (McpClientException ex) { caught = ex; }
                    AssertNotNull(caught, "a JSON-RPC error must throw");
                    AssertEqual(-32602, caught!.JsonRpcCode!.Value);
                    AssertEqual(200, caught.HttpStatusCode!.Value);
                }
            }));

            cases.Add(CaseAsync("http_error_has_typed_status", "A non-success HTTP status raises McpClientException with the status", TestTags.Negative, async () =>
            {
                ToolClientStubHandler handler = new ToolClientStubHandler();
                handler.Respond = request => ToolClientStubHandler.Raw(HttpStatusCode.ServiceUnavailable, "busy");

                using (McpToolClient client = new McpToolClient(_Endpoint, handler))
                {
                    McpClientException? caught = null;
                    try { await client.ListToolsAsync().ConfigureAwait(false); }
                    catch (McpClientException ex) { caught = ex; }
                    AssertNotNull(caught, "a 503 must throw");
                    AssertEqual(503, caught!.HttpStatusCode!.Value);
                    AssertNull(caught.JsonRpcCode);
                }
            }));

            cases.Add(CaseAsync("missing_response_throws", "A stream that carries only notifications is a protocol failure, not an empty result", TestTags.Negative, async () =>
            {
                ToolClientStubHandler handler = new ToolClientStubHandler();
                handler.Respond = request => ToolClientStubHandler.Sse("data: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/message\",\"params\":{\"level\":\"info\"}}\n\n");

                using (McpToolClient client = new McpToolClient(_Endpoint, handler))
                {
                    McpClientException? caught = null;
                    try { await client.CallToolResultAsync("remote_tool", "{}").ConfigureAwait(false); }
                    catch (McpClientException ex) { caught = ex; }
                    AssertNotNull(caught, "no response frame must throw");
                    AssertNull(caught!.JsonRpcCode);
                }
            }));

            cases.Add(CaseAsync("list_tools_paginates_typed", "tools/list follows nextCursor across JSON and SSE pages and keeps only object schemas", TestTags.Positive, async () =>
            {
                ToolClientStubHandler handler = new ToolClientStubHandler();
                handler.Respond = request =>
                {
                    if (request.Method != "tools/list") return ToolClientStubHandler.Json(request.Id, "{}");
                    if (request.Params?.Cursor == null)
                        return ToolClientStubHandler.Json(request.Id, "{\"tools\":[{\"name\":\"alpha\",\"description\":\"A\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\"}}}},{\"name\":\"\"}],\"nextCursor\":\"p2\"}");
                    return ToolClientStubHandler.Sse("data: {\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":{\"tools\":[{\"name\":\"beta\",\"inputSchema\":\"not-an-object\"}]}}\n\n");
                };

                using (McpToolClient client = new McpToolClient(_Endpoint, handler))
                {
                    List<McpRemoteTool> tools = await client.ListToolsAsync().ConfigureAwait(false);
                    AssertEqual(2, tools.Count, "the nameless tool is skipped");
                    AssertEqual("alpha", tools[0].Name);
                    AssertEqual("A", tools[0].Description);
                    Dictionary<string, object>? schema = JsonSerializer.Deserialize<Dictionary<string, object>>(tools[0].InputSchemaJson);
                    AssertNotNull(schema);
                    AssertTrue(schema!.ContainsKey("properties"), "the object schema is kept verbatim");
                    AssertEqual("beta", tools[1].Name);
                    AssertEqual(String.Empty, tools[1].InputSchemaJson, "a non-object schema is dropped");
                }

                AssertEqual("p2", handler.Requests.FindLast(r => r.Method == "tools/list")!.Params!.Cursor);
            }));

            cases.Add(CaseAsync("non_text_parts_rendered_as_json", "Non-text content parts are rendered as their JSON, not dropped", TestTags.Positive, async () =>
            {
                ToolClientStubHandler handler = new ToolClientStubHandler();
                handler.Respond = request => ToolClientStubHandler.Json(request.Id, "{\"content\":[{\"type\":\"text\",\"text\":\"caption\"},{\"type\":\"image\",\"data\":\"AAAA\",\"mimeType\":\"image/png\"}]}");

                using (McpToolClient client = new McpToolClient(_Endpoint, handler))
                {
                    string text = await client.CallToolAsync("remote_tool", "{}").ConfigureAwait(false);
                    string[] lines = text.Split('\n');
                    AssertEqual(2, lines.Length);
                    AssertEqual("caption", lines[0]);
                    McpToolContentPart? image = JsonSerializer.Deserialize<McpToolContentPart>(lines[1]);
                    AssertEqual("image", image!.Type);
                    AssertEqual("image/png", image.MimeType);
                    AssertEqual("AAAA", image.Data);
                }
            }));

            cases.Add(Case("sse_event_data_joined_per_spec", "SSE data lines of one event are joined with newlines; CRLF, comments, and other fields are handled", TestTags.Positive, () =>
            {
                List<string> events = McpResponseReader.ReadEventData("id: 1\r\nevent: message\r\ndata: first\r\ndata:second\r\n\r\n: comment\r\n\r\ndata: third\r\n");
                AssertEqual(2, events.Count);
                AssertEqual("first\nsecond", events[0]);
                AssertEqual("third", events[1]);
            }));

            cases.Add(Case("plain_json_requires_matching_id", "A plain JSON body for a different id is not taken as the response", TestTags.Negative, () =>
            {
                JsonRpcResponse<McpToolCallResult>? other = McpResponseReader.ReadResponse<McpToolCallResult>("{\"jsonrpc\":\"2.0\",\"id\":7,\"result\":{\"isError\":true}}", "8");
                AssertNull(other, "id 7 does not answer request 8");
                JsonRpcResponse<McpToolCallResult>? match = McpResponseReader.ReadResponse<McpToolCallResult>("{\"jsonrpc\":\"2.0\",\"id\":\"8\",\"result\":{\"isError\":true}}", "8");
                AssertTrue(match!.Result!.IsError, "a string id matches its numeric request id");
            }));

            return new TestSuiteDescriptor(
                suiteId: _SuiteId,
                displayName: "MCP Tool Client Protocol",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: _SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
