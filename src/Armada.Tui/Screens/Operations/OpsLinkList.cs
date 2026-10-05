namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// A short list of lines, some of which act on <c>Enter</c> or a click (alert banners with View, recent signals,
    /// linked records). <c>Up</c>/<c>Down</c> move between lines; focusable only when some line has an action.
    /// Not thread-safe.
    /// </summary>
    public class OpsLinkList : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Lines.
        /// </summary>
        public List<OpsLinkItem> Items { get; set; } = new List<OpsLinkItem>();

        /// <summary>
        /// Cursor line.
        /// </summary>
        public int Cursor { get; private set; } = 0;

        /// <summary>
        /// Not a focus stop while there is nothing to act on (see <see cref="TUIKit.Widgets.IHideable"/>).
        /// </summary>
        public override bool IsVisible
        {
            get { return base.IsVisible && Items.Any(i => i.Action != null); }
        }

        #endregion

        #region Private-Members

        private int _Scroll = 0;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Items.Count == 0) return false;
            switch (key.Code)
            {
                case KeyCode.Up:
                    if (Cursor == 0) return false;
                    Cursor--;
                    return true;
                case KeyCode.Down:
                    if (Cursor >= Items.Count - 1) return false;
                    Cursor++;
                    return true;
                case KeyCode.Enter:
                    Cursor = Math.Clamp(Cursor, 0, Items.Count - 1);
                    if (Items[Cursor].Action == null) return false;
                    Items[Cursor].Action!();
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press) return false;
            int idx = _Scroll + mouse.Y;
            if (idx < 0 || idx >= Items.Count) return false;
            Cursor = idx;
            Items[idx].Action?.Invoke();
            return true;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (Items.Count == 0) return;
            Cursor = Math.Clamp(Cursor, 0, Items.Count - 1);
            if (Cursor < _Scroll) _Scroll = Cursor;
            if (Cursor >= _Scroll + height) _Scroll = Cursor - height + 1;
            for (int row = 0; row < height; row++)
            {
                int idx = _Scroll + row;
                if (idx >= Items.Count) break;
                OpsLinkItem item = Items[idx];
                bool cursor = IsFocused && idx == Cursor;
                CellStyle style = item.Style != null ? item.Style(Theme) : Theme.Text;
                if (cursor)
                {
                    SurfaceText.FillRow(surface, 0, row, width, Theme.GridCursor);
                    style = style.WithBackground(Theme.GridCursor.Background);
                }

                int nw = TextCells.Width(item.Note);
                SurfaceText.Draw(surface, 0, row, (item.Action != null ? (cursor ? "> " : "  ") : "  ") + item.Text, style, Math.Max(1, width - nw - 1));
                if (nw > 0) SurfaceText.Draw(surface, width - nw, row, item.Note, cursor ? Theme.GridCursor : Theme.Muted, nw);
            }
        }

        #endregion
    }
}
