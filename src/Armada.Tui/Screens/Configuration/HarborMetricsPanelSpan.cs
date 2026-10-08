namespace Armada.Tui.Screens.Configuration
{
    using System;
    using TUIKit;

    /// <summary>
    /// A run of text in one style on a line of the <see cref="HarborMetricsPanel"/>.
    /// </summary>
    public class HarborMetricsPanelSpan
    {
        #region Public-Members

        /// <summary>
        /// Text (already translated).
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Style.
        /// </summary>
        public CellStyle Style { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="style">Style.</param>
        public HarborMetricsPanelSpan(string text, CellStyle style)
        {
            Text = text ?? String.Empty;
            Style = style;
        }

        #endregion
    }
}
