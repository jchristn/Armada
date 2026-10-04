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
