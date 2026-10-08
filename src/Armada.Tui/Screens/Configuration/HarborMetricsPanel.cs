namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Metrics;
    using Armada.Core.Metrics.Charts;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Text;
    using Armada.Tui.Theming;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// The Harbors screen's activity panel for the selected Harbor (dashboard <c>HarborMetricsPanel.tsx</c>): totals, jobs
    /// over time as stacked bars (<see cref="MultiSeriesChart"/>), slot usage as a sparkline against the capacity, link
    /// health as a one-line strip, launch speed per runtime (median and p95), and token totals. Every state has its own
    /// glyph as well as its own color, with an ASCII set in ASCII icon mode. A focus region: when it is taller than its
    /// box, Up/Down/PgUp/PgDn/Home/End scroll it. Not thread-safe.
    /// </summary>
    public class HarborMetricsPanel : ArmadaWidget
    {
        #region Public-Members

        /// <summary>
        /// Metrics shown, or null.
        /// </summary>
        public HarborMetrics? Metrics { get; private set; } = null;

        /// <summary>
        /// Name of the Harbor the panel is about (English or data), or empty.
        /// </summary>
        public string HarborName { get; set; } = "";

        /// <summary>
        /// Range shown (1h, 24h, or 7d).
        /// </summary>
        public string Range { get; set; } = HarborMetricsRanges.DefaultWireName;

        /// <summary>
        /// Message instead of (or over) the charts: loading, an error, or no Harbor selected. Empty for none.
        /// </summary>
        public string Message { get; set; } = "";

        /// <summary>
        /// Jobs over time.
        /// </summary>
        public MultiSeriesChart JobsChart { get; } = new MultiSeriesChart();

        /// <summary>
        /// First visible row.
        /// </summary>
        public int ScrollOffset
        {
            get { return _Scroll; }
        }

        /// <summary>
        /// True when the content is taller than the panel (so the scroll keys apply).
        /// </summary>
        public bool CanScroll
        {
            get { return _LastContentHeight > _LastHeight; }
        }

        /// <summary>
        /// Unicode glyphs of the link states, in <see cref="StatusStripModel.LegendStates"/> order.
        /// </summary>
        public static readonly IReadOnlyDictionary<HarborLinkSegmentStateEnum, string> UnicodeStateGlyphs = new Dictionary<HarborLinkSegmentStateEnum, string>
        {
            { HarborLinkSegmentStateEnum.Connected, "\u2588" },
            { HarborLinkSegmentStateEnum.Reconnecting, "\u2592" },
            { HarborLinkSegmentStateEnum.Down, "X" },
            { HarborLinkSegmentStateEnum.Unknown, "\u00b7" }
        };

        /// <summary>
        /// ASCII glyphs of the link states.
        /// </summary>
        public static readonly IReadOnlyDictionary<HarborLinkSegmentStateEnum, string> AsciiStateGlyphs = new Dictionary<HarborLinkSegmentStateEnum, string>
        {
            { HarborLinkSegmentStateEnum.Connected, "#" },
            { HarborLinkSegmentStateEnum.Reconnecting, "~" },
            { HarborLinkSegmentStateEnum.Down, "X" },
            { HarborLinkSegmentStateEnum.Unknown, "." }
        };

        #endregion

        #region Private-Members

        private const int _ChartRows = 9;
        private const string _UnicodeLevels = "\u00b7\u2581\u2582\u2583\u2584\u2585\u2586\u2587\u2588";
        private const string _AsciiLevels = "._-=+*#%@";

        private int _Scroll = 0;
        private int _LastHeight = 1;
        private int _LastContentHeight = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborMetricsPanel()
        {
            BoxTitle = "Activity";
            JobsChart.Kind = ChartKindEnum.Bar;
            JobsChart.EmptyText = "No jobs ended on this Harbor in this time range.";
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show metrics (null clears them); keeps the scroll position when the same Harbor refreshes.
        /// </summary>
        /// <param name="metrics">Metrics or null.</param>
        public void SetMetrics(HarborMetrics? metrics)
        {
            if (metrics == null || Metrics == null || !String.Equals(metrics.HarborId, Metrics.HarborId, StringComparison.Ordinal)) _Scroll = 0;
            Metrics = metrics;
            if (metrics == null)
            {
                JobsChart.SetData(new List<string>(), new List<ChartSeries>());
                return;
            }

            BucketChartModel jobs = HarborChartMapper.Jobs(metrics);
            List<string> labels = new List<string>();
            for (int i = 0; i < jobs.BucketCount; i++) labels.Add(jobs.BucketLabel(i, TimeZoneInfo.Local));
            JobsChart.SetData(labels, jobs.Series.Select(s => new ChartSeries(s.Name, s.Values.Select(v => v ?? 0))));
        }

        /// <summary>
        /// The link strip as glyphs: one per column, the worst state in that column's time.
        /// </summary>
        /// <param name="model">Strip.</param>
        /// <param name="columns">Columns.</param>
        /// <param name="ascii">True for the ASCII glyph set.</param>
        /// <returns>Text, one glyph per column.</returns>
        public static string StripText(StatusStripModel model, int columns, bool ascii)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            IReadOnlyDictionary<HarborLinkSegmentStateEnum, string> glyphs = ascii ? AsciiStateGlyphs : UnicodeStateGlyphs;
            StringBuilder sb = new StringBuilder();
            foreach (HarborLinkSegmentStateEnum state in model.StatesForColumns(columns)) sb.Append(glyphs[state]);
            return sb.ToString();
        }

        /// <summary>
        /// A text sparkline of values against zero and a maximum: one glyph per column (adjacent buckets keep their
        /// largest value), a dot for zero, and a space where there is no value.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <param name="max">Top of the scale.</param>
        /// <param name="columns">Columns available.</param>
        /// <param name="ascii">True for the ASCII glyph set.</param>
        /// <returns>Text.</returns>
        public static string SparkText(IList<double?> values, double max, int columns, bool ascii)
        {
            string levels = ascii ? _AsciiLevels : _UnicodeLevels;
            List<double?> grouped = ChartGeometry.Downsample(values, columns, true);
            StringBuilder sb = new StringBuilder();
            foreach (int level in ChartGeometry.Levels(grouped, max, levels.Length)) sb.Append(level < 0 ? ' ' : levels[level]);
            return sb.ToString();
        }

        /// <summary>
        /// Rows the content takes at a width.
        /// </summary>
        /// <param name="width">Width.</param>
        /// <returns>Rows.</returns>
        public int ContentHeight(int width)
        {
            return BuildLines(Math.Max(10, width)).Count;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Modifiers != KeyModifiers.None) return false;
            int page = Math.Max(1, _LastHeight - 1);
            switch (key.Code)
            {
                case KeyCode.Up: return ScrollTo(_Scroll - 1);
                case KeyCode.Down: return ScrollTo(_Scroll + 1);
                case KeyCode.PageUp: return ScrollTo(_Scroll - page);
                case KeyCode.PageDown: return ScrollTo(_Scroll + page);
                case KeyCode.Home: return ScrollTo(0);
                case KeyCode.End: return ScrollTo(Int32.MaxValue);
                default: return false;
            }
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind != MouseEventKind.Wheel) return false;
            if (mouse.Button == MouseButton.WheelUp) return ScrollTo(_Scroll - 3);
            if (mouse.Button == MouseButton.WheelDown) return ScrollTo(_Scroll + 3);
            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 10 || height < 1) return;
            List<HarborMetricsPanelLine> lines = BuildLines(width);
            _LastHeight = height;
            _LastContentHeight = lines.Count;
            _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, lines.Count - height));

            CellBuffer buffer = new CellBuffer(width, Math.Max(1, lines.Count));
            BufferSurface canvas = new BufferSurface(buffer);
            SurfaceText.FillRect(canvas, new Rect(0, 0, width, buffer.Height), Theme.Text);
            int y = 0;
            foreach (HarborMetricsPanelLine line in lines)
            {
                if (line.Chart != null)
                {
                    // The chart takes this row and the rows after it (placeholders keep the count right).
                    line.Chart.Render(new SurfaceView(canvas, new Rect(0, y, width, line.ChartRows)));
                }
                else if (line.Spans != null)
                {
                    int x = 0;
                    foreach (HarborMetricsPanelSpan span in line.Spans)
                    {
                        if (x >= width) break;
                        x += SurfaceText.Draw(canvas, x, y, span.Text, span.Style, width - x);
                    }
                }

                y++;
            }

            for (int row = 0; row < height && row + _Scroll < buffer.Height; row++)
            {
                for (int col = 0; col < width; col++) surface.Set(col, row, buffer.Get(col, row + _Scroll));
            }

            if (CanScroll && width > 12)
            {
                string more = (_Scroll > 0 ? "^" : " ") + (_Scroll + height < lines.Count ? "v" : " ");
                SurfaceText.Draw(surface, width - 2, 0, more, Theme.Muted, 2);
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnThemeChanged(ArmadaTheme theme)
        {
            JobsChart.ApplyTheme(theme);
        }

        /// <inheritdoc />
        protected override void OnLocalizerChanged(Services.ITextLocalizer localizer)
        {
            JobsChart.Localizer = localizer;
        }

        #endregion

        #region Private-Methods

        private bool ScrollTo(int row)
        {
            int max = Math.Max(0, _LastContentHeight - _LastHeight);
            _Scroll = Math.Clamp(row, 0, max);
            return true;
        }

        private List<HarborMetricsPanelLine> BuildLines(int width)
        {
            List<HarborMetricsPanelLine> lines = new List<HarborMetricsPanelLine>();
            bool ascii = Theme.AsciiGlyphs || Theme.AsciiBorders;
            string range = T(Capitalize(ChartFormat.RangeName(Range)));
            string title = String.IsNullOrEmpty(HarborName) ? range : HarborName + " - " + range;
            lines.Add(Line(new HarborMetricsPanelSpan(title, Theme.Accent), new HarborMetricsPanelSpan("   r " + T("Range") + "  u " + T("Token usage"), Theme.Muted)));
            HarborMetrics? metrics = Metrics;
            if (!String.IsNullOrEmpty(Message)) lines.Add(Line(new HarborMetricsPanelSpan(T(Message), metrics == null ? Theme.Muted : Theme.Warning)));
            if (metrics == null) return lines;

            HarborJobMetrics jobs = metrics.Jobs;
            lines.Add(Line(
                new HarborMetricsPanelSpan(Num(jobs.MissionsFinished + jobs.InteractiveFinished), Theme.Success), new HarborMetricsPanelSpan(" " + T("finished") + "  ", Theme.Muted),
                new HarborMetricsPanelSpan(Num(jobs.MissionsFailed + jobs.InteractiveFailed), Theme.Error), new HarborMetricsPanelSpan(" " + T("failed") + "  ", Theme.Muted),
                new HarborMetricsPanelSpan(Num(jobs.Running), Theme.Text), new HarborMetricsPanelSpan(" " + T("running") + "  ", Theme.Muted),
                new HarborMetricsPanelSpan(metrics.Link.ConnectedPercent.HasValue ? Math.Round(metrics.Link.ConnectedPercent.Value, 1).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "-", Theme.Text),
                new HarborMetricsPanelSpan(" " + T("connected") + "  ", Theme.Muted),
                new HarborMetricsPanelSpan(ChartFormat.Duration(metrics.Link.RoundTripMedianMs), Theme.Text), new HarborMetricsPanelSpan(" " + T("round trip"), Theme.Muted)));

            lines.Add(Line(new HarborMetricsPanelSpan(T("Jobs over time"), Theme.Header)));
            AddChart(lines, JobsChart, _ChartRows);

            // Slot usage: peak concurrent jobs per bucket against the capacity.
            int max = Math.Max(1, Math.Max(metrics.Slots.MaxConcurrentJobs, metrics.Slots.Peak));
            string slotLabel = T("Slots") + " ";
            string slotSuffix = " " + T("peak") + " " + Num(metrics.Slots.Peak) + "/" + Num(metrics.Slots.MaxConcurrentJobs) + ", " + T("avg") + " " + metrics.Slots.Average.ToString("0.##", CultureInfo.InvariantCulture);
            int sparkColumns = Math.Max(4, width - TextCells.Width(slotLabel) - TextCells.Width(slotSuffix) - 1);
            SparklineModel slots = HarborChartMapper.SlotTrend(metrics);
            lines.Add(Line(new HarborMetricsPanelSpan(slotLabel, Theme.Header), new HarborMetricsPanelSpan(SparkText(slots.Values, max, sparkColumns, ascii), Theme.Accent), new HarborMetricsPanelSpan(slotSuffix, Theme.Muted)));

            // Link health: one glyph per column, worst state wins, and a legend so no state depends on color.
            StatusStripModel strip = HarborChartMapper.Link(metrics);
            string linkLabel = T("Link") + "  ";
            int stripColumns = Math.Max(4, width - TextCells.Width(linkLabel));
            List<HarborMetricsPanelSpan> stripSpans = new List<HarborMetricsPanelSpan> { new HarborMetricsPanelSpan(linkLabel, Theme.Header) };
            IReadOnlyDictionary<HarborLinkSegmentStateEnum, string> glyphs = ascii ? AsciiStateGlyphs : UnicodeStateGlyphs;
            foreach (HarborLinkSegmentStateEnum state in strip.StatesForColumns(stripColumns)) stripSpans.Add(new HarborMetricsPanelSpan(glyphs[state], StateStyle(state)));
            lines.Add(new HarborMetricsPanelLine { Spans = Merge(stripSpans) });
            List<HarborMetricsPanelSpan> legend = new List<HarborMetricsPanelSpan> { new HarborMetricsPanelSpan(new string(' ', TextCells.Width(linkLabel)), Theme.Muted) };
            foreach (HarborLinkSegmentStateEnum state in StatusStripModel.LegendStates)
            {
                legend.Add(new HarborMetricsPanelSpan(glyphs[state], StateStyle(state)));
                legend.Add(new HarborMetricsPanelSpan(" " + T(StatusStripModel.StateLabel(state)) + "  ", Theme.Muted));
            }

            legend.Add(new HarborMetricsPanelSpan(Num(metrics.Link.Disconnects) + " " + T(metrics.Link.Disconnects == 1 ? "disconnect" : "disconnects"), Theme.Muted));
            lines.Add(new HarborMetricsPanelLine { Spans = legend });

            // Launch speed per runtime.
            lines.Add(Line(new HarborMetricsPanelSpan(T("Launch speed"), Theme.Header)));
            if (metrics.LaunchSpeed.Count == 0)
            {
                lines.Add(Line(new HarborMetricsPanelSpan(T("No jobs ended on this Harbor in this time range."), Theme.Muted)));
            }
            else
            {
                int runtimeWidth = Math.Clamp(metrics.LaunchSpeed.Max(s => TextCells.Width(s.Runtime)), 7, 14);
                lines.Add(Line(new HarborMetricsPanelSpan(SpeedRow(runtimeWidth, T("Runtime"), T("Jobs"), T("First out"), "p95", T("Run time"), "p95"), Theme.Muted)));
                foreach (HarborLaunchSpeed speed in metrics.LaunchSpeed)
                {
                    lines.Add(Line(new HarborMetricsPanelSpan(SpeedRow(runtimeWidth, speed.Runtime, Num(speed.JobCount), ChartFormat.Duration(speed.FirstOutputMedianMs),
                        ChartFormat.Duration(speed.FirstOutputP95Ms), ChartFormat.Duration(speed.DurationMedianMs), ChartFormat.Duration(speed.DurationP95Ms)), Theme.Text)));
                }
            }

            // Tokens: totals here; u opens Token Usage filtered to this Harbor for the charts.
            HarborTokenMetrics tokens = metrics.Tokens;
            lines.Add(Line(new HarborMetricsPanelSpan(T("Tokens") + "  ", Theme.Header), new HarborMetricsPanelSpan(ChartFormat.Tokens(tokens.TotalTokens), Theme.Text),
                new HarborMetricsPanelSpan(" " + T("total") + ", " + ChartFormat.Tokens(tokens.InputTokens) + " " + T("in") + ", " + ChartFormat.Tokens(tokens.OutputTokens) + " " + T("out") + "  ", Theme.Muted),
                new HarborMetricsPanelSpan("u " + T("Open in Token Usage"), Theme.Link)));
            return lines;
        }

        private static string SpeedRow(int runtimeWidth, string runtime, string jobs, string firstMedian, string firstP95, string runMedian, string runP95)
        {
            return TextCells.PadRight(TextCells.Truncate(runtime, runtimeWidth), runtimeWidth) + " "
                + TextCells.PadLeft(jobs, 5) + " "
                + TextCells.PadLeft(firstMedian, 9) + " "
                + TextCells.PadLeft(firstP95, 7) + " "
                + TextCells.PadLeft(runMedian, 9) + " "
                + TextCells.PadLeft(runP95, 8);
        }

        private static void AddChart(List<HarborMetricsPanelLine> lines, MultiSeriesChart chart, int rows)
        {
            lines.Add(new HarborMetricsPanelLine { Chart = chart, ChartRows = rows });
            for (int i = 1; i < rows; i++) lines.Add(new HarborMetricsPanelLine());
        }

        private CellStyle StateStyle(HarborLinkSegmentStateEnum state)
        {
            switch (state)
            {
                case HarborLinkSegmentStateEnum.Connected: return Theme.Success;
                case HarborLinkSegmentStateEnum.Reconnecting: return Theme.Warning;
                case HarborLinkSegmentStateEnum.Down: return Theme.Error;
                default: return Theme.Muted;
            }
        }

        private static List<HarborMetricsPanelSpan> Merge(List<HarborMetricsPanelSpan> spans)
        {
            // Join runs of one style so the strip is drawn in a few calls.
            List<HarborMetricsPanelSpan> merged = new List<HarborMetricsPanelSpan>();
            foreach (HarborMetricsPanelSpan span in spans)
            {
                if (merged.Count > 0 && merged[merged.Count - 1].Style.Equals(span.Style)) merged[merged.Count - 1] = new HarborMetricsPanelSpan(merged[merged.Count - 1].Text + span.Text, span.Style);
                else merged.Add(span);
            }

            return merged;
        }

        private static HarborMetricsPanelLine Line(params HarborMetricsPanelSpan[] spans)
        {
            return new HarborMetricsPanelLine { Spans = new List<HarborMetricsPanelSpan>(spans) };
        }

        private string Num(int value)
        {
            return Localizer.FormatNumber(value);
        }

        private static string Capitalize(string text)
        {
            return String.IsNullOrEmpty(text) ? text : Char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        #endregion
    }
}
