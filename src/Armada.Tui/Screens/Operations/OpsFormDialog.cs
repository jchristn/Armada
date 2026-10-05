namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal form (the dashboard's create/edit/run modals): an optional intro, a <see cref="FormView"/> with the
    /// submit and cancel buttons, inline validation, and an error line. <c>Ctrl+S</c> or the submit button validates
    /// the fields and <see cref="Validate"/>, then calls <see cref="Submit"/>; the handler either finishes at once
    /// (return true) or marks the dialog busy and later calls <see cref="Complete"/> or <see cref="Fail"/>.
    /// <c>Esc</c> cancels. Not thread-safe.
    /// </summary>
    public class OpsFormDialog : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// The form.
        /// </summary>
        public FormView Form { get; } = new FormView();

        /// <summary>
        /// English intro text shown above the fields, or null.
        /// </summary>
        public string? Intro { get; set; } = null;

        /// <summary>
        /// Extra lines (already translated) shown under the intro, for example a preview; rebuilt by the owner.
        /// </summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// Error shown above the buttons (translated), or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// True while a submit is running (keys other than Esc are ignored).
        /// </summary>
        public bool Busy { get; set; } = false;

        /// <summary>
        /// Extra validation run after the field validators; returns a translated error or null.
        /// </summary>
        public Func<string?>? Validate { get; set; } = null;

        /// <summary>
        /// Submit handler; return true to close at once, or false when it will call <see cref="Complete"/> or
        /// <see cref="Fail"/> later.
        /// </summary>
        public Func<OpsFormDialog, bool>? Submit { get; set; } = null;

        /// <summary>
        /// Runs after a programmatic close (<see cref="Complete"/>) to drop the closed dialog from the modal stack
        /// (TUIKit keeps a closed modal until the next key, which it would swallow), or null.
        /// </summary>
        public Action? AfterProgrammaticClose { get; set; } = null;

        /// <summary>
        /// Width ratio of the screen (0.4 to 1.0). Default 0.7.
        /// </summary>
        public double WidthRatio
        {
            get { return _WidthRatio; }
            set { _WidthRatio = Math.Clamp(value, 0.4, 1.0); }
        }

        #endregion

        #region Private-Members

        private readonly string _SubmitLabel;
        private double _WidthRatio = 0.7;
        private bool _CleanMarked = false;
        private int _ScreenWidth = 100;
        private int _ScreenHeight = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="submitLabel">English submit label.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public OpsFormDialog(string title, string submitLabel, ITextLocalizer? localizer, ArmadaTheme? theme)
            : base(title, localizer, theme)
        {
            _SubmitLabel = submitLabel ?? "Save";
            Form.Localizer = Localizer;
            Form.ApplyTheme(Theme);
            Form.SaveButton.Label = _SubmitLabel;
            Form.SaveButton.Hint = "Ctrl+S";
            Form.DiscardButton.Label = "Cancel";
            Form.SaveRequested += (s, e) => DoSubmit();
            Form.DiscardRequested += (s, e) => RequestClose(null);
            Form.OnFocusChanged(true);
            FooterHint = " Tab " + T("Next field") + "  Ctrl+S " + T(_SubmitLabel) + "  Esc " + T("Cancel") + " ";
            MinContentWidth = 50;
            MaxContentWidth = 140;
            MinContentHeight = 4;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a labeled field to the form (themed and localized).
        /// </summary>
        /// <typeparam name="TWidget">Widget type.</typeparam>
        /// <param name="label">English label.</param>
        /// <param name="widget">Widget.</param>
        /// <param name="hint">English hint, or null.</param>
        /// <param name="height">Rows.</param>
        /// <returns>The widget.</returns>
        public TWidget AddField<TWidget>(string label, TWidget widget, string? hint = null, int height = 1) where TWidget : IWidget
        {
            if (widget is ArmadaWidget aw)
            {
                aw.Localizer = Localizer;
                aw.ApplyTheme(Theme);
            }

            return Form.AddField(label, widget, hint, height);
        }

        /// <summary>
        /// Finish a pending submit and close with a result.
        /// </summary>
        /// <param name="result">Result.</param>
        public void Complete(object? result = null)
        {
            Busy = false;
            RequestClose(result ?? true);
            AfterProgrammaticClose?.Invoke();
        }

        /// <summary>
        /// Finish a pending submit with an error (the dialog stays open).
        /// </summary>
        /// <param name="message">Translated message.</param>
        public void Fail(string message)
        {
            Busy = false;
            Error = message;
            Form.SaveButton.Label = _SubmitLabel;
        }

        /// <summary>
        /// Validate and submit (the submit button and <c>Ctrl+S</c>).
        /// </summary>
        public void DoSubmit()
        {
            if (Busy) return;
            if (!Form.ValidateAll()) return;
            string? error = Validate?.Invoke();
            if (error != null)
            {
                Error = error;
                return;
            }

            Error = null;
            if (Submit == null)
            {
                RequestClose(true);
                return;
            }

            Busy = true;
            if (Submit(this))
            {
                Busy = false;
                RequestClose(true);
            }
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                RequestClose(null);
                return true;
            }

            if (Busy) return true;
            if (key.Code == KeyCode.Tab)
            {
                if (!Form.Scope.HandleKey(key))
                {
                    if ((key.Modifiers & KeyModifiers.Shift) == 0) Form.Scope.FocusFirst();
                    else Form.Scope.FocusLast();
                }

                return true;
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
            Form.SaveButton.Label = Busy ? "Working..." : _SubmitLabel;
            if (!_CleanMarked)
            {
                Form.MarkClean();
                _CleanMarked = true;
            }

            base.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, Math.Max(50, (int)(_ScreenWidth * _WidthRatio)));
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            int formRows = 0;
            foreach (FormRow row in Form.Rows)
            {
                if (FormView.IsHidden(row)) continue;
                if (row.IsSection) formRows += 2;
                else formRows += row.Height + ((row.Field as IFormField)?.FieldError != null ? 1 : 0) + (String.IsNullOrEmpty(row.Hint) ? 0 : 1);
            }

            formRows += 2;
            int intro = IntroLines(contentWidth).Count;
            int extra = intro + (intro > 0 ? 1 : 0) + (Error != null ? 2 : 0);
            return Math.Min(Math.Max(6, _ScreenHeight - 6), formRows + extra);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            int width = content.Size.Width;
            int height = content.Size.Height;
            int y = 0;
            List<string> intro = IntroLines(width);
            foreach (string line in intro)
            {
                if (y >= height) break;
                SurfaceText.Draw(content, 0, y++, line, Dim(), width);
            }

            if (intro.Count > 0) y++;
            int errorRows = Error != null ? 2 : 0;
            if (Error != null && height - 2 >= y)
            {
                SurfaceText.Draw(content, 0, height - 2, "! " + Error, On(Theme.Error), width);
            }

            int formHeight = Math.Max(1, height - y - errorRows);
            Form.Render(new SurfaceView(content, new Rect(0, y, width, formHeight)));
        }

        #endregion

        #region Private-Methods

        private List<string> IntroLines(int width)
        {
            List<string> lines = new List<string>();
            if (!String.IsNullOrEmpty(Intro)) lines.AddRange(TextCells.Wrap(T(Intro!), Math.Max(10, width)));
            foreach (string note in Notes) lines.AddRange(TextCells.Wrap(note, Math.Max(10, width)));
            return lines;
        }

        #endregion
    }
}
