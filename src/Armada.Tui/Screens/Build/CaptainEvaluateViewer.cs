namespace Armada.Tui.Screens.Build
{
    using System;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Evaluate run viewer: a <see cref="ViewerModal"/> whose <c>c</c> key asks to cancel the run while it runs.
    /// </summary>
    public class CaptainEvaluateViewer : ViewerModal
    {
        #region Public-Members

        /// <summary>
        /// True while the run is queued or running (the <c>c</c> key is live).
        /// </summary>
        public bool CanCancel
        {
            get => _CanCancel;
            set
            {
                _CanCancel = value;
                FooterHint = (value ? " c " + T("Cancel run") : "") + " y " + T("Copy") + "  Esc " + T("Close") + " ";
            }
        }

        /// <summary>
        /// Raised when the operator presses <c>c</c> while <see cref="CanCancel"/> is true.
        /// </summary>
        public event EventHandler? CancelRequested;

        #endregion

        #region Private-Members

        private bool _CanCancel = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="viewer">Viewer widget.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public CaptainEvaluateViewer(string title, ArmadaWidget viewer, ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
            : base(title, viewer, localizer, theme)
        {
            CanCancel = false;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (_CanCancel && key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && key.Rune == 'c')
            {
                EventHandler? handler = CancelRequested;
                if (handler != null) handler(this, EventArgs.Empty);
                return true;
            }

            return base.HandleKey(key);
        }

        #endregion
    }
}
