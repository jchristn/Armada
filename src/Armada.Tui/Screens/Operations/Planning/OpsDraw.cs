namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Draws styled lines (for example an <see cref="OpsDocument"/>) into a fixed area with wrapping.
    /// </summary>
    public static class OpsDraw
    {
        #region Public-Methods

        /// <summary>
        /// Count the rows lines take at a width.
        /// </summary>
        /// <param name="lines">Lines.</param>
        /// <param name="width">Width.</param>
        /// <returns>Rows.</returns>
        public static int Rows(IEnumerable<StyledText> lines, int width)
        {
            int rows = 0;
            foreach (StyledText line in lines) rows += line.Width <= width ? 1 : TextWrapper.Wrap(line, Math.Max(1, width)).Count;
            return rows;
        }

        /// <summary>
        /// Draw lines wrapped at a width, stopping at a row limit.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="x">Left.</param>
        /// <param name="y">Top.</param>
        /// <param name="lines">Lines.</param>
        /// <param name="width">Width.</param>
        /// <param name="maxRows">Row limit.</param>
        /// <param name="baseStyle">Base style.</param>
        /// <returns>Rows drawn.</returns>
        public static int Lines(ISurface surface, int x, int y, IEnumerable<StyledText> lines, int width, int maxRows, CellStyle baseStyle)
        {
            int drawn = 0;
            foreach (StyledText line in lines)
            {
                IReadOnlyList<StyledText> rows = line.Width <= width ? new List<StyledText> { line } : TextWrapper.Wrap(line, Math.Max(1, width));
                foreach (StyledText row in rows)
                {
                    if (drawn >= maxRows) return drawn;
                    surface.DrawStyledText(x, y + drawn, row, baseStyle);
                    drawn++;
                }
            }

            return drawn;
        }

        #endregion
    }
}
