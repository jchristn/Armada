namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A row of tabs (hub tabs, login modes). Left/Right or <c>[</c>/<c>]</c> move between tabs, <c>Alt+1</c>..<c>Alt+9</c>
    /// jump, clicks select. Raises <see cref="SelectedChanged"/>. Not thread-safe.
    /// </summary>
    public class TabStrip : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Tab keys in order.
        /// </summary>
        public List<string> Keys { get; } = new List<string>();

        /// <summary>
        /// English labels in order (translated when drawn).
        /// </summary>
        public List<string> Labels { get; } = new List<string>();

        /// <summary>
        /// Selected index (-1 when empty).
        /// </summary>
        public int SelectedIndex
        {
            get { return _Selected; }
            set { Select(value, false); }
        }

        /// <summary>
        /// Selected key, or null.
        /// </summary>
        public string? SelectedKey
        {
            get { return _Selected >= 0 && _Selected < Keys.Count ? Keys[_Selected] : null; }
        }

        /// <summary>
        /// Raised after the user changes the selected tab.
        /// </summary>
        public event EventHandler<ValueChangedEventArgs<string>>? SelectedChanged;

        #endregion

        #region Private-Members

        private int _Selected = -1;
        private readonly List<int> _Starts = new List<int>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a tab.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <param name="label">English label.</param>
        public void Add(string key, string label)
        {
            Keys.Add(key);
            Labels.Add(label);
            if (_Selected < 0) _Selected = 0;
        }

        /// <summary>
        /// Select a tab by key without raising the event.
        /// </summary>
        /// <param name="key">Key.</param>
        public void SelectKey(string? key)
        {
            int idx = key == null ? -1 : Keys.IndexOf(key);
            if (idx >= 0) _Selected = idx;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Keys.Count == 0) return false;
            if (key.Code == KeyCode.Right || (key.Code == KeyCode.Character && key.Rune == ']' && key.Modifiers == KeyModifiers.None))
                return Select((_Selected + 1) % Keys.Count, true);
            if (key.Code == KeyCode.Left || (key.Code == KeyCode.Character && key.Rune == '[' && key.Modifiers == KeyModifiers.None))
                return Select((_Selected - 1 + Keys.Count) % Keys.Count, true);
            return HandleGlobalKey(key);
        }

        /// <summary>
        /// Keys that switch tabs even when focus is inside the tab content (<c>[</c>, <c>]</c>, <c>Alt+1..9</c>).
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when consumed.</returns>
        public bool HandleGlobalKey(KeyEvent key)
        {
            if (Keys.Count == 0 || key.Code != KeyCode.Character) return false;
            if ((key.Modifiers & KeyModifiers.Alt) != 0 && key.Rune >= '1' && key.Rune <= '9')
            {
                int idx = key.Rune - '1';
                if (idx < Keys.Count) return Select(idx, true);
                return false;
            }

            if (key.Modifiers != KeyModifiers.None) return false;
            if (key.Rune == ']') return Select((_Selected + 1) % Keys.Count, true);
            if (key.Rune == '[') return Select((_Selected - 1 + Keys.Count) % Keys.Count, true);
            return false;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press || mouse.Y != 0) return false;
            for (int i = _Starts.Count - 1; i >= 0; i--)
            {
                if (mouse.X >= _Starts[i]) return Select(i, true);
            }

            return false;
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, 1);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _Starts.Clear();
            int x = 0;
            SurfaceText.FillRow(surface, 0, 0, surface.Size.Width, Theme.Text);
            for (int i = 0; i < Keys.Count; i++)
            {
                string label = " " + T(Labels[i]) + " ";
                bool active = i == _Selected;
                CellStyle style = active ? (IsFocused ? Theme.Selection : Theme.TabActive) : Theme.TabInactive;
                _Starts.Add(x);
                // The selected tab is bracketed; while the tab bar itself has focus its brackets become arrows, so the
                // focused tab reads without color (and the bar's box is drawn focused, see RegionFrames).
                string shown = active ? (IsFocused ? ">" + label.Trim() + "<" : "[" + label.Trim() + "]") : label;
                x += SurfaceText.Draw(surface, x, 0, shown, style, surface.Size.Width - x);
                x += 1;
                if (x >= surface.Size.Width) break;
            }
        }

        #endregion

        #region Private-Methods

        private bool Select(int index, bool raise)
        {
            if (index < 0 || index >= Keys.Count) return false;
            if (index == _Selected) return true;
            string old = SelectedKey ?? "";
            _Selected = index;
            if (raise)
            {
                EventHandler<ValueChangedEventArgs<string>>? handler = SelectedChanged;
                if (handler != null) handler(this, new ValueChangedEventArgs<string>(old, Keys[index]));
            }

            return true;
        }

        #endregion
    }
}
