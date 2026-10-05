namespace Armada.Tui.Screens.Admin
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// One paragraph of setup wizard text: already-translated text and a style.
    /// </summary>
    public class SetupWizardLine
    {
        #region Public-Members

        /// <summary>
        /// Text (already translated).
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Style, or null for the body style.
        /// </summary>
        public Func<ArmadaTheme, CellStyle>? Style { get; }

        /// <summary>
        /// Indent in cells.
        /// </summary>
        public int Indent { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text (already translated).</param>
        /// <param name="style">Style, or null.</param>
        /// <param name="indent">Indent in cells.</param>
        public SetupWizardLine(string text, Func<ArmadaTheme, CellStyle>? style = null, int indent = 0)
        {
            Text = text ?? "";
            Style = style;
            Indent = Math.Clamp(indent, 0, 20);
        }

        #endregion
    }
}
