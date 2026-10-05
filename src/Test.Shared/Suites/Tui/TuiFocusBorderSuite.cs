namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Screens.Ask;
    using Armada.Tui.Shell;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Testing;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Focus borders: the shell pane that holds keyboard focus (sidebar, main screen, Ask dock) draws its border in the
    /// theme's <see cref="ArmadaTheme.FocusBorder"/> with heavy glyphs, the others a light <see cref="ArmadaTheme.Border"/>
    /// line, and a focused sub-region of the main screen (grid, filter row, Ask composer or transcript) lights only the
    /// stretch of the main border alongside it. Checked cell by cell (glyph and style) while Tab, Shift+Tab, Ctrl+J, and
    /// Esc move focus, in Unicode and ASCII glyph modes, in the high-contrast theme (reverse video, not color alone), and
    /// at the 80x24 minimum, and the content never moves when focus does.
    /// </summary>
    public sealed class TuiFocusBorderSuite : IArmadaTestSuite
    {
        private const string Suite = "Tui.FocusBorder";

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Sync(Suite, "tab_moves_the_pane_border", "Shift+Tab and Tab move the focus border between the sidebar and the main screen", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.WaitForText("Missions");
                    ShellView shell = host.Tui.Shell;
                    ShellLayout layout = shell.LastLayout!;
                    AssertFalse(layout.Sidebar.IsEmpty, "sidebar shown");
                    AssertTrue(ReferenceEquals(shell.Scope.Focused, shell.Screen), "main focused after navigation");
                    CellBuffer frame = Render(host);
                    AssertTrue(Perimeter(frame, layout.Main, shell.Theme).Any(c => c.Focused), "main border lit");
                    AssertTrue(Perimeter(frame, layout.Sidebar, shell.Theme).All(c => !c.Focused), "sidebar border dim");

                    AssertTrue(PressUntil(host, "shift+tab", () => ReferenceEquals(shell.Scope.Focused, shell.Sidebar)), "Shift+Tab reaches the sidebar");
                    frame = Render(host);
                    AssertTrue(Perimeter(frame, layout.Sidebar, shell.Theme).All(c => c.Focused), "whole sidebar border lit");
                    AssertTrue(Perimeter(frame, layout.Main, shell.Theme).All(c => !c.Focused), "main border dim");
                    AssertEqual("\u250F", frame.Get(layout.Sidebar.X, layout.Sidebar.Y).Grapheme, "heavy corner");
                    AssertEqual("\u2510", frame.Get(layout.Main.Right - 1, layout.Main.Y).Grapheme, "light corner");

                    host.Press("tab");
                    AssertTrue(ReferenceEquals(shell.Scope.Focused, shell.Screen), "Tab returns to the main screen");
                    frame = Render(host);
                    AssertTrue(Perimeter(frame, layout.Main, shell.Theme).Any(c => c.Focused), "main border lit again");
                    AssertTrue(Perimeter(frame, layout.Sidebar, shell.Theme).All(c => !c.Focused), "sidebar border dim again");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "sub_region_stretch", "Inside the main screen only the border alongside the focused sub-region is lit, and it follows Tab", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.WaitForText("Missions");
                    ShellView shell = host.Tui.Shell;
                    ShellLayout layout = shell.LastLayout!;
                    AssertTrue(PressUntil(host, "shift+tab", () => ReferenceEquals(shell.Scope.Focused, shell.Sidebar)), "start from the sidebar");
                    HashSet<string> seen = new HashSet<string>();
                    for (int step = 0; step < 12; step++)
                    {
                        host.Press("tab");
                        if (!ReferenceEquals(shell.Scope.Focused, shell.Screen)) break;
                        CellBuffer frame = Render(host);
                        Rect region = shell.Screen!.Scope.FocusedRegion();
                        AssertFalse(region.IsEmpty, "focused sub-region placed");
                        Rect inner = layout.MainInner;
                        bool whole = region.X <= 0 && region.Y <= 0 && region.Right >= inner.Width && region.Bottom >= inner.Height;
                        AssertTrue(region.X <= 0 || region.Right >= inner.Width, "list screens stretch regions to a pane edge");
                        int x = region.X <= 0 ? layout.Main.X : layout.Main.Right - 1;
                        for (int r = 0; r < inner.Height; r++)
                        {
                            TuiBorderCell cell = Classify(frame, x, inner.Y + r, shell.Theme);
                            bool expected = whole || (r >= region.Y && r < region.Bottom);
                            AssertEqual(expected, cell.Focused, "side border row " + r + " at step " + step);
                        }

                        seen.Add(region.Y + ":" + region.Height);
                        AssertTrue(Perimeter(frame, layout.Sidebar, shell.Theme).All(c => !c.Focused), "sidebar dim at step " + step);
                    }

                    AssertTrue(seen.Count >= 2, "Tab visited at least two sub-regions (tab strip, filters, grid)");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ask_dock_and_escape", "Ctrl+J docks Ask with its own border; Tab lights it; on the Ask screen Esc moves the lit stretch between composer and transcript", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.WaitForText("Missions");
                    ShellView shell = host.Tui.Shell;
                    host.Press("ctrl+j");
                    host.Screen();
                    ShellLayout layout = shell.LastLayout!;
                    AssertFalse(layout.Dock.IsEmpty, "dock shown");
                    CellBuffer frame = Render(host);
                    AssertTrue(Perimeter(frame, layout.Dock, shell.Theme).All(c => !c.Focused), "dock dim before it has focus");
                    TuiCase.Contains(Text(frame), "Ask Armada", "dock title inside its border");
                    AssertTrue(PressUntil(host, "tab", () => ReferenceEquals(shell.Scope.Focused, shell.Dock)), "Tab reaches the dock");
                    frame = Render(host);
                    AssertTrue(Perimeter(frame, layout.Dock, shell.Theme).All(c => c.Focused), "whole dock border lit");
                    AssertTrue(Perimeter(frame, layout.Main, shell.Theme).All(c => !c.Focused), "main dim");
                    AssertTrue(Perimeter(frame, layout.Sidebar, shell.Theme).All(c => !c.Focused), "sidebar dim");
                }

                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/ask"))
                {
                    ShellView shell = host.Tui.Shell;
                    AskScreen screen = (AskScreen)shell.Screen!;
                    host.Screen();
                    ShellLayout layout = shell.LastLayout!;
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Composer), "composer focused");
                    CellBuffer frame = Render(host);
                    Rect composer = screen.Scope.RectOf(screen.Composer);
                    Rect transcript = screen.Scope.RectOf(screen.Transcript);
                    int right = layout.Main.Right - 1;
                    int top = layout.MainInner.Y;
                    AssertTrue(RowsLit(frame, right, top + composer.Y, composer.Height, shell.Theme), "composer stretch lit");
                    AssertTrue(RowsDim(frame, right, top + transcript.Y, transcript.Height, shell.Theme), "transcript stretch dim");
                    AssertTrue(Classify(frame, layout.Main.X + 1 + composer.X, layout.Main.Bottom - 1, shell.Theme).Focused, "bottom edge under the composer lit");
                    AssertFalse(Classify(frame, layout.Main.X + 1, layout.Main.Y, shell.Theme).Focused, "top edge dim");

                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Transcript), "Esc focuses the transcript");
                    frame = Render(host);
                    transcript = screen.Scope.RectOf(screen.Transcript);
                    AssertTrue(RowsLit(frame, right, top + transcript.Y, transcript.Height, shell.Theme), "transcript stretch lit");
                    AssertTrue(RowsDim(frame, right, top + composer.Y, composer.Height, shell.Theme), "composer stretch dim");
                    AssertFalse(Classify(frame, layout.Main.X + 1 + composer.X, layout.Main.Bottom - 1, shell.Theme).Focused, "bottom edge dim");

                    host.Press("esc");
                    AssertTrue(ReferenceEquals(screen.Scope.Focused, screen.Composer), "Esc returns to the composer");
                    frame = Render(host);
                    AssertTrue(RowsLit(frame, right, top + composer.Y, composer.Height, shell.Theme), "composer stretch lit again");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "ascii_glyphs", "ASCII glyph mode draws + - | borders and still tells the focused pane apart", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.WaitForText("Missions");
                    host.Tui.Context.Commands.Execute("view.icons.ascii");
                    host.Tui.Context.Notifications.DismissToasts();
                    ShellView shell = host.Tui.Shell;
                    AssertTrue(shell.Theme.AsciiGlyphs, "ascii glyphs");
                    AssertTrue(PressUntil(host, "shift+tab", () => ReferenceEquals(shell.Scope.Focused, shell.Sidebar)), "sidebar focused");
                    ShellLayout layout = shell.LastLayout!;
                    CellBuffer frame = Render(host);
                    List<TuiBorderCell> side = Perimeter(frame, layout.Sidebar, shell.Theme);
                    List<TuiBorderCell> main = Perimeter(frame, layout.Main, shell.Theme);
                    AssertTrue(side.All(c => c.Focused), "sidebar lit");
                    AssertTrue(main.All(c => !c.Focused), "main dim");
                    AssertTrue(side.Concat(main).All(c => c.Glyph == "+" || c.Glyph == "-" || c.Glyph == "|"), "ASCII border glyphs only");
                    string text = host.Screen();
                    string[] lines = text.Split('\n');
                    AssertTrue(lines[layout.Sidebar.Y].StartsWith("+" + new string('-', layout.Sidebar.Width - 2) + "++", StringComparison.Ordinal), "ASCII boxes in the snapshot: " + lines[layout.Sidebar.Y]);
                    AssertFalse(text.Any(c => c > 0x7F), "snapshot is ASCII");

                    host.Press("tab");
                    frame = Render(host);
                    AssertTrue(Perimeter(frame, layout.Sidebar, shell.Theme).All(c => !c.Focused), "sidebar dim after Tab");
                    AssertTrue(Perimeter(frame, layout.Main, shell.Theme).Any(c => c.Focused), "main lit after Tab");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "high_contrast_reverse", "High contrast marks the focused border with reverse video, not color alone", () =>
            {
                ArmadaTheme hc = ThemePalettes.HighContrast();
                AssertTrue(hc.FocusBorder.HasAttribute(CellAttributes.Reverse), "focus border reversed");
                AssertFalse(hc.Border.HasAttribute(CellAttributes.Reverse), "plain border not reversed");
                foreach (ArmadaTheme palette in new ArmadaTheme[] { ThemePalettes.Dark(), ThemePalettes.Light(), hc })
                {
                    AssertTrue(palette.FocusBorder != palette.Border, palette.Name + ": focus border differs from the plain border");
                    AssertTrue(palette.FocusBorder.HasAttribute(CellAttributes.Bold), palette.Name + ": focus border bold");
                }

                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.WaitForText("Missions");
                    host.Tui.Context.Commands.Execute("view.theme.high-contrast");
                    host.Tui.Context.Notifications.DismissToasts();
                    ShellView shell = host.Tui.Shell;
                    AssertEqual(ThemeModeEnum.HighContrast, shell.Theme.Mode, "high contrast applied");
                    AssertTrue(PressUntil(host, "shift+tab", () => ReferenceEquals(shell.Scope.Focused, shell.Sidebar)), "sidebar focused");
                    ShellLayout layout = shell.LastLayout!;
                    CellBuffer frame = Render(host);
                    List<TuiBorderCell> side = Perimeter(frame, layout.Sidebar, shell.Theme);
                    List<TuiBorderCell> main = Perimeter(frame, layout.Main, shell.Theme);
                    AssertTrue(side.All(c => c.Focused && c.Style.HasAttribute(CellAttributes.Reverse)), "focused border in reverse video");
                    AssertTrue(main.All(c => !c.Focused && !c.Style.HasAttribute(CellAttributes.Reverse)), "unfocused border plain");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "minimum_size", "At 80x24 the main pane and the dock keep their borders, the chrome stays intact, and focus moves the border", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(80, 24, "/missions"))
                {
                    host.WaitForText("Missions");
                    ShellView shell = host.Tui.Shell;
                    ShellLayout layout = shell.LastLayout!;
                    AssertEqual(LayoutModeEnum.Narrow, layout.Mode, "narrow");
                    AssertEqual(FocusFrameKindEnum.Box, FocusFrame.KindFor(layout.Main), "main boxed at the minimum");
                    AssertEqual(new Rect(0, 2, 80, 21), layout.Main, "main border rectangle");
                    AssertEqual(new Rect(1, 3, 78, 19), layout.MainInner, "main content rectangle");
                    CellBuffer frame = Render(host);
                    AssertTrue(Perimeter(frame, layout.Main, shell.Theme).Any(c => c.Focused), "main lit");
                    string[] lines = Text(frame).Split('\n');
                    TuiCase.Contains(lines[0], "Armada", "header row intact");
                    TuiCase.Contains(lines[23], "? Help", "status row intact");
                    TuiCase.Contains(Text(frame), "Missions", "title visible");

                    host.Press("ctrl+j");
                    host.Screen();
                    layout = shell.LastLayout!;
                    AssertEqual(FocusFrameKindEnum.Box, FocusFrame.KindFor(layout.Dock), "dock boxed at the minimum");
                    AssertTrue(layout.DockInner.Height >= 4, "dock keeps room for its title, a line, and the input");
                    AssertTrue(layout.MainInner.Height >= 7, "main keeps usable rows with the dock open");
                    AssertTrue(PressUntil(host, "tab", () => ReferenceEquals(shell.Scope.Focused, shell.Dock)), "Tab reaches the dock");
                    frame = Render(host);
                    AssertTrue(Perimeter(frame, layout.Dock, shell.Theme).All(c => c.Focused), "dock lit");
                    AssertTrue(Perimeter(frame, layout.Main, shell.Theme).All(c => !c.Focused), "main dim");
                    TuiCase.Contains(Text(frame), "Ask Armada", "dock title shown");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "layout_stable", "Moving focus changes only border cells: the content does not move", () =>
            {
                using (TuiTestHost host = TuiCase.SignedIn(120, 40, "/missions"))
                {
                    host.WaitForText("Missions");
                    ShellView shell = host.Tui.Shell;
                    string mainFocused = Body(host);
                    Rect inner = shell.LastLayout!.MainInner;
                    AssertTrue(PressUntil(host, "shift+tab", () => ReferenceEquals(shell.Scope.Focused, shell.Sidebar)), "sidebar focused");
                    string sidebarFocused = Body(host);
                    AssertEqual(inner, shell.LastLayout!.MainInner, "main content rectangle unchanged");
                    AssertEqual(mainFocused, sidebarFocused, "same content cells with either pane focused");
                }
            }));

            cases.Add(TuiCase.Sync(Suite, "frame_fallbacks", "Boxes need three rows and columns; smaller rectangles fall back to a left gutter bar; an inner sub-region lights the whole box", () =>
            {
                AssertEqual(FocusFrameKindEnum.Box, FocusFrame.KindFor(new Rect(0, 0, 3, 3)), "3x3 box");
                AssertEqual(FocusFrameKindEnum.Gutter, FocusFrame.KindFor(new Rect(0, 0, 20, 2)), "two rows: gutter");
                AssertEqual(FocusFrameKindEnum.None, FocusFrame.KindFor(new Rect(0, 0, 1, 5)), "one column: nothing");
                AssertEqual(new Rect(1, 0, 19, 2), FocusFrame.Inner(new Rect(0, 0, 20, 2)), "gutter reserves one column");
                ArmadaTheme theme = ThemePalettes.Dark();

                CellBuffer gutter = new CellBuffer(20, 2);
                FocusFrame.Draw(new BufferSurface(gutter), new Rect(0, 0, 20, 2), theme, true, Rect.Empty);
                AssertEqual("\u2503", gutter.Get(0, 0).Grapheme, "focused gutter bar");
                AssertEqual(theme.FocusBorder, gutter.Get(0, 1).Style, "gutter style");
                FocusFrame.Draw(new BufferSurface(gutter), new Rect(0, 0, 20, 2), theme, false, Rect.Empty);
                AssertEqual(" ", gutter.Get(0, 0).Grapheme, "unfocused gutter blank");

                CellBuffer box = new CellBuffer(12, 8);
                FocusFrame.Draw(new BufferSurface(box), new Rect(0, 0, 12, 8), theme, true, new Rect(3, 2, 4, 2));
                AssertTrue(Perimeter(box, new Rect(0, 0, 12, 8), theme).All(c => c.Focused), "sub-region touching no edge lights the whole box");
                FocusFrame.Draw(new BufferSurface(box), new Rect(0, 0, 12, 8), theme, true, new Rect(0, 2, 8, 2));
                AssertTrue(Classify(box, 0, 3, theme).Focused, "left edge beside the sub-region lit");
                AssertFalse(Classify(box, 0, 1, theme).Focused, "left edge above it dim");
                AssertFalse(Classify(box, 11, 3, theme).Focused, "right edge dim (not touched)");
                AssertFalse(Classify(box, 0, 0, theme).Focused, "corner dim");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "TUI focus borders", cases: cases);
        }

        private static CellBuffer Render(TuiTestHost host)
        {
            host.Pump();
            CellBuffer buffer = new CellBuffer(host.Width, host.Height);
            host.Tui.Shell.Render(new BufferSurface(buffer));
            return buffer;
        }

        private static string Text(CellBuffer buffer)
        {
            return Snapshot.ToText(buffer);
        }

        /// <summary>
        /// The body rows (between the menu bar and the status bar) with every border cell blanked, so only content
        /// remains.
        /// </summary>
        private static string Body(TuiTestHost host)
        {
            CellBuffer frame = Render(host);
            ShellLayout layout = host.Tui.Shell.LastLayout!;
            foreach (Rect pane in new Rect[] { layout.Sidebar, layout.Main, layout.Dock })
            {
                if (pane.IsEmpty) continue;
                foreach (TuiBorderCell c in Perimeter(frame, pane, host.Tui.Shell.Theme)) frame.Set(c.X, c.Y, Cell.Blank(CellStyle.Default));
            }

            string[] lines = Snapshot.ToText(frame).Split('\n');
            return String.Join("\n", lines.Skip(layout.Main.Y).Take(layout.Main.Height));
        }

        private static bool PressUntil(TuiTestHost host, string key, Func<bool> condition)
        {
            for (int i = 0; i < 12 && !condition(); i++) host.Press(key);
            return condition();
        }

        private static bool RowsLit(CellBuffer frame, int x, int y, int count, ArmadaTheme theme)
        {
            for (int r = 0; r < count; r++)
            {
                if (!Classify(frame, x, y + r, theme).Focused) return false;
            }

            return count > 0;
        }

        private static bool RowsDim(CellBuffer frame, int x, int y, int count, ArmadaTheme theme)
        {
            for (int r = 0; r < count; r++)
            {
                if (Classify(frame, x, y + r, theme).Focused) return false;
            }

            return count > 0;
        }

        private static List<TuiBorderCell> Perimeter(CellBuffer frame, Rect outer, ArmadaTheme theme)
        {
            List<TuiBorderCell> cells = new List<TuiBorderCell>();
            for (int x = outer.X; x < outer.Right; x++)
            {
                cells.Add(Classify(frame, x, outer.Y, theme));
                cells.Add(Classify(frame, x, outer.Bottom - 1, theme));
            }

            for (int y = outer.Y + 1; y < outer.Bottom - 1; y++)
            {
                cells.Add(Classify(frame, outer.X, y, theme));
                cells.Add(Classify(frame, outer.Right - 1, y, theme));
            }

            return cells;
        }

        /// <summary>
        /// Classify a border cell: focused cells use the focus style and the focused glyph set, unfocused cells the
        /// plain border style and the light glyph set; anything else fails (the border was overwritten or missing).
        /// </summary>
        private static TuiBorderCell Classify(CellBuffer frame, int x, int y, ArmadaTheme theme)
        {
            Cell cell = frame.Get(x, y);
            TuiBorderCell result = new TuiBorderCell { X = x, Y = y, Glyph = cell.Grapheme, Style = cell.Style };
            string focusedGlyphs = FocusFrame.GlyphsFor(theme, true);
            string plainGlyphs = FocusFrame.GlyphsFor(theme, false);
            if (cell.Style == theme.FocusBorder && cell.Grapheme.Length == 1 && focusedGlyphs.Contains(cell.Grapheme, StringComparison.Ordinal))
            {
                result.Focused = true;
            }
            else if (cell.Style == theme.Border && cell.Grapheme.Length == 1 && plainGlyphs.Contains(cell.Grapheme, StringComparison.Ordinal))
            {
                result.Focused = false;
            }
            else
            {
                throw new AssertionException("not a border cell at " + x + "," + y + ": \"" + cell.Grapheme + "\"");
            }

            return result;
        }
    }
}
