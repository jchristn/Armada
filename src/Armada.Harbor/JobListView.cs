namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;

    /// <summary>
    /// A compact list of the jobs running on this computer: what each is (a mission, an Ask turn), the runtime running
    /// it, and how long it has been running. Shows a one-line note when nothing runs.
    /// </summary>
    public class JobListView : UserControl
    {
        #region Private-Members

        private readonly StackPanel _Rows = new StackPanel { Spacing = 2 };
        private readonly TextBlock _Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public JobListView()
        {
            _Empty = HarborUi.Note("Nothing is running. Work the Admiral sends to this computer appears here.");
            StackPanel root = new StackPanel();
            root.Children.Add(_Empty);
            root.Children.Add(_Rows);
            Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show these jobs.
        /// </summary>
        /// <param name="jobs">Running jobs, oldest first.</param>
        /// <param name="nowUtc">Current time, UTC, for the elapsed times.</param>
        public void Update(List<HarborJobInfo> jobs, DateTime nowUtc)
        {
            if (jobs == null) throw new ArgumentNullException(nameof(jobs));
            _Empty.IsVisible = jobs.Count == 0;

            // Rebuild only when the set of jobs changed; otherwise just tick the elapsed times.
            bool same = _Rows.Children.Count == jobs.Count;
            for (int i = 0; same && i < jobs.Count; i++)
            {
                if (_Rows.Children[i].Tag is not string id || !String.Equals(id, jobs[i].JobId, StringComparison.Ordinal)) same = false;
            }

            if (!same)
            {
                _Rows.Children.Clear();
                foreach (HarborJobInfo job in jobs) _Rows.Children.Add(BuildRow(job));
            }

            for (int i = 0; i < jobs.Count; i++)
            {
                if (_Rows.Children[i] is Grid row && row.Children[2] is TextBlock elapsed) elapsed.Text = jobs[i].Elapsed(nowUtc);
            }
        }

        #endregion

        #region Private-Methods

        private static Grid BuildRow(HarborJobInfo job)
        {
            Grid row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto,64"),
                ColumnSpacing = 10,
                MinHeight = 30,
                Tag = job.JobId
            };

            StackPanel what = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            what.Children.Add(new TextBlock { Text = job.Title(), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (job.CaptainId != null)
            {
                TextBlock captain = HarborUi.Secondary(new TextBlock { Text = "Captain " + job.CaptainId, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                what.Children.Add(captain);
            }

            row.Children.Add(what);

            TextBlock runtimeText = new TextBlock { Text = String.IsNullOrEmpty(job.Runtime) ? "Unknown runtime" : job.Runtime, FontSize = 12 };
            runtimeText.Bind(TextBlock.ForegroundProperty, runtimeText.GetResourceObservable("HarborAccentSoftTextBrush"));
            Border runtime = new Border
            {
                Child = runtimeText,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            runtime.Bind(Border.BackgroundProperty, runtime.GetResourceObservable("HarborAccentSoftBrush"));
            Grid.SetColumn(runtime, 1);
            row.Children.Add(runtime);

            TextBlock elapsed = HarborUi.Secondary(new TextBlock
            {
                FontFamily = new FontFamily(HarborUi.MonospaceFonts),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            });
            ToolTip.SetTip(elapsed, "Running for this long");
            Grid.SetColumn(elapsed, 2);
            row.Children.Add(elapsed);
            return row;
        }

        #endregion
    }
}
