namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;

    /// <summary>
    /// Base for read-only scrolling viewers (JSON, Markdown, logs, diffs): wraps styled lines to the width, scrolls
    /// with Up/Down/PgUp/PgDn/Home/End and the wheel, searches with <c>/</c> or <c>Ctrl+F</c> (<c>n</c>/<c>N</c> for
    /// next/previous), and optionally follows the tail. Not thread-safe.
    /// </summary>
    public abstract class ScrollTextView : ArmadaWidget, IFocusHintSource
    {
        #region Public-Members

        /// <summary>
        /// Keep the view scrolled to the end as content grows (logs). Toggle with <c>f</c> when
        /// <see cref="AllowFollow"/> is set.
        /// </summary>
        public bool Follow { get; set; } = false;

        /// <summary>
        /// Allow <c>f</c> to toggle follow mode.
        /// </summary>
        public bool AllowFollow { get; set; } = false;

        /// <summary>
        /// First visible wrapped row.
        /// </summary>
        public int ScrollOffset
        {
            get { return _Scroll; }
        }

        /// <summary>
        /// Active search text, or empty.
        /// </summary>
        public string SearchText
        {
            get { return _Search; }
        }

        /// <summary>
        /// True while the search prompt is open.
        /// </summary>
        public bool Searching
        {
            get { return _Searching; }
        }

        /// <summary>
        /// Plain text of the content (for copying).
        /// </summary>
        public abstract string PlainText { get; }

        #endregion

        #region Private-Members

        private List<StyledText> _Wrapped = new List<StyledText>();
        private List<string> _WrappedPlain = new List<string>();
        private int _WrappedWidth = -1;
        private bool _Dirty = true;
        private int _Scroll = 0;
        private int _Height = 1;
        private string _Search = "";
        private bool _Searching = false;
        private int _Match = -1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Mark the content changed so it is re-wrapped on the next render.
        /// </summary>
        public void Invalidate()
        {
            _Dirty = true;
        }

        /// <summary>
        /// Scroll to the first row containing the search text after the current match.
        /// </summary>
        /// <param name="forward">Direction.</param>
        /// <returns>True when a match was found.</returns>
        public bool FindNext(bool forward = true)
        {
            if (String.IsNullOrEmpty(_Search) || _WrappedPlain.Count == 0) return false;
            int count = _WrappedPlain.Count;
            int start = _Match < 0 ? (forward ? 0 : count - 1) : _Match + (forward ? 1 : -1);
            for (int step = 0; step < count; step++)
            {
                int idx = ((start + (forward ? step : -step)) % count + count) % count;
                if (_WrappedPlain[idx].IndexOf(_Search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _Match = idx;
                    _Scroll = Math.Max(0, idx - _Height / 2);
                    Follow = false;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Set the search text and jump to the first match.
        /// </summary>
        /// <param name="text">Search text.</param>
        /// <returns>True when found.</returns>
        public bool Search(string text)
        {
            _Search = text ?? "";
            _Match = -1;
            EnsureWrapped(_WrappedWidth > 0 ? _WrappedWidth : 80);
            return FindNext(true);
        }

        /// <inheritdoc />
        public FocusHints? GetFocusHints()
        {
            // While the search prompt is open, keys type into it; otherwise the screen's hints apply.
            if (_Searching) return FocusHints.Typing("Esc", "Cancel search").Add("Enter", "Find");
            return null;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (_Searching) return HandleSearchKey(key);
            bool plain = key.Modifiers == KeyModifiers.None;
            switch (key.Code)
            {
                case KeyCode.Up:
                    ScrollBy(-1);
                    return true;
                case KeyCode.Down:
                    ScrollBy(1);
                    return true;
                case KeyCode.PageUp:
                    ScrollBy(-Math.Max(1, _Height - 1));
                    return true;
                case KeyCode.PageDown:
                    ScrollBy(Math.Max(1, _Height - 1));
                    return true;
                case KeyCode.Home:
                    _Scroll = 0;
                    Follow = false;
                    return true;
                case KeyCode.End:
                    _Scroll = Math.Max(0, _Wrapped.Count - _Height);
                    if (AllowFollow) Follow = true;
                    return true;
                case KeyCode.Character:
                    if ((key.Modifiers & KeyModifiers.Ctrl) != 0 && Char.ToLowerInvariant((char)key.Rune) == 'f')
                    {
                        BeginSearch();
                        return true;
                    }

                    if (!plain && (key.Modifiers & KeyModifiers.Shift) == 0) return false;
                    if (key.Rune == '/')
                    {
                        BeginSearch();
                        return true;
                    }

                    if (key.Rune == 'n' && !String.IsNullOrEmpty(_Search)) return FindNext(true) || true;
                    if (key.Rune == 'N' && !String.IsNullOrEmpty(_Search)) return FindNext(false) || true;
                    if (key.Rune == 'f' && AllowFollow)
                    {
                        Follow = !Follow;
                        if (Follow) _Scroll = Math.Max(0, _Wrapped.Count - _Height);
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Wheel) return false;
            if (mouse.Button == MouseButton.WheelUp) ScrollBy(-3);
            else if (mouse.Button == MouseButton.WheelDown) ScrollBy(3);
            else return false;
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 1 || height < 1) return;
            bool showPrompt = _Searching || !String.IsNullOrEmpty(_Search);
            _Height = Math.Max(1, height - (showPrompt ? 1 : 0));
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text.WithBackground(BackgroundColor()));
            EnsureWrapped(width);
            int max = Math.Max(0, _Wrapped.Count - _Height);
            if (Follow) _Scroll = max;
            _Scroll = Math.Clamp(_Scroll, 0, max);

            CellStyle baseStyle = Theme.Text.WithBackground(BackgroundColor());
            for (int row = 0; row < _Height; row++)
            {
                int idx = _Scroll + row;
                if (idx >= _Wrapped.Count) break;
                CellStyle rowStyle = idx == _Match ? Theme.Selection : baseStyle;
                if (idx == _Match) SurfaceText.FillRow(surface, 0, row, width, rowStyle);
                surface.DrawStyledText(0, row, _Wrapped[idx], rowStyle);
            }

            if (_Wrapped.Count == 0)
            {
                SurfaceText.Draw(surface, 0, 0, T(EmptyText()), Theme.Muted.WithBackground(BackgroundColor()), width);
            }

            if (showPrompt)
            {
                string prompt = (_Searching ? "/" : T("Search") + ": ") + _Search + (_Searching ? "_" : "  (n/N)");
                SurfaceText.FillRow(surface, 0, height - 1, width, Theme.StatusBar);
                SurfaceText.Draw(surface, 0, height - 1, prompt, Theme.StatusBar, width);
            }
            else if (AllowFollow && Follow && height > 1)
            {
                string tag = "[" + T("follow") + "]";
                SurfaceText.Draw(surface, Math.Max(0, width - TextCells.Width(tag)), height - 1, tag, Theme.Accent.WithBackground(BackgroundColor()), width);
            }
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Build the unwrapped styled lines for the content.
        /// </summary>
        /// <returns>Lines.</returns>
        protected abstract IReadOnlyList<StyledText> BuildLines();

        /// <summary>
        /// English text shown when there is no content.
        /// </summary>
        /// <returns>Text.</returns>
        protected virtual string EmptyText()
        {
            return "No content.";
        }

        /// <summary>
        /// Background color of the viewer.
        /// </summary>
        /// <returns>Color.</returns>
        protected virtual Color BackgroundColor()
        {
            return Theme.Text.Background;
        }

        #endregion

        #region Private-Methods

        private void EnsureWrapped(int width)
        {
            if (!_Dirty && width == _WrappedWidth) return;
            _Wrapped = new List<StyledText>();
            foreach (StyledText line in BuildLines())
            {
                IReadOnlyList<StyledText> rows = line.Width <= width ? new List<StyledText> { line } : TextWrapper.Wrap(line, Math.Max(1, width));
                _Wrapped.AddRange(rows);
            }

            _WrappedPlain = _Wrapped.Select(l => l.ToPlainString()).ToList();
            _WrappedWidth = width;
            _Dirty = false;
        }

        private void ScrollBy(int delta)
        {
            _Scroll = Math.Max(0, _Scroll + delta);
            if (delta < 0) Follow = false;
        }

        private void BeginSearch()
        {
            _Searching = true;
            _Search = "";
            _Match = -1;
        }

        private bool HandleSearchKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Escape:
                    _Searching = false;
                    _Search = "";
                    _Match = -1;
                    return true;
                case KeyCode.Enter:
                    _Searching = false;
                    _Match = -1;
                    FindNext(true);
                    return true;
                case KeyCode.Backspace:
                    if (_Search.Length > 0) _Search = _Search.Substring(0, _Search.Length - 1);
                    return true;
                case KeyCode.Character:
                    if ((key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0 || key.Rune < 32) return true;
                    _Search += Char.ConvertFromUtf32(key.Rune);
                    return true;
                default:
                    return true;
            }
        }

        #endregion
    }
}
