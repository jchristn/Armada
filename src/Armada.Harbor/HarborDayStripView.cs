namespace Armada.Harbor
{
    using System;
    using System.Globalization;
    using Armada.Client.Metrics;
    using Armada.Core.Metrics.Charts;
    using Armada.Core.Models;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;

    /// <summary>
    /// The "Last 24 hours" card on the Status window's Overview tab: three small charts of this Harbor from the Admiral
    /// (jobs, slot usage, and link health), each with a one-line caption, and a button that opens the Activity tab for the
    /// full charts. Refreshes while shown; says why when the Admiral cannot answer.
    /// </summary>
    public class HarborDayStripView : UserControl
    {
        #region Public-Members

        /// <summary>
        /// Time between refreshes while shown.
        /// </summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Jobs over the last 24 hours.
        /// </summary>
        public StackedBarChart JobsChart { get; } = new StackedBarChart(112);

        /// <summary>
        /// Slot usage over the last 24 hours.
        /// </summary>
        public LineChart SlotsChart { get; } = new LineChart(112);

        /// <summary>
        /// Link health over the last 24 hours.
        /// </summary>
        public StatusStrip LinkStrip { get; } = new StatusStrip(20);

        /// <summary>
        /// The latest answer, or null before the first.
        /// </summary>
        public HarborMetricsLoadResult? LastResult
        {
            get { return _LastResult; }
        }

        #endregion

        #region Private-Members

        private readonly HarborSession _Session;
        private readonly HarborMetricsRefresher _Refresher;
        private readonly TextBlock _JobsCaption;
        private readonly TextBlock _SlotsCaption;
        private readonly TextBlock _LinkCaption;
        private readonly TextBlock _MessageText = new TextBlock();
        private readonly Border _Message;
        private readonly Grid _Charts;
        private HarborMetricsLoadResult? _LastResult = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="session">Harbor session.</param>
        public HarborDayStripView(HarborSession session)
        {
            _Session = session ?? throw new ArgumentNullException(nameof(session));
            _Refresher = new HarborMetricsRefresher(_Session.CreateMetricsFeed(), RefreshInterval);
            _Refresher.Loaded += (sender, result) => Dispatcher.UIThread.Post(() => Apply(result));

            _JobsCaption = HarborUi.Note("-");
            _SlotsCaption = HarborUi.Note("-");
            _LinkCaption = HarborUi.Note("-");
            _Charts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 18 };
            AddColumn(0, "Jobs", JobsChart, _JobsCaption);
            AddColumn(1, "Slot usage", SlotsChart, _SlotsCaption);
            StackPanel link = new StackPanel { Spacing = 6 };
            link.Children.Add(LinkStrip);
            ChartLegend legend = new ChartLegend { Margin = new Thickness(0, 2, 0, 0) };
            legend.ShowStates();
            link.Children.Add(legend);
            AddColumn(2, "Link health", link, _LinkCaption);
            JobsChart.EmptyText = "No jobs";
            SlotsChart.EmptyText = "No jobs";

            _Message = HarborUi.Notice(_MessageText);
            _Message.IsVisible = false;
            StackPanel body = new StackPanel { Spacing = 10 };
            body.Children.Add(_Message);
            body.Children.Add(_Charts);
            Content = body;

            AttachedToVisualTree += (sender, args) => _Refresher.Start("24h");
            DetachedFromVisualTree += (sender, args) => _Refresher.Stop();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show an answer (the refresher posts each one here; tests and screenshots may call it directly).
        /// </summary>
        /// <param name="result">Answer.</param>
        public void Apply(HarborMetricsLoadResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            _LastResult = result;
            if (!result.IsLoaded)
            {
                _MessageText.Text = result.Message;
                _Message.IsVisible = true;
                _Charts.IsVisible = JobsChart.Model != null;
                return;
            }

            HarborMetrics metrics = result.Metrics!;
            _Message.IsVisible = false;
            _Charts.IsVisible = true;
            JobsChart.Model = HarborChartMapper.Jobs(metrics);
            SlotsChart.Model = HarborChartMapper.Slots(metrics);
            LinkStrip.Model = HarborChartMapper.Link(metrics);

            HarborJobMetrics jobs = metrics.Jobs;
            _JobsCaption.Text = Number(jobs.MissionsFinished + jobs.InteractiveFinished) + " finished, " + Number(jobs.MissionsFailed + jobs.InteractiveFailed) + " failed, " + Number(jobs.Running) + " running";
            _SlotsCaption.Text = "Peak " + Number(metrics.Slots.Peak) + " of " + Number(metrics.Slots.MaxConcurrentJobs) + ", average " + metrics.Slots.Average.ToString("0.##", CultureInfo.InvariantCulture);
            _LinkCaption.Text = (metrics.Link.ConnectedPercent.HasValue ? Math.Round(metrics.Link.ConnectedPercent.Value, 1).ToString("0.#", CultureInfo.InvariantCulture) + "% connected" : "No link data")
                + ", " + (metrics.Link.Disconnects == 1 ? "1 disconnect" : Number(metrics.Link.Disconnects) + " disconnects")
                + ", round trip " + ChartFormat.Duration(metrics.Link.RoundTripMedianMs);
        }

        #endregion

        #region Private-Methods

        private void AddColumn(int column, string title, Control chart, TextBlock caption)
        {
            StackPanel panel = new StackPanel { Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13 });
            panel.Children.Add(chart);
            panel.Children.Add(caption);
            panel.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(panel, column);
            _Charts.Children.Add(panel);
        }

        private static string Number(int value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        #endregion
    }
}
