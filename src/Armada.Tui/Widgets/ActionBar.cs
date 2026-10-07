namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A detail page's header actions (the dashboard's <c>PageHeader</c> buttons): buttons laid out left to right that
    /// wrap onto more lines when the terminal is narrow. <c>Left</c>/<c>Right</c> (and <c>Up</c>/<c>Down</c> across
    /// lines) move between buttons, <c>Enter</c> or <c>Space</c> presses. Hidden buttons are skipped. Not thread-safe.
    /// </summary>
    public class ActionBar : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// True when the bar can take focus: never while it has no buttons (focus would land on nothing visible).
        /// </summary>
        public override bool CanFocus
        {
            get { return _CanFocus && Scope.Children.Count > 0; }
            set { _CanFocus = value; }
        }

        /// <summary>
        /// Buttons in order. Never null.
        /// </summary>
        public IReadOnlyList<Button> Buttons
        {
            get { return _Buttons; }
        }

        /// <summary>
        /// Gap between buttons in cells. Default 1; clamped to 0..4.
        /// </summary>
        public int Gap
        {
            get { return _Gap; }
            set { _Gap = Math.Clamp(value, 0, 4); }
        }

        #endregion

        #region Private-Members

        private bool _CanFocus = true;

        private readonly List<Button> _Buttons = new List<Button>();
        private int _Gap = 1;
        private int _LastWidth = 80;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a button.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="action">Action.</param>
        /// <param name="hint">Key hint drawn inside the button, or null.</param>
        /// <returns>The button.</returns>
        public Button Add(string label, Action action, string? hint = null)
        {
            Button button = new Button(label, action);
            button.Hint = hint;
            _Buttons.Add(button);
            AddChild(button);
            return button;
        }

        /// <summary>
        /// Remove every button.
        /// </summary>
        public void ClearButtons()
        {
            foreach (Button b in _Buttons) Scope.Remove(b);
            _Buttons.Clear();
        }

        /// <summary>
        /// Find a button by English label.
        /// </summary>
        /// <param name="label">Label.</param>
        /// <returns>Button or null.</returns>
        public Button? Find(string label)
        {
            return _Buttons.FirstOrDefault(b => String.Equals(b.Label, label, StringComparison.Ordinal));
        }

        /// <summary>
        /// Labels of the visible buttons (tests and snapshots).
        /// </summary>
        /// <returns>Labels.</returns>
        public List<string> VisibleLabels()
        {
            return _Buttons.Where(b => b.Visible).Select(b => b.Label).ToList();
        }

        /// <summary>
        /// Lines needed at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Line count (0 when no button is visible).</returns>
        public int LinesFor(int width)
        {
            List<KeyValuePair<Button, Rect>> layout = Layout(width);
            return layout.Count == 0 ? 0 : layout.Max(l => l.Value.Y) + 1;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Scope.HandleKey(key)) return true;
            // Alt+arrows belong to the shell (Back and Forward), as in ArmadaGrid.
            if ((key.Modifiers & KeyModifiers.Alt) != 0) return false;
            if (key.Code == KeyCode.Right) return Scope.Move(true);
            if (key.Code == KeyCode.Left) return Scope.Move(false);
            if (key.Code == KeyCode.Down || key.Code == KeyCode.Up) return MoveLine(key.Code == KeyCode.Down);
            return false;
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, Math.Min(available.Height, LinesFor(available.Width)));
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            _LastWidth = surface.Size.Width;
            SurfaceText.FillRect(surface, new Rect(0, 0, surface.Size.Width, surface.Size.Height), Theme.Text);
            foreach (KeyValuePair<Button, Rect> entry in Layout(surface.Size.Width))
            {
                if (entry.Value.Y >= surface.Size.Height) continue;
                Scope.RenderChild(surface, entry.Key, entry.Value);
            }
        }

        #endregion

        #region Private-Methods

        private List<KeyValuePair<Button, Rect>> Layout(int width)
        {
            List<KeyValuePair<Button, Rect>> result = new List<KeyValuePair<Button, Rect>>();
            int x = 0;
            int y = 0;
            foreach (Button b in _Buttons)
            {
                if (!b.Visible) continue;
                int w = Math.Min(Math.Max(1, width), b.DisplayWidth);
                if (x > 0 && x + w > width)
                {
                    x = 0;
                    y++;
                }

                result.Add(new KeyValuePair<Button, Rect>(b, new Rect(x, y, w, 1)));
                x += w + _Gap;
            }

            return result;
        }

        private bool MoveLine(bool down)
        {
            List<KeyValuePair<Button, Rect>> layout = Layout(_LastWidth);
            int idx = layout.FindIndex(l => ReferenceEquals(l.Key, Scope.Focused));
            if (idx < 0) return false;
            Rect current = layout[idx].Value;
            int targetY = current.Y + (down ? 1 : -1);
            List<KeyValuePair<Button, Rect>> line = layout.Where(l => l.Value.Y == targetY).ToList();
            if (line.Count == 0) return false;
            KeyValuePair<Button, Rect> best = line.OrderBy(l => Math.Abs(l.Value.X - current.X)).First();
            return Scope.Focus(best.Key);
        }

        #endregion
    }
}
