namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// The dashboard's overview cards as one or two lines of text: "Total Deployments 12 | Pending Approval 1 | ...".
    /// Items wrap to the next line when the width runs out. Values are drawn in the accent color, and an item may
    /// carry its own style (a failure count in the error color); labels are translated. Not focusable.
    /// </summary>
    public class KpiStrip : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Items in order. Never null.
        /// </summary>
        public List<KpiItem> Items { get; private set; } = new List<KpiItem>();

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the items.
        /// </summary>
        /// <param name="items">Items.</param>
        public void SetItems(IEnumerable<KpiItem>? items)
        {
            Items = items != null ? new List<KpiItem>(items) : new List<KpiItem>();
        }

        /// <summary>
        /// Lines needed at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Line count (0 when there are no items).</returns>
        public int LinesFor(int width)
        {
            if (Items.Count == 0) return 0;
            int lines = 1;
            int x = 0;
            foreach (KpiItem item in Items)
            {
                int w = TextCells.Width(Segment(item));
                if (x > 0 && x + 3 + w > width)
                {
                    lines++;
                    x = 0;
                }

                x += (x > 0 ? 3 : 0) + w;
            }

            return lines;
        }

        /// <summary>
        /// Plain text of the strip (tests and snapshots).
        /// </summary>
        /// <returns>Text.</returns>
        public string ToText()
        {
            List<string> parts = new List<string>();
            foreach (KpiItem item in Items) parts.Add(Segment(item));
            return String.Join(" | ", parts);
        }

        /// <inheritdoc />
        public override Size Measure(Size available)
        {
            return new Size(available.Width, Math.Min(available.Height, LinesFor(available.Width)));
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            int x = 0;
            int y = 0;
            foreach (KpiItem item in Items)
            {
                string label = T(item.Label) + " ";
                string value = item.Value;
                int w = TextCells.Width(label) + TextCells.Width(value);
                if (x > 0 && x + 3 + w > width)
                {
                    y++;
                    x = 0;
                }

                if (y >= height) return;
                if (x > 0)
                {
                    x = SurfaceText.Draw(surface, x, y, " | ", Theme.Border, width - x) + x;
                }

                x += SurfaceText.Draw(surface, x, y, label, Theme.Muted, Math.Max(0, width - x));
                CellStyle valueStyle = item.Style != null ? item.Style(Theme) : Theme.Accent;
                x += SurfaceText.Draw(surface, x, y, value, valueStyle, Math.Max(0, width - x));
            }
        }

        #endregion

        #region Private-Methods

        private string Segment(KpiItem item)
        {
            return T(item.Label) + " " + item.Value;
        }

        #endregion
    }
}
