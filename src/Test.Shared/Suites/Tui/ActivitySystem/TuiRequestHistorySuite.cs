namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Screens.Admin;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless keyboard flows for API Requests (request history) against a stubbed server: KPIs and rows, filters,
    /// role-gated filters and bulk actions, deletes, the detail drawer, the deep link, and replay into the explorer.
    /// </summary>
    public sealed class TuiRequestHistorySuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Activity.Requests";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "kpis_rows_chart", "API Requests shows KPIs, the activity chart, and the request rows", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/activity?source=requests", stub))
                {
                    AssertTrue(host.WaitForText("/api/v1/missions/msn_1"), "rows render");
                    AssertTrue(host.WaitForText("66.7%"), "success rate");
                    string frame = host.Screen();
                    TuiScreenDump.Write("requests", frame);
                    TuiCase.Contains(frame, "Total Requests", "kpi");
                    TuiCase.Contains(frame, "Average Duration", "kpi");
                    TuiCase.Contains(frame, "12.50 ms", "avg duration");
                    TuiCase.Contains(frame, "Last Day", "range tab");
                    TuiCase.Contains(frame, "Success", "chart legend");
                    TuiCase.Contains(frame, "! 500", "failure status marker");
                    TuiCase.Contains(frame, "Delete Visible Range", "bulk range button");
                    AssertTrue(stub.Requests.Any(r => r.StartsWith("GET /api/v1/request-history/summary?", StringComparison.Ordinal) && r.Contains("bucketMinutes=15")), "summary with bucket minutes");
                    RequestHistoryScreen screen = Current<RequestHistoryScreen>(host);
                    screen.SetRange("lastHour");
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.Contains("summary") && (r.Contains("bucketMinutes=1&") || r.EndsWith("bucketMinutes=1", StringComparison.Ordinal)))), "hour range requery");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_send_query", "Filters (f) send method, route, and result to the server and switch the bulk action to Delete Filtered", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/activity?source=requests", stub))
                {
                    AssertTrue(host.WaitForText("/api/v1/missions/msn_1"), "rows render");
                    host.Press("f");
                    RequestHistoryScreen screen = Current<RequestHistoryScreen>(host);
                    AssertTrue(screen.Filters.Visible, "filters expanded");
                    TuiCase.Contains(host.Screen(), "Status Code", "filter field");
                    screen.MethodFilter.Choose(screen.MethodFilter.Options.First(o => o.Value == "POST"));
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.StartsWith("GET /api/v1/request-history?", StringComparison.Ordinal) && r.Contains("method=POST"))), "method filter sent");
                    screen.Filters.Scope.Focus(screen.RouteFilter);
                    host.Type("/api/v1/fleets");
                    host.Press("enter");
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.Contains("route=/api/v1/fleets"))), "route filter sent: " + String.Join("\n", stub.Requests));
                    screen.ResultFilter.Choose(screen.ResultFilter.Options.First(o => o.Value == "false"));
                    AssertTrue(host.PumpUntil(() => stub.Requests.Any(r => r.Contains("isSuccess=false"))), "result filter sent");
                    AssertTrue(host.WaitForText("Delete Filtered"), "label switches");
                    TuiScreenDump.Write("requests-filters", host.Screen());
                    screen.ResetFilters();
                    AssertFalse(screen.HasActiveFilters, "reset clears");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "role_gating_tenant_admin", "A tenant admin gets the User filter but not the Tenant filter; a global admin gets both", () =>
            {
                StubHttpHandler stub = Stub();
                stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Tenant\":{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true},\"User\":{\"Id\":\"usr_ta\",\"TenantId\":\"ten_default\",\"Email\":\"ta@armada\",\"IsAdmin\":false,\"IsTenantAdmin\":true,\"Active\":true}}"));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/activity?source=requests", stub))
                {
                    AssertTrue(host.WaitForText("/api/v1/missions/msn_1"), "rows render");
                    RequestHistoryScreen screen = Current<RequestHistoryScreen>(host);
                    AssertFalse(screen.TenantFilter.Visible, "tenant filter hidden for tenant admin");
                    AssertTrue(screen.UserFilter.Visible, "user filter visible for tenant admin");
                    host.Press("f");
                    string frame = host.Screen();
                    TuiCase.NotContains(frame, "Tenant:", "no tenant filter label");
                    TuiCase.Contains(frame, "User:", "user filter label");
                    TuiCase.Contains(frame, "Delete Visible Range", "range delete available");
                }

                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/activity?source=requests", Stub()))
                {
                    AssertTrue(host.WaitForText("/api/v1/missions/msn_1"), "rows render");
                    RequestHistoryScreen screen = Current<RequestHistoryScreen>(host);
                    AssertTrue(screen.TenantFilter.Visible, "tenant filter for admin");
                    AssertTrue(screen.UserFilter.Visible, "user filter for admin");
                }

                StubHttpHandler user = Stub();
                user.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(HttpStatusCode.OK, "{\"Tenant\":{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true},\"User\":{\"Id\":\"usr_u\",\"TenantId\":\"ten_default\",\"Email\":\"u@armada\",\"IsAdmin\":false,\"IsTenantAdmin\":false,\"Active\":true}}"));
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/activity?source=requests", user))
                {
                    AssertTrue(host.WaitForText("/api/v1/missions/msn_1"), "rows render");
                    RequestHistoryScreen screen = Current<RequestHistoryScreen>(host);
                    AssertFalse(screen.TenantFilter.Visible, "tenant filter hidden for user");
                    AssertFalse(screen.UserFilter.Visible, "user filter hidden for user");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "delete_selected_and_filtered", "Delete Selected confirms and posts the ids; Delete Visible Range confirms and posts the filter", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/activity?source=requests", stub))
                {
                    AssertTrue(host.WaitForText("/api/v1/missions/msn_1"), "rows render");
                    host.Press("space").Press("down").Press("space");
                    AssertTrue(host.WaitForText("Delete Selected"), "bulk button");
                    host.Press("del");
                    AssertTrue(host.WaitForText("Delete 2 selected request-history entries?"), "confirm text");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/request-history/delete/multiple") == 1), "batch delete sent");
                    string bodies = String.Join("\n", stub.Bodies);
                    AssertTrue(bodies.Contains("req_1") && bodies.Contains("req_2"), "ids sent");
                    AssertTrue(host.WaitForText("Deleted 2 request entries."), "toast");

                    RequestHistoryScreen screen = Current<RequestHistoryScreen>(host);
                    screen.DeleteFilteredButton.Press();
                    AssertTrue(host.WaitForText("Delete all request-history entries matching the current filters?"), "filtered confirm");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.Count("POST /api/v1/request-history/delete/by-filter") == 1), "by-filter delete sent");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "drawer_on_enter", "Enter opens the request detail drawer with summary, headers, and bodies", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/activity?source=requests", stub))
                {
                    AssertTrue(host.WaitForText("/api/v1/missions/msn_1"), "rows render");
                    host.Press("enter");
                    AssertTrue(host.WaitForText("Entry ID"), "drawer summary");
                    AssertTrue(host.WaitForText("Stored body was truncated"), "truncation note");
                    string frame = host.Screen();
                    TuiScreenDump.Write("requests-drawer", frame);
                    TuiCase.Contains(frame, "Request Detail", "drawer title");
                    TuiCase.Contains(frame, "Bearer", "auth method");
                    TuiCase.Contains(frame, "Request Headers", "headers block");
                    TuiCase.Contains(frame, "Response Body", "response body block");
                    host.Press("esc");
                    RequestHistoryScreen screen = Current<RequestHistoryScreen>(host);
                    AssertFalse(screen.DetailDrawer.IsOpen, "esc closes");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "deep_link", "/requests/:id opens the drawer for that entry", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/requests/req_1", stub))
                {
                    AssertTrue(host.WaitForText("Entry ID"), "drawer opens");
                    AssertTrue(stub.Count("GET /api/v1/request-history/req_1") >= 1, "entry fetched");
                    RequestHistoryScreen screen = Current<RequestHistoryScreen>(host);
                    AssertTrue(screen.DetailDrawer.IsOpen, "drawer open");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "replay_into_explorer", "Replay from the drawer opens the API Explorer prefilled with the captured request", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(160, 50, "/activity?source=requests", stub))
                {
                    AssertTrue(host.WaitForText("/api/v1/missions/msn_1"), "rows render");
                    host.Press("enter");
                    AssertTrue(host.WaitForText("Entry ID"), "drawer");
                    host.Press("r");
                    AssertTrue(host.PumpUntil(() => host.Tui.Shell.Screen is ApiExplorerScreen), "explorer opened");
                    ApiExplorerScreen explorer = (ApiExplorerScreen)host.Tui.Shell.Screen!;
                    AssertTrue(host.PumpUntil(() => explorer.Selected != null && explorer.Selected.Id == "updateMission"), "operation matched");
                    AssertEqual("msn_1", explorer.PathFields["id"].Value, "path value");
                    AssertTrue(explorer.BodyField != null && explorer.BodyField.Value.Contains("Renamed"), "body prefilled");
                    AssertEqual("trace-1", explorer.HeaderFields["X-Trace"].Value, "captured header");
                    AssertFalse(explorer.HeaderFields.ContainsKey("X-Token"), "token header dropped");
                    TuiScreenDump.Write("requests-replay", host.Screen());
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI API Requests", cases: cases);
        }

        /// <summary>
        /// A signed-in stub with request history, summary, entry, delete, and OpenAPI endpoints.
        /// </summary>
        /// <returns>Stub.</returns>
        internal static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            string now = DateTime.UtcNow.ToString("o");
            stub.Json("GET", "/api/v1/request-history", "{\"Objects\":["
                + "{\"Id\":\"req_1\",\"Method\":\"PUT\",\"Route\":\"/api/v1/missions/msn_1\",\"RouteTemplate\":\"/api/v1/missions/{id}\",\"PrincipalDisplay\":\"admin@armada\",\"AuthMethod\":\"Bearer\",\"StatusCode\":200,\"DurationMs\":12.5,\"RequestSizeBytes\":120,\"ResponseSizeBytes\":2048,\"IsSuccess\":true,\"CreatedUtc\":\"" + now + "\"},"
                + "{\"Id\":\"req_2\",\"Method\":\"GET\",\"Route\":\"/api/v1/fleets\",\"PrincipalDisplay\":null,\"StatusCode\":500,\"DurationMs\":3,\"RequestSizeBytes\":0,\"ResponseSizeBytes\":80,\"IsSuccess\":false,\"CreatedUtc\":\"" + now + "\"},"
                + "{\"Id\":\"req_3\",\"Method\":\"GET\",\"Route\":\"/api/v1/status/health\",\"StatusCode\":200,\"DurationMs\":1,\"IsSuccess\":true,\"CreatedUtc\":\"" + now + "\"}"
                + "],\"TotalRecords\":3,\"TotalPages\":1}");
            DateTime bucket = new DateTime((DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(15).Ticks) * TimeSpan.FromMinutes(15).Ticks, DateTimeKind.Utc);
            stub.Json("GET", "/api/v1/request-history/summary", "{\"TotalCount\":3,\"SuccessCount\":2,\"FailureCount\":1,\"SuccessRate\":66.666,\"AverageDurationMs\":12.5,\"BucketMinutes\":15,\"Buckets\":[{\"BucketStartUtc\":\"" + bucket.ToString("o") + "\",\"BucketEndUtc\":\"" + bucket.AddMinutes(15).ToString("o") + "\",\"TotalCount\":3,\"SuccessCount\":2,\"FailureCount\":1,\"AverageDurationMs\":12.5}]}");
            stub.Json("GET", "/api/v1/request-history/req_1", "{\"Entry\":{\"Id\":\"req_1\",\"Method\":\"PUT\",\"Route\":\"/api/v1/missions/msn_1\",\"RouteTemplate\":\"/api/v1/missions/{id}\",\"PrincipalDisplay\":\"admin@armada\",\"AuthMethod\":\"Bearer\",\"StatusCode\":200,\"DurationMs\":12.5,\"RequestSizeBytes\":120,\"ResponseSizeBytes\":2048,\"IsSuccess\":true,\"CreatedUtc\":\"" + now + "\"},"
                + "\"Detail\":{\"RequestHistoryId\":\"req_1\",\"PathParamsJson\":\"{\\\"id\\\":\\\"msn_1\\\"}\",\"QueryParamsJson\":\"{}\",\"RequestHeadersJson\":\"{\\\"X-Token\\\":\\\"secret\\\",\\\"X-Trace\\\":\\\"trace-1\\\"}\",\"ResponseHeadersJson\":\"{\\\"content-type\\\":\\\"application/json\\\"}\","
                + "\"RequestBodyText\":\"{\\\"Title\\\":\\\"Renamed\\\"}\",\"ResponseBodyText\":\"{\\\"Id\\\":\\\"msn_1\\\"}\",\"RequestBodyTruncated\":false,\"ResponseBodyTruncated\":true}}");
            stub.Json("POST", "/api/v1/request-history/delete/multiple", "{\"Deleted\":2}");
            stub.Json("POST", "/api/v1/request-history/delete/by-filter", "{\"Deleted\":3}");
            stub.Json("GET", "/openapi.json", TuiApiExplorerSuite.OpenApiJson);
            return stub;
        }

        internal static T Current<T>(TuiTestHost host) where T : ScreenBase
        {
            ScreenBase? screen = host.Tui.Shell.Screen;
            if (screen is HubScreen hub) screen = hub.Content;
            if (screen is T typed) return typed;
            throw new AssertionException("current screen is " + (screen?.GetType().Name ?? "null") + ", expected " + typeof(T).Name);
        }
    }
}
