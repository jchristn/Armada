namespace Armada.Tui.Widgets
{
    using System;
    using TUIKit.Widgets;

    /// <summary>
    /// One row of a <see cref="FormView"/>: a labeled field, or a section heading.
    /// </summary>
    public class FormRow
    {
        #region Public-Members

        /// <summary>
        /// English label (or section title).
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Field widget, or null for a section heading.
        /// </summary>
        public IWidget? Field { get; }

        /// <summary>
        /// English hint shown under the field, or null.
        /// </summary>
        public string? Hint { get; set; }

        /// <summary>
        /// True for a section heading.
        /// </summary>
        public bool IsSection
        {
            get { return Field == null; }
        }

        /// <summary>
        /// Height of the field in rows. Default 1.
        /// </summary>
        public int Height
        {
            get { return _Height; }
            set { _Height = Math.Clamp(value, 1, 50); }
        }

        #endregion

        #region Private-Members

        private int _Height = 1;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="field">Field, or null for a section.</param>
        /// <param name="hint">Hint, or null.</param>
        public FormRow(string label, IWidget? field, string? hint = null)
        {
            Label = label ?? "";
            Field = field;
            Hint = hint;
        }

        #endregion
    }
}
