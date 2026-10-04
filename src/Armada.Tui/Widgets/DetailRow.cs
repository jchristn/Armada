namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// One row of a <see cref="DetailView"/>.
    /// </summary>
    public class DetailRow
    {
        #region Public-Members

        /// <summary>
        /// English label or section title.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Value, or null.
        /// </summary>
        public string? Value { get; }

        /// <summary>
        /// Value style selector, or null.
        /// </summary>
        public Func<ArmadaTheme, CellStyle>? Style { get; }

        /// <summary>
        /// True for a section heading.
        /// </summary>
        public bool IsSection { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">Label.</param>
        /// <param name="value">Value.</param>
        /// <param name="style">Style selector.</param>
        /// <param name="isSection">Section heading.</param>
        public DetailRow(string label, string? value, Func<ArmadaTheme, CellStyle>? style, bool isSection)
        {
            Label = label ?? "";
            Value = value;
            Style = style;
            IsSection = isSection;
        }

        #endregion
    }
}
