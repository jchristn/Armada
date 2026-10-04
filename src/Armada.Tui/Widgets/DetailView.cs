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
    /// Detail pages' labeled values in sections (the dashboard's detail panels): a cursor row moves with Up/Down,
    /// <c>y</c> raises <see cref="CopyRequested"/> with the row's value (IDs, branches, paths), long values wrap, and
    /// values can carry a status style. Not thread-safe.
    /// </summary>
    public class DetailView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Raised when the user copies the value under the cursor.
        /// </summary>
        public event EventHandler<string>? CopyRequested;

        /// <summary>
        /// Index of the cursor row among value rows.
        /// </summary>
        public int Cursor
        {
            get { return _Cursor; }
        }

        #endregion

        #region Private-Members

        private readonly List<DetailRow> _Rows = new List<DetailRow>();
        private int _Cursor = 0;
        private int _Scroll = 0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remove all rows.
        /// </summary>
        public void Clear()
        {
            _Rows.Clear();
            _Cursor = 0;
            _Scroll = 0;
        }

        /// <summary>
        /// Add a section heading.
        /// </summary>
        /// <param name="title">English title.</param>
        public void AddSection(string title)
        {
            _Rows.Add(new DetailRow(title, null, null, true));
        }

        /// <summary>
        /// Add a labeled value.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Value (shown as-is), or null for a dash.</param>
        /// <param name="style">Optional style selector for the value (status coloring).</param>
        public void Add(string label, string? value, Func<ArmadaTheme, CellStyle>? style = null)
        {
            _Rows.Add(new DetailRow(label, value, style, false));
        }

        /// <summary>
        /// Value of the cursor row, or null.
        /// </summary>
        /// <returns>Value.</returns>
        public string? CurrentValue()
        {
            List<DetailRow> values = _Rows.Where(r => !r.IsSection).ToList();
            return _Cursor >= 0 && _Cursor < values.Count ? values[_Cursor].Value : null;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            int count = _Rows.Count(r => !r.IsSection);
            switch (key.Code)
            {
                case KeyCode.Up:
                    _Cursor = Math.Max(0, _Cursor - 1);
                    return true;
                case KeyCode.Down:
                    _Cursor = Math.Min(Math.Max(0, count - 1), _Cursor + 1);
                    return true;
                case KeyCode.Home:
                    _Cursor = 0;
                    return true;
                case KeyCode.End:
                    _Cursor = Math.Max(0, count - 1);
                    return true;
                case KeyCode.Character:
                    if (key.Modifiers == KeyModifiers.None && key.Rune == 'y')
                    {
                        string? value = CurrentValue();
                        EventHandler<string>? handler = CopyRequested;
                        if (value != null && handler != null) handler(this, value);
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 4 || height < 1) return;
            int labelWidth = Math.Min(Math.Max(10, _Rows.Where(r => !r.IsSection).Select(r => TextCells.Width(T(r.Label))).DefaultIfEmpty(8).Max() + 2), Math.Max(8, width / 3));
            int valueWidth = Math.Max(1, width - labelWidth);

            List<KeyValuePair<DetailRow, List<string>>> laid = new List<KeyValuePair<DetailRow, List<string>>>();
            foreach (DetailRow row in _Rows)
            {
                List<string> lines = row.IsSection ? new List<string> { T(row.Label) } : TextCells.Wrap(String.IsNullOrEmpty(row.Value) ? "-" : row.Value, valueWidth);
                laid.Add(new KeyValuePair<DetailRow, List<string>>(row, lines));
            }

            int cursorTop = 0;
            int y = 0;
            int valueIndex = 0;
            foreach (KeyValuePair<DetailRow, List<string>> entry in laid)
            {
                if (!entry.Key.IsSection)
                {
                    if (valueIndex == _Cursor) cursorTop = y;
                    valueIndex++;
                }

                y += entry.Value.Count + (entry.Key.IsSection ? 1 : 0);
            }

            if (cursorTop < _Scroll) _Scroll = cursorTop;
            if (cursorTop >= _Scroll + height) _Scroll = cursorTop - height + 1;

            y = -_Scroll;
            valueIndex = 0;
            foreach (KeyValuePair<DetailRow, List<string>> entry in laid)
            {
                DetailRow row = entry.Key;
                if (row.IsSection)
                {
                    if (y >= 0 && y < height) SurfaceText.Draw(surface, 0, y, entry.Value[0], Theme.Accent, width);
                    y += 2;
                    continue;
                }

                bool cursor = IsFocused && valueIndex == _Cursor;
                CellStyle valueStyle = row.Style != null ? row.Style(Theme) : Theme.Text;
                for (int i = 0; i < entry.Value.Count; i++)
                {
                    if (y >= 0 && y < height)
                    {
                        if (cursor) SurfaceText.FillRow(surface, 0, y, width, Theme.SelectionInactive);
                        if (i == 0) SurfaceText.Draw(surface, 0, y, T(row.Label), cursor ? Theme.SelectionInactive : Theme.Muted, labelWidth - 1);
                        SurfaceText.Draw(surface, labelWidth, y, entry.Value[i], cursor ? valueStyle.WithBackground(Theme.SelectionInactive.Background) : valueStyle, valueWidth);
                    }

                    y++;
                }

                valueIndex++;
            }
        }

        #endregion
    }
}
