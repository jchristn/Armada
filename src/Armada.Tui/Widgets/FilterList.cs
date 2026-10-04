namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A filterable, scrollable option list used by pickers and the command palette: a query (typed directly) narrows
    /// options by fuzzy score; Up/Down/PgUp/PgDn/Home/End move the cursor; optional check marks for multi-select. All
    /// widths are cell widths (CJK-safe). Not thread-safe.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    public class FilterList<T>
    {
        #region Public-Members

        /// <summary>
        /// Current query.
        /// </summary>
        public string Query
        {
            get { return _Query; }
            set
            {
                _Query = value ?? "";
                Refilter();
            }
        }

        /// <summary>
        /// Options matching the query, best first. Never null.
        /// </summary>
        public IReadOnlyList<SelectOption<T>> Visible
        {
            get { return _Visible; }
        }

        /// <summary>
        /// Cursor index into <see cref="Visible"/> (-1 when empty).
        /// </summary>
        public int Cursor
        {
            get { return _Cursor; }
        }

        /// <summary>
        /// Option under the cursor, or null.
        /// </summary>
        public SelectOption<T>? Current
        {
            get { return _Cursor >= 0 && _Cursor < _Visible.Count ? _Visible[_Cursor] : null; }
        }

        /// <summary>
        /// Checked options (multi-select). Never null.
        /// </summary>
        public HashSet<SelectOption<T>> Checked { get; } = new HashSet<SelectOption<T>>();

        /// <summary>
        /// Show check marks.
        /// </summary>
        public bool MultiSelect { get; set; } = false;

        /// <summary>
        /// Extra candidates (for example entity-jump entries) computed from the query and listed first, or null.
        /// </summary>
        public Func<string, IEnumerable<SelectOption<T>>>? Dynamic { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly List<SelectOption<T>> _All;
        private List<SelectOption<T>> _Visible = new List<SelectOption<T>>();
        private string _Query = "";
        private int _Cursor = -1;
        private int _Scroll = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="options">Options.</param>
        public FilterList(IEnumerable<SelectOption<T>> options)
        {
            _All = (options ?? Enumerable.Empty<SelectOption<T>>()).ToList();
            Refilter();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Move the cursor to the option holding a value.
        /// </summary>
        /// <param name="value">Value.</param>
        public void SelectValue(T value)
        {
            int idx = _Visible.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, value));
            if (idx >= 0) _Cursor = idx;
        }

        /// <summary>
        /// Handle navigation and query editing keys.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="page">Rows per page.</param>
        /// <returns>True when consumed.</returns>
        public bool HandleKey(KeyEvent key, int page)
        {
            switch (key.Code)
            {
                case KeyCode.Up:
                    Move(-1);
                    return true;
                case KeyCode.Down:
                    Move(1);
                    return true;
                case KeyCode.PageUp:
                    Move(-Math.Max(1, page));
                    return true;
                case KeyCode.PageDown:
                    Move(Math.Max(1, page));
                    return true;
                case KeyCode.Home:
                    _Cursor = _Visible.Count > 0 ? 0 : -1;
                    return true;
                case KeyCode.End:
                    _Cursor = _Visible.Count - 1;
                    return true;
                case KeyCode.Backspace:
                    if (_Query.Length > 0) Query = RemoveLastGrapheme(_Query);
                    return true;
                case KeyCode.Character:
                    if ((key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0)
                    {
                        if ((key.Modifiers & KeyModifiers.Ctrl) != 0 && Char.ToLowerInvariant((char)key.Rune) == 'u')
                        {
                            Query = "";
                            return true;
                        }

                        if (MultiSelect && (key.Modifiers & KeyModifiers.Ctrl) != 0 && Char.ToLowerInvariant((char)key.Rune) == 'a')
                        {
                            ToggleAllVisible();
                            return true;
                        }

                        return false;
                    }

                    if (MultiSelect && key.Rune == ' ')
                    {
                        Toggle(Current);
                        return true;
                    }

                    if (key.Rune < 32) return false;
                    Query = _Query + Char.ConvertFromUtf32(key.Rune);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Insert pasted text into the query.
        /// </summary>
        /// <param name="text">Text.</param>
        public void Paste(string text)
        {
            if (String.IsNullOrEmpty(text)) return;
            Query = _Query + text.Replace("\n", " ").Replace("\r", "");
        }

        /// <summary>
        /// Toggle a check mark (pinned and disabled options do not change).
        /// </summary>
        /// <param name="option">Option.</param>
        public void Toggle(SelectOption<T>? option)
        {
            if (option == null || option.Pinned || !option.Enabled) return;
            if (!Checked.Remove(option)) Checked.Add(option);
        }

        /// <summary>
        /// Check every visible option, or uncheck them all when all are already checked (pinned stay checked).
        /// </summary>
        public void ToggleAllVisible()
        {
            bool allChecked = _Visible.Where(o => o.Enabled).All(o => Checked.Contains(o));
            foreach (SelectOption<T> option in _Visible)
            {
                if (!option.Enabled || option.Pinned) continue;
                if (allChecked) Checked.Remove(option);
                else Checked.Add(option);
            }
        }

        /// <summary>
        /// Draw the visible rows into a surface region.
        /// </summary>
        /// <param name="surface">Surface.</param>
        /// <param name="top">First row.</param>
        /// <param name="height">Rows available.</param>
        /// <param name="theme">Palette.</param>
        /// <param name="background">Base style.</param>
        public void Render(ISurface surface, int top, int height, ArmadaTheme theme, CellStyle background)
        {
            int width = surface.Size.Width;
            if (height < 1 || width < 1) return;
            if (_Cursor < _Scroll) _Scroll = _Cursor;
            if (_Cursor >= _Scroll + height) _Scroll = _Cursor - height + 1;
            if (_Scroll < 0) _Scroll = 0;

            for (int row = 0; row < height; row++)
            {
                int idx = _Scroll + row;
                int y = top + row;
                if (idx >= _Visible.Count) break;
                SelectOption<T> option = _Visible[idx];
                bool cursor = idx == _Cursor;
                CellStyle style = cursor ? theme.Selection : !option.Enabled ? background.WithForeground(theme.Disabled.Foreground) : background;
                SurfaceText.FillRow(surface, 0, y, width, style);
                string prefix = MultiSelect ? (Checked.Contains(option) ? "[x] " : "[ ] ") : (cursor ? "> " : "  ");
                int x = SurfaceText.Draw(surface, 0, y, prefix, style, width);
                int detailWidth = String.IsNullOrEmpty(option.Detail) ? 0 : Math.Min(TextCells.Width(option.Detail), Math.Max(0, (width - x) / 2));
                int labelWidth = width - x - (detailWidth > 0 ? detailWidth + 1 : 0);
                SurfaceText.Draw(surface, x, y, option.Label, style, labelWidth);
                if (detailWidth > 0)
                {
                    CellStyle detailStyle = cursor ? style : style.WithForeground(theme.Muted.Foreground);
                    SurfaceText.Draw(surface, width - detailWidth, y, TextCells.PadLeft(option.Detail, detailWidth), detailStyle, detailWidth);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static string RemoveLastGrapheme(string text)
        {
            IReadOnlyList<TUIKit.Unicode.Grapheme> parts = TUIKit.Unicode.Graphemes.Split(text);
            if (parts.Count == 0) return "";
            return String.Concat(parts.Take(parts.Count - 1).Select(g => g.Text));
        }

        private void Move(int delta)
        {
            if (_Visible.Count == 0)
            {
                _Cursor = -1;
                return;
            }

            _Cursor = Math.Clamp(_Cursor + delta, 0, _Visible.Count - 1);
        }

        private void Refilter()
        {
            List<SelectOption<T>> result = new List<SelectOption<T>>();
            if (Dynamic != null) result.AddRange(Dynamic(_Query));
            if (String.IsNullOrWhiteSpace(_Query))
            {
                result.AddRange(_All);
            }
            else
            {
                result.AddRange(_All
                    .Select(o => new KeyValuePair<SelectOption<T>, int>(o, Math.Max(FuzzyMatcher.Score(_Query, o.Label), FuzzyMatcher.Score(_Query, o.Detail) - 5)))
                    .Where(p => p.Value >= 0)
                    .OrderByDescending(p => p.Value)
                    .Select(p => p.Key));
            }

            _Visible = result;
            _Cursor = _Visible.Count > 0 ? 0 : -1;
            _Scroll = 0;
        }

        #endregion
    }
}
