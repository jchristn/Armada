namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Tui.Screens.Admin;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui.Bodies;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless flows for the API Explorer against a stubbed server and a small OpenAPI document: operation listing and
    /// filtering, request building with path parameters and an example body, snippets, send, abort state, and saving
    /// the response to a file.
    /// </summary>
    public sealed class TuiApiExplorerSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.System.ApiExplorer";

        /// <summary>
        /// The OpenAPI document the stubs serve.
        /// </summary>
        internal const string OpenApiJson = "{\"openapi\":\"3.0.1\",\"paths\":{"
            + "\"/api/v1/missions/{id}\":{"
            + "\"get\":{\"operationId\":\"getMission\",\"summary\":\"Get mission\",\"tags\":[\"Missions\"],\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"description\":\"Mission ID\",\"schema\":{\"type\":\"string\"}}]},"
            + "\"put\":{\"operationId\":\"updateMission\",\"summary\":\"Update mission\",\"tags\":[\"Missions\"],\"parameters\":[{\"name\":\"id\",\"in\":\"path\",\"required\":true,\"schema\":{\"type\":\"string\"}}],\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"$ref\":\"#/components/schemas/MissionUpdate\"}}}}}},"
            + "\"/api/v1/fleets\":{\"get\":{\"operationId\":\"listFleets\",\"summary\":\"List fleets\",\"tags\":[\"Fleets\"],\"parameters\":[{\"name\":\"pageSize\",\"in\":\"query\",\"example\":25,\"schema\":{\"type\":\"integer\"}},{\"name\":\"X-Token\",\"in\":\"header\",\"schema\":{\"type\":\"string\"}}]}}"
            + "},\"components\":{\"schemas\":{\"MissionUpdate\":{\"type\":\"object\",\"properties\":{\"Title\":{\"type\":\"string\",\"example\":\"New title\"},\"Priority\":{\"type\":\"integer\"},\"Status\":{\"type\":\"string\",\"enum\":[\"Pending\",\"Complete\"]}}}}}}";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "operations_listed_and_filtered", "Operations load from /openapi.json and filter by category and text", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/api-explorer", Stub()))
                {
                    ApiExplorerScreen screen = Screen(host);
                    AssertTrue(host.PumpUntil(() => screen.Selected != null), "operation selected");
                    AssertEqual(3, screen.Operations.Count, "operations");
                    AssertEqual("getMission", screen.Selected!.Id, "first operation selected");
                    AssertEqual(3, screen.CategoryField.Options.Count, "All plus two categories");
                    string frame = host.Screen();
                    TuiScreenDump.Write("api-explorer", frame);
                    TuiCase.Contains(frame, "API Explorer", "title");
                    TuiCase.Contains(frame, "GET /api/v1/missions/{id} -- Get mission", "operation label");
                    TuiCase.Contains(frame, "Path Parameters (1)", "path section");
                    screen.CategoryField.Choose(screen.CategoryField.Options.First(o => o.Value == "Fleets"));
                    host.Pump();
                    AssertEqual(1, screen.OperationField.Options.Count(o => o.Value == "listFleets"), "fleets op listed");
                    AssertFalse(screen.OperationField.Options.Any(o => o.Value == "updateMission"), "missions op filtered out");
                    screen.CategoryField.Choose(screen.CategoryField.Options.First(o => o.Value == "All"));
                    screen.FilterField.Value = "update";
                    host.Pump();
                    AssertTrue(screen.OperationField.Options.Any(o => o.Value == "updateMission"), "text filter keeps update");
                    AssertFalse(screen.OperationField.Options.Any(o => o.Value == "listFleets"), "text filter drops fleets");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "build_request_and_snippets", "Path parameters, the example body, and the curl, fetch, and C# snippets", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/api-explorer/updateMission", Stub()))
                {
                    ApiExplorerScreen screen = Screen(host);
                    AssertTrue(host.PumpUntil(() => screen.Selected != null && screen.Selected.Id == "updateMission"), "route operation selected");
                    AssertNotNull(screen.BodyField, "example body field");
                    ApiExplorerExampleBody example = JsonHelper.Deserialize<ApiExplorerExampleBody>(screen.BodyField!.Value);
                    AssertEqual("New title", example.Title, "example body: " + screen.BodyField.Value);
                    AssertEqual("Pending", example.Status, "enum example");
                    screen.PathFields["id"].Value = "msn_9";
                    ApiExplorerRequestPreview preview = screen.BuildPreview()!;
                    AssertEqual("/api/v1/missions/msn_9", preview.PathAndQuery, "path");
                    Dictionary<string, string> code = ApiExplorerSpec.Snippets(preview);
                    AssertTrue(code["curl"].StartsWith("curl -X PUT \"http://127.0.0.1:9/api/v1/missions/msn_9\"", StringComparison.Ordinal), code["curl"]);
                    AssertTrue(code["curl"].Contains("tok_env"), "auth header in curl");
                    AssertTrue(code["curl"].Contains("-H \"Content-Type: application/json\""), "content type");
                    AssertTrue(code["curl"].Contains("--data '{"), "body");
                    AssertTrue(code["fetch"].Contains("method: \"PUT\""), "fetch");
                    AssertTrue(code["csharp"].Contains("HttpMethod.Put"), "csharp");
                    screen.ResponsePane.SelectView("code");
                    host.Pump();
                    string frame = host.Screen();
                    TuiScreenDump.Write("api-explorer-code", frame);
                    TuiCase.Contains(frame, "curl -X PUT", "curl preview on screen");
                    TuiCase.Contains(frame, "PUT http://127.0.0.1:9/api/v1/missions/msn_9", "url preview");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "send_and_save", "Send calls the endpoint and shows status and body; Save Response writes the body to a file", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/missions/msn_9", "{\"Id\":\"msn_9\",\"Title\":\"Explorer\"}");
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/api-explorer", stub))
                {
                    ApiExplorerScreen screen = Screen(host);
                    AssertTrue(host.PumpUntil(() => screen.Selected != null), "operation selected");
                    screen.PathFields["id"].Value = "msn_9";
                    host.Tui.Context.Commands.Execute("ApiExplorerScreen.send");
                    AssertTrue(host.PumpUntil(() => screen.ResponsePane.Response != null), "response");
                    AssertTrue(stub.CountFor("GET", "/api/v1/missions/msn_9") == 1, "endpoint called");
                    AssertEqual(200, screen.ResponsePane.Response!.Status, "status");
                    AssertTrue(host.WaitForText("\"Title\": \"Explorer\""), "body pretty-printed");
                    AssertTrue(host.WaitForText("Request completed with status 200."), "toast");
                    TuiScreenDump.Write("api-explorer-response", host.Screen());
                    screen.ResponsePane.SelectView("headers");
                    AssertTrue(host.WaitForText("content-type"), "headers view");

                    string path = Path.Combine(host.TempDir, "out", "response.json");
                    host.Tui.Context.Commands.Execute("ApiExplorerScreen.save");
                    AssertTrue(host.WaitForText("File path"), "path prompt");
                    host.Press("ctrl+u");
                    host.Type(path);
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => File.Exists(path)), "file written");
                    AssertEqual("Explorer", JsonHelper.Deserialize<ApiExplorerExampleBody>(File.ReadAllText(path)).Title, "body saved");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "send_error_and_abort_state", "A failing status still renders, and the abort button only shows while sending", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/missions/missing", "{\"Error\":\"NotFound\",\"Message\":\"Mission not found\"}", System.Net.HttpStatusCode.NotFound);
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/api-explorer", stub))
                {
                    ApiExplorerScreen screen = Screen(host);
                    AssertTrue(host.PumpUntil(() => screen.Selected != null), "operation selected");
                    AssertFalse(screen.Sending, "idle");
                    screen.PathFields["id"].Value = "missing";
                    screen.Send();
                    AssertTrue(host.PumpUntil(() => screen.ResponsePane.Response != null), "response");
                    AssertEqual(404, screen.ResponsePane.Response!.Status, "status");
                    AssertFalse(screen.ResponsePane.Response.Ok, "not ok");
                    AssertTrue(host.WaitForText("Mission not found"), "error body");
                    AssertFalse(screen.Sending, "done");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI API Explorer", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            return TuiRequestHistorySuite.Stub();
        }

        private static ApiExplorerScreen Screen(TuiTestHost host)
        {
            AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is ApiExplorerScreen), "explorer screen");
            return (ApiExplorerScreen)host.Tui.Shell.Screen!;
        }
    }
}
