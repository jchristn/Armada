namespace Armada.Harbor
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;
    using Avalonia.Controls;

    /// <summary>
    /// The jobs running on this computer, one <see cref="JobListRow"/> each: what each is (a mission's title, an Ask turn's
    /// question), its runtime and elapsed time, its vessel and voyage, captain and stage, branch and IDs, its latest
    /// activity, and Open in Dashboard and View output. A click on a row shows its last output lines. Shows a one-line
    /// note when nothing runs.
    /// </summary>
    public class JobListView : UserControl
    {
        #region Public-Members

        /// <summary>
        /// Raised with a dashboard link to open (Open in Dashboard).
        /// </summary>
        public event Action<string>? OpenLink;

        /// <summary>
        /// Raised with a job whose log to show (View output).
        /// </summary>
        public event Action<HarborRunningJobView>? ViewOutput;

        #endregion

        #region Private-Members

        private readonly StackPanel _Rows = new StackPanel { Spacing = 2 };
        private readonly TextBlock _Empty;
        private readonly HashSet<string> _Expanded = new HashSet<string>(StringComparer.Ordinal);

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
        /// <param name="dashboardUrl">The dashboard address from Harbor's settings, for Open in Dashboard, or null.</param>
        public void Update(List<HarborJobInfo> jobs, DateTime nowUtc, string? dashboardUrl)
        {
            if (jobs == null) throw new ArgumentNullException(nameof(jobs));
            _Empty.IsVisible = jobs.Count == 0;

            // Rebuild only when the set of jobs changed; otherwise refresh each row in place (elapsed time, activity,
            // output), so an expanded row stays expanded.
            bool same = _Rows.Children.Count == jobs.Count;
            for (int i = 0; same && i < jobs.Count; i++)
            {
                if (_Rows.Children[i] is not JobListRow row || !String.Equals(row.JobId, jobs[i].JobId, StringComparison.Ordinal)) same = false;
            }

            if (!same)
            {
                HashSet<string> live = new HashSet<string>(StringComparer.Ordinal);
                foreach (HarborJobInfo job in jobs) live.Add(job.JobId);
                _Expanded.RemoveWhere(id => !live.Contains(id));

                _Rows.Children.Clear();
                foreach (HarborJobInfo job in jobs) _Rows.Children.Add(BuildRow(job.JobId));
            }

            for (int i = 0; i < jobs.Count; i++)
            {
                if (_Rows.Children[i] is JobListRow row) row.Apply(HarborRunningJobView.From(jobs[i], nowUtc, dashboardUrl));
            }
        }

        /// <summary>
        /// Expand or collapse a job's row (as a click on it does).
        /// </summary>
        /// <param name="jobId">Job.</param>
        /// <param name="expanded">Whether to show its last output lines.</param>
        public void SetExpanded(string jobId, bool expanded)
        {
            if (expanded) _Expanded.Add(jobId);
            else _Expanded.Remove(jobId);
            foreach (Control child in _Rows.Children)
            {
                if (child is JobListRow row && String.Equals(row.JobId, jobId, StringComparison.Ordinal)) row.IsExpanded = expanded;
            }
        }

        #endregion

        #region Private-Methods

        private JobListRow BuildRow(string jobId)
        {
            JobListRow row = new JobListRow(jobId);
            row.IsExpanded = _Expanded.Contains(jobId);
            row.ExpandedChanged += (sender, args) =>
            {
                if (row.IsExpanded) _Expanded.Add(row.JobId);
                else _Expanded.Remove(row.JobId);
            };
            row.OpenLink += link => OpenLink?.Invoke(link);
            row.ViewOutput += view => ViewOutput?.Invoke(view);
            return row;
        }

        #endregion
    }
}
