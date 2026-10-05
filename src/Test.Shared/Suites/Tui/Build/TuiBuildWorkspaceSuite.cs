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
    /// Workspace (W4.6) against a stubbed client: picker, tree, editor and save, read-only preview, new folder,
    /// rename, delete, diff, terminal, context, and the Plan and Dispatch handoffs.
    /// </summary>
    public sealed class TuiBuildWorkspaceSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Build.Workspace";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "picker", "The Workspace tab lists vessels with their workspace state and opens one", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 45, "/vessels?tab=workspace", stub))
                {
                    AssertTrue(host.WaitForText("DemoRepo"), "rows\n" + host.Screen());
                    AssertTrue(host.WaitForText("Workspace ready"), "state\n" + host.Screen());
                    AssertTrue(host.WaitForText("1 active mission(s)"), "status\n" + host.Screen());
                    WorkspacePickerScreen screen = (WorkspacePickerScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("/").Type("Demo").Press("esc");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("ApiRepo")), "filter");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/workspace/vsl_demo"), "opens workspace");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "edit_save", "The tree expands folders, opens files in tabs, saves with the content hash, and previews binaries", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/workspace/vsl_demo", stub))
                {
                    AssertTrue(host.WaitForText("README.md"), "tree\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[main]", "branch pill");
                    TuiCase.Contains(frame, "[Dirty working tree]", "dirty pill");
                    TuiCase.Contains(frame, "[1 active mission(s)]", "missions");
                    TuiCase.Contains(frame, "[2 ahead / 0 behind]", "ahead behind");
                    TuiCase.Contains(frame, "Readiness: [Ready]", "readiness");
                    WorkspaceScreen screen = (WorkspaceScreen)host.Tui.Shell.Screen!;
                    host.Press("home").Press("enter");
                    AssertTrue(host.WaitForText("Program.cs"), "folder expanded\n" + host.Screen());
                    host.Press("down").Press("enter");
                    AssertTrue(host.WaitForText("Console.WriteLine"), "file open\n" + host.Screen());
                    AssertEqual("src/Program.cs", screen.ActivePath, "active");
                    host.Type("// edit");
                    AssertTrue(host.WaitForText("Program.cs*"), "dirty tab\n" + host.Screen());
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/workspace/vessels/vsl_demo/file") + stub.Count("POST /api/v1/workspace/vessels/vsl_demo/file") == 1), "save call: " + String.Join("\n", stub.Requests));
                    string body = stub.Bodies.Last(b => b.Contains("ExpectedHash"));
                    AssertTrue(body.Contains("\"ExpectedHash\":\"hash1\"") && body.Contains("// edit"), "save body: " + body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Saved src/Program.cs"))), "saved toast");
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("Program.cs*")), "clean after save");
                    host.Press("esc");
                    screen.Tree.Reveal("logo.png");
                    host.Press("enter");
                    AssertTrue(host.WaitForText("This file is binary and cannot be edited in Workspace."), "binary\n" + host.Screen());
                    host.Press("esc");
                    screen.Tree.Reveal("src/Program.cs");
                    host.Press("space");
                    AssertTrue(host.WaitForText("Selection (1)"), "selection");
                    TuiCase.Contains(host.Screen(), "Active mission overlap", "overlap");
                    host.Press("D");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/dispatch"), "dispatch handoff");
                    IReadOnlyDictionary<string, string> q = host.Tui.Context.Router.Current!.Query;
                    AssertEqual("workspace", q["from"], "from");
                    AssertEqual("vsl_demo", q["vesselId"], "vessel");
                    AssertEqual("Workspace: Program.cs", q["voyageTitle"], "title");
                    AssertTrue(q["prompt"].StartsWith("Touch only src/Program.cs", StringComparison.Ordinal), "prompt: " + q["prompt"]);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "tools", "New folder, rename, delete, diff, terminal, context, and the Plan handoff", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 50, "/workspace/vsl_demo", stub))
                {
                    AssertTrue(host.WaitForText("README.md"), "tree");
                    WorkspaceScreen screen = (WorkspaceScreen)host.Tui.Shell.Screen!;
                    host.Press("N");
                    AssertTrue(host.WaitForText("New folder path"), "folder prompt");
                    host.Press("ctrl+u").Type("docs").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/workspace/vessels/vsl_demo/directory") == 1), "create folder");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"Path\":\"docs\"")), "folder body");
                    screen.Tree.Reveal("README.md");
                    host.Press("R");
                    AssertTrue(host.WaitForText("Rename or move path"), "rename prompt");
                    host.Press("ctrl+u").Type("README2.md").Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/workspace/vessels/vsl_demo/rename") == 1), "rename call");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"NewPath\":\"README2.md\"")), "rename body");
                    screen.Tree.Reveal("README.md");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete README.md?", "delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/workspace/vessels/vsl_demo/entry") == 1), "delete call");
                    host.Press("d");
                    AssertTrue(host.WaitForText("+added line"), "diff\n" + host.Screen());
                    host.Press("esc");
                    host.PumpUntil(() => !host.App.Modals.IsActive);
                    host.Press("t");
                    AssertTrue(host.WaitForText("Run a command in the vessel working tree"), "terminal\n" + host.Screen());
                    host.Type("git status").Press("enter");
                    AssertTrue(host.WaitForText("On branch main"), "stdout\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "exit 0 - 42ms", "exit line");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"Command\":\"git status\"")), "exec body");
                    host.Press("esc");
                    host.PumpUntil(() => !host.App.Modals.IsActive);
                    AssertEqual(3, screen.TerminalLines.Count, "output kept");
                    screen.Tree.Reveal("src");
                    host.Press("enter");
                    AssertTrue(host.WaitForText("Program.cs"), "expanded");
                    host.Press("down").Press("space");
                    host.Press("C");
                    AssertTrue(host.WaitForText("Vessel Context"), "context dialog\n" + host.Screen());
                    OpsFormDialog dialog = (OpsFormDialog)host.App.Modals.Top!;
                    Armada.Tui.Widgets.Button append = (Armada.Tui.Widgets.Button)dialog.Form.Rows.First(r => r.Label == "  ").Field!;
                    append.Press();
                    OpsTextArea model = (OpsTextArea)dialog.Form.Rows.First(r => r.Label == "Model Context").Field!;
                    AssertTrue(host.PumpUntil(() => model.Text.Contains("## Workspace Selection")), "appended snippet: " + model.Text);
                    TuiCase.Contains(model.Text, "### src/Program.cs", "snippet path");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/vessels/vsl_demo") == 1), "context save");
                    string body = stub.Bodies.Last(b => b.Contains("Workspace Selection"));
                    AssertTrue(body.Contains("\"WorkingDirectory\":\"/work/DemoRepo\""), "full record: " + body);
                    host.Press("P");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/planning"), "plan handoff");
                    AssertEqual("Plan Program.cs", host.Tui.Context.Router.Current!.Query["title"], "plan title");
                    AssertEqual("flt_web", host.Tui.Context.Router.Current!.Query["fleetId"], "fleet");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI workspace", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = BuildStubs.Server();
            string w = "/api/v1/workspace/vessels/vsl_demo";
            stub.Json("GET", w + "/status", "{\"VesselId\":\"vsl_demo\",\"HasWorkingDirectory\":true,\"RootPath\":\"/work/DemoRepo\",\"BranchName\":\"main\",\"IsDirty\":true,\"CommitsAhead\":2,\"CommitsBehind\":0,\"ActiveMissionCount\":1,\"ActiveMissions\":[{\"MissionId\":\"msn_1\",\"Title\":\"Fix parser\",\"Status\":\"InProgress\",\"ScopedFiles\":[\"src/Program.cs\"]}]}");
            stub.Json("GET", "/api/v1/workspace/vessels/vsl_api/status", "{\"VesselId\":\"vsl_api\",\"HasWorkingDirectory\":false}");
            stub.Json("GET", w + "/tree", "{\"VesselId\":\"vsl_demo\",\"RootPath\":\"/work/DemoRepo\",\"CurrentPath\":\"\",\"Entries\":[" +
                "{\"Name\":\"src\",\"RelativePath\":\"src\",\"IsDirectory\":true,\"IsEditable\":false,\"LastWriteUtc\":\"2026-10-01T10:00:00Z\"}," +
                "{\"Name\":\"README.md\",\"RelativePath\":\"README.md\",\"IsDirectory\":false,\"IsEditable\":true,\"SizeBytes\":20,\"LastWriteUtc\":\"2026-10-01T10:00:00Z\"}," +
                "{\"Name\":\"logo.png\",\"RelativePath\":\"logo.png\",\"IsDirectory\":false,\"IsEditable\":false,\"SizeBytes\":2000,\"LastWriteUtc\":\"2026-10-01T10:00:00Z\"}]}");
            stub.Json("GET", w + "/tree?path=src", "{\"VesselId\":\"vsl_demo\",\"RootPath\":\"/work/DemoRepo\",\"CurrentPath\":\"src\",\"Entries\":[" +
                "{\"Name\":\"Program.cs\",\"RelativePath\":\"src/Program.cs\",\"IsDirectory\":false,\"IsEditable\":true,\"SizeBytes\":40,\"LastWriteUtc\":\"2026-10-01T10:00:00Z\"}]}");
            stub.Json("GET", w + "/file?path=src/Program.cs", "{\"VesselId\":\"vsl_demo\",\"Path\":\"src/Program.cs\",\"Name\":\"Program.cs\",\"Content\":\"Console.WriteLine(1);\",\"ContentHash\":\"hash1\",\"IsEditable\":true,\"Language\":\"csharp\",\"SizeBytes\":21}");
            stub.Json("GET", w + "/file?path=logo.png", "{\"VesselId\":\"vsl_demo\",\"Path\":\"logo.png\",\"Name\":\"logo.png\",\"Content\":\"\",\"ContentHash\":\"h\",\"IsEditable\":false,\"IsBinary\":true,\"Language\":\"plaintext\"}");
            stub.Json("PUT", w + "/file", "{\"Path\":\"src/Program.cs\",\"ContentHash\":\"hash2\",\"SizeBytes\":28,\"LastWriteUtc\":\"2026-10-04T10:00:00Z\"}");
            stub.Json("POST", w + "/file", "{\"Path\":\"src/Program.cs\",\"ContentHash\":\"hash2\",\"SizeBytes\":28,\"LastWriteUtc\":\"2026-10-04T10:00:00Z\"}");
            stub.Json("POST", w + "/directory", "{\"Path\":\"docs\",\"Status\":\"created\"}");
            stub.Json("POST", w + "/rename", "{\"Path\":\"README.md\",\"NewPath\":\"README2.md\",\"Status\":\"renamed\"}");
            stub.Json("DELETE", w + "/entry", "{\"Path\":\"README.md\",\"Status\":\"deleted\"}");
            stub.Json("GET", w + "/diff", "{\"Diff\":\"diff --git a/x b/x\\n+added line\\n\"}");
            stub.Json("POST", w + "/exec", "{\"Command\":\"git status\",\"WorkingDirectory\":\"/work/DemoRepo\",\"ExitCode\":0,\"Stdout\":\"On branch main\\n\",\"Stderr\":\"\",\"TimedOut\":false,\"DurationMs\":42}");
            stub.Json("GET", "/api/v1/vessels/vsl_demo/readiness", "{\"VesselId\":\"vsl_demo\",\"HasWorkingDirectory\":true,\"HasRepositoryContext\":true,\"AvailableCheckTypes\":[],\"ErrorCount\":0,\"WarningCount\":0,\"SetupChecklist\":[],\"Issues\":[],\"DeploymentEnvironments\":[],\"DetectedToolchains\":[],\"ToolchainProbes\":[]}");
            stub.Json("PUT", "/api/v1/vessels/vsl_demo", BuildStubs.Vessel("vsl_demo", "DemoRepo", "flt_web", "LocalMerge"));
            return stub;
        }
    }
}
