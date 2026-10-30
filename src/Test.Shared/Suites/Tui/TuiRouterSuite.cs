namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Routing;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Router: dashboard redirects, parameters, deep-link queries, hub tabs, not found, and back/forward history.
    /// </summary>
    public sealed class TuiRouterSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Router";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "redirects", "Dashboard redirects resolve like App.tsx", () =>
            {
                AssertEqual("/missions?tab=voyages", Router.Resolve("/voyages").FullPath, "voyages");
                AssertEqual("/activity?source=signals", Router.Resolve("/signals").FullPath, "signals");
                AssertEqual("/server?tab=diagnostics", Router.Resolve("/doctor").FullPath, "doctor");
                AssertEqual("/", Router.Resolve("/dashboard").FullPath, "dashboard");
                AssertEqual("/inbox", Router.Resolve("/notifications").FullPath, "notifications");
                AssertEqual("/server", Router.Resolve("/settings").FullPath, "settings");
            }));

            cases.Add(TuiCase.Sync(Suite, "params_and_specificity", "Parameters bind and literal segments win", () =>
            {
                RouteMatch m = Router.Resolve("/missions/msn_abc");
                AssertEqual("/missions/:id", m.Route.Pattern, "pattern");
                AssertEqual("msn_abc", m.Param("id"), "id");
                AssertEqual("/voyages/create", Router.Resolve("/voyages/create").Route.Pattern, "create beats :id");
                AssertEqual("/vessels/import", Router.Resolve("/vessels/import").Route.Pattern, "import beats :id");
                AssertEqual("/ask/:threadId?", Router.Resolve("/ask").Route.Pattern, "optional param absent");
                AssertEqual("ath_1", Router.Resolve("/ask/ath_1").Param("threadId"), "optional param present");
                AssertEqual("/workspace/:vesselId/:panel", Router.Resolve("/workspace/vsl_1/terminal").Route.Pattern, "two params");
            }));

            cases.Add(TuiCase.Sync(Suite, "deep_link_query", "Deep links keep their query and pick hub tabs", () =>
            {
                RouteMatch m = Router.Resolve("/vessels/health?overall=Fail");
                AssertEqual("health", m.Tab?.Key, "health tab");
                AssertEqual("Fail", m.Query["overall"], "filter kept");
                AssertEqual("merge-queue", Router.Resolve("/missions?tab=merge-queue").Tab?.Key, "tab query");
                AssertEqual("missions", Router.Resolve("/missions?tab=bogus").Tab?.Key, "unknown tab falls back");
            }));

            cases.Add(TuiCase.Sync(Suite, "not_found", "Unknown paths resolve to the not-found route", () =>
            {
                AssertTrue(ReferenceEquals(RouteTable.NotFound, Router.Resolve("/nope/at/all").Route), "not found");
            }, TestTags.Negative));

            cases.Add(TuiCase.Sync(Suite, "back_forward", "Back and forward walk history; navigating clears forward", () =>
            {
                Router router = new Router();
                router.Navigate("/");
                router.Navigate("/missions");
                router.Navigate("/vessels");
                AssertTrue(router.Back(), "back");
                AssertEqual("/missions", router.Current!.Path, "after back");
                AssertTrue(router.Back(), "back 2");
                AssertEqual("/", router.Current!.Path, "home");
                AssertFalse(router.Back(), "no more back");
                AssertTrue(router.Forward(), "forward");
                AssertEqual("/missions", router.Current!.Path, "after forward");
                router.Navigate("/inbox");
                AssertFalse(router.CanGoForward, "forward cleared");
                router.SelectTab("voyages");
                AssertEqual("/inbox", router.Current!.Path, "select tab ignored on non-hub");
            }));

            cases.Add(TuiCase.Sync(Suite, "tab_select_replaces", "Selecting a hub tab replaces the history entry", () =>
            {
                Router router = new Router();
                router.Navigate("/");
                router.Navigate("/missions");
                router.SelectTab("voyages");
                router.SelectTab("merge-queue");
                AssertEqual("/missions?tab=merge-queue", router.Current!.FullPath, "tab");
                router.Back();
                AssertEqual("/", router.Current!.Path, "tab flips did not flood history");
                router.Navigate("/vessels/health");
                router.SelectTab("fleets");
                AssertEqual("/vessels?tab=fleets", router.Current!.FullPath, "fixed-tab route switches to the hub");
            }));

            cases.Add(TuiCase.Sync(Suite, "consolidated_nav_redirects", "Pages that became hub tabs redirect there: /jobs, /storage, /cli-permissions (tab becomes section), /planning and /planning/:id (id becomes session)", () =>
            {
                RouteMatch jobs = Router.Resolve("/jobs");
                AssertEqual("/activity", jobs.Path, "jobs path");
                AssertEqual("jobs", jobs.Tab?.Key, "jobs tab");
                AssertEqual("JobsScreen", jobs.Tab?.ScreenName, "jobs screen");

                RouteMatch storage = Router.Resolve("/storage");
                AssertEqual("/server", storage.Path, "storage path");
                AssertEqual("storage", storage.Tab?.Key, "storage tab");

                RouteMatch cli = Router.Resolve("/cli-permissions");
                AssertEqual("/server", cli.Path, "cli path");
                AssertEqual("cli-permissions", cli.Tab?.Key, "cli tab");
                AssertEqual("CliPermissionsScreen", cli.Tab?.ScreenName, "cli screen");
                AssertFalse(cli.Query.ContainsKey("section"), "no section by default");

                RouteMatch rules = Router.Resolve("/cli-permissions?tab=rules&request=cpr_1");
                AssertEqual("/server?request=cpr_1&section=rules&tab=cli-permissions", rules.FullPath, "old tab carried as section, request kept");
                AssertEqual("cli-permissions", rules.Tab?.Key, "rules lands on the cli tab");

                RouteMatch request = Router.Resolve("/cli-permissions?request=cpr_9");
                AssertEqual("/server?request=cpr_9&tab=cli-permissions", request.FullPath, "request deep link");

                RouteMatch planning = Router.Resolve("/planning");
                AssertEqual("/dispatch?tab=planning", planning.FullPath, "planning");
                AssertEqual("PlanningScreen", planning.Tab?.ScreenName, "planning screen");

                RouteMatch session = Router.Resolve("/planning/ps_1?from=workspace&vesselId=vsl_1");
                AssertEqual("/dispatch", session.Path, "session path");
                AssertEqual("planning", session.Tab?.Key, "session tab");
                AssertEqual("ps_1", session.Query["session"], "session id preserved");
                AssertEqual("workspace", session.Query["from"], "from kept");
                AssertEqual("vsl_1", session.Query["vesselId"], "vesselId kept");

                RouteMatch escaped = Router.Resolve("/planning/ps%20a");
                AssertEqual("ps a", escaped.Query["session"], "session id unescaped once");
                AssertEqual("/dispatch?session=ps%20a&tab=planning", escaped.FullPath, "session id re-escaped");

                RouteMatch setup = Router.Resolve("/planning?fromSetupWizard=true&vesselId=vsl_2&fleetId=flt_1");
                AssertEqual("/dispatch?fleetId=flt_1&fromSetupWizard=true&tab=planning&vesselId=vsl_2", setup.FullPath, "setup wizard handoff kept");
            }));

            cases.Add(TuiCase.Sync(Suite, "nav_catalog_matches_dashboard", "The sidebar sections, items, labels, and tooltips match the dashboard's lib/navModel.ts", () =>
            {
                string src = System.IO.File.ReadAllText(System.IO.Path.Combine(DashboardSource.Src(), "lib", "navModel.ts"));
                int start = src.IndexOf("export const navModelSections", StringComparison.Ordinal);
                int end = src.IndexOf("];", start, StringComparison.Ordinal);
                AssertTrue(start >= 0 && end > start, "navModelSections in navModel.ts");
                string block = src.Substring(start, end - start);
                List<string> dashboard = new List<string>();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(block, @"label: '([A-Z]+)',|\{ to: '([^']+)', label: '([^']+)', tooltip: '([^']+)'"))
                {
                    if (m.Groups[1].Success) dashboard.Add("section " + m.Groups[1].Value);
                    else dashboard.Add(m.Groups[2].Value + " | " + m.Groups[3].Value + " | " + m.Groups[4].Value);
                }

                List<string> tui = new List<string>();
                foreach (NavSection section in NavCatalog.Sections)
                {
                    tui.Add("section " + section.Label);
                    foreach (NavItem item in section.Items) tui.Add(item.To + " | " + item.Label + " | " + item.Tooltip);
                }

                AssertEqual(String.Join("\n", dashboard), String.Join("\n", tui), "sidebar");
                AssertFalse(NavCatalog.Sections.Any(sec => sec.Label == "ACTIVITY" || sec.Label == "DELIVERY"), "ACTIVITY and DELIVERY sections removed");
                List<string> ops = NavCatalog.Sections.First(sec => sec.Label == "OPERATIONS").Items.Select(i => i.To).ToList();
                AssertEqual(ops.IndexOf("/missions") + 1, ops.IndexOf("/activity"), "Activity directly under Missions");
                AssertTrue(NavCatalog.Sections.First(sec => sec.Label == "BUILD").Items.Any(i => i.To == "/delivery"), "Delivery in BUILD");
                foreach (string gone in new[] { "/jobs", "/planning", "/storage", "/cli-permissions", "/blackbox" })
                    AssertFalse(NavCatalog.AllItems().Any(i => i.To == gone), gone + " is no sidebar item");
                AssertEqual("/activity", NavCatalog.ItemForPath("/events/evt_1")?.To, "activity owns its detail pages");
                AssertEqual("/delivery", NavCatalog.ItemForPath("/deployments/dpl_1")?.To, "delivery owns its detail pages");
                AssertEqual("/server", NavCatalog.ItemForPath("/storage")?.To, "settings owns storage");
                AssertEqual("/missions", NavCatalog.ItemForPath("/blackbox/bbx_1")?.To, "missions owns black box entries");
            }));

            cases.Add(TuiCase.Sync(Suite, "nav_shortcuts_keep_go_keys", "Planning, Jobs, Black Box, Storage, and CLI Tool Permissions keep Go commands that open their new tabs", () =>
            {
                Dictionary<string, string> expected = new Dictionary<string, string>
                {
                    ["go.planning"] = "/dispatch?tab=planning", ["go.jobs"] = "/activity?source=jobs",
                    ["go.storage"] = "/server?tab=storage", ["go.clipermissions"] = "/server?tab=cli-permissions",
                    ["go.blackbox"] = "/missions?tab=blackbox",
                };
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/inbox"))
                {
                    foreach (KeyValuePair<string, string> kvp in expected)
                    {
                        Armada.Tui.Input.ArmadaCommand? go = host.Tui.Context.Commands.All().FirstOrDefault(c => c.Id == kvp.Key);
                        AssertNotNull(go, kvp.Key);
                        go!.Handler();
                        AssertTrue(host.PumpUntil(() => host.Tui.Context.Router.Current!.FullPath == kvp.Value), kvp.Key + " opens " + kvp.Value + " (now " + host.Tui.Context.Router.Current!.FullPath + ")");
                    }

                    AssertTrue(host.Tui.Context.Commands.All().First(c => c.Id == "go.jobs").Gestures.Count > 0, "jobs keeps its g-letter key");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "consolidated_tab_links", "TabLinks builds the new tab URLs and they open the right tab", () =>
            {
                AssertEqual("/dispatch?tab=planning", TabLinks.Planning(), "planning");
                AssertEqual("/dispatch?session=ps_1&tab=planning", TabLinks.Planning("ps_1"), "session");
                AssertEqual("/dispatch?from=workspace&session=ps_1&tab=planning", TabLinks.Planning("ps_1", new Dictionary<string, string> { ["from"] = "workspace", ["tab"] = "backlog" }), "extra query cannot override the tab");
                AssertEqual("/server?tab=cli-permissions", TabLinks.CliPermissions(), "cli");
                AssertEqual("/server?request=cpr_1&section=rules&tab=cli-permissions", TabLinks.CliPermissions("cpr_1", "rules"), "cli section and request");
                AssertEqual("jobs", Router.Resolve(TabLinks.JobsPath).Tab?.Key, "jobs link");
                AssertEqual("storage", Router.Resolve(TabLinks.StoragePath).Tab?.Key, "storage link");
                AssertEqual("blackbox", Router.Resolve(TabLinks.BlackBoxPath).Tab?.Key, "black box link");
                AssertEqual("/missions?fingerprint=fp%201&tab=blackbox", TabLinks.BlackBox("fp 1"), "black box fingerprint link");
                AssertEqual("/missions?bbtab=fingerprints&tab=blackbox&vesselId=vsl_1", Router.Resolve("/blackbox?tab=fingerprints&vesselId=vsl_1").FullPath, "/blackbox keeps its filters; its old tab becomes bbtab");
                AssertEqual("planning", Router.Resolve(TabLinks.Planning("ps_1")).Tab?.Key, "planning link");
                AssertEqual("cli-permissions", Router.Resolve(TabLinks.CliPermissions("cpr_1")).Tab?.Key, "cli link");
                AssertEqual("/dispatch?tab=planning&session=", Armada.Tui.Modals.CommandPaletteModal.EntityRoutes["psn_"], "palette psn_ jump");
                AssertEqual("ps_7", Router.Resolve(Armada.Tui.Modals.CommandPaletteModal.EntityRoutes["psn_"] + "ps_7").Query["session"], "palette psn_ jump opens the session");
            }));

            cases.Add(TuiCase.Sync(Suite, "every_route_resolves", "Every route pattern in the table resolves to itself or its redirect", () =>
            {
                foreach (RouteDefinition route in RouteTable.All)
                {
                    string path = String.Join("/", route.Segments.Select(s => s.StartsWith(":") ? "x_1" : s));
                    RouteMatch m = Router.Resolve("/" + path);
                    AssertFalse(ReferenceEquals(RouteTable.NotFound, m.Route), route.Pattern);
                    AssertTrue(m.Route.RedirectTo == null, route.Pattern + " fully redirected");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "shell_back_forward_keys", "Alt+Left/Alt+Right and Backspace navigate history in the shell", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/approvals"))
                {
                    host.Tui.Context.Navigate("/inbox");
                    host.Press("alt+left");
                    AssertEqual("/approvals", host.Tui.Context.Router.Current!.Path, "alt+left");
                    host.Press("alt+right");
                    AssertEqual("/inbox", host.Tui.Context.Router.Current!.Path, "alt+right");
                    host.Press("backspace");
                    AssertEqual("/approvals", host.Tui.Context.Router.Current!.Path, "backspace");
                    AssertEqual("/approvals", host.Tui.Context.Prefs.Current.LastRoute, "last route persisted");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI router", cases: cases);
        }
    }
}
