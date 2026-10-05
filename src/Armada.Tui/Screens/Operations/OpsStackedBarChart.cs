namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// A stacked bar chart drawn with ASCII cells (the dashboard's Mission History chart): one column group per bucket
    /// with complete, failed, and other segments, y-axis ticks, and x labels spaced to fit. Bars use <c>#</c> for
    /// complete, <c>x</c> for failed, and <c>.</c> for other, so the series read without color. Not focusable.
    /// </summary>
    public class OpsStackedBarChart : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Buckets: label and the three segment counts.
        /// </summary>
        public List<OpsChartBucket> Buckets { get; set; } = new List<OpsChartBucket>();

        /// <summary>
        /// English text shown when there are no buckets.
        /// </summary>
        public string EmptyText { get; set; } = "No mission data for this time range";

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The y-axis ticks for a maximum (the dashboard's computeYTicks).
        /// </summary>
        /// <param name="max">Maximum.</param>
        /// <returns>Ticks.</returns>
        public static List<int> Ticks(int max)
        {
            List<int> ticks = new List<int>();
            if (max <= 0)
            {
                ticks.Add(0);
                return ticks;
            }

            int step = Math.Max(1, (int)Math.Ceiling(max / 4.0));
            for (int i = 0; i <= max; i += step) ticks.Add(i);
            if (ticks[ticks.Count - 1] < max) ticks.Add(ticks[ticks.Count - 1] + step);
            return ticks;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (Buckets.Count == 0)
            {
                SurfaceText.Draw(surface, 0, Math.Max(0, height / 2), T(EmptyText), Theme.Muted, width);
                return;
            }

            int max = Math.Max(1, Buckets.Max(b => b.Complete + b.Failed + b.Other));
            List<int> ticks = Ticks(max);
            int yMax = Math.Max(1, ticks[ticks.Count - 1]);
            int axis = Math.Max(3, TextCells.Width(yMax.ToString(CultureInfo.InvariantCulture)) + 1);
            int plotHeight = Math.Max(2, height - 1);
            int plotWidth = Math.Max(1, width - axis - 1);
            int group = Math.Max(1, plotWidth / Buckets.Count);
            int shown = Math.Min(Buckets.Count, plotWidth);
            int start = Buckets.Count - shown;
            int barWidth = Math.Max(1, group > 2 ? group - 1 : group);

            foreach (int tick in ticks)
            {
                int ty = plotHeight - 1 - (int)Math.Round((double)tick / yMax * (plotHeight - 1));
                if (ty < 0 || ty >= plotHeight) continue;
                string label = tick.ToString(CultureInfo.InvariantCulture);
                SurfaceText.Draw(surface, axis - 1 - TextCells.Width(label), ty, label, Theme.Muted, axis);
            }

            for (int y = 0; y < plotHeight; y++) surface.DrawText(axis - 1, y, "|", Theme.Border);

            for (int i = 0; i < shown; i++)
            {
                OpsChartBucket b = Buckets[start + i];
                int x = axis + i * group;
                int completeRows = Rows(b.Complete, yMax, plotHeight);
                int failedRows = Rows(b.Failed, yMax, plotHeight);
                int otherRows = Rows(b.Other, yMax, plotHeight);
                int y = plotHeight - 1;
                Fill(surface, x, ref y, completeRows, barWidth, "#", Theme.Success);
                Fill(surface, x, ref y, failedRows, barWidth, "x", Theme.Error);
                Fill(surface, x, ref y, otherRows, barWidth, ".", Theme.Muted);
            }

            int labelWidth = Math.Max(1, Buckets.Where((b, i) => i >= start).Select(b => TextCells.Width(b.Label)).DefaultIfEmpty(5).Max() + 1);
            int every = Math.Max(1, (int)Math.Ceiling((double)labelWidth / group));
            for (int i = 0; i < shown; i += every)
            {
                int x = axis + i * group;
                if (x >= width) break;
                SurfaceText.Draw(surface, x, height - 1, Buckets[start + i].Label, Theme.Muted, Math.Min(labelWidth, width - x));
            }
        }

        #endregion

        #region Private-Methods

        private static int Rows(int value, int yMax, int plotHeight)
        {
            if (value <= 0) return 0;
            return Math.Max(1, (int)Math.Round((double)value / yMax * plotHeight));
        }

        private static void Fill(ISurface surface, int x, ref int y, int rows, int width, string glyph, CellStyle style)
        {
            for (int r = 0; r < rows && y >= 0; r++)
            {
                SurfaceText.Draw(surface, x, y, new string(glyph[0], width), style, width);
                y--;
            }
        }

        #endregion
    }
}
