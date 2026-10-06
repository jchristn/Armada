namespace Test.Shared.Suites.Tui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Armada.Tui.Screens;
    using Armada.Tui.Shell;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using Test.Shared.Infrastructure;
    using TUIKit;
    using TUIKit.Testing;
    using TUIKit.Widgets;
    using FocusFrame = Armada.Tui.Widgets.FocusFrame;
    using FocusScope = Armada.Tui.Widgets.FocusScope;

    /// <summary>
    /// Typed render inspection for the focus treatment (see <see cref="TuiFocusSweepSuite"/>): renders the shell into a
    /// cell buffer and checks, for the current focus, that exactly one box is drawn in the focus style with focused
    /// glyphs, that it is the box of the pane or region on the focus path and contains the focused widget, that every
    /// other box is a plain line, and that no box covers content. Not thread-safe.
    /// </summary>
    public static class TuiFocusSweep
    {
        #region Public-Methods

        /// <summary>
        /// Render the shell (no modals) into a buffer.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <returns>Buffer.</returns>
        public static CellBuffer Render(TuiTestHost host)
        {
            host.Pump();
            CellBuffer buffer = new CellBuffer(host.Width, host.Height);
            host.Tui.Shell.Render(new BufferSurface(buffer));
            return buffer;
        }

        /// <summary>
        /// True when a cell is drawn as a focused border (focus style and a focused glyph).
        /// </summary>
        /// <param name="cell">Cell.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>True when focused.</returns>
        public static bool IsFocusedBorder(Cell cell, ArmadaTheme theme)
        {
            string glyphs = FocusFrame.GlyphsFor(theme, true);
            return cell.Style == theme.FocusBorder && cell.Grapheme.Length == 1 && glyphs.Contains(cell.Grapheme, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when a cell is drawn as a plain border (border style and a light glyph or junction).
        /// </summary>
        /// <param name="cell">Cell.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>True when plain.</returns>
        public static bool IsPlainBorder(Cell cell, ArmadaTheme theme)
        {
            string glyphs = FocusFrame.UnfocusedGlyphsFor(theme);
            return cell.Style == theme.Border && cell.Grapheme.Length == 1 && glyphs.Contains(cell.Grapheme, StringComparison.Ordinal);
        }

        /// <summary>
        /// The perimeter cells of a box.
        /// </summary>
        /// <param name="box">Box.</param>
        /// <returns>Points.</returns>
        public static List<Point> Perimeter(Rect box)
        {
            List<Point> points = new List<Point>();
            if (box.IsEmpty) return points;
            for (int x = box.X; x < box.Right; x++)
            {
                points.Add(new Point(x, box.Y));
                if (box.Height > 1) points.Add(new Point(x, box.Bottom - 1));
            }

            for (int y = box.Y + 1; y < box.Bottom - 1; y++)
            {
                points.Add(new Point(box.X, y));
                if (box.Width > 1) points.Add(new Point(box.Right - 1, y));
            }

            return points;
        }

        /// <summary>
        /// Check the current focus state and return every problem found (empty when the treatment is right).
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="label">Label for messages.</param>
        /// <returns>Problems.</returns>
        public static List<string> Check(TuiTestHost host, string label)
        {
            List<string> problems = new List<string>();
            ShellView shell = host.Tui.Shell;
            CellBuffer frame = Render(host);
            // A dialog that opened while rendering (an error report) holds focus now.
            if (host.Tui.Context.Modals.IsModalOpen) return CheckModal(host, label + " dialog");
            Dump(frame, label);
            ArmadaTheme theme = shell.Theme;
            ShellLayout? layout = shell.LastLayout;
            if (layout == null)
            {
                problems.Add(label + ": no layout");
                return problems;
            }

            // TUIKit's focus path follows Armada's scopes to the same leaf, and focus never rests on a widget that reports
            // itself hidden or empty (TUIKit's IHideable, the check behind FocusAudit's InvisibleStop).
            FocusPath path = shell.BuildFocusPath();
            if (!ReferenceEquals(path.Leaf, shell.FocusedLeaf())) problems.Add(label + ": focus path leaf " + path.Leaf + " is not the focused leaf " + Describe(shell.FocusedLeaf()));
            foreach (object node in path.Nodes)
            {
                if (node is IHideable hideable && !hideable.IsVisible) problems.Add(label + ": focus rests on hidden " + node.GetType().Name);
            }

            Rect focusedBox = shell.LastFocusedBox;
            if (focusedBox.IsEmpty)
            {
                problems.Add(label + ": no focused box drawn");
                return problems;
            }

            // Exactly one box in the focus style: every focused-border cell lies on the focused box's perimeter and the
            // whole perimeter is focused.
            HashSet<Point> perimeter = new HashSet<Point>(Perimeter(focusedBox));
            HashSet<Point> titles = TitleCells(shell.LastRegions, layout.Main);
            foreach (Point p in perimeter)
            {
                if (titles.Contains(p)) continue;
                if (!IsFocusedBorder(frame.Get(p.X, p.Y), theme))
                {
                    problems.Add(label + ": focused box " + focusedBox + " not whole at " + p.X + "," + p.Y + " (\"" + frame.Get(p.X, p.Y).Grapheme + "\")");
                    break;
                }
            }

            int stray = 0;
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    if (!perimeter.Contains(new Point(x, y)) && IsFocusedBorder(frame.Get(x, y), theme)) stray++;
                }
            }

            if (stray > 0) problems.Add(label + ": " + stray + " focused border cells outside the focused box");

            IWidget? pane = shell.Scope.Focused;
            if (ReferenceEquals(pane, shell.Screen) && shell.Screen != null)
            {
                IReadOnlyList<RegionFrame> regions = shell.LastRegions;
                RegionFrame? focused = RegionFrames.FocusedOf(regions);
                List<IWidget> chain = shell.BuildFocusPath().Nodes.Skip(1).OfType<IWidget>().ToList();
                IWidget? leaf = chain.Count > 0 ? chain[chain.Count - 1] : null;
                bool leafPlaced = leaf != null && !ReferenceEquals(leaf, shell.Screen);
                if (focused == null)
                {
                    if (regions.Count > 0 || leafPlaced) problems.Add(label + ": focus on " + Describe(leaf) + " but no focused region (regions: " + regions.Count + ")");
                    else if (focusedBox != layout.Main) problems.Add(label + ": screen without regions should light the main pane");
                }
                else if (chain.OfType<IRegionOverlayHost>().Any(h => ReferenceEquals(h.RegionOverlay, focused.Widget)))
                {
                    // An open drawer holds focus over its screen: it is the only box.
                    if (regions.Count != 1) problems.Add(label + ": " + regions.Count + " boxes while a drawer is open");
                }
                else
                {
                    if (!chain.Contains(focused.Widget)) problems.Add(label + ": focused region " + Describe(focused.Widget) + " is not on the focus path");
                    if (focused.Box.Intersect(layout.Main) != focusedBox) problems.Add(label + ": focused box " + focusedBox + " is not the region box " + focused.Box);
                    Rect leafRect = LeafRect(shell, layout.MainInner);
                    if (!leafRect.IsEmpty && leafRect.Intersect(focused.Region) != leafRect) problems.Add(label + ": focused widget " + Describe(leaf) + " at " + leafRect + " is outside its region " + focused.Region);
                    if (focused.Region.Width < 1 || focused.Region.Height < 1) problems.Add(label + ": empty focused region");
                }

                // Unfocused boxes are plain lines, and no box covers content or another region.
                CellBuffer bare = new CellBuffer(layout.MainInner.Width, layout.MainInner.Height);
                shell.Screen.Render(new BufferSurface(bare));
                foreach (RegionFrame r in regions)
                {
                    foreach (Point p in Perimeter(r.Box.Intersect(layout.Main)))
                    {
                        if (perimeter.Contains(p) || titles.Contains(p)) continue;
                        Cell cell = frame.Get(p.X, p.Y);
                        if (!IsPlainBorder(cell, theme))
                        {
                            problems.Add(label + ": box of " + Describe(r.Widget) + " " + r.Box + " not a plain line at " + p.X + "," + p.Y + " (\"" + cell.Grapheme + "\")");
                            break;
                        }
                    }

                    foreach (Point p in Perimeter(r.Box))
                    {
                        int bx = p.X - layout.MainInner.X;
                        int by = p.Y - layout.MainInner.Y;
                        if (bx < 0 || by < 0 || bx >= bare.Width || by >= bare.Height) continue;
                        string under = bare.Get(bx, by).Grapheme;
                        if (under != " " && under != "")
                        {
                            problems.Add(label + ": box of " + Describe(r.Widget) + " " + r.Box + " covers content \"" + under + "\" at " + p.X + "," + p.Y);
                            break;
                        }

                        RegionFrame? inside = regions.FirstOrDefault(o => !ReferenceEquals(o, r) && o.Region.Contains(p));
                        if (inside != null)
                        {
                            problems.Add(label + ": box of " + Describe(r.Widget) + " cuts through " + Describe(inside.Widget) + " at " + p.X + "," + p.Y);
                            break;
                        }
                    }
                }
            }
            else if (ReferenceEquals(pane, shell.Sidebar))
            {
                if (focusedBox != layout.Sidebar) problems.Add(label + ": sidebar focused but the focused box is " + focusedBox);
            }
            else if (ReferenceEquals(pane, shell.Dock))
            {
                if (focusedBox != layout.Dock) problems.Add(label + ": dock focused but the focused box is " + focusedBox);
            }

            return problems;
        }

        /// <summary>
        /// Check the focus treatment while a dialog is open: no pane box is focused, the topmost dialog's box is drawn
        /// whole in the focus style, and no other cell is.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <param name="label">Label for messages.</param>
        /// <returns>Problems.</returns>
        public static List<string> CheckModal(TuiTestHost host, string label)
        {
            List<string> problems = new List<string>();
            ShellView shell = host.Tui.Shell;
            CellBuffer frame = Render(host);
            if (!shell.LastFocusedBox.IsEmpty) problems.Add(label + ": a pane box " + shell.LastFocusedBox + " is focused behind the dialog");
            host.App.Modals.Render(new BufferSurface(frame));
            Dump(frame, label);
            if (!(host.App.Modals.Top is Armada.Tui.Modals.ArmadaDialog dialog))
            {
                problems.Add(label + ": the top modal is not an Armada dialog");
                return problems;
            }

            Rect box = dialog.FrameBounds;
            if (box.IsEmpty)
            {
                problems.Add(label + ": dialog box not measured");
                return problems;
            }

            HashSet<Point> perimeter = new HashSet<Point>(Perimeter(box));
            foreach (Point corner in new Point[] { new Point(box.X, box.Y), new Point(box.Right - 1, box.Y), new Point(box.X, box.Bottom - 1), new Point(box.Right - 1, box.Bottom - 1) })
            {
                if (!IsFocusedBorder(frame.Get(corner.X, corner.Y), shell.Theme)) problems.Add(label + ": dialog corner " + corner.X + "," + corner.Y + " not focused (\"" + frame.Get(corner.X, corner.Y).Grapheme + "\")");
            }

            for (int y = box.Y + 1; y < box.Bottom - 1; y++)
            {
                if (!IsFocusedBorder(frame.Get(box.X, y), shell.Theme) || !IsFocusedBorder(frame.Get(box.Right - 1, y), shell.Theme))
                {
                    problems.Add(label + ": dialog side not focused at row " + y);
                    break;
                }
            }

            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    if (!perimeter.Contains(new Point(x, y)) && IsFocusedBorder(frame.Get(x, y), shell.Theme))
                    {
                        problems.Add(label + ": focused border outside the dialog at " + x + "," + y);
                        return problems;
                    }
                }
            }

            return problems;
        }

        /// <summary>
        /// Walk a route's Tab stops (forward, wrapping through the other panes) and check each one.
        /// </summary>
        /// <param name="host">Host on the route.</param>
        /// <param name="label">Label.</param>
        /// <param name="visited">Receives a description of each main-pane stop visited.</param>
        /// <returns>Problems.</returns>
        public static List<string> Sweep(TuiTestHost host, string label, List<string>? visited = null)
        {
            List<string> problems = new List<string>();
            ShellView shell = host.Tui.Shell;
            HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (int step = 0; step < 48; step++)
            {
                host.Pump();
                if (host.Tui.Context.Modals.IsModalOpen)
                {
                    // A dialog holds focus (an error report on a route without data): its box is the focused one.
                    problems.AddRange(CheckModal(host, label + " step " + step + " dialog"));
                    host.Press("esc");
                    continue;
                }

                IWidget? leaf = shell.FocusedLeaf();
                bool inMain = ReferenceEquals(shell.Scope.Focused, shell.Screen);
                if (leaf != null && !seen.Add(leaf) && inMain) break;
                problems.AddRange(Check(host, label + " step " + step + " [" + Describe(leaf) + "]"));
                if (inMain && visited != null) visited.Add(Describe(leaf));
                host.Press("tab");
            }

            return problems;
        }

        /// <summary>
        /// The cells covered by box titles (see <see cref="ArmadaWidget.BoxTitle"/>).
        /// </summary>
        /// <param name="regions">Regions.</param>
        /// <param name="pane">Pane rectangle.</param>
        /// <returns>Cells.</returns>
        public static HashSet<Point> TitleCells(IReadOnlyList<RegionFrame> regions, Rect pane)
        {
            HashSet<Point> cells = new HashSet<Point>();
            foreach (RegionFrame r in regions)
            {
                if (r.Title == null) continue;
                Rect box = r.Box.Intersect(pane);
                if (box.Width < 8) continue;
                int width = Armada.Tui.Text.TextCells.Width(Armada.Tui.Text.TextCells.Truncate(" " + r.Title + " ", box.Width - 4));
                for (int x = 0; x < width; x++) cells.Add(new Point(box.X + 2 + x, box.Y));
            }

            return cells;
        }

        /// <summary>
        /// A short description of a widget (type name).
        /// </summary>
        /// <param name="widget">Widget.</param>
        /// <returns>Text.</returns>
        public static string Describe(IWidget? widget)
        {
            if (widget == null) return "(none)";
            string name = widget.GetType().Name;
            int tick = name.IndexOf('`');
            return tick > 0 ? name.Substring(0, tick) : name;
        }

        /// <summary>
        /// The deepest known rectangle of the focused widget (following placements down the focus path), in shell
        /// coordinates, or empty.
        /// </summary>
        /// <param name="shell">Shell.</param>
        /// <param name="mainInner">Main content rectangle.</param>
        /// <returns>Rectangle.</returns>
        public static Rect LeafRect(ShellView shell, Rect mainInner)
        {
            if (shell.Screen == null) return Rect.Empty;
            FocusScope scope = shell.Screen.Scope;
            int ox = mainInner.X;
            int oy = mainInner.Y;
            Rect result = Rect.Empty;
            for (int depth = 0; depth < 16; depth++)
            {
                IWidget? child = scope.Focused;
                if (child == null) break;
                Rect r = scope.RectOf(child);
                if (r.IsEmpty) break;
                result = r.Offset(ox, oy);
                if (!(child is IFocusScopeOwner owner)) break;
                scope = owner.Scope;
                ox = result.X;
                oy = result.Y;
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static readonly HashSet<string> _Dumped = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Write a checked frame (text, then one style letter per cell and the style legend) to the directory named by
        /// <c>ARMADA_TUI_SWEEP_DUMP</c>, so the frames of two builds can be compared cell by cell. Does nothing when the
        /// variable is unset.
        /// </summary>
        /// <param name="frame">Frame.</param>
        /// <param name="label">Check label (the file name).</param>
        private static void Dump(CellBuffer frame, string label)
        {
            string? dir = Environment.GetEnvironmentVariable("ARMADA_TUI_SWEEP_DUMP");
            if (String.IsNullOrEmpty(dir)) return;
            StringBuilder name = new StringBuilder();
            foreach (char c in label) name.Append(Char.IsLetterOrDigit(c) || c == '-' || c == '@' ? c : '_');
            string file;
            lock (_Dumped)
            {
                string baseName = name.ToString();
                string candidate = baseName;
                for (int n = 2; !_Dumped.Add(candidate); n++) candidate = baseName + "-" + n;
                file = candidate;
            }

            List<string> styles = new List<string>();
            StringBuilder text = new StringBuilder();
            StringBuilder grid = new StringBuilder();
            for (int y = 0; y < frame.Height; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    Cell cell = frame.Get(x, y);
                    if (!cell.IsContinuation) text.Append(String.IsNullOrEmpty(cell.Grapheme) ? " " : cell.Grapheme);
                    string key = cell.Style.Foreground + "/" + cell.Style.Background + "/" + (int)cell.Style.Attributes;
                    int index = styles.IndexOf(key);
                    if (index < 0)
                    {
                        styles.Add(key);
                        index = styles.Count - 1;
                    }

                    grid.Append((char)(index < 26 ? 'a' + index : (index < 52 ? 'A' + index - 26 : '0' + Math.Min(9, index - 52))));
                }

                text.Append('\n');
                grid.Append('\n');
            }

            StringBuilder legend = new StringBuilder();
            for (int i = 0; i < styles.Count; i++) legend.Append(i).Append(' ').Append(styles[i]).Append('\n');
            Directory.CreateDirectory(dir!);
            File.WriteAllText(Path.Combine(dir!, file + ".txt"), text + "\n" + grid + "\n" + legend);
        }

        #endregion
    }
}
