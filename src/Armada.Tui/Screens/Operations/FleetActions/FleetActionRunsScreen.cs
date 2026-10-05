namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Fleet Actions: Runs tab (W3.7, <c>/fleet-actions?tab=runs</c>), the dashboard's FleetActionRunsTable: runs
    /// with the status filter (also read from <c>?status=</c>), action (with the Ad hoc tag), kind, status, progress,
    /// created (sortable on the server), started, completed, and duration; the row menu (View, Cancel with
    /// confirmation while active, View JSON); auto-refresh (paused while a dialog is open) and refresh on
    /// fleet action and voyage events. Not thread-safe.
    /// </summary>
    public class FleetActionRunsScreen : OpsListScreen<FleetActionRun>
    {
        #region Public-Members

        /// <summary>
        /// Status filter.
        /// </summary>
        public SelectField<string> StatusFilter { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public FleetActionRunsScreen(RouteMatch route, TuiContext context)
            : base(route, context, r => r.Id, "FleetActionRunsScreen", "Runs")
        {
            Grid.MultiSelect = false;
            Grid.EmptyText = "No fleet action runs yet";
            List<SelectOption<string>> statuses = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All statuses")) };
            statuses.AddRange(FleetActionLabels.RunStatuses.Select(s => new SelectOption<string>(s.ToString(), Tr(FleetActionLabels.RunStatusLabel(s)))));
            StatusFilter = SelectFilter("Status", statuses, 26, true);
            string? initial = OpsHandoff.Get(route, "status");
            if (initial != null && statuses.Any(o => o.Value == initial)) StatusFilter.SetValue(initial);

            Column("action", "Action", r => r.ActionName + (r.ActionId == null ? "  [" + Tr("Ad hoc") + "]" : ""), 3);
            Column("id", "ID", r => r.Id, 0, 24).DefaultVisible = false;
            Column("kind", "Kind", r => Tr(r.Kind.ToString()), 0, 9);
            Column("status", "Status", r => StatusBadge.Marker(FleetActionLabels.RunTone(r.Status)) + " " + Tr(FleetActionLabels.RunStatusLabel(r.Status)), 0, 18, null, (r, t) => StatusBadge.Style(FleetActionLabels.RunTone(r.Status), t));
            Column("progress", "Progress", r => FleetActionLabels.ProgressBar(r, 8) + " " + FleetActionLabels.ProgressText(Context.Loc, r, false), 6);
            Column("createdUtc", "Created", r => Context.Loc.FormatRelative(r.CreatedUtc, Context.Clock.UtcNow), 0, 11, r => r.CreatedUtc);
            Column("startedUtc", "Started", r => r.StartedUtc.HasValue ? Context.Loc.FormatRelative(r.StartedUtc.Value, Context.Clock.UtcNow) : "-", 0, 11);
            Column("completedUtc", "Completed", r => r.CompletedUtc.HasValue ? Context.Loc.FormatRelative(r.CompletedUtc.Value, Context.Clock.UtcNow) : "-", 0, 11);
            Column("duration", "Duration", r => FleetActionLabels.FormatDuration(Context.Loc, FleetActionLabels.DurationBetween(r.StartedUtc, r.CompletedUtc, FleetActionLabels.IsRunActive(r.Status), Context.Clock.UtcNow)), 0, 11).Align = TUIKit.Widgets.CellAlignment.Right;

            RowActions.Add(new OpsAction<FleetActionRun>("view", "View", r => OpenRow(r), "o"));
            OpsAction<FleetActionRun> cancel = new OpsAction<FleetActionRun>("cancel", "Cancel", CancelRun, "x", r => FleetActionLabels.IsRunActive(r.Status) && IsTenantAdmin);
            cancel.Danger = true;
            RowActions.Add(cancel);
            RowActions.Add(new OpsAction<FleetActionRun>("json", "View JSON", r => ShowJson(Tr("Fleet action run: {{name}}", LocalizationArgs.Of("name", r.ActionName)), r), "j"));
            RowActions.Add(new OpsAction<FleetActionRun>("copy-id", "Copy ID", r => Copy(r.Id, "Run ID"), "y"));
            ScreenActions.Add(new OpsScreenAction("clear-filter", "Clear filter", () => StatusFilter.Choose(StatusFilter.Options[0]), null, () => !String.IsNullOrEmpty(StatusFilter.Value)));
            SubscribeCoalesced("voyage.changed", () => { if (LoadedRows.Any(r => FleetActionLabels.IsRunActive(r.Status))) Refresh(); });
            Start("createdUtc", true);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<FleetActionRun>> FetchAsync(GridQuery query, CancellationToken token)
        {
            FleetActionRunEnumerateQuery q = new FleetActionRunEnumerateQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            q.Order = query.SortKey == "createdUtc" && !query.SortDescending ? "CreatedAscending" : "CreatedDescending";
            string? status = StatusFilter.Value;
            if (!String.IsNullOrEmpty(status) && Enum.TryParse(status, out FleetActionRunStatusEnum parsed)) q.Status = parsed;
            EnumerationResult<FleetActionRun>? result = await Context.Client.EnumerateFleetActionRunsAsync(q, token).ConfigureAwait(false);
            List<FleetActionRun> rows = result?.Objects ?? new List<FleetActionRun>();
            return new GridPage<FleetActionRun>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<FleetActionRun> rows)
        {
            bool filtered = !String.IsNullOrEmpty(StatusFilter.Value);
            Grid.EmptyText = ServerTotal == 0
                ? (filtered ? "No runs match this status" : "No fleet action runs yet")
                : "No runs on this page.";
        }

        /// <inheritdoc />
        protected override int AboveHeight(int width)
        {
            return HasLoaded && ServerTotal == 0 && String.IsNullOrEmpty(StatusFilter.Value) ? 1 : 0;
        }

        /// <inheritdoc />
        protected override void RenderAbove(TUIKit.ISurface surface, int width)
        {
            Armada.Tui.Text.SurfaceText.Draw(surface, 0, 0, Tr("Select vessels on the Vessels page and choose Run action... in the selection bar, or run an action from the Actions tab."), Theme.Muted, width);
        }

        /// <inheritdoc />
        protected override void OpenRow(FleetActionRun row)
        {
            Context.Navigate("/fleet-actions/runs/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(FleetActionRun row)
        {
            return row.ActionName;
        }

        #endregion

        #region Private-Methods

        private void CancelRun(FleetActionRun run)
        {
            FleetActionRunScreen.ConfirmCancel(this, run, Refresh);
        }

        #endregion
    }
}
