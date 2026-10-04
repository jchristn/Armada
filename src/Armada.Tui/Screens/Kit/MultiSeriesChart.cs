namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// A multi-series time chart (TUIKit's charts draw one series): stacked bars or one marker line per series, a
    /// y-axis scale, sparse x labels, and a legend. Each series has its own color and its own glyph, so the chart
    /// does not depend on color. When there are more buckets than columns, adjacent buckets are summed. Not
    /// focusable. Not thread-safe.
    /// </summary>
    public class MultiSeriesChart : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// English title, or empty.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Bars (stacked) or lines.
        /// </summary>
        public ChartKindEnum Kind { get; set; } = ChartKindEnum.Bar;

        /// <summary>
        /// Series (names are English or data labels). Never null.
        /// </summary>
        public List<ChartSeries> Series { get; } = new List<ChartSeries>();

        /// <summary>
        /// X labels, one per bucket. Never null.
        /// </summary>
        public List<string> Labels { get; } = new List<string>();

        /// <summary>
        /// Formats axis and legend values (default: whole numbers in the locale).
        /// </summary>
        public Func<double, string>? ValueFormatter { get; set; } = null;

        /// <summary>
        /// Text shown when there is no data (English).
        /// </summary>
        public string EmptyText { get; set; } = "No data.";

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = false;

        #endregion

        #region Private-Members

        private static readonly string[] _Glyphs = new string[] { "█", "▓", "▒", "░", "#", "+", "=", "%" };
        private static readonly string[] _AsciiGlyphs = new string[] { "#", "=", "+", "*", "o", "%", "x", "@" };
        private static readonly string[] _LineGlyphs = new string[] { "*", "o", "+", "x", "#", "@", "%", "=" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the data.
        /// </summary>
        /// <param name="labels">X labels.</param>
        /// <param name="series">Series.</param>
        public void SetData(IEnumerable<string> labels, IEnumerable<ChartSeries> series)
        {
            Labels.Clear();
            Series.Clear();
            if (labels != null) Labels.AddRange(labels);
            if (series != null) Series.AddRange(series);
        }

        /// <summary>
        /// The data as a tab-separated table (label, then one column per series), for copying.
        /// </summary>
        /// <returns>Text.</returns>
        public string ToTextTable()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(T("Time"));
            foreach (ChartSeries s in Series) sb.Append('\t').Append(T(s.Name));
            sb.Append('\n');
            int rows = BucketCount();
            for (int i = 0; i < rows; i++)
            {
                sb.Append(i < Labels.Count ? Labels[i] : (i + 1).ToString(CultureInfo.InvariantCulture));
                foreach (ChartSeries s in Series)
                {
                    sb.Append('\t').Append(i < s.Values.Count ? s.Values[i].ToString(CultureInfo.InvariantCulture) : "0");
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// The glyph a series is drawn with.
        /// </summary>
        /// <param name="index">Series index.</param>
        /// <returns>Glyph.</returns>
        public string GlyphFor(int index)
        {
            if (Kind == ChartKindEnum.Line) return _LineGlyphs[index % _LineGlyphs.Length];
            string[] set = Theme.AsciiBorders ? _AsciiGlyphs : _Glyphs;
            return set[index % set.Length];
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 3) return;
            int y = 0;
            if (!String.IsNullOrEmpty(Title)) SurfaceText.Draw(surface, 0, y++, T(Title), Theme.Accent, width);
            int legendX = 0;
            for (int i = 0; i < Series.Count && legendX < width; i++)
            {
                string item = GlyphFor(i) + " " + T(Series[i].Name) + " " + Format(Series[i].Values.Sum());
                legendX += SurfaceText.Draw(surface, legendX, y, item, StyleFor(i), width - legendX) + 3;
            }

            if (Series.Count > 0) y++;
            int buckets = BucketCount();
            if (buckets == 0 || Series.Count == 0 || Series.All(s => s.Values.All(v => v <= 0)))
            {
                if (y < height) SurfaceText.Draw(surface, 0, y, T(EmptyText), Theme.Muted, width);
                return;
            }

            int plotHeight = height - y - 1;
            if (plotHeight < 1) return;
            List<List<double>> columns = Downsample(buckets, width - 10, out List<string> labels);
            double max = Kind == ChartKindEnum.Bar ? columns.Max(c => c.Sum()) : columns.Max(c => c.Count == 0 ? 0 : c.Max());
            if (max <= 0) max = 1;
            string maxLabel = Format(max);
            int axisWidth = Math.Max(TextCells.Width(maxLabel), 1) + 1;
            int plotWidth = width - axisWidth;
            int colWidth = Math.Max(1, plotWidth / Math.Max(1, columns.Count));
            int barWidth = colWidth > 2 ? colWidth - 1 : colWidth;
            SurfaceText.Draw(surface, 0, y, TextCells.PadLeft(maxLabel, axisWidth - 1), Theme.Muted, axisWidth);
            SurfaceText.Draw(surface, 0, y + plotHeight - 1, TextCells.PadLeft("0", axisWidth - 1), Theme.Muted, axisWidth);
            for (int row = 0; row < plotHeight; row++) surface.DrawText(axisWidth - 1, y + row, "|", Theme.Border);

            for (int c = 0; c < columns.Count; c++)
            {
                int x = axisWidth + c * colWidth;
                if (x >= width) break;
                List<double> values = columns[c];
                if (Kind == ChartKindEnum.Bar)
                {
                    double cumulative = 0;
                    int filledRows = 0;
                    for (int s = 0; s < values.Count; s++)
                    {
                        cumulative += values[s];
                        int top = (int)Math.Round(cumulative / max * plotHeight);
                        if (values[s] > 0 && top <= filledRows) top = filledRows + 1;
                        top = Math.Min(top, plotHeight);
                        for (int r = filledRows; r < top; r++)
                        {
                            int py = y + plotHeight - 1 - r;
                            for (int bx = 0; bx < barWidth && x + bx < width; bx++) surface.DrawText(x + bx, py, GlyphFor(s), StyleFor(s));
                        }

                        filledRows = Math.Max(filledRows, top);
                    }
                }
                else
                {
                    for (int s = 0; s < values.Count; s++)
                    {
                        if (values[s] <= 0) continue;
                        int level = Math.Clamp((int)Math.Round(values[s] / max * (plotHeight - 1)), 0, plotHeight - 1);
                        surface.DrawText(x, y + plotHeight - 1 - level, GlyphFor(s), StyleFor(s));
                    }
                }
            }

            int labelY = y + plotHeight;
            if (labelY < height && labels.Count > 0)
            {
                int lastEnd = -1;
                for (int c = 0; c < labels.Count; c++)
                {
                    int x = axisWidth + c * colWidth;
                    string label = labels[c];
                    int lw = TextCells.Width(label);
                    if (x <= lastEnd + 1 || x + lw > width) continue;
                    SurfaceText.Draw(surface, x, labelY, label, Theme.Muted, width - x);
                    lastEnd = x + lw;
                }
            }
        }

        #endregion

        #region Private-Methods

        private int BucketCount()
        {
            int fromSeries = Series.Count == 0 ? 0 : Series.Max(s => s.Values.Count);
            return Math.Max(fromSeries, Labels.Count);
        }

        private List<List<double>> Downsample(int buckets, int maxColumns, out List<string> labels)
        {
            int groups = Math.Max(1, (int)Math.Ceiling(buckets / (double)Math.Max(1, maxColumns)));
            List<List<double>> columns = new List<List<double>>();
            labels = new List<string>();
            for (int start = 0; start < buckets; start += groups)
            {
                List<double> values = new List<double>();
                foreach (ChartSeries s in Series)
                {
                    double sum = 0;
                    for (int i = start; i < Math.Min(buckets, start + groups); i++)
                    {
                        if (i < s.Values.Count) sum += s.Values[i];
                    }

                    values.Add(sum);
                }

                columns.Add(values);
                labels.Add(start < Labels.Count ? Labels[start] : "");
            }

            return columns;
        }

        private string Format(double value)
        {
            if (ValueFormatter != null) return ValueFormatter(value);
            return Localizer.FormatNumber((long)Math.Round(value));
        }

        private CellStyle StyleFor(int index)
        {
            ArmadaTheme t = Theme;
            CellStyle[] styles = new CellStyle[] { t.Accent, t.Success, t.Error, t.Warning, t.Info, t.Link, t.Code, t.Muted };
            return styles[index % styles.Length];
        }

        #endregion
    }
}
