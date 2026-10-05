namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.ApiSurface;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Server.WebSocket;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The 1.0 API contract test (V1 readiness W2.4). Builds the live public surface (REST routes with OpenAPI metadata and
    /// declared authorization, MCP tools and input schemas, the WebSocket contract, the Helm CLI command model, and the
    /// settings keys) and compares it to the frozen baseline docs/api-surface-1.0.json: removals and incompatible changes
    /// of non-experimental items fail with a readable diff; additions are allowed. Also proves the experimental markers
    /// reach OpenAPI and MCP, every declared WebSocket command is dispatched, every event literal in the server source is
    /// declared, the Markdown view matches the JSON baseline, and the comparer catches each kind of break.
    /// </summary>
    public sealed class ApiContractSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.ApiContract";

        // Commands not sent by the dispatch check: they stop the server or touch backup files. They are still gated by
        // WebSocketSurface.CommandActions.
        private static readonly HashSet<string> _UndispatchedCommands = new HashSet<string>(StringComparer.Ordinal)
        {
            "stop_server", "backup", "restore"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("live_surface_is_compatible_with_baseline", "The live API surface keeps every frozen 1.0 item", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                ApiSurfaceDocument baseline = ApiSurfaceFiles.LoadBaseline();
                ApiSurfaceDocument live = ApiSurfaceBuilder.Build(fx.Server);
                AssertTrue(live.Rest.Count > 300, "expected the full REST surface, found " + live.Rest.Count);
                AssertTrue(live.Mcp.Count > 100, "expected the full MCP surface, found " + live.Mcp.Count);
                AssertTrue(live.Cli.Count > 40, "expected the full CLI surface, found " + live.Cli.Count);
                AssertTrue(live.Settings.Count > 100, "expected the full settings surface, found " + live.Settings.Count);

                ApiSurfaceDiff diff = ApiSurfaceComparer.Compare(baseline, live);
                if (diff.Breaking.Count > 0)
                {
                    throw new AssertionException("The public API changed incompatibly against docs/" + ApiSurfaceFiles.JsonFileName
                        + " (see docs/COMPATIBILITY.md).\n" + ApiSurfaceComparer.Format(diff));
                }

                if (diff.Additions.Count > 0)
                    Console.WriteLine("[ApiContract] " + diff.Additions.Count + " addition(s) not yet frozen; run scripts/common/generate-api-surface.sh:\n" + ApiSurfaceComparer.Format(diff, 25));
            }));

            cases.Add(Case("markdown_matches_baseline", "API_SURFACE_1.0.md is the rendering of api-surface-1.0.json", TestTags.Positive, () =>
            {
                string docs = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "docs");
                string expected = ApiSurfaceMarkdown.Render(ApiSurfaceFiles.LoadBaseline());
                string actual = File.ReadAllText(Path.Combine(docs, ApiSurfaceFiles.MarkdownFileName)).Replace("\r\n", "\n");
                AssertTrue(String.Equals(expected, actual, StringComparison.Ordinal),
                    "docs/" + ApiSurfaceFiles.MarkdownFileName + " was edited by hand or is stale; run scripts/common/generate-api-surface.sh");
            }));

            cases.Add(CaseAsync("experimental_items_are_marked", "Experimental routes and tools are marked in OpenAPI, MCP, and the surface file", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                HttpResponseMessage response = await fx.AuthClient.GetAsync("/openapi.json").ConfigureAwait(false);
                AssertEqual(200, (int)response.StatusCode, "openapi.json");
                string openApi = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using (JsonDocument document = JsonDocument.Parse(openApi))
                {
                    JsonElement harbors = document.RootElement.GetProperty("paths").GetProperty("/api/v1/harbors").GetProperty("get");
                    AssertStartsWith(ExperimentalSurface.Marker, harbors.GetProperty("summary").GetString() ?? "", "harbor route summary");
                    AssertTrue(harbors.GetProperty("tags").EnumerateArray().Any(t => t.GetString() == ExperimentalSurface.Tag), "harbor route carries the Experimental tag");
                    JsonElement fleets = document.RootElement.GetProperty("paths").GetProperty("/api/v1/fleets").GetProperty("get");
                    AssertFalse((fleets.GetProperty("summary").GetString() ?? "").StartsWith(ExperimentalSurface.Marker, StringComparison.Ordinal), "stable route is not marked");
                }

                CaptainToolSummary? harborTool = fx.Server.RegisteredMcpToolDescriptors.FirstOrDefault(t => t.Name == "create_harbor");
                AssertNotNull(harborTool, "create_harbor registered");
                AssertStartsWith(ExperimentalSurface.Marker, harborTool!.Description ?? "", "create_harbor description");
                CaptainToolSummary? fleetTool = fx.Server.RegisteredMcpToolDescriptors.FirstOrDefault(t => t.Name == "create_fleet");
                AssertFalse((fleetTool?.Description ?? "").StartsWith(ExperimentalSurface.Marker, StringComparison.Ordinal), "stable tool is not marked");

                ApiSurfaceDocument baseline = ApiSurfaceFiles.LoadBaseline();
                AssertTrue(baseline.Rest.Where(r => r.Route.StartsWith("/api/v1/harbors", StringComparison.Ordinal)).All(r => r.Experimental), "harbor routes are experimental in the baseline");
                AssertTrue(baseline.Mcp.Where(t => t.Name.Contains("harbor")).All(t => t.Experimental), "harbor tools are experimental in the baseline");
                AssertTrue(baseline.WebSocket.Endpoints.Single(e => e.Name == "harbor-link").Experimental, "Harbor link endpoint is experimental");
            }));

            cases.Add(CaseAsync("declared_websocket_commands_dispatch", "Every declared WebSocket command reaches a handler; undeclared ones are rejected", TestTags.Positive, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                using (ClientWebSocket ws = await E2EServerFixture.ConnectAuthenticatedWebSocketAsync(fx.RestPort).ConfigureAwait(false))
                {
                    List<string> unhandled = new List<string>();
                    foreach (string action in WebSocketSurface.CommandActions)
                    {
                        if (_UndispatchedCommands.Contains(action)) continue;
                        E2eWebSocketFrame reply = await SendCommandAsync(ws, action).ConfigureAwait(false);
                        if (reply.Type == "command.error" && reply.Code == WebSocketCommandErrorCodeEnum.UnknownAction) unhandled.Add(action);
                    }

                    AssertTrue(unhandled.Count == 0, "declared WebSocket commands without a handler: " + String.Join(", ", unhandled));
                    E2eWebSocketFrame unknown = await SendCommandAsync(ws, "not_a_real_action").ConfigureAwait(false);
                    AssertEqual("command.error", unknown.Type, "undeclared action is rejected");
                    AssertEqual(WebSocketCommandErrorCodeEnum.UnknownAction, unknown.Code, "undeclared action is rejected with code UnknownAction");
                    AssertEqual("not_a_real_action", unknown.Action, "the reply names the rejected action");

                    // A declared command that fails carries its reason as a code too: NotFound for a missing entity, and
                    // the exception-mapped code (InvalidArgument) when the handler throws on a missing id.
                    E2eWebSocketFrame missing = await SendCommandAsync(ws, "get_fleet", "flt_does_not_exist").ConfigureAwait(false);
                    AssertEqual("command.error", missing.Type, "get_fleet for an unknown id fails");
                    AssertEqual(WebSocketCommandErrorCodeEnum.NotFound, missing.Code, "missing fleet is NotFound");
                    E2eWebSocketFrame noId = await SendCommandAsync(ws, "get_fleet").ConfigureAwait(false);
                    AssertEqual("command.error", noId.Type, "get_fleet without an id fails");
                    AssertEqual(WebSocketCommandErrorCodeEnum.InvalidArgument, noId.Code, "missing id is InvalidArgument");
                }
            }));

            cases.Add(Case("websocket_event_literals_are_declared", "Every event type the server source broadcasts is declared in WebSocketSurface", TestTags.Positive, () =>
            {
                string serverSource = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "src", "Armada.Server");
                Regex call = new Regex("(BroadcastToTenant|SendToUser|_emitEvent|EmitEventAsync|_EmitEventAsync)\\s*\\((?<args>[^;]{0,400})", RegexOptions.Singleline);
                Regex literal = new Regex("\"(?<type>[a-z][a-z0-9_\\-]*(\\.[a-z0-9_\\-]+)+|approval-needed)\"");
                Regex assigned = new Regex("eventType\\s*=\\s*\"(?<type>[a-z][a-z0-9_\\-]*(\\.[a-z0-9_\\-]+)+)\"");
                SortedSet<string> undeclared = new SortedSet<string>(StringComparer.Ordinal);
                int found = 0;
                foreach (string file in Directory.GetFiles(serverSource, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.EndsWith("RemoteDashboardRelayService.cs", StringComparison.Ordinal)) continue; // proxy relay frames, not hub events
                    string text = File.ReadAllText(file);
                    List<string> types = new List<string>();
                    foreach (Match m in call.Matches(text))
                    {
                        Match first = literal.Match(m.Groups["args"].Value);
                        if (first.Success) types.Add(first.Groups["type"].Value);
                    }

                    foreach (Match m in assigned.Matches(text)) types.Add(m.Groups["type"].Value);
                    foreach (string type in types)
                    {
                        found++;
                        if (!WebSocketSurface.IsEvent(type)) undeclared.Add(type + " (" + Path.GetFileName(file) + ")");
                    }
                }

                AssertTrue(found > 30, "expected to find the server's event literals, found " + found);
                AssertTrue(undeclared.Count == 0, "event types broadcast but not declared in WebSocketSurface (declare them, then regenerate the surface): " + String.Join(", ", undeclared));
            }));

            cases.Add(CaseAsync("error_codes_match_status", "REST errors are ApiErrorResponse bodies whose Error code matches the status", TestTags.Negative, async () =>
            {
                E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);

                HttpResponseMessage unauth = await fx.UnauthClient.GetAsync("/api/v1/fleets").ConfigureAwait(false);
                await AssertErrorAsync(unauth, 401, "NotAuthorized").ConfigureAwait(false);

                HttpResponseMessage missing = await fx.AuthClient.GetAsync("/api/v1/zz-not-a-route/value").ConfigureAwait(false);
                await AssertErrorAsync(missing, 404, "NotFound").ConfigureAwait(false);

                using (StringContent empty = new StringContent("{}", Encoding.UTF8, "application/json"))
                {
                    HttpResponseMessage noIds = await fx.AuthClient.PostAsync("/api/v1/fleets/delete/multiple", empty).ConfigureAwait(false);
                    await AssertErrorAsync(noIds, 400, "BadRequest").ConfigureAwait(false);
                }

                HttpResponseMessage noFleet = await fx.AuthClient.GetAsync("/api/v1/fleets/flt_does_not_exist").ConfigureAwait(false);
                await AssertErrorAsync(noFleet, 404, "NotFound").ConfigureAwait(false);
            }));

            cases.Add(Case("comparer_flags_removals", "Removing a route, tool, argument, event, CLI option, or setting is breaking", TestTags.Negative, () =>
            {
                ApiSurfaceDocument baseline = ApiSurfaceFiles.LoadBaseline();

                ApiSurfaceDocument live = Clone(baseline);
                live.Rest.RemoveAll(r => r.Method == "GET" && r.Route == "/api/v1/fleets");
                AssertBreaking(baseline, live, "REST GET /api/v1/fleets: route removed");

                live = Clone(baseline);
                live.Mcp.RemoveAll(t => t.Name == "create_fleet");
                AssertBreaking(baseline, live, "MCP create_fleet: tool removed");

                live = Clone(baseline);
                live.Mcp.Single(t => t.Name == "update_fleet").Arguments.RemoveAll(a => a.Name == "name");
                AssertBreaking(baseline, live, "MCP update_fleet: argument 'name' removed");

                live = Clone(baseline);
                live.WebSocket.Events.Single(e => e.Type == "mission.changed").Fields.Remove("status");
                AssertBreaking(baseline, live, "WebSocket event mission.changed: payload field 'status' removed");

                live = Clone(baseline);
                live.WebSocket.Commands.Remove("get_mission");
                AssertBreaking(baseline, live, "WebSocket command 'get_mission' removed");

                live = Clone(baseline);
                live.Cli.Single(c => c.Command == "mission list").Parameters.RemoveAll(p => p.Name == "status");
                AssertBreaking(baseline, live, "CLI 'armada mission list': option --status removed");

                live = Clone(baseline);
                live.Settings.RemoveAll(s => s.Key == "admiralPort");
                AssertBreaking(baseline, live, "Setting admiralPort: key removed");
            }));

            cases.Add(Case("comparer_flags_incompatible_changes", "Required-ness, type, and authorization changes are breaking", TestTags.Negative, () =>
            {
                ApiSurfaceDocument baseline = ApiSurfaceFiles.LoadBaseline();

                ApiSurfaceDocument live = Clone(baseline);
                live.Mcp.Single(t => t.Name == "update_fleet").Arguments.Single(a => a.Name == "name").Required = true;
                AssertBreaking(baseline, live, "MCP update_fleet: argument 'name' became required");

                live = Clone(baseline);
                live.Mcp.Single(t => t.Name == "create_fleet").Arguments.Add(new ApiMcpArgument { Name = "zzMandatory", Type = "string", Required = true });
                AssertBreaking(baseline, live, "MCP create_fleet: new required argument 'zzMandatory'");

                live = Clone(baseline);
                live.Mcp.Single(t => t.Name == "update_fleet").Arguments.Single(a => a.Name == "name").Type = "integer";
                AssertBreaking(baseline, live, "MCP update_fleet: argument 'name' type string -> integer");

                live = Clone(baseline);
                live.Rest.Single(r => r.Method == "GET" && r.Route == "/api/v1/fleets").Auth = "AdminOnly";
                AssertBreaking(baseline, live, "REST GET /api/v1/fleets: authorization tightened Authenticated -> AdminOnly");

                live = Clone(baseline);
                live.Settings.Single(s => s.Key == "admiralPort").Type = "string";
                AssertBreaking(baseline, live, "Setting admiralPort: type int -> string");
            }));

            cases.Add(Case("comparer_allows_additions_and_experimental_changes", "Additions and experimental removals are not breaking", TestTags.Positive, () =>
            {
                ApiSurfaceDocument baseline = ApiSurfaceFiles.LoadBaseline();
                ApiSurfaceDocument live = Clone(baseline);
                live.Rest.Add(new ApiRestRoute { Method = "GET", Route = "/api/v1/zz-new", Auth = "Authenticated" });
                live.Mcp.Add(new ApiMcpTool { Name = "zz_new_tool", Auth = "Authenticated" });
                live.Mcp.Single(t => t.Name == "create_fleet").Arguments.Add(new ApiMcpArgument { Name = "zzOptional", Type = "string", Required = false });
                live.Settings.Add(new ApiSettingKey { Key = "zzNewSetting", Type = "bool", Default = "false" });
                live.Rest.RemoveAll(r => r.Experimental);
                live.Mcp.RemoveAll(t => t.Experimental);
                live.Settings.RemoveAll(s => s.Experimental);
                ApiSurfaceDiff diff = ApiSurfaceComparer.Compare(baseline, live);
                AssertTrue(diff.Breaking.Count == 0, "unexpected breaking changes:\n" + ApiSurfaceComparer.Format(diff));
                AssertTrue(diff.Additions.Count >= 4, "additions are reported");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "API Contract",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ApiSurfaceDocument Clone(ApiSurfaceDocument document)
        {
            return ApiSurfaceFiles.FromJson(ApiSurfaceFiles.ToJson(document));
        }

        private static void AssertBreaking(ApiSurfaceDocument baseline, ApiSurfaceDocument live, string expected)
        {
            ApiSurfaceDiff diff = ApiSurfaceComparer.Compare(baseline, live);
            AssertTrue(diff.Breaking.Any(b => b.StartsWith(expected, StringComparison.Ordinal)),
                "expected a breaking change starting with '" + expected + "', got:\n" + ApiSurfaceComparer.Format(diff));
        }

        private static async Task AssertErrorAsync(HttpResponseMessage response, int status, string error)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(status, (int)response.StatusCode, "status for " + response.RequestMessage?.RequestUri + ": " + body);
            ApiErrorProbe probe = ApiErrorProbe.From(body);
            AssertEqual(error, probe.Error?.ToString(), "Error code in " + body);
            AssertEqual(status, probe.StatusCode, "StatusCode in " + body);
            AssertNotNull(JsonShape.TopLevelProperty(body, "Message"), "Message in " + body);
        }

        private static async Task<E2eWebSocketFrame> SendCommandAsync(ClientWebSocket ws, string action, string? id = null)
        {
            string payload = id == null
                ? "{\"Route\":\"command\",\"action\":\"" + action + "\"}"
                : "{\"Route\":\"command\",\"action\":\"" + action + "\",\"id\":\"" + id + "\"}";
            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

            byte[] buffer = new byte[1048576];
            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                while (true)
                {
                    StringBuilder message = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                        message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    E2eWebSocketFrame? frame = E2eWebSocketFrame.Parse(message.ToString());
                    // Replies echo the action; the hub's exception reply carries none and belongs to the one command in flight.
                    if (frame != null && (frame.Type == "command.result" || frame.Type == "command.error") && (frame.Action == action || frame.Action == null))
                        return frame;
                }
            }
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

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
