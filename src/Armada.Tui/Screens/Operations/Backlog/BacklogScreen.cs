namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Backlog (W3.6; the Backlog tab of <c>/dispatch</c> and the <c>/objectives</c> route), the dashboard's Objectives
    /// page: KPI cards (backlog items, ready for planning, ready for dispatch, blocked), group pills with counts
    /// (<c>1</c>-<c>5</c>), the full filter set (search, kind, priority, backlog state, effort, status, fleet, vessel,
    /// owner, target version, title column, user scope) with Clear Filters, the sort select (rank, priority, updated,
    /// due), the rank/item/shape/state/scope/due-updated grid, rank moves (<c>Alt+Up</c>/<c>Alt+Down</c> through
    /// <c>reorderBacklog</c>), the row menu (Open, Duplicate, View JSON, Move Up, Move Down, Delete), Import GitHub, and
    /// New backlog item. Writes are tenant-admin only, as on the dashboard. Refreshes on <c>objective.changed</c>.
    /// Not thread-safe.
    /// </summary>
    public class BacklogScreen : OpsListScreen<Objective>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Capture future work, refine it, and carry the same record through planning, dispatch, release, deployment, and incident follow-through."; }
        }

        /// <summary>
        /// Active group pill key.
        /// </summary>
        public string Group { get; private set; } = "all";

        /// <summary>
        /// Search filter.
        /// </summary>
        public TextInput Search { get; }

        /// <summary>
        /// Kind filter.
        /// </summary>
        public SelectField<string> KindFilter { get; }

        /// <summary>
        /// Priority filter.
        /// </summary>
        public SelectField<string> PriorityFilter { get; }

        /// <summary>
        /// Backlog state filter.
        /// </summary>
        public SelectField<string> StateFilter { get; }

        /// <summary>
        /// Effort filter.
        /// </summary>
        public SelectField<string> EffortFilter { get; }

        /// <summary>
        /// Lifecycle status filter.
        /// </summary>
        public SelectField<string> StatusFilter { get; }

        /// <summary>
        /// Fleet filter.
        /// </summary>
        public SelectField<string> FleetFilter { get; }

        /// <summary>
        /// Vessel filter.
        /// </summary>
        public SelectField<string> VesselFilter { get; }

        /// <summary>
        /// Owner filter.
        /// </summary>
        public TextInput OwnerFilter { get; }

        /// <summary>
        /// Target version filter.
        /// </summary>
        public TextInput VersionFilter { get; }

        /// <summary>
        /// Title column filter.
        /// </summary>
        public TextInput TitleFilter { get; }

        /// <summary>
        /// Sort select.
        /// </summary>
        public SelectField<string> SortSelect { get; }

        /// <summary>
        /// User scope (admins only).
        /// </summary>
        public SelectField<string>? UserScope { get; }

        /// <summary>
        /// True for tenant and global admins (create, import, duplicate, move, delete).
        /// </summary>
        public bool CanManage
        {
            get { return IsTenantAdmin; }
        }

        #endregion

        #region Private-Members

        private int _Shown = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public BacklogScreen(RouteMatch route, TuiContext context)
            : base(route, context, o => o.Id, "BacklogScreen", "Backlog")
        {
            Grid.MultiSelect = false;
            Reference.Ensure("vessels", "fleets");

            Search = TextFilter("Search", 24, "Search by title, description, owner, category, captain, tags, or ID...");
            KindFilter = SelectFilter("Kind", Options("All kinds", BacklogLogic.Kinds), 12, false);
            PriorityFilter = SelectFilter("Priority", Options("All priorities", BacklogLogic.Priorities), 14, false);
            StateFilter = SelectFilter("Backlog state", Options("All backlog states", BacklogLogic.BacklogStates), 18, false);
            EffortFilter = SelectFilter("Effort", Options("All effort sizes", BacklogLogic.Efforts), 16, false);
            StatusFilter = SelectFilter("Status", Options("All lifecycle statuses", BacklogLogic.Statuses), 22, false);
            FleetFilter = SelectFilter("Fleet", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All fleets")) }, 16, false);
            VesselFilter = SelectFilter("Vessel", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All vessels")) }, 16, false);
            OwnerFilter = TextFilter("Owner", 14, "Owner filter");
            VersionFilter = TextFilter("Target version", 12, "Target version filter");
            TitleFilter = TextFilter("Title", 14, "Filter...");
            SortSelect = SelectFilter("Sort", new List<SelectOption<string>>
            {
                new SelectOption<string>("rank", Tr("Sort by rank")),
                new SelectOption<string>("priority", Tr("Sort by priority")),
                new SelectOption<string>("updated", Tr("Sort by last updated")),
                new SelectOption<string>("due", Tr("Sort by due date")),
            }, 20, false);
            UserScope = UserScopeFilter();

            string? qFleet = OpsHandoff.Get(route, "fleetId");
            string? qVessel = OpsHandoff.Get(route, "vesselId");
            Reference.Changed += (s, name) =>
            {
                if (name == "fleets")
                {
                    string current = !String.IsNullOrEmpty(FleetFilter.Value) ? FleetFilter.Value! : qFleet ?? "";
                    qFleet = null;
                    FleetFilter.Options = Reference.FleetOptions("All fleets");
                    FleetFilter.SetValue(current);
                }
                else if (name == "vessels")
                {
                    string current = !String.IsNullOrEmpty(VesselFilter.Value) ? VesselFilter.Value! : qVessel ?? "";
                    qVessel = null;
                    VesselFilter.Options = Reference.VesselOptions("All vessels");
                    VesselFilter.SetValue(current);
                }

                if (name == "fleets" || name == "vessels") ApplyFilters();
            };

            Column("rank", "Rank", o => o.Rank.ToString(CultureInfo.InvariantCulture), 0, 6).Align = TUIKit.Widgets.CellAlignment.Right;
            Column("item", "Backlog Item", ItemText, 5);
            Column("shape", "Shape", o => o.Kind + " " + o.Priority + " " + o.Effort, 0, 18);
            Column("state", "State", StateText, 3, null, null, (o, t) => StatusBadge.Style(o.Status.ToString(), t));
            Column("scope", "Scope", ScopeText, 3);
            Column("due", "Due / Updated", DueText, 3);
            GridColumn<Objective> idCol = Column("id", "ID", o => o.Id, 0, 26);
            idCol.DefaultVisible = false;

            OpsScreenAction clear = new OpsScreenAction("clear-filters", "Clear Filters", ClearFilters, "X", HasActiveFilters);
            ScreenActions.Add(clear);
            ScreenActions.Add(new OpsScreenAction("import-github", "Import GitHub", OpenImport, "i", () => CanManage));
            ScreenActions.Add(new OpsScreenAction("new", "+ Backlog Item", OpenNew, "n", () => CanManage));
            for (int i = 0; i < BacklogLogic.GroupKeys.Length; i++)
            {
                string key = BacklogLogic.GroupKeys[i];
                OpsScreenAction pill = new OpsScreenAction("group-" + key, BacklogLogic.GroupLabels[i], () => SelectGroup(key), (i + 1).ToString(CultureInfo.InvariantCulture));
                pill.Toolbar = false;
                ScreenActions.Add(pill);
            }

            RowActions.Add(new OpsAction<Objective>("open", "Open", o => OpenRow(o), "o"));
            RowActions.Add(new OpsAction<Objective>("duplicate", "Duplicate", Duplicate, "u", o => CanManage));
            RowActions.Add(new OpsAction<Objective>("json", "View JSON", o => ShowJson(o.Title, o), "j"));
            RowActions.Add(new OpsAction<Objective>("move-up", "Move Up", o => MoveRank(o, -1), "alt+up", o => CanManage));
            RowActions.Add(new OpsAction<Objective>("move-down", "Move Down", o => MoveRank(o, 1), "alt+down", o => CanManage));
            RowActions.Add(new OpsAction<Objective>("copy-id", "Copy ID", o => Copy(o.Id, "Backlog item ID"), "y"));
            OpsAction<Objective> delete = new OpsAction<Objective>("delete", "Delete", Delete, "del", o => CanManage);
            delete.Danger = true;
            RowActions.Add(delete);

            SubscribeCoalesced("objective.changed", Refresh);
            Start(null, false, 25);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Group counts over the loaded items (all plus each group).
        /// </summary>
        /// <returns>Counts by group key.</returns>
        public Dictionary<string, int> GroupCounts()
        {
            Dictionary<string, int> counts = BacklogLogic.GroupKeys.ToDictionary(k => k, k => 0, StringComparer.Ordinal);
            counts["all"] = LoadedRows.Count;
            foreach (Objective o in LoadedRows)
            {
                string g = BacklogLogic.Group(o);
                if (g != "all") counts[g] = counts[g] + 1;
            }

            return counts;
        }

        /// <summary>
        /// Select a group pill.
        /// </summary>
        /// <param name="key">Group key.</param>
        public void SelectGroup(string key)
        {
            Group = BacklogLogic.GroupKeys.Contains(key) ? key : "all";
            ApplyFilters();
        }

        /// <summary>
        /// True when any filter, group, or sort differs from the defaults.
        /// </summary>
        /// <returns>True when filtered.</returns>
        public bool HasActiveFilters()
        {
            return Search.Value.Trim().Length > 0
                || !String.IsNullOrEmpty(KindFilter.Value) || !String.IsNullOrEmpty(PriorityFilter.Value)
                || !String.IsNullOrEmpty(StateFilter.Value) || !String.IsNullOrEmpty(EffortFilter.Value)
                || !String.IsNullOrEmpty(StatusFilter.Value) || !String.IsNullOrEmpty(FleetFilter.Value)
                || !String.IsNullOrEmpty(VesselFilter.Value) || OwnerFilter.Value.Trim().Length > 0
                || VersionFilter.Value.Trim().Length > 0 || Group != "all" || (SortSelect.Value ?? "rank") != "rank";
        }

        /// <summary>
        /// Reset every filter, the group, and the sort.
        /// </summary>
        public void ClearFilters()
        {
            Search.Value = "";
            OwnerFilter.Value = "";
            VersionFilter.Value = "";
            TitleFilter.Value = "";
            foreach (SelectField<string> s in new SelectField<string>[] { KindFilter, PriorityFilter, StateFilter, EffortFilter, StatusFilter, FleetFilter, VesselFilter }) s.SetValue("");
            SortSelect.SetValue("rank");
            Group = "all";
            ApplyFilters();
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override bool ServerPaging
        {
            get { return false; }
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Objective>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ObjectiveQuery q = new ObjectiveQuery();
            q.PageNumber = 1;
            q.PageSize = 9999;
            string? user = UserScope?.Value;
            if (!String.IsNullOrEmpty(user)) q.UserId = user;
            EnumerationResult<Objective>? result = await Context.Client.ListBacklogAsync(q, token).ConfigureAwait(false);
            List<Objective> rows = result?.Objects ?? new List<Objective>();
            return new GridPage<Objective>(rows, rows.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<Objective> FilterLocal(IEnumerable<Objective> rows)
        {
            string search = Search.Value.Trim().ToLowerInvariant();
            string kind = KindFilter.Value ?? "";
            string priority = PriorityFilter.Value ?? "";
            string state = StateFilter.Value ?? "";
            string effort = EffortFilter.Value ?? "";
            string status = StatusFilter.Value ?? "";
            string fleet = FleetFilter.Value ?? "";
            string vessel = VesselFilter.Value ?? "";
            string owner = OwnerFilter.Value.Trim().ToLowerInvariant();
            string version = VersionFilter.Value.Trim().ToLowerInvariant();
            string title = TitleFilter.Value.Trim().ToLowerInvariant();
            string group = Group;
            List<Objective> filtered = rows.Where(o =>
            {
                if (group != "all" && BacklogLogic.Group(o) != group) return false;
                if (status.Length > 0 && o.Status.ToString() != status) return false;
                if (kind.Length > 0 && o.Kind.ToString() != kind) return false;
                if (priority.Length > 0 && o.Priority.ToString() != priority) return false;
                if (state.Length > 0 && o.BacklogState.ToString() != state) return false;
                if (effort.Length > 0 && o.Effort.ToString() != effort) return false;
                if (fleet.Length > 0 && !(o.FleetIds ?? new List<string>()).Contains(fleet)) return false;
                if (vessel.Length > 0 && !(o.VesselIds ?? new List<string>()).Contains(vessel)) return false;
                if (owner.Length > 0 && !(o.Owner ?? "").ToLowerInvariant().Contains(owner)) return false;
                if (version.Length > 0 && !(o.TargetVersion ?? "").ToLowerInvariant().Contains(version)) return false;
                if (title.Length > 0 && !(o.Title ?? "").ToLowerInvariant().Contains(title)) return false;
                if (search.Length == 0) return true;
                return Has(o.Title, search) || Has(o.Description, search) || Has(o.Owner, search) || Has(o.Category, search)
                    || Has(o.TargetVersion, search) || Has(o.RefinementSummary, search)
                    || (o.Tags ?? new List<string>()).Any(t => Has(t, search))
                    || (o.AcceptanceCriteria ?? new List<string>()).Any(t => Has(t, search))
                    || Has(o.Id, search);
            }).ToList();
            List<Objective> ordered = BacklogLogic.Order(filtered, SortSelect.Value ?? "rank");
            _Shown = ordered.Count;
            return ordered;
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<Objective> rows)
        {
            Grid.EmptyText = rows.Count == 0 ? "No backlog items yet." : "No backlog items match the current filters.";
        }

        /// <inheritdoc />
        protected override void OpenRow(Objective row)
        {
            Context.Navigate("/backlog/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(Objective row)
        {
            return row.Title;
        }

        /// <inheritdoc />
        protected override int AboveHeight(int width)
        {
            return 3;
        }

        /// <inheritdoc />
        protected override void RenderAbove(ISurface surface, int width)
        {
            IReadOnlyList<Objective> all = LoadedRows;
            int planning = all.Count(o => o.BacklogState == ObjectiveBacklogStateEnum.ReadyForPlanning);
            int dispatch = all.Count(o => o.BacklogState == ObjectiveBacklogStateEnum.ReadyForDispatch);
            int blocked = all.Count(o => o.Status == ObjectiveStatusEnum.Blocked || (o.BlockedByObjectiveIds?.Count ?? 0) > 0);
            string kpi = Tr("Backlog Items") + ": " + all.Count + "   " + Tr("Ready For Planning") + ": " + planning + "   "
                + Tr("Ready For Dispatch") + ": " + dispatch + "   " + Tr("Blocked") + ": " + blocked;
            SurfaceText.Draw(surface, 0, 0, kpi, Theme.Text, width);
            Dictionary<string, int> counts = GroupCounts();
            int x = 0;
            for (int i = 0; i < BacklogLogic.GroupKeys.Length; i++)
            {
                string key = BacklogLogic.GroupKeys[i];
                bool active = key == Group;
                string pill = (i + 1) + " " + Tr(BacklogLogic.GroupLabels[i]) + " " + counts[key];
                string text = active ? "[" + pill + "]" : " " + pill + " ";
                x += SurfaceText.Draw(surface, x, 1, text, active ? Theme.TabActive : Theme.TabInactive, width - x) + 1;
                if (x >= width) break;
            }

            string meta = Tr("Showing {{shown}} of {{total}} backlog items.", LocalizationArgs.Of("shown", _Shown, "total", all.Count));
            if (HasActiveFilters()) meta += "   X " + Tr("Clear Filters");
            SurfaceText.Draw(surface, 0, 2, meta, Theme.Muted, width);
        }

        #endregion

        #region Private-Methods

        private static bool Has(string? value, string needle)
        {
            return !String.IsNullOrEmpty(value) && value!.ToLowerInvariant().Contains(needle);
        }

        private List<SelectOption<string>> Options(string allLabel, string[] values)
        {
            List<SelectOption<string>> list = new List<SelectOption<string>> { new SelectOption<string>("", Tr(allLabel)) };
            list.AddRange(values.Select(v => new SelectOption<string>(v, v)));
            return list;
        }

        private string ItemText(Objective o)
        {
            string meta = (String.IsNullOrEmpty(o.Owner) ? Tr("No owner") : o.Owner!)
                + (String.IsNullOrEmpty(o.Category) ? "" : " - " + o.Category)
                + (String.IsNullOrEmpty(o.TargetVersion) ? "" : " - " + o.TargetVersion);
            return o.Title + "  (" + meta + ")";
        }

        private string StateText(Objective o)
        {
            string text = StatusBadge.Label(o.Status.ToString()) + " " + o.BacklogState;
            int blocked = o.BlockedByObjectiveIds?.Count ?? 0;
            if (blocked > 0) text += "  " + Tr("Blocked by") + " " + blocked;
            return text;
        }

        private string ScopeText(Objective o)
        {
            List<string> vessels = o.VesselIds ?? new List<string>();
            List<string> fleets = o.FleetIds ?? new List<string>();
            string v = vessels.Count > 0 ? Names(vessels, Reference.VesselName) : Tr("No vessel linked");
            string f = fleets.Count > 0 ? Names(fleets, Reference.FleetName) : Tr("No fleet linked");
            return v + " / " + f;
        }

        private static string Names(List<string> ids, Func<string?, string> name)
        {
            return String.Join(", ", ids.Take(2).Select(i => name(i))) + (ids.Count > 2 ? " +" + (ids.Count - 2) : "");
        }

        private string DueText(Objective o)
        {
            DateTime now = Context.Clock.UtcNow;
            string due = o.DueUtc.HasValue ? Tr("Due {{date}}", LocalizationArgs.Of("date", Context.Loc.FormatRelative(o.DueUtc.Value, now))) : Tr("No due date");
            return due + " / " + Tr("Updated {{time}}", LocalizationArgs.Of("time", Context.Loc.FormatRelative(o.LastUpdateUtc, now)));
        }

        private void OpenNew()
        {
            string? vessel = VesselFilter.Value;
            Context.Navigate(String.IsNullOrEmpty(vessel) ? "/backlog/new" : "/backlog/new?vesselId=" + Uri.EscapeDataString(vessel!));
        }

        private void MoveRank(Objective objective, int direction)
        {
            List<Objective> ranked = LoadedRows.OrderBy(o => o.Rank).ToList();
            int index = ranked.FindIndex(o => o.Id == objective.Id);
            int neighbor = index + direction;
            if (index < 0 || neighbor < 0 || neighbor >= ranked.Count) return;
            Objective current = ranked[index];
            Objective other = ranked[neighbor];
            ObjectiveReorderRequest req = new ObjectiveReorderRequest();
            ObjectiveReorderItem a = new ObjectiveReorderItem();
            a.ObjectiveId = current.Id;
            a.Rank = other.Rank;
            ObjectiveReorderItem b = new ObjectiveReorderItem();
            b.ObjectiveId = other.Id;
            b.Rank = current.Rank;
            req.Items = new List<ObjectiveReorderItem> { a, b };
            Call((c, t) => c.ReorderBacklogAsync(req, t), updated =>
            {
                Toast(NotificationSeverityEnum.Success, Tr("Backlog ranking updated."));
                Refresh();
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to reorder backlog.") : ex.Message));
        }

        private void Delete(Objective objective)
        {
            Confirm("Delete Backlog Item", Tr("Delete \"{{title}}\"? This removes the backlog item and its objective snapshot history, but leaves linked missions, releases, deployments, and incidents intact.", LocalizationArgs.Of("title", objective.Title)), () =>
            {
                Run((c, t) => c.DeleteBacklogItemAsync(objective.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Backlog item \"{{title}}\" deleted.", LocalizationArgs.Of("title", objective.Title)));
                    Refresh();
                }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Delete failed.") : ex.Message));
            }, "Delete");
        }

        private void Duplicate(Objective objective)
        {
            ObjectiveUpsertRequest payload = BacklogLogic.DuplicatePayload(objective);
            Call((c, t) => c.CreateBacklogItemAsync(payload, t), created =>
            {
                if (created == null) return;
                Toast(NotificationSeverityEnum.Success, Tr("Backlog item \"{{title}}\" duplicated.", LocalizationArgs.Of("title", created.Title)));
                Context.Navigate("/backlog/" + Uri.EscapeDataString(created.Id));
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Duplicate failed.") : ex.Message));
        }

        private void OpenImport()
        {
            Reference.Ensure("vessels");
            OpsFormDialog dialog = NewForm("Import GitHub Backlog Item", "Import");
            dialog.Intro = "Create a backlog item from a GitHub issue or pull request using the selected vessel repository and configured GitHub token.";
            SelectField<string> vessel = NewSelect("Vessel", Reference.VesselOptions(), "Select a vessel");
            SelectField<string> source = NewSelect("Source Type", new List<SelectOption<string>>
            {
                new SelectOption<string>("Issue", Tr("Issue")),
                new SelectOption<string>("PullRequest", Tr("Pull Request")),
            });
            source.SetValue("Issue");
            InputField number = new InputField();
            number.Placeholder = "123";
            dialog.AddField("Vessel", vessel);
            dialog.AddField("Source Type", source);
            dialog.AddField("Number", number);
            EventHandler<string> arrived = (s, name) =>
            {
                if (name == "vessels") vessel.Options = Reference.VesselOptions();
            };
            Reference.Changed += arrived;
            dialog.Validate = () =>
            {
                bool ok = Int32.TryParse(number.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0;
                return String.IsNullOrEmpty(vessel.Value) || !ok ? Tr("Select a vessel and enter a valid GitHub issue or pull-request number.") : null;
            };
            dialog.Submit = d =>
            {
                GitHubObjectiveImportRequest req = new GitHubObjectiveImportRequest();
                req.VesselId = vessel.Value;
                req.SourceType = source.Value == "PullRequest" ? GitHubObjectiveSourceTypeEnum.PullRequest : GitHubObjectiveSourceTypeEnum.Issue;
                req.Number = Int32.Parse(number.Value.Trim(), CultureInfo.InvariantCulture);
                string sourceType = source.Value ?? "Issue";
                Call((c, t) => c.ImportObjectiveFromGitHubAsync(req, t), imported =>
                {
                    d.Complete();
                    if (imported == null) return;
                    Toast(NotificationSeverityEnum.Success, Tr("Imported GitHub {{sourceType}} #{{number}} into backlog item \"{{title}}\".", LocalizationArgs.Of("sourceType", sourceType, "number", req.Number, "title", imported.Title)));
                    Refresh();
                    Context.Navigate("/backlog/" + Uri.EscapeDataString(imported.Id));
                }, null, ex => d.Fail(String.IsNullOrEmpty(ex.Message) ? Tr("GitHub import failed.") : ex.Message));
                return false;
            };
            Context.Modals.Show(dialog, r => Reference.Changed -= arrived);
        }

        #endregion
    }
}
