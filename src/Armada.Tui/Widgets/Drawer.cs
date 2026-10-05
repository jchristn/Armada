namespace Armada.Tui.Widgets
{
    using System;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// A right-docked panel (target detail, request detail, quick views) that a screen draws over the right part of its
    /// area while open. Keys go to the content; Esc raises <see cref="Closed"/>. Not thread-safe.
    /// </summary>
    public class Drawer : ContainerWidget
    {
        #region Public-Members

        /// <summary>
        /// English title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Content widget, or null.
        /// </summary>
        public IWidget? Content
        {
            get { return _Content; }
            set
            {
                if (_Content != null) Scope.Remove(_Content);
                _Content = value;
                if (value != null)
                {
                    AddChild(value);
                    Scope.Focus(value);
                }
            }
        }

        /// <summary>
        /// Width as a fraction of the host width (0.25..0.9). Default 0.45.
        /// </summary>
        public double WidthRatio
        {
            get { return _WidthRatio; }
            set { _WidthRatio = Math.Clamp(value, 0.25, 0.9); }
        }

        /// <summary>
        /// True while open (hosts render and route keys to it only while open).
        /// </summary>
        public bool IsOpen { get; private set; } = false;

        /// <summary>
        /// Raised after the drawer closes.
        /// </summary>
        public event EventHandler? Closed;

        #endregion

        #region Private-Members

        private IWidget? _Content = null;
        private double _WidthRatio = 0.45;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open with a title and content.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="content">Content.</param>
        public void Open(string title, IWidget content)
        {
            Title = title ?? "";
            Content = content;
            IsOpen = true;
        }

        /// <summary>
        /// Close and raise <see cref="Closed"/>.
        /// </summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            EventHandler? handler = Closed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        /// <summary>
        /// The rectangle the drawer occupies inside a host area.
        /// </summary>
        /// <param name="host">Host size.</param>
        /// <returns>Rectangle.</returns>
        public Rect RectIn(Size host)
        {
            int w = Math.Max(20, Math.Min(host.Width, (int)(host.Width * _WidthRatio)));
            return new Rect(host.Width - w, 0, w, host.Height);
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.Escape)
            {
                Close();
                return true;
            }

            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Dialog);
            // Column 0 stays blank: the screen's box around the open drawer is drawn there (see IRegionOverlayHost).
            SurfaceText.Draw(surface, 2, 0, T(Title), Theme.DialogBorder.WithAttribute(CellAttributes.Bold, true), width - 12);
            string hint = "Esc " + T("Close");
            SurfaceText.Draw(surface, Math.Max(2, width - TextCells.Width(hint) - 1), 0, hint, Theme.Dialog.WithForeground(Theme.Muted.Foreground), width);
            if (_Content != null && height > 2) Scope.RenderChild(surface, _Content, new Rect(2, 2, Math.Max(1, width - 3), height - 2));
        }

        #endregion
    }
}
