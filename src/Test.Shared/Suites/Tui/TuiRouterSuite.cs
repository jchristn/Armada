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
                router.Navigate("/jobs");
                AssertFalse(router.CanGoForward, "forward cleared");
                router.SelectTab("voyages");
                AssertEqual("/jobs", router.Current!.Path, "select tab ignored on non-hub");
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
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    host.Tui.Context.Navigate("/inbox");
                    host.Press("alt+left");
                    AssertEqual("/jobs", host.Tui.Context.Router.Current!.Path, "alt+left");
                    host.Press("alt+right");
                    AssertEqual("/inbox", host.Tui.Context.Router.Current!.Path, "alt+right");
                    host.Press("backspace");
                    AssertEqual("/jobs", host.Tui.Context.Router.Current!.Path, "backspace");
                    AssertEqual("/jobs", host.Tui.Context.Prefs.Current.LastRoute, "last route persisted");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI router", cases: cases);
        }
    }
}
