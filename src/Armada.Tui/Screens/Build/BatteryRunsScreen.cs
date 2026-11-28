namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Battery Runs (the Battery Runs tab of <c>/captains</c>), the dashboard's Battery Runs tab: Full captain
    /// evaluations and acceptance gate runs, newest first, with a status filter and Cancel for a queued or running run
    /// (its open mission is cancelled and its dock released). Not thread-safe.
    /// </summary>
    public class BatteryRunsScreen : OpsListScreen<BatteryRun>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Full captain evaluations and acceptance gate runs, newest first."; }
        }

        /// <summary>
        /// Status filter (server side).
        /// </summary>
        public SelectField<string> StatusFilter { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public BatteryRunsScreen(RouteMatch route, TuiContext context)
            : base(route, context, r => r.Id, "BatteryRunsScreen", "Battery Runs")
        {
            Grid.EmptyText = "No battery runs yet.";
            List<SelectOption<string>> statuses = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All")) };
            foreach (BatteryRunStatusEnum s in Enum.GetValues(typeof(BatteryRunStatusEnum)))
                statuses.Add(new SelectOption<string>(s.ToString(), Tr(s.ToString())));
            StatusFilter = SelectFilter("Status", statuses);

            Column("id", "ID", r => r.Id, 0, 26);
            Column("status", "Status", r => Tr(r.Status.ToString()), 0, 12);
            Column("captain", "Captain", r => r.CaptainName ?? r.CaptainId ?? "-", 2);
            Column("model", "Model", r => r.Model ?? "-", 2);
            Column("passed", "Passed", r => r.PassedCount.ToString(CultureInfo.InvariantCulture), 0, 7);
            Column("failed", "Failed", r => r.FailedCount.ToString(CultureInfo.InvariantCulture), 0, 7);
            Column("total", "Total", r => r.TotalTasks.ToString(CultureInfo.InvariantCulture), 0, 7);
            Column("createdUtc", "Created", r => Context.Loc.FormatRelative(r.CreatedUtc, Context.Clock.UtcNow), 0, 14, r => r.CreatedUtc);

            RowActions.Add(new OpsAction<BatteryRun>("json", "View JSON", r => ShowJson(Tr("Battery Run") + ": " + r.Id, r), "j"));
            OpsAction<BatteryRun> cancel = new OpsAction<BatteryRun>("cancel", "Cancel Run", Cancel, "x", r => IsOpen(r));
            cancel.Danger = true;
            RowActions.Add(cancel);
            RowActions.Add(new OpsAction<BatteryRun>("captain", "Open Captain", r => Context.Navigate("/captains/" + Uri.EscapeDataString(r.CaptainId!)), "C", r => !String.IsNullOrEmpty(r.CaptainId)));
            RowActions.Add(new OpsAction<BatteryRun>("copy-id", "Copy ID", r => Copy(r.Id, "Battery Run ID"), "y"));

            Start("createdUtc", true);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the run can still be cancelled (queued or running).
        /// </summary>
        /// <param name="run">Run.</param>
        /// <returns>True for a queued or running run.</returns>
        /// <exception cref="ArgumentNullException">When run is null.</exception>
        public static bool IsOpen(BatteryRun run)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            return run.Status == BatteryRunStatusEnum.Queued || run.Status == BatteryRunStatusEnum.Running;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<BatteryRun>> FetchAsync(GridQuery query, CancellationToken token)
        {
            BatteryRunQuery q = new BatteryRunQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            if (!String.IsNullOrEmpty(StatusFilter.Value) && Enum.TryParse<BatteryRunStatusEnum>(StatusFilter.Value, out BatteryRunStatusEnum status)) q.Status = status;
            EnumerationResult<BatteryRun>? result = await Context.Client.Battery.ListRunsAsync(q, token).ConfigureAwait(false);
            List<BatteryRun> rows = result?.Objects ?? new List<BatteryRun>();
            return new GridPage<BatteryRun>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override string RowTitle(BatteryRun row)
        {
            return Tr("Battery Run") + ": " + row.Id;
        }

        #endregion

        #region Private-Methods

        private void Cancel(BatteryRun run)
        {
            Confirm("Cancel Battery Run", Tr("Cancel battery run {{id}}? Its open mission is cancelled and its dock released.", LocalizationArgs.Of("id", run.Id)), () =>
            {
                Call((c, t) => c.Battery.CancelRunAsync(run.Id, null, t), r =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Battery run {{id}} cancel requested.", LocalizationArgs.Of("id", run.Id)));
                    Refresh();
                }, "Cancel failed.");
            }, "Cancel Run");
        }

        #endregion
    }
}
