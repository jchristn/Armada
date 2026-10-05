namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Theming;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A <see cref="DetailView"/> whose rows can carry an action: link rows (drawn in the link color) open their
    /// target with <c>Enter</c>, the way the dashboard's detail panels link to related records. Build it with
    /// <see cref="Reset"/>, <see cref="Section"/>, <see cref="Row"/>, and <see cref="Link"/> so actions stay aligned
    /// with rows. <c>y</c> still copies the value under the cursor. Not thread-safe.
    /// </summary>
    public class LinkDetailView : DetailView
    {
        #region Private-Members

        private readonly List<Action?> _Actions = new List<Action?>();
        private int _RestoreCursor = -1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remove every row (keeps the cursor when <paramref name="keepCursor"/> is true and the row still exists).
        /// </summary>
        /// <param name="keepCursor">Keep the cursor position across a rebuild (live refresh).</param>
        public void Reset(bool keepCursor = true)
        {
            int cursor = Cursor;
            Clear();
            _Actions.Clear();
            _RestoreCursor = keepCursor ? cursor : 0;
        }

        /// <summary>
        /// Add a section heading.
        /// </summary>
        /// <param name="title">English title.</param>
        public void Section(string title)
        {
            AddSection(title);
        }

        /// <summary>
        /// Add a plain row.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Value (already translated where needed).</param>
        /// <param name="style">Optional value style.</param>
        public void Row(string label, string? value, Func<ArmadaTheme, CellStyle>? style = null)
        {
            Add(label, value, style);
            _Actions.Add(null);
        }

        /// <summary>
        /// Add a row that runs an action on Enter (drawn as a link when an action is given).
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="value">Value.</param>
        /// <param name="action">Action, or null for a plain row.</param>
        public void Link(string label, string? value, Action? action)
        {
            Add(label, value, action != null ? (Func<ArmadaTheme, CellStyle>)(t => t.Link) : null);
            _Actions.Add(action);
        }

        /// <summary>
        /// True when the row under the cursor has an action.
        /// </summary>
        /// <returns>True for link rows.</returns>
        public bool CurrentIsLink()
        {
            return Cursor >= 0 && Cursor < _Actions.Count && _Actions[Cursor] != null;
        }

        /// <summary>
        /// Run the action of the row under the cursor.
        /// </summary>
        /// <returns>True when an action ran.</returns>
        public bool Activate()
        {
            if (!CurrentIsLink()) return false;
            Action? action = _Actions[Cursor];
            action?.Invoke();
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            ApplyRestore();
            if (key.Code == KeyCode.Enter) return Activate();
            return base.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            ApplyRestore();
            base.Render(surface);
        }

        #endregion

        #region Private-Methods

        private void ApplyRestore()
        {
            if (_RestoreCursor < 0) return;
            int target = Math.Min(_RestoreCursor, Math.Max(0, _Actions.Count - 1));
            _RestoreCursor = -1;
            for (int i = 0; i < target; i++) base.HandleKey(new KeyEvent(KeyCode.Down, 0, KeyModifiers.None));
        }

        #endregion
    }
}
