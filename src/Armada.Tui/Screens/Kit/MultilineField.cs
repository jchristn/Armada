namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Threading.Tasks;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A multi-line text field over the TUIKit <see cref="TextEditor"/> for request bodies, payloads, and one-per-line
    /// lists. <c>Enter</c> inserts a newline, <c>Tab</c> leaves the field, <c>Ctrl+E</c> edits the text in
    /// <c>$EDITOR</c> when an editor callback is set, and other <c>Ctrl</c> chords fall through to the host (so
    /// <c>Ctrl+S</c> still saves a form). Not thread-safe.
    /// </summary>
    public class MultilineField : ArmadaWidget, IFormField, IPasteTarget, ITextEntry
    {
        #region Public-Members

        /// <summary>
        /// Text (lines joined by newlines). Never null.
        /// </summary>
        public string Value
        {
            get { return _Editor.Text; }
            set { _Editor.Text = value ?? ""; }
        }

        /// <summary>
        /// English placeholder shown while empty and unfocused.
        /// </summary>
        public string Placeholder { get; set; } = "";

        /// <summary>
        /// Validator returning an English error, or null when valid.
        /// </summary>
        public Func<string, string?>? Validator { get; set; } = null;

        /// <summary>
        /// Opens the text in an external editor and returns the edited text, or null for no external editing.
        /// </summary>
        public Func<string, Task<string>>? ExternalEditor { get; set; } = null;

        /// <summary>
        /// Posts work back to the UI loop (required with <see cref="ExternalEditor"/>).
        /// </summary>
        public IUiDispatcher? Dispatcher { get; set; } = null;

        /// <summary>
        /// Wrap long lines at the field width instead of clipping them. Default false.
        /// </summary>
        public bool WordWrap
        {
            get { return _Editor.WordWrap; }
            set { _Editor.WordWrap = value; }
        }

        /// <summary>
        /// When true the field ignores edits. Default false.
        /// </summary>
        public bool ReadOnly { get; set; } = false;

        /// <inheritdoc />
        public bool AcceptsText
        {
            get { return !ReadOnly; }
        }

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return Value; }
        }

        /// <inheritdoc />
        public string? FieldError { get; private set; } = null;

        /// <summary>
        /// Raised after an edit changes the text.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Private-Members

        private readonly TextEditor _Editor = new TextEditor();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="value">Initial text.</param>
        public MultilineField(string value = "")
        {
            _Editor.Text = value ?? "";
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool ValidateField()
        {
            FieldError = Validator != null ? Validator(Value) : null;
            return FieldError == null;
        }

        /// <summary>
        /// Insert text at the caret.
        /// </summary>
        /// <param name="text">Text.</param>
        public void Insert(string text)
        {
            if (ReadOnly || String.IsNullOrEmpty(text)) return;
            _Editor.InsertText(text.Replace("\r\n", "\n").Replace("\r", "\n"));
            RaiseChanged();
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            Insert(text);
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Tab || key.Code == KeyCode.Escape) return false;
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (ctrl && key.Code == KeyCode.Character)
            {
                char c = Char.ToLowerInvariant((char)key.Rune);
                if (c == 'e' && ExternalEditor != null && !ReadOnly)
                {
                    EditExternally();
                    return true;
                }

                if (ReadOnly || (c != 'z' && c != 'y' && c != 'k' && c != 'u')) return false;
            }

            if ((key.Modifiers & KeyModifiers.Alt) != 0) return false;
            if (ReadOnly)
            {
                bool nav = key.Code == KeyCode.Up || key.Code == KeyCode.Down || key.Code == KeyCode.Left || key.Code == KeyCode.Right || key.Code == KeyCode.Home || key.Code == KeyCode.End;
                return nav && _Editor.HandleKey(key);
            }

            string before = _Editor.Text;
            bool handled = _Editor.HandleKey(key);
            if (handled && !String.Equals(before, _Editor.Text, StringComparison.Ordinal)) RaiseChanged();
            return handled;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            return _Editor.HandleMouse(mouse);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            CellStyle style = IsFocused ? Theme.InputFocused : Theme.Input;
            _Editor.NormalStyle = style;
            _Editor.IsFocused = IsFocused;
            if (_Editor.Text.Length == 0 && !IsFocused && Placeholder.Length > 0)
            {
                SurfaceText.FillRect(surface, new Rect(0, 0, width, height), style);
                SurfaceText.Draw(surface, 0, 0, T(Placeholder), style.WithForeground(Theme.Muted.Foreground), width);
                return;
            }

            _Editor.Render(surface);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnFocusChangedCore(bool focused)
        {
            _Editor.OnFocusChanged(focused);
        }

        #endregion

        #region Private-Methods

        private void EditExternally()
        {
            Func<string, Task<string>>? editor = ExternalEditor;
            if (editor == null) return;
            string current = Value;
            Task.Run(async () =>
            {
                string edited = await editor(current).ConfigureAwait(false);
                Action apply = () =>
                {
                    Value = edited.TrimEnd('\n');
                    RaiseChanged();
                };
                if (Dispatcher != null) Dispatcher.Post(apply);
                else apply();
            });
        }

        private void RaiseChanged()
        {
            EventHandler? handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        #endregion
    }
}
