namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
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
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/dispatch" && host.Tui.Context.Router.Current!.Tab?.Key == "planning"), "start planning: " + host.Tui.Context.Router.Current!.FullPath);
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
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains/cpt_2/stop") == 1), "stop call");
                    host.Press("R");
                    TuiCase.Contains(host.Screen(), "will be recalled from its current", "recall text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains/cpt_2/stop") == 2), "recall calls the stop route (the server has no recall route)");
                    host.Press("X");
                    TuiCase.Contains(host.Screen(), "Stop ALL captains?", "stop all text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains/stop-all") == 1), "stop all call");
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
                    Armada.Tui.Widgets.SelectField<Armada.Core.Enums.AgentRuntimeEnum?> runtime = (Armada.Tui.Widgets.SelectField<Armada.Core.Enums.AgentRuntimeEnum?>)dialog.Form.Rows.First(r => r.Label == "Runtime").Field!;
                    runtime.Choose(runtime.Options.First(o => o.Value == Armada.Core.Enums.AgentRuntimeEnum.Mux));
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
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains") == 1), "create call");
                    Captain body = stub.LastBody<Captain>("POST", "/api/v1/captains");
                    AssertEqual("mux-2", body.Name, "name");
                    AssertEqual(AgentRuntimeEnum.Mux, body.Runtime, "runtime");
                    AssertNotNull(body.RuntimeOptionsJson, "runtime options sent");
                    MuxCaptainOptions options = JsonHelper.Deserialize<MuxCaptainOptions>(body.RuntimeOptionsJson!);
                    AssertEqual("local-llm", options.Endpoint, "mux endpoint");
                    AssertEqual<double?>(0.2, options.Temperature, "mux temperature");
                    AssertEqual<bool?>(false, options.AutoApprove, "auto-approve off");
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
                    AssertTrue(stub.Saw("GET", "/api/v1/missions/summaries", r => r.QueryValue("captainId") == "cpt_2"), "missions by captain: " + String.Join("\n", stub.Requests));
                    host.Press("q");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains/cpt_2/unquarantine") == 1), "unquarantine");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Quarantine lifted for \"mux-1\"."))), "toast");
                    host.Press("l");
                    AssertTrue(host.WaitForText("[tool: Read] reading file"), "readable log\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "[redacted]", "redacted tag");
                    AssertTrue(stub.Saw("GET", "/api/v1/captains/cpt_2/log", r => r.QueryValue("lines") == "500" && r.QueryValue("formatted") == "true"), "500 readable lines: " + String.Join("\n", stub.Requests));
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => stub.Saw("GET", "/api/v1/captains/cpt_2/log", r => r.QueryValue("lines") == "500" && r.QueryValue("formatted") != "true")), "raw log request: " + String.Join("\n", stub.RequestsFor("GET", "/api/v1/captains/cpt_2/log")));
                    host.Press("esc");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Remove captain \"mux-1\"? This cannot be undone.", "remove text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/captains/cpt_2") == 1), "remove");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/captains"), "back to list");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "evaluate", "Evaluate on the captain page opens a form (Full by default, idle evaluators, packs, allowed personas, run count), submits the options, shows the run and report, and cancels", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/captains/cpt_q", "{\"Id\":\"cpt_q\",\"Name\":\"qwen-cand\",\"Runtime\":\"Mux\",\"Model\":\"qwen3:32b\",\"State\":\"Idle\",\"AllowedPersonas\":\"[\\\"Worker\\\",\\\"Judge\\\"]\",\"CreatedUtc\":\"2026-10-02T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T10:00:00Z\"}");
                stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
                stub.Json("GET", "/api/v1/battery/packs", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":1000,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                    "{\"Id\":\"csharp\",\"Name\":\"CSharp\",\"Version\":1,\"Valid\":true,\"Tasks\":[{\"Id\":\"add\",\"Persona\":\"Worker\"},{\"Id\":\"plan\",\"Persona\":\"Architect\"}]}," +
                    "{\"Id\":\"broken\",\"Version\":1,\"Valid\":false,\"Errors\":[\"manifest missing\"]}]}");
                string running = "{\"Id\":\"mpr_e1\",\"TargetKind\":\"Captain\",\"CaptainId\":\"cpt_q\",\"Depth\":\"Full\",\"Status\":\"Running\",\"PassedCount\":2}";
                stub.Json("POST", "/api/v1/captains/cpt_q/evaluate", running);
                stub.Json("GET", "/api/v1/model-probe-runs/mpr_e1", running);
                stub.Json("POST", "/api/v1/model-probe-runs/mpr_e1/cancel", "{\"Id\":\"mpr_e1\",\"TargetKind\":\"Captain\",\"CaptainId\":\"cpt_q\",\"Depth\":\"Full\",\"Status\":\"Cancelled\",\"PassedCount\":5,\"FailedCount\":1," +
                    "\"FailureReason\":\"Cancelled by an administrator.\",\"Results\":[{\"Probe\":\"AsciiFormat\",\"Outcome\":\"Failed\",\"Summary\":\"2 em dashes\"}]," +
                    "\"Report\":{\"RunId\":\"mpr_e1\",\"Complete\":false,\"IncompleteReason\":\"Battery did not finish.\",\"EvaluatorCaptainName\":\"claude-1\"," +
                    "\"PersonaOutcomes\":[{\"Persona\":\"Judge\",\"Totals\":{\"Key\":\"persona:Judge\",\"Passed\":1,\"Failed\":1,\"CouldNotRun\":0,\"PendingEvaluation\":0}}]," +
                    "\"Narrative\":{\"Status\":\"Skipped\",\"Reason\":\"The run was stopped.\"}}}");
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/captains/cpt_q", stub))
                {
                    AssertTrue(host.WaitForText("qwen-cand"), "captain page\n" + host.Screen());
                    host.Press("v");
                    AssertTrue(host.WaitForText("Evaluate qwen-cand"), "form\n" + host.Screen());
                    OpsFormDialog dialog = (OpsFormDialog)host.App.Modals.Top!;
                    Armada.Tui.Widgets.SelectField<string> depth = (Armada.Tui.Widgets.SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Depth").Field!;
                    AssertEqual("Full", depth.Value, "Full by default");
                    Armada.Tui.Widgets.SelectField<string> evaluator = (Armada.Tui.Widgets.SelectField<string>)dialog.Form.Rows.First(r => r.Label == "Evaluator captain").Field!;
                    AssertEqual("", evaluator.Value, "automatic by default");
                    AssertEqual(",cpt_1", String.Join(",", evaluator.Options.Select(o => o.Value)), "idle captains other than the candidate only");
                    OpsCheckField pack = (OpsCheckField)dialog.Form.Rows.First(r => r.Label == "Battery packs").Field!;
                    AssertEqual("CSharp (csharp)", pack.Label, "valid pack listed");
                    List<string> checks = dialog.Form.Rows.Where(r => r.Field is OpsCheckField).Select(r => ((OpsCheckField)r.Field!).Label).ToList();
                    AssertEqual("CSharp (csharp),Worker,Judge", String.Join(",", checks), "invalid pack hidden; only the allowed personas");
                    Armada.Tui.Widgets.InputField runCount = (Armada.Tui.Widgets.InputField)dialog.Form.Rows.First(r => r.Label == "Run count").Field!;

                    runCount.Value = "11";
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("The run count must be a whole number from 1 to 10."), "run count validated\n" + host.Screen());
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/captains/cpt_q/evaluate"), "nothing submitted");

                    runCount.Value = "2";
                    evaluator.Choose(evaluator.Options.First(o => o.Value == "cpt_1"));
                    pack.Checked = true;
                    ((OpsCheckField)dialog.Form.Rows.First(r => r.Field is OpsCheckField f && f.Label == "Judge").Field!).Checked = true;
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains/cpt_q/evaluate") == 1), "evaluate posted");
                    EvaluateCaptainRequest body = stub.LastBody<EvaluateCaptainRequest>("POST", "/api/v1/captains/cpt_q/evaluate");
                    AssertEqual(ModelProbeDepthEnum.Full, body.Depth, "depth");
                    AssertEqual("cpt_1", body.EvaluatorCaptainId, "evaluator");
                    AssertEqual("csharp", String.Join(",", body.Packs!), "packs");
                    AssertEqual("Judge", String.Join(",", body.Personas!), "personas");
                    AssertEqual(2, body.RunCount, "run count");

                    AssertTrue(host.WaitForText("Live: refreshing every 3 s."), "run viewer\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "2 passed, 0 failed, 0 could not run", "counts");
                    TuiCase.Contains(host.Screen(), "Cancel run", "cancel hint while running");
                    host.Press("c");
                    AssertTrue(host.WaitForText("Incomplete: Battery did not finish."), "incomplete report\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Cancelled by an administrator.", "failure reason");
                    TuiCase.Contains(frame, "Evaluator: claude-1", "evaluator");
                    TuiCase.Contains(frame, "Judge: 1 of 2 passed, 0 pending, 0 could not run", "persona counts");
                    TuiCase.Contains(frame, "Subjective grading skipped: The run was stopped.", "narrative");
                    TuiCase.Contains(frame, "2 em dashes", "probe result");
                    TuiCase.NotContains(frame, "Cancel run", "no cancel once finished");
                    AssertEqual(1, stub.CountFor("POST", "/api/v1/model-probe-runs/mpr_e1/cancel"), "cancel posted once");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "evaluate_options", "The Evaluate request omits empty Full options, sends only the depth for Quick, rejects a bad run count; evaluators are idle others; same-family evaluators are labeled and noted", () =>
            {
                string? error;
                EvaluateCaptainRequest defaults = CaptainEvaluate.Build("Full", "", new List<string>(), new List<string>(), " ", out error);
                AssertNull(error, "defaults valid");
                AssertEqual(ModelProbeDepthEnum.Full, defaults.Depth, "full");
                AssertNull(defaults.EvaluatorCaptainId, "no evaluator");
                AssertNull(defaults.Packs, "empty packs mean the default");
                AssertNull(defaults.Personas, "empty personas mean all allowed");
                AssertNull(defaults.RunCount, "blank run count means the default");
                EvaluateCaptainRequest quick = CaptainEvaluate.Build("Quick", "cpt_x", new List<string> { "csharp" }, new List<string> { "Judge" }, "5", out error);
                AssertEqual(ModelProbeDepthEnum.Quick, quick.Depth, "quick");
                AssertNull(quick.Packs, "quick sends no Full options");
                AssertNull(quick.RunCount, "quick sends no run count");
                foreach (string bad in new[] { "0", "11", "x", "-1", "2.5" })
                {
                    CaptainEvaluate.Build("Full", null, null, null, bad, out error);
                    AssertNotNull(error, "run count " + bad);
                }

                Captain candidate = new Captain("qwen-cand") { Id = "cpt_c", Model = "qwen3:32b", Runtime = AgentRuntimeEnum.Mux, State = CaptainStateEnum.Idle };
                Captain sameFamily = new Captain("qwen-big") { Id = "cpt_s", Model = "qwen3.8:27b", Runtime = AgentRuntimeEnum.Mux, State = CaptainStateEnum.Idle };
                Captain other = new Captain("claude") { Id = "cpt_o", Runtime = AgentRuntimeEnum.ClaudeCode, State = CaptainStateEnum.Idle };
                Captain busy = new Captain("busy") { Id = "cpt_b", Runtime = AgentRuntimeEnum.Codex, State = CaptainStateEnum.Working };
                List<Captain> idle = CaptainEvaluate.IdleEvaluators(candidate, new List<Captain> { candidate, sameFamily, other, busy });
                AssertEqual("cpt_o,cpt_s", String.Join(",", idle.Select(c => c.Id)), "idle others by name");
                Armada.Tui.Services.LocalizationService loc = new Armada.Tui.Services.LocalizationService();
                AssertEqual("qwen-big (same model family)", CaptainEvaluate.EvaluatorLabel(candidate, sameFamily, loc), "same family labeled");
                AssertEqual("claude", CaptainEvaluate.EvaluatorLabel(candidate, other, loc), "other family plain");
                AssertContains("same model family (qwen)", CaptainEvaluate.FamilyNote(candidate, sameFamily, loc) ?? "");
                AssertNull(CaptainEvaluate.FamilyNote(candidate, other, loc), "no note for another family");
                AssertNull(CaptainEvaluate.FamilyNote(candidate, null, loc), "no note for automatic");

                List<BatteryPackInfo> packs = new List<BatteryPackInfo>
                {
                    new BatteryPackInfo { Id = "a", Valid = true, Tasks = new List<BatteryTaskInfo> { new BatteryTaskInfo { Id = "t1", Persona = "Worker" }, new BatteryTaskInfo { Id = "t2", Persona = "Architect" } } },
                    new BatteryPackInfo { Id = "b", Valid = false, Tasks = new List<BatteryTaskInfo> { new BatteryTaskInfo { Id = "t3", Persona = "Judge" } } }
                };
                AssertEqual("Architect,Worker", String.Join(",", CaptainEvaluate.PersonaOptions(candidate, packs)), "all allowed: the valid packs' personas");
                candidate.AllowedPersonas = "[\"Judge\"]";
                AssertEqual("Judge", String.Join(",", CaptainEvaluate.PersonaOptions(candidate, packs)), "the allowed personas");
                candidate.AllowedPersonas = "not json";
                AssertEqual("Architect,Worker", String.Join(",", CaptainEvaluate.PersonaOptions(candidate, packs)), "unparsable allowed list falls back");
                AssertFalse(CaptainEvaluate.IsFinished(new ModelProbeRun { Status = ModelProbeRunStatusEnum.Queued }), "queued");
                AssertTrue(CaptainEvaluate.IsFinished(new ModelProbeRun { Status = ModelProbeRunStatusEnum.Failed }), "failed");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI captains", cases: cases);
        }

        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = BuildStubs.Server();
            stub.On("POST", "/api/v1/captains/cpt_2/stop", b => BuildStubs.NoContent());
            stub.On("POST", "/api/v1/captains/stop-all", b => BuildStubs.NoContent());
            stub.Json("GET", "/api/v1/runtimes/mux/endpoints", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":1000,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[{\"Name\":\"local-llm\",\"AdapterType\":\"openai\",\"Model\":\"qwen\"}]}");
            stub.Json("GET", "/api/v1/captains/cpt_2/tools", "{\"CaptainId\":\"cpt_2\",\"CaptainName\":\"mux-1\",\"Runtime\":\"Mux\",\"ToolsAccessible\":true,\"AvailabilityVerified\":true,\"Summary\":\"Armada tools reachable.\",\"ConfiguredServerCount\":1,\"ReachableServerCount\":1," +
                "\"Servers\":[{\"Name\":\"armada\",\"SourceKind\":\"McpServer\",\"Transport\":\"http\",\"Url\":\"http://127.0.0.1:7891/rpc\",\"Enabled\":true,\"Reachable\":true,\"ToolCount\":1,\"Status\":\"Reachable\"}]," +
                "\"ToolCount\":1,\"McpToolCount\":1,\"Tools\":{\"PageNumber\":1,\"PageSize\":1000,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[{\"Name\":\"armada_status\",\"Description\":\"Status\",\"RegistrationSource\":\"armada\",\"SourceKind\":\"McpServer\"}]}}");
            return stub;
        }
    }
}
