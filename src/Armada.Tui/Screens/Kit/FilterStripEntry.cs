namespace Armada.Tui.Screens.Kit
{
    using System;
    using TUIKit.Widgets;

    /// <summary>
    /// One labeled field in a <see cref="FilterStrip"/>.
    /// </summary>
    public class FilterStripEntry
    {
        #region Public-Members

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Field widget.
        /// </summary>
        public IWidget Field { get; }

        /// <summary>
        /// Field width in cells.
        /// </summary>
        public int Width
        {
            get { return _Width; }
            set { _Width = Math.Clamp(value, 3, 120); }
        }

        #endregion

        #region Private-Members

        private int _Width = 16;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="field">Field.</param>
        /// <param name="width">Field width in cells.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="field"/> is null.</exception>
        public FilterStripEntry(string label, IWidget field, int width)
        {
            Label = label ?? "";
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Width = width;
        }

        #endregion
    }
}
