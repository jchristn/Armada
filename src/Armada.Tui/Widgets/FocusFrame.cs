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
    /// falls back to a one-column left gutter bar. The drawing is TUIKit's: <see cref="TUIKit.Widgets.FocusFrame"/> with
    /// <see cref="FocusFrameOptions"/> for a box or gutter, and <c>SurfaceExtensions.DrawJoinedBox</c> for the shared
    /// light lines and their tees; this class adds Armada's palette, box geometry (<see cref="Inner"/>, <see cref="Outer"/>),
    /// left-aligned titles, and the plain-cornered focused box over joined neighbours. Stateless and thread-safe.
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
        /// The marker kind for a rectangle (by size only).
        /// </summary>
        /// <param name="outer">Rectangle including the border.</param>
        /// <returns>Kind.</returns>
        public static FocusFrameKindEnum KindFor(Rect outer)
        {
            if (outer.Width >= 3 && outer.Height >= 3) return FocusFrameKindEnum.Box;
            if (outer.Width >= 2 && outer.Height >= 1) return FocusFrameKindEnum.Gutter;
            return FocusFrameKindEnum.None;
        }

        /// <summary>
        /// The content rectangle inside the border.
        /// </summary>
        /// <param name="outer">Rectangle including the border.</param>
        /// <returns>Content rectangle (the input itself when no marker fits; empty stays empty).</returns>
        public static Rect Inner(Rect outer)
        {
            if (outer.IsEmpty) return outer;
            switch (KindFor(outer))
            {
                case FocusFrameKindEnum.Box:
                    return new Rect(outer.X + 1, outer.Y + 1, outer.Width - 2, outer.Height - 2);
                case FocusFrameKindEnum.Gutter:
                    return new Rect(outer.X + 1, outer.Y, outer.Width - 1, outer.Height);
                default:
                    return outer;
            }
        }

        /// <summary>
        /// The box around a content rectangle (one cell larger on every side).
        /// </summary>
        /// <param name="inner">Content rectangle.</param>
        /// <returns>Box rectangle, or empty for an empty input.</returns>
        public static Rect Outer(Rect inner)
        {
            if (inner.IsEmpty) return Rect.Empty;
            return new Rect(inner.X - 1, inner.Y - 1, inner.Width + 2, inner.Height + 2);
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
            if (theme.AsciiBorders || theme.AsciiGlyphs) return focused ? AsciiFocusedGlyphs : AsciiGlyphs;
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
            if (theme.AsciiBorders || theme.AsciiGlyphs) return AsciiGlyphs;
            return LightGlyphs + LightJunctions;
        }

        /// <summary>
        /// Draw the border of a single pane around its content (call after the content so the border is never
        /// covered): the whole box focused or the whole box plain.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="outer">Rectangle including the border.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="focused">True when focus is in this pane.</param>
        public static void Draw(ISurface surface, Rect outer, ArmadaTheme theme, bool focused)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            FocusFrameKindEnum kind = KindFor(outer);
            if (kind == FocusFrameKindEnum.None) return;
            if (kind == FocusFrameKindEnum.Gutter)
            {
                // The gutter column is cleared whatever the state. TUIKit draws its ASCII gutter as "|", the same glyph as
                // a plain ASCII line, so the focused ASCII gutter keeps the focused vertical glyph instead.
                Rect gutter = new Rect(outer.X, outer.Y, 1, outer.Height);
                surface.Fill(gutter, Cell.Blank(theme.Text));
                if (!focused) return;
                if (IsAscii(theme)) surface.Fill(gutter, Cell.Glyph(AsciiFocusedGlyphs.Substring(1, 1), theme.FocusBorder, 1));
                else TUIKit.Widgets.FocusFrame.Draw(surface, outer, true, theme.FocusBorder, theme.Border, false, _Options);
                return;
            }

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
            if (KindFor(pane) != FocusFrameKindEnum.Box)
            {
                Draw(surface, pane, theme, !focusedBox.IsEmpty);
                return;
            }

            // Plain boxes join the lines already drawn (tees and crosses where they meet); the focused box is drawn
            // last and unjoined, so it reads whole with plain heavy corners over the shared lines.
            BorderStyle plain = IsAscii(theme) ? BorderStyle.Ascii : BorderStyle.Line;
            surface.DrawJoinedBox(pane, theme.Border, plain);
            foreach (Rect box in boxes) surface.DrawJoinedBox(box.Intersect(pane), theme.Border, plain);
            Rect focused = focusedBox.Intersect(pane);
            if (!focused.IsEmpty && KindFor(focused) == FocusFrameKindEnum.Box) Draw(surface, focused, theme, true);
        }

        /// <summary>
        /// Draw a title on the top line of a box (after the boxes), in the box's style, two cells in from the corner.
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
            options.GutterGlyph = HeavyGlyphs.Substring(1, 1);
            options.JoinBorders = false;
            return options;
        }

        #endregion
    }
}
