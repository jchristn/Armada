namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless flows for Signals and Signal detail against a stubbed server.
    /// </summary>
    public sealed class TuiSignalsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Activity.Signals";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "signals_list_filters", "Signals lists rows with captain names and sends the type, captain, and unread filters", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/activity?source=signals", stub))
                {
                    AssertTrue(host.WaitForText("sig_1"), "rows");
                    string frame = host.Screen();
                    TuiScreenDump.Write("signals", frame);
                    TuiCase.Contains(frame, "Alpha", "captain name");
                    TuiCase.Contains(frame, "Admiral", "admiral");
                    SignalsScreen screen = Current<SignalsScreen>(host);
                    screen.TypeFilter.Choose(screen.TypeFilter.Options.First(o => o.Value == "Mail"));
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.StartsWith("GET /api/v1/signals?") && r.Contains("type=Mail"))), "type filter sent");
                    AssertTrue(host.PumpUntil(() => screen.CaptainFilter.Options.Count > 1), "captains loaded");
                    screen.CaptainFilter.Choose(screen.CaptainFilter.Options.First(o => o.Value == "cpt_1"));
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.Contains("toCaptainId=cpt_1"))), "captain filter sent");
                    screen.UnreadOnly.SetValue(true);
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.Contains("unreadOnly=true"))), "unread filter sent");
                    AssertTrue(host.WaitForText("Clear Filters"), "clear button");
                    screen.PayloadColumn.Value = "hello";
                    screen.Grid.Reload();
                    AssertTrue(host.PumpUntil(() => screen.Grid.Rows.Count == 1), "payload column filter");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "signals_send", "Send Signal posts the type, payload, and target captain", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/activity?source=signals", stub))
                {
                    AssertTrue(host.WaitForText("sig_1"), "rows");
                    SignalsScreen screen = Current<SignalsScreen>(host);
                    AssertTrue(host.PumpUntil(() => screen.CaptainFilter.Options.Count > 1), "captains loaded");
                    FormModal modal = screen.OpenSendSignal();
                    host.Pump();
                    TuiScreenDump.Write("signals-send", host.Screen());
                    TuiCase.Contains(host.Screen(), "Admiral (broadcast)", "default target");
                    ((MultilineField)modal.Form.Rows.First(r => r.Label == "Payload").Field!).Value = "hello captain";
                    ((SelectField<string>)modal.Form.Rows.First(r => r.Label == "To Captain (optional)").Field!).SetValue("cpt_1");
                    ((SelectField<string>)modal.Form.Rows.First(r => r.Label == "Type").Field!).SetValue("Mail");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/signals") == 1), "posted");
                    string body = stub.Bodies.Last(b => b.Contains("hello captain"));
                    AssertTrue(body.Contains("\"Type\":\"Mail\"") && body.Contains("\"Payload\":\"hello captain\"") && body.Contains("\"ToCaptainId\":\"cpt_1\""), "body: " + body);
                    AssertTrue(host.WaitForText("Signal sent."), "toast");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "signals_mark_read_delete", "r marks the cursor signal read and bulk delete removes the selection", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/activity?source=signals", stub))
                {
                    AssertTrue(host.WaitForText("sig_1"), "rows");
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/signals/sig_1/read") == 1), "mark read");
                    AssertTrue(host.WaitForText("Signal marked as read."), "toast");
                    host.Press("space").Press("down").Press("space").Press("del");
                    AssertTrue(host.WaitForText("Delete 2 selected signal(s)?"), "confirm");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/signals/delete/multiple") == 1), "batch delete");
                    string body = stub.Bodies.Last(b => b.Contains("Ids"));
                    AssertTrue(body.Contains("sig_1") && body.Contains("sig_2"), "ids: " + body);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "signal_detail", "Signal detail shows fields, the mission link, and the pretty payload; Mark Read reloads", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/signals/sig_1", stub))
                {
                    AssertTrue(host.WaitForText("Signal Details"), "title");
                    AssertTrue(host.WaitForText("\"step\": 2"), "payload pretty-printed");
                    AssertTrue(host.WaitForText("msn_9"), "mission link");
                    string frame = host.Screen();
                    TuiScreenDump.Write("signal-detail", frame);
                    TuiCase.Contains(frame, "Alpha", "to captain");
                    TuiCase.Contains(frame, "Mark Read", "mark read button");
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => stub.Count("PUT /api/v1/signals/sig_1/read") == 1), "mark read");
                    host.Press("del");
                    AssertTrue(host.WaitForText("Delete signal sig_1?"), "confirm");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/signals/delete/multiple") == 1), "deleted");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI Signals", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/signals", "{\"Objects\":["
                + "{\"Id\":\"sig_1\",\"Type\":\"Progress\",\"ToCaptainId\":\"cpt_1\",\"Payload\":\"{\\\"step\\\":2}\",\"Read\":false,\"CreatedUtc\":\"2026-10-04T10:00:00Z\"},"
                + "{\"Id\":\"sig_2\",\"Type\":\"Mail\",\"FromCaptainId\":\"cpt_1\",\"Payload\":\"hello\",\"Read\":true,\"CreatedUtc\":\"2026-10-04T09:00:00Z\"}"
                + "],\"TotalRecords\":2,\"TotalPages\":1}");
            stub.Json("GET", "/api/v1/signals/sig_1", "{\"Id\":\"sig_1\",\"Type\":\"Progress\",\"ToCaptainId\":\"cpt_1\",\"MissionId\":\"msn_9\",\"TenantId\":\"ten_default\",\"Payload\":\"{\\\"step\\\":2}\",\"Read\":false,\"CreatedUtc\":\"2026-10-04T10:00:00Z\"}");
            stub.Json("GET", "/api/v1/captains", "{\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"Alpha\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/users", "{\"Objects\":[{\"Id\":\"usr_admin\",\"Email\":\"admin@armada\"}],\"TotalRecords\":1}");
            stub.On("POST", "/api/v1/signals", body => StubHttpHandler.Response(HttpStatusCode.Created, "{\"Id\":\"sig_3\",\"Type\":\"Mail\"}"));
            stub.On("PUT", "/api/v1/signals/sig_1/read", body => StubHttpHandler.Response(HttpStatusCode.NoContent, ""));
            stub.On("POST", "/api/v1/signals/delete/multiple", body => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Deleted\":2}"));
            return stub;
        }

        private static T Current<T>(TuiTestHost host) where T : ScreenBase
        {
            ScreenBase? screen = host.Tui.Shell.Screen;
            if (screen is HubScreen hub) screen = hub.Content;
            if (screen is T typed) return typed;
            throw new AssertionException("current screen is " + (screen?.GetType().Name ?? "null") + ", expected " + typeof(T).Name);
        }
    }
}
