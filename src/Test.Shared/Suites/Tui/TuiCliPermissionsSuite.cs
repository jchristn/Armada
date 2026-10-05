namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Test.Shared.Infrastructure.ApiSurface;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// CLI tool permission prompts in the TUI against a stubbed client: the Approvals center row (tool, command,
    /// captain, vessel, mission, expiry countdown), the a / A / d decisions with the exact decide body, requests the user
    /// cannot decide or remember, live requested and resolved events, a request decided elsewhere (409), the Ask
    /// transcript's permission card, the explanation under a tool chip the CLI refused, and the catalog strings.
    /// </summary>
    public sealed class TuiCliPermissionsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.CliPermissions";
        private const string DecidePath = "/api/v1/cli-permissions/requests/cpr_1/decide";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "row_renders", "A pending CLI permission request lists tool, command, captain, vessel, mission, countdown, and its keys", () =>
            {
                using (TuiTestHost host = Host(Request(true, true), out StubHttpHandler stub))
                {
                    ApprovalsScreen screen = Load(host);
                    ApprovalItem item = screen.Current() ?? throw new AssertionException("selected item");
                    AssertEqual(ApprovalKindEnum.CliPermission, item.Kind, "kind");
                    AssertEqual("cpr_1", item.EntityId, "entity id");
                    AssertEqual("/missions/msn_1", item.Route, "Enter opens the mission");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "CLI permission", "kind label");
                    TuiCase.Contains(frame, "Bash: git push origin main", "tool and command");
                    TuiCase.Contains(frame, "Captain claude-1  Vessel web  Mission Fix tables", "where the captain runs");
                    TuiCase.Contains(frame, "Expires in ", "countdown");
                    TuiCase.Contains(frame, "[Allow once] a  [Allow and remember] A  [Deny] d", "decision buttons");
                    TuiCase.Contains(frame, "Suggested rule: Bash(git push:*)", "suggested rule in the detail");
                    List<string> hints = screen.Hints.Select(h => h.Key).ToList();
                    AssertTrue(hints.Contains("a") && hints.Contains("A") && hints.Contains("d"), "status bar hints");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "allow_once", "a allows once with no dialog and the item leaves the queue", () =>
            {
                using (TuiTestHost host = Host(Request(true, true), out StubHttpHandler stub))
                {
                    Load(host);
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", DecidePath) == 1), "decide call");
                    CliPermissionDecisionRequest body = stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath);
                    AssertEqual(CliPermissionDecisionEnum.AllowOnce, body.Decision, "decision");
                    AssertNull(body.RulePattern, "no rule");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "Allowed Bash once."), "toast");
                    AssertNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.CliPermission, "cpr_1"), "left the queue");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "allow_and_remember", "A opens the rule dialog prefilled with the suggested rule; the edited pattern and the chosen scope are sent", () =>
            {
                using (TuiTestHost host = Host(Request(true, true), out StubHttpHandler stub))
                {
                    ApprovalsScreen screen = Load(host);
                    host.Press("A");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "rule dialog");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Allow and Remember", "dialog title");
                    TuiCase.Contains(frame, "Bash(git push:*)", "prefilled with the suggested rule");
                    TuiCase.Contains(frame, "(*) This captain", "captain scope is the default");
                    TuiCase.Contains(frame, "( ) This vessel", "vessel scope offered for a request with a vessel");
                    TuiCase.Contains(frame, "( ) Everywhere (global)", "global scope offered");
                    host.Press("ctrl+u").Type("Bash(git push origin:*)");
                    host.Press("down");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", DecidePath) == 1), "decide call");
                    CliPermissionDecisionRequest body = stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath);
                    AssertEqual(CliPermissionDecisionEnum.AllowAndRemember, body.Decision, "decision");
                    AssertEqual("Bash(git push origin:*)", body.RulePattern, "edited pattern");
                    AssertEqual(CliPermissionRuleScopeEnum.Vessel, body.RuleScope, "scope");
                    AssertTrue(TuiToasts.WaitForSuccess(host, "Allowed Bash and saved the rule Bash(git push origin:*)."), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "remember_requires_pattern", "Allow and remember refuses an empty pattern; Esc sends nothing", () =>
            {
                using (TuiTestHost host = Host(Request(true, true), out StubHttpHandler stub))
                {
                    ApprovalsScreen screen = Load(host);
                    CliPermissionDecisionModal modal = screen.Actions.RememberCliPermission(screen.Current()!) ?? throw new AssertionException("dialog");
                    host.Pump();
                    host.Press("ctrl+u").Press("enter");
                    AssertEqual("Enter a rule pattern.", modal.Error, "validation");
                    AssertTrue(host.App.Modals.IsActive, "still open");
                    host.Press("esc");
                    AssertTrue(host.PumpUntil(() => !host.App.Modals.IsActive), "closed");
                    host.Pump();
                    AssertEqual(0, stub.CountFor("POST", DecidePath), "nothing sent");
                    AssertNotNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.CliPermission, "cpr_1"), "still queued");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "scopes_follow_request", "The vessel scope is offered only when the request has a vessel", () =>
            {
                CliPermissionRequest thread = Request(true, true);
                thread.VesselId = null;
                thread.VesselName = null;
                thread.MissionId = null;
                thread.MissionTitle = null;
                thread.ThreadId = "ath_1";
                thread.ThreadTitle = "Docs cleanup";
                CliPermissionDecisionModal modal = new CliPermissionDecisionModal(thread, true, null, null);
                AssertEqual("Captain,Global", String.Join(",", modal.Scopes), "no vessel scope");
                AssertFalse(modal.SelectScope(CliPermissionRuleScopeEnum.Vessel), "vessel cannot be chosen");
                AssertEqual("Captain claude-1  Conversation Docs cleanup", CliPermissionText.Where(null, thread), "conversation instead of mission");
                CliPermissionDecisionModal full = new CliPermissionDecisionModal(Request(true, true), true, null, null);
                AssertEqual("Captain,Vessel,Global", String.Join(",", full.Scopes), "all scopes");
                AssertEqual("Bash(git push:*)", full.Input.Value, "prefill");
                CliPermissionRequest noSuggestion = Request(true, true);
                noSuggestion.SuggestedRule = null;
                AssertEqual("Bash", new CliPermissionDecisionModal(noSuggestion, true, null, null).Input.Value, "falls back to the tool name");
            }));

            cases.Add(TuiCase.Sync(Suite, "deny_with_message", "d opens the deny dialog and sends the optional message", () =>
            {
                using (TuiTestHost host = Host(Request(true, true), out StubHttpHandler stub))
                {
                    Load(host);
                    host.Press("d");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "deny dialog");
                    TuiCase.Contains(host.Screen(), "Deny Permission", "dialog title");
                    host.Type("Do not push to main").Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", DecidePath) == 1), "decide call");
                    CliPermissionDecisionRequest body = stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath);
                    AssertEqual(CliPermissionDecisionEnum.Deny, body.Decision, "decision");
                    AssertEqual("Do not push to main", body.Message, "message");
                    AssertTrue(host.PumpUntil(() => TuiToasts.Has(host, NotificationSeverityEnum.Warning, "Denied Bash.")), "toast");
                    AssertNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.CliPermission, "cpr_1"), "left the queue");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "deny_without_message", "Deny with an empty message sends no message", () =>
            {
                using (TuiTestHost host = Host(Request(true, true), out StubHttpHandler stub))
                {
                    Load(host);
                    host.Press("d");
                    AssertTrue(host.PumpUntil(() => host.App.Modals.IsActive), "deny dialog");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", DecidePath) == 1), "decide call");
                    CliPermissionDecisionRequest body = stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath);
                    AssertEqual(CliPermissionDecisionEnum.Deny, body.Decision, "decision");
                    AssertNull(body.Message, "no message");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "cannot_decide", "A request the user cannot decide says an admin must and sends nothing on a, A, or d", () =>
            {
                using (TuiTestHost host = Host(Request(false, false), out StubHttpHandler stub))
                {
                    ApprovalsScreen screen = Load(host);
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "An admin must decide this request.", "admin notice");
                    TuiCase.NotContains(frame, "Allow once", "no decision keys");
                    AssertFalse(screen.Hints.Any(h => h.Key == "a" || h.Key == "A" || h.Key == "d"), "no decision hints");
                    host.Press("a");
                    host.Press("A");
                    host.Press("d");
                    host.Pump();
                    AssertFalse(host.App.Modals.IsActive, "no dialog");
                    AssertEqual(0, stub.CountFor("POST", DecidePath), "nothing sent");
                    AssertTrue(TuiToasts.Has(host, NotificationSeverityEnum.Info, "An admin must decide this request."), "notice toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "cannot_remember", "A user who may decide but not remember gets a and d only", () =>
            {
                using (TuiTestHost host = Host(Request(true, false), out StubHttpHandler stub))
                {
                    ApprovalsScreen screen = Load(host);
                    TuiCase.Contains(host.Screen(), "[Allow once] a  [Deny] d", "decision buttons without remember");
                    AssertFalse(screen.Buttons().Any(b => b.DecisionKey == 'A'), "no Allow and remember button");
                    host.Press("A");
                    host.Pump();
                    AssertFalse(host.App.Modals.IsActive, "no rule dialog");
                    AssertTrue(TuiToasts.Has(host, NotificationSeverityEnum.Warning, "Only an admin can save a permission rule."), "notice");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", DecidePath) == 1), "allow once still works");
                    AssertEqual(CliPermissionDecisionEnum.AllowOnce, stub.LastBody<CliPermissionDecisionRequest>("POST", DecidePath).Decision, "decision");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "already_decided", "A request decided elsewhere (409) leaves the queue with a notice instead of an error dialog", () =>
            {
                using (TuiTestHost host = Host(Request(true, true), out StubHttpHandler stub))
                {
                    stub.On("POST", DecidePath, body => StubHttpHandler.Response(HttpStatusCode.Conflict, "{\"Error\":\"Conflict\",\"Message\":\"Request is not pending.\"}"));
                    Load(host);
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => TuiToasts.Has(host, NotificationSeverityEnum.Warning, "already decided or expired")), "notice");
                    AssertFalse(host.App.Modals.IsActive, "no error dialog");
                    AssertNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.CliPermission, "cpr_1"), "left the queue");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "live_events", "cli_permission.requested adds the item at once; cli_permission.resolved removes it", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.Json("GET", "/api/v1/inbox", "[]");
                using (TuiTestHost host = TuiCase.SignedIn(140, 45, "/approvals", stub))
                {
                    int arrived = 0;
                    host.Tui.Context.Approvals.Arrived += (s, e) => arrived++;
                    CliPermissionRequest request = Request(true, true);
                    host.Tui.Context.Events.Inject(AskFixtures.Event("cli_permission.requested", new CliPermissionEvent { RequestId = "cpr_1", Status = CliPermissionRequestStatusEnum.Pending, Request = request }));
                    host.Pump();
                    ApprovalItem item = host.Tui.Context.Approvals.Find(ApprovalKindEnum.CliPermission, "cpr_1") ?? throw new AssertionException("added from the event");
                    AssertEqual("Bash: git push origin main", item.Title, "title");
                    AssertEqual(1, arrived, "attention for a new prompt");
                    request.Status = CliPermissionRequestStatusEnum.Allowed;
                    host.Tui.Context.Events.Inject(AskFixtures.Event("cli_permission.resolved", new CliPermissionEvent { RequestId = "cpr_1", Status = CliPermissionRequestStatusEnum.Allowed, Request = request }));
                    host.Pump();
                    AssertNull(host.Tui.Context.Approvals.Find(ApprovalKindEnum.CliPermission, "cpr_1"), "removed when resolved");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "inbox_mapping", "Inbox items of kind cli_permission map typed fields; a conversation opens only for its owner", () =>
            {
                CliPermissionRequest request = Request(true, true);
                request.MissionId = null;
                request.MissionTitle = null;
                request.ThreadId = "ath_9";
                request.UserId = "usr_admin";
                InboxItem own = new InboxItem { Kind = InboxItemKinds.CliPermission, EntityId = "cpr_1", Href = "/ask/ath_9", CliPermission = request, ExpiresUtc = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
                ApprovalItem mapped = ApprovalSources.FromInbox(own) ?? throw new AssertionException("mapped");
                AssertEqual(ApprovalKindEnum.CliPermission, mapped.Kind, "kind");
                AssertEqual("/ask/ath_9", mapped.Route, "own conversation");
                AssertEqual(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), mapped.ExpiresUtc, "expiry");
                InboxItem other = new InboxItem { Kind = InboxItemKinds.CliPermission, EntityId = "cpr_1", Href = "/cli-permissions?request=cpr_1", CliPermission = request };
                AssertEqual("/captains/cpt_1", ApprovalSources.FromInbox(other)!.Route, "another user's conversation opens the captain");
                AssertNull(ApprovalSources.FromInbox(new InboxItem { Kind = InboxItemKinds.CliPermission, EntityId = "cpr_2" }), "no request, no item");
                request.Status = CliPermissionRequestStatusEnum.Denied;
                AssertNull(ApprovalSources.FromInbox(own), "decided requests are not queued");
            }));

            cases.Add(TuiCase.Sync(Suite, "transcript_card", "The Ask transcript renders CLI permission cards and explains a tool call the CLI refused", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskThread thread = AskFixtures.Thread("ath_1", "Release prep");
                thread.CliPermission = new CliPermissionResolution
                {
                    Requested = CliPermissionPolicyEnum.ApproveInArmada,
                    Effective = CliPermissionPolicyEnum.Refuse,
                    Source = CliPermissionPolicySourceEnum.Captain,
                    FallbackReason = CliPermissionFallbackReasonEnum.RuntimeUnsupported
                };
                AskMessage pending = AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.System, AskMessageKindEnum.CliPermission, "Bash: git push origin main");
                pending.CliPermissionRequest = Request(true, true);
                pending.CliPermissionRequest.MissionId = null;
                pending.CliPermissionRequest.MissionTitle = null;
                pending.CliPermissionRequest.VesselId = null;
                pending.CliPermissionRequest.VesselName = null;
                pending.CliPermissionRequest.ThreadId = "ath_1";
                AskMessage waiting = AskFixtures.Message("amg_2", "ath_1", 2, AskMessageRoleEnum.System, AskMessageKindEnum.CliPermission, "WebFetch: https://example.com");
                waiting.CliPermissionRequest = Request(false, false);
                waiting.CliPermissionRequest.Id = "cpr_2";
                waiting.CliPermissionRequest.ToolName = "WebFetch";
                waiting.CliPermissionRequest.SummaryText = "https://example.com";
                AskMessage allowed = AskFixtures.Message("amg_3", "ath_1", 3, AskMessageRoleEnum.System, AskMessageKindEnum.CliPermission, "Bash: npm test");
                allowed.CliPermissionRequest = Request(false, false);
                allowed.CliPermissionRequest.Id = "cpr_3";
                allowed.CliPermissionRequest.SummaryText = "npm test";
                allowed.CliPermissionRequest.Status = CliPermissionRequestStatusEnum.Allowed;
                allowed.CliPermissionRequest.DecisionSource = CliPermissionDecisionSourceEnum.AllowRule;
                AskMessage denied = AskFixtures.Message("amg_4", "ath_1", 4, AskMessageRoleEnum.System, AskMessageKindEnum.CliPermission, "Bash: rm -rf build");
                denied.CliPermissionRequest = Request(false, false);
                denied.CliPermissionRequest.Id = "cpr_4";
                denied.CliPermissionRequest.SummaryText = "rm -rf build";
                denied.CliPermissionRequest.Status = CliPermissionRequestStatusEnum.Denied;
                denied.CliPermissionRequest.DecisionSource = CliPermissionDecisionSourceEnum.Approver;
                denied.CliPermissionRequest.DecisionMessage = "Not in this repo";
                AskMessage reply = AskFixtures.Message("amg_5", "ath_1", 5, AskMessageRoleEnum.Assistant, AskMessageKindEnum.Text, "I could not run the tests.");
                AskMessageToolCall refused = new AskMessageToolCall();
                refused.ToolName = "Bash";
                refused.Ok = false;
                refused.ResultText = "permission denied";
                refused.PermissionDenied = true;
                reply.ToolCalls.Add(refused);
                AskMessageToolCall fine = new AskMessageToolCall();
                fine.ToolName = "Read";
                fine.Ok = false;
                fine.ResultText = "no such file";
                reply.ToolCalls.Add(fine);
                fx.AddThread(thread, pending, waiting, allowed, denied, reply);
                using (TuiTestHost host = TuiCase.SignedIn(160, 90, "/ask/ath_1", fx.Stub))
                {
                    AskScreen screen = (AskScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count == 5 && host.Tui.Ask.Conversation.Thread?.CliPermission != null), "loaded");
                    screen.Transcript.Layout(150);
                    string text = String.Join("\n", screen.Transcript.PlainLines());
                    TuiCase.Contains(text, "Permission needed  Bash", "pending card heading");
                    TuiCase.Contains(text, "! Pending", "pending status");
                    TuiCase.Contains(text, "git push origin main", "command");
                    TuiCase.Contains(text, "Captain claude-1", "captain");
                    TuiCase.Contains(text, "Expires in ", "countdown");
                    TuiCase.Contains(text, "Decide here or in the Approvals center (Ctrl+A).", "how to decide");
                    TuiCase.Contains(text, "[Allow once] (a)   [Allow and remember] (A)   [Deny] (d)", "decision buttons on the card");
                    TuiCase.Contains(text, "WebFetch", "second card tool");
                    TuiCase.Contains(text, "Waiting for an admin to decide.", "non-approver wording");
                    TuiCase.Contains(text, "+ Allowed", "allowed status");
                    TuiCase.Contains(text, "Allowed by a saved rule.", "allowed by rule");
                    TuiCase.Contains(text, "x Denied", "denied status");
                    TuiCase.Contains(text, "Denied. The tool did not run.", "denied by approver");
                    TuiCase.Contains(text, "Not in this repo", "denial message");
                    TuiCase.Contains(text, "[x!] Bash", "refused chip");
                    TuiCase.Contains(text, "Refused: CLI tools run with policy Refuse (from the captain).", "refusal explanation with source");
                    string flat = Regex.Replace(text, "\\s+", " ");
                    TuiCase.Contains(flat, "because this runtime cannot ask Armada for permission.", "fallback reason");
                    AssertEqual(1, CountOf(text, "Refused: CLI tools run"), "only the permission-denied chip is explained");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "live_tool_permission_denied", "A live ask.tool completion with permissionDenied explains the refusal while streaming", () =>
            {
                AskFixtures fx = new AskFixtures();
                AskThread thread = AskFixtures.Thread("ath_1", "Release prep");
                thread.CliPermission = new CliPermissionResolution { Requested = CliPermissionPolicyEnum.ApproveInArmada, Effective = CliPermissionPolicyEnum.ApproveInArmada, Source = CliPermissionPolicySourceEnum.ServerDefault };
                fx.AddThread(thread, AskFixtures.Message("amg_1", "ath_1", 1, AskMessageRoleEnum.User, AskMessageKindEnum.Text, "run the tests"));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/ask/ath_1", fx.Stub))
                {
                    EventPump events = host.Tui.Context.Events;
                    AssertTrue(host.PumpUntil(() => host.Tui.Ask.Conversation.Messages.Count == 1 && host.Tui.Ask.Conversation.Thread?.CliPermission != null), "loaded");
                    events.Inject(AskFixtures.EventJson("ask.turn", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"state\":\"started\"}"));
                    events.Inject(AskFixtures.EventJson("ask.tool", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"phase\":\"started\",\"id\":\"c1\",\"name\":\"Bash\",\"arguments\":{\"command\":\"npm test\"}}"));
                    events.Inject(AskFixtures.EventJson("ask.tool", "{\"threadId\":\"ath_1\",\"turnId\":\"atn_1\",\"phase\":\"completed\",\"id\":\"c1\",\"ok\":false,\"elapsedMs\":5,\"result\":\"denied\",\"permissionDenied\":true}"));
                    host.Pump();
                    AssertTrue(host.WaitForText("Denied in Armada (see the permission card)."), "explanation under the live chip\n" + host.Screen());
                    TuiCase.Contains(host.Screen(), "[x!] Bash", "refused chip");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "text_helpers", "Countdown, status, and refusal explanations come from typed fields", () =>
            {
                DateTime now = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                AssertEqual("4:12", CliPermissionText.Countdown(now.AddSeconds(252), now), "minutes");
                AssertEqual("0:05", CliPermissionText.Countdown(now.AddSeconds(4.2), now), "rounds up");
                AssertEqual("1:02:05", CliPermissionText.Countdown(now.AddSeconds(3725), now), "hours");
                AssertEqual("Expires in 4:12", CliPermissionText.ExpiresIn(null, now.AddSeconds(252), now), "expires in");
                AssertEqual("Expired", CliPermissionText.ExpiresIn(null, now.AddSeconds(-1), now), "expired");
                AssertEqual("Denied in Armada (see the permission card).", CliPermissionText.DeniedExplanation(null, new CliPermissionResolution { Effective = CliPermissionPolicyEnum.ApproveInArmada }), "approve in armada");
                string refuse = CliPermissionText.DeniedExplanation(null, new CliPermissionResolution { Effective = CliPermissionPolicyEnum.Refuse, Source = CliPermissionPolicySourceEnum.AskThread });
                AssertTrue(refuse.StartsWith("Refused: CLI tools run with policy Refuse (from this conversation).", StringComparison.Ordinal), refuse);
                AssertFalse(refuse.Contains("fell back"), "no fallback sentence without a reason");
                string fallback = CliPermissionText.DeniedExplanation(null, new CliPermissionResolution { Effective = CliPermissionPolicyEnum.Refuse, Source = CliPermissionPolicySourceEnum.ServerDefault, FallbackReason = CliPermissionFallbackReasonEnum.RemoteHarbor });
                AssertTrue(fallback.Contains("(from the server default)") && fallback.EndsWith("the captain runs on a remote harbor.", StringComparison.Ordinal), fallback);
                AssertEqual("Refused: the CLI did not have permission to run this tool.", CliPermissionText.DeniedExplanation(null, null), "unknown resolution");
            }));

            cases.Add(TuiCase.Sync(Suite, "strings_localized", "Every CLI permission string the TUI shows is translated in every maintained locale", () =>
            {
                string path = Path.Combine(ApiSurfaceFiles.FindRepositoryRoot(), "src", "Armada.Server", "wwwroot", "i18n", "armada.json");
                I18nCatalog catalog = ArmadaJson.Deserialize<I18nCatalog>(File.ReadAllText(path)) ?? throw new AssertionException("catalog");
                LocalizationService loc = new LocalizationService();
                loc.SetCatalog(catalog);
                DateTime now = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                CliPermissionResolution resolution = new CliPermissionResolution { Effective = CliPermissionPolicyEnum.Refuse, Source = CliPermissionPolicySourceEnum.Captain, FallbackReason = CliPermissionFallbackReasonEnum.NoSessionToken };
                foreach (string locale in catalog.Locales.Keys)
                {
                    loc.SetLocale(locale);
                    AssertTrue(!CliPermissionText.ExpiresIn(loc, now.AddSeconds(252), now).StartsWith("Expires in", StringComparison.Ordinal), locale + " countdown");
                    AssertTrue(CliPermissionText.ExpiresIn(loc, now.AddSeconds(252), now).Contains("4:12"), locale + " countdown keeps the time");
                    string explanation = CliPermissionText.DeniedExplanation(loc, resolution);
                    AssertFalse(explanation.Contains("CLI tools run with policy"), locale + " explanation: " + explanation);
                    AssertFalse(explanation.Contains("session token"), locale + " fallback: " + explanation);
                    foreach (CliPermissionRuleScopeEnum scope in Enum.GetValues(typeof(CliPermissionRuleScopeEnum)))
                        AssertTrue(CliPermissionText.Scope(loc, scope) != CliPermissionText.Scope(null, scope), locale + " scope " + scope);
                    foreach (CliPermissionPolicySourceEnum source in Enum.GetValues(typeof(CliPermissionPolicySourceEnum)))
                        AssertTrue(CliPermissionText.Source(loc, source) != CliPermissionText.Source(null, source), locale + " source " + source);
                    foreach (string phrase in new[] { "CLI permission", "Allow once", "Allow and remember", "Deny", "An admin must decide this request.", "Waiting for an admin to decide.", "Permission needed", "Allow and Remember", "Deny Permission", "Rule pattern" })
                        AssertTrue(loc.T(phrase) != phrase, locale + " translates \"" + phrase + "\"");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI CLI tool permissions", cases: cases);
        }

        private static CliPermissionRequest Request(bool canDecide, bool canRemember)
        {
            CliPermissionRequest request = new CliPermissionRequest();
            request.Id = "cpr_1";
            request.TenantId = "ten_default";
            request.UserId = "usr_other";
            request.CaptainId = "cpt_1";
            request.CaptainName = "claude-1";
            request.VesselId = "vsl_1";
            request.VesselName = "web";
            request.MissionId = "msn_1";
            request.MissionTitle = "Fix tables";
            request.ToolName = "Bash";
            request.InputText = "{\"command\":\"git push origin main\"}";
            request.SummaryText = "git push origin main";
            request.SuggestedRule = "Bash(git push:*)";
            request.Status = CliPermissionRequestStatusEnum.Pending;
            request.ExpiresUtc = DateTime.UtcNow.AddMinutes(9);
            request.CanDecide = canDecide;
            request.CanRemember = canRemember;
            return request;
        }

        private static TuiTestHost Host(CliPermissionRequest request, out StubHttpHandler stub)
        {
            stub = TuiFixtures.SignedInServer();
            InboxItem item = new InboxItem
            {
                Kind = InboxItemKinds.CliPermission,
                Severity = InboxSeverityEnum.Warning,
                Title = "CLI permission: Bash git push origin main",
                EntityName = "Bash",
                Detail = "server text",
                EntityType = "cli_permission_request",
                EntityId = request.Id,
                Href = "/cli-permissions?request=" + request.Id,
                CliPermission = request,
                ExpiresUtc = request.ExpiresUtc
            };
            stub.Json("GET", "/api/v1/inbox", ArmadaJson.Serialize(new List<InboxItem> { item }));
            CliPermissionRequest decided = ArmadaJson.Deserialize<CliPermissionRequest>(ArmadaJson.Serialize(request))!;
            decided.Status = CliPermissionRequestStatusEnum.Allowed;
            stub.Json("POST", DecidePath, ArmadaJson.Serialize(decided));
            return TuiCase.SignedIn(140, 45, "/approvals", stub);
        }

        private static ApprovalsScreen Load(TuiTestHost host)
        {
            host.Tui.Context.Status.PollAllAsync().GetAwaiter().GetResult();
            AssertTrue(host.PumpUntil(() => host.Tui.Context.Approvals.Find(ApprovalKindEnum.CliPermission, "cpr_1") != null), "CLI permission item queued");
            ApprovalsScreen screen = (ApprovalsScreen)host.Tui.Shell.Screen!;
            host.Press("home");
            host.Pump();
            AssertEqual(ApprovalKindEnum.CliPermission, screen.Current()?.Kind, "selected");
            return screen;
        }

        private static int CountOf(string text, string fragment)
        {
            int count = 0;
            int index = text.IndexOf(fragment, StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
            }

            return count;
        }
    }
}
