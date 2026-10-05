namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Jobs (dashboard <c>Jobs.tsx</c>, route <c>/jobs</c>): background jobs with name (and error reason), kind,
    /// status, progress, created, and updated, Cancel for unfinished jobs, and auto-refresh at the screen's interval
    /// (plus a refresh whenever the header's job poll sees a change). Not thread-safe.
    /// </summary>
    public class JobsScreen : GridScreen<Job>
    {
        #region Private-Members

        private static readonly JobStatusEnum[] _Terminal = new JobStatusEnum[] { JobStatusEnum.Succeeded, JobStatusEnum.Failed, JobStatusEnum.Cancelled };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public JobsScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Jobs", "Background jobs and their status.", "jobs", j => j.Id)
        {
            Header.AddButton("Refresh", Refresh, "F5");
            Header.AddButton("Cancel Job", CancelCurrent, "x");
            AddFixed(Header, w => Header.HeightFor(w));
            Grid.MultiSelect = false;
            Grid.EmptyText = "No background jobs.";
            Grid.AddColumn(new GridColumn<Job>("name", "Name", j => String.IsNullOrEmpty(j.ErrorReason) ? j.Name : j.Name + "  (" + j.ErrorReason + ")") { Weight = 3 });
            Grid.AddColumn(new GridColumn<Job>("kind", "Kind", j => j.Kind.ToString()) { Width = 20 });
            Grid.AddColumn(new GridColumn<Job>("status", "Status", j => StatusBadge.Label(j.Status.ToString())) { Width = 14, Style = (j, t) => StatusBadge.Style(j.Status.ToString(), t) });
            Grid.AddColumn(new GridColumn<Job>("progress", "Progress", j => j.Progress + "%") { Width = 9 });
            Grid.AddColumn(new GridColumn<Job>("created", "Created", j => ScreenOps.Relative(Context, j.CreatedUtc)) { Width = 10, Style = (j, t) => t.Muted });
            Grid.AddColumn(new GridColumn<Job>("updated", "Updated", j => ScreenOps.Relative(Context, j.LastUpdateUtc)) { Width = 10, Style = (j, t) => t.Muted });
            Grid.Loader = LoadAsync;
            BindPreferences(null, false, 50);
            AddFill(Grid);
            Scope.Focus(Grid);
            Grid.Reload();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when a job can still be cancelled.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <returns>True when unfinished.</returns>
        public static bool IsCancellable(Job job)
        {
            return job != null && !_Terminal.Contains(job.Status);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(Job row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            ActionMenuItem cancel = new ActionMenuItem("Cancel", () => Cancel(row), "x");
            cancel.Enabled = IsCancellable(row);
            items.Add(cancel);
            items.Add(new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), row), "j"));
            return items;
        }

        /// <inheritdoc />
        protected override string JsonTitle(Job row)
        {
            return Context.Loc.T("Job") + ": " + row.Id;
        }

        /// <inheritdoc />
        protected override IEnumerable<Armada.Tui.Input.ArmadaCommand> ExtraCommands()
        {
            return new List<Armada.Tui.Input.ArmadaCommand>
            {
                Command(ScreenKey + ".cancel", "Cancel Job", CancelCurrent, () => Grid.Current != null && IsCancellable(Grid.Current), "x"),
            };
        }

        #endregion

        #region Private-Methods

        private async Task<GridPage<Job>> LoadAsync(GridQuery query, CancellationToken token)
        {
            EnumerationResult<Job>? result = await Context.Client.ListJobsAsync(token).ConfigureAwait(false);
            List<Job> rows = result?.Objects ?? new List<Job>();
            int skip = (query.PageNumber - 1) * query.PageSize;
            return new GridPage<Job>(rows.Skip(skip).Take(query.PageSize).ToList(), rows.Count);
        }

        private void CancelCurrent()
        {
            Job? job = Grid.Current;
            if (job != null && IsCancellable(job)) Cancel(job);
        }

        private void Cancel(Job job)
        {
            ScreenOps.Run(Context, () => Context.Client.CancelJobAsync(job.Id), r =>
            {
                ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Job \"{{name}}\" cancelled.", LocalizationArgs.Of("name", job.Name));
                Refresh();
            }, "Failed to cancel job.");
        }

        #endregion
    }
}
