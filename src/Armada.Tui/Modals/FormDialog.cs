namespace Armada.Tui.Modals
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A create or edit modal (the dashboard's <c>modal</c> forms): hosts a <see cref="FormView"/>, validates on Save
    /// (<c>Ctrl+S</c> or the Save button), runs <see cref="Submit"/> off the UI loop with a "Saving..." state, shows
    /// server errors inline (the form stays open), and closes with <c>true</c> on success. <c>Esc</c> cancels; when the
    /// form has unsaved changes the first <c>Esc</c> asks for a second one. Not thread-safe; results are posted to
    /// the UI loop through the dispatcher.
    /// </summary>
    public class FormDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// The form.
        /// </summary>
        public FormView Form { get; }

        /// <summary>
        /// Saves the form; throw <see cref="ArmadaApiException"/> (or return an English error) to keep the dialog open.
        /// Returning null closes the dialog with <c>true</c>.
        /// </summary>
        public Func<CancellationToken, Task<string?>>? Submit { get; set; } = null;

        /// <summary>
        /// Inline status or error (already translated), or null.
        /// </summary>
        public string? Status { get; private set; } = null;

        /// <summary>
        /// True while <see cref="Submit"/> runs.
        /// </summary>
        public bool Busy { get; private set; } = false;

        /// <summary>
        /// Runs after the dialog closes itself from a posted (asynchronous) callback. TUIKit removes closed modals only
        /// after the next input event, so hosts pass <c>app.Modals.RemoveClosed</c> here to drop the dialog at once.
        /// </summary>
        public Action? AfterAsyncClose { get; set; } = null;

        /// <summary>
        /// Share of the screen width used. Default 0.8; clamped to 0.3..1.
        /// </summary>
        public double WidthRatio
        {
            get { return _WidthRatio; }
            set { _WidthRatio = Math.Clamp(value, 0.3, 1.0); }
        }

        #endregion

        #region Private-Members

        private readonly IUiDispatcher _Dispatcher;
        private double _WidthRatio = 0.8;
        private int _ScreenWidth = 100;
        private int _ScreenHeight = 30;
        private bool _ConfirmDiscard = false;
        private CancellationTokenSource? _Cts = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="form">Form.</param>
        /// <param name="dispatcher">UI dispatcher.</param>
        /// <param name="saveLabel">English label of the Save button.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public FormDialog(string title, FormView form, IUiDispatcher dispatcher, string saveLabel = "Save", ITextLocalizer? localizer = null, ArmadaTheme? theme = null)
            : base(title, localizer, theme)
        {
            Form = form ?? throw new ArgumentNullException(nameof(form));
            _Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Form.Localizer = Localizer;
            Form.ApplyTheme(Theme);
            Form.SaveButton.Label = saveLabel ?? "Save";
            Form.DiscardButton.Label = "Cancel";
            Form.SaveRequested += (s, e) => RunSubmit();
            Form.DiscardRequested += (s, e) => Cancel();
            Form.OnFocusChanged(true);
            Form.Scope.FocusFirst();
            Form.MarkClean();
            FooterHint = " Ctrl+S " + T("Save") + "  Tab " + T("Next field") + "  Esc " + T("Cancel") + " ";
            MinContentWidth = 50;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate and run <see cref="Submit"/>.
        /// </summary>
        public void RunSubmit()
        {
            if (Busy) return;
            Func<CancellationToken, Task<string?>>? submit = Submit;
            if (submit == null)
            {
                RequestClose(true);
                return;
            }

            Busy = true;
            Status = T("Saving...");
            CancellationTokenSource cts = new CancellationTokenSource();
            _Cts = cts;
            Task.Run(async () =>
            {
                string? error;
                try
                {
                    error = await submit(cts.Token).ConfigureAwait(false);
                }
                catch (ArmadaApiException ex)
                {
                    error = ex.Message;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                _Dispatcher.Post(() =>
                {
                    Busy = false;
                    if (error == null)
                    {
                        Status = null;
                        Form.MarkClean();
                        RequestClose(true);
                        AfterAsyncClose?.Invoke();
                    }
                    else
                    {
                        Status = "! " + T(error);
                    }
                });
            });
        }

        /// <summary>
        /// Show a message under the form (already translated).
        /// </summary>
        /// <param name="message">Message, or null to clear.</param>
        public void SetStatus(string? message)
        {
            Status = message;
        }

        /// <summary>
        /// Cancel (asks for a second Esc when the form is dirty).
        /// </summary>
        public void Cancel()
        {
            if (Form.IsDirty && !_ConfirmDiscard && !Busy)
            {
                _ConfirmDiscard = true;
                Status = T("Unsaved changes. Press Esc again to discard them.");
                return;
            }

            _Cts?.Cancel();
            RequestClose(false);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                Cancel();
                return true;
            }

            if (key.Code != KeyCode.Escape && _ConfirmDiscard)
            {
                _ConfirmDiscard = false;
                if (!Busy) Status = null;
            }

            Form.HandleKey(key);
            return true;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            Form.HandlePaste(text);
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
            return Math.Max(30, Math.Min(availableWidth, Math.Min(110, (int)(_ScreenWidth * _WidthRatio))));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            int rows = 0;
            foreach (FormRow row in Form.Rows)
            {
                if (row.IsSection) rows += 2;
                else rows += row.Height + ((row.Field as IFormField)?.FieldError != null ? 1 : 0) + (String.IsNullOrEmpty(row.Hint) ? 0 : 1);
            }

            rows += 2;
            int status = 1;
            return Math.Max(4, Math.Min(rows + status, _ScreenHeight - 6));
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            Form.Render(new SurfaceView(content, new Rect(0, 0, width, Math.Max(1, height - 1))));
            SurfaceText.FillRow(content, 0, height - 1, width, Body());
            if (Status != null)
            {
                CellStyle style = Status.StartsWith("!", StringComparison.Ordinal) ? On(Theme.Error) : _ConfirmDiscard ? On(Theme.Warning) : Dim();
                SurfaceText.Draw(content, 0, height - 1, TextCells.Truncate(Status, width), style, width);
            }
        }

        #endregion
    }
}
