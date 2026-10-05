namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Shell;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using TUIKit;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// W8.4 accessibility and display: every route and hub tab at the 80x24 minimum (sidebar collapsed, header status,
    /// the help hint, and the screen title always visible), the terminal-too-small screen, ASCII icon mode (auto from
    /// the terminal encoding, and every glyph written to the terminal is ASCII), the high-contrast theme, and status
    /// text that never depends on color. Set <c>ARMADA_TUI_FRAME_DUMP</c> to a directory to write every 80x24 frame
    /// there for review.
    /// </summary>
    public sealed class TuiDisplaySuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.Display";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "every_route_80x24", "Every route and hub tab lays out at 80x24 with the chrome intact", () =>
            {
                string? dump = Environment.GetEnvironmentVariable("ARMADA_TUI_FRAME_DUMP");
                List<string> failures = new List<string>();
                foreach (string path in AllPaths())
                {
                    using (TuiTestHost host = TuiCase.SignedIn(80, 24, path))
                    {
                        host.PumpUntil(() => false, 150);
                        string frame;
                        try
                        {
                            frame = host.Screen();
                        }
                        catch (Exception ex)
                        {
                            failures.Add(path + ": render threw " + ex.GetType().Name + ": " + ex.Message);
                            continue;
                        }

                        if (!String.IsNullOrEmpty(dump))
                        {
                            Directory.CreateDirectory(dump!);
                            File.WriteAllText(Path.Combine(dump!, path.Trim('/').Replace('/', '_').Replace('?', '_').Replace('=', '-') + ".txt"), frame);
                        }

                        string[] lines = frame.Split('\n');
                        ShellLayout? layout = host.Tui.Shell.LastLayout;
                        if (layout == null || layout.Mode != LayoutModeEnum.Narrow) failures.Add(path + ": expected the narrow layout");
                        else if (!layout.Sidebar.IsEmpty) failures.Add(path + ": sidebar should collapse at 80 columns");
                        if (!lines[0].Contains("Armada", StringComparison.Ordinal)) failures.Add(path + ": header missing");
                        if (!lines[0].Contains("bell", StringComparison.Ordinal)) failures.Add(path + ": notification bell cut off");
                        string status = lines.Length >= 24 ? lines[23] : "";
                        // Help is ? outside text fields and F1 while one has focus (? would be typed there).
                        string help = host.Tui.Shell.CurrentFocusHints().TextEntry ? "F1 Help" : "? Help";
                        if (!status.Contains(help, StringComparison.Ordinal)) failures.Add(path + ": help hint (" + help + ") cut off: " + status.TrimEnd());
                        foreach (string line in lines)
                        {
                            if (TextCells.Width(line.TrimEnd()) > 80) failures.Add(path + ": line wider than the terminal");
                        }
                    }
                }

                if (failures.Count > 0) throw new AssertionException(String.Join("\n", failures));
            }));

            cases.Add(TuiCase.Sync(Suite, "title_column_gets_the_room", "List titles keep a readable width at 80, 100, and 120 columns; IDs shrink or drop first", () =>
            {
                string? dump = Environment.GetEnvironmentVariable("ARMADA_TUI_FRAME_DUMP");
                string title = "Refactor the landing pipeline to retry flaky checks";
                foreach (int width in new int[] { 80, 100, 120, 160 })
                {
                    StubHttpHandler stub = TuiOpsMissionsSuite.Stub();
                    stub.Json("GET", "/api/v1/missions/summaries", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":1,\"Objects\":[{\"Id\":\"msn_01JABCDEFGHJKMNPQRSTVWXYZ4\",\"Title\":\"" + title + "\",\"Status\":\"InProgress\",\"Priority\":100,\"VesselId\":\"vsl_demo\",\"CaptainId\":\"cpt_1\",\"BranchName\":\"armada/landing-retry\",\"CreatedUtc\":\"2026-10-04T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T10:00:00Z\"}]}");
                    using (TuiTestHost host = TuiCase.SignedIn(width, 30, "/missions", stub))
                    {
                        AssertTrue(host.WaitForText("Refactor the"), "row shown at " + width);
                        string frame = host.Screen();
                        if (!String.IsNullOrEmpty(dump))
                        {
                            Directory.CreateDirectory(dump!);
                            File.WriteAllText(Path.Combine(dump!, "missions-" + width + ".txt"), frame);
                        }

                        string row = frame.Split('\n').First(l => l.Contains("Refactor the", StringComparison.Ordinal));
                        int shown = 0;
                        while (shown < title.Length && row.Contains(title.Substring(0, shown + 1), StringComparison.Ordinal)) shown++;
                        int expected = width >= 120 ? 28 : 22;
                        AssertTrue(shown >= expected, "title shows at least " + expected + " characters at " + width + " columns (shows " + shown + "):\n" + frame);
                        if (width >= 160) TuiCase.Contains(row, "msn_0", "ID shown (elided in the middle) when there is room");
                    }
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "grid_layout_priorities", "Grid layout: identifiers elide in the middle, drop before other columns, and grow last", () =>
            {
                ArmadaGrid<TestGridRow> grid = new ArmadaGrid<TestGridRow>(r => r.Id);
                grid.AddColumn(new GridColumn<TestGridRow>("name", "Name", r => r.Name) { Weight = 4 });
                grid.AddColumn(new GridColumn<TestGridRow>("id", "ID", r => r.Id) { Width = 30 });
                grid.AddColumn(new GridColumn<TestGridRow>("status", "Status", r => r.Status) { Width = 12 });
                grid.AddColumn(new GridColumn<TestGridRow>("other", "Other", r => r.Status) { Weight = 2 });
                grid.MultiSelect = false;
                grid.ShowPagingBar = false;
                grid.SetLocalRows(new List<TestGridRow> { new TestGridRow { Id = "msn_01JABCDEFGHJKMNPQRSTVWXYZ4", Name = "A fairly long mission title here", Status = "Complete" } });
                string narrow = TUIKit.Testing.Snapshot.RenderWidget(grid, 44, 4);
                TuiCase.NotContains(narrow, "msn_", "ID dropped first at 44 columns");
                TuiCase.Contains(narrow, "A fairly", "title kept");
                TuiCase.Contains(narrow, "Other", "other proportional column kept");
                TuiCase.Contains(narrow, "Status", "status kept");
                string mid = TUIKit.Testing.Snapshot.RenderWidget(grid, 90, 4);
                TuiCase.Contains(mid, "A fairly long mission title here", "title in full at 90");
                TuiCase.Contains(mid, "msn_", "ID shown at 90");
                TuiCase.Contains(mid, "XYZ4", "elided ID keeps its distinctive end");
                string wide = TUIKit.Testing.Snapshot.RenderWidget(grid, 140, 4);
                TuiCase.Contains(wide, "msn_01JABCDEFGHJKMNPQRSTVWXYZ4", "full ID when there is room");
                AssertEqual("msn_0...XYZ4", TextCells.ElideMiddle("msn_01JABCDEFGHJKMNPQRSTVWXYZ4", 12), "middle elision");
            }));

            cases.Add(TuiCase.Sync(Suite, "terminal_too_small", "Below 80x24 the TUI says what it needs and how to continue, signed in or out, at any size", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(80, 24, "/jobs"))
                {
                    host.Resize(79, 24);
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "Terminal too small", "headline");
                    TuiCase.Contains(frame, "Need 80x24, have 79x24.", "sizes");
                    TuiCase.Contains(frame, "Ctrl+Q Quit", "how to leave");
                    AssertEqual(LayoutModeEnum.TooSmall, host.Tui.Shell.LastLayout!.Mode, "too-small mode");
                    host.Resize(80, 23);
                    TuiCase.Contains(host.Screen(), "have 80x23", "short terminal");
                    host.Resize(12, 3);
                    host.Screen();
                    host.Resize(1, 1);
                    host.Screen();
                    host.Resize(80, 24);
                    TuiCase.NotContains(host.Screen(), "Terminal too small", "recovers on resize");
                }

                using (TuiTestHost host = new TuiTestHost(60, 20, TuiFixtures.SignedInServer()))
                {
                    host.Start();
                    TuiCase.Contains(host.Screen(), "Need 80x24, have 60x20.", "login screen too");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ascii_icon_mode", "ASCII icon mode writes only ASCII to the terminal and to snapshots, persists, and switches back", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/server?tab=server"))
                {
                    host.PumpUntil(() => false, 150);
                    host.Tui.Context.Commands.Execute("help.palette");
                    host.Pump();
                    AssertTrue(host.App.Modals.IsActive, "palette open (bordered modal)");
                    string unicode = host.Screen();
                    AssertTrue(unicode.Any(c => c > 0x7F), "unicode mode draws box glyphs");
                    host.Tui.Context.Commands.Execute("view.icons.ascii");
                    host.Pump();
                    AssertTrue(host.Tui.Context.Theme.AsciiGlyphs, "ascii glyphs");
                    AssertTrue(host.App.Theme.UseAsciiBorders, "TUIKit ascii borders");
                    AssertTrue(host.Adapter.AsciiOutput, "terminal output transliterated");
                    AssertEqual(GlyphModeEnum.Ascii, host.Tui.Context.Prefs.Current.Glyphs, "persisted");
                    AssertEqual(GlyphModeEnum.Ascii, JsonHelper.Deserialize<TuiPreferences>(File.ReadAllText(host.Tui.Context.Prefs.FilePath)).Glyphs, "saved to tui.json");
                    string ascii = host.Screen();
                    char bad = ascii.FirstOrDefault(c => c > 0x7F);
                    AssertTrue(bad == default(char), "snapshot is ASCII (found U+" + ((int)bad).ToString("X4") + ")");

                    host.App.Start();
                    try
                    {
                        host.Backend.TakeOutput();
                        host.App.RenderOnce();
                        string output = host.Backend.TakeOutput();
                        AssertTrue(output.Length > 0, "a frame was written");
                        char badOut = output.FirstOrDefault(c => c > 0x7F);
                        AssertTrue(badOut == default(char), "terminal output is ASCII (found U+" + ((int)badOut).ToString("X4") + ")");
                    }
                    finally
                    {
                        host.App.Stop();
                    }

                    host.Tui.Context.Commands.Execute("view.icons.unicode");
                    host.Pump();
                    AssertFalse(host.Adapter.AsciiOutput, "unicode output again");
                    AssertFalse(host.Tui.Context.Theme.AsciiGlyphs, "unicode glyphs again");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ascii_auto_detection", "Auto picks ASCII when the terminal is not UTF-8", () =>
            {
                Dictionary<string, string> env = new Dictionary<string, string>();
                Func<string, string?> read = name => env.TryGetValue(name, out string? v) ? v : null;
                AssertTrue(TerminalEncoding.SupportsUtf8(read, false), "nothing set assumes UTF-8");
                env["LANG"] = "en_US.UTF-8";
                AssertTrue(TerminalEncoding.SupportsUtf8(read, false), "UTF-8 LANG");
                env["LC_ALL"] = "C";
                AssertFalse(TerminalEncoding.SupportsUtf8(read, false), "LC_ALL wins and C is ASCII");
                env.Remove("LC_ALL");
                env["LC_CTYPE"] = "en_US.ISO8859-1";
                AssertFalse(TerminalEncoding.SupportsUtf8(read, false), "non-UTF-8 charset");
                env.Clear();
                env["TERM"] = "dumb";
                AssertFalse(TerminalEncoding.SupportsUtf8(read, false), "dumb terminal");
                env.Clear();
                AssertFalse(TerminalEncoding.SupportsUtf8(read, true), "legacy conhost");
                env["WT_SESSION"] = "abc";
                AssertTrue(TerminalEncoding.SupportsUtf8(read, true), "Windows Terminal");

                ThemeService svc = new ThemeService();
                svc.EnvironmentReader = name => null;
                svc.Utf8Probe = () => false;
                svc.ApplyGlyphs(GlyphModeEnum.Auto);
                AssertTrue(svc.AsciiGlyphs, "auto is ascii without UTF-8");
                AssertTrue(svc.TuiKitTheme.UseAsciiBorders, "ascii borders follow");
                svc.ApplyGlyphs(GlyphModeEnum.Unicode);
                AssertFalse(svc.AsciiGlyphs, "explicit unicode wins");
                svc.Utf8Probe = () => true;
                svc.ApplyGlyphs(GlyphModeEnum.Auto);
                AssertFalse(svc.AsciiGlyphs, "auto is unicode with UTF-8");

                using (TuiTestHost host = new TuiTestHost(100, 30, TuiFixtures.SignedInServer(), "http://127.0.0.1:9", o => o.Utf8Probe = () => false))
                {
                    host.Start();
                    AssertTrue(host.Adapter.AsciiOutput, "a non-UTF-8 terminal starts in ASCII mode");
                    AssertFalse(host.Screen().Any(c => c > 0x7F), "login screen is ASCII");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ascii_glyph_table", "Every mapped glyph is one cell wide and maps to printable ASCII; text is untouched", () =>
            {
                foreach (KeyValuePair<char, char> pair in AsciiGlyphs.Table)
                {
                    AssertEqual(1, TextCells.Width(pair.Key.ToString()), "one cell: U+" + ((int)pair.Key).ToString("X4"));
                    AssertTrue(pair.Value >= 0x20 && pair.Value < 0x7F, "printable ASCII for U+" + ((int)pair.Key).ToString("X4"));
                }

                for (char c = '\u2500'; c <= '\u257F'; c++) AssertTrue(AsciiGlyphs.Map(c) < 0x7F, "box drawing U+" + ((int)c).ToString("X4"));
                for (char c = '\u2800'; c <= '\u28FF'; c++) AssertTrue(AsciiGlyphs.Map(c) < 0x7F, "braille U+" + ((int)c).ToString("X4"));
                AssertEqual("+--+ * | o > ...", AsciiGlyphs.Transliterate("\u256D\u2500\u2500\u256E \u25CF \u2502 \u25CB \u2192 \u2026\u2026\u2026"), "transliterated");
                string cjk = "\u4FEE\u590D caf\u00E9 \u65E5\u672C";
                AssertTrue(ReferenceEquals(cjk, AsciiGlyphs.Transliterate(cjk)), "letters and CJK untouched");
            }));

            cases.Add(TuiCase.Sync(Suite, "high_contrast_without_color", "NO_COLOR picks high contrast, whose selection uses reverse video and underline", () =>
            {
                ThemeService svc = new ThemeService();
                svc.EnvironmentReader = name => name == "NO_COLOR" ? "1" : null;
                svc.Utf8Probe = () => true;
                svc.Apply(ThemeModeEnum.Auto);
                AssertEqual(ThemeModeEnum.HighContrast, svc.EffectiveMode, "auto with NO_COLOR");
                ArmadaTheme hc = ThemePalettes.HighContrast();
                foreach (CellStyle style in new CellStyle[] { hc.Selection, hc.GridCursor, hc.SidebarFocused, hc.MenuActive, hc.ButtonFocused, hc.TabActive })
                {
                    AssertTrue((style.Attributes & CellAttributes.Reverse) != 0, "selected states use reverse video");
                }

                AssertTrue((hc.InputFocused.Attributes & CellAttributes.Underline) != 0, "focused input underlined");
                AssertTrue((hc.SidebarSelected.Attributes & CellAttributes.Underline) != 0, "current sidebar item underlined");
                AssertTrue(hc.AsciiBorders, "ascii borders");
            }));

            cases.Add(TuiCase.Sync(Suite, "no_color_only_states", "Status, selection, progress, and connection states all carry text or a symbol", () =>
            {
                Dictionary<StatusSeverityEnum, string> markers = new Dictionary<StatusSeverityEnum, string>();
                foreach (string status in new string[] { "Complete", "Failed", "Stalled", "InProgress", "Pending", "Landed", "Unknown" })
                {
                    string label = StatusBadge.Label(status);
                    AssertTrue(label.EndsWith(status, StringComparison.Ordinal), "label keeps the status text");
                    markers[StatusBadge.Severity(status)] = StatusBadge.Marker(status);
                }

                AssertEqual(markers.Count, markers.Values.Distinct().Count(), "each severity has its own symbol");

                Wizard wizard = new Wizard(new[] { new WizardStep("Fleet", new TextBlock("a")), new WizardStep("Vessel", new TextBlock("b")) });
                wizard.Next();
                string steps = TUIKit.Testing.Snapshot.RenderWidget(wizard, 60, 8);
                TuiCase.Contains(steps, "+1. Fleet", "completed step marked");
                TuiCase.Contains(steps, "[2. Vessel]", "current step bracketed");

                using (TuiTestHost host = TuiCase.SignedIn(120, 30, "/missions"))
                {
                    string frame = host.Screen();
                    TuiCase.Contains(frame, "[Missions]", "active tab bracketed");
                    TuiCase.Contains(frame, "o Offline", "connection state in words");
                    TuiCase.Contains(frame, ">  Missions", "current sidebar item marked");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "header_keeps_status_at_80", "At 80 columns a long user and tenant give way to approvals and the bell", () =>
            {
                StubHttpHandler stub = TuiFixtures.SignedInServer();
                stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(System.Net.HttpStatusCode.OK, TuiFixtures.WhoAmI().Replace("admin@armada", "someone.with.a.very.long.address@engineering.example.com").Replace("Default Tenant", "Platform Engineering Department")));
                using (TuiTestHost host = TuiCase.SignedIn(80, 24, "/jobs", stub))
                {
                    ApprovalItem item = new ApprovalItem();
                    item.EntityId = "msn_1";
                    item.Title = "Review";
                    host.Tui.Context.Approvals.Upsert(item);
                    string header = host.Screen().Split('\n')[0];
                    TuiCase.Contains(header, "[!1 approvals]", "approvals visible");
                    TuiCase.Contains(header, "[bell", "bell visible");
                    TuiCase.Contains(header, "Armada", "product visible");
                    AssertTrue(TextCells.Width(header.TrimEnd()) <= 80, "fits");
                }
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI accessibility and display", cases: cases);
        }

        internal static List<string> AllPaths()
        {
            List<string> paths = new List<string>();
            foreach (RouteDefinition route in RouteTable.All)
            {
                if (route.RedirectTo != null) continue;
                string path = String.Join("/", route.Pattern.Split('/').Select(s => s.StartsWith(":", StringComparison.Ordinal) ? (s.EndsWith("?", StringComparison.Ordinal) ? "" : "x_1") : s)).TrimEnd('/');
                if (path.Length == 0) path = "/";
                if (route.Hub != null)
                {
                    foreach (HubTab tab in route.Hub.Tabs) paths.Add(path + "?" + route.Hub.QueryParam + "=" + tab.Key);
                }
                else
                {
                    paths.Add(path);
                }
            }

            return paths.Distinct().ToList();
        }
    }
}
