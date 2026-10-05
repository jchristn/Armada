namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Voyages (W3.9, the Voyages tab of <c>/missions</c>), the dashboard's Voyages page: server paging with the
    /// user-scope filter, the title and status column filters, sortable title and status, multi-select with Cancel
    /// Selected, + Voyage (Create Voyage), and the row menu (View Detail, View Status, View JSON, Cancel, Purge).
    /// Refreshes on <c>voyage.changed</c> and auto-refresh. Not thread-safe.
    /// </summary>
    public class VoyagesScreen : OpsListScreen<Voyage>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Batches of related missions dispatched together"; }
        }

        /// <summary>
        /// Title column filter.
        /// </summary>
        public TextInput TitleFilter { get; }

        /// <summary>
        /// Status column filter.
        /// </summary>
        public TextInput StatusFilter { get; }

        /// <summary>
        /// User scope filter (admins only).
        /// </summary>
        public SelectField<string>? UserScope { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public VoyagesScreen(RouteMatch route, TuiContext context)
            : base(route, context, v => v.Id, "VoyagesScreen", "Voyages")
        {
            Grid.EmptyText = "No voyages found.";
            UserScope = UserScopeFilter();
            TitleFilter = TextFilter("Title");
            StatusFilter = TextFilter("Status", 12);

            Column("title", "Title", v => v.Title, 4, null, v => v.Title);
            Column("id", "ID", v => v.Id, 0, 24);
            Column("status", "Status", v => StatusBadge.Label(v.Status.ToString()), 0, 14, v => v.Status.ToString(), (v, t) => StatusBadge.Style(v.Status.ToString(), t));
            Column("autoPush", "Auto Push", v => YesNo(v.AutoPush), 0, 10);
            Column("autoCreatePullRequests", "Auto Create PRs", v => YesNo(v.AutoCreatePullRequests), 0, 15);
            Column("landingMode", "Landing Mode", v => v.LandingMode?.ToString() ?? "-", 1);
            GridColumn<Voyage> created = Column("createdUtc", "Created", v => Context.Loc.FormatRelative(v.CreatedUtc, Context.Clock.UtcNow), 0, 14, v => v.CreatedUtc);
            created.DefaultVisible = false;

            OpsScreenAction bulk = new OpsScreenAction("cancel-selected", "Cancel Selected", BulkCancel, "X", () => Grid.Marked.Count > 0);
            bulk.DynamicLabel = () => Tr("Cancel Selected") + " (" + Grid.Marked.Count + ")";
            bulk.Danger = true;
            ScreenActions.Add(bulk);
            ScreenActions.Add(new OpsScreenAction("new", "+ Voyage", () => Context.Navigate("/voyages/create"), "n"));

            RowActions.Add(new OpsAction<Voyage>("view", "View Detail", v => OpenRow(v), "o"));
            RowActions.Add(new OpsAction<Voyage>("status", "View Status", ViewStatus, "u"));
            RowActions.Add(new OpsAction<Voyage>("json", "View JSON", v => ShowJson("Voyage: " + v.Title, v), "j"));
            RowActions.Add(new OpsAction<Voyage>("copy-id", "Copy ID", v => Copy(v.Id, "Voyage ID"), "y"));
            OpsAction<Voyage> cancel = new OpsAction<Voyage>("cancel", "Cancel", v => Cancel(v), "x");
            cancel.Danger = true;
            RowActions.Add(cancel);
            OpsAction<Voyage> purge = new OpsAction<Voyage>("purge", "Purge", v => Purge(v), "del");
            purge.Danger = true;
            RowActions.Add(purge);

            SubscribeCoalesced("voyage.changed", Refresh);
            Start("createdUtc", true);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<Voyage>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("userId", UserScope?.Value);
            EnumerationResult<Voyage>? result = await Context.Client.ListVoyagesAsync(q, token).ConfigureAwait(false);
            List<Voyage> rows = result?.Objects ?? new List<Voyage>();
            return new GridPage<Voyage>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<Voyage> FilterLocal(IEnumerable<Voyage> rows)
        {
            string title = TitleFilter.Value.Trim();
            string status = StatusFilter.Value.Trim();
            return rows.Where(v =>
                (title.Length == 0 || (v.Title ?? "").IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (status.Length == 0 || v.Status.ToString().IndexOf(status, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<Voyage> rows)
        {
            Grid.EmptyText = rows.Count == 0 ? "No voyages found." : "No voyages match the current filters.";
        }

        /// <inheritdoc />
        protected override void OpenRow(Voyage row)
        {
            Context.Navigate("/voyages/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(Voyage row)
        {
            return row.Title;
        }

        #endregion

        #region Private-Methods

        private string YesNo(bool? value)
        {
            return value.HasValue ? Tr(value.Value ? "Yes" : "No") : "-";
        }

        private void ViewStatus(Voyage v)
        {
            Call((c, t) => c.GetVoyageStatusAsync(v.Id, t), status => ShowJsonText(Tr("Voyage Status"), status?.Json ?? ""), null, ex => ShowMessage(Tr("Failed to load voyage status.")));
        }

        private void Cancel(Voyage v)
        {
            Confirm("Cancel Voyage", Tr("Cancel voyage \"{{title}}\"? All pending missions will be cancelled.", LocalizationArgs.Of("title", v.Title)), () =>
            {
                Run((c, t) => c.CancelVoyageAsync(v.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Voyage \"{{title}}\" cancelled.", LocalizationArgs.Of("title", v.Title)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Cancel failed.")));
            }, "Cancel Voyage");
        }

        private void Purge(Voyage v)
        {
            Confirm("Purge Voyage", Tr("Purge voyage \"{{title}}\"? This will permanently remove the voyage and all associated missions. This cannot be undone.", LocalizationArgs.Of("title", v.Title)), () =>
            {
                Run((c, t) => c.PurgeVoyageAsync(v.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Voyage \"{{title}}\" purged.", LocalizationArgs.Of("title", v.Title)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Purge failed.")));
            }, "Purge");
        }

        private void BulkCancel()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Confirm("Cancel Selected Voyages", Tr("Cancel {{count}} selected voyage(s)?", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                Call(async (c, t) =>
                {
                    int failed = 0;
                    foreach (string id in ids)
                    {
                        try { await c.CancelVoyageAsync(id, t).ConfigureAwait(false); }
                        catch (Armada.Client.ArmadaApiException) { failed++; }
                    }

                    return failed;
                }, failed =>
                {
                    int success = ids.Count - failed;
                    if (success > 0)
                    {
                        Toast(failed > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success, failed > 0
                            ? Tr("Cancelled {{success}} voyages. {{failed}} failed.", LocalizationArgs.Of("success", success, "failed", failed))
                            : Tr("Cancelled {{success}} voyages.", LocalizationArgs.Of("success", success)));
                    }

                    if (failed > 0) ShowMessage(Tr("Cancelled {{success}} voyages, {{failed}} failed.", LocalizationArgs.Of("success", success, "failed", failed)));
                    Refresh();
                }, "Cancel failed.");
            }, "Cancel Voyages");
        }

        #endregion
    }
}
