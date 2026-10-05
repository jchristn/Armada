namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Threading;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
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

            cases.Add(TuiCase.Sync(Suite, "deployment_name_agrees_across_sources", "A deployment approval reads the same from a live event and from the inbox, so its confirmation names it the same way", () =>
            {
                // Regression: the live path named the item by the deployment title and the inbox by the environment,
                // so whichever source reported last decided the row and the confirmation text (an end-to-end test that
                // pressed a after an inbox poll replaced the live item saw a different name than it expected).
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("deployment.changed", "{\"id\":\"dpl_1\",\"title\":\"Deploy web v1.2\",\"environmentName\":\"Staging\",\"status\":\"PendingApproval\"}"));
                    host.Pump();
                    ApprovalItem live = host.Tui.Context.Approvals.Find(ApprovalKindEnum.DeploymentApproval, "dpl_1")
                        ?? throw new AssertionException("deployment from the event");
                    AssertEqual("Live", live.Source, "item came from the event");

                    InboxItem inbox = new InboxItem { Kind = InboxItemKinds.DeploymentApproval, Title = "Deploy to Staging: Deploy web v1.2", EntityName = "Staging", EnvironmentName = "Staging", DeploymentTitle = "Deploy web v1.2", EntityType = "deployment", EntityId = "dpl_1", Href = "/deployments/dpl_1" };
                    ApprovalItem polled = ApprovalSources.FromInbox(inbox)!;
                    AssertEqual(polled.EntityName, live.EntityName, "entity name");
                    AssertEqual(polled.Title, live.Title, "row title");
                    AssertEqual(polled.Route, live.Route, "route");

                    Select(host, ApprovalKindEnum.DeploymentApproval);
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "confirm");
                    TuiCase.Contains(host.Screen(), "Approve and execute \"Deploy to Staging: Deploy web v1.2\"?", "the confirmation names the environment, then the title");
                    host.Press("n");

                    host.Tui.Context.Events.Inject(AskFixtures.EventJson("deployment.changed", "{\"id\":\"dpl_3\",\"title\":\"No environment\",\"status\":\"PendingApproval\"}"));
                    host.Pump();
                    AssertEqual("Deploy: No environment", host.Tui.Context.Approvals.Find(ApprovalKindEnum.DeploymentApproval, "dpl_3")?.EntityName, "no environment leads with the title, like the inbox");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "deployment_label_environment_first", "Deployment approvals read \"Deploy to {environment}: {title}\" in the queue, the Inbox screen, and notifications, with graceful fallbacks", () =>
            {
                // Owner decision: every surface labels a deployment approval with the environment first and the
                // deployment title second. Before, the TUI named it by the environment alone.
                InboxItem typed = new InboxItem { Kind = InboxItemKinds.DeploymentApproval, Title = "server text", EntityName = "Staging", EnvironmentName = "Staging", DeploymentTitle = "Release 2.3 hotfix", EntityType = "deployment", EntityId = "dpl_1", Href = "/deployments/dpl_1" };
                ApprovalItem mapped = ApprovalSources.FromInbox(typed)!;
                AssertEqual("Deploy to Staging: Release 2.3 hotfix", mapped.Title, "row title");
                AssertEqual("Deploy to Staging: Release 2.3 hotfix", mapped.EntityName, "confirmation name");
                AssertEqual("Deploy to Staging: Release 2.3 hotfix", Armada.Tui.Screens.Operations.InboxScreen.TitleFor(null, typed), "Inbox screen title");

                InboxItem noEnvironment = new InboxItem { Kind = InboxItemKinds.DeploymentApproval, EntityName = "dpl_2", DeploymentTitle = "Ad hoc", EntityId = "dpl_2" };
                AssertEqual("Deploy: Ad hoc", ApprovalSources.FromInbox(noEnvironment)!.Title, "no environment leads with the title");
                InboxItem legacy = new InboxItem { Kind = InboxItemKinds.DeploymentApproval, Title = "Deployment awaiting approval: Staging", EntityName = "Staging", EntityId = "dpl_3" };
                AssertEqual("Deploy to Staging", ApprovalSources.FromInbox(legacy)!.Title, "an older server's EntityName is the environment");
                InboxItem legacyId = new InboxItem { Kind = InboxItemKinds.DeploymentApproval, Title = "Deployment awaiting approval: dpl_4", EntityName = "dpl_4", EntityId = "dpl_4" };
                AssertEqual("Deploy: dpl_4", ApprovalSources.FromInbox(legacyId)!.Title, "never \"Deploy to <id>\"");

                InboxItem review = new InboxItem { Kind = InboxItemKinds.Review, Title = "Review: Fix tables", EntityName = "Fix tables", EntityId = "msn_1" };
                AssertEqual("Review: Fix tables", Armada.Tui.Screens.Operations.InboxScreen.TitleFor(null, review), "other kinds keep the server title");

                NotificationService notifications = new NotificationService(new SystemClock(), new LocalizationService(), null, null, null);
                notifications.HandleSocketMessage(ArmadaSocketMessage.Parse("{\"type\":\"deployment.changed\",\"data\":{\"id\":\"dpl_5\",\"title\":\"Release 2.3 hotfix\",\"environmentName\":\"production\",\"status\":\"PendingApproval\"}}")!);
                AssertEqual("Deploy to production: Release 2.3 hotfix", notifications.History[0].Name, "pending approval notification");
                notifications.HandleSocketMessage(ArmadaSocketMessage.Parse("{\"type\":\"deployment.changed\",\"data\":{\"id\":\"dpl_5\",\"title\":\"Release 2.3 hotfix\",\"environmentName\":\"production\",\"status\":\"Running\"}}")!);
                AssertEqual("Release 2.3 hotfix", notifications.History[0].Name, "other statuses keep the deployment title");
            }));

            cases.Add(TuiCase.Sync(Suite, "deployment_label_localized", "The deployment approval label comes from the shared catalog in every locale, environment before title", () =>
            {
                string path = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "src", "Armada.Server", "wwwroot", "i18n", "armada.json");
                I18nCatalog catalog = ArmadaJson.Deserialize<I18nCatalog>(File.ReadAllText(path)) ?? throw new AssertionException("catalog");
                AssertTrue(catalog.Locales.Count >= 8, "every maintained locale is present");
                LocalizationService loc = new LocalizationService();
                loc.SetCatalog(catalog);
                foreach (string locale in catalog.Locales.Keys)
                {
                    loc.SetLocale(locale);
                    string label = DeploymentApprovalText.Label(loc, "production", "Release 2.3 hotfix", "dpl_1");
                    AssertTrue(label != "Deploy to production: Release 2.3 hotfix", locale + " is translated: " + label);
                    int environment = label.IndexOf("production", StringComparison.Ordinal);
                    int title = label.IndexOf("Release 2.3 hotfix", StringComparison.Ordinal);
                    AssertTrue(environment >= 0 && title > environment, locale + " names the environment first: " + label);
                    AssertTrue(DeploymentApprovalText.Label(loc, "production", null, "dpl_1").Contains("production"), locale + " environment only");
                    AssertTrue(DeploymentApprovalText.Label(loc, null, "Release 2.3 hotfix", "dpl_1").Contains("Release 2.3 hotfix"), locale + " title only");
                }

                loc.SetLocale("ja");
                InboxItem typed = new InboxItem { Kind = InboxItemKinds.DeploymentApproval, EnvironmentName = "Staging", DeploymentTitle = "Release 2.3 hotfix", EntityId = "dpl_1" };
                AssertEqual("Staging \u3078\u306e\u30c7\u30d7\u30ed\u30a4: Release 2.3 hotfix", ApprovalSources.FromInbox(typed, loc)!.Title, "Japanese queue row");
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
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/missions/msn_r/review/approve") == 1), "approve endpoint");
                    MissionReviewApproveRequest approve = stub.LastBody<MissionReviewApproveRequest>("POST", "/api/v1/missions/msn_r/review/approve");
                    AssertEqual("Rename the helper", approve.Comment, "feedback sent");
                    AssertEqual(true, approve.Conditional, "conditional flag");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "Conditionally approved"), "toast");
                    AssertNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.MissionReview, "msn_r"), "left the queue");

                    Load(host);
                    Select(host, ApprovalKindEnum.MissionReview);
                    host.Press("m").Type("Add tests").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.BodiesFor<MissionReviewDenyRequest>("POST", "/api/v1/missions/msn_r/review/deny").Any(d => d.Action == "RetryStage" && d.Comment == "Add tests")), "more work: deny with RetryStage");
                    Load(host);
                    Select(host, ApprovalKindEnum.MissionReview);
                    host.Press("d").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.BodiesFor<MissionReviewDenyRequest>("POST", "/api/v1/missions/msn_r/review/deny").Any(d => d.Action == "FailPipeline")), "deny with FailPipeline");
                    Load(host);
                    Select(host, ApprovalKindEnum.MissionReview);
                    int approvals = stub.CountFor("POST", "/api/v1/missions/msn_r/review/approve");
                    host.Press("a").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/missions/msn_r/review/approve") == approvals + 1), "plain approve");
                    AssertTrue(host.PumpUntil(() => TuiToasts.Has(host, NotificationSeverityEnum.Success, "Review approved for")), "approve toast");
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
                    TuiCase.Contains(host.Screen(), "Approve and execute \"Deploy to Staging: Release 2.3 hotfix\"?", "dashboard text: environment first, then title");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/deployments/dpl_1/approve") == 1), "approve call");
                    AssertTrue(host.PumpUntil(() => TuiToasts.Has(host, NotificationSeverityEnum.Success, "Deployment \"Release 2.3 hotfix\" updated.")), "toast");
                    Load(host);
                    Select(host, ApprovalKindEnum.DeploymentApproval);
                    host.Press("d");
                    TuiCase.Contains(host.Screen(), "Deny \"Deploy to Staging: Release 2.3 hotfix\" without executing it?", "deny text");
                    host.Press("n");
                    AssertEqual(0, stub.CountFor("POST", "/api/v1/deployments/dpl_1/deny"), "cancel does nothing");
                    host.Press("d").Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/deployments/dpl_1/deny") == 1), "deny call");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "landing_and_captains", "Retry landing; stop, recall, and restart a stalled captain", () =>
            {
                using (TuiTestHost host = Host(out StubHttpHandler stub))
                {
                    Load(host);
                    Select(host, ApprovalKindEnum.FailedLanding);
                    host.Press("l");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/missions/msn_l/retry-landing") == 1), "retry landing");
                    AssertTrue(host.PumpUntil(() => TuiToasts.Has(host, NotificationSeverityEnum.Success, "Landing succeeded for \"Ship it\"")), "toast");
                    Select(host, ApprovalKindEnum.StalledCaptain);
                    host.Press("s");
                    TuiCase.Contains(host.Screen(), "The captain process will be terminated.", "stop text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains/cpt_s/stop") == 1), "stop");
                    Load(host);
                    Select(host, ApprovalKindEnum.StalledCaptain);
                    host.Press("R").Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains/cpt_s/stop") == 2), "recall uses the server's stop (recall) route");
                    Load(host);
                    Select(host, ApprovalKindEnum.StalledCaptain);
                    host.Press("t");
                    TuiCase.Contains(host.Screen(), "deleted and recreated with", "restart text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/captains") >= 1 && stub.CountFor("DELETE", "/api/v1/captains/cpt_s") == 1), "restart deletes and recreates");
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
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_2/proposals/aap_7/approve") == 1), "approve call");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Count == 0), "left the queue");
                    AskActionProposal q = AskFixtures.Proposal("aap_8", "ath_2", "cancel_voyage", AskProposalStatusEnum.Pending);
                    fx.Decisions(q);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_2", Proposal = q }));
                    host.Pump();
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_2/proposals/aap_8/reject") == 1), "reject call");
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_2", Proposal = AskFixtures.Proposal("aap_9", "ath_2", "dispatch", AskProposalStatusEnum.Pending) }));
                    host.Pump();
                    host.Press("enter");
                    AssertEqual("/ask/ath_2", host.Tui.Context.Router.Current!.FullPath, "Enter opens the thread");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "keyboard_flow_back", "Keyboard flow: select a proposal, view its arguments, open its thread, and Alt+Left back to the center with the item kept", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_3", "Docs rollout"));
                AskActionProposal p = AskFixtures.Proposal("aap_k", "ath_3", "dispatch", AskProposalStatusEnum.Pending);
                fx.Decisions(p);
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/approvals", fx.Stub))
                {
                    host.PumpUntil(() => host.Tui.Ask.Threads.Count == 1);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_3", Proposal = p }));
                    host.Pump();
                    ApprovalsScreen screen = Select(host, ApprovalKindEnum.AskProposal);
                    AssertEqual("aap_k", screen.Current()?.EntityId, "selected");
                    host.Press("x");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "arguments modal");
                    host.Press("esc");
                    AssertTrue(host.PumpUntil(() => !host.App.Modals.IsActive), "modal closed");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == "/ask/ath_3"), "Enter opens the thread");
                    host.Press("esc");
                    host.Press("alt+left");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/approvals"), "Alt+Left back to the center: " + host.Tui.Context.Router.Current!.FullPath);
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Find(ApprovalKindEnum.AskProposal, "aap_k") != null), "the item is still queued");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "decisions_do_not_block_each_other", "Rejecting one proposal while another proposal's approve call is still in flight sends the reject (no keypress is dropped)", () =>
            {
                AskFixtures fx = new AskFixtures();
                fx.AddThread(AskFixtures.Thread("ath_4", "Two changes"));
                AskActionProposal first = AskFixtures.Proposal("aap_h", "ath_4", "dispatch", AskProposalStatusEnum.Pending);
                AskActionProposal second = AskFixtures.Proposal("aap_j", "ath_4", "dispatch", AskProposalStatusEnum.Pending);
                fx.Decisions(first).Decisions(second);
                using (ManualResetEventSlim releaseApprove = new ManualResetEventSlim(false))
                {
                    fx.Stub.On("POST", "/api/v1/ask/threads/ath_4/proposals/aap_h/approve", body =>
                    {
                        releaseApprove.Wait(TimeSpan.FromSeconds(30));
                        AskActionProposal executed = AskFixtures.Proposal("aap_h", "ath_4", "dispatch", AskProposalStatusEnum.Executed);
                        return StubHttpHandler.Response(HttpStatusCode.OK, ArmadaJson.Serialize(executed));
                    });

                    using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/approvals", fx.Stub))
                    {
                        host.PumpUntil(() => host.Tui.Ask.Threads.Count == 1);
                        host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_4", Proposal = first }));
                        host.Tui.Context.Events.Inject(AskFixtures.Event("ask.proposal", new AskProposalEvent { ThreadId = "ath_4", Proposal = second }));
                        host.Pump();
                        ApprovalsScreen screen = SelectProposal(host, "aap_h");
                        host.Press("a");
                        AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_4/proposals/aap_h/approve") == 1), "approve call started and is held");
                        AssertTrue(host.Tui.Ask.IsProposalBusy("aap_h"), "the approved proposal is busy");

                        SelectProposal(host, "aap_j");
                        host.Press("r");
                        AssertTrue(host.PumpUntil(() => fx.Stub.CountFor("POST", "/api/v1/ask/threads/ath_4/proposals/aap_j/reject") == 1, 10000), "reject call sent while the approve is in flight");
                        AssertTrue(host.Tui.Ask.IsProposalBusy("aap_h"), "the approve is still in flight");

                        releaseApprove.Set();
                        AssertTrue(host.PumpUntil(() => !host.Tui.Ask.IsProposalBusy("aap_h") && !host.Tui.Ask.IsProposalBusy("aap_j")), "both calls finish");
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Count == 0), "both proposals left the queue");
                        AssertEqual(screen, (ApprovalsScreen)host.Tui.Shell.Screen!, "still on the center");
                    }
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI approvals center", cases: cases);
        }

        private static TuiTestHost Host(out StubHttpHandler stub)
        {
            stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/inbox", "[" +
                "{\"Kind\":\"review\",\"Severity\":\"Warning\",\"Title\":\"Review: Fix tables\",\"EntityName\":\"Fix tables\",\"Detail\":\"Waiting 5m\",\"EntityType\":\"mission\",\"EntityId\":\"msn_r\",\"Href\":\"/missions/msn_r\"}," +
                "{\"Kind\":\"landing_failed\",\"Severity\":\"Critical\",\"Title\":\"Landing failed: Ship it\",\"EntityName\":\"Ship it\",\"Detail\":\"Conflict\",\"EntityType\":\"mission\",\"EntityId\":\"msn_l\",\"Href\":\"/missions/msn_l\"}," +
                "{\"Kind\":\"stalled_captain\",\"Severity\":\"Warning\",\"Title\":\"Stalled captain: slow\",\"EntityName\":\"slow\",\"Detail\":\"No heartbeat\",\"EntityType\":\"captain\",\"EntityId\":\"cpt_s\",\"Href\":\"/captains/cpt_s\"}," +
                "{\"Kind\":\"deployment_approval\",\"Severity\":\"Warning\",\"Title\":\"Deploy to Staging: Release 2.3 hotfix\",\"EntityName\":\"Staging\",\"EnvironmentName\":\"Staging\",\"DeploymentTitle\":\"Release 2.3 hotfix\",\"Detail\":\"v1.2\",\"EntityType\":\"deployment\",\"EntityId\":\"dpl_1\",\"Href\":\"/deployments/dpl_1\"}," +
                "{\"Kind\":\"failed\",\"Severity\":\"Warning\",\"Title\":\"Failed: Other\",\"EntityName\":\"Other\",\"Detail\":\"\",\"EntityType\":\"mission\",\"EntityId\":\"msn_f\",\"Href\":\"/missions/msn_f\"}]");
            stub.Json("POST", "/api/v1/missions/msn_r/review/approve", "{\"Id\":\"msn_r\",\"Title\":\"Fix tables\"}");
            stub.Json("POST", "/api/v1/missions/msn_r/review/deny", "{\"Id\":\"msn_r\",\"Title\":\"Fix tables\"}");
            stub.Json("POST", "/api/v1/deployments/dpl_1/approve", "{\"Id\":\"dpl_1\",\"Title\":\"Release 2.3 hotfix\",\"EnvironmentName\":\"Staging\"}");
            stub.Json("POST", "/api/v1/deployments/dpl_1/deny", "{\"Id\":\"dpl_1\",\"Title\":\"Release 2.3 hotfix\",\"EnvironmentName\":\"Staging\"}");
            stub.Json("POST", "/api/v1/missions/msn_l/retry-landing", "{\"Success\":true}");
            stub.Json("POST", "/api/v1/captains/cpt_s/stop", "{}");
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

        private static ApprovalsScreen SelectProposal(TuiTestHost host, string proposalId)
        {
            ApprovalsScreen screen = (ApprovalsScreen)host.Tui.Shell.Screen!;
            host.Pump();
            host.Press("home");
            for (int i = 0; i < 20; i++)
            {
                ApprovalItem? current = screen.Current();
                if (current != null && current.Kind == ApprovalKindEnum.AskProposal && current.EntityId == proposalId) return screen;
                host.Press("down");
            }

            throw new AssertionException("could not select proposal " + proposalId + "\n" + host.Screen());
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
