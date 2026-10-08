namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using Armada.Core.Enums;
    using Armada.Core.Metrics.Charts;
    using Armada.Tui.Screens.Activity;
    using Armada.Tui.Screens.Configuration;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui.ActivitySystem;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The Harbors screen's Activity panel for the selected Harbor (jobs over time, slot sparkline, link strip, launch
    /// speed, token totals) at 120x40 and 80x24 in Unicode and ASCII icon mode, the range key, the Token Usage link with its
    /// Harbor filter, focus-aware hints, errors, the focus treatment and TUIKit's FocusAudit, and the strip's glyphs
    /// (never color alone).
    /// </summary>
    public sealed class TuiHarborMetricsSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.HarborMetrics";
        private const string Route = "/configuration?tab=harbors";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            foreach (int[] size in new int[][] { new int[] { 120, 40 }, new int[] { 80, 24 } })
            {
                int width = size[0];
                int height = size[1];
                foreach (bool ascii in new bool[] { false, true })
                {
                    string mode = ascii ? "ascii" : "unicode";
                    cases.Add(TuiCase.Sync(Suite, "renders_" + width + "x" + height + "_" + mode, "Activity panel with metrics at " + width + "x" + height + " (" + mode + ")", () =>
                    {
                        using (TuiTestHost host = TuiCase.SignedIn(width, height, Route, TuiHarborsSuite.Server()))
                        {
                            if (ascii)
                            {
                                host.Tui.Context.Commands.Execute("view.icons.ascii");
                                host.Pump();
                            }

                            HarborsScreen screen = Loaded(host);
                            AssertEqual("hbr_1", screen.Activity.Metrics!.HarborId, "the selected Harbor's metrics");
                            AssertEqual(4, screen.Activity.JobsChart.Series.Count, "four job series");
                            AssertEqual("Missions finished", screen.Activity.JobsChart.Series[0].Name);
                            AssertEqual(48, screen.Activity.JobsChart.Labels.Count, "a label per bucket");
                            string frame = host.Screen();
                            TuiScreenDump.Write("harbor-metrics-" + width + "x" + height + "-" + mode, frame);
                            TuiCase.Contains(frame, "Activity", "panel box title");
                            TuiCase.Contains(frame, "build-box - Last 24 hours", "panel header");
                            if (ascii)
                            {
                                char bad = frame.FirstOrDefault(c => c > 0x7F);
                                AssertTrue(bad == default(char), "ASCII frame (found U+" + ((int)bad).ToString("X4") + ")");
                            }

                            // Scroll through the panel: every section is reachable at every size.
                            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                            Focus(host, screen);
                            for (int i = 0; i < 40; i++)
                            {
                                string current = host.Screen();
                                foreach (string section in new string[] { "Jobs over time", "Slots", "Link", "Launch speed", "ClaudeCode", "Tokens" })
                                {
                                    if (current.Contains(section, StringComparison.Ordinal)) seen.Add(section);
                                }

                                if (i == 0 && screen.Activity.CanScroll) TuiScreenDump.Write("harbor-metrics-" + width + "x" + height + "-" + mode + "-focused", current);
                                if (!screen.Activity.CanScroll) break;
                                host.Press("down");
                            }

                            TuiScreenDump.Write("harbor-metrics-" + width + "x" + height + "-" + mode + "-end", host.Screen());
                            AssertEqual(6, seen.Count, "sections seen: " + String.Join(", ", seen));
                        }
                    }));
                }
            }

            cases.Add(TuiCase.Sync(Suite, "strip_glyphs_not_color_alone", "Each link state has its own glyph in Unicode and in ASCII, and the strip keeps a short drop visible", () =>
            {
                StatusStripModel strip = HarborChartMapper.Link(HarborMetricsFixture.Build());
                foreach (bool ascii in new bool[] { false, true })
                {
                    IReadOnlyDictionary<HarborLinkSegmentStateEnum, string> glyphs = ascii ? HarborMetricsPanel.AsciiStateGlyphs : HarborMetricsPanel.UnicodeStateGlyphs;
                    AssertEqual(4, glyphs.Values.Distinct().Count(), "a distinct glyph per state");
                    string text = HarborMetricsPanel.StripText(strip, 60, ascii);
                    AssertEqual(60, text.Length, "a glyph per column");
                    AssertTrue(text.Contains(glyphs[HarborLinkSegmentStateEnum.Down], StringComparison.Ordinal), "down shows");
                    AssertTrue(text.Contains(glyphs[HarborLinkSegmentStateEnum.Reconnecting], StringComparison.Ordinal), "the short reconnect shows");
                    AssertTrue(text.StartsWith(glyphs[HarborLinkSegmentStateEnum.Unknown], StringComparison.Ordinal), "unknown first");
                    if (ascii) AssertTrue(text.All(c => c < 0x7F), "ASCII strip");
                }

                string spark = HarborMetricsPanel.SparkText(new double?[] { 0, 1, 2, 4, null }, 4, 10, true);
                AssertEqual(".-+@ ", spark, "zero, levels to the capacity, and a gap");
            }));

            cases.Add(TuiCase.Sync(Suite, "range_key", "r cycles the range 24h, 7d, 1h and asks for each", () =>
            {
                StubHttpHandler stub = TuiHarborsSuite.Server();
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, Route, stub))
                {
                    HarborsScreen screen = Loaded(host);
                    AssertEqual("24h", stub.Last("GET", "/api/v1/harbors/hbr_1/metrics").QueryValue("range"), "24h first");
                    host.Press("r");
                    AssertEqual("7d", screen.MetricsRange);
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/harbors/hbr_1/metrics", "range", "7d");
                    TuiEntityFixtures.WaitFor(host, () => screen.Activity.Range == "7d" && host.Screen().Contains("Last 7 days", StringComparison.Ordinal), "7d header");
                    host.Press("r");
                    TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/harbors/hbr_1/metrics", "range", "1h");
                    AssertEqual("1h", screen.MetricsRange);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "selection_follows_cursor", "Moving to another Harbor loads its activity", () =>
            {
                StubHttpHandler stub = TuiHarborsSuite.Server();
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, Route, stub))
                {
                    HarborsScreen screen = Loaded(host);
                    host.Press("down");
                    TuiEntityFixtures.WaitFor(host, () => screen.Activity.Metrics != null && screen.Activity.Metrics.HarborId == "hbr_2", "second Harbor's metrics");
                    AssertEqual("old-box", screen.Activity.HarborName);
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "token_usage_link_and_filter", "u opens Token Usage filtered to the Harbor; x shows every Harbor again", () =>
            {
                StubHttpHandler stub = TuiHarborsSuite.Server();
                stub.Json("GET", "/api/v1/token-usage/summary", "{\"RecordCount\":0,\"Buckets\":[],\"ByModel\":[]}");
                using (TuiTestHost host = TuiCase.SignedIn(160, 44, Route, stub))
                {
                    Loaded(host);
                    host.Press("u");
                    TuiEntityFixtures.WaitFor(host, () => Current<TokenUsageScreen>(host) != null, "Token Usage opened");
                    TokenUsageScreen tokens = Current<TokenUsageScreen>(host)!;
                    AssertEqual("hbr_1", tokens.HarborId, "filtered to the Harbor");
                    AssertEqual("day", tokens.RangeField.Value, "the panel's 24h range");
                    StubRequest filtered = TuiEntityFixtures.WaitForQuery(host, stub, "GET", "/api/v1/token-usage/summary", "harborId", "hbr_1");
                    AssertEqual("15", filtered.QueryValue("bucketMinutes"), "day buckets");
                    AssertEqual("hbr_1", tokens.BuildQuery().HarborId, "query carries the Harbor");
                    string frame = host.Screen();
                    TuiScreenDump.Write("token-usage-harbor-filter", frame);
                    TuiCase.Contains(frame, "Showing captains run on Harbor hbr_1", "says it is filtered");
                    TuiCase.Contains(Status(host), "x All Harbors", "the clear key is in the status bar");
                    int before = stub.CountFor("GET", "/api/v1/token-usage/summary");
                    host.Press("x");
                    AssertEqual("", tokens.HarborId, "filter cleared");
                    AssertNull(tokens.BuildQuery().HarborId, "no Harbor in the query");
                    TuiEntityFixtures.WaitFor(host, () => stub.CountFor("GET", "/api/v1/token-usage/summary") > before, "reloaded");
                    AssertEqual(null, stub.Last("GET", "/api/v1/token-usage/summary").QueryValue("harborId"), "reloaded without the filter");
                    TuiCase.NotContains(Status(host), "x All Harbors", "the clear key is gone");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "hints_follow_focus", "r and u are in the status bar; Tab to a panel that scrolls adds Up/Down", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(80, 24, Route, TuiHarborsSuite.Server()))
                {
                    HarborsScreen screen = Loaded(host);
                    string status = Status(host);
                    TuiCase.Contains(status, "r Range", "range key");
                    TuiCase.Contains(status, "u Token usage", "token usage key");
                    TuiCase.NotContains(status, "Up/Down Scroll", "grid focused: no scroll hint");
                    Focus(host, screen);
                    AssertTrue(screen.Activity.CanScroll, "the panel is taller than its box at 80x24");
                    TuiCase.Contains(Status(host), "Up/Down Scroll", "panel focused: scroll hint");
                    host.Press("end");
                    AssertTrue(screen.Activity.ScrollOffset > 0, "End scrolls to the bottom");
                    host.Press("home");
                    AssertEqual(0, screen.Activity.ScrollOffset, "Home scrolls back");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "errors", "A denied or missing metrics route says so in the panel; no Harbors means nothing to show", () =>
            {
                StubHttpHandler denied = TuiHarborsSuite.Server();
                denied.Json("GET", "/api/v1/harbors/hbr_1/metrics", "{\"Error\":\"Forbidden\"}", HttpStatusCode.Forbidden);
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, Route, denied))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.MetricsLoadCount > 0, "metrics answered");
                    AssertNull(screen.Activity.Metrics, "no charts");
                    AssertEqual("You are not allowed to see this Harbor's activity.", screen.Activity.Message);
                    TuiCase.Contains(host.Screen(), "You are not allowed", "shown");
                }

                StubHttpHandler missing = TuiHarborsSuite.Server();
                missing.Json("GET", "/api/v1/harbors/hbr_1/metrics", "{\"Error\":\"NotFound\"}", HttpStatusCode.NotFound);
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, Route, missing))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.MetricsLoadCount > 0, "metrics answered");
                    AssertEqual("The server has no activity for this Harbor (it may need updating).", screen.Activity.Message);
                }

                StubHttpHandler none = TuiEntityFixtures.Server();
                none.Json("GET", "/api/v1/harbors", "[]");
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, Route, none))
                {
                    HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
                    TuiEntityFixtures.WaitFor(host, () => screen.LoadCount > 0, "list loaded");
                    AssertNull(screen.Activity.Metrics);
                    AssertEqual("Select a Harbor to see its activity.", screen.Activity.Message);
                    AssertEqual(0, none.CountFor("GET", "/api/v1/harbors/hbr_1/metrics"), "nothing asked");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "focus_treatment_and_audit", "One focused box at every Tab stop at 80x24 and 120x40, and TUIKit's FocusAudit, with metrics", () =>
            {
                List<string> problems = new List<string>();
                foreach (int[] size in new int[][] { new int[] { 120, 40 }, new int[] { 80, 24 } })
                {
                    using (TuiTestHost host = TuiCase.SignedIn(size[0], size[1], Route, TuiHarborsSuite.Server()))
                    {
                        Loaded(host);
                        host.SettleRequests();
                        problems.AddRange(TuiFocusSweep.Sweep(host, Route + "@" + size[0] + "x" + size[1]));
                    }

                    using (TuiTestHost host = TuiCase.SignedIn(size[0], size[1], Route, TuiHarborsSuite.Server()))
                    {
                        Loaded(host);
                        host.SettleRequests();
                        AssertEqual(1, TuiFocusSweepSuite.Audit(host, Route + "@" + size[0] + "x" + size[1], problems), "audited");
                    }
                }

                AssertEqual(0, problems.Count, String.Join("\n", problems));
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI Harbor metrics", cases: cases);
        }

        private static HarborsScreen Loaded(TuiTestHost host)
        {
            HarborsScreen screen = TuiEntityFixtures.Screen<HarborsScreen>(host);
            TuiEntityFixtures.WaitFor(host, () => screen.Grid.Rows.Count == 2 && screen.Activity.Metrics != null, "rows and metrics");
            return screen;
        }

        private static void Focus(TuiTestHost host, HarborsScreen screen)
        {
            for (int i = 0; i < 8 && screen.Scope.Focused != screen.Activity; i++) host.Press("tab");
            AssertTrue(screen.Scope.Focused == screen.Activity, "Tab reaches the Activity panel");
        }

        private static string Status(TuiTestHost host)
        {
            string[] lines = host.Screen().Split('\n');
            return lines[host.Height - 1];
        }

        private static T? Current<T>(TuiTestHost host) where T : Armada.Tui.Screens.ScreenBase
        {
            Armada.Tui.Screens.ScreenBase? screen = host.Tui.Shell.Screen;
            if (screen is Armada.Tui.Screens.HubScreen hub) screen = hub.Content;
            return screen as T;
        }
    }
}
