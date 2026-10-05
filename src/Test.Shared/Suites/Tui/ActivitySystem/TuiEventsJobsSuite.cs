namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Screens.Admin;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless keyboard flows for Events, Event detail, Jobs, and Diagnostics against a stubbed server.
    /// </summary>
    public sealed class TuiEventsJobsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Activity.Events";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "events_list_filter", "Events lists rows, names captains and vessels, and filters by event type", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/activity?source=events", stub))
                {
                    AssertTrue(host.WaitForText("mission.completed"), "rows render");
                    string frame = host.Screen();
                    TuiScreenDump.Write("events", frame);
                    TuiCase.Contains(frame, "Events", "title");
                    TuiCase.Contains(frame, "Alpha", "captain name");
                    TuiCase.Contains(frame, "Showing 1-3 of 3", "paging");
                    EventsScreen screen = Current<EventsScreen>(host);
                    screen.EventTypeFilter.Value = "mission.failed";
                    host.Pump();
                    screen.Grid.Reload();
                    AssertTrue(host.PumpUntil(() => screen.Grid.Rows.Count == 1), "one row after filter");
                    AssertEqual("evt_2", screen.Grid.Rows[0].Id, "filtered row");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "events_bulk_delete", "Selecting rows and Del confirms then deletes the batch", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 40, "/activity?source=events", stub))
                {
                    AssertTrue(host.WaitForText("mission.completed"), "rows render");
                    host.Press("space").Press("down").Press("space");
                    EventsScreen screen = Current<EventsScreen>(host);
                    AssertEqual(2, screen.Grid.Marked.Count, "two marked");
                    AssertTrue(host.WaitForText("Delete Selected"), "bulk button");
                    host.Press("del");
                    AssertTrue(host.WaitForText("Delete 2 selected event(s)?"), "confirm text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/events/delete/multiple") == 1), "batch delete sent");
                    StubRequest batch = stub.Last("POST", "/api/v1/events/delete/multiple");
                    AssertEqual("evt_1|evt_2", String.Join("|", batch.BodyAs<Armada.Core.Models.DeleteMultipleRequest>().Ids.OrderBy(i => i, StringComparer.Ordinal)), "ids sent: " + batch.Body);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "event_detail", "Event detail shows fields and pretty-prints the payload", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/events/evt_1", stub))
                {
                    AssertTrue(host.WaitForText("Event Details"), "title");
                    AssertTrue(host.WaitForText("\"Status\": \"Complete\""), "payload pretty-printed");
                    string frame = host.Screen();
                    TuiScreenDump.Write("event-detail", frame);
                    TuiCase.Contains(frame, "msn_1", "mission link");
                    TuiCase.Contains(frame, "Payload", "payload title");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "diagnostics", "Diagnostics runs checks on open and shows counts and the verdict", () =>
            {
                StubHttpHandler stub = Stub();
                stub.Json("GET", "/api/v1/doctor", "[{\"Name\":\"Database\",\"Status\":\"Pass\",\"Message\":\"ok\"},{\"Name\":\"Git\",\"Status\":\"Warn\",\"Message\":\"old git\"}]");
                using (TuiTestHost host = TuiCase.SignedIn(140, 40, "/server?tab=diagnostics", stub))
                {
                    AssertTrue(host.WaitForText("old git"), "rows");
                    string frame = host.Screen();
                    TuiScreenDump.Write("diagnostics", frame);
                    TuiCase.Contains(frame, "[Warnings]", "verdict");
                    TuiCase.Contains(frame, "Passed", "kpi");
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("GET", "/api/v1/doctor") >= 2), "rerun");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI Events, Jobs, Diagnostics", cases: cases);
        }

        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/events", "{\"Objects\":["
                + "{\"Id\":\"evt_1\",\"EventType\":\"mission.completed\",\"EntityType\":\"mission\",\"EntityId\":\"msn_1\",\"CaptainId\":\"cpt_1\",\"MissionId\":\"msn_1\",\"VesselId\":\"vsl_1\",\"Message\":\"done\",\"Payload\":\"{\\\"Status\\\":\\\"Complete\\\"}\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\"},"
                + "{\"Id\":\"evt_2\",\"EventType\":\"mission.failed\",\"EntityType\":\"mission\",\"EntityId\":\"msn_2\",\"Message\":\"failed\",\"CreatedUtc\":\"2026-10-04T09:00:00Z\"},"
                + "{\"Id\":\"evt_3\",\"EventType\":\"captain.stalled\",\"EntityType\":\"captain\",\"EntityId\":\"cpt_1\",\"Message\":\"stalled\",\"CreatedUtc\":\"2026-10-04T08:00:00Z\"}"
                + "],\"TotalRecords\":3,\"TotalPages\":1}");
            stub.Json("GET", "/api/v1/events/evt_1", "{\"Id\":\"evt_1\",\"EventType\":\"mission.completed\",\"EntityType\":\"mission\",\"EntityId\":\"msn_1\",\"CaptainId\":\"cpt_1\",\"MissionId\":\"msn_1\",\"VesselId\":\"vsl_1\",\"Message\":\"done\",\"Payload\":\"{\\\"Status\\\":\\\"Complete\\\"}\",\"TenantId\":\"ten_default\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\"}");
            stub.Json("GET", "/api/v1/captains", "{\"Objects\":[{\"Id\":\"cpt_1\",\"Name\":\"Alpha\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/vessels", "{\"Objects\":[{\"Id\":\"vsl_1\",\"Name\":\"armada\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/users", "{\"Objects\":[{\"Id\":\"usr_admin\",\"Email\":\"admin@armada\"}],\"TotalRecords\":1}");
            stub.On("POST", "/api/v1/events/delete/multiple", body => StubHttpHandler.Response(System.Net.HttpStatusCode.OK, "{\"Deleted\":2}"));
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
