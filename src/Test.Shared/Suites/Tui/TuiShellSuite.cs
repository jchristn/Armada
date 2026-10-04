namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Shell;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Shell rendering and layout at the plan's sizes (80x24, 120x40, 200x60), breakpoints, panes, and the
    /// terminal-too-small screen.
    /// </summary>
    public sealed class TuiShellSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Shell";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (int[] size in new[] { new[] { 80, 24 }, new[] { 120, 40 }, new[] { 200, 60 } })
            {
                int w = size[0];
                int h = size[1];
                cases.Add(TuiCase.Sync(Suite, "renders_" + w + "x" + h, "Shell renders at " + w + "x" + h, () =>
                {
                    using (TuiTestHost host = TuiCase.SignedIn(w, h, "/missions"))
                    {
                        string frame = host.Screen();
                        string[] lines = frame.Split('\n');
                        AssertTrue(lines.Length >= h, "row count");
                        TuiCase.Contains(frame, "Armada", "header");
                        TuiCase.Contains(frame, "admin@armada", "user");
                        TuiCase.Contains(frame, "File", "menu bar");
                        TuiCase.Contains(frame, "Help", "menu bar help");
                        TuiCase.Contains(frame, "Missions", "hub tab");
                        TuiCase.Contains(frame, "Merge Queue", "hub tab");
                        TuiCase.Contains(frame, "Coming in a later milestone", "placeholder");
                        TuiCase.Contains(frame, "MissionsScreen", "screen name");
                        TuiCase.Contains(frame, "Ctrl+K", "status hints");
                        ShellLayout layout = host.Tui.Shell.LastLayout!;
                        LayoutModeEnum expected = w >= 110 ? LayoutModeEnum.Wide : w >= 90 ? LayoutModeEnum.Compact : LayoutModeEnum.Narrow;
                        AssertEqual(expected, layout.Mode, "mode");
                        if (expected == LayoutModeEnum.Wide)
                        {
                            TuiCase.Contains(frame, "OPERATIONS", "sidebar section");
                            TuiCase.Contains(frame, "Needs You", "sidebar item");
                            TuiCase.Contains(frame, "CONFIGURATION", "sidebar section");
                        }
                        else
                        {
                            TuiCase.NotContains(frame, "OPERATIONS", "sidebar hidden");
                        }
                    }
                }));
            }

            cases.Add(TuiCase.Sync(Suite, "too_small", "Below 80x24 shows the terminal-too-small screen", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(70, 20))
                {
                    string frame = host.Screen();
                    AssertEqual(LayoutModeEnum.TooSmall, host.Tui.Shell.LastLayout!.Mode, "mode");
                    TuiCase.NotContains(frame, "Merge Queue", "content hidden");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "resize_reflows", "Resizing switches breakpoints without restart", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40))
                {
                    host.Screen();
                    AssertEqual(LayoutModeEnum.Wide, host.Tui.Shell.LastLayout!.Mode, "wide");
                    host.Resize(100, 30);
                    host.Screen();
                    AssertEqual(LayoutModeEnum.Compact, host.Tui.Shell.LastLayout!.Mode, "compact");
                    AssertTrue(host.Tui.Shell.LastLayout!.CompactSidebar, "icon sidebar");
                    host.Resize(85, 30);
                    string frame = host.Screen();
                    AssertEqual(LayoutModeEnum.Narrow, host.Tui.Shell.LastLayout!.Mode, "narrow");
                    host.Press("ctrl+b");
                    frame = host.Screen();
                    TuiCase.Contains(frame, "OPERATIONS", "ctrl+b opens the sidebar when narrow");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sidebar_toggle_and_dock", "Ctrl+B hides the sidebar and Ctrl+J shows the Ask dock", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40))
                {
                    TuiCase.Contains(host.Screen(), "OPERATIONS", "sidebar shown");
                    host.Press("ctrl+b");
                    TuiCase.NotContains(host.Screen(), "OPERATIONS", "sidebar hidden");
                    host.Press("ctrl+j");
                    TuiCase.Contains(host.Screen(), "W2.7", "dock placeholder");
                    AssertTrue(host.Tui.Context.Prefs.Current.AskDockVisible, "dock persisted");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "focus_router_tab_cycles_panes", "Tab cycles sidebar, main, and back; the sidebar opens destinations", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    ShellView shell = host.Tui.Shell;
                    AssertTrue(ReferenceEquals(shell.Scope.Focused, shell.Screen), "main focused");
                    host.Press("tab");
                    AssertTrue(ReferenceEquals(shell.Scope.Focused, shell.Sidebar), "sidebar focused after tab");
                    host.Press("home");
                    host.Press("down");
                    host.Press("enter");
                    AssertEqual("/ask", host.Tui.Context.Router.Current!.Path, "sidebar opened Ask Armada");
                    AssertTrue(ReferenceEquals(shell.Scope.Focused, shell.Screen), "main focused after navigation");
                    host.Press("f6");
                    AssertTrue(ReferenceEquals(shell.Scope.Focused, shell.Sidebar), "f6 moves to the next pane (wraps to the sidebar)");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "menu_bar_opens_and_runs", "F10 opens the menu bar; Go menu runs a navigation", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40))
                {
                    host.Press("f10");
                    AssertTrue(host.Tui.Shell.Menu.IsOpen, "open");
                    host.Press("right");
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Needs You", "go menu items");
                    TuiCase.Contains(frame, "g i", "go menu key");
                    host.Press("down");
                    host.Press("down");
                    host.Press("enter");
                    AssertFalse(host.Tui.Shell.Menu.IsOpen, "closed");
                    AssertEqual("/inbox", host.Tui.Context.Router.Current!.Path, "navigated");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "go_to_sequences", "g-letter sequences navigate", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/jobs"))
                {
                    host.Press("g").Press("v");
                    AssertEqual("/vessels", host.Tui.Context.Router.Current!.Path, "g v");
                    host.Press("g").Press("s");
                    AssertEqual("/server", host.Tui.Context.Router.Current!.Path, "g s");
                    host.Press("g").Press("h");
                    AssertEqual("/", host.Tui.Context.Router.Current!.Path, "g h");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "hub_tabs_switch", "Hub tabs switch with ] and keep the tab in the route", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.Press("]");
                    AssertEqual("/missions?tab=voyages", host.Tui.Context.Router.Current!.FullPath, "voyages tab");
                    TuiCase.Contains(host.Screen(), "VoyagesScreen", "voyages placeholder");
                    host.Press("alt+3");
                    AssertEqual("/missions?tab=merge-queue", host.Tui.Context.Router.Current!.FullPath, "alt+3");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI shell and layout", cases: cases);
        }
    }
}
