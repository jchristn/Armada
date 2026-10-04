namespace Armada.Tui.Modals
{
    using System;
    using System.Collections.Generic;
    using Armada.Client;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Error dialog matching the dashboard's ErrorModal: message, status, error code, and request id (so the call can
    /// be found in API Requests), with Retry (when offered), Copy details, and Close. Closes with <c>retry</c>,
    /// <c>copy</c>, or null. Not thread-safe.
    /// </summary>
    public class ErrorDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Error code, or null.
        /// </summary>
        public string? Code { get; }

        /// <summary>
        /// Request id, or null.
        /// </summary>
        public string? RequestId { get; }

        /// <summary>
        /// HTTP status, or 0.
        /// </summary>
        public int Status { get; }

        /// <summary>
        /// Offer a Retry button.
        /// </summary>
        public bool CanRetry { get; }

        /// <summary>
        /// Highlighted button index.
        /// </summary>
        public int SelectedButton { get; private set; } = 0;

        #endregion

        #region Private-Members

        private readonly List<string> _Buttons = new List<string>();
        private readonly List<string> _Results = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="message">Message (translated by the caller).</param>
        /// <param name="code">Error code, or null.</param>
        /// <param name="requestId">Request id, or null.</param>
        /// <param name="status">HTTP status, or 0.</param>
        /// <param name="canRetry">Offer Retry.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public ErrorDialog(string title, string message, string? code, string? requestId, int status, bool canRetry, ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
            : base(title, localizer, theme)
        {
            Message = message ?? "";
            Code = code;
            RequestId = requestId;
            Status = status;
            CanRetry = canRetry;
            if (canRetry)
            {
                _Buttons.Add(T("Retry"));
                _Results.Add("retry");
            }

            _Buttons.Add(T("Copy details"));
            _Results.Add("copy");
            _Buttons.Add(T("Close"));
            _Results.Add("");
            SelectedButton = _Buttons.Count - 1;
            FooterHint = " Enter " + T("Select") + "  Esc " + T("Close") + " ";
            MinContentWidth = 40;
            MaxContentWidth = 80;
        }

        /// <summary>
        /// Build from an API exception.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="ex">Exception.</param>
        /// <param name="canRetry">Offer Retry.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>The dialog.</returns>
        public static ErrorDialog From(string title, ArmadaApiException ex, bool canRetry, ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
        {
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            return new ErrorDialog(title, ex.Message, ex.Code, ex.RequestId, ex.StatusCode, canRetry, localizer, theme);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Details text for copying.
        /// </summary>
        /// <returns>Details.</returns>
        public string Details()
        {
            return Message + "\nStatus: " + Status + "\nCode: " + (Code ?? "") + "\nRequest ID: " + (RequestId ?? "");
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Tab || key.Code == KeyCode.Right)
            {
                SelectedButton = (SelectedButton + 1) % _Buttons.Count;
                return true;
            }

            if (key.Code == KeyCode.Left)
            {
                SelectedButton = (SelectedButton - 1 + _Buttons.Count) % _Buttons.Count;
                return true;
            }

            if (key.Code == KeyCode.Enter)
            {
                string result = _Results[SelectedButton];
                RequestClose(result.Length == 0 ? null : result);
                return true;
            }

            return true;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(40, Math.Min(80, TextCells.Width(Message) + 2)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return TextCells.Wrap(Message, contentWidth).Count + 6;
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int y = 0;
            foreach (string line in TextCells.Wrap(Message, width))
            {
                SurfaceText.Draw(content, 0, y++, line, On(Theme.Error), width);
            }

            y++;
            if (Status > 0) SurfaceText.Draw(content, 0, y++, T("Status") + ": " + Status, Dim(), width);
            if (!String.IsNullOrEmpty(Code)) SurfaceText.Draw(content, 0, y++, T("Code") + ": " + Code, Dim(), width);
            if (!String.IsNullOrEmpty(RequestId)) SurfaceText.Draw(content, 0, y++, T("Request ID") + ": " + RequestId, Dim(), width);
            int row = content.Size.Height - 1;
            int x = 0;
            for (int i = 0; i < _Buttons.Count; i++)
            {
                CellStyle style = i == SelectedButton ? Theme.ButtonFocused : Body();
                x += SurfaceText.Draw(content, x, row, "[ " + _Buttons[i] + " ]", style, width - x) + 2;
            }
        }

        #endregion
    }
}
