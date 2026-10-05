namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Merge Queue (W3.10, the Merge Queue tab of <c>/missions</c>), the dashboard's MergeQueue page: server paging
    /// with the user-scope filter, the branch, target, status, and vessel column filters, sortable branch, target,
    /// status, and priority, multi-select with Delete Selected, Process All (confirmed), + Enqueue, and the row menu
    /// (View Detail, Process, Cancel, Mission Diff, Mission Log, View JSON, Delete) plus the mission and vessel links
    /// and copy actions. Refreshes on <c>mission.changed</c> and auto-refresh. Not thread-safe.
    /// </summary>
    public class MergeQueueScreen : OpsListScreen<MergeEntry>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Completed missions awaiting merge. Review, test, approve, and manage the merge pipeline."; }
        }

        /// <summary>
        /// Mission actions (diff and log).
        /// </summary>
        public MissionOps Ops { get; }

        /// <summary>
        /// Branch filter.
        /// </summary>
        public TextInput BranchFilter { get; }

        /// <summary>
        /// Target branch filter.
        /// </summary>
        public TextInput TargetFilter { get; }

        /// <summary>
        /// Status filter.
        /// </summary>
        public TextInput StatusFilter { get; }

        /// <summary>
        /// Vessel filter (local).
        /// </summary>
        public SelectField<string> VesselFilter { get; }

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
        public MergeQueueScreen(RouteMatch route, TuiContext context)
            : base(route, context, e => e.Id, "MergeQueueScreen", "Merge Queue")
        {
            Ops = new MissionOps(this);
            Grid.EmptyText = "Merge queue is empty.";
            Reference.Ensure("vessels");
            UserScope = UserScopeFilter();
            BranchFilter = TextFilter("Branch", 16, "Filter...");
            TargetFilter = TextFilter("Target", 12, "Filter...");
            StatusFilter = TextFilter("Status", 12, "Filter...");
            VesselFilter = SelectFilter("Vessel", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Vessels")) }, 18, false);
            Reference.Changed += (s, name) =>
            {
                if (name != "vessels") return;
                string? current = VesselFilter.Value;
                VesselFilter.Options = Reference.VesselOptions("All Vessels");
                VesselFilter.SetValue(current ?? "");
            };

            Column("id", "ID", e => e.Id, 0, 24);
            Column("branchName", "Branch", e => e.BranchName, 3, null, e => e.BranchName);
            Column("targetBranch", "Target", e => e.TargetBranch, 2, null, e => e.TargetBranch);
            Column("status", "Status", e => StatusBadge.Label(e.Status.ToString()), 0, 12, e => e.Status.ToString(), (e, t) => StatusBadge.Style(e.Status.ToString(), t));
            Column("priority", "Priority", e => e.Priority.ToString(CultureInfo.InvariantCulture), 0, 8, e => e.Priority).Align = TUIKit.Widgets.CellAlignment.Right;
            Column("mission", "Mission", e => e.MissionId ?? "-", 2);
            Column("vessel", "Vessel", e => Reference.VesselName(e.VesselId), 2);
            GridColumn<MergeEntry> created = Column("createdUtc", "Created", e => Context.Loc.FormatRelative(e.CreatedUtc, Context.Clock.UtcNow), 0, 14, e => e.CreatedUtc);
            created.DefaultVisible = false;

            OpsScreenAction bulk = new OpsScreenAction("delete-selected", "Delete Selected", BulkDelete, "D", () => Grid.Marked.Count > 0);
            bulk.DynamicLabel = () => Tr("Delete Selected") + " (" + Grid.Marked.Count + ")";
            bulk.Danger = true;
            ScreenActions.Add(bulk);
            ScreenActions.Add(new OpsScreenAction("process-all", "Process All", ProcessAll, "P"));
            ScreenActions.Add(new OpsScreenAction("enqueue", "+ Enqueue", OpenEnqueue, "n"));

            RowActions.Add(new OpsAction<MergeEntry>("view", "View Detail", e => OpenRow(e), "o"));
            RowActions.Add(new OpsAction<MergeEntry>("process", "Process", Process, "p"));
            RowActions.Add(new OpsAction<MergeEntry>("cancel", "Cancel", Cancel, "x"));
            RowActions.Add(new OpsAction<MergeEntry>("mission-diff", "Mission Diff", e => Ops.ViewDiff(e.MissionId!, Tr("Diff") + ": " + Tr("Mission") + " " + Short(e.MissionId!) + "..."), "d", e => !String.IsNullOrEmpty(e.MissionId)));
            RowActions.Add(new OpsAction<MergeEntry>("mission-log", "Mission Log", e => Ops.ViewLog(e.MissionId!, Tr("Log") + ": " + Tr("Mission") + " " + Short(e.MissionId!) + "..."), "l", e => !String.IsNullOrEmpty(e.MissionId)));
            RowActions.Add(new OpsAction<MergeEntry>("json", "View JSON", e => ShowJson(Tr("Merge Entry") + ": " + e.Id, e), "j"));
            RowActions.Add(new OpsAction<MergeEntry>("mission", "Open Mission", e => Context.Navigate("/missions/" + Uri.EscapeDataString(e.MissionId!)), "m", e => !String.IsNullOrEmpty(e.MissionId)));
            RowActions.Add(new OpsAction<MergeEntry>("vessel", "Open Vessel", e => Context.Navigate("/vessels/" + Uri.EscapeDataString(e.VesselId!)), "v", e => !String.IsNullOrEmpty(e.VesselId)));
            RowActions.Add(new OpsAction<MergeEntry>("copy-id", "Copy ID", e => Copy(e.Id, "Merge entry ID"), "y"));
            RowActions.Add(new OpsAction<MergeEntry>("copy-branch", "Copy branch", e => Copy(e.BranchName, "Branch"), "Y", e => !String.IsNullOrEmpty(e.BranchName)));
            RowActions.Add(new OpsAction<MergeEntry>("copy-target", "Copy target branch", e => Copy(e.TargetBranch, "Branch"), null, e => !String.IsNullOrEmpty(e.TargetBranch)));
            OpsAction<MergeEntry> delete = new OpsAction<MergeEntry>("delete", "Delete", Delete, "del");
            delete.Danger = true;
            RowActions.Add(delete);

            SubscribeCoalesced("mission.changed", Refresh);
            Start("createdUtc", true);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<MergeEntry>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("userId", UserScope?.Value);
            EnumerationResult<MergeEntry>? result = await Context.Client.ListMergeQueueAsync(q, token).ConfigureAwait(false);
            List<MergeEntry> rows = result?.Objects ?? new List<MergeEntry>();
            return new GridPage<MergeEntry>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<MergeEntry> FilterLocal(IEnumerable<MergeEntry> rows)
        {
            string branch = BranchFilter.Value.Trim();
            string target = TargetFilter.Value.Trim();
            string status = StatusFilter.Value.Trim();
            string vessel = VesselFilter.Value ?? "";
            return rows.Where(e =>
                (branch.Length == 0 || (e.BranchName ?? "").IndexOf(branch, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (target.Length == 0 || (e.TargetBranch ?? "").IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (status.Length == 0 || e.Status.ToString().IndexOf(status, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (vessel.Length == 0 || e.VesselId == vessel));
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<MergeEntry> rows)
        {
            Grid.EmptyText = rows.Count == 0 ? "Merge queue is empty." : "No entries match the current filters.";
        }

        /// <inheritdoc />
        protected override void OpenRow(MergeEntry row)
        {
            Context.Navigate("/merge-queue/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(MergeEntry row)
        {
            return row.BranchName;
        }

        #endregion

        #region Private-Methods

        private static string Short(string id)
        {
            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        private void OpenEnqueue()
        {
            OpsFormDialog dialog = NewForm("Enqueue Merge", "Enqueue");
            InputField branch = new InputField();
            branch.Validator = v => String.IsNullOrWhiteSpace(v) ? "Branch name is required." : null;
            InputField target = new InputField();
            target.Value = "main";
            target.Validator = v => String.IsNullOrWhiteSpace(v) ? "Target branch is required." : null;
            InputField mission = new InputField();
            mission.Placeholder = "msn_...";
            SelectField<string> vessel = NewSelect("Vessel", Reference.VesselOptions("(none)"));
            vessel.SetValue("");
            InputField test = new InputField();
            test.Placeholder = "npm test";
            InputField priority = new InputField();
            priority.Value = "0";
            priority.Validator = v => Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? null : "Enter a number.";
            dialog.AddField("Branch Name", branch);
            dialog.AddField("Target Branch", target);
            dialog.AddField("Mission ID (optional)", mission);
            dialog.AddField("Vessel", vessel);
            dialog.AddField("Test Command (optional)", test);
            dialog.AddField("Priority", priority);
            dialog.Submit = d =>
            {
                MergeEntry entry = new MergeEntry(branch.Value.Trim(), String.IsNullOrWhiteSpace(target.Value) ? "main" : target.Value.Trim());
                entry.MissionId = String.IsNullOrWhiteSpace(mission.Value) ? null : mission.Value.Trim();
                entry.VesselId = String.IsNullOrEmpty(vessel.Value) ? null : vessel.Value;
                entry.TestCommand = String.IsNullOrWhiteSpace(test.Value) ? null : test.Value.Trim();
                entry.Priority = Int32.Parse(priority.Value, NumberStyles.Integer, CultureInfo.InvariantCulture);
                Call((c, t) => c.EnqueueMergeAsync(entry, t), r =>
                {
                    d.Complete();
                    Toast(NotificationSeverityEnum.Success, Tr("Merge entry enqueued."));
                    Refresh();
                }, null, ex => d.Fail(Tr("Enqueue failed.")));
                return false;
            };
            Context.Modals.Show(dialog);
        }

        private void ProcessAll()
        {
            Confirm("Process Merge Queue", Tr("Process all queued entries in the merge queue now?"), () =>
            {
                Run((c, t) => c.ProcessAllMergeQueueAsync(t), () =>
                {
                    Toast(NotificationSeverityEnum.Success, Tr("Merge queue processing started."));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Process all failed.")));
            }, "Process All");
        }

        private void Process(MergeEntry e)
        {
            Confirm("Process Entry", Tr("Process merge entry {{id}} now?", LocalizationArgs.Of("id", e.Id)), () =>
            {
                Run((c, t) => c.ProcessMergeEntryAsync(e.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Success, Tr("Merge entry {{id}} processing started.", LocalizationArgs.Of("id", e.Id)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Process failed.")));
            }, "Process");
        }

        private void Cancel(MergeEntry e)
        {
            Confirm("Cancel Entry", Tr("Cancel merge entry {{id}}?", LocalizationArgs.Of("id", e.Id)), () =>
            {
                Run((c, t) => c.CancelMergeEntryAsync(e.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Merge entry {{id}} cancelled.", LocalizationArgs.Of("id", e.Id)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Cancel failed.")));
            }, "Cancel Entry");
        }

        private void Delete(MergeEntry e)
        {
            Confirm("Delete Entry", Tr("Delete merge entry {{id}}? This cannot be undone.", LocalizationArgs.Of("id", e.Id)), () =>
            {
                Run((c, t) => c.DeleteMergeEntryAsync(e.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Merge entry {{id}} deleted.", LocalizationArgs.Of("id", e.Id)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Delete failed.")));
            }, "Delete");
        }

        private void BulkDelete()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Confirm("Delete Selected Entries", Tr("Delete {{count}} selected merge queue entries? This cannot be undone.", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                Call(async (c, t) =>
                {
                    int failed = 0;
                    foreach (string id in ids)
                    {
                        try { await c.DeleteMergeEntryAsync(id, t).ConfigureAwait(false); }
                        catch (Armada.Client.ArmadaApiException) { failed++; }
                    }

                    return failed;
                }, failed =>
                {
                    int deleted = ids.Count - failed;
                    if (deleted > 0)
                    {
                        Toast(failed > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success, failed > 0
                            ? Tr("Deleted {{deleted}} merge entries. {{failed}} failed.", LocalizationArgs.Of("deleted", deleted, "failed", failed))
                            : Tr("Deleted {{deleted}} merge entries.", LocalizationArgs.Of("deleted", deleted)));
                    }

                    if (failed > 0) ShowMessage(Tr("Deleted {{deleted}} entries, {{failed}} failed.", LocalizationArgs.Of("deleted", deleted, "failed", failed)));
                    Refresh();
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
