namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens.Operations;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Mission detail (W3.8) against a stubbed client: panels, landing preview, pull request, review resolution, edit,
    /// transition, land, delete, and the description and playbook panels.
    /// </summary>
    public sealed class TuiOpsMissionDetailSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Ops.MissionDetail";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "panels", "Shows header actions, landing preview, pull request, fields, description, linked items, and playbooks", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/missions/msn_r", Stub()))
                {
                    AssertTrue(host.WaitForText("Ready To Land"), "landing preview\n" + host.Screen());
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Fix tables", "heading");
                    TuiCase.Contains(frame, "[ Resolve Review R ]", "resolve review button");
                    TuiCase.Contains(frame, "[ Instructions i ]", "instructions button");
                    TuiCase.NotContains(frame, "Mark Complete M", "no mark complete with a review gate");
                    TuiCase.Contains(frame, "armada/fix-tables -> main", "preview branches");
                    TuiCase.Contains(frame, "No landing blockers are currently predicted for this mission.", "no blockers");
                    AssertTrue(host.WaitForText("acme/demo"), "pull request repository\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "build  completed / success", "pr check");
                    host.Press("/").Type("Waiting").Press("enter");
                    TuiCase.Contains(host.Screen(), "Waiting Review", "review gate field");
                    host.Press("]");
                    AssertTrue(host.WaitForText("Heading one"), "description markdown");
                    host.Press("]");
                    AssertTrue(host.WaitForText("Lint"), "linked check\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Staging deploy", "linked deployment");
                    host.Press("]");
                    AssertTrue(host.WaitForText("style.md"), "playbook snapshot");
                    TuiCase.Contains(host.Screen(), "Attach Into Worktree", "delivery mode");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "actions", "Resolve Review, Edit, Transition, Land, and Delete call the dashboard endpoints", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/missions/msn_r", stub))
                {
                    AssertTrue(host.WaitForText("Ready To Land"), "loaded");
                    host.Press("R");
                    AssertTrue(host.WaitForText("Choose how to resolve this review gate."), "resolve dialog");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/missions/msn_r/review/approve") == 1), "approve");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Review approved for \"Fix tables\"."))), "approve toast");
                    host.Press("e");
                    AssertTrue(host.WaitForText("Edit Mission"), "edit dialog");
                    host.Press("ctrl+u").Type("Fix tables v2");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/missions/msn_r") >= 1), "update call");
                    string body = stub.Bodies.Last(b => b.Contains("Fix tables v2"));
                    AssertTrue(body.Contains("\"VesselId\":\"vsl_demo\""), "full record keeps the vessel: " + body);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Mission \"Fix tables v2\" saved."))), "save toast");
                    host.Press("t");
                    AssertTrue(host.WaitForText("Transition Mission Status"), "transition");
                    TuiCase.Contains(host.Screen(), "Current status: Review", "current status");
                    host.Press("enter").Type("LandingFailed").Press("enter");
                    TuiOpsMissionsSuite.Settle(host);
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Bodies.Any(b => b.Contains("\"Status\":\"LandingFailed\""))), "transition to LandingFailed (detail offers it)");
                    host.Press("d");
                    AssertTrue(host.WaitForText("+new line"), "diff\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Diff: Fix tables", "diff title");
                    host.Press("esc");
                    host.Press("i");
                    AssertTrue(host.WaitForText("Instructions: CLAUDE.md"), "instructions");
                    host.Press("esc");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Permanently delete mission", "delete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("DELETE /api/v1/missions/msn_r") == 1), "delete");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/missions"), "back to missions");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "land_and_complete", "Land shows for WorkProduced; Mark Complete for Review without a gate; Run Check hands off", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/missions/msn_w", "{\"Id\":\"msn_w\",\"Title\":\"Ship it\",\"Status\":\"WorkProduced\",\"VesselId\":\"vsl_demo\",\"BranchName\":\"armada/ship\",\"Priority\":100,\"CreatedUtc\":\"2026-10-04T08:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T08:00:00Z\"}");
                stub.Json("GET", "/api/v1/missions/msn_w/landing-preview", "{\"VesselId\":\"vsl_demo\",\"TargetBranch\":\"main\",\"BranchCategory\":\"Feature\",\"IsReadyToLand\":false,\"Issues\":[{\"Code\":\"dirty\",\"Severity\":\"Warning\",\"Title\":\"Checks missing\",\"Message\":\"Run the build first\"}]}");
                stub.Json("POST", "/api/v1/missions/msn_w/retry-landing", "{\"Success\":true}");
                stub.Json("GET", "/api/v1/missions/msn_c", "{\"Id\":\"msn_c\",\"Title\":\"Docs\",\"Status\":\"Review\",\"RequiresReview\":false,\"Priority\":100,\"CreatedUtc\":\"2026-10-04T08:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T08:00:00Z\"}");
                stub.Json("PUT", "/api/v1/missions/msn_c/status", "{\"Id\":\"msn_c\",\"Title\":\"Docs\",\"Status\":\"Complete\"}");
                using (TuiTestHost host = TuiCase.SignedIn(150, 50, "/missions/msn_w", stub))
                {
                    AssertTrue(host.WaitForText("Checks missing"), "issue shown\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "Needs Review", "not ready");
                    TuiCase.Contains(host.Screen(), "[ Land L ]", "land button");
                    host.Press("L");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/missions/msn_w/retry-landing") == 1), "land call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Notifications.ActiveToasts().Any(t => t.Text.Contains("Landing succeeded! Mission status updated."))), "land toast");
                    host.Press("k");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/delivery"), "run check handoff");
                    AssertEqual("msn_w", host.Tui.Context.Router.Current!.Query["missionId"], "mission id handed off");
                    AssertEqual("armada/ship", host.Tui.Context.Router.Current!.Query["branchName"], "branch handed off");
                    host.Tui.Context.Navigate("/missions/msn_c");
                    AssertTrue(host.WaitForText("[ Mark Complete M ]"), "mark complete\n" + host.Screen());
                    host.Press("M");
                    TuiCase.Contains(host.Screen(), "graduate out of Review", "mark complete text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/missions/msn_c/status") == 1), "complete transition");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI mission detail", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/missions/msn_r", "{\"Id\":\"msn_r\",\"Title\":\"Fix tables\",\"Description\":\"# Heading one\\n\\nSome *text*.\",\"Status\":\"Review\",\"RequiresReview\":true,\"Priority\":100,\"VesselId\":\"vsl_demo\",\"CaptainId\":\"cpt_1\",\"VoyageId\":\"vyg_1\",\"BranchName\":\"armada/fix-tables\",\"PrUrl\":\"https://github.com/acme/demo/pull/7\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"," +
                "\"PlaybookSnapshots\":[{\"FileName\":\"style.md\",\"Description\":\"House style\",\"Content\":\"Use tabs.\",\"DeliveryMode\":\"AttachIntoWorktree\"}]}");
            stub.Json("GET", "/api/v1/missions/msn_r/landing-preview", "{\"VesselId\":\"vsl_demo\",\"SourceBranch\":\"armada/fix-tables\",\"TargetBranch\":\"main\",\"BranchCategory\":\"Feature\",\"IsReadyToLand\":true,\"Issues\":[]}");
            stub.Json("GET", "/api/v1/missions/msn_r/github/pull-request", "{\"Repository\":\"acme/demo\",\"Number\":7,\"Url\":\"https://github.com/acme/demo/pull/7\",\"Title\":\"Fix tables\",\"State\":\"open\",\"ReviewStatus\":\"Approved\",\"BaseRefName\":\"main\",\"HeadRefName\":\"armada/fix-tables\",\"Reviews\":[{\"ReviewerLogin\":\"octo\",\"State\":\"APPROVED\"}],\"Checks\":[{\"Name\":\"build\",\"Status\":\"completed\",\"Conclusion\":\"success\"}]}");
            stub.Json("GET", "/api/v1/check-runs", "{\"Success\":true,\"Objects\":[{\"Id\":\"chk_1\",\"Label\":\"Lint\",\"Type\":\"Lint\",\"Status\":\"Passed\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/deployments", "{\"Success\":true,\"Objects\":[{\"Id\":\"dpl_1\",\"Title\":\"Staging deploy\",\"EnvironmentName\":\"staging\",\"Status\":\"Succeeded\",\"VerificationStatus\":\"Passed\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"Objects\":[{\"Id\":\"vsl_demo\",\"Name\":\"DemoRepo\",\"DefaultBranch\":\"main\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\"}],\"TotalRecords\":1}");
            stub.Json("POST", "/api/v1/missions/msn_r/review/approve", "{\"Id\":\"msn_r\",\"Title\":\"Fix tables\"}");
            stub.Json("PUT", "/api/v1/missions/msn_r", "{\"Id\":\"msn_r\",\"Title\":\"Fix tables v2\"}");
            stub.Json("PUT", "/api/v1/missions/msn_r/status", "{\"Id\":\"msn_r\",\"Title\":\"Fix tables\",\"Status\":\"LandingFailed\"}");
            stub.Json("GET", "/api/v1/missions/msn_r/diff", "{\"Diff\":\"diff --git a/x b/x\\n+new line\\n\"}");
            stub.Json("GET", "/api/v1/missions/msn_r/instructions", "{\"FileName\":\"CLAUDE.md\",\"Content\":\"# Do the thing\"}");
            stub.On("DELETE", "/api/v1/missions/msn_r", b => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/voyages", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/users", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            return stub;
        }
    }
}
