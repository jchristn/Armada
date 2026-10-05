namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
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
    /// Missions (W3.8, the Missions tab of <c>/missions</c>), the dashboard's Missions page: server paging with the
    /// status and user-scope filters, the title/status/branch column filters, sortable title, status, and priority,
    /// multi-select with Delete Selected (purge), the Create Mission dialog, and the row menu (View Detail, Edit,
    /// Restart, Retry Landing, View Diff, View Log, Transition Status, View JSON, Cancel, Purge) plus the cell links
    /// (vessel, captain, voyage) and copy actions. Refreshes on <c>mission.changed</c> and auto-refresh. Not
    /// thread-safe.
    /// </summary>
    public class MissionsScreen : OpsListScreen<MissionSummary>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Individual work units assigned to captains"; }
        }

        /// <summary>
        /// Mission actions.
        /// </summary>
        public MissionOps Ops { get; }

        /// <summary>
        /// Status filter.
        /// </summary>
        public SelectField<string> StatusFilter { get; }

        /// <summary>
        /// Title column filter.
        /// </summary>
        public TextInput TitleFilter { get; }

        /// <summary>
        /// Status column filter.
        /// </summary>
        public TextInput StatusText { get; }

        /// <summary>
        /// Branch column filter.
        /// </summary>
        public TextInput BranchFilter { get; }

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
        public MissionsScreen(RouteMatch route, TuiContext context)
            : base(route, context, m => m.Id, "MissionsScreen", "Missions")
        {
            Ops = new MissionOps(this);
            Grid.EmptyText = "No missions found.";
            Reference.Ensure("vessels", "captains", "voyages");

            List<SelectOption<string>> statuses = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Statuses")) };
            statuses.AddRange(MissionOps.ListStatuses.Select(s => new SelectOption<string>(s, Tr(s))));
            StatusFilter = SelectFilter("Status", statuses, 16, true);
            UserScope = UserScopeFilter();
            TitleFilter = TextFilter("Title");
            StatusText = TextFilter("Status text", 12);
            BranchFilter = TextFilter("Branch");

            Column("title", "Title", m => m.Title, 4, null, m => m.Title);
            Column("id", "ID", m => m.Id, 0, 24);
            Column("status", "Status", m => StatusBadge.Label(m.Status.ToString()), 0, 16, m => m.Status.ToString(), (m, t) => StatusBadge.Style(m.Status.ToString(), t));
            Column("priority", "Priority", m => m.Priority.ToString(CultureInfo.InvariantCulture), 0, 8, m => m.Priority).Align = TUIKit.Widgets.CellAlignment.Right;
            Column("vessel", "Vessel", m => Reference.VesselName(m.VesselId), 2);
            Column("captain", "Captain", m => Reference.CaptainName(m.CaptainId), 2);
            Column("voyage", "Voyage", m => m.VoyageId ?? "-", 2);
            Column("branch", "Branch", m => m.BranchName ?? "-", 3);
            GridColumn<MissionSummary> created = Column("createdUtc", "Created", m => Context.Loc.FormatRelative(m.CreatedUtc, Context.Clock.UtcNow), 0, 14, m => m.CreatedUtc);
            created.DefaultVisible = false;

            OpsScreenAction bulk = new OpsScreenAction("delete-selected", "Delete Selected", BulkDelete, "D", () => Grid.Marked.Count > 0);
            bulk.DynamicLabel = () => Tr("Delete Selected") + " (" + Grid.Marked.Count + ")";
            bulk.Danger = true;
            ScreenActions.Add(bulk);
            ScreenActions.Add(new OpsScreenAction("new", "+ Mission", OpenCreate, "n"));

            RowActions.Add(new OpsAction<MissionSummary>("view", "View Detail", m => OpenRow(m), "o"));
            RowActions.Add(new OpsAction<MissionSummary>("edit", "Edit", m => OpenRow(m), "e"));
            RowActions.Add(new OpsAction<MissionSummary>("restart", "Restart", m => Ops.Restart(m.Id, m.Title, false, Refresh), "r"));
            RowActions.Add(new OpsAction<MissionSummary>("retry-landing", "Retry Landing", m => Ops.RetryLanding(m.Id, m.Title, false, Refresh), "L",
                m => m.Status == MissionStatusEnum.WorkProduced || m.Status == MissionStatusEnum.LandingFailed));
            RowActions.Add(new OpsAction<MissionSummary>("diff", "View Diff", m => Ops.ViewDiff(m.Id, Tr("Mission Diff")), "d"));
            RowActions.Add(new OpsAction<MissionSummary>("log", "View Log", m => Ops.ViewLog(m.Id, "Log: " + m.Title), "l"));
            RowActions.Add(new OpsAction<MissionSummary>("transition", "Transition Status", m => Ops.Transition(m.Id, m.Title, m.Status.ToString(), false, Refresh), "t"));
            RowActions.Add(new OpsAction<MissionSummary>("json", "View JSON", m => ShowJson("Mission: " + m.Title, m), "j"));
            RowActions.Add(new OpsAction<MissionSummary>("vessel", "Open Vessel", m => Context.Navigate("/vessels/" + Uri.EscapeDataString(m.VesselId!)), "v", m => !String.IsNullOrEmpty(m.VesselId)));
            RowActions.Add(new OpsAction<MissionSummary>("captain", "Open Captain", m => Context.Navigate("/captains/" + Uri.EscapeDataString(m.CaptainId!)), "C", m => !String.IsNullOrEmpty(m.CaptainId)));
            RowActions.Add(new OpsAction<MissionSummary>("voyage", "Open Voyage", m => Context.Navigate("/voyages/" + Uri.EscapeDataString(m.VoyageId!)), "V", m => !String.IsNullOrEmpty(m.VoyageId)));
            RowActions.Add(new OpsAction<MissionSummary>("copy-id", "Copy ID", m => Copy(m.Id, "Mission ID"), "y"));
            RowActions.Add(new OpsAction<MissionSummary>("copy-branch", "Copy branch", m => Copy(m.BranchName, "Branch"), "Y", m => !String.IsNullOrEmpty(m.BranchName)));
            OpsAction<MissionSummary> cancel = new OpsAction<MissionSummary>("cancel", "Cancel", m => Ops.Cancel(m.Id, m.Title, Refresh), "x");
            cancel.Danger = true;
            RowActions.Add(cancel);
            OpsAction<MissionSummary> purge = new OpsAction<MissionSummary>("purge", "Purge (permanent)", m => Ops.Purge(m.Id, m.Title, false, Refresh), "del");
            purge.Danger = true;
            RowActions.Add(purge);

            SubscribeCoalesced("mission.changed", Refresh);
            Start("createdUtc", true);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<MissionSummary>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("status", StatusFilter.Value);
            q.With("userId", UserScope?.Value);
            EnumerationResult<MissionSummary>? result = await Context.Client.ListMissionSummariesAsync(q, token).ConfigureAwait(false);
            List<MissionSummary> rows = result?.Objects ?? new List<MissionSummary>();
            return new GridPage<MissionSummary>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<MissionSummary> FilterLocal(IEnumerable<MissionSummary> rows)
        {
            string title = TitleFilter.Value.Trim();
            string status = StatusText.Value.Trim();
            string branch = BranchFilter.Value.Trim();
            return rows.Where(m =>
                (title.Length == 0 || (m.Title ?? "").IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (status.Length == 0 || m.Status.ToString().IndexOf(status, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (branch.Length == 0 || (m.BranchName ?? "").IndexOf(branch, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<MissionSummary> rows)
        {
            Grid.EmptyText = rows.Count == 0 ? "No missions found." : "No missions match the current filters.";
        }

        /// <inheritdoc />
        protected override void OpenRow(MissionSummary row)
        {
            Context.Navigate("/missions/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(MissionSummary row)
        {
            return row.Title;
        }

        #endregion

        #region Private-Methods

        private void OpenCreate()
        {
            Reference.Ensure("vessels");
            OpsFormDialog dialog = NewForm("Create Mission", "Create");
            InputField title = new InputField();
            title.Validator = v => String.IsNullOrWhiteSpace(v) ? "Title is required." : null;
            OpsTextArea description = new OpsTextArea();
            description.ExternalEditor = (text, done) => EditExternally(text, done);
            SelectField<string> vessel = new SelectField<string>();
            vessel.ModalHost = Context.Modals;
            vessel.PickerTitle = "Vessel";
            vessel.Placeholder = "Select a vessel...";
            vessel.Required = true;
            vessel.Options = Reference.VesselOptions();
            SelectField<string> mode = new SelectField<string>();
            mode.ModalHost = Context.Modals;
            mode.PickerTitle = "Mode";
            mode.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("Implementation", Tr("Implementation")),
                new SelectOption<string>("Audit", Tr("Audit (read-only)")),
                new SelectOption<string>("Research", Tr("Research (read-only)")),
            };
            mode.SetValue("Implementation");
            InputField priority = new InputField();
            priority.Value = "100";
            priority.Validator = v => Int32.TryParse(v, out int p) ? null : "Enter a number.";
            dialog.AddField("Title", title);
            dialog.AddField("Description", description, null, 4);
            dialog.AddField("Vessel", vessel);
            dialog.AddField("Mode", mode);
            dialog.AddField("Priority", priority);
            EventHandler<string> vesselsArrived = (s, name) =>
            {
                if (name == "vessels") vessel.Options = Reference.VesselOptions();
            };
            Reference.Changed += vesselsArrived;
            dialog.Submit = d =>
            {
                Mission mission = new Mission(title.Value.Trim(), description.Text);
                mission.VesselId = vessel.Value;
                mission.Priority = Int32.Parse(priority.Value, CultureInfo.InvariantCulture);
                mission.Mode = Enum.TryParse(mode.Value, out MissionModeEnum m) ? m : MissionModeEnum.Implementation;
                string created = mission.Title;
                Call((c, t) => c.CreateMissionAsync(mission, t), r =>
                {
                    d.Complete();
                    Toast(NotificationSeverityEnum.Success, Tr("Mission \"{{title}}\" created.", LocalizationArgs.Of("title", created)));
                    Refresh();
                }, null, ex => d.Fail(Tr("Create failed.")));
                return false;
            };
            Context.Modals.Show(dialog, r => Reference.Changed -= vesselsArrived);
        }

        private void BulkDelete()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Confirm("Delete Selected Missions", Tr("Delete {{count}} selected mission(s)? This cannot be undone.", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                Call(async (c, t) =>
                {
                    int failed = 0;
                    foreach (string id in ids)
                    {
                        try { await c.PurgeMissionAsync(id, t).ConfigureAwait(false); }
                        catch (Armada.Client.ArmadaApiException) { failed++; }
                    }

                    return failed;
                }, failed =>
                {
                    int success = ids.Count - failed;
                    if (success > 0)
                    {
                        Toast(failed > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success, failed > 0
                            ? Tr("Purged {{success}} missions. {{failed}} failed.", LocalizationArgs.Of("success", success, "failed", failed))
                            : Tr("Purged {{success}} missions.", LocalizationArgs.Of("success", success)));
                    }

                    if (failed > 0) ShowMessage(Tr("Deleted {{success}} missions, {{failed}} failed.", LocalizationArgs.Of("success", success, "failed", failed)));
                    Refresh();
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
