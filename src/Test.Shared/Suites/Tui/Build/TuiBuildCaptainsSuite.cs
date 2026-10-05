namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Captains and the captain page (W4.7) against a stubbed client: grid, form with Mux fields, tools, log, quarantine.
    /// </summary>
    public sealed class TuiBuildCaptainsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Build.Captains";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list", "Captains lists tier and quarantine, starts planning, stops, recalls, restarts, stops all, views tools", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 48, "/captains", stub))
                {
                    AssertTrue(host.WaitForText("claude-1"), "rows\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Premium]", "tier badge");
                    TuiCase.Contains(frame, "[quarantined]", "quarantine tag");
                    TuiCase.Contains(frame, "msn_1234...", "current mission");
                    CaptainsScreen screen = (CaptainsScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("home");
                    AssertEqual("cpt_1", screen.Grid.Current!.Id, "claude first");
                    host.Press(".");
                    AssertTrue(host.WaitForText("Start Planning"), "row menu has Start Planning for idle captain");
                    host.Press("esc");
                    host.Press("P");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/planning"), "start planning");
                    AssertEqual("cpt_1", host.Tui.Context.Router.Current!.Query["captainId"], "captain handoff");
                    host.Tui.Context.Navigate("/captains");
                    AssertTrue(host.WaitForText("mux-1"), "back");
                    screen = (CaptainsScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("home").Press("down");
                    host.Press(".");
                    TuiCase.NotContains(host.Screen(), "Start Planning", "quarantined captain cannot plan");
                    host.Press("esc");
                    host.Press("x");
                    TuiCase.Contains(host.Screen(), "Stop captain \"mux-1\"? The captain process will be terminated.", "stop text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/captains/cpt_2/stop") == 1), "stop call");
                    host.Press("R");
                    TuiCase.Contains(host.Screen(), "will be recalled from its current", "recall text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/captains/cpt_2/recall") == 1), "recall call");
                    host.Press("X");
                    TuiCase.Contains(host.Screen(), "Stop ALL captains?", "stop all text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/captains/stop-all") == 1), "stop all call");
                    host.Press("t");
                    AssertTrue(host.WaitForText("Configured MCP Servers"), "tools viewer\n" + host.Screen());
                    frame = host.Screen();
                    TuiCase.Contains(frame, "armada_status", "tool listed");
                    TuiCase.Contains(frame, "[Accessible]", "accessible badge");
                    TuiCase.Contains(frame, "Runtime Internal Tools", "internal tools section");
                    host.Press("esc");
                    host.Press("r");
                    TuiCase.Contains(host.Screen(), "Restart captain \"mux-1\"?", "restart text");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "form", "Create Captain validates runtime, shows Mux fields with discovery, and writes runtime options", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("POST", "/api/v1/captains", "{\"Id\":\"cpt_new\",\"Name\":\"mux-2\",\"Runtime\":\"Mux\",\"State\":\"Idle\"}");
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/captains", stub))
                {
                    AssertTrue(host.WaitForText("claude-1"), "rows");
                    host.Press("n");
                    AssertTrue(host.WaitForText("Create Captain"), "form");
                    TuiCase.NotContains(host.Screen(), "Mux Config Directory", "mux fields hidden");
                    host.Type("mux-2");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("A selection is required."), "runtime required\n" + host.Screen());
                    OpsFormDialog dialog = (OpsFormDialog)host.App.Modals.Top!;
                    Armada.Tui.Widgets.SelectField<string> runtime = (Armada.Tui.Widgets.SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Runtime").Field!;
                    runtime.Choose(runtime.Options.First(o => o.Value == "Mux"));
                    AssertTrue(host.WaitForText("Mux Config Directory"), "mux fields shown\n" + host.Screen());
                    AssertTrue(host.WaitForText("1 saved Mux endpoint(s) available."), "discovery\n" + host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Mux captains require a named Mux endpoint."), "endpoint required");
                    Armada.Tui.Widgets.InputField endpoint = (Armada.Tui.Widgets.InputField)dialog.Form.Rows.First(r => r.Label == "Mux Endpoint").Field!;
                    endpoint.Value = "local-llm";
                    Armada.Tui.Widgets.InputField temperature = (Armada.Tui.Widgets.InputField)dialog.Form.Rows.First(r => r.Label == "Mux Temperature").Field!;
                    temperature.Value = "0.2";
                    OpsCheckField approve = (OpsCheckField)dialog.Form.Rows.First(r => r.Label == "Auto-approve").Field!;
                    approve.Checked = false;
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/captains") == 1), "create call");
                    string body = stub.Bodies.Last(b => b.Contains("mux-2"));
                    AssertTrue(body.Contains("\"Runtime\":\"Mux\""), "runtime: " + body);
                    AssertTrue(body.Contains("endpoint") && body.Contains("local-llm") && body.Contains("0.2") && body.Contains("autoApprove") && body.Contains("false"), "runtime options: " + body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Captain \"mux-2\" created."))), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail", "The captain page shows Mux settings and quarantine, lifts it, opens the log with Readable, and removes", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/captains/cpt_2", "{\"Id\":\"cpt_2\",\"Name\":\"mux-1\",\"Runtime\":\"Mux\",\"State\":\"Quarantined\",\"QuarantineReason\":\"Too many failures\",\"SystemInstructions\":\"Be careful.\",\"RuntimeOptionsJson\":\"{\\\"schemaVersion\\\":1,\\\"endpoint\\\":\\\"local-llm\\\"}\",\"CurrentMissionId\":\"msn_1234567890\",\"RecoveryAttempts\":3,\"CreatedUtc\":\"2026-10-02T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T10:00:00Z\"}");
                stub.Json("GET", "/api/v1/missions/msn_1234567890", "{\"Id\":\"msn_1234567890\",\"Title\":\"Fix parser\",\"Status\":\"InProgress\",\"BranchName\":\"armada/fix\",\"Priority\":100}");
                stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[{\"Id\":\"msn_old\",\"Title\":\"Old work\",\"Status\":\"Complete\",\"CreatedUtc\":\"2026-10-01T10:00:00Z\"}],\"TotalRecords\":1}");
                stub.Json("POST", "/api/v1/captains/cpt_2/unquarantine", "{\"Id\":\"cpt_2\",\"Name\":\"mux-1\",\"Runtime\":\"Mux\",\"State\":\"Idle\"}");
                stub.Json("GET", "/api/v1/captains/cpt_2/log", "{\"Log\":\"raw line\",\"Lines\":1,\"TotalLines\":1,\"Entries\":[{\"Text\":\"reading file\",\"IsToolCall\":true,\"ToolName\":\"Read\"},{\"Text\":\"token sk-***\",\"Redacted\":true}]}");
                stub.On("DELETE", "/api/v1/captains/cpt_2", b => BuildStubs.NoContent());
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/captains/cpt_2", stub))
                {
                    AssertTrue(host.WaitForText("Too many failures"), "quarantine\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "local-llm", "mux endpoint");
                    TuiCase.Contains(frame, "Mux default", "mux config default");
                    TuiCase.Contains(frame, "Be careful.", "instructions");
                    TuiCase.Contains(frame, "Fix parser", "current mission card");
                    TuiCase.Contains(frame, "Lift Quarantine", "lift button");
                    AssertTrue(stub.Requests.Any(r => r.StartsWith("GET /api/v1/missions/summaries?", StringComparison.Ordinal) && r.Contains("captainId=cpt_2")), "missions by captain");
                    host.Press("q");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/captains/cpt_2/unquarantine") == 1), "unquarantine");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Quarantine lifted for \"mux-1\"."))), "toast");
                    host.Press("l");
                    AssertTrue(host.WaitForText("[tool: Read] reading file"), "readable log\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "[redacted]", "redacted tag");
                    AssertTrue(stub.Requests.Any(r => r.Contains("/captains/cpt_2/log?lines=500") && r.Contains("formatted=true")), "500 readable lines: " + String.Join("\n", stub.Requests));
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.Contains("/captains/cpt_2/log?lines=500") && !r.Contains("formatted=true"))), "raw log request: " + String.Join("\n", stub.Requests.Where(r => r.Contains("/log"))));
                    host.Press("esc");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Remove captain \"mux-1\"? This cannot be undone.", "remove text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/captains/cpt_2") == 1), "remove");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/captains"), "back to list");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI captains", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = BuildStubs.Server();
            stub.On("POST", "/api/v1/captains/cpt_2/stop", b => BuildStubs.NoContent());
            stub.On("POST", "/api/v1/captains/cpt_2/recall", b => BuildStubs.NoContent());
            stub.On("POST", "/api/v1/captains/stop-all", b => BuildStubs.NoContent());
            stub.Json("GET", "/api/v1/runtimes/mux/endpoints", "{\"Success\":true,\"Endpoints\":[{\"Name\":\"local-llm\",\"AdapterType\":\"openai\",\"Model\":\"qwen\"}]}");
            stub.Json("GET", "/api/v1/captains/cpt_2/tools", "{\"CaptainId\":\"cpt_2\",\"CaptainName\":\"mux-1\",\"Runtime\":\"Mux\",\"ToolsAccessible\":true,\"AvailabilityVerified\":true,\"Summary\":\"Armada tools reachable.\",\"ConfiguredServerCount\":1,\"ReachableServerCount\":1," +
                "\"Servers\":[{\"Name\":\"armada\",\"SourceKind\":\"McpServer\",\"Transport\":\"http\",\"Url\":\"http://127.0.0.1:7891/rpc\",\"Enabled\":true,\"Reachable\":true,\"ToolCount\":1,\"Status\":\"Reachable\"}]," +
                "\"Tools\":[{\"Name\":\"armada_status\",\"Description\":\"Status\",\"RegistrationSource\":\"armada\",\"SourceKind\":\"McpServer\"}]}");
            return stub;
        }
    }
}
