namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Unicode;
    using TUIKit.Widgets;

    /// <summary>
    /// Single-line text field with grapheme-aware editing and cell-width scrolling (CJK-safe, TUIKit gap U9), optional
    /// masking for passwords and tokens (<c>Ctrl+R</c> toggles reveal), a placeholder, inline validation, and change
    /// and submit events. Keys: Left/Right, Home/End (Ctrl+A/Ctrl+E), Backspace/Delete, Ctrl+U clear, Ctrl+W delete
    /// word, Enter submits. A value set with <see cref="Prefill"/> is shown selected: the first typed character or paste
    /// replaces it, Backspace/Delete/Ctrl+U clear it, and a caret movement keeps it for editing. Not thread-safe.
    /// </summary>
    public class TextInput : ArmadaWidget, IPasteTarget, ITextEntry
    {
        #region Public-Members

        /// <inheritdoc />
        public virtual bool AcceptsText
        {
            get { return true; }
        }

        /// <summary>
        /// Current text. Setting it moves the caret to the end and raises <see cref="ValueChanged"/> when it differs.
        /// </summary>
        public string Value
        {
            get { return String.Concat(_Graphemes); }
            set
            {
                string old = Value;
                _Graphemes = Split(value ?? "");
                if (_MaxLength > 0 && _Graphemes.Count > _MaxLength) _Graphemes = _Graphemes.Take(_MaxLength).ToList();
                _Caret = _Graphemes.Count;
                _PrefillSelected = false;
                RaiseIfChanged(old);
            }
        }

        /// <summary>
        /// True while a value set with <see cref="Prefill"/> is still selected (untouched by the user), so the next typed
        /// character or paste replaces it instead of appending to it.
        /// </summary>
        public bool PrefillSelected
        {
            get { return _PrefillSelected && _Graphemes.Count > 0; }
        }

        /// <summary>
        /// Placeholder shown (muted) when empty.
        /// </summary>
        public string Placeholder { get; set; } = "";

        /// <summary>
        /// Mask the text (passwords, API keys).
        /// </summary>
        public bool Masked { get; set; } = false;

        /// <summary>
        /// Show a masked value in clear text (toggled with Ctrl+R).
        /// </summary>
        public bool Revealed { get; set; } = false;

        /// <summary>
        /// Maximum length in graphemes; 0 is unlimited. Clamped to 0..100000.
        /// </summary>
        public int MaxLength
        {
            get { return _MaxLength; }
            set { _MaxLength = Math.Clamp(value, 0, 100000); }
        }

        /// <summary>
        /// Validation: returns an error message (English, translated when shown) or null when valid.
        /// </summary>
        public Func<string, string?>? Validator { get; set; } = null;

        /// <summary>
        /// Last validation error, or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        /// <summary>
        /// Caret position in graphemes.
        /// </summary>
        public int Caret
        {
            get { return _Caret; }
        }

        /// <summary>
        /// Raised after the text changes.
        /// </summary>
        public event EventHandler<ValueChangedEventArgs<string>>? ValueChanged;

        /// <summary>
        /// Raised when Enter is pressed. When no handler is attached Enter is not consumed.
        /// </summary>
        public event EventHandler? Submitted;

        #endregion

        #region Private-Members

        private List<string> _Graphemes = new List<string>();
        private int _Caret = 0;
        private int _Scroll = 0;
        private int _MaxLength = 0;
        private bool _PrefillSelected = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the validator and store the result in <see cref="Error"/>.
        /// </summary>
        /// <returns>True when valid.</returns>
        public bool Validate()
        {
            Error = Validator != null ? Validator(Value) : null;
            return Error == null;
        }

        /// <summary>
        /// Set a suggested value (for example a documented default password) that the user can accept with Enter or
        /// replace by typing: it starts selected, so typing does not append to it.
        /// </summary>
        /// <param name="value">Suggested value.</param>
        public void Prefill(string? value)
        {
            Value = value ?? "";
            _PrefillSelected = _Graphemes.Count > 0;
        }

        /// <summary>
        /// Insert text at the caret (newlines become spaces).
        /// </summary>
        /// <param name="text">Text.</param>
        public void Insert(string text)
        {
            if (String.IsNullOrEmpty(text)) return;
            string old = Value;
            ReplaceSelectedPrefill();
            List<string> parts = Split(text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' '));
            foreach (string g in parts)
            {
                if (_MaxLength > 0 && _Graphemes.Count >= _MaxLength) break;
                _Graphemes.Insert(_Caret, g);
                _Caret++;
            }

            RaiseIfChanged(old);
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
            string old = Value;
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            bool alt = (key.Modifiers & KeyModifiers.Alt) != 0;
            if (PrefillSelected)
            {
                bool clears = key.Code == KeyCode.Backspace || key.Code == KeyCode.Delete
                    || (key.Code == KeyCode.Character && ctrl && Char.ToLowerInvariant((char)key.Rune) == 'u');
                if (clears)
                {
                    ReplaceSelectedPrefill();
                    RaiseIfChanged(old);
                    return true;
                }

                bool movesCaret = key.Code == KeyCode.Left || key.Code == KeyCode.Right || key.Code == KeyCode.Home || key.Code == KeyCode.End
                    || (key.Code == KeyCode.Character && ctrl && (Char.ToLowerInvariant((char)key.Rune) == 'a' || Char.ToLowerInvariant((char)key.Rune) == 'e'));
                if (movesCaret) _PrefillSelected = false;
            }

            switch (key.Code)
            {
                case KeyCode.Left:
                    if (_Caret > 0) _Caret--;
                    return true;
                case KeyCode.Right:
                    if (_Caret < _Graphemes.Count) _Caret++;
                    return true;
                case KeyCode.Home:
                    _Caret = 0;
                    return true;
                case KeyCode.End:
                    _Caret = _Graphemes.Count;
                    return true;
                case KeyCode.Backspace:
                    if (_Caret > 0)
                    {
                        _Graphemes.RemoveAt(_Caret - 1);
                        _Caret--;
                    }

                    RaiseIfChanged(old);
                    return true;
                case KeyCode.Delete:
                    if (_Caret < _Graphemes.Count) _Graphemes.RemoveAt(_Caret);
                    RaiseIfChanged(old);
                    return true;
                case KeyCode.Enter:
                    if (Submitted == null) return false;
                    Submitted(this, EventArgs.Empty);
                    return true;
                case KeyCode.Character:
                    if (ctrl)
                    {
                        switch (Char.ToLowerInvariant((char)key.Rune))
                        {
                            case 'a':
                                _Caret = 0;
                                return true;
                            case 'e':
                                _Caret = _Graphemes.Count;
                                return true;
                            case 'u':
                                _Graphemes.Clear();
                                _Caret = 0;
                                RaiseIfChanged(old);
                                return true;
                            case 'w':
                                DeleteWord();
                                RaiseIfChanged(old);
                                return true;
                            case 'r':
                                if (!Masked) return false;
                                Revealed = !Revealed;
                                return true;
                            default:
                                return false;
                        }
                    }

                    if (alt) return false;
                    if (key.Rune < 32) return false;
                    Insert(Char.ConvertFromUtf32(key.Rune));
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press) return false;
            _PrefillSelected = false;
            int cells = 0;
            int target = _Graphemes.Count;
            for (int i = _Scroll; i < _Graphemes.Count; i++)
            {
                int w = Display(_Graphemes[i]).Length == 1 && Masked && !Revealed ? 1 : TextCells.Width(_Graphemes[i]);
                if (cells + w > mouse.X)
                {
                    target = i;
                    break;
                }

                cells += w;
            }

            _Caret = Math.Clamp(target, 0, _Graphemes.Count);
            return true;
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, 1);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            if (width < 1 || surface.Size.Height < 1) return;
            CellStyle style = IsFocused ? Theme.InputFocused : Theme.Input;
            SurfaceText.FillRow(surface, 0, 0, width, style);

            if (_Graphemes.Count == 0 && !IsFocused && !String.IsNullOrEmpty(Placeholder))
            {
                SurfaceText.Draw(surface, 0, 0, T(Placeholder), style.WithForeground(Theme.Muted.Foreground), width);
                return;
            }

            if (_Graphemes.Count == 0 && IsFocused && !String.IsNullOrEmpty(Placeholder))
            {
                SurfaceText.Draw(surface, 1, 0, T(Placeholder), style.WithForeground(Theme.Muted.Foreground), width - 1);
            }

            EnsureCaretVisible(width);
            int x = 0;
            for (int i = _Scroll; i < _Graphemes.Count; i++)
            {
                string shown = Display(_Graphemes[i]);
                int w = TextCells.Width(shown);
                if (x + w > width) break;
                bool selected = IsFocused && (i == _Caret || PrefillSelected);
                CellStyle cellStyle = selected ? style.WithAttribute(CellAttributes.Reverse, true) : style;
                surface.DrawText(x, 0, shown, cellStyle);
                x += w;
            }

            if (IsFocused && _Caret == _Graphemes.Count && x < width)
            {
                surface.DrawText(x, 0, " ", style.WithAttribute(CellAttributes.Reverse, true));
            }
        }

        #endregion

        #region Private-Methods

        private string Display(string grapheme)
        {
            return Masked && !Revealed ? "*" : grapheme;
        }

        private void EnsureCaretVisible(int width)
        {
            if (_Caret < _Scroll) _Scroll = _Caret;
            while (true)
            {
                int cells = 1;
                for (int i = _Scroll; i < _Caret && i < _Graphemes.Count; i++) cells += TextCells.Width(Display(_Graphemes[i]));
                if (cells <= width || _Scroll >= _Caret) break;
                _Scroll++;
            }
        }

        private void ReplaceSelectedPrefill()
        {
            if (!_PrefillSelected) return;
            _PrefillSelected = false;
            _Graphemes.Clear();
            _Caret = 0;
            _Scroll = 0;
        }

        private void DeleteWord()
        {
            while (_Caret > 0 && _Graphemes[_Caret - 1] == " ")
            {
                _Graphemes.RemoveAt(_Caret - 1);
                _Caret--;
            }

            while (_Caret > 0 && _Graphemes[_Caret - 1] != " ")
            {
                _Graphemes.RemoveAt(_Caret - 1);
                _Caret--;
            }
        }

        private void RaiseIfChanged(string old)
        {
            string current = Value;
            if (String.Equals(old, current, StringComparison.Ordinal)) return;
            if (Error != null) Validate();
            EventHandler<ValueChangedEventArgs<string>>? handler = ValueChanged;
            if (handler != null) handler(this, new ValueChangedEventArgs<string>(old, current));
        }

        private static List<string> Split(string text)
        {
            return Graphemes.Split(text).Select(g => g.Text).ToList();
        }

        #endregion
    }
}
