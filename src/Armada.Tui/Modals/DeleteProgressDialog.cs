namespace Armada.Tui.Modals
{
    using System;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Progress notice matching the dashboard's DeleteProgressDialog: shown from the moment a delete starts until the
    /// server answers, saying the delete may take some time because related records are cleaned up. It cannot be
    /// dismissed (keys, Escape, and mouse clicks are swallowed); <see cref="Finish"/> closes it. Failures are shown
    /// by <see cref="Screens.Kit.DeleteProgress"/> as an error with Retry. Not thread-safe; use from the UI loop.
    /// </summary>
    public class DeleteProgressDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Progress message (translated). Never null.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// True once <see cref="Finish"/> has closed the dialog.
        /// </summary>
        public bool Finished { get; private set; } = false;

        /// <inheritdoc />
        public override bool CanClose
        {
            get { return false; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">Title, already translated (for example "Deleting vessel" or "Deleting 12 missions").</param>
        /// <param name="message">Progress message, already translated.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public DeleteProgressDialog(string title, string message, ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
            : base("", localizer, theme)
        {
            Title = " " + (title ?? "") + " ";
            Message = message ?? "";
            FooterHint = "";
            MinContentWidth = 36;
            MaxContentWidth = 70;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Close the dialog (the delete finished or failed). Safe to call more than once.
        /// </summary>
        public void Finish()
        {
            if (Finished) return;
            Finished = true;
            Close(null);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            return true;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            return true;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(36, Math.Min(70, TextCells.Width(Message) + 2)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return TextCells.Wrap(Message, Math.Max(1, contentWidth)).Count;
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int y = 0;
            foreach (string line in TextCells.Wrap(Message, Math.Max(1, width)))
            {
                SurfaceText.Draw(content, 0, y++, line, Body(), width);
            }
        }

        #endregion
    }
}
