namespace Armada.Tui.Modals
{
    using System;
    using Armada.Tui.Services;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Shows a viewer widget (JSON, Markdown, log, diff, chart, copied text) in a large dialog. Keys go to the viewer;
    /// <c>y</c> invokes <see cref="CopyRequested"/>; Esc or <c>q</c> closes. Not thread-safe.
    /// </summary>
    public class ViewerModal : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// The hosted viewer.
        /// </summary>
        public ArmadaWidget Viewer { get; }

        /// <summary>
        /// Width as a fraction of the screen (0.3..1.0). Default 0.85.
        /// </summary>
        public double WidthRatio
        {
            get { return _WidthRatio; }
            set { _WidthRatio = Math.Clamp(value, 0.3, 1.0); }
        }

        /// <summary>
        /// Height as a fraction of the screen (0.3..1.0). Default 0.8.
        /// </summary>
        public double HeightRatio
        {
            get { return _HeightRatio; }
            set { _HeightRatio = Math.Clamp(value, 0.3, 1.0); }
        }

        /// <summary>
        /// Raised when the user presses <c>y</c>.
        /// </summary>
        public event EventHandler? CopyRequested;

        #endregion

        #region Private-Members

        private double _WidthRatio = 0.85;
        private double _HeightRatio = 0.8;
        private int _ScreenWidth = 80;
        private int _ScreenHeight = 24;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="viewer">Viewer widget.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="viewer"/> is null.</exception>
        public ViewerModal(string title, ArmadaWidget viewer, ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
            : base(title, localizer, theme)
        {
            Viewer = viewer ?? throw new ArgumentNullException(nameof(viewer));
            Viewer.Localizer = Localizer;
            Viewer.ApplyTheme(Theme);
            Viewer.OnFocusChanged(true);
            FooterHint = " y " + T("Copy") + "  Esc " + T("Close") + " ";
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            if (Viewer.HandleKey(key)) return true;
            if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None)
            {
                if (key.Rune == 'q')
                {
                    RequestClose(null);
                    return true;
                }

                if (key.Rune == 'y')
                {
                    EventHandler? handler = CopyRequested;
                    if (handler != null) handler(this, EventArgs.Empty);
                    return true;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _ScreenWidth = surface.Size.Width;
            _ScreenHeight = surface.Size.Height;
            base.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Max(20, Math.Min(availableWidth, (int)(_ScreenWidth * _WidthRatio)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Max(5, (int)(_ScreenHeight * _HeightRatio) - 4);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            Viewer.Render(content);
        }

        #endregion
    }
}
