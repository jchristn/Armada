namespace Armada.Tui.Widgets
{
    using System;
    using TUIKit.Widgets;

    /// <summary>
    /// One filter of a <see cref="FilterBar"/>.
    /// </summary>
    public class FilterBarItem
    {
        #region Public-Members

        /// <summary>
        /// Filter key.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// English label drawn before the field, or empty.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Field widget.
        /// </summary>
        public IWidget Field { get; }

        /// <summary>
        /// Field width in cells (clamped to 4..120).
        /// </summary>
        public int Width
        {
            get { return _Width; }
            set { _Width = Math.Clamp(value, 4, 120); }
        }

        #endregion

        #region Private-Members

        private int _Width = 20;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="label">English label or empty.</param>
        /// <param name="field">Field.</param>
        /// <param name="width">Width.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="field"/> is null.</exception>
        public FilterBarItem(string key, string label, IWidget field, int width)
        {
            Key = key ?? "";
            Label = label ?? "";
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Width = width;
        }

        #endregion
    }
}
