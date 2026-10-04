namespace Armada.Tui.Services
{
    using System;
    using Armada.Tui.Modals;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit.Content;

    /// <summary>
    /// Copies IDs, tokens, JSON, curl snippets, and diffs with OSC 52 (works over SSH). When the terminal does not
    /// advertise OSC 52 the text is shown in a "copy manually" dialog instead. Call on the UI loop thread.
    /// </summary>
    public class ClipboardService
    {
        #region Public-Members

        /// <summary>
        /// Last copied text (diagnostics and tests).
        /// </summary>
        public string? LastCopied { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly ITerminalOutput _Terminal;
        private readonly IModalHost? _Modals;
        private readonly NotificationService? _Notifications;
        private readonly ITextLocalizer _Loc;
        private readonly Func<ArmadaTheme> _Theme;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="terminal">Terminal.</param>
        /// <param name="modals">Modal host for the fallback, or null.</param>
        /// <param name="notifications">Notifications for the confirmation toast, or null.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Current palette provider.</param>
        public ClipboardService(ITerminalOutput terminal, IModalHost? modals, NotificationService? notifications, ITextLocalizer localizer, Func<ArmadaTheme> theme)
        {
            _Terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
            _Modals = modals;
            _Notifications = notifications;
            _Loc = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Copy text.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="label">English description for the toast, for example ID.</param>
        /// <returns>True when sent with OSC 52; false when the manual fallback was shown.</returns>
        public bool Copy(string text, string label = "Text")
        {
            LastCopied = text ?? "";
            if (_Terminal.SupportsClipboard)
            {
                _Terminal.Write(ClipboardWriter.BuildSequence(LastCopied));
                _Notifications?.Toast(NotificationSeverityEnum.Success, _Loc.T("Copied") + ": " + _Loc.T(label));
                return true;
            }

            if (_Modals != null)
            {
                ViewerModal modal = new ViewerModal("Copy manually (F12 releases the mouse for terminal selection)", new JsonOrTextViewer(LastCopied), _Loc, _Theme());
                _Modals.Show(modal);
            }

            return false;
        }

        #endregion
    }
}
