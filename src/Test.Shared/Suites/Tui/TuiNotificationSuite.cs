namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Client.Socket;
    using Armada.Tui.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Notifications: dashboard severity and text mapping, dedupe, 100-entry history with persistence, unread and
    /// clear, toasts with actions and expiry, socket events, the center modal, and OSC notification sequences.
    /// </summary>
    public sealed class TuiNotificationSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Notifications";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "severity_mapping", "Statuses map to the dashboard's severities", () =>
            {
                AssertEqual(NotificationSeverityEnum.Success, NotificationService.SeverityFor("Landed"), "landed");
                AssertEqual(NotificationSeverityEnum.Success, NotificationService.SeverityFor("Complete"), "complete");
                AssertEqual(NotificationSeverityEnum.Error, NotificationService.SeverityFor("LandingFailed"), "landing failed");
                AssertEqual(NotificationSeverityEnum.Warning, NotificationService.SeverityFor("Stalled"), "stalled");
                AssertEqual(NotificationSeverityEnum.Warning, NotificationService.SeverityFor("RolledBack"), "rolled back");
                AssertEqual(NotificationSeverityEnum.Info, NotificationService.SeverityFor("InProgress"), "in progress");
            }));

            cases.Add(TuiCase.Sync(Suite, "history_dedupe_cap_persist", "History dedupes, caps at 100, and persists", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "armada-ntf-" + Guid.NewGuid().ToString("N"));
                string file = Path.Combine(dir, "n.json");
                try
                {
                    NotificationService svc = new NotificationService(new SystemClock(), new LocalizationService(), null, null, file);
                    AssertTrue(svc.PushEntityChange("Mission", "msn_1", "Fix", "InProgress"), "first");
                    AssertFalse(svc.PushEntityChange("Mission", "msn_1", "Fix", "InProgress"), "duplicate ignored");
                    for (int i = 0; i < 120; i++) svc.PushEntityChange("Mission", "msn_" + i, "M" + i, "Complete");
                    AssertEqual(100, svc.History.Count, "capped");
                    AssertEqual("Mission \"M119\" - Complete", svc.History[0].Message, "dashboard message text");
                    AssertEqual("/missions/msn_119", svc.History[0].Route, "deep link");
                    AssertEqual(100, svc.UnreadCount, "unread");
                    svc.MarkRead(svc.History[0].Id);
                    AssertEqual(99, svc.UnreadCount, "mark read");
                    NotificationService reloaded = new NotificationService(new SystemClock(), new LocalizationService(), null, null, file);
                    AssertEqual(100, reloaded.History.Count, "persisted");
                    AssertEqual(99, reloaded.UnreadCount, "read state persisted");
                    reloaded.MarkAllRead();
                    AssertEqual(0, reloaded.UnreadCount, "mark all read");
                    reloaded.Clear();
                    AssertEqual(0, reloaded.History.Count, "cleared");
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "toasts_expire_and_act", "Toasts expire after 5 s and run their action with Ctrl+O", () =>
            {
                ManualClock clock = new ManualClock();
                NotificationService svc = new NotificationService(clock, new LocalizationService(), null, null, null);
                int ran = 0;
                svc.Toast(NotificationSeverityEnum.Warning, "Approval needed", "Open", () => ran++);
                AssertEqual(1, svc.ActiveToasts().Count, "visible");
                AssertTrue(svc.RunLatestToastAction(), "action ran");
                AssertEqual(1, ran, "ran once");
                svc.Toast(NotificationSeverityEnum.Info, "Hello");
                clock.Advance(TimeSpan.FromSeconds(6));
                AssertEqual(0, svc.ActiveToasts().Count, "expired");
            }));

            cases.Add(TuiCase.Sync(Suite, "repeated_toast_refreshes", "The same toast raised again while showing refreshes it with a count instead of stacking", () =>
            {
                ManualClock clock = new ManualClock();
                NotificationService svc = new NotificationService(clock, new LocalizationService(), null, null, null);
                svc.Toast(NotificationSeverityEnum.Info, "Nothing to approve here.");
                clock.Advance(TimeSpan.FromSeconds(3));
                svc.Toast(NotificationSeverityEnum.Info, "Nothing to approve here.");
                svc.Toast(NotificationSeverityEnum.Info, "Nothing to approve here.");
                IReadOnlyList<ToastEntry> active = svc.ActiveToasts();
                AssertEqual(1, active.Count, "one toast, not three");
                AssertEqual(3, active[0].Repeat, "repeat count");
                clock.Advance(TimeSpan.FromSeconds(3));
                AssertEqual(1, svc.ActiveToasts().Count, "the repeat restarted its timer");
                svc.Toast(NotificationSeverityEnum.Warning, "Nothing to approve here.");
                AssertEqual(2, svc.ActiveToasts().Count, "a different severity is its own toast");
            }));

            cases.Add(TuiCase.Sync(Suite, "repeated_action_toast_refreshes", "A repeated toast with an action coalesces in TUIKit's NotificationCenter and Ctrl+O runs the newest action", () =>
            {
                ManualClock clock = new ManualClock();
                NotificationService svc = new NotificationService(clock, new LocalizationService(), null, null, null);
                int first = 0;
                int second = 0;
                svc.Toast(NotificationSeverityEnum.Warning, "Permission needed: Bash", "Open", () => first++);
                clock.Advance(TimeSpan.FromSeconds(4));
                svc.Toast(NotificationSeverityEnum.Warning, "Permission needed: Bash", "Open", () => second++);
                IReadOnlyList<ToastEntry> active = svc.ActiveToasts();
                AssertEqual(1, active.Count, "one toast, not two");
                AssertEqual(2, active[0].Repeat, "repeat count");
                AssertEqual(" (x2)", active[0].RepeatSuffix, "repeat suffix from TUIKit's RepeatSuffixFormat");
                AssertEqual("Open", active[0].ActionLabel, "action label kept");
                AssertEqual(2, svc.Toasts.Active((long)(clock.UtcNow - DateTime.UnixEpoch).TotalMilliseconds)[0].RepeatCount, "TUIKit's notification holds the count");
                clock.Advance(TimeSpan.FromSeconds(4));
                AssertEqual(1, svc.ActiveToasts().Count, "the repeat restarted its timer");
                svc.Toast(NotificationSeverityEnum.Warning, "Permission needed: Bash", "Retry", () => first++);
                AssertEqual(2, svc.ActiveToasts().Count, "a different action label is its own toast");
                svc.DismissToasts();
                svc.Toast(NotificationSeverityEnum.Warning, "Permission needed: Bash", "Open", () => first++);
                svc.Toast(NotificationSeverityEnum.Warning, "Permission needed: Bash", "Open", () => second++);
                AssertTrue(svc.RunLatestToastAction(), "Ctrl+O ran an action");
                AssertEqual(0, first, "not the older callback");
                AssertEqual(1, second, "the newest callback");
                AssertEqual(0, svc.ActiveToasts().Count, "the toast is dismissed");
                AssertFalse(svc.RunLatestToastAction(), "nothing left to run");
            }));

            cases.Add(TuiCase.Sync(Suite, "cli_permission_merge_keeps_flags", "A flagless copy of a pending CLI permission request keeps the caller's decision flags", () =>
            {
                Armada.Core.Models.CliPermissionRequest withFlags = new Armada.Core.Models.CliPermissionRequest { ToolName = "Bash", CanDecide = true, CanRemember = true };
                Armada.Core.Models.CliPermissionRequest flagless = new Armada.Core.Models.CliPermissionRequest { Id = withFlags.Id, ToolName = "Bash", LastUpdateUtc = withFlags.LastUpdateUtc.AddSeconds(1) };
                Armada.Core.Models.CliPermissionRequest merged = Armada.Tui.Ask.AskConversation.MergeCliPermission(withFlags, flagless);
                AssertTrue(merged.CanDecide, "can decide kept");
                AssertTrue(merged.CanRemember, "can remember kept");
            }));

            cases.Add(TuiCase.Sync(Suite, "socket_event_to_toast", "mission.changed over the socket raises a toast and bell count", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    ArmadaSocketMessage msg = ArmadaSocketMessage.Parse("{\"type\":\"mission.changed\",\"data\":{\"id\":\"msn_9\",\"title\":\"Fix tables\",\"status\":\"Failed\"}}")!;
                    host.Tui.Context.Events.Inject(msg);
                    AssertTrue(host.WaitForText("Mission \"Fix tables\" - Failed"), "toast");
                    TuiCase.Contains(host.Screen(), "[bell 1]", "bell count");
                    TuiCase.Contains(host.Screen(), "[Ctrl+O] Open", "action hint");
                    host.Press("ctrl+o");
                    AssertEqual("/missions/msn_9", host.Tui.Context.Router.Current!.Path, "toast action opens the mission");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "six_entity_events", "All six entity-change events map to the dashboard's text, severity, and links", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/missions"))
                {
                    NotificationService n = host.Tui.Context.Notifications;
                    n.Clear();
                    string[] events = new string[]
                    {
                        "{\"type\":\"mission.changed\",\"data\":{\"id\":\"msn_1\",\"title\":\"Fix tables\",\"status\":\"Landed\"}}",
                        "{\"type\":\"voyage.changed\",\"data\":{\"id\":\"vyg_1\",\"title\":\"Table work\",\"status\":\"Complete\"}}",
                        "{\"type\":\"captain.changed\",\"data\":{\"id\":\"cpt_1\",\"name\":\"claude-1\",\"state\":\"Stalled\"}}",
                        "{\"type\":\"deployment.changed\",\"data\":{\"id\":\"dpl_1\",\"title\":\"Staging\",\"status\":\"Succeeded\",\"verificationStatus\":\"Passed\"}}",
                        "{\"type\":\"objective.changed\",\"data\":{\"id\":\"obj_1\",\"title\":\"Faster CI\",\"status\":\"InProgress\"}}",
                        "{\"type\":\"incident.changed\",\"data\":{\"id\":\"inc_1\",\"title\":\"Outage\",\"status\":\"Failed\"}}"
                    };
                    foreach (string e in events) host.Tui.Context.Events.Inject(ArmadaSocketMessage.Parse(e)!);
                    host.Pump();
                    AssertEqual(6, n.History.Count, "six notifications");
                    Dictionary<string, NotificationEntry> byRoute = n.History.ToDictionary(h => h.Route ?? "", h => h);
                    AssertEqual("Mission \"Fix tables\" - Landed", byRoute["/missions/msn_1"].Message, "mission text");
                    AssertEqual(NotificationSeverityEnum.Success, byRoute["/missions/msn_1"].Severity, "mission severity");
                    AssertEqual("Voyage Complete", byRoute["/voyages/vyg_1"].Title, "voyage title");
                    AssertEqual("Captain \"claude-1\" - Stalled", byRoute["/captains/cpt_1"].Message, "captain uses name and state");
                    AssertEqual(NotificationSeverityEnum.Warning, byRoute["/captains/cpt_1"].Severity, "stalled is a warning");
                    AssertEqual("Succeeded / Passed", byRoute["/deployments/dpl_1"].Status, "deployment adds verification");
                    AssertEqual(NotificationSeverityEnum.Info, byRoute["/backlog/obj_1"].Severity, "objective links to the backlog item");
                    AssertEqual(NotificationSeverityEnum.Error, byRoute["/incidents/inc_1"].Severity, "incident failure is an error");
                    AssertTrue(n.ActiveToasts().Count >= 6, "every change toasts");
                    host.Tui.Context.Events.Inject(ArmadaSocketMessage.Parse(events[0])!);
                    host.Pump();
                    AssertEqual(6, n.History.Count, "repeated status deduplicated");
                    host.Tui.Context.Events.Inject(ArmadaSocketMessage.Parse("{\"type\":\"mission.changed\",\"data\":{\"id\":\"msn_2\"}}")!);
                    host.Pump();
                    AssertEqual(6, n.History.Count, "no status, no notification");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "center_modal", "The notification center lists entries and Enter opens the item", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    host.Tui.Context.Notifications.PushEntityChange("Voyage", "vyg_1", "Release train", "Complete");
                    host.Press("ctrl+n");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Notifications", "title");
                    TuiCase.Contains(frame, "Voyage Complete", "entry title");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.Path == "/voyages/vyg_1"), "opened");
                    AssertEqual(0, host.Tui.Context.Notifications.UnreadCount, "marked read");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "os_notification_sequences", "OSC 9 and OSC 777 sequences are well formed", () =>
            {
                AssertEqual("\u001b]9;Armada: Done\u0007", OsNotifier.Sequence(OsNotificationModeEnum.Osc9, "Armada", "Done"), "osc 9");
                AssertEqual("\u001b]777;notify;Armada;a, b\u0007", OsNotifier.Sequence(OsNotificationModeEnum.Osc777, "Armada", "a; b"), "osc 777");
                AssertNull(OsNotifier.Sequence(OsNotificationModeEnum.Native, "a", "b"), "native uses no escape");
            }));

            cases.Add(TuiCase.Sync(Suite, "approvals_header_count", "The approvals queue drives the header count and rings for attention", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    ApprovalItem item = new ApprovalItem();
                    item.Kind = ApprovalKindEnum.MissionReview;
                    item.EntityId = "msn_1";
                    item.Title = "Review: Fix tables";
                    host.Tui.Context.Approvals.Upsert(item);
                    TuiCase.Contains(host.Screen(), "[!1 approvals]", "header count");
                    host.Tui.Context.Approvals.Remove(ApprovalKindEnum.MissionReview, "msn_1");
                    TuiCase.NotContains(host.Screen(), "approvals]", "cleared");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI notifications and approvals", cases: cases);
        }
    }
}
