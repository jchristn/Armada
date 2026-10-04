namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// One overview figure in a <see cref="KpiStrip"/>.
    /// </summary>
    public class KpiItem
    {
        #region Public-Members

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Display value (already formatted).
        /// </summary>
        public string Value { get; }

        /// <summary>
        /// Optional value style; null uses the accent color.
        /// </summary>
        public Func<ArmadaTheme, CellStyle>? Style { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Display value.</param>
        /// <param name="style">Optional value style.</param>
        public KpiItem(string label, string value, Func<ArmadaTheme, CellStyle>? style = null)
        {
            Label = label ?? "";
            Value = value ?? "";
            Style = style;
        }

        #endregion
    }
}
