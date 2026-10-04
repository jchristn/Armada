namespace Armada.Tui.Text
{
    using System;
    using TUIKit;

    /// <summary>
    /// Drawing helpers over a TUIKit <see cref="ISurface"/> that clip to a cell budget using <see cref="TextCells"/>.
    /// Thread-safe (stateless); surfaces themselves are not.
    /// </summary>
    public static class SurfaceText
    {
        #region Public-Methods

        /// <summary>
        /// Draw text at (x, y) clipped to <paramref name="maxWidth"/> cells (truncated with an ellipsis).
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <param name="text">Text; null draws nothing.</param>
        /// <param name="style">Style.</param>
        /// <param name="maxWidth">Maximum cells; values below 1 draw nothing.</param>
        /// <returns>Cells drawn.</returns>
        public static int Draw(ISurface surface, int x, int y, string? text, CellStyle style, int maxWidth)
        {
            if (surface == null || String.IsNullOrEmpty(text) || maxWidth < 1) return 0;
            if (y < 0 || y >= surface.Size.Height || x >= surface.Size.Width) return 0;
            int budget = Math.Min(maxWidth, surface.Size.Width - Math.Max(0, x));
            string fitted = TextCells.Truncate(text, budget);
            surface.DrawText(x, y, fitted, style);
            return TextCells.Width(fitted);
        }

        /// <summary>
        /// Fill a row segment with spaces in a style.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <param name="width">Cells.</param>
        /// <param name="style">Style.</param>
        public static void FillRow(ISurface surface, int x, int y, int width, CellStyle style)
        {
            if (surface == null || width < 1 || y < 0 || y >= surface.Size.Height) return;
            int start = Math.Max(0, x);
            int w = Math.Min(width - (start - x), surface.Size.Width - start);
            if (w < 1) return;
            surface.Fill(new Rect(start, y, w, 1), Cell.Blank(style));
        }

        /// <summary>
        /// Fill a rectangle with spaces in a style, clipped to the surface.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="rect">Rectangle.</param>
        /// <param name="style">Style.</param>
        public static void FillRect(ISurface surface, Rect rect, CellStyle style)
        {
            if (surface == null) return;
            Rect clipped = rect.Intersect(new Rect(0, 0, surface.Size.Width, surface.Size.Height));
            if (clipped.IsEmpty) return;
            surface.Fill(clipped, Cell.Blank(style));
        }

        #endregion
    }
}
