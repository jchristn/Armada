namespace Test.Shared.Suites.Tui
{
    using TUIKit;

    /// <summary>
    /// One cell of a pane border as read back from a rendered frame (see <see cref="TuiFocusBorderSuite"/>).
    /// </summary>
    internal sealed class TuiBorderCell
    {
        #region Public-Members

        /// <summary>
        /// Column.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// Row.
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// Glyph drawn in the cell.
        /// </summary>
        public string Glyph { get; set; } = "";

        /// <summary>
        /// Style of the cell.
        /// </summary>
        public CellStyle Style { get; set; } = CellStyle.Default;

        /// <summary>
        /// True when the cell is drawn as a focused border.
        /// </summary>
        public bool Focused { get; set; }

        #endregion
    }
}
