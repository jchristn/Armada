namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Client.Metrics;
    using Armada.Core.Metrics.Charts;
    using Armada.Core.Models;
    using Avalonia;
    using Avalonia.Automation;
    using Avalonia.Controls;
    using Avalonia.Controls.Primitives;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;

    /// <summary>
    /// The Activity tab of the Status window: this Harbor's charts from the Admiral (GET /api/v1/harbors/{id}/metrics,
    /// read with Harbor's own credential) over the last hour, 24 hours, or 7 days: jobs over time, slot usage against the
    /// capacity, link health and heartbeat round trip, launch speed per runtime (median and p95, with a trend), and tokens
    /// by runtime and model. Refreshes on a timer while the tab is shown and stops when it is hidden. When the Admiral
    /// cannot answer (offline, a refused credential, an Admiral too old to have the charts), says why in plain language
    /// and keeps the last charts it had.
    /// </summary>
    public class HarborActivityView : UserControl
    {
        #region Public-Members

        /// <summary>
        /// Time between refreshes while the tab is shown.
        /// </summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Range shown (1h, 24h, or 7d).
        /// </summary>
        public string Range
        {
            get { return _Range; }
        }

        /// <summary>
        /// The latest answer, or null before the first.
        /// </summary>
        public HarborMetricsLoadResult? LastResult
        {
            get { return _LastResult; }
        }

        /// <summary>
        /// Jobs over time.
        /// </summary>
        public StackedBarChart JobsChart { get; } = new StackedBarChart(200);

        /// <summary>
        /// Slot usage.
        /// </summary>
        public LineChart SlotsChart { get; } = new LineChart(180);

        /// <summary>
        /// Link health strip.
        /// </summary>
        public StatusStrip LinkStrip { get; } = new StatusStrip(20);

        /// <summary>
        /// Heartbeat round trip.
        /// </summary>
        public LineChart RoundTripChart { get; } = new LineChart(160);

        /// <summary>
        /// Tokens by runtime and model.
        /// </summary>
        public StackedBarChart TokensChart { get; } = new StackedBarChart(200);

        #endregion

        #region Private-Members

        private static readonly string[] _Ranges = new string[] { "1h", "24h", "7d" };
        private static readonly string[] _RangeLabels = new string[] { "Last hour", "Last 24 hours", "Last 7 days" };

        private readonly HarborSession _Session;
        private readonly HarborMetricsRefresher _Refresher;
        private readonly List<ToggleButton> _RangeButtons = new List<ToggleButton>();
        private readonly CopyableIdText _HarborId;
        private readonly TextBlock _Updated;
        private readonly TextBlock _MessageText = new TextBlock();
        private readonly Border _Message;
        private readonly WrapPanel _Stats = new WrapPanel { Orientation = Orientation.Horizontal, IsVisible = false };
        private readonly TextBlock _Empty;
        private readonly StackPanel _Charts = new StackPanel { Spacing = 14 };
        private readonly ChartLegend _JobsLegend = new ChartLegend();
        private readonly ChartLegend _SlotsLegend = new ChartLegend();
        private readonly ChartLegend _LinkLegend = new ChartLegend();
        private readonly WrapPanel _LinkStats = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
        private readonly ChartLegend _RoundTripLegend = new ChartLegend();
        private readonly StackPanel _SpeedPanel = new StackPanel();
        private readonly WrapPanel _TokenStats = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        private readonly ChartLegend _TokensLegend = new ChartLegend();
        private readonly TextBlock _TokensEmpty;
        private string _Range = "24h";
        private HarborMetricsLoadResult? _LastResult = null;
        private HarborMetrics? _Shown = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public HarborActivityView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));
            _Refresher = new HarborMetricsRefresher(_Session.CreateMetricsFeed(), RefreshInterval);
            _Refresher.Loaded += (sender, result) => Dispatcher.UIThread.Post(() => Apply(result));

            StackPanel rangeButtons = HarborUi.ButtonRow();
            for (int i = 0; i < _Ranges.Length; i++)
            {
                string range = _Ranges[i];
                ToggleButton button = new ToggleButton { Content = _RangeLabels[i], IsChecked = range == _Range };
                AutomationProperties.SetName(button, "Show the " + _RangeLabels[i].ToLowerInvariant());
                button.Click += (sender, args) => SetRange(range);
                _RangeButtons.Add(button);
                rangeButtons.Children.Add(button);
            }

            rangeButtons.Children.Add(HarborUi.Button("Refresh", () => _Refresher.RefreshNow(), "Ask the Admiral for the latest charts now"));

            Grid header = HarborUi.DetailGrid(110);
            _HarborId = HarborUi.AddIdRow(header, "Harbor ID", _Session.Settings.HarborId);
            _Updated = HarborUi.Note("Loading...");
            HarborUi.AddControlRow(header, "Updated", _Updated);
            _Message = HarborUi.Notice(_MessageText);
            _Message.IsVisible = false;
            _Message.Margin = new Thickness(0, 10, 0, 0);
            AutomationProperties.SetLiveSetting(_MessageText, AutomationLiveSetting.Polite);
            StackPanel summary = new StackPanel { Spacing = 10 };
            summary.Children.Add(header);
            summary.Children.Add(_Stats);
            summary.Children.Add(_Message);
            _Empty = HarborUi.Note("No activity on this Harbor in this time range.");
            _Empty.IsVisible = false;
            summary.Children.Add(_Empty);

            _Charts.Children.Add(HarborUi.Card("Jobs over time", Stack(JobsChart, _JobsLegend), null));
            _Charts.Children.Add(HarborUi.Card("Slot usage", Stack(SlotsChart, _SlotsLegend), null));
            _LinkLegend.ShowStates();
            TextBlock roundTripTitle = new TextBlock { Text = "Heartbeat round trip", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 6) };
            _Charts.Children.Add(HarborUi.Card("Link health", Stack(LinkStrip, _LinkLegend, _LinkStats, roundTripTitle, RoundTripChart, _RoundTripLegend), null));
            _Charts.Children.Add(HarborUi.Card("Launch speed", _SpeedPanel, null));
            _TokensEmpty = HarborUi.Note("No token usage in this time range.");
            _Charts.Children.Add(HarborUi.Card("Token usage", Stack(_TokenStats, _TokensEmpty, TokensChart, _TokensLegend), null));
            _Charts.IsVisible = false;

            Content = HarborUi.Page(HarborUi.Card("Harbor activity", summary, rangeButtons), _Charts);

            AttachedToVisualTree += (sender, args) =>
            {
                _Session.Changed += OnSessionChanged;
                _HarborId.Id = _Session.Settings.HarborId;
                _Refresher.Start(_Range);
            };
            DetachedFromVisualTree += (sender, args) =>
            {
                // Hidden (another tab, or the window closed): stop asking the Admiral.
                _Session.Changed -= OnSessionChanged;
                _Refresher.Stop();
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show another range and load it now.
        /// </summary>
        /// <param name="range">1h, 24h, or 7d.</param>
        public void SetRange(string range)
        {
            if (Array.IndexOf(_Ranges, range) < 0) return;
            _Range = range;
            for (int i = 0; i < _Ranges.Length; i++) _RangeButtons[i].IsChecked = _Ranges[i] == range;
            _Updated.Text = "Loading the " + ChartFormat.RangeName(range) + "...";
            if (VisualRoot != null) _Refresher.SetRange(range);
        }

        /// <summary>
        /// Show an answer (the refresher posts each one here; tests and screenshots may call it directly).
        /// </summary>
        /// <param name="result">Answer.</param>
        public void Apply(HarborMetricsLoadResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (!String.Equals(result.Range, _Range, StringComparison.Ordinal)) return;
            _LastResult = result;
            if (result.IsLoaded)
            {
                _Message.IsVisible = false;
                _Updated.Text = HarborUi.LocalTime(result.ReceivedUtc) + "  (" + ChartFormat.RangeName(result.Range) + ", refreshes every " + (int)RefreshInterval.TotalSeconds + "s)";
                Show(result.Metrics!);
                return;
            }

            _MessageText.Text = result.Message;
            _Message.IsVisible = true;
            if (_Shown != null && String.Equals(_Shown.Range, result.Range, StringComparison.Ordinal))
            {
                _Updated.Text = "Showing the charts from " + HarborUi.LocalTime(_Shown.GeneratedUtc);
            }
            else
            {
                _Updated.Text = "-";
                _Shown = null;
                _Stats.Children.Clear();
                _Stats.IsVisible = false;
                _Empty.IsVisible = false;
                _Charts.IsVisible = false;
            }
        }

        #endregion

        #region Private-Methods

        private void OnSessionChanged(object? sender, EventArgs e)
        {
            if (!String.Equals(_HarborId.Id, _Session.Settings.HarborId, StringComparison.Ordinal))
            {
                _HarborId.Id = _Session.Settings.HarborId;
                _Refresher.RefreshNow();
            }
        }

        private static StackPanel Stack(params Control[] children)
        {
            StackPanel panel = new StackPanel();
            foreach (Control child in children) panel.Children.Add(child);
            return panel;
        }

        private void Show(HarborMetrics metrics)
        {
            _Shown = metrics;
            _Charts.IsVisible = true;
            _Empty.IsVisible = !HarborChartMapper.HasAnyData(metrics);

            _Stats.Children.Clear();
            _Stats.IsVisible = true;
            HarborJobMetrics jobs = metrics.Jobs;
            AddStat(_Stats, Number(jobs.MissionsFinished + jobs.InteractiveFinished), "Finished", "HarborSuccessBrush");
            AddStat(_Stats, Number(jobs.MissionsFailed + jobs.InteractiveFailed), "Failed", "HarborDangerBrush");
            AddStat(_Stats, Number(jobs.Running), "Running", null);
            AddStat(_Stats, Number(metrics.Slots.Peak) + "/" + Number(metrics.Slots.MaxConcurrentJobs), "Peak slots", null);
            AddStat(_Stats, metrics.Link.ConnectedPercent.HasValue ? Math.Round(metrics.Link.ConnectedPercent.Value, 1).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "-", "Connected", null);
            AddStat(_Stats, ChartFormat.Duration(metrics.Link.RoundTripMedianMs), "Median round trip", null);

            BucketChartModel jobsModel = HarborChartMapper.Jobs(metrics);
            JobsChart.Model = jobsModel;
            _JobsLegend.Show(jobsModel);
            BucketChartModel slots = HarborChartMapper.Slots(metrics);
            SlotsChart.Model = slots;
            _SlotsLegend.Show(slots);

            LinkStrip.Model = HarborChartMapper.Link(metrics);
            _LinkStats.Children.Clear();
            AddStat(_LinkStats, Number(metrics.Link.Disconnects), "Disconnects", null);
            AddStat(_LinkStats, metrics.Link.ReconnectCount.HasValue ? Number(metrics.Link.ReconnectCount.Value) : "-", "Reconnects reported by Harbor", null);
            AddStat(_LinkStats, metrics.Link.LastReconnectUtc.HasValue ? HarborUi.LocalTime(metrics.Link.LastReconnectUtc.Value) : "-", "Last reconnect", null);
            BucketChartModel roundTrip = HarborChartMapper.RoundTrip(metrics);
            RoundTripChart.Model = roundTrip;
            _RoundTripLegend.Show(roundTrip);

            ShowLaunchSpeed(metrics);

            _TokenStats.Children.Clear();
            HarborTokenMetrics tokens = metrics.Tokens;
            AddStat(_TokenStats, ChartFormat.Tokens(tokens.TotalTokens), "Total", null);
            AddStat(_TokenStats, ChartFormat.Tokens(tokens.InputTokens), "Input", null);
            AddStat(_TokenStats, ChartFormat.Tokens(tokens.OutputTokens), "Output", null);
            AddStat(_TokenStats, ChartFormat.Tokens(tokens.CachedTokens), "Cached", null);
            if (tokens.EstimatedCount > 0) AddStat(_TokenStats, Number(tokens.EstimatedCount) + " of " + Number(tokens.RecordCount), "records estimated", "HarborWarningBrush");
            BucketChartModel tokenModel = HarborChartMapper.Tokens(metrics);
            bool hasTokens = tokenModel.Series.Count > 0 && tokenModel.HasData;
            _TokensEmpty.IsVisible = !hasTokens;
            TokensChart.IsVisible = hasTokens;
            _TokensLegend.IsVisible = hasTokens;
            TokensChart.Model = tokenModel;
            _TokensLegend.Show(tokenModel);
        }

        private void ShowLaunchSpeed(HarborMetrics metrics)
        {
            _SpeedPanel.Children.Clear();
            if (metrics.LaunchSpeed.Count == 0)
            {
                _SpeedPanel.Children.Add(HarborUi.Note("No jobs ended on this Harbor in this time range."));
                return;
            }

            Grid grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,Auto,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 6, ColumnSpacing = 18 };
            string[] headers = new string[] { "Runtime", "Jobs", "First output (median)", "First output (p95)", "Runtime (median)", "Runtime (p95)", "First output trend" };
            AddRow(grid, headers, true);
            List<DateTime> starts = HarborChartMapper.BucketStarts(metrics);
            foreach (HarborLaunchSpeed speed in metrics.LaunchSpeed)
            {
                int row = AddRow(grid, new string[]
                {
                    speed.Runtime,
                    Number(speed.JobCount),
                    ChartFormat.Duration(speed.FirstOutputMedianMs),
                    ChartFormat.Duration(speed.FirstOutputP95Ms),
                    ChartFormat.Duration(speed.DurationMedianMs),
                    ChartFormat.Duration(speed.DurationP95Ms)
                }, false);
                Sparkline trend = new Sparkline { Model = HarborChartMapper.LaunchTrend(speed), BucketStartsUtc = starts, BucketMinutes = metrics.BucketMinutes, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(trend, row);
                Grid.SetColumn(trend, 6);
                grid.Children.Add(trend);
            }

            AutomationProperties.SetName(grid, "Launch speed per runtime");
            _SpeedPanel.Children.Add(grid);
        }

        private static int AddRow(Grid grid, string[] cells, bool header)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int i = 0; i < cells.Length; i++)
            {
                TextBlock cell = new TextBlock { Text = cells[i], VerticalAlignment = VerticalAlignment.Center };
                if (header) cell.FontWeight = FontWeight.SemiBold;
                else if (i == 0) cell.FontWeight = FontWeight.SemiBold;
                else HarborUi.Secondary(cell);
                if (!header && i > 0) cell.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }

            return row;
        }

        private static void AddStat(WrapPanel panel, string value, string label, string? valueBrush)
        {
            StackPanel stat = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 22, 4) };
            TextBlock valueBlock = new TextBlock { Text = value, FontWeight = FontWeight.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Bottom };
            if (valueBrush != null) valueBlock.Bind(TextBlock.ForegroundProperty, valueBlock.GetResourceObservable(valueBrush));
            stat.Children.Add(valueBlock);
            stat.Children.Add(HarborUi.Secondary(new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1) }));
            panel.Children.Add(stat);
        }

        private static string Number(int value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        #endregion
    }
}
