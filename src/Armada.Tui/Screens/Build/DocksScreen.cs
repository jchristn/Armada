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
    /// Docks (W4.8, the Docks tab of <c>/captains</c>), the dashboard's Docks page: server paging with the admin user
    /// scope, the branch and worktree path filters, sortable branch, active, and created, Delete Selected, and the row
    /// menu (View Detail, View JSON, the vessel and captain links, copy branch, Delete with worktree cleanup).
    /// Not thread-safe.
    /// </summary>
    public class DocksScreen : OpsListScreen<Dock>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Git worktrees provisioned for captains. Docks are system-managed and track branch activity."; }
        }

        /// <summary>
        /// Branch filter.
        /// </summary>
        public TextInput BranchFilter { get; }

        /// <summary>
        /// Worktree path filter.
        /// </summary>
        public TextInput PathFilter { get; }

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
        public DocksScreen(RouteMatch route, TuiContext context)
            : base(route, context, d => d.Id, "DocksScreen", "Docks")
        {
            Grid.EmptyText = "No docks found.";
            Reference.Ensure("vessels", "captains");
            UserScope = UserScopeFilter();
            BranchFilter = TextFilter("Branch", 18, "Filter...");
            PathFilter = TextFilter("Worktree Path", 18, "Filter...");

            Column("id", "ID", d => d.Id, 0, 26);
            Column("vessel", "Vessel", d => Reference.VesselName(d.VesselId), 2);
            Column("captain", "Captain", d => Reference.CaptainName(d.CaptainId), 2);
            Column("branchName", "Branch", d => String.IsNullOrEmpty(d.BranchName) ? "-" : d.BranchName!, 3, null, d => (d.BranchName ?? "").ToLowerInvariant());
            Column("worktreePath", "Worktree Path", d => String.IsNullOrEmpty(d.WorktreePath) ? "-" : d.WorktreePath!, 4);
            Column("active", "Active", d => Tr(d.Active ? "Yes" : "No"), 0, 7, d => d.Active ? 1 : 0);
            Column("createdUtc", "Created", d => Context.Loc.FormatRelative(d.CreatedUtc, Context.Clock.UtcNow), 0, 14, d => d.CreatedUtc);

            OpsScreenAction bulk = new OpsScreenAction("delete-selected", "Delete Selected", BulkDelete, "D", () => Grid.Marked.Count > 0);
            bulk.DynamicLabel = () => Tr("Delete Selected") + " (" + Grid.Marked.Count + ")";
            bulk.Danger = true;
            ScreenActions.Add(bulk);

            RowActions.Add(new OpsAction<Dock>("view", "View Detail", d => OpenRow(d), "o"));
            RowActions.Add(new OpsAction<Dock>("json", "View JSON", d => ShowJson(Tr("Dock") + ": " + d.Id, d), "j"));
            RowActions.Add(new OpsAction<Dock>("vessel", "Open Vessel", d => Context.Navigate("/vessels/" + Uri.EscapeDataString(d.VesselId)), "v", d => !String.IsNullOrEmpty(d.VesselId)));
            RowActions.Add(new OpsAction<Dock>("captain", "Open Captain", d => Context.Navigate("/captains/" + Uri.EscapeDataString(d.CaptainId!)), "C", d => !String.IsNullOrEmpty(d.CaptainId)));
            RowActions.Add(new OpsAction<Dock>("copy-id", "Copy ID", d => Copy(d.Id, "Dock ID"), "y"));
            RowActions.Add(new OpsAction<Dock>("copy-branch", "Copy branch", d => Copy(d.BranchName, "Branch"), "Y", d => !String.IsNullOrEmpty(d.BranchName)));
            OpsAction<Dock> delete = new OpsAction<Dock>("delete", "Delete", Delete, "del");
            delete.Danger = true;
            RowActions.Add(delete);

            Start("createdUtc", true);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<Dock>> FetchAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("userId", UserScope?.Value);
            EnumerationResult<Dock>? result = await Context.Client.ListDocksAsync(q, token).ConfigureAwait(false);
            List<Dock> rows = result?.Objects ?? new List<Dock>();
            return new GridPage<Dock>(rows, result?.TotalRecords ?? rows.Count);
        }

        /// <inheritdoc />
        protected override IEnumerable<Dock> FilterLocal(IEnumerable<Dock> rows)
        {
            string branch = BranchFilter.Value.Trim();
            string path = PathFilter.Value.Trim();
            return rows.Where(d =>
                (branch.Length == 0 || (d.BranchName ?? "").IndexOf(branch, StringComparison.OrdinalIgnoreCase) >= 0) &&
                (path.Length == 0 || (d.WorktreePath ?? "").IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<Dock> rows)
        {
            Grid.EmptyText = rows.Count == 0 ? "No docks found." : "No docks match the current filters.";
        }

        /// <inheritdoc />
        protected override void OpenRow(Dock row)
        {
            Context.Navigate("/docks/" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        protected override string RowTitle(Dock row)
        {
            return Tr("Dock") + ": " + (String.IsNullOrEmpty(row.BranchName) ? row.Id : row.BranchName);
        }

        #endregion

        #region Private-Methods

        private void Delete(Dock dock)
        {
            Confirm("Delete Dock", Tr("Delete dock {{id}}? This will clean up the git worktree and cannot be undone.", LocalizationArgs.Of("id", dock.Id)), () =>
            {
                Run((c, t) => c.DeleteDockAsync(dock.Id, t), () =>
                {
                    Toast(NotificationSeverityEnum.Warning, Tr("Dock {{id}} deleted.", LocalizationArgs.Of("id", dock.Id)));
                    Refresh();
                }, null, ex => ShowMessage(Tr("Delete failed.")));
            }, "Delete");
        }

        private void BulkDelete()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Confirm("Delete Selected Docks", Tr("Delete {{count}} selected dock(s)? This will clean up the git worktrees and cannot be undone.", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                Call(async (c, t) =>
                {
                    int failed = 0;
                    foreach (string id in ids)
                    {
                        try { await c.DeleteDockAsync(id, t).ConfigureAwait(false); }
                        catch (ArmadaApiException) { failed++; }
                    }

                    return failed;
                }, failed =>
                {
                    int deleted = ids.Count - failed;
                    if (deleted > 0)
                    {
                        Toast(failed > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success, failed > 0
                            ? Tr("Deleted {{deleted}} docks. {{failed}} failed.", LocalizationArgs.Of("deleted", deleted, "failed", failed))
                            : Tr("Deleted {{deleted}} docks.", LocalizationArgs.Of("deleted", deleted)));
                    }

                    if (failed > 0) ShowMessage(Tr("Deleted {{deleted}} docks, {{failed}} failed.", LocalizationArgs.Of("deleted", deleted, "failed", failed)));
                    Refresh();
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
