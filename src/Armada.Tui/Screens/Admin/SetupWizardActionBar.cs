namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A row of setup wizard buttons that wraps onto more rows when they do not fit. <c>Left</c>/<c>Right</c> and
    /// <c>Tab</c> move between buttons. Not thread-safe.
    /// </summary>
    public class SetupWizardActionBar : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// Buttons in order. Never null.
        /// </summary>
        public IReadOnlyList<Button> Buttons
        {
            get { return _Buttons; }
        }

        /// <summary>
        /// Align the buttons to the right edge. Default false.
        /// </summary>
        public bool AlignRight { get; set; } = false;

        /// <inheritdoc />
        public override bool CanFocus
        {
            get { return Visible && _Buttons.Any(b => b.Visible); }
            set { }
        }

        #endregion

        #region Private-Members

        private readonly List<Button> _Buttons = new List<Button>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a button.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="onPressed">Action.</param>
        /// <returns>The button.</returns>
        public Button Add(string label, Action onPressed)
        {
            Button button = new Button(label, onPressed);
            _Buttons.Add(button);
            AddChild(button);
            return button;
        }

        /// <summary>
        /// Remove every button.
        /// </summary>
        public void Clear()
        {
            bool active = Scope.IsActive;
            if (active) Scope.SetActive(false);
            _Buttons.Clear();
            Scope.Clear();
            if (active) Scope.SetActive(true);
        }

        /// <summary>
        /// Rows needed at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows (0 with no visible button).</returns>
        public int HeightFor(int width)
        {
            List<Button> visible = _Buttons.Where(b => b.Visible).ToList();
            if (visible.Count == 0) return 0;
            int rows = 1;
            int x = 0;
            foreach (Button b in visible)
            {
                int w = Math.Min(width, b.DisplayWidth);
                if (x > 0 && x + w > width)
                {
                    rows++;
                    x = 0;
                }

                x += w + 1;
            }

            return rows;
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
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            List<Button> visible = _Buttons.Where(b => b.Visible).ToList();
            List<List<Button>> rows = new List<List<Button>>();
            List<Button> row = new List<Button>();
            int x = 0;
            foreach (Button b in visible)
            {
                int w = Math.Min(width, b.DisplayWidth);
                if (x > 0 && x + w > width)
                {
                    rows.Add(row);
                    row = new List<Button>();
                    x = 0;
                }

                row.Add(b);
                x += w + 1;
            }

            if (row.Count > 0) rows.Add(row);
            for (int y = 0; y < rows.Count && y < height; y++)
            {
                int total = rows[y].Sum(b => Math.Min(width, b.DisplayWidth)) + rows[y].Count - 1;
                int bx = AlignRight ? Math.Max(0, width - total) : 0;
                foreach (Button b in rows[y])
                {
                    int w = Math.Min(width - bx, b.DisplayWidth);
                    if (w <= 0) break;
                    Scope.RenderChild(surface, b, new Rect(bx, y, w, 1));
                    bx += w + 1;
                }
            }
        }

        #endregion
    }
}
