namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Tui.Modals;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A dialog hosting a <see cref="FormView"/>: the create and edit modals of the dashboard (users, credentials,
    /// tenants, Send Signal, path prompts). <c>Ctrl+S</c> or the primary button validates and submits; the submit
    /// callback runs off the UI loop and returns an English error to show inline, or null to close with
    /// <c>true</c>. <c>Esc</c> closes with <c>false</c>. Not thread-safe.
    /// </summary>
    public class FormModal : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// The hosted form.
        /// </summary>
        public FormView Form { get; }

        /// <summary>
        /// Submit callback (runs off the UI loop): returns an English error, or null on success.
        /// </summary>
        public Func<Task<string?>>? SubmitAsync { get; set; } = null;

        /// <summary>
        /// Synchronous submit callback (runs on the UI loop): returns an English error, or null on success. Used when
        /// <see cref="SubmitAsync"/> is null.
        /// </summary>
        public Func<string?>? Submit { get; set; } = null;

        /// <summary>
        /// Error shown under the form (English), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// True while a submit is running.
        /// </summary>
        public bool Busy { get; private set; } = false;

        /// <summary>
        /// Content width in columns (clamped to the screen). Default 76.
        /// </summary>
        public int ContentWidth
        {
            get { return _ContentWidth; }
            set { _ContentWidth = Math.Clamp(value, 30, 200); }
        }

        /// <summary>
        /// Intro text (already translated) shown above the form, or empty.
        /// </summary>
        public string Intro { get; set; } = "";

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;
        private int _ContentWidth = 76;
        private int _ScreenHeight = 24;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="form">Form (its Save button label is set from <paramref name="submitLabel"/>).</param>
        /// <param name="context">Context (localizer, theme, dispatcher).</param>
        /// <param name="submitLabel">English label for the primary button.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public FormModal(string title, FormView form, TuiContext context, string submitLabel = "Save")
            : base(title, context?.Loc, context?.Theme.Current)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            Form = form ?? throw new ArgumentNullException(nameof(form));
            Form.Localizer = Localizer;
            Form.ApplyTheme(Theme);
            Form.SaveButton.Label = submitLabel ?? "Save";
            Form.DiscardButton.Label = "Cancel";
            Form.SaveRequested += (s, e) => RunSubmit();
            Form.DiscardRequested += (s, e) => RequestClose(false);
            Form.MarkClean();
            Form.OnFocusChanged(true);
            Form.Scope.FocusFirst();
            FooterHint = " Ctrl+S " + T(submitLabel ?? "Save") + "  Tab " + T("Next") + "  Esc " + T("Cancel") + " ";
            MinContentWidth = 30;
            MaxContentWidth = 200;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate and submit (what <c>Ctrl+S</c> does).
        /// </summary>
        public void RunSubmit()
        {
            if (Busy) return;
            Error = null;
            if (SubmitAsync == null)
            {
                string? syncError = Submit != null ? Submit() : null;
                if (syncError != null) Error = syncError;
                else RequestClose(true);
                return;
            }

            Busy = true;
            Func<Task<string?>> submit = SubmitAsync;
            Task.Run(async () =>
            {
                string? error;
                try
                {
                    error = await submit().ConfigureAwait(false);
                }
                catch (ArmadaApiException ex)
                {
                    error = ex.Message;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                _Context.Dispatcher.Post(() =>
                {
                    Busy = false;
                    if (error != null) Error = error;
                    else RequestClose(true);
                });
            });
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Busy && key.Code != KeyCode.Escape) return true;
            if (Form.HandleKey(key)) return true;
            if (HandleDismiss(key, false)) return true;
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
            _ScreenHeight = surface.Size.Height;
            base.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, _ContentWidth);
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            int rows = 0;
            foreach (FormRow row in Form.Rows)
            {
                if (row.IsSection)
                {
                    rows += 2;
                    continue;
                }

                rows += row.Height;
                if (!String.IsNullOrEmpty(row.Hint)) rows++;
                if ((row.Field as IFormField)?.FieldError != null) rows++;
            }

            int intro = Intro.Length > 0 ? TextCells.Wrap(Intro, contentWidth).Count + 1 : 0;
            int total = intro + rows + 2 + 2;
            return Math.Max(4, Math.Min(total, Math.Max(6, _ScreenHeight - 6)));
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            int y = 0;
            if (Intro.Length > 0)
            {
                foreach (string line in TextCells.Wrap(Intro, width))
                {
                    if (y >= height) break;
                    SurfaceText.Draw(content, 0, y++, line, Dim(), width);
                }

                y++;
            }

            int formHeight = Math.Max(1, height - y - 2);
            Form.Render(new SurfaceView(content, new Rect(0, y, width, formHeight)));
            int statusY = height - 1;
            if (Busy) SurfaceText.Draw(content, 0, statusY, T("Saving..."), Dim(), width);
            else if (Error != null) SurfaceText.Draw(content, 0, statusY, "! " + T(Error), On(Theme.Error), width);
        }

        #endregion
    }
}
