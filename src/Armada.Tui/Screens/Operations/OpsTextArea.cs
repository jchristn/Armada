namespace Armada.Tui.Screens.Operations
{
    using System;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A multi-line form field (the dashboard's textarea) over TUIKit's <see cref="TextEditor"/>: <c>Enter</c> adds a
    /// line, <c>Up</c> on the first line and <c>Down</c> on the last move to the neighbouring field, <c>Tab</c> leaves,
    /// and <c>Ctrl+E</c> opens the text in <c>$EDITOR</c> through <see cref="ExternalEditor"/>. Other Ctrl chords are left
    /// to the form (so <c>Ctrl+S</c> saves) except undo/redo. Not thread-safe.
    /// </summary>
    public class OpsTextArea : ArmadaWidget, IFormField, IPasteTarget, ITextEntry
    {
        #region Public-Members

        /// <inheritdoc />
        public virtual bool AcceptsText
        {
            get { return true; }
        }

        /// <summary>
        /// Editor.
        /// </summary>
        public TextEditor Editor { get; } = new TextEditor();

        /// <summary>
        /// Text.
        /// </summary>
        public string Text
        {
            get { return Editor.Text; }
            set
            {
                Editor.Text = (value ?? "").Replace("\r\n", "\n");
                if (FieldError != null) ValidateField();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// English placeholder shown while empty.
        /// </summary>
        public string Placeholder { get; set; } = "";

        /// <summary>
        /// Validator returning an English error, or null.
        /// </summary>
        public Func<string, string?>? Validator { get; set; } = null;

        /// <summary>
        /// Opens <c>$EDITOR</c>: receives the current text and a callback for the result. Null disables <c>Ctrl+E</c>.
        /// </summary>
        public Action<string, Action<string>>? ExternalEditor { get; set; } = null;

        /// <inheritdoc />
        public object? FieldValue
        {
            get { return Text; }
        }

        /// <inheritdoc />
        public string? FieldError { get; private set; } = null;

        /// <summary>
        /// Raised after the text changes.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public OpsTextArea()
        {
            Editor.WordWrap = true;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public bool ValidateField()
        {
            FieldError = Validator != null ? Validator(Text) : null;
            return FieldError == null;
        }

        /// <inheritdoc />
        public bool HandlePaste(string text)
        {
            if (String.IsNullOrEmpty(text)) return true;
            Editor.InsertText(text.Replace("\r\n", "\n").Replace('\r', '\n'));
            Raise();
            return true;
        }

        /// <summary>
        /// Open the text in <c>$EDITOR</c> (no-op without <see cref="ExternalEditor"/>).
        /// </summary>
        public void EditExternally()
        {
            ExternalEditor?.Invoke(Text, edited => Text = edited);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            bool alt = (key.Modifiers & KeyModifiers.Alt) != 0;
            if (ctrl && key.Code == KeyCode.Character)
            {
                char c = Char.ToLowerInvariant((char)key.Rune);
                if (c == 'e' && ExternalEditor != null)
                {
                    EditExternally();
                    return true;
                }

                if (c == 'z' || c == 'y')
                {
                    Editor.HandleKey(key);
                    Raise();
                    return true;
                }

                if (c == 'u')
                {
                    Text = "";
                    return true;
                }

                return false;
            }

            if (alt) return false;
            switch (key.Code)
            {
                case KeyCode.Tab:
                case KeyCode.Escape:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                    return false;
                case KeyCode.Up:
                    if (Editor.CaretRow == 0) return false;
                    Editor.MoveUp();
                    return true;
                case KeyCode.Down:
                    if (Editor.CaretRow >= Editor.Text.Split('\n').Length - 1) return false;
                    Editor.MoveDown();
                    return true;
                case KeyCode.Enter:
                    Editor.InsertNewline();
                    Raise();
                    return true;
                default:
                    bool handled = Editor.HandleKey(key);
                    if (handled && (key.Code == KeyCode.Character || key.Code == KeyCode.Backspace || key.Code == KeyCode.Delete)) Raise();
                    return handled;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Press) return Editor.HandleMouse(mouse) || true;
            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 1 || height < 1) return;
            CellStyle style = IsFocused ? Theme.InputFocused : Theme.Input;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), style);
            Editor.NormalStyle = style;
            Editor.Render(surface);
            if (Editor.Text.Length == 0 && !String.IsNullOrEmpty(Placeholder))
            {
                SurfaceText.Draw(surface, IsFocused ? 1 : 0, 0, T(Placeholder), style.WithForeground(Theme.Muted.Foreground), width - 1);
            }

            if (IsFocused && ExternalEditor != null && height > 1)
            {
                string hint = "Ctrl+E " + T("Editor");
                SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(hint)), height - 1, hint, style.WithForeground(Theme.Muted.Foreground), width);
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnFocusChangedCore(bool focused)
        {
            Editor.OnFocusChanged(focused);
        }

        #endregion

        #region Private-Methods

        private void Raise()
        {
            if (FieldError != null) ValidateField();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
