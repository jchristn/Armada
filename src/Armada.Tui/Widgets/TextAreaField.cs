namespace Armada.Tui.Widgets
{
    using System;
    using System.Threading.Tasks;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A multi-line text field for <see cref="FormView"/> (the dashboard's <c>textarea</c>): edits inline with TUIKit's
    /// <see cref="TextEditor"/> (word wrap, undo with <c>Ctrl+Z</c>/<c>Ctrl+Y</c>, kill and yank), and <c>Ctrl+E</c> opens
    /// the text in <c>$VISUAL</c>/<c>$EDITOR</c> through <see cref="ExternalEditor"/>. <c>Tab</c> leaves the field;
    /// <c>Enter</c> inserts a newline. A character count and an editor hint are drawn on the last row. Not thread-safe;
    /// the editor result is posted back through <see cref="Dispatcher"/>.
    /// </summary>
    public class TextAreaField : ArmadaWidget, IFormField, IPasteTarget
    {
        #region Public-Members

        /// <summary>
        /// Current text (newline separated). Setting replaces the text and moves the caret to the end.
        /// </summary>
        public string Value
        {
            get { return _Editor.Text; }
            set
            {
                string old = _Editor.Text;
                _Editor.Text = value ?? "";
                RaiseIfChanged(old);
            }
        }

        /// <summary>
        /// English placeholder shown while empty and unfocused.
        /// </summary>
        public string Placeholder { get; set; } = "";

        /// <summary>
        /// Validator returning an English error or null.
        /// </summary>
        public Func<string, string?>? Validator { get; set; } = null;

        /// <summary>
        /// Read-only fields can be scrolled and copied but not edited.
        /// </summary>
        public bool ReadOnly { get; set; } = false;

        /// <summary>
        /// Opens the external editor with the current text and returns the edited text (normally
        /// <see cref="ExternalService.EditTextAsync"/>). Null disables <c>Ctrl+E</c>.
        /// </summary>
        public Func<string, Task<string>>? ExternalEditor { get; set; } = null;

        /// <summary>
        /// Dispatcher used to apply the external editor's result on the UI loop.
        /// </summary>
        public IUiDispatcher? Dispatcher { get; set; } = null;

        /// <summary>
        /// File extension for the external editor's temporary file (".md" for Markdown).
        /// </summary>
        public string EditorExtension { get; set; } = ".md";

        /// <summary>
        /// Show the character count and key hint row. Default true.
        /// </summary>
        public bool ShowFooter { get; set; } = true;

        /// <summary>
        /// Last validation error (English), or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        /// <summary>
        /// True while the external editor is open.
        /// </summary>
        public bool EditingExternally { get; private set; } = false;

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return Value; }
        }

        /// <inheritdoc />
        public string? FieldError
        {
            get { return Error; }
        }

        /// <summary>
        /// Raised after the text changes.
        /// </summary>
        public event EventHandler<ValueChangedEventArgs<string>>? ValueChanged;

        #endregion

        #region Private-Members

        private readonly TextEditor _Editor = new TextEditor();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="value">Initial text.</param>
        public TextAreaField(string value = "")
        {
            _Editor.WordWrap = true;
            _Editor.Text = value ?? "";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Insert text at the caret.
        /// </summary>
        /// <param name="text">Text.</param>
        public void Insert(string text)
        {
            if (ReadOnly || String.IsNullOrEmpty(text)) return;
            string old = _Editor.Text;
            _Editor.InsertText(text.Replace("\r\n", "\n"));
            RaiseIfChanged(old);
        }

        /// <summary>
        /// Run the validator.
        /// </summary>
        /// <returns>True when valid.</returns>
        public bool Validate()
        {
            Error = Validator != null ? Validator(Value) : null;
            return Error == null;
        }

        /// <inheritdoc />
        public bool ValidateField()
        {
            return Validate();
        }

        /// <summary>
        /// Open the external editor (<c>Ctrl+E</c>).
        /// </summary>
        /// <returns>True when an editor was started.</returns>
        public bool OpenExternalEditor()
        {
            if (ReadOnly || ExternalEditor == null || EditingExternally) return false;
            EditingExternally = true;
            Func<string, Task<string>> editor = ExternalEditor;
            string initial = Value;
            Task.Run(async () =>
            {
                string result = initial;
                try
                {
                    result = await editor(initial).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    result = initial;
                }

                Action apply = () =>
                {
                    EditingExternally = false;
                    string trimmed = (result ?? "").Replace("\r\n", "\n");
                    if (trimmed.EndsWith("\n", StringComparison.Ordinal) && !initial.EndsWith("\n", StringComparison.Ordinal)) trimmed = trimmed.TrimEnd('\n');
                    Value = trimmed;
                };
                if (Dispatcher != null) Dispatcher.Post(apply);
                else apply();
            });
            return true;
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            if (ReadOnly) return false;
            Insert(text);
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (key.Code == KeyCode.Tab || key.Code == KeyCode.Escape) return false;
            if (ctrl && key.Code == KeyCode.Character)
            {
                char c = Char.ToLowerInvariant((char)key.Rune);
                if (c == 'e') return OpenExternalEditor() || ExternalEditor != null;
                if (ReadOnly || (c != 'z' && c != 'y' && c != 'u')) return false;
            }

            if ((key.Modifiers & KeyModifiers.Alt) != 0) return false;
            if (key.Code == KeyCode.F10 || key.Code == KeyCode.F5 || key.Code == KeyCode.F1 || key.Code == KeyCode.F6 || key.Code == KeyCode.F12) return false;
            if (ReadOnly)
            {
                if (key.Code == KeyCode.Up) { _Editor.MoveUp(); return true; }
                if (key.Code == KeyCode.Down) { _Editor.MoveDown(); return true; }
                if (key.Code == KeyCode.Left) { _Editor.MoveLeft(); return true; }
                if (key.Code == KeyCode.Right) { _Editor.MoveRight(); return true; }
                return false;
            }

            string old = _Editor.Text;
            bool handled = _Editor.HandleKey(key);
            RaiseIfChanged(old);
            return handled;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            return _Editor.HandleMouse(mouse);
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return available;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 2 || height < 1) return;
            CellStyle style = ReadOnly ? Theme.Disabled : IsFocused ? Theme.InputFocused : Theme.Input;
            int footer = ShowFooter && height >= 3 ? 1 : 0;
            int body = height - footer;
            _Editor.NormalStyle = style;
            _Editor.IsFocused = IsFocused && !ReadOnly;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, body), style);
            if (_Editor.Text.Length == 0 && !IsFocused && Placeholder.Length > 0)
            {
                SurfaceText.Draw(surface, 0, 0, T(Placeholder), style.WithForeground(Theme.Muted.Foreground), width);
            }
            else
            {
                _Editor.Render(new SurfaceView(surface, new Rect(0, 0, width, body)));
            }

            if (footer == 1)
            {
                SurfaceText.FillRow(surface, 0, height - 1, width, Theme.Text);
                string count = Localizer.FormatNumber(_Editor.Text.Length) + " " + T("chars");
                string hint = EditingExternally ? T("Editing in external editor...")
                    : ExternalEditor != null && !ReadOnly ? "Ctrl+E " + T("Open in editor") : "";
                SurfaceText.Draw(surface, 0, height - 1, count, Theme.Muted, width);
                if (hint.Length > 0) SurfaceText.Draw(surface, Math.Max(TextCells.Width(count) + 2, width - TextCells.Width(hint)), height - 1, hint, Theme.Muted, width);
            }
        }

        #endregion

        #region Private-Methods

        private void RaiseIfChanged(string old)
        {
            string current = _Editor.Text;
            if (String.Equals(old, current, StringComparison.Ordinal)) return;
            if (Error != null) Validate();
            EventHandler<ValueChangedEventArgs<string>>? handler = ValueChanged;
            if (handler != null) handler(this, new ValueChangedEventArgs<string>(old, current));
        }

        #endregion
    }
}
