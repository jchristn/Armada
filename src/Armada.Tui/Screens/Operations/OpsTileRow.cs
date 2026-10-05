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
    /// A row of KPI or call-to-action tiles (the dashboard's cards): label, bold value, and detail, in equal-width
    /// boxes. <c>Left</c>/<c>Right</c> move between tiles that have an action, <c>Enter</c> runs it, and a click runs
    /// the clicked tile. Not focusable when no tile has an action. Not thread-safe.
    /// </summary>
    public class OpsTileRow : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Tiles.
        /// </summary>
        public List<OpsTile> Tiles { get; set; } = new List<OpsTile>();

        /// <summary>
        /// Selected tile index.
        /// </summary>
        public int Selected { get; private set; } = 0;

        /// <summary>
        /// Rows each tile takes (label, value, detail). Default 3.
        /// </summary>
        public int TileHeight { get; set; } = 3;

        /// <inheritdoc />
        public override bool CanFocus
        {
            get { return Visible && Tiles.Any(t => t.Action != null); }
            set { }
        }

        #endregion

        #region Private-Members

        private List<int> _Starts = new List<int>();
        private int _TileWidth = 10;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the selected tile's action.
        /// </summary>
        /// <returns>True when an action ran.</returns>
        public bool Activate()
        {
            if (Selected < 0 || Selected >= Tiles.Count || Tiles[Selected].Action == null) return false;
            Tiles[Selected].Action!();
            return true;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (Tiles.Count == 0) return false;
            switch (key.Code)
            {
                case KeyCode.Left:
                    return Move(-1);
                case KeyCode.Right:
                    return Move(1);
                case KeyCode.Enter:
                    return Activate();
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Press) return false;
            for (int i = _Starts.Count - 1; i >= 0; i--)
            {
                if (mouse.X >= _Starts[i])
                {
                    Selected = i;
                    return Activate() || true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            _Starts.Clear();
            if (Tiles.Count == 0 || width < 4) return;
            Selected = Math.Clamp(Selected, 0, Tiles.Count - 1);
            if (Tiles[Selected].Action == null) Move(1);
            _TileWidth = Math.Max(10, (width - (Tiles.Count - 1) * 2) / Tiles.Count);
            int x = 0;
            for (int i = 0; i < Tiles.Count; i++)
            {
                OpsTile tile = Tiles[i];
                _Starts.Add(x);
                bool cursor = IsFocused && i == Selected && tile.Action != null;
                CellStyle box = cursor ? Theme.SelectionInactive : Theme.Text;
                if (cursor) SurfaceText.FillRect(surface, new Rect(x, 0, Math.Min(_TileWidth, width - x), Math.Min(height, TileHeight)), box);
                string mark = cursor ? "> " : "  ";
                SurfaceText.Draw(surface, x, 0, mark + tile.Label + (tile.KeyHint != null ? "  (" + tile.KeyHint + ")" : ""), cursor ? box.WithForeground(Theme.Accent.Foreground) : Theme.Muted, Math.Min(_TileWidth, width - x));
                if (height > 1)
                {
                    CellStyle valueStyle = tile.ValueStyle != null ? tile.ValueStyle(Theme) : Theme.Accent;
                    SurfaceText.Draw(surface, x + 2, 1, tile.Value, valueStyle.WithAttribute(CellAttributes.Bold, true).WithBackground(box.Background), Math.Max(0, Math.Min(_TileWidth - 2, width - x - 2)));
                }

                int detailTop = tile.Value.Length > 0 ? 2 : 1;
                if (height > detailTop && tile.Detail.Length > 0)
                {
                    List<string> lines = TextCells.Wrap(tile.Detail, Math.Max(4, _TileWidth - 2));
                    for (int l = 0; l < lines.Count && detailTop + l < Math.Min(height, TileHeight); l++)
                    {
                        SurfaceText.Draw(surface, x + 2, detailTop + l, lines[l], Theme.Muted.WithBackground(box.Background), Math.Max(0, Math.Min(_TileWidth - 2, width - x - 2)));
                    }
                }

                x += _TileWidth + 2;
                if (x >= width) break;
            }
        }

        #endregion

        #region Private-Methods

        private bool Move(int delta)
        {
            if (Tiles.Count == 0) return false;
            int i = Selected;
            for (int step = 0; step < Tiles.Count; step++)
            {
                i = (i + delta + Tiles.Count) % Tiles.Count;
                if (Tiles[i].Action != null)
                {
                    Selected = i;
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
