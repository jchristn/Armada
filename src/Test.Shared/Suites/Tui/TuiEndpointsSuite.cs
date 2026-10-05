namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Configuration;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Endpoints tab (W6.8) against a stubbed server: KPIs, local filters, sort, paging, row menu, the form with
    /// provider rules and the write-only key, health detail with history, Validate Now, health sweep, and delete.
    /// </summary>
    public sealed class TuiEndpointsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Endpoints";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "list_opens_with_kpis", "Endpoints tab shows KPIs, health, and rows", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=endpoints"))
                {
                    EndpointsScreen screen = TuiEntityFixtures.Screen<EndpointsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Kpis.Items.Count == 4, "rows and KPIs");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Total Endpoints 2", "Healthy 1", "Unhealthy 1", "openai-chat", "Inference", "OpenAI", "+ Healthy", "(disabled)", "Never");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "filter_sort_page", "Kind filter, search, sort, and local paging", () =>
            {
                StubHttpHandler stub = TuiEntityFixtures.Server();
                List<string> many = Enumerable.Range(1, 30).Select(i => Endpoint("mep_" + i.ToString("00"), "ep-" + i.ToString("00"), i % 2 == 0 ? "Embedding" : "Inference", "Healthy", true)).ToList();
                stub.Json("GET", "/api/v1/model-endpoints", "[" + String.Join(",", many) + "]");
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=endpoints"))
                {
                    EndpointsScreen screen = TuiEntityFixtures.Screen<EndpointsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 25, "first page");
                    host.Press(">");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.PageNumber == 2 && screen.Grid.Rows.Count == 5, "second page");
                    screen.Grid.SortBy("name", true);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count > 0 && screen.Grid.Rows[0].Name == "ep-30", "sorted");
                    SelectField<string> kind = (SelectField<string>)screen.Filters.Field("kind")!;
                    kind.Choose(kind.Options.First(o => o.Value == "Embedding"));
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.TotalRecords == 15, "kind filter");
                    host.Press("/");
                    host.Type("ep-12");
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.TotalRecords == 1, "search");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "row_menu", "The row menu lists Health, Validate, Edit, View JSON, and Delete", () =>
            {
                StubHttpHandler stub = Server();
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=endpoints"))
                {
                    EndpointsScreen screen = TuiEntityFixtures.Screen<EndpointsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press(".");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Health", "Validate", "Edit", "View JSON", "Delete");
                    host.Press("esc");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "form_rules_and_submit", "Unsupported provider and kind keep the form open; blank key is not sent", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "PUT", "/api/v1/model-endpoints/mep_1", Endpoint("mep_1", "openai-chat", "Inference", "Healthy", true), box);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=endpoints"))
                {
                    EndpointsScreen screen = TuiEntityFixtures.Screen<EndpointsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("e");
                    FormDialog dialog = TuiConfigTestHelpers.Dialog(host, "Edit Endpoint");
                    TuiCase.Contains(host.Screen(), "(unchanged - leave blank to keep stored key)", "blank keeps stored");
                    SelectField<string> kind = TuiConfigTestHelpers.Field<SelectField<string>>(dialog, "Kind");
                    SelectField<string> provider = TuiConfigTestHelpers.Field<SelectField<string>>(dialog, "Provider");
                    kind.Choose(kind.Options.First(o => o.Value == "Embedding"));
                    provider.Choose(provider.Options.First(o => o.Value == "Anthropic"));
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => host.Screen().Contains("Anthropic does not provide an embeddings API"), "provider rule");
                    AssertTrue(host.App.Modals.IsActive && box.Count == 0, "still open");
                    kind.Choose(kind.Options.First(o => o.Value == "Inference"));
                    TuiConfigTestHelpers.Input(dialog, "Name").Value = "";
                    host.Press("ctrl+s");
                    TuiCase.Contains(host.Screen(), "This field is required.", "required name");
                    TuiConfigTestHelpers.Input(dialog, "Name").Value = "claude";
                    host.Press("ctrl+s");
                    TuiEntityFixtures.WaitFor(host, () => box.Body != null && !host.App.Modals.IsActive, "saved");
                    AssertEqual(Armada.Core.Enums.ModelProviderEnum.Anthropic, box.As<Armada.Core.Models.ModelEndpoint>().Provider, "provider: " + box.Body);
                    AssertNull(JsonShape.TopLevelProperty(box.Body!, "apiKey"), "blank key not sent (any casing): " + box.Body);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "health_and_validate", "Health shows uptime and history; v validates and shows the probe", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody box = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "POST", "/api/v1/model-endpoints/mep_1/validate", "{\"Success\":true,\"LatencyMs\":42,\"StatusCode\":200,\"SampleText\":\"pong\",\"TimestampUtc\":\"2026-10-04T00:00:00Z\"}", box);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=endpoints"))
                {
                    EndpointsScreen screen = TuiEntityFixtures.Screen<EndpointsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("h");
                    TuiConfigTestHelpers.Contains(host.Screen(), "Health: openai-chat", "Uptime", "50.00%", "Consecutive OK", "Health History", "v Validate Now");
                    host.Press("v");
                    TuiEntityFixtures.WaitFor(host, () => box.Count == 1 && host.Screen().Contains("Validation Result"), "probe shown");
                    TuiConfigTestHelpers.Contains(host.Screen(), "42 ms", "pong");
                    host.Press("esc");
                    List<HealthBucket> buckets = EndpointHealthHistogram.Buckets(new List<ModelEndpointHealthRecord>
                    {
                        new ModelEndpointHealthRecord { TimestampUtc = DateTime.UtcNow.AddMinutes(-3), Success = true },
                        new ModelEndpointHealthRecord { TimestampUtc = DateTime.UtcNow.AddMinutes(-2), Success = false }
                    }, DateTime.UtcNow, 20);
                    AssertEqual("+x", new string(buckets.Select(b => b.Marker).ToArray()), "per-probe buckets under an hour");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "health_sweep_and_delete", "H runs the sweep; Del confirms and deletes", () =>
            {
                StubHttpHandler stub = Server();
                TuiConfigBody sweep = new TuiConfigBody();
                TuiConfigBody delete = new TuiConfigBody();
                TuiConfigTestHelpers.Capture(stub, "POST", "/api/v1/model-endpoints/health-check", "{\"DistinctBaseUrlsProbed\":2}", sweep);
                TuiConfigTestHelpers.Capture(stub, "DELETE", "/api/v1/model-endpoints/mep_1", "{}", delete);
                using (TuiTestHost host = TuiEntityFixtures.Open(stub, "/configuration?tab=endpoints"))
                {
                    EndpointsScreen screen = TuiEntityFixtures.Screen<EndpointsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2, "rows");
                    host.Press("H");
                    TuiEntityFixtures.WaitFor(host, () => sweep.Count == 1 && !screen.Sweeping, "sweep");
                    host.Press("del");
                    TuiCase.Contains(host.Screen(), "Delete Endpoint", "confirm");
                    AssertEqual(0, delete.Count, "not yet");
                    host.Press("y");
                    TuiEntityFixtures.WaitFor(host, () => delete.Count == 1, "deleted");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI endpoints", cases: cases);
        }

        internal static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiEntityFixtures.Server();
            stub.Json("GET", "/api/v1/model-endpoints", "[" + Endpoint("mep_1", "openai-chat", "Inference", "Healthy", true) + "," + Endpoint("mep_2", "embedder", "Embedding", "Unhealthy", false) + "]");
            return stub;
        }

        private static string Endpoint(string id, string name, string kind, string health, bool enabled)
        {
            string now = DateTime.UtcNow.AddMinutes(-10).ToString("o");
            string history = health == "Healthy"
                ? "[{\"TimestampUtc\":\"" + now + "\",\"Success\":true},{\"TimestampUtc\":\"" + DateTime.UtcNow.AddMinutes(-5).ToString("o") + "\",\"Success\":false}]"
                : "[]";
            return "{\"Id\":\"" + id + "\",\"TenantId\":\"ten_default\",\"Name\":\"" + name + "\",\"Kind\":\"" + kind + "\",\"Provider\":\"OpenAI\",\"BaseUrl\":\"https://api.openai.com\",\"Model\":\"gpt-4o-mini\",\"Scope\":\"TenantWide\","
                + "\"Enabled\":" + (enabled ? "true" : "false") + ",\"HasApiKey\":true,\"HealthStatus\":\"" + health + "\",\"HealthHistory\":" + history + ",\"UptimePercentage\":75,\"ConsecutiveSuccesses\":3,\"ConsecutiveFailures\":0,"
                + (health == "Healthy" ? "\"LastHealthCheckUtc\":\"" + now + "\",\"FirstHealthCheckUtc\":\"" + now + "\"," : "")
                + "\"Dimensionality\":0,\"TimeoutMs\":120000,\"CreatedUtc\":\"2026-10-01T00:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T00:00:00Z\"}";
        }
    }
}
