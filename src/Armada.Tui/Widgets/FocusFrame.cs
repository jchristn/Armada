namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// Draws the focus border of a region (a shell pane: sidebar, main screen, or Ask dock). The border cells are
    /// always reserved, focused or not, so the layout never moves when focus does. The region that holds focus draws
    /// its border in <see cref="ArmadaTheme.FocusBorder"/> with heavy lines (ASCII <c>+ - |</c> when the palette uses
    /// ASCII borders or glyphs); other regions draw a light line in <see cref="ArmadaTheme.Border"/>. When focus is
    /// in a sub-region of the pane (a grid, a filter row, the Ask composer), only the stretches of the pane border that
    /// the sub-region touches are drawn as focused, so exactly one region stands out. Below three rows or columns the
    /// box falls back to a one-column left gutter bar. Stateless and thread-safe.
    /// </summary>
    public static class FocusFrame
    {
        #region Public-Members

        /// <summary>
        /// Light (unfocused) Unicode glyphs: horizontal, vertical, top-left, top-right, bottom-left, bottom-right.
        /// </summary>
        public const string LightGlyphs = "\u2500\u2502\u250C\u2510\u2514\u2518";

        /// <summary>
        /// Heavy (focused) Unicode glyphs: horizontal, vertical, top-left, top-right, bottom-left, bottom-right.
        /// </summary>
        public const string HeavyGlyphs = "\u2501\u2503\u250F\u2513\u2517\u251B";

        /// <summary>
        /// ASCII glyphs (focused and unfocused alike; the style tells them apart): horizontal, vertical, corners.
        /// </summary>
        public const string AsciiGlyphs = "-|++++";

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
        /// The glyphs a palette draws focused or unfocused borders with.
        /// </summary>
        /// <param name="theme">Palette.</param>
        /// <param name="focused">Focused border.</param>
        /// <returns>Six glyphs: horizontal, vertical, top-left, top-right, bottom-left, bottom-right.</returns>
        public static string GlyphsFor(ArmadaTheme theme, bool focused)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            if (theme.AsciiBorders || theme.AsciiGlyphs) return AsciiGlyphs;
            return focused ? HeavyGlyphs : LightGlyphs;
        }

        /// <summary>
        /// Draw the border of a region around its content (call after the content so the border is never covered).
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="outer">Rectangle including the border.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="focused">True when focus is in this region.</param>
        /// <param name="subRegion">The focused sub-region in content coordinates (relative to <see cref="Inner"/>), or
        /// <see cref="Rect.Empty"/> when the region itself is the focus target. Only the border stretches the
        /// sub-region touches are drawn as focused; a sub-region that touches no edge highlights the whole border.</param>
        public static void Draw(ISurface surface, Rect outer, ArmadaTheme theme, bool focused, Rect subRegion)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            FocusFrameKindEnum kind = KindFor(outer);
            if (kind == FocusFrameKindEnum.None) return;
            if (kind == FocusFrameKindEnum.Gutter)
            {
                string bar = focused ? GlyphsFor(theme, true).Substring(1, 1) : " ";
                CellStyle style = focused ? theme.FocusBorder : theme.Text;
                for (int y = outer.Y; y < outer.Bottom; y++) surface.DrawText(outer.X, y, bar, style);
                return;
            }

            Rect inner = Inner(outer);
            bool all = focused && subRegion.IsEmpty;
            bool top = false;
            bool bottom = false;
            bool left = false;
            bool right = false;
            if (focused && !subRegion.IsEmpty)
            {
                top = subRegion.Y <= 0;
                bottom = subRegion.Bottom >= inner.Height;
                left = subRegion.X <= 0;
                right = subRegion.Right >= inner.Width;
                if (!top && !bottom && !left && !right) all = true;
            }

            for (int c = 0; c < inner.Width; c++)
            {
                bool inSpan = all || (c >= subRegion.X && c < subRegion.Right);
                Put(surface, inner.X + c, outer.Y, 0, all || (top && inSpan), theme);
                Put(surface, inner.X + c, outer.Bottom - 1, 0, all || (bottom && inSpan), theme);
            }

            for (int r = 0; r < inner.Height; r++)
            {
                bool inSpan = all || (r >= subRegion.Y && r < subRegion.Bottom);
                Put(surface, outer.X, inner.Y + r, 1, all || (left && inSpan), theme);
                Put(surface, outer.Right - 1, inner.Y + r, 1, all || (right && inSpan), theme);
            }

            bool firstCol = subRegion.X <= 0;
            bool lastCol = subRegion.Right >= inner.Width;
            bool firstRow = subRegion.Y <= 0;
            bool lastRow = subRegion.Bottom >= inner.Height;
            Put(surface, outer.X, outer.Y, 2, all || (top && left && firstCol && firstRow), theme);
            Put(surface, outer.Right - 1, outer.Y, 3, all || (top && right && lastCol && firstRow), theme);
            Put(surface, outer.X, outer.Bottom - 1, 4, all || (bottom && left && firstCol && lastRow), theme);
            Put(surface, outer.Right - 1, outer.Bottom - 1, 5, all || (bottom && right && lastCol && lastRow), theme);
        }

        #endregion

        #region Private-Methods

        private static void Put(ISurface surface, int x, int y, int glyph, bool focused, ArmadaTheme theme)
        {
            string glyphs = GlyphsFor(theme, focused);
            surface.DrawText(x, y, glyphs.Substring(glyph, 1), focused ? theme.FocusBorder : theme.Border);
        }

        #endregion
    }
}
