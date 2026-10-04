namespace Armada.Tui.Screens.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Nodes;
    using Armada.Core.Models;
    using Armada.Tui.Ask;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Base of the inline quick-action forms shown above the composer: loading and load-error state, field errors,
    /// Cancel and submit buttons, <c>Tab</c>/<c>Shift+Tab</c> between fields, <c>Ctrl+S</c> submits, <c>Esc</c> cancels.
    /// Submitting validates, builds the MCP arguments, and runs the quick action; the form closes when it succeeded.
    /// Not thread-safe.
    /// </summary>
    public abstract class AskQuickActionForm : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// The quick action.
        /// </summary>
        public AskQuickAction Action { get; }

        /// <summary>
        /// Choices are loading.
        /// </summary>
        public bool Loading { get; protected set; } = true;

        /// <summary>
        /// Load error, or null.
        /// </summary>
        public string? LoadError { get; protected set; } = null;

        /// <summary>
        /// Field errors (English) keyed by field. Never null.
        /// </summary>
        public Dictionary<string, string> Errors { get; protected set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Arguments of the last submission (tests and diagnostics), or null.
        /// </summary>
        public JsonObject? LastArguments { get; private set; } = null;

        /// <summary>
        /// Raised when the form closes (cancelled or submitted successfully).
        /// </summary>
        public event EventHandler? Closed;

        /// <summary>
        /// Rows the form wants.
        /// </summary>
        public abstract int PreferredHeight { get; }

        #endregion

        #region Protected-Members

        /// <summary>
        /// Services.
        /// </summary>
        protected TuiContext Context { get; }

        /// <summary>
        /// Ask session.
        /// </summary>
        protected AskController Ask { get; }

        /// <summary>
        /// Cancel button.
        /// </summary>
        protected Button CancelButton { get; }

        /// <summary>
        /// Submit button.
        /// </summary>
        protected Button SubmitButton { get; }

        /// <summary>
        /// Buttons row.
        /// </summary>
        protected ButtonRow Buttons { get; } = new ButtonRow();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="ask">Ask session.</param>
        /// <param name="action">Quick action.</param>
        /// <param name="submitLabel">English submit label.</param>
        protected AskQuickActionForm(TuiContext context, AskController ask, AskQuickAction action, string submitLabel)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Ask = ask ?? throw new ArgumentNullException(nameof(ask));
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Localizer = context.Loc;
            ApplyTheme(context.Theme.Current);
            CancelButton = new Button("Cancel", Cancel);
            SubmitButton = new Button(submitLabel, () => SubmitForm());
            Buttons.Add(CancelButton);
            Buttons.Add(SubmitButton);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate and run the quick action.
        /// </summary>
        /// <returns>True when the request started.</returns>
        public bool SubmitForm()
        {
            if (Ask.ActionBusy || Loading) return false;
            Errors = Validate();
            if (Errors.Count > 0) return false;
            JsonObject args = BuildArguments();
            LastArguments = args;
            Ask.RunQuickAction(Action, (JsonObject)args.DeepClone(), ok =>
            {
                if (ok) Close();
            });
            return true;
        }

        /// <summary>
        /// Close without running.
        /// </summary>
        public void Cancel()
        {
            if (Ask.ActionBusy) return;
            Close();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (key.Code == KeyCode.Escape)
            {
                Cancel();
                return true;
            }

            if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 's')
            {
                SubmitForm();
                return true;
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Down && key.Modifiers == KeyModifiers.None) return Scope.Move(true) || true;
            if (key.Code == KeyCode.Up && key.Modifiers == KeyModifiers.None) return Scope.Move(false) || true;
            if (key.Code == KeyCode.Tab) return true;
            return false;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Validate the fields (English errors keyed by field).
        /// </summary>
        /// <returns>Errors.</returns>
        protected abstract Dictionary<string, string> Validate();

        /// <summary>
        /// Build the MCP arguments.
        /// </summary>
        /// <returns>Arguments.</returns>
        protected abstract JsonObject BuildArguments();

        /// <summary>
        /// Draw the form's header line.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="title">English title.</param>
        /// <param name="width">Width.</param>
        protected void DrawHeader(ISurface surface, string title, int width)
        {
            SurfaceText.FillRow(surface, 0, 0, width, Theme.Header);
            int x = SurfaceText.Draw(surface, 1, 0, AskQuickActions.CommandOf(Action), Theme.Header.WithForeground(Theme.Code.Foreground), width - 1) + 2;
            x += SurfaceText.Draw(surface, x, 0, T(title), Theme.Header, width - x);
            string keys = "Tab " + T("Next field") + "  Ctrl+S " + T("Submit") + "  Esc " + T("Cancel");
            int kw = TextCells.Width(keys);
            if (x + kw + 2 < width) SurfaceText.Draw(surface, width - kw - 1, 0, keys, Theme.Header.WithForeground(Theme.Muted.Foreground), kw);
        }

        /// <summary>
        /// Draw a field error under a field, when present.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="key">Field key.</param>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <param name="width">Width.</param>
        /// <returns>True when drawn.</returns>
        protected bool DrawError(ISurface surface, string key, int x, int y, int width)
        {
            if (!Errors.TryGetValue(key, out string? error)) return false;
            SurfaceText.Draw(surface, x, y, "! " + T(error), Theme.Error, width - x);
            return true;
        }

        /// <summary>
        /// Draw a muted label.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <param name="text">English text.</param>
        /// <param name="width">Width.</param>
        protected void Label(ISurface surface, int x, int y, string text, int width)
        {
            SurfaceText.Draw(surface, x, y, T(text), Theme.Muted, width);
        }

        #endregion

        #region Private-Methods

        private void Close()
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
