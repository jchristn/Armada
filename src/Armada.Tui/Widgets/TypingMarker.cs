namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Services;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// The "[typing]" marker a text-entry control (the Ask composer, the dock's input) shows while it has focus, so it
    /// is clear without relying on color that keys type into it and single-key shortcuts wait until focus leaves.
    /// Bold reverse video in the accent color. Stateless and thread-safe.
    /// </summary>
    public static class TypingMarker
    {
        #region Public-Methods

        /// <summary>
        /// The marker text, for example <c>[typing]</c>.
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <returns>Text.</returns>
        public static string Text(ITextLocalizer loc)
        {
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            return "[" + loc.T("typing") + "]";
        }

        /// <summary>
        /// The marker style: bold reverse video, so it reads without color.
        /// </summary>
        /// <param name="theme">Palette.</param>
        /// <returns>Style.</returns>
        public static CellStyle Style(ArmadaTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            return theme.Accent.WithAttribute(CellAttributes.Bold, true).WithAttribute(CellAttributes.Reverse, true);
        }

        #endregion
    }
}
