namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Armada.Tui.Text;
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// Chart panel (mission history, request activity, token usage, endpoint health) over TUIKit's braille line chart,
    /// bar chart, and sparkline, with a title, a legend showing each series' total (so values are readable without
    /// color), and a text-table export for copying. Not thread-safe.
    /// </summary>
    public class ChartView : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// English title.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Rendering style.
        /// </summary>
        public ChartKindEnum Kind { get; set; } = ChartKindEnum.Line;

        /// <summary>
        /// Series. The line chart and sparkline draw the first series; the legend lists all. Never null.
        /// </summary>
        public List<ChartSeries> Series { get; } = new List<ChartSeries>();

        /// <summary>
        /// Category labels (bar chart labels, table row labels). Never null.
        /// </summary>
        public List<string> Labels { get; } = new List<string>();

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The data as a tab-separated table (header row, then one row per label or index).
        /// </summary>
        /// <returns>Table text.</returns>
        public string ToTextTable()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("label");
            foreach (ChartSeries s in Series) sb.Append('\t').Append(s.Name);
            sb.Append('\n');
            int rows = Series.Count == 0 ? 0 : Series.Max(s => s.Values.Count);
            for (int i = 0; i < rows; i++)
            {
                sb.Append(i < Labels.Count ? Labels[i] : i.ToString(CultureInfo.InvariantCulture));
                foreach (ChartSeries s in Series)
                {
                    sb.Append('\t').Append(i < s.Values.Count ? s.Values[i].ToString(CultureInfo.InvariantCulture) : "");
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 4 || height < 1) return;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            int y = 0;
            if (!String.IsNullOrEmpty(Title))
            {
                SurfaceText.Draw(surface, 0, y++, T(Title), Theme.Accent, width);
            }

            string legend = String.Join("   ", Series.Select(s => T(s.Name) + ": " + Localizer.FormatNumber((long)Math.Round(s.Values.Sum()))));
            if (legend.Length > 0 && y < height) SurfaceText.Draw(surface, 0, y++, legend, Theme.Muted, width);
            int area = height - y;
            if (area < 1 || Series.Count == 0 || Series[0].Values.Count == 0)
            {
                if (area >= 1) SurfaceText.Draw(surface, 0, y, T("No data."), Theme.Muted, width);
                return;
            }

            Rect chartRect = new Rect(0, y, width, area);
            ISurface view = new SurfaceView(surface, chartRect);
            switch (Kind)
            {
                case ChartKindEnum.Bar:
                    BarChart bars = new BarChart();
                    bars.Color = Theme.Success.Foreground;
                    for (int i = 0; i < Series[0].Values.Count; i++)
                    {
                        bars.Add(i < Labels.Count ? Labels[i] : (i + 1).ToString(CultureInfo.InvariantCulture), Series[0].Values[i]);
                    }

                    bars.Render(view);
                    break;
                case ChartKindEnum.Sparkline:
                    Sparkline spark = new Sparkline();
                    spark.LineColor = Theme.Accent.Foreground;
                    spark.SetValues(Series[0].Values);
                    spark.Render(view);
                    break;
                default:
                    LineChart line = new LineChart(Series[0].Values);
                    line.Color = Theme.Accent.Foreground;
                    line.Render(view);
                    break;
            }
        }

        #endregion
    }
}
