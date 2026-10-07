namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Concurrent;
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
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// Vessels (W4.1, the Vessels tab of <c>/vessels</c>), the dashboard's Vessels page: every vessel (admin user scope on
    /// the server) with the name, fleet, repository, and landing mode filters; sortable name, fleet, repository, and
    /// created; the repository URL with the default branch, landing mode with its short label, sync (commits ahead
    /// and behind the remote, fetched per vessel in the background), and branch count; Import repositories (tenant
    /// admins) and + Vessel; bulk Run action (tenant admins), Delete Selected, and Clear selection; and the row menu
    /// (Manage Branches, Manage Objectives, Manage Fleet, Open Workspace, View Detail, View History, Build or Refine Context, Edit,
    /// Duplicate, View JSON, Delete). Not thread-safe.
    /// </summary>
    public class VesselsScreen : OpsListScreen<Vessel>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Git repositories registered with Armada"; }
        }

        /// <summary>
        /// Name filter.
        /// </summary>
        public TextInput NameFilter { get; }

        /// <summary>
        /// Fleet filter.
        /// </summary>
        public SelectField<string> FleetFilter { get; }

        /// <summary>
        /// Repository filter.
        /// </summary>
        public TextInput RepoFilter { get; }

        /// <summary>
        /// Landing mode filter.
        /// </summary>
        public SelectField<string> LandingFilter { get; }

        /// <summary>
        /// User scope filter (admins only).
        /// </summary>
        public SelectField<string>? UserScope { get; }

        /// <summary>
        /// Fleets from the last load.
        /// </summary>
        public List<Fleet> Fleets { get; private set; } = new List<Fleet>();

        /// <summary>
        /// Pipelines from the last load.
        /// </summary>
        public List<Pipeline> Pipelines { get; private set; } = new List<Pipeline>();

        /// <summary>
        /// Git status by vessel id (null counts when unavailable).
        /// </summary>
        public ConcurrentDictionary<string, VesselGitStatusResult?> GitStatus { get; } = new ConcurrentDictionary<string, VesselGitStatusResult?>(StringComparer.Ordinal);

        /// <summary>
        /// Branch counts by vessel id (null when unavailable).
        /// </summary>
        public ConcurrentDictionary<string, int?> BranchCounts { get; } = new ConcurrentDictionary<string, int?>(StringComparer.Ordinal);

        #endregion

        #region Private-Members

        private List<Fleet> _LoadedFleets = new List<Fleet>();
        private List<Pipeline> _LoadedPipelines = new List<Pipeline>();
        private int _StatusGeneration = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public VesselsScreen(RouteMatch route, TuiContext context)
            : base(route, context, v => v.Id, "VesselsScreen", "Vessels")
        {
            Grid.EmptyText = "No vessels configured.";
            UserScope = UserScopeFilter();
            NameFilter = TextFilter("Name", 16, "Search...");
            FleetFilter = SelectFilter("Fleet", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Fleets")) }, 16, false);
            RepoFilter = TextFilter("Repository", 16, "Search...");
            List<SelectOption<string>> modes = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Modes")) };
            modes.AddRange(LandingModeInfo.All.Where(m => m.Value.Length > 0).Select(m => new SelectOption<string>(m.Value, m.Value + " -- " + Tr(m.Short), Tr(m.Description))));
            LandingFilter = SelectFilter("Landing Mode", modes, 20, false);

            Column("name", "Name", v => v.Name, 3, null, v => v.Name.ToLowerInvariant()).Pinned = true;
            Column("id", "ID", v => v.Id, 0, 24);
            Column("fleetId", "Fleet", v => FleetName(v.FleetId), 2, null, v => FleetName(v.FleetId).ToLowerInvariant());
            Column("repoUrl", "Repository", v => String.IsNullOrEmpty(v.RepoUrl) ? "-" : v.RepoUrl!, 5, null, v => (v.RepoUrl ?? "").ToLowerInvariant());
            Column("defaultBranch", "Branch", v => String.IsNullOrEmpty(v.DefaultBranch) ? "main" : v.DefaultBranch, 0, 12, v => (String.IsNullOrEmpty(v.DefaultBranch) ? "main" : v.DefaultBranch).ToLowerInvariant());
            Column("landingMode", "Landing Mode", v => (v.LandingMode?.ToString() ?? Tr("Default")) + " (" + Tr(LandingModeInfo.For(v.LandingMode?.ToString()).Short) + ")", 0, 30);
            Column("sync", "Sync", SyncText, 0, 14, null, SyncStyle);
            Column("branches", "Branches", v => BranchCounts.TryGetValue(v.Id, out int? n) && n.HasValue ? Tr("{{count}} branches", LocalizationArgs.Of("count", n.Value)) : Tr("Branches"), 0, 11);
            GridColumn<Vessel> created = Column("createdUtc", "Created", v => Context.Loc.FormatRelative(v.CreatedUtc, Context.Clock.UtcNow), 0, 14, v => v.CreatedUtc);
            created.DefaultVisible = false;

            OpsScreenAction run = new OpsScreenAction("run-action", "Run action...", RunAction, "A", () => Grid.Marked.Count > 0 && IsTenantAdmin);
            ScreenActions.Add(run);
            OpsScreenAction bulk = new OpsScreenAction("delete-selected", "Delete Selected", BulkDelete, "D", () => Grid.Marked.Count > 0);
            bulk.DynamicLabel = () => Tr("Delete Selected") + " (" + Grid.Marked.Count + ")";
            bulk.Danger = true;
            ScreenActions.Add(bulk);
            ScreenActions.Add(new OpsScreenAction("clear-selection", "Clear selection", () => Grid.ClearMarks(), null, () => Grid.Marked.Count > 0));
            ScreenActions.Add(new OpsScreenAction("import", "Import repositories", () => Context.Navigate("/vessels/import"), "I", () => IsTenantAdmin));
            ScreenActions.Add(new OpsScreenAction("new", "+ Vessel", () => VesselForm.Open(this, null, Fleets, Pipelines, v => Refresh()), "n"));

            RowActions.Add(new OpsAction<Vessel>("dispatch", "Dispatch", v => Context.Navigate(DispatchRoute(v)), "d"));
            RowActions.Add(new OpsAction<Vessel>("branches", "Manage Branches", v => ManageBranches(v), "b"));
            RowActions.Add(new OpsAction<Vessel>("objectives", "Manage Objectives", v => Context.Navigate(ObjectivesRoute(v)), "O"));
            RowActions.Add(new OpsAction<Vessel>("fleet", "Manage Fleet", v => Context.Navigate("/fleets/" + Uri.EscapeDataString(v.FleetId!)), "f", v => !String.IsNullOrEmpty(v.FleetId)));
            RowActions.Add(new OpsAction<Vessel>("workspace", "Open Workspace", v => Context.Navigate("/workspace/" + Uri.EscapeDataString(v.Id)), "w"));
            RowActions.Add(new OpsAction<Vessel>("view", "View Detail", v => OpenRow(v), "o"));
            RowActions.Add(new OpsAction<Vessel>("history", "View History", v => Context.Navigate(HistoryRoute(v.Id)), "H"));
            RowActions.Add(new OpsAction<Vessel>("context", "Build Context", v => BuildContextForm.Open(this, v, updated => Refresh()), "B", v => !BuildContextForm.IsRefine(v)));
            RowActions.Add(new OpsAction<Vessel>("refine-context", "Refine Context", v => BuildContextForm.Open(this, v, updated => Refresh()), "B", BuildContextForm.IsRefine));
            RowActions.Add(new OpsAction<Vessel>("edit", "Edit", v => VesselForm.Open(this, v, Fleets, Pipelines, x => Refresh()), "e"));
            RowActions.Add(new OpsAction<Vessel>("duplicate", "Duplicate", Duplicate, "u"));
            RowActions.Add(new OpsAction<Vessel>("json", "View JSON", v => ShowJson("Vessel: " + v.Name, v), "j"));
            RowActions.Add(new OpsAction<Vessel>("copy-id", "Copy ID", v => Copy(v.Id, "Vessel ID"), "y"));
            RowActions.Add(new OpsAction<Vessel>("copy-url", "Copy URL", v => Copy(v.RepoUrl, "URL"), "Y", v => !String.IsNullOrEmpty(v.RepoUrl)));
            OpsAction<Vessel> delete = new OpsAction<Vessel>("delete", "Delete", Delete, "del");
            delete.Danger = true;
            RowActions.Add(delete);

            Start("name", false);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The backlog route for a vessel (the dashboard's Manage Objectives).
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>Route.</returns>
        public static string ObjectivesRoute(Vessel vessel)
        {
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["tab"] = "backlog";
            q["vesselId"] = vessel.Id;
            if (!String.IsNullOrEmpty(vessel.FleetId)) q["fleetId"] = vessel.FleetId!;
            return "/dispatch" + RouteMatch.BuildQuery(q);
        }

        /// <summary>
        /// A vessel's commit history route.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <returns>Route.</returns>
        public static string HistoryRoute(string vesselId)
        {
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            return "/vessels/" + Uri.EscapeDataString(vesselId) + "/history";
        }

        /// <summary>
        /// The Dispatch route with a vessel pre-selected (the dashboard's Dispatch row action).
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>Route.</returns>
        public static string DispatchRoute(Vessel vessel)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            return OpsHandoff.Dispatch(OpsHandoff.FromVessel, vessel.Id, null, null, null, null, null);
        }

        /// <summary>
        /// Open the Manage Branches dialog; the list reloads when it closes, as on the dashboard.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>The dialog.</returns>
        public VesselBranchesDialog ManageBranches(Vessel vessel)
        {
            VesselBranchesDialog dialog = new VesselBranchesDialog(this, vessel.Id, vessel.Name);
            Context.Modals.Show(dialog, r => Refresh());
            return dialog;
        }

        /// <summary>
        /// Fleet name (or a short id) for a fleet id, or "-".
        /// </summary>
        /// <param name="id">Fleet id.</param>
        /// <returns>Name.</returns>
        public string FleetName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            Fleet? f = Fleets.FirstOrDefault(x => x.Id == id);
            return f?.Name ?? BuildText.Short(id);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool ServerPaging
        {
            get { return false; }
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Vessel>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(1, 9999);
            q.With("userId", UserScope?.Value);
            Task<EnumerationResult<Vessel>?> vessels = Context.Client.ListVesselsAsync(q, token);
            Task<EnumerationResult<Fleet>?> fleets = Context.Client.ListFleetsAsync(new ArmadaPageQuery(1, 9999), token);
            Task<EnumerationResult<Pipeline>?> pipelines = Context.Client.ListPipelinesAsync(new ArmadaPageQuery(1, 9999), token);
            await Task.WhenAll(vessels, fleets, pipelines).ConfigureAwait(false);
            _LoadedFleets = fleets.Result?.Objects ?? new List<Fleet>();
            _LoadedPipelines = pipelines.Result?.Objects ?? new List<Pipeline>();
            List<Vessel> rows = vessels.Result?.Objects ?? new List<Vessel>();
            return new GridPage<Vessel>(rows, rows.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<Vessel> FilterLocal(IEnumerable<Vessel> rows)
        {
            string name = NameFilter.Value.Trim();
            string repo = RepoFilter.Value.Trim();
            string fleet = FleetFilter.Value ?? "";
            string mode = LandingFilter.Value ?? "";
            return rows.Where(v =>
                (fleet.Length == 0 || v.FleetId == fleet) &&
                (mode.Length == 0 || (v.LandingMode?.ToString() ?? "") == mode) &&
                (name.Length == 0 || v.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (repo.Length == 0 || (v.RepoUrl ?? "").IndexOf(repo, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<Vessel> rows)
        {
            Fleets = _LoadedFleets;
            Pipelines = _LoadedPipelines;
            string? current = FleetFilter.Value;
            List<SelectOption<string>> fleetOptions = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Fleets")) };
            fleetOptions.AddRange(Fleets.Select(f => new SelectOption<string>(f.Id, f.Name)));
            FleetFilter.Options = fleetOptions;
            FleetFilter.SetValue(current ?? "");
            Grid.EmptyText = rows.Count == 0
                ? Tr("No vessels configured.") + " " + Tr("Add a single repository with + Vessel, or import many existing local repositories at once.") + (IsTenantAdmin ? "  (I " + Tr("Import repositories") + ")" : "")
                : "No vessels match the current filters.";
            LoadStatuses(rows.ToList());
        }

        /// <inheritdoc />
        protected override void OpenRow(Vessel row)
        {
            Context.Navigate("/vessels/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(Vessel row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override int AboveHeight(int width)
        {
            return Grid.Marked.Count > 0 ? 1 : 0;
        }

        /// <inheritdoc />
        protected override void RenderAbove(ISurface surface, int width)
        {
            string text = Context.Loc.T("{count, plural, one {# vessel selected} other {# vessels selected}}", LocalizationArgs.Of("count", Grid.Marked.Count));
            SurfaceText.Draw(surface, 0, 0, text + "   " + (IsTenantAdmin ? "A " + Tr("Run action...") + "  " : "") + "D " + Tr("Delete Selected") + "  " + Tr("Clear selection"), Theme.Info, width);
        }

        #endregion

        #region Private-Methods

        private void LoadStatuses(List<Vessel> rows)
        {
            int generation = Interlocked.Increment(ref _StatusGeneration);
            ArmadaClient client = Context.Client;
            _ = Task.Run(async () =>
            {
                List<Task> tasks = new List<Task>();
                foreach (Vessel v in rows)
                {
                    string id = v.Id;
                    tasks.Add(Task.Run(async () =>
                    {
                        try { GitStatus[id] = await client.GetVesselGitStatusAsync(id).ConfigureAwait(false); }
                        catch (Exception) { GitStatus[id] = null; }
                        try { BranchCounts[id] = (await client.GetVesselBranchesAsync(id).ConfigureAwait(false))?.BranchCount; }
                        catch (Exception) { BranchCounts[id] = null; }
                    }));
                }

                try { await Task.WhenAll(tasks).ConfigureAwait(false); }
                catch (Exception) { }
                if (generation == _StatusGeneration) Post(() => { });
            });
        }

        private string SyncText(Vessel v)
        {
            if (!GitStatus.TryGetValue(v.Id, out VesselGitStatusResult? gs) || gs == null || (gs.CommitsAhead == null && gs.CommitsBehind == null)) return "-";
            int ahead = gs.CommitsAhead ?? 0;
            int behind = gs.CommitsBehind ?? 0;
            if (ahead == 0 && behind == 0) return Tr("in sync");
            List<string> parts = new List<string>();
            if (ahead > 0) parts.Add(ahead + " " + Tr("ahead"));
            if (behind > 0) parts.Add(behind + " " + Tr("behind"));
            return String.Join(" ", parts);
        }

        private CellStyle? SyncStyle(Vessel v, Armada.Tui.Theming.ArmadaTheme theme)
        {
            if (!GitStatus.TryGetValue(v.Id, out VesselGitStatusResult? gs) || gs == null) return theme.Muted;
            int ahead = gs.CommitsAhead ?? 0;
            int behind = gs.CommitsBehind ?? 0;
            if (gs.CommitsAhead == null && gs.CommitsBehind == null) return theme.Muted;
            if (ahead == 0 && behind == 0) return theme.Success;
            return behind > 0 ? theme.Warning : theme.Info;
        }

        private void RunAction()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            FleetActionRunFlow.Start(this, ids, null, null, result =>
            {
                Grid.ClearMarks();
                Context.Navigate("/fleet-actions/runs/" + Uri.EscapeDataString(result.RunId));
            });
        }

        private void Duplicate(Vessel vessel)
        {
            Call((c, t) => c.CreateVesselAsync(VesselForm.DuplicatePayload(vessel), t), created =>
            {
                if (created == null) return;
                Toast(NotificationSeverityEnum.Success, Tr("Vessel \"{{name}}\" duplicated.", LocalizationArgs.Of("name", created.Name)));
                Context.Navigate("/vessels/" + Uri.EscapeDataString(created.Id) + "?edit=1");
            }, null, ex => ShowMessage(ex is ArmadaApiException api ? api.Message : Tr("Duplicate failed.")));
        }

        private void Delete(Vessel vessel)
        {
            Confirm("Delete Vessel", Tr("Delete vessel \"{{name}}\"? This cannot be undone.", LocalizationArgs.Of("name", vessel.Name)), () =>
            {
                Run((c, t) => c.DeleteVesselAsync(vessel.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Vessel \"{{name}}\" deleted.", LocalizationArgs.Of("name", vessel.Name)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Delete failed.")));
            }, "Delete");
        }

        private void BulkDelete()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Confirm("Delete Selected Vessels", Tr("Delete {{count}} selected vessel(s)? This cannot be undone.", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                Call(async (c, t) =>
                {
                    int failed = 0;
                    foreach (string id in ids)
                    {
                        try { await c.DeleteVesselAsync(id, t).ConfigureAwait(false); }
                        catch (ArmadaApiException) { failed++; }
                    }

                    return failed;
                }, failed =>
                {
                    int success = ids.Count - failed;
                    if (success > 0)
                    {
                        Toast(failed > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success, failed > 0
                            ? Tr("Deleted {{success}} vessels. {{failed}} failed.", LocalizationArgs.Of("success", success, "failed", failed))
                            : Tr("Deleted {{success}} vessels.", LocalizationArgs.Of("success", success)));
                    }

                    if (failed > 0) ShowMessage(Tr("Deleted {{success}} vessels, {{failed}} failed.", LocalizationArgs.Of("success", success, "failed", failed)));
                    Refresh();
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
