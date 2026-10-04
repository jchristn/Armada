namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Approvals center (W3.3) against a stubbed client: inbox and event sources, the header count, and every
    /// decision per source with the dashboard's API call, body, confirmation, and toast.
    /// </summary>
    public sealed class TuiApprovalsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Approvals";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "sources", "The inbox and entity events feed the queue; the first sync is silent", () =>
            {
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    int arrived = 0;
                    host.Tui.Context.Approvals.Arrived += (s, e) => arrived++;
                    host.Tui.Context.Status.PollAllAsync().GetAwaiter().GetResult();
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Count == 4), "four inbox items");
                    AssertEqual(0, arrived, "first sync does not ring");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Approvals  (4 waiting)", "heading");
                    TuiCase.Contains(frame, "Mission review", "review kind");
                    TuiCase.Contains(frame, "Failed landing", "landing kind");
                    TuiCase.Contains(frame, "Stalled captain", "captain kind");
                    TuiCase.Contains(frame, "Deployment approval", "deployment kind");
                    TuiCase.Contains(frame, "[!4 approvals]", "header count");
                    AssertEqual(ApprovalKindEnum.FailedLanding, host.Tui.Context.Approvals.Items[0].Kind, "critical first");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("mission.changed", "{\"id\":\"msn_9\",\"title\":\"Add tests\",\"status\":\"Review\"}"));
                    host.Pump();
                    AssertEqual(5, host.Tui.Context.Approvals.Count, "review from an event");
                    AssertEqual(1, arrived, "new item raises attention");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("mission.changed", "{\"id\":\"msn_9\",\"title\":\"Add tests\",\"status\":\"InProgress\"}"));
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("captain.changed", "{\"id\":\"cpt_s\",\"name\":\"slow\",\"state\":\"Working\"}"));
                    host.Pump();
                    AssertEqual(3, host.Tui.Context.Approvals.Count, "resolved by events");
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("deployment.changed", "{\"id\":\"dpl_2\",\"title\":\"Prod\",\"status\":\"PendingApproval\"}"));
                    host.Pump();
                    AssertNotNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.DeploymentApproval, "dpl_2"), "deployment from an event");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "review_decisions", "Mission reviews: approve, conditionally approve and more work need feedback, deny", () =>
            {
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    Load(host);
                    ApprovalsScreen screen = Select(host, ApprovalKindEnum.MissionReview);
                    host.Press("c");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "resolve dialog");
                    TuiCase.Contains(host.Screen(), "Resolve Review", "dialog title");
                    host.Press("enter");
                    AssertTrue(host.WaitForText("Add feedback first"), "feedback required for conditional");
                    host.Type("Rename the helper").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/missions/msn_r/review/approve") == 1), "approve endpoint");
                    string body = stub.Bodies.Last(b => b.Contains("Rename the helper"));
                    AssertTrue(body.Contains("\"Conditional\":true") || body.Contains("\"conditional\":true"), "conditional flag: " + body);
                    AssertTrue(host.WaitForText("Conditionally approved"), "toast");
                    AssertNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.MissionReview, "msn_r"), "left the queue");

                    Load(host);
                    Select(host, ApprovalKindEnum.MissionReview);
                    host.Press("m").Type("Add tests").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.Bodies.Any(b => b.Contains("RetryStage") && b.Contains("Add tests"))), "more work: deny with RetryStage");
                    Load(host);
                    Select(host, ApprovalKindEnum.MissionReview);
                    host.Press("d").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.Bodies.Any(b => b.Contains("FailPipeline"))), "deny with FailPipeline");
                    Load(host);
                    Select(host, ApprovalKindEnum.MissionReview);
                    int approvals = stub.Count("POST /api/v1/missions/msn_r/review/approve");
                    host.Press("a").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/missions/msn_r/review/approve") == approvals + 1), "plain approve");
                    AssertTrue(host.WaitForText("Review approved for"), "approve toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "deployment_decisions", "Deployments approve and deny after the dashboard's confirmation", () =>
            {
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    Load(host);
                    Select(host, ApprovalKindEnum.DeploymentApproval);
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "confirm");
                    TuiCase.Contains(host.Screen(), "Approve and execute \"Staging\"?", "dashboard text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/deployments/dpl_1/approve") == 1), "approve call");
                    AssertTrue(host.WaitForText("Deployment \"Staging\" updated."), "toast");
                    Load(host);
                    Select(host, ApprovalKindEnum.DeploymentApproval);
                    host.Press("d");
                    TuiCase.Contains(host.Screen(), "Deny \"Staging\" without executing it?", "deny text");
                    host.Press("n");
                    AssertEqual(0, stub.Count("POST /api/v1/deployments/dpl_1/deny"), "cancel does nothing");
                    host.Press("d").Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/deployments/dpl_1/deny") == 1), "deny call");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "landing_and_captains", "Retry landing; stop, recall, and restart a stalled captain", () =>
            {
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    Load(host);
                    Select(host, ApprovalKindEnum.FailedLanding);
                    host.Press("l");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/missions/msn_l/retry-landing") == 1), "retry landing");
                    AssertTrue(host.WaitForText("Landing succeeded for \"Ship it\""), "toast");
                    Select(host, ApprovalKindEnum.StalledCaptain);
                    host.Press("s");
                    TuiCase.Contains(host.Screen(), "The captain process will be terminated.", "stop text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/captains/cpt_s/stop") == 1), "stop");
                    Load(host);
                    Select(host, ApprovalKindEnum.StalledCaptain);
                    host.Press("R").Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/captains/cpt_s/recall") == 1), "recall");
                    Load(host);
                    Select(host, ApprovalKindEnum.StalledCaptain);
                    host.Press("t");
                    TuiCase.Contains(host.Screen(), "deleted and recreated with", "restart text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/captains") >= 1 && stub.Count("DELETE /api/v1/captains/cpt_s") == 1), "restart deletes and recreates");
                    Load(host);
                    Select(host, ApprovalKindEnum.StalledCaptain);
                    host.Tui.Context.External.UrlOpener = u => true;
                    host.Press("enter");
                    AssertEqual("/captains/cpt_s", host.Tui.Context.Router.Current!.FullPath, "Enter opens the item");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_proposals", "Ask proposals approve, reject, and show arguments from the center", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_2", "Greeting rollout"));
                AskActionProposal p = AskFixtures.Proposal("aap_7", "ath_2", "dispatch", AskProposalStatusEnum.Pending);
                fx.Decisions(p);
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/approvals", fx.Stub))
                {
                    host.PumpUntil(() => host.Tui.Ask.Threads.Count == 1);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_2", Proposal = p }));
                    host.Pump();
                    TuiCase.Contains(host.Screen(), "Ask proposal", "listed");
                    TuiCase.Contains(host.Screen(), "\"vesselId\": \"vsl_a\"", "arguments in the detail");
                    host.Press("x");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "arguments viewer");
                    host.Press("esc");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_2/proposals/aap_7/approve") == 1), "approve call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Count == 0), "left the queue");
                    AskActionProposal q = AskFixtures.Proposal("aap_8", "ath_2", "cancel_voyage", AskProposalStatusEnum.Pending);
                    fx.Decisions(q);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_2", Proposal = q }));
                    host.Pump();
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => fx.Stub.Count("POST /api/v1/ask/threads/ath_2/proposals/aap_8/reject") == 1), "reject call");
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_2", Proposal = AskFixtures.Proposal("aap_9", "ath_2", "dispatch", AskProposalStatusEnum.Pending) }));
                    host.Pump();
                    host.Press("enter");
                    AssertEqual("/ask/ath_2", host.Tui.Context.Router.Current!.FullPath, "Enter opens the thread");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI approvals center", cases: cases);
        }

        private static TuiTestHost Host(out StubHttpHandler stub)
        {
            stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/inbox", "[" +
                "{\"Kind\":\"review\",\"Severity\":\"Warning\",\"Title\":\"Review: Fix tables\",\"Detail\":\"Waiting 5m\",\"EntityType\":\"mission\",\"EntityId\":\"msn_r\",\"Href\":\"/missions/msn_r\"}," +
                "{\"Kind\":\"landing_failed\",\"Severity\":\"Critical\",\"Title\":\"Landing failed: Ship it\",\"Detail\":\"Conflict\",\"EntityType\":\"mission\",\"EntityId\":\"msn_l\",\"Href\":\"/missions/msn_l\"}," +
                "{\"Kind\":\"stalled_captain\",\"Severity\":\"Warning\",\"Title\":\"Stalled captain: slow\",\"Detail\":\"No heartbeat\",\"EntityType\":\"captain\",\"EntityId\":\"cpt_s\",\"Href\":\"/captains/cpt_s\"}," +
                "{\"Kind\":\"deployment_approval\",\"Severity\":\"Warning\",\"Title\":\"Deployment awaiting approval: Staging\",\"Detail\":\"v1.2\",\"EntityType\":\"deployment\",\"EntityId\":\"dpl_1\",\"Href\":\"/deployments/dpl_1\"}," +
                "{\"Kind\":\"failed\",\"Severity\":\"Warning\",\"Title\":\"Failed: Other\",\"Detail\":\"\",\"EntityType\":\"mission\",\"EntityId\":\"msn_f\",\"Href\":\"/missions/msn_f\"}]");
            stub.Json("POST", "/api/v1/missions/msn_r/review/approve", "{\"Id\":\"msn_r\",\"Title\":\"Fix tables\"}");
            stub.Json("POST", "/api/v1/missions/msn_r/review/deny", "{\"Id\":\"msn_r\",\"Title\":\"Fix tables\"}");
            stub.Json("POST", "/api/v1/deployments/dpl_1/approve", "{\"Id\":\"dpl_1\",\"Title\":\"Staging\"}");
            stub.Json("POST", "/api/v1/deployments/dpl_1/deny", "{\"Id\":\"dpl_1\",\"Title\":\"Staging\"}");
            stub.Json("POST", "/api/v1/missions/msn_l/retry-landing", "{\"Success\":true}");
            stub.Json("POST", "/api/v1/captains/cpt_s/stop", "{}");
            stub.Json("POST", "/api/v1/captains/cpt_s/recall", "{}");
            stub.Json("GET", "/api/v1/captains/cpt_s", "{\"Id\":\"cpt_s\",\"Name\":\"slow\",\"Runtime\":\"ClaudeCode\"}");
            stub.On("DELETE", "/api/v1/captains/cpt_s", body => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.Json("POST", "/api/v1/captains", "{\"Id\":\"cpt_t\",\"Name\":\"slow\",\"Runtime\":\"ClaudeCode\"}");
            return TuiCase.SignedIn(140, 45, "/approvals", stub);
        }

        private static void Load(TuiTestHost host)
        {
            host.Tui.ApprovalSources.SyncInbox();
            host.Tui.Context.Status.PollAllAsync().GetAwaiter().GetResult();
            host.PumpUntil(() => host.Tui.Context.Approvals.Count >= 4);
            ForceResync(host);
        }

        private static void ForceResync(TuiTestHost host)
        {
            // Re-add items a decision removed: the inbox stub is unchanged, so replay it through the mapping.
            foreach (InboxItem item in host.Tui.Context.Status.Inbox)
            {
                ApprovalItem? mapped = ApprovalSources.FromInbox(item);
                if (mapped != null && host.Tui.Context.Approvals.Find(mapped.Kind, mapped.EntityId) == null) host.Tui.Context.Approvals.Upsert(mapped, false);
            }

            host.Pump();
        }

        private static ApprovalsScreen Select(TuiTestHost host, ApprovalKindEnum kind)
        {
            ApprovalsScreen screen = (ApprovalsScreen)host.Tui.Shell.Screen!;
            host.Pump();
            for (int i = 0; i < 10; i++)
            {
                if (screen.Current()?.Kind == kind) return screen;
                host.Press("down");
            }

            host.Press("home");
            for (int i = 0; i < 10; i++)
            {
                if (screen.Current()?.Kind == kind) return screen;
                host.Press("down");
            }

            throw new AssertionException("no " + kind + " item");
        }
    }
}
