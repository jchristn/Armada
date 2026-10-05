namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Build;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Vessels tab, vessel form, branches, build context, vessel page, and onboarding (W4.1, W4.4) against a stub.
    /// </summary>
    public sealed class TuiBuildVesselsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Build.Vessels";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "page_size_controls", "The table footer names the page size keys; z cycles the size and Z picks one", () =>
            {
                StubHttpHandler stub = BuildStubs.Server();
                using (TuiTestHost host = TuiCase.SignedIn(180, 48, "/vessels", stub))
                {
                    AssertTrue(host.WaitForText("DemoRepo"), "rows");
                    TuiCase.Contains(host.Screen(), "z/Z Page size: 25 (10/25/50/100/250)  < > Page  c Columns", "footer explains the keys");
                    host.Press("z");
                    AssertTrue(host.WaitForText("z/Z Page size: 50"), "z cycles to the next size");
                    host.Press("Z");
                    AssertTrue(host.WaitForText("rows per page"), "Z opens the page size picker");
                    host.Press("home").Press("enter");
                    AssertTrue(host.WaitForText("z/Z Page size: 10 "), "the chosen size applies");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "hub_tabs_keep_focus", "With the tab strip focused, Left/Right keep switching hub tabs (not just the first press)", () =>
            {
                StubHttpHandler stub = BuildStubs.Server();
                using (TuiTestHost host = TuiCase.SignedIn(180, 48, "/vessels", stub))
                {
                    HubScreen hub = (HubScreen)host.Tui.Shell.Screen!;
                    string first = hub.Route.Tab!.Key;
                    hub.Scope.Focus(hub.Tabs);
                    host.Press("right");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is HubScreen h && h.Route.Tab!.Key != first), "first Right switches tabs");
                    HubScreen second = (HubScreen)host.Tui.Shell.Screen!;
                    string secondKey = second.Route.Tab!.Key;
                    AssertTrue(second.Tabs.IsFocused, "tab strip still focused after the switch");
                    host.Press("right");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is HubScreen h && h.Route.Tab!.Key != secondKey && h.Route.Tab!.Key != first), "second Right switches tabs again");
                    host.Press("left");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is HubScreen h && h.Route.Tab!.Key == secondKey), "Left goes back");
                    host.Tui.Context.Navigate("/vessels");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is HubScreen h && h.Content.IsFocused), "other navigation focuses the content");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "list", "Vessels lists sync and branch counts, filters by landing mode, and deletes in bulk", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(180, 48, "/vessels", stub))
                {
                    AssertTrue(host.WaitForText("DemoRepo"), "rows\n" + host.Screen());
                    AssertTrue(host.WaitForText("2 ahead"), "sync\n" + host.Screen());
                    AssertTrue(host.WaitForText("3 branches"), "branch count\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "in sync", "in sync");
                    TuiCase.Contains(frame, "LocalMerge (local working dir)", "landing short label");
                    TuiCase.Contains(frame, "Branch ", "default branch column");
                    TuiCase.Contains(frame, "Import repositories", "import button");
                    VesselsScreen screen = (VesselsScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    screen.LandingFilter.Choose(screen.LandingFilter.Options.First(o => o.Value == "PullRequest"));
                    AssertTrue(host.PumpUntil(() => !host.Screen().Contains("DemoRepo")), "landing filter");
                    screen.LandingFilter.Choose(screen.LandingFilter.Options.First(o => o.Value == ""));
                    AssertTrue(host.WaitForText("DemoRepo"), "filter cleared");
                    host.Press("home").Press("space").Press("down").Press("space");
                    TuiCase.Contains(host.Screen(), "2 vessels selected", "bulk bar");
                    host.Press("D");
                    TuiCase.Contains(host.Screen(), "Delete 2 selected vessel(s)?", "bulk text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/vessels/vsl_") == 2), "bulk delete calls");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Deleted 2 vessels."))), "bulk toast");
                    host.Press("I");
                    AssertEqual("/vessels/import", host.Tui.Context.Router.Current!.Path, "import route");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "form", "The vessel form validates, creates, edits the full record, and writes the token override", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("POST", "/api/v1/vessels", "{\"Id\":\"vsl_new\",\"Name\":\"NewRepo\",\"RepoUrl\":\"https://x/new.git\",\"DefaultBranch\":\"main\"}");
                stub.Json("PUT", "/api/v1/vessels/vsl_demo", BuildStubs.Vessel("vsl_demo", "DemoRepo", "flt_web", "LocalMerge"));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/vessels", stub))
                {
                    AssertTrue(host.WaitForText("DemoRepo"), "rows");
                    host.Press("n");
                    AssertTrue(host.WaitForText("Create Vessel"), "form");
                    host.Press("ctrl+s");
                    AssertTrue(host.WaitForText("Name is required."), "name required");
                    OpsFormDialog dialog = (OpsFormDialog)host.App.Modals.Top!;
                    ((InputField)Row(dialog, "Name")).Value = "NewRepo";
                    ((InputField)Row(dialog, "Repository URL")).Value = "https://x/new.git";
                    TuiCase.Contains(host.Screen(), "Merges the mission branch directly", "landing mode description");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/vessels") == 1), "create");
                    string body = stub.Bodies.Last(b => b.Contains("NewRepo"));
                    AssertTrue(body.Contains("\"LandingMode\":\"LocalMerge\"") && body.Contains("\"BranchCleanupPolicy\":\"LocalAndRemote\"") && body.Contains("\"EnableModelContext\":true"), "create defaults: " + body);
                    AssertTrue(!body.Contains("gitHubTokenOverride"), "no token: " + body);
                    host.Press("home").Press("down");
                    host.Press("e");
                    AssertTrue(host.WaitForText("Edit Vessel"), "edit form");
                    dialog = (OpsFormDialog)host.App.Modals.Top!;
                    ((InputField)Row(dialog, "GitHub Token Override")).Value = "ghp_secret";
                    ((OpsTextArea)Row(dialog, "Protected Branch Patterns")).Text = "main\n\nrelease/*";
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/vessels/vsl_demo") == 1), "update");
                    string update = stub.Bodies.Last(b => b.Contains("ghp_secret"));
                    AssertTrue(update.Contains("\"gitHubTokenOverride\":\"ghp_secret\""), "token: " + update);
                    AssertTrue(update.Contains("\"ModelContext\":\"Uses xunit.\"") && update.Contains("\"WorkingDirectory\":\"/work/DemoRepo\""), "full record: " + update);
                    AssertTrue(update.Contains("\"ProtectedBranchPatterns\":[\"main\",\"release/*\"]"), "line list: " + update);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "branches_context", "Manage Branches pushes and merges; Build Context launches a captain", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("POST", "/api/v1/vessels/vsl_demo/branches/push", "{\"VesselId\":\"vsl_demo\",\"Branch\":\"feature/x\",\"Pushed\":true}");
                stub.Json("POST", "/api/v1/vessels/vsl_demo/branches/merge", "{\"VesselId\":\"vsl_demo\",\"Merged\":true,\"Pushed\":true}");
                stub.Json("POST", "/api/v1/vessels/vsl_api/build-context", BuildStubs.Vessel("vsl_api", "ApiRepo", "flt_web", "PullRequest"));
                using (TuiTestHost host = TuiCase.SignedIn(180, 50, "/vessels", stub))
                {
                    AssertTrue(host.WaitForText("DemoRepo"), "rows");
                    VesselsScreen screen = (VesselsScreen)((HubScreen)host.Tui.Shell.Screen!).Content;
                    host.Press("home");
                    AssertEqual("vsl_api", screen.Grid.Current!.Id, "ApiRepo first");
                    host.Press("down");
                    host.Press("b");
                    AssertTrue(host.WaitForText("feature/x"), "branches\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[default]", "default tag");
                    TuiCase.Contains(frame, "+2 / -1", "ahead behind");
                    TuiCase.Contains(frame, "Fix parser", "commit subject");
                    host.Press("down").Press("p");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/vessels/vsl_demo/branches/push") == 1), "push");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"Branch\":\"feature/x\"")), "push body");
                    host.Press("m");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Select a source and a target branch."))), "merge needs branches");
                    VesselBranchesDialog branches = (VesselBranchesDialog)host.App.Modals.Top!;
                    branches.Source.Choose(branches.Source.Options.First(o => o.Value == "feature/x"));
                    branches.Target.Choose(branches.Target.Options.First(o => o.Value == "main"));
                    branches.Merge();
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/vessels/vsl_demo/branches/merge") == 1), "merge");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"Source\":\"feature/x\"") && b.Contains("\"Target\":\"main\"") && b.Contains("\"Push\":true")), "merge body");
                    host.Press("esc");
                    host.PumpUntil(() => !host.App.Modals.IsActive);
                    host.Press("home");
                    host.Press(".");
                    AssertTrue(host.WaitForText("Refine Context"), "refine label for a vessel with model context");
                    host.Press("esc");
                    host.Press("B");
                    AssertTrue(host.WaitForText("Refine Model Context"), "context dialog\n" + host.Screen());
                    AssertTrue(host.WaitForText("claude-1"), "captain preselected");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/vessels/vsl_api/build-context") == 1), "build context");
                    AssertTrue(stub.Bodies.Any(b => b.Contains("\"CaptainId\":\"cpt_1\"")), "captain in body");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Model Context updated for \"ApiRepo\"."))), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "detail", "The vessel page shows readiness, landing preview, fields, and missions; opens edit on ?edit=1; hands off Run Check", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/vessels/vsl_demo?edit=1", stub))
                {
                    AssertTrue(host.WaitForText("Edit Vessel"), "edit form opened by ?edit=1\n" + host.Screen());
                    AssertEqual("/vessels/vsl_demo", host.Tui.Context.Router.Current!.FullPath, "edit flag removed");
                    host.Press("esc");
                    host.PumpUntil(() => !host.App.Modals.IsActive);
                    AssertTrue(host.WaitForText("Needs Attention"), "readiness\n" + host.Screen());
                    // The landing preview loads separately from the readiness panel; wait for it instead of racing it.
                    host.WaitForText("Ready To Land");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Ready To Land", "landing preview");
                    TuiCase.Contains(frame, "Web", "fleet name");
                    TuiCase.Contains(frame, "None (WorkerOnly)", "pipeline");
                    TuiCase.Contains(frame, "Inherited / None", "token state");
                    host.Press("]");
                    AssertTrue(host.WaitForText("Uses xunit."), "model context\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), ".env*", "dock boundary");
                    host.Press("]");
                    AssertTrue(host.WaitForText("Fix parser task"), "missions");
                    host.Press("k");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/delivery"), "run check");
                    AssertEqual("vsl_demo", host.Tui.Context.Router.Current!.Query["vesselId"], "run check vessel");
                    AssertEqual("main", host.Tui.Context.Router.Current!.Query["branchName"], "run check branch");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "onboarding", "Onboarding shows counts, the next step, grouped checklist, and follows actions", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/vessels/vsl_demo", BuildStubs.Vessel("vsl_demo", "DemoRepo", "flt_web", "LocalMerge"));
                using (TuiTestHost host = TuiCase.SignedIn(180, 60, "/vessels/vsl_demo/onboarding", stub))
                {
                    AssertTrue(host.WaitForText("Next Recommended Step"), "next step\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Completed 1/2", "completed count");
                    TuiCase.Contains(frame, "Repository Basics", "group");
                    TuiCase.Contains(frame, "Workflow Profile", "group 2");
                    TuiCase.Contains(frame, "Open Issues", "issues");
                    host.Press("n");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/workflow-profiles/new"), "follows the next step");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI vessels", cases: cases);
        }

        private static TUIKit.Widgets.IWidget Row(OpsFormDialog dialog, string label)
        {
            return (TUIKit.Widgets.IWidget)dialog.Form.Rows.First(r => r.Label == label).Field!;
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = BuildStubs.Server();
            stub.Json("GET", "/api/v1/vessels/vsl_demo/git-status", "{\"VesselId\":\"vsl_demo\",\"CommitsAhead\":2,\"CommitsBehind\":0}");
            stub.Json("GET", "/api/v1/vessels/vsl_api/git-status", "{\"VesselId\":\"vsl_api\",\"CommitsAhead\":0,\"CommitsBehind\":0}");
            stub.Json("GET", "/api/v1/vessels/vsl_demo/branches", "{\"VesselId\":\"vsl_demo\",\"DefaultBranch\":\"main\",\"BranchCount\":3,\"Branches\":[" +
                "{\"Name\":\"main\",\"IsDefault\":true,\"IsCurrent\":true,\"CommitHash\":\"abc1234\",\"CommitSubject\":\"Initial\"}," +
                "{\"Name\":\"feature/x\",\"Ahead\":2,\"Behind\":1,\"CommitHash\":\"def5678\",\"CommitSubject\":\"Fix parser\"}," +
                "{\"Name\":\"armada/msn_1\",\"Ahead\":1}]}");
            stub.Json("GET", "/api/v1/vessels/vsl_api/branches", "{\"VesselId\":\"vsl_api\",\"DefaultBranch\":\"main\",\"BranchCount\":1,\"Branches\":[{\"Name\":\"main\",\"IsDefault\":true}]}");
            stub.On("DELETE", "/api/v1/vessels/vsl_demo", b => BuildStubs.NoContent());
            stub.On("DELETE", "/api/v1/vessels/vsl_api", b => BuildStubs.NoContent());
            stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[{\"Id\":\"msn_9\",\"Title\":\"Fix parser task\",\"Status\":\"Complete\",\"CaptainId\":\"cpt_1\",\"BranchName\":\"armada/fix\",\"CreatedUtc\":\"2026-10-01T10:00:00Z\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/vessels/vsl_demo/readiness", "{\"VesselId\":\"vsl_demo\",\"HasWorkingDirectory\":true,\"HasRepositoryContext\":true,\"AvailableCheckTypes\":[\"Build\"],\"WarningCount\":1,\"ErrorCount\":0," +
                "\"SetupChecklistSatisfiedCount\":1,\"SetupChecklistTotalCount\":2,\"DeploymentEnvironments\":[],\"DetectedToolchains\":[],\"ToolchainProbes\":[]," +
                "\"SetupChecklist\":[{\"Code\":\"working_directory\",\"Severity\":\"Info\",\"Title\":\"Working directory\",\"Message\":\"Configured.\",\"IsSatisfied\":true}," +
                "{\"Code\":\"workflow_profile\",\"Severity\":\"Warning\",\"Title\":\"Workflow profile\",\"Message\":\"No workflow profile resolves for this vessel.\",\"IsSatisfied\":false,\"ActionLabel\":\"Create Workflow Profile\",\"ActionRoute\":\"/workflow-profiles/new\"}]," +
                "\"Issues\":[{\"Code\":\"no_profile\",\"Severity\":\"Warning\",\"Title\":\"No workflow profile\",\"Message\":\"Checks cannot run yet.\"}]}");
            stub.Json("GET", "/api/v1/vessels/vsl_demo/landing-preview", "{\"VesselId\":\"vsl_demo\",\"TargetBranch\":\"main\",\"BranchCategory\":\"Default\",\"IsReadyToLand\":true,\"Issues\":[]}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"}],\"TotalRecords\":1}");
            return stub;
        }
    }
}
