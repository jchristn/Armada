namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Fleets (W4.5, the Fleets tab of <c>/vessels</c>), the dashboard's Fleets page: every fleet with its vessel count,
    /// local name and description filters, sortable name, description, vessels, and created; Delete Selected; + Fleet;
    /// and the row menu (View Detail, Edit, Duplicate, View JSON, Delete). Not thread-safe.
    /// </summary>
    public class FleetsScreen : OpsListScreen<Fleet>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Fleets are groups of vessels (repositories) useful for organizing and understanding relationships amongst code assets."; }
        }

        /// <summary>
        /// Name filter.
        /// </summary>
        public TextInput NameFilter { get; }

        /// <summary>
        /// Description filter.
        /// </summary>
        public TextInput DescriptionFilter { get; }

        /// <summary>
        /// Pipelines from the last load (for the form).
        /// </summary>
        public List<Pipeline> Pipelines { get; private set; } = new List<Pipeline>();

        #endregion

        #region Private-Members

        private Dictionary<string, int> _VesselCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private List<Pipeline> _LoadedPipelines = new List<Pipeline>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public FleetsScreen(RouteMatch route, TuiContext context)
            : base(route, context, f => f.Id, "FleetsScreen", "Fleets")
        {
            Grid.EmptyText = "No fleets configured.";
            NameFilter = TextFilter("Name", 18, "Filter...");
            DescriptionFilter = TextFilter("Description", 18, "Filter...");

            Column("name", "Name", f => f.Name, 3, null, f => f.Name.ToLowerInvariant()).Pinned = true;
            Column("id", "ID", f => f.Id, 0, 26);
            Column("description", "Description", f => String.IsNullOrEmpty(f.Description) ? "-" : f.Description!, 4, null, f => f.Description ?? "");
            Column("vessels", "Vessels", f => VesselCount(f.Id).ToString(System.Globalization.CultureInfo.InvariantCulture), 0, 8, f => VesselCount(f.Id)).Align = TUIKit.Widgets.CellAlignment.Right;
            Column("active", "Active", f => Tr(f.Active ? "Yes" : "No"), 0, 7);
            Column("createdUtc", "Created", f => Context.Loc.FormatRelative(f.CreatedUtc, Context.Clock.UtcNow), 0, 14, f => f.CreatedUtc);

            OpsScreenAction bulk = new OpsScreenAction("delete-selected", "Delete Selected", BulkDelete, "D", () => Grid.Marked.Count > 0);
            bulk.DynamicLabel = () => Tr("Delete Selected") + " (" + Grid.Marked.Count + ")";
            bulk.Danger = true;
            ScreenActions.Add(bulk);
            ScreenActions.Add(new OpsScreenAction("new", "+ Fleet", () => FleetForm.Open(this, null, Pipelines, f => Refresh()), "n"));

            RowActions.Add(new OpsAction<Fleet>("view", "View Detail", f => OpenRow(f), "o"));
            RowActions.Add(new OpsAction<Fleet>("edit", "Edit", f => FleetForm.Open(this, f, Pipelines, x => Refresh()), "e"));
            RowActions.Add(new OpsAction<Fleet>("duplicate", "Duplicate", Duplicate, "u"));
            RowActions.Add(new OpsAction<Fleet>("json", "View JSON", f => ShowJson(Tr("Fleet") + ": " + f.Name, f), "j"));
            RowActions.Add(new OpsAction<Fleet>("copy-id", "Copy ID", f => Copy(f.Id, "Fleet ID"), "y"));
            OpsAction<Fleet> delete = new OpsAction<Fleet>("delete", "Delete", Delete, "del");
            delete.Danger = true;
            RowActions.Add(delete);

            Start("name", false);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Vessels in a fleet from the last load.
        /// </summary>
        /// <param name="fleetId">Fleet id.</param>
        /// <returns>Count.</returns>
        public int VesselCount(string fleetId)
        {
            return _VesselCounts.TryGetValue(fleetId, out int n) ? n : 0;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool ServerPaging
        {
            get { return false; }
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Fleet>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery all = new ArmadaPageQuery(1, 9999);
            Task<EnumerationResult<Fleet>?> fleets = Context.Client.ListFleetsAsync(all, token);
            Task<EnumerationResult<Vessel>?> vessels = Context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 9999), token);
            Task<EnumerationResult<Pipeline>?> pipelines = Context.Client.ListPipelinesAsync(new ArmadaPageQuery(1, 9999), token);
            await Task.WhenAll(fleets, vessels, pipelines).ConfigureAwait(false);
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Vessel v in vessels.Result?.Objects ?? new List<Vessel>())
            {
                if (String.IsNullOrEmpty(v.FleetId)) continue;
                counts[v.FleetId!] = counts.TryGetValue(v.FleetId!, out int n) ? n + 1 : 1;
            }

            _VesselCounts = counts;
            _LoadedPipelines = pipelines.Result?.Objects ?? new List<Pipeline>();
            List<Fleet> rows = fleets.Result?.Objects ?? new List<Fleet>();
            return new GridPage<Fleet>(rows, rows.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<Fleet> FilterLocal(IEnumerable<Fleet> rows)
        {
            string name = NameFilter.Value.Trim();
            string description = DescriptionFilter.Value.Trim();
            return rows.Where(f =>
                (name.Length == 0 || f.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (description.Length == 0 || (f.Description ?? "").IndexOf(description, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<Fleet> rows)
        {
            Pipelines = _LoadedPipelines;
            Grid.EmptyText = rows.Count == 0 ? "No fleets configured." : "No fleets match the current filters.";
        }

        /// <inheritdoc />
        protected override void OpenRow(Fleet row)
        {
            Context.Navigate("/fleets/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(Fleet row)
        {
            return row.Name;
        }

        #endregion

        #region Private-Methods

        private void Duplicate(Fleet fleet)
        {
            Call((c, t) => c.CreateFleetAsync(FleetForm.DuplicatePayload(fleet), t), created =>
            {
                if (created == null) return;
                Toast(NotificationSeverityEnum.Success, Tr("Fleet \"{{name}}\" duplicated.", LocalizationArgs.Of("name", created.Name)));
                Context.Navigate("/fleets/" + Uri.EscapeDataString(created.Id));
            }, null, ex => ShowMessage(ex is ArmadaApiException api ? api.Message : Tr("Duplicate failed.")));
        }

        private void Delete(Fleet fleet)
        {
            Confirm("Delete Fleet", Tr("Delete fleet \"{{name}}\"? This cannot be undone.", LocalizationArgs.Of("name", fleet.Name)), () =>
            {
                Run((c, t) => c.DeleteFleetAsync(fleet.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Fleet \"{{name}}\" deleted.", LocalizationArgs.Of("name", fleet.Name)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Delete failed.")));
            }, "Delete");
        }

        private void BulkDelete()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Confirm("Delete Selected Fleets", Tr("Delete {{count}} selected fleet(s)? This cannot be undone.", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                Call(async (c, t) =>
                {
                    int failed = 0;
                    foreach (string id in ids)
                    {
                        try { await c.DeleteFleetAsync(id, t).ConfigureAwait(false); }
                        catch (ArmadaApiException) { failed++; }
                    }

                    return failed;
                }, failed =>
                {
                    int deleted = ids.Count - failed;
                    if (deleted > 0)
                    {
                        Toast(failed > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success, failed > 0
                            ? Tr("Deleted {{deleted}} fleets. {{failed}} failed.", LocalizationArgs.Of("deleted", deleted, "failed", failed))
                            : Tr("Deleted {{deleted}} fleets.", LocalizationArgs.Of("deleted", deleted)));
                    }

                    if (failed > 0) ShowMessage(Tr("Deleted {{deleted}} fleets, {{failed}} failed.", LocalizationArgs.Of("deleted", deleted, "failed", failed)));
                    Refresh();
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
