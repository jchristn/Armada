namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// A column of an <see cref="ArmadaGrid{T}"/>: fixed or proportional width, alignment, value and per-cell style
    /// selectors, server sort key, and chooser flags.
    /// </summary>
    /// <typeparam name="T">Row type.</typeparam>
    public class GridColumn<T>
    {
        #region Public-Members

        /// <summary>
        /// Stable key (persisted in table preferences).
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// English title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Fixed width in cells, or null for proportional.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Share of the remaining width for proportional columns. Default 1; clamped to 1..100.
        /// </summary>
        public int Weight
        {
            get { return _Weight; }
            set { _Weight = Math.Clamp(value, 1, 100); }
        }

        /// <summary>
        /// Minimum width in cells. Default 4; clamped to 1..200.
        /// </summary>
        public int MinWidth
        {
            get { return _MinWidth; }
            set { _MinWidth = Math.Clamp(value, 1, 200); }
        }

        /// <summary>
        /// Alignment. Default left.
        /// </summary>
        public CellAlignment Align { get; set; } = CellAlignment.Left;

        /// <summary>
        /// Cell text.
        /// </summary>
        public Func<T, string> Value { get; }

        /// <summary>
        /// Optional per-cell style (status badges); null uses the row style.
        /// </summary>
        public Func<T, ArmadaTheme, CellStyle?>? Style { get; set; } = null;

        /// <summary>
        /// Column can be sorted.
        /// </summary>
        public bool Sortable { get; set; } = false;

        /// <summary>
        /// Server sort field, or null to use <see cref="Key"/>.
        /// </summary>
        public string? SortKey { get; set; } = null;

        /// <summary>
        /// Pinned columns cannot be hidden in the column chooser.
        /// </summary>
        public bool Pinned { get; set; } = false;

        /// <summary>
        /// Visible before the user customizes columns. Default true.
        /// </summary>
        public bool DefaultVisible { get; set; } = true;

        /// <summary>
        /// Identifier column (IDs): shrinks with middle elision and is the first column dropped when the table is
        /// narrow, so names and titles keep their room. Null (default) detects it: key <c>id</c> or a title ending in
        /// <c>ID</c>.
        /// </summary>
        public bool? Identifier { get; set; } = null;

        /// <summary>
        /// The table's main column (title or name): it gets a comfortable share of the width before other columns
        /// grow. Null (default) detects it: the first pinned proportional column, otherwise the proportional column
        /// with the largest weight.
        /// </summary>
        public bool? Primary { get; set; } = null;

        /// <summary>
        /// True when this is an identifier column (explicit or detected).
        /// </summary>
        public bool IsIdentifier
        {
            get
            {
                if (Identifier.HasValue) return Identifier.Value;
                return String.Equals(Key, "id", StringComparison.OrdinalIgnoreCase)
                    || (Title ?? "").EndsWith("ID", StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Effective sort key.
        /// </summary>
        public string EffectiveSortKey
        {
            get { return SortKey ?? Key; }
        }

        #endregion

        #region Private-Members

        private int _Weight = 1;
        private int _MinWidth = 4;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="title">English title.</param>
        /// <param name="value">Cell text selector.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> or <paramref name="value"/> is null.</exception>
        public GridColumn(string key, string title, Func<T, string> value)
        {
            Key = String.IsNullOrEmpty(key) ? throw new ArgumentNullException(nameof(key)) : key;
            Title = title ?? key;
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        #endregion
    }
}
