namespace Armada.Tui.Screens.Operations
{
    using System;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// One line of an <see cref="OpsLinkList"/>: text, an optional right-aligned note, a style, and an optional action.
    /// </summary>
    public class OpsLinkItem
    {
        #region Public-Members

        /// <summary>
        /// Text (translated).
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Right-aligned note, or empty.
        /// </summary>
        public string Note { get; set; } = "";

        /// <summary>
        /// Text style, or null for the text style.
        /// </summary>
        public Func<ArmadaTheme, CellStyle>? Style { get; set; } = null;

        /// <summary>
        /// Action on Enter or click, or null.
        /// </summary>
        public Action? Action { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="action">Action, or null.</param>
        /// <param name="style">Style, or null.</param>
        /// <param name="note">Note.</param>
        public OpsLinkItem(string text, Action? action = null, Func<ArmadaTheme, CellStyle>? style = null, string note = "")
        {
            Text = text ?? "";
            Action = action;
            Style = style;
            Note = note ?? "";
        }

        #endregion
    }
}
