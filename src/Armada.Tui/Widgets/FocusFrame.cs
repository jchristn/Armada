namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// The one focus treatment of the TUI: every focusable pane (a shell pane such as the sidebar or the Ask dock, and
    /// every focus region inside a screen, see <see cref="RegionFrames"/>) sits in a one-cell box. The box around the
    /// pane that holds keyboard focus is drawn whole in <see cref="ArmadaTheme.FocusBorder"/> with heavy lines
    /// (<c>&#x250F; &#x2501; &#x2503;</c>, or <c>#</c> and <c>=</c> when the palette uses ASCII borders or glyphs), so the
    /// focused pane reads by shape as well as by color; every other box is a light line (<c>&#x250C; &#x2500; &#x2502;</c>
    /// or <c>+ - |</c>) in <see cref="ArmadaTheme.Border"/>, joined with tees where boxes share an edge. Box cells are
    /// always reserved, focused or not, so the layout never moves when focus does. Below three rows or columns a box
    /// falls back to a one-column left gutter bar (<c>&#x2503;</c>, or <c>#</c> in ASCII), and a rectangle one column wide
    /// keeps every column for content. The drawing and the geometry are TUIKit's: <see cref="TUIKit.Widgets.FocusFrame"/>
    /// with Armada's <see cref="FocusFrameOptions"/> for a box or gutter and for <see cref="ContentRect"/>, <see cref="OuterRect"/>, and
    /// <see cref="UsesGutter"/>, and <c>SurfaceExtensions.DrawJoinedBox</c> for the shared light lines and their tees.
    /// The focused box is drawn over its joined neighbours as a plain box (<see cref="JoinMode.None"/>: heavy corners,
    /// not TUIKit's default heavy junctions), the look the TUI shipped with; this class adds Armada's palette and the
    /// left-aligned titles, which are drawn after every box so no box line covers one. Stateless and thread-safe.
    /// </summary>
    public static class FocusFrame
    {
        #region Public-Members

        /// <summary>
        /// Light (unfocused) Unicode glyphs: horizontal, vertical, top-left, top-right, bottom-left, bottom-right.
        /// </summary>
        public const string LightGlyphs = "\u2500\u2502\u250C\u2510\u2514\u2518";

        /// <summary>
        /// Light Unicode junction glyphs where boxes share an edge: tee right, tee left, tee down, tee up, cross.
        /// </summary>
        public const string LightJunctions = "\u251C\u2524\u252C\u2534\u253C";

        /// <summary>
        /// Heavy (focused) Unicode glyphs: horizontal, vertical, top-left, top-right, bottom-left, bottom-right.
        /// </summary>
        public const string HeavyGlyphs = "\u2501\u2503\u250F\u2513\u2517\u251B";

        /// <summary>
        /// ASCII unfocused glyphs: horizontal, vertical, corners (junctions are <c>+</c> too).
        /// </summary>
        public const string AsciiGlyphs = "-|++++";

        /// <summary>
        /// ASCII focused glyphs: horizontal, vertical, corners. Different characters from the unfocused set, so the
        /// focused pane is told apart without color.
        /// </summary>
        public const string AsciiFocusedGlyphs = "=#####";

        #endregion

        #region Private-Members

        private static readonly FocusFrameOptions _Options = CreateOptions();

        #endregion

        #region Public-Methods

        /// <summary>
        /// The content rectangle inside a frame (TUIKit's <see cref="TUIKit.Widgets.FocusFrame.ContentRect"/>): inset by
        /// one cell for a box, minus the first column for the gutter, the whole rectangle when it is one column wide.
        /// </summary>
        /// <param name="outer">Rectangle including the border.</param>
        /// <returns>Content rectangle (empty for an empty input).</returns>
        public static Rect ContentRect(Rect outer)
        {
            return TUIKit.Widgets.FocusFrame.ContentRect(outer, _Options);
        }

        /// <summary>
        /// The box around a content rectangle, one cell larger on every side (TUIKit's
        /// <see cref="TUIKit.Widgets.FocusFrame.OuterRect"/>).
        /// </summary>
        /// <param name="content">Content rectangle.</param>
        /// <returns>Box rectangle; an empty input is returned unchanged.</returns>
        public static Rect OuterRect(Rect content)
        {
            return TUIKit.Widgets.FocusFrame.OuterRect(content, _Options);
        }

        /// <summary>
        /// True when a frame in <paramref name="outer"/> is the one-column gutter rather than a box (TUIKit's
        /// <see cref="TUIKit.Widgets.FocusFrame.UsesGutter"/>).
        /// </summary>
        /// <param name="outer">Rectangle including the border.</param>
        /// <returns>True for the gutter.</returns>
        public static bool UsesGutter(Rect outer)
        {
            return TUIKit.Widgets.FocusFrame.UsesGutter(outer, _Options);
        }

        /// <summary>
        /// The glyphs a palette draws focused or unfocused borders with.
        /// </summary>
        /// <param name="theme">Palette.</param>
        /// <param name="focused">Focused border.</param>
        /// <returns>Six glyphs: horizontal, vertical, top-left, top-right, bottom-left, bottom-right.</returns>
        public static string GlyphsFor(ArmadaTheme theme, bool focused)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (IsAscii(theme)) return focused ? AsciiFocusedGlyphs : AsciiGlyphs;
            return focused ? HeavyGlyphs : LightGlyphs;
        }

        /// <summary>
        /// Every glyph an unfocused border cell may carry in a palette (lines, corners, and junctions).
        /// </summary>
        /// <param name="theme">Palette.</param>
        /// <returns>Glyphs.</returns>
        public static string UnfocusedGlyphsFor(ArmadaTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (IsAscii(theme)) return AsciiGlyphs;
            return LightGlyphs + LightJunctions;
        }

        /// <summary>
        /// The border style the focused frame and the topmost dialog use in a palette (heavy lines, or ASCII
        /// <c>#</c> and <c>=</c>).
        /// </summary>
        /// <param name="theme">Palette.</param>
        /// <returns>Border style.</returns>
        public static BorderStyle FocusedBorderFor(ArmadaTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            return IsAscii(theme) ? BorderStyle.AsciiHeavy : BorderStyle.Thick;
        }

        /// <summary>
        /// Draw the border of a single pane around its content (call after the content so the border is never
        /// covered): the whole box focused or the whole box plain, or the gutter column below three rows or columns.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="outer">Rectangle including the border.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="focused">True when focus is in this pane.</param>
        public static void Draw(ISurface surface, Rect outer, ArmadaTheme theme, bool focused)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            TUIKit.Widgets.FocusFrame.Draw(surface, outer, focused, theme.FocusBorder, theme.Border, IsAscii(theme), _Options);
        }

        /// <summary>
        /// Draw a pane border and the boxes of the regions inside it, sharing edges: unfocused boxes as light lines
        /// joined by tees, then the focused box (the pane itself or one region) whole in the focus style on top.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="pane">The pane rectangle including its border.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="boxes">Region boxes (each one cell larger than its region), clipped to the pane.</param>
        /// <param name="focusedBox">The box to draw focused (the pane or one of <paramref name="boxes"/>), or
        /// <see cref="Rect.Empty"/> when focus is elsewhere.</param>
        public static void DrawNested(ISurface surface, Rect pane, ArmadaTheme theme, IReadOnlyList<Rect> boxes, Rect focusedBox)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (boxes == null) throw new ArgumentNullException(nameof(boxes));
            if (pane.IsEmpty || UsesGutter(pane) || ContentRect(pane) == pane)
            {
                Draw(surface, pane, theme, !focusedBox.IsEmpty);
                return;
            }

            // Plain boxes join the lines already drawn (tees and crosses where they meet); the focused box goes last,
            // drawn whole over the shared lines (FocusedJoinMode).
            BorderStyle plain = IsAscii(theme) ? BorderStyle.Ascii : BorderStyle.Line;
            surface.DrawJoinedBox(pane, theme.Border, plain, null, theme.Border, _Options.UnfocusedJoinMode);
            foreach (Rect box in boxes) surface.DrawJoinedBox(box.Intersect(pane), theme.Border, plain, null, theme.Border, _Options.UnfocusedJoinMode);
            Rect focused = focusedBox.Intersect(pane);
            if (!focused.IsEmpty && !UsesGutter(focused) && ContentRect(focused) != focused)
                surface.DrawJoinedBox(focused, theme.FocusBorder, FocusedBorderFor(theme), null, theme.FocusBorder, _Options.FocusedJoinMode);
        }

        /// <summary>
        /// Draw a title on the top line of a box (after the boxes), in the box's style, two cells in from the corner.
        /// TUIKit draws a left-aligned title (<see cref="TitleAlignment.Left"/>) with the box itself, which in a joined
        /// layout lets a box drawn later (the focused one, below a titled box) cover the title, so Armada draws titles
        /// in a pass of their own.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="box">Box.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="title">Title text.</param>
        /// <param name="focused">True for the focused box.</param>
        /// <returns>The cells the title covers (empty when it does not fit).</returns>
        public static Rect DrawTitle(ISurface surface, Rect box, ArmadaTheme theme, string? title, bool focused)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (String.IsNullOrEmpty(title) || box.Width < 8) return Rect.Empty;
            string text = TextCells.Truncate(" " + title + " ", box.Width - 4);
            int width = TextCells.Width(text);
            surface.DrawText(box.X + 2, box.Y, text, focused ? theme.FocusBorder : theme.Border);
            return new Rect(box.X + 2, box.Y, width, 1);
        }

        #endregion

        #region Private-Methods

        private static bool IsAscii(ArmadaTheme theme)
        {
            return theme.AsciiBorders || theme.AsciiGlyphs;
        }

        private static FocusFrameOptions CreateOptions()
        {
            FocusFrameOptions options = new FocusFrameOptions();
            options.FocusedBorder = BorderStyle.Thick;
            options.UnfocusedBorder = BorderStyle.Line;
            options.TitleMarker = "";
            options.MinimumBoxSize = 3;
            options.MinimumGutterWidth = 2;
            options.FocusedGutterGlyph = HeavyGlyphs.Substring(1, 1);
            options.AsciiFocusedGutterGlyph = AsciiFocusedGlyphs.Substring(1, 1);
            // A pane drawn on its own is a plain box; inside a pane the plain boxes merge into tees, and the focused box
            // is drawn over them as a plain box with heavy corners (JoinMode.None), not TUIKit's default of heavy
            // junctions (OverlayWhole), which would change the approved look.
            options.JoinBorders = false;
            options.UnfocusedJoinMode = JoinMode.Merge;
            options.FocusedJoinMode = JoinMode.None;
            return options;
        }

        #endregion
    }
}
