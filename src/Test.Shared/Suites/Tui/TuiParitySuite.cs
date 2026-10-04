namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Armada.Client;
    using Armada.Tui.Routing;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Parity enforcement (TUI_APP_PLAN.md): every dashboard route, hub tab, API client function, WebSocket event, and
    /// Server settings field has a manifest entry; implemented entries point at something that exists; every client.ts
    /// export has an ArmadaClient method; every dashboard route resolves in the TUI router. (Release builds will also
    /// fail on "planned" entries once screens land; not enforced yet.)
    /// </summary>
    public sealed class TuiParitySuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Parity";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "routes_have_entries", "Every App.tsx route has a manifest entry and resolves in the router", () =>
            {
                ParityManifest manifest = Load();
                List<string> routes = DashboardSource.Routes();
                AssertTrue(routes.Count >= 80, "parsed routes: " + routes.Count);
                Missing(manifest, "route", routes);
                foreach (string route in routes)
                {
                    string sample = String.Join("/", route.Split('/').Select(s => s.StartsWith(":") ? "x_1" : s));
                    RouteMatch m = Router.Resolve(sample.Length == 0 ? "/" : sample);
                    AssertFalse(ReferenceEquals(RouteTable.NotFound, m.Route), "router resolves " + route);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "tabs_have_entries", "Every hub tab has a manifest entry and a TUI tab", () =>
            {
                ParityManifest manifest = Load();
                List<string> tabs = DashboardSource.Tabs();
                AssertTrue(tabs.Count >= 37, "parsed tabs: " + tabs.Count);
                Missing(manifest, "tab", tabs);
                foreach (string tab in tabs)
                {
                    string page = tab.Split(':')[0];
                    string key = tab.Split(':')[1];
                    RouteMatch m = Router.Resolve(DashboardSource.HubPages[page] + key);
                    AssertEqual(key, m.Tab?.Key, "TUI hub tab for " + tab);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "api_exports_have_client_methods", "Every client.ts server-calling export has a manifest entry and an ArmadaClient method", () =>
            {
                ParityManifest manifest = Load();
                List<string> exports = DashboardSource.ApiExports();
                AssertEqual(317, exports.Count, "server-calling exports");
                Missing(manifest, "api", exports);
                HashSet<string> methods = new HashSet<string>(typeof(ArmadaClient).GetMethods(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name), StringComparer.Ordinal);
                List<string> missing = exports.Where(e => !methods.Contains(Char.ToUpperInvariant(e[0]) + e.Substring(1) + "Async")).ToList();
                if (missing.Count > 0) throw new AssertionException("client.ts exports without an ArmadaClient method: " + String.Join(", ", missing));
            }));

            cases.Add(TuiCase.Sync(Suite, "events_have_entries", "Every WebSocket event the dashboard handles has a manifest entry and a typed payload", () =>
            {
                ParityManifest manifest = Load();
                List<string> events = DashboardSource.Events();
                AssertTrue(events.Count >= 28, "parsed events: " + events.Count);
                Missing(manifest, "event", events);
                foreach (string ev in events) AssertNotNull(Armada.Client.Socket.ArmadaEventTypes.PayloadTypeFor(ev), "payload type for " + ev);
            }));

            cases.Add(TuiCase.Sync(Suite, "settings_have_entries", "Every Server page settings field has a manifest entry", () =>
            {
                ParityManifest manifest = Load();
                List<string> fields = DashboardSource.SettingsFields();
                AssertTrue(fields.Count >= 30, "parsed settings fields: " + fields.Count);
                Missing(manifest, "setting", fields);
            }));

            cases.Add(TuiCase.Sync(Suite, "implemented_entries_exist", "Implemented and extension entries point at code that exists", () =>
            {
                ParityManifest manifest = Load();
                HashSet<string> statuses = new HashSet<string>(manifest.Statuses);
                Assembly tui = typeof(Armada.Tui.ArmadaTuiApp).Assembly;
                foreach (ParityEntry e in manifest.Entries)
                {
                    AssertTrue(statuses.Contains(e.Status), "status " + e.Status + " for " + e.Key);
                    if (e.Status != "implemented" && e.Status != "extension") continue;
                    if (e.Tui.StartsWith("ArmadaClient.", StringComparison.Ordinal))
                    {
                        string method = e.Tui.Substring("ArmadaClient.".Length);
                        AssertTrue(typeof(ArmadaClient).GetMethod(method, BindingFlags.Public | BindingFlags.Instance) != null || typeof(ArmadaClient).GetMethods().Any(m => m.Name == method), e.Tui);
                    }
                    else if (e.Tui == "Router")
                    {
                        AssertTrue(Router.Resolve(e.Key).Route.RedirectTo == null, "redirect resolved: " + e.Key);
                    }
                    else if (e.Kind == "route" && e.Status == "extension")
                    {
                        AssertNotNull(RouteTable.Find(e.Key), "extension route " + e.Key);
                    }
                    else if (e.Tui.Length > 0)
                    {
                        AssertTrue(tui.GetTypes().Any(t => t.Name == e.Tui), "TUI type " + e.Tui + " for " + e.Key);
                    }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "manifest_embedded", "The manifest is embedded in Armada.Tui", () =>
            {
                using (Stream? s = typeof(Armada.Tui.ArmadaTuiApp).Assembly.GetManifestResourceStream("Armada.Tui.parity.json"))
                {
                    AssertNotNull(s, "embedded resource");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI parity manifest", cases: cases);
        }

        private static ParityManifest Load()
        {
            string path = Path.Combine(DashboardSource.RepoRoot(), "src", "Armada.Tui", "parity.json");
            ParityManifest? manifest = ArmadaJson.Deserialize<ParityManifest>(File.ReadAllText(path));
            AssertNotNull(manifest, "manifest");
            return manifest!;
        }

        private static void Missing(ParityManifest manifest, string kind, IEnumerable<string> keys)
        {
            HashSet<string> present = new HashSet<string>(manifest.Entries.Where(e => e.Kind == kind).Select(e => e.Key), StringComparer.Ordinal);
            List<string> missing = keys.Where(k => !present.Contains(k)).Distinct().ToList();
            if (missing.Count > 0)
                throw new AssertionException("Dashboard " + kind + " surfaces with no parity.json entry (run scripts/tui/generate-parity-manifest.py): " + String.Join(", ", missing));
        }
    }
}
