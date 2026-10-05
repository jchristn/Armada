namespace Test.Shared.Suites.Tui.ActivitySystem
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Tui.Screens;
    using Armada.Tui.Screens.Activity;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Headless flows for All Activity (History): rows and KPIs, filters, saved views, exports, and request-entry
    /// delete, against a stubbed server.
    /// </summary>
    public sealed class TuiActivityHistorySuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Activity.History";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "rows_and_kpis", "All Activity lists entries with KPIs, source counts, and vessel names", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 44, "/activity?source=history", stub))
                {
                    AssertTrue(host.WaitForText("Deploy failed"), "rows render");
                    string frame = host.Screen();
                    TuiScreenDump.Write("activity-history", frame);
                    TuiCase.Contains(frame, "Visible Entries", "kpi");
                    TuiCase.Contains(frame, "Source Types", "kpi");
                    TuiCase.Contains(frame, "deployment (1)", "source counts");
                    TuiCase.Contains(frame, "armada", "vessel name");
                    ActivityScreen screen = Current<ActivityScreen>(host);
                    AssertEqual(3, screen.Entries.Count, "entries");
                    TuiCase.Contains(screen.Kpis.ToPlainText(), "Errors: 1", "error count");
                    TuiCase.Contains(screen.Kpis.ToPlainText(), "Warnings: 1", "warning count");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filters_query", "Filters and deep-link parameters are sent in the enumerate body", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 44, "/activity?source=history&actor=alice&postmortemOnly=true", stub))
                {
                    AssertTrue(host.WaitForText("Deploy failed"), "rows render");
                    AssertTrue(stub.BodiesFor<Armada.Core.Models.HistoricalTimelineQuery>("POST", "/api/v1/history/enumerate").Any(q => q.Actor == "alice" && q.PostmortemOnly), "deep link filters: " + String.Join("\n", stub.RequestsFor("POST", "/api/v1/history/enumerate").Select(r => r.Body)));
                    ActivityScreen screen = Current<ActivityScreen>(host);
                    screen.TextFilter.Value = "deploy";
                    screen.SourceTypeFilter.SetValue("deployment");
                    screen.VesselFilter.SetValue("vsl_1");
                    int before = stub.CountFor("POST", "/api/v1/history/enumerate");
                    host.Press("a");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("POST", "/api/v1/history/enumerate") > before), "apply reloads");
                    StubRequest applied = stub.Last("POST", "/api/v1/history/enumerate");
                    Armada.Core.Models.HistoricalTimelineQuery query = applied.BodyAs<Armada.Core.Models.HistoricalTimelineQuery>();
                    AssertEqual("deploy", query.Text, "text: " + applied.Body);
                    AssertEqual("deployment", String.Join("|", query.SourceTypes), "source: " + applied.Body);
                    AssertEqual("vsl_1", query.VesselId, "vessel: " + applied.Body);
                    AssertEqual(250, query.PageSize, "page size: " + applied.Body);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "saved_views", "Save View persists a named view; applying it restores filters; delete removes it", () =>
            {
                StubHttpHandler stub = Stub();
                using (TuiTestHost host = TuiCase.SignedIn(170, 44, "/activity?source=history", stub))
                {
                    AssertTrue(host.WaitForText("Deploy failed"), "rows render");
                    ActivityScreen screen = Current<ActivityScreen>(host);
                    screen.ActorFilter.Value = "bob";
                    host.Press("w");
                    AssertTrue(host.WaitForText("Save History View"), "save dialog");
                    host.Type("Staging failures");
                    host.Press("ctrl+s");
                    AssertTrue(host.PumpUntil(() => screen.SavedViews.Views.Count == 1), "view saved");
                    string file = Path.Combine(host.TempDir, "tui-activity-views.json");
                    AssertTrue(File.Exists(file), "views file");
                    TuiCase.Contains(File.ReadAllText(file), "Staging failures", "persisted name");
                    TuiCase.Contains(File.ReadAllText(file), "bob", "persisted filter");
                    AssertTrue(host.WaitForText("[Staging failures]"), "saved views line");

                    screen.ActorFilter.Value = "";
                    ActivitySavedViewStore reloaded = new ActivitySavedViewStore(file);
                    AssertEqual(1, reloaded.Views.Count, "reload from disk");
                    screen.ApplySavedView(screen.SavedViews.Views[0]);
                    AssertEqual("bob", screen.ActorFilter.Value, "applied actor");
                    screen.DeleteSavedView(screen.SavedViews.Views[0].Id);
                    AssertEqual(0, new ActivitySavedViewStore(file).Views.Count, "deleted on disk");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "exports", "Export CSV, JSON, and Markdown each write a file with the dashboard's content", () =>
            {
                StubHttpHandler stub = Stub();
                string dir = Path.Combine(Path.GetTempPath(), "armada-tui-export-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string? previous = Environment.GetEnvironmentVariable("ARMADA_TUI_SAVE_DIR");
                Environment.SetEnvironmentVariable("ARMADA_TUI_SAVE_DIR", dir);
                try
                {
                    using (TuiTestHost host = TuiCase.SignedIn(170, 44, "/activity?source=history", stub))
                    {
                        AssertTrue(host.WaitForText("Deploy failed"), "rows render");
                        ActivityScreen screen = Current<ActivityScreen>(host);
                        foreach (string format in new string[] { "csv", "json", "md" })
                        {
                            screen.Export(format);
                            AssertTrue(host.WaitForText("File path"), "path prompt for " + format);
                            host.Press("ctrl+s");
                            AssertTrue(host.PumpUntil(() => Directory.Exists(dir) && Directory.GetFiles(dir, "*." + format).Length == 1, 5000), format + " written");
                            host.PumpUntil(() => screen.Exporting == null);
                        }

                        string csv = File.ReadAllText(Directory.GetFiles(dir, "*.csv")[0]);
                        AssertTrue(csv.StartsWith("id,sourceType,title,status,severity,occurredUtc,actorDisplay,vesselId,missionId,voyageId,route,description", StringComparison.Ordinal), "csv header: " + csv);
                        TuiCase.Contains(csv, "\"Deploy failed, rolled back\"", "csv quoting");
                        string json = File.ReadAllText(Directory.GetFiles(dir, "*.json")[0]);
                        List<JsonPropertyShape> exportShape = JsonShape.TopLevel(json);
                        AssertTrue(exportShape.Any(p => p.Name == "totalCount" && p.ValueToken == System.Text.Json.JsonTokenType.Number && p.ScalarText == "3"), "json total (camelCase totalCount = 3)");
                        AssertTrue(exportShape.Any(p => p.Name == "entries" && p.ValueToken == System.Text.Json.JsonTokenType.StartArray), "json entries array");
                        string md = File.ReadAllText(Directory.GetFiles(dir, "*.md")[0]);
                        TuiCase.Contains(md, "# Armada History Export", "md heading");
                        TuiCase.Contains(md, "Entries: 3", "md count");
                        TuiCase.Contains(md, "- Source: deployment", "md source");
                        AssertTrue(stub.BodiesFor<Armada.Core.Models.HistoricalTimelineQuery>("POST", "/api/v1/history/enumerate").Any(q => q.PageSize == 5000), "export page size");
                    }
                }
                finally
                {
                    Environment.SetEnvironmentVariable("ARMADA_TUI_SAVE_DIR", previous);
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "delete_request_entry", "Del on a request entry confirms and deletes the request-history record", () =>
            {
                StubHttpHandler stub = Stub();
                stub.On("DELETE", "/api/v1/request-history/req_9", body => StubHttpHandler.Response(System.Net.HttpStatusCode.NoContent, ""));
                using (TuiTestHost host = TuiCase.SignedIn(170, 44, "/activity?source=history", stub))
                {
                    AssertTrue(host.WaitForText("GET /api/v1/fleets"), "rows render");
                    ActivityScreen screen = Current<ActivityScreen>(host);
                    int idx = screen.Grid.Rows.ToList().FindIndex(r => r.SourceId == "req_9");
                    screen.Grid.MoveCursor(idx);
                    AssertTrue(ActivityScreen.CanDeleteEntry(screen.Grid.Current!), "deletable");
                    host.Press("del");
                    AssertTrue(host.WaitForText("Delete this request-history entry?"), "confirm");
                    host.Press("y");
                    AssertTrue(host.PumpUntil(() => stub.CountFor("DELETE", "/api/v1/request-history/req_9") == 1), "delete sent");
                    screen.Grid.MoveCursor(0);
                    AssertFalse(ActivityScreen.CanDeleteEntry(screen.Grid.Rows.First(r => r.SourceId != "req_9")), "non-request not deletable");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI All Activity", cases: cases);
        }

        private static StubHttpHandler Stub()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("POST", "/api/v1/history/enumerate", "{\"Objects\":["
                + "{\"Id\":\"hst_1\",\"SourceType\":\"deployment\",\"SourceId\":\"dpl_1\",\"Title\":\"Deploy failed, rolled back\",\"Status\":\"Failed\",\"Severity\":\"error\",\"VesselId\":\"vsl_1\",\"ActorDisplay\":\"alice\",\"Route\":\"/deployments/dpl_1\",\"OccurredUtc\":\"2026-10-04T10:00:00Z\",\"MetadataJson\":\"{\\\"a\\\":1}\"},"
                + "{\"Id\":\"hst_2\",\"SourceType\":\"incident\",\"SourceId\":\"inc_1\",\"Title\":\"Latency spike\",\"Status\":\"Open\",\"Severity\":\"warning\",\"OccurredUtc\":\"2026-10-04T09:00:00Z\"},"
                + "{\"Id\":\"hst_3\",\"SourceType\":\"request\",\"SourceId\":\"req_9\",\"Title\":\"GET /api/v1/fleets\",\"Status\":\"200\",\"Severity\":\"info\",\"OccurredUtc\":\"2026-10-04T08:00:00Z\"}"
                + "],\"TotalRecords\":3,\"TotalPages\":1}");
            stub.Json("GET", "/api/v1/vessels", "{\"Objects\":[{\"Id\":\"vsl_1\",\"Name\":\"armada\"}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/objectives", "{\"Objects\":[{\"Id\":\"obj_1\",\"Title\":\"Ship v1\"}],\"TotalRecords\":1}");
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
