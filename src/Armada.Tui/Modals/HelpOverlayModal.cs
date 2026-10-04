namespace Armada.Tui.Modals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Input;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The help overlay (<c>?</c> or <c>F1</c>, W1.9): every key binding for the current screen and the global
    /// commands, grouped, generated from <see cref="CommandService"/> plus the documented widget keys (lists, forms,
    /// logs). Scrolls with arrows and PgUp/PgDn; Esc closes. Not thread-safe.
    /// </summary>
    public class HelpOverlayModal : ArmadaDialog
    {
        #region Public-Members

        /// <summary>
        /// Rendered rows (group headings and binding rows), for tests.
        /// </summary>
        public List<string> Lines { get; } = new List<string>();

        #endregion

        #region Private-Members

        private readonly List<KeyValuePair<string, string>> _Rows = new List<KeyValuePair<string, string>>();
        private int _Scroll = 0;
        private int _Height = 10;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="commands">Command registry.</param>
        /// <param name="screenTitle">English title of the current screen.</param>
        /// <param name="localizer">Localizer.</param>
        /// <param name="theme">Palette.</param>
        public HelpOverlayModal(CommandService commands, string screenTitle, ITextLocalizer localizer, ArmadaTheme theme)
            : base("Keyboard shortcuts", localizer, theme)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            List<ArmadaCommand> screen = commands.All().Where(c => c.Scope != null && c.Visible && c.Gestures.Count > 0).ToList();
            if (screen.Count > 0)
            {
                Heading(T(screenTitle));
                foreach (ArmadaCommand c in screen) Row(String.Join(", ", c.Gestures.Select(g => g.ToLabel())), T(c.Title));
            }

            foreach (IGrouping<string, ArmadaCommand> group in commands.All().Where(c => c.Scope == null && c.Visible && c.Gestures.Count > 0).GroupBy(c => c.Group))
            {
                Heading(T(group.Key));
                foreach (ArmadaCommand c in group) Row(String.Join(", ", c.Gestures.Select(g => g.ToLabel())), T(c.Title));
            }

            Heading(T("Lists"));
            Row("Up/Down PgUp/PgDn Home/End", T("Move"));
            Row("Enter", T("Open"));
            Row(". / Shift+F10", T("Row actions"));
            Row("Space, Shift+Up/Down, Ctrl+A, Esc", T("Select, extend, select page, clear"));
            Row("Left/Right, s, S", T("Focus column, sort, reverse sort"));
            Row("c", T("Columns"));
            Row("< >, z", T("Previous/next page, page size"));
            Heading(T("Forms"));
            Row("Tab / Shift+Tab", T("Next/previous field"));
            Row("Enter / Space", T("Open a select field"));
            Row("Ctrl+S", T("Save"));
            Row("Esc", T("Cancel"));
            Heading(T("Logs and viewers"));
            Row("f", T("Follow"));
            Row("/ or Ctrl+F, n/N", T("Search, next/previous"));
            Row("y", T("Copy"));
            Heading(T("Panes"));
            Row("Tab / Shift+Tab, F6", T("Next pane"));
            Row("[ ] / Alt+1..9", T("Previous/next tab"));
            FooterHint = " Esc " + T("Close") + " ";
            MinContentWidth = 50;
            MaxContentWidth = 90;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (HandleDismiss(key, null)) return true;
            if (key.Code == KeyCode.Character && (key.Rune == '?' || key.Rune == 'q'))
            {
                RequestClose(null);
                return true;
            }

            switch (key.Code)
            {
                case KeyCode.Up: _Scroll = Math.Max(0, _Scroll - 1); return true;
                case KeyCode.Down: _Scroll = Math.Min(Math.Max(0, _Rows.Count - _Height), _Scroll + 1); return true;
                case KeyCode.PageUp: _Scroll = Math.Max(0, _Scroll - _Height); return true;
                case KeyCode.PageDown: _Scroll = Math.Min(Math.Max(0, _Rows.Count - _Height), _Scroll + _Height); return true;
                case KeyCode.Home: _Scroll = 0; return true;
                case KeyCode.End: _Scroll = Math.Max(0, _Rows.Count - _Height); return true;
                case KeyCode.F1: RequestClose(null); return true;
                default: return true;
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override int MeasureContentWidth(int availableWidth)
        {
            return Math.Min(availableWidth, 84);
        }

        /// <inheritdoc />
        protected override int MeasureContentHeight(int contentWidth)
        {
            return Math.Min(_Rows.Count, 40);
        }

        /// <inheritdoc />
        protected override void RenderContent(ISurface content)
        {
            _Height = content.Size.Height;
            int width = content.Size.Width;
            int keyWidth = Math.Min(34, Math.Max(12, width / 2 - 4));
            for (int r = 0; r < _Height; r++)
            {
                int i = _Scroll + r;
                if (i >= _Rows.Count) break;
                KeyValuePair<string, string> row = _Rows[i];
                if (row.Value.Length == 0)
                {
                    SurfaceText.Draw(content, 0, r, row.Key, On(Theme.Accent), width);
                    continue;
                }

                SurfaceText.Draw(content, 2, r, row.Key, On(Theme.StatusKey), keyWidth);
                SurfaceText.Draw(content, keyWidth + 3, r, row.Value, Body(), width - keyWidth - 3);
            }
        }

        #endregion

        #region Private-Methods

        private void Heading(string title)
        {
            if (_Rows.Count > 0) _Rows.Add(new KeyValuePair<string, string>("", ""));
            _Rows.Add(new KeyValuePair<string, string>(title, ""));
            Lines.Add("# " + title);
        }

        private void Row(string keys, string description)
        {
            _Rows.Add(new KeyValuePair<string, string>(keys, description));
            Lines.Add(keys + " = " + description);
        }

        #endregion
    }
}
