namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A horizontal row of buttons (form and dialog actions). Left/Right and Tab move between buttons. Not thread-safe.
    /// </summary>
    public class ButtonRow : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// True when the row can take focus: never while it has no buttons (focus would land on nothing visible).
        /// </summary>
        public override bool CanFocus
        {
            get { return _CanFocus && Scope.Children.Count > 0; }
            set { _CanFocus = value; }
        }

        /// <summary>
        /// Buttons in order.
        /// </summary>
        public IReadOnlyList<Button> Buttons
        {
            get { return Scope.Children.OfType<Button>().ToList(); }
        }

        /// <summary>
        /// Cells between buttons. Default 2.
        /// </summary>
        public int Gap { get; set; } = 2;

        #endregion

        #region Private-Members

        private bool _CanFocus = true;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a button.
        /// </summary>
        /// <param name="button">Button.</param>
        /// <returns>The button.</returns>
        public Button Add(Button button)
        {
            return AddChild(button);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Right) return Scope.Move(true);
            if (key.Code == KeyCode.Left) return Scope.Move(false);
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
            int x = 0;
            foreach (Button button in Buttons)
            {
                if (!button.Visible) continue;
                int w = button.DisplayWidth;
                if (x >= surface.Size.Width) break;
                Scope.RenderChild(surface, button, new Rect(x, 0, Math.Min(w, surface.Size.Width - x), 1));
                x += w + Gap;
            }
        }

        #endregion
    }
}
