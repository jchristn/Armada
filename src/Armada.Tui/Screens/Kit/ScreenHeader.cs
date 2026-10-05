namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The page header of an Activity or System screen: title, subtitle, a right-aligned status text, and the page's
    /// action buttons (flowing onto more rows when they do not fit). Buttons take focus with <c>Tab</c> and move with
    /// <c>Left</c>/<c>Right</c>. Not thread-safe.
    /// </summary>
    public class ScreenHeader : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// English title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// English subtitle, or empty.
        /// </summary>
        public string Subtitle { get; set; } = "";

        /// <summary>
        /// Status text (already translated) drawn at the right of the title row, or empty.
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// Style for the status text, or null for muted.
        /// </summary>
        public Func<Armada.Tui.Theming.ArmadaTheme, CellStyle>? StatusStyle { get; set; } = null;

        /// <summary>
        /// Buttons in order. Never null.
        /// </summary>
        public IReadOnlyList<Button> Buttons
        {
            get { return _Buttons; }
        }

        /// <inheritdoc />
        public override bool CanFocus
        {
            get { return _Buttons.Any(b => b.Visible); }
            set { }
        }

        #endregion

        #region Private-Members

        private readonly List<Button> _Buttons = new List<Button>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="subtitle">English subtitle.</param>
        public ScreenHeader(string title, string subtitle = "")
        {
            Title = title ?? "";
            Subtitle = subtitle ?? "";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add a button.
        /// </summary>
        /// <param name="label">English label.</param>
        /// <param name="onPressed">Action.</param>
        /// <param name="hint">Key hint shown inside the button, or null.</param>
        /// <returns>The button.</returns>
        public Button AddButton(string label, Action onPressed, string? hint = null)
        {
            Button button = new Button(label, onPressed);
            button.Hint = hint;
            _Buttons.Add(button);
            AddChild(button);
            return button;
        }

        /// <summary>
        /// Rows needed at a width: one for the title (two with a subtitle that does not fit beside it), plus the
        /// button rows.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows.</returns>
        public int HeightFor(int width)
        {
            int rows = 1 + (SubtitleOnOwnRow(width) ? 1 : 0);
            List<Button> visible = _Buttons.Where(b => b.Visible).ToList();
            if (visible.Count == 0) return rows;
            int lines = 1;
            int x = 0;
            foreach (Button b in visible)
            {
                int w = b.DisplayWidth;
                if (x > 0 && x + w > width)
                {
                    lines++;
                    x = 0;
                }

                x += w + 1;
            }

            return rows + lines;
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
            if (width < 4 || height < 1) return;
            string status = Status ?? "";
            int statusWidth = TextCells.Width(status);
            int x = SurfaceText.Draw(surface, 0, 0, T(Title), Theme.Accent.WithAttribute(CellAttributes.Bold, true), Math.Max(0, width - statusWidth - 1));
            int y = 1;
            if (Subtitle.Length > 0)
            {
                if (SubtitleOnOwnRow(width))
                {
                    if (height > 1) SurfaceText.Draw(surface, 0, 1, T(Subtitle), Theme.Muted, width);
                    y = 2;
                }
                else
                {
                    SurfaceText.Draw(surface, x + 2, 0, T(Subtitle), Theme.Muted, Math.Max(0, width - x - statusWidth - 3));
                }
            }

            if (statusWidth > 0) SurfaceText.Draw(surface, Math.Max(0, width - statusWidth), 0, status, StatusStyle != null ? StatusStyle(Theme) : Theme.Muted, width);
            int bx = 0;
            foreach (Button b in _Buttons)
            {
                if (!b.Visible) continue;
                int w = Math.Min(b.DisplayWidth, width);
                if (bx > 0 && bx + w > width)
                {
                    y++;
                    bx = 0;
                }

                if (y >= height) break;
                Scope.RenderChild(surface, b, new Rect(bx, y, w, 1));
                bx += w + 1;
            }
        }

        #endregion

        #region Private-Methods

        private bool SubtitleOnOwnRow(int width)
        {
            if (Subtitle.Length == 0) return false;
            return TextCells.Width(T(Title)) + TextCells.Width(T(Subtitle)) + TextCells.Width(Status ?? "") + 4 > width;
        }

        #endregion
    }
}
