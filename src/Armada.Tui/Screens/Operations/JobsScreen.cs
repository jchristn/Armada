namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Jobs (W3.11, <c>/jobs</c>), the dashboard's Jobs page: background jobs with name (and error reason), kind,
    /// status, progress, created, and updated; Cancel while unfinished; auto-refresh (default 15 s). The TUI adds
    /// View JSON and Copy ID. Not thread-safe.
    /// </summary>
    public class JobsScreen : OpsListScreen<Job>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Background jobs and their status."; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public JobsScreen(RouteMatch route, TuiContext context)
            : base(route, context, j => j.Id, "JobsScreen", "Jobs")
        {
            Grid.MultiSelect = false;
            Grid.EmptyText = "No background jobs.";
            Column("name", "Name", j => j.Name + (String.IsNullOrEmpty(j.ErrorReason) ? "" : "  (" + j.ErrorReason + ")"), 4, null, j => j.Name);
            Column("kind", "Kind", j => j.Kind.ToString(), 2, null, j => j.Kind.ToString());
            Column("status", "Status", j => StatusBadge.Label(j.Status), 0, 14, j => j.Status.ToString(), (j, t) => StatusBadge.Style(j.Status, t));
            Column("progress", "Progress", j => j.Progress + "%", 0, 9, j => j.Progress).Align = TUIKit.Widgets.CellAlignment.Right;
            Column("createdUtc", "Created", j => Context.Loc.FormatRelative(j.CreatedUtc, Context.Clock.UtcNow), 0, 16, j => j.CreatedUtc);
            Column("lastUpdateUtc", "Updated", j => Context.Loc.FormatRelative(j.LastUpdateUtc, Context.Clock.UtcNow), 0, 16, j => j.LastUpdateUtc);

            OpsAction<Job> cancel = new OpsAction<Job>("cancel", "Cancel", Cancel, "x", j => !IsTerminal(j.Status));
            cancel.Danger = true;
            RowActions.Add(cancel);
            RowActions.Add(new OpsAction<Job>("json", "View JSON", j => ShowJson(Tr("Job") + ": " + j.Name, j), "j"));
            RowActions.Add(new OpsAction<Job>("copy-id", "Copy ID", j => Copy(j.Id, "Job ID"), "y"));
            Start("createdUtc", true);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True for Succeeded, Failed, and Cancelled (the dashboard's TERMINAL list).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True when finished.</returns>
        public static bool IsTerminal(JobStatusEnum status)
        {
            return status == JobStatusEnum.Succeeded || status == JobStatusEnum.Failed || status == JobStatusEnum.Cancelled;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool ServerPaging
        {
            get { return false; }
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Job>> FetchAsync(GridQuery query, CancellationToken token)
        {
            try
            {
                EnumerationResult<Job>? result = await Context.Client.ListJobsAsync(token).ConfigureAwait(false);
                List<Job> rows = result?.Objects ?? new List<Job>();
                return new GridPage<Job>(rows, rows.Count);
            }
            catch (Armada.Client.ArmadaApiException ex)
            {
                throw new Armada.Client.ArmadaApiException(Tr("Failed to load jobs."), ex.StatusCode, ex.Code, ex.ErrorName, ex.RequestId, ex.Method, ex.Path, ex.ResponseBody, ex.ErrorData, ex);
            }
        }

        /// <inheritdoc />
        protected override string RowTitle(Job row)
        {
            return row.Name;
        }

        #endregion

        #region Private-Methods

        private void Cancel(Job job)
        {
            Run((c, t) => c.CancelJobAsync(job.Id, t), () =>
            {
                Toast(NotificationSeverityEnum.Warning, Tr("Job \"{{name}}\" cancelled.", LocalizationArgs.Of("name", job.Name)));
                Refresh();
            }, "Failed to cancel job.");
        }

        #endregion
    }
}
