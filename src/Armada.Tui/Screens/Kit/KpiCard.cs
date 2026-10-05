namespace Armada.Tui.Screens.Kit
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// One KPI card in a <see cref="KpiBar"/>: an English label, a formatted value, an optional detail line, and an
    /// optional value style.
    /// </summary>
    public class KpiCard
    {
        #region Public-Members

        /// <summary>
        /// English label.
        /// </summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// Value text (already formatted).
        /// </summary>
        public string Value { get; set; } = "";

        /// <summary>
        /// Detail text (already translated), or empty.
        /// </summary>
        public string Detail { get; set; } = "";

        /// <summary>
        /// Style for the value, or null for the accent style.
        /// </summary>
        public Func<ArmadaTheme, CellStyle>? Style { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Value text.</param>
        /// <param name="style">Value style, or null.</param>
        /// <param name="detail">Detail text, or empty.</param>
        public KpiCard(string label, string value, Func<ArmadaTheme, CellStyle>? style = null, string detail = "")
        {
            Label = label ?? "";
            Value = value ?? "";
            Style = style;
            Detail = detail ?? "";
        }

        #endregion
    }
}
