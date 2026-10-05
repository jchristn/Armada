namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Configuration, Workflow Profiles tab (dashboard <c>WorkflowProfiles.tsx</c>): overview (total, active,
    /// defaults, environment targets), filters (search, scope Global/Fleet/Vessel, status) applied on the server, the
    /// profiles grid (profile, scope, visibility, capabilities, targets, status, updated), row actions (Open, Edit,
    /// Duplicate, View JSON, Delete), and the quick create/edit form. Editing follows the scoped-visibility rules:
    /// anyone may create (non-admins get personal profiles); only owners and admins edit or delete.
    /// </summary>
    public class WorkflowProfilesScreen : EntityListScreen<WorkflowProfile>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Workflow Profile"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public WorkflowProfilesScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(WorkflowProfile row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(WorkflowProfile row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override string? DetailPath(WorkflowProfile row)
        {
            return "/workflow-profiles/" + row.Id;
        }

        /// <inheritdoc />
        protected override bool HasCreate
        {
            get { return true; }
        }

        /// <inheritdoc />
        protected override bool HasEdit
        {
            get { return true; }
        }

        /// <inheritdoc />
        protected override bool HasDelete
        {
            get { return true; }
        }

        /// <inheritdoc />
        protected override bool CanCreate
        {
            get { return Context.Session.IsSignedIn; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(WorkflowProfile row)
        {
            return ScopeRules.CanEdit(Context.Session, row.OwnershipScope, row.TenantId, row.UserId);
        }

        /// <inheritdoc />
        protected override string CreateLabel
        {
            get { return "Create Workflow Profile"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<WorkflowProfile> grid)
        {
            grid.AddColumn(new GridColumn<WorkflowProfile>("name", "Profile", p => p.Name) { Weight = 3, MinWidth = 16, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<WorkflowProfile>("scope", "Scope", p => p.Scope.ToString() + (p.IsDefault ? " (" + T("Default") + ")" : "")) { Width = 18, Sortable = true });
            grid.AddColumn(new GridColumn<WorkflowProfile>("visibility", "Visibility", p => T(ScopeRules.Label(p.OwnershipScope))) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<WorkflowProfile>("capabilities", "Capabilities", p => WorkflowProfileForms.CountCapabilities(p).ToString(CultureInfo.InvariantCulture) + " " + T("commands")) { Width = 14, Sortable = true });
            grid.AddColumn(new GridColumn<WorkflowProfile>("targets", "Targets", p => (p.Environments?.Count ?? 0).ToString(CultureInfo.InvariantCulture) + " " + T("environments")) { Width = 16, Sortable = true });
            grid.AddColumn(new GridColumn<WorkflowProfile>("status", "Status", p => StatusBadge.Label(p.Active ? "Active" : "Inactive")) { Width = 11, Sortable = true, Style = (p, t) => StatusBadge.Style(p.Active ? "Active" : "Inactive", t) });
            grid.AddColumn(new GridColumn<WorkflowProfile>("updated", "Last Updated", p => EntityUi.When(Context, p.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<WorkflowProfile>("description", "Description", p => EntityUi.Dash(p.Description)) { Weight = 3, DefaultVisible = false });
            grid.AddColumn(new GridColumn<WorkflowProfile>("id", "ID", p => p.Id) { Width = 28, DefaultVisible = false });
            grid.EmptyText = "No workflow profiles match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by name, description, or ID...");
            filters.AddSelect("scope", "All scopes", ProfileFormSupport.ScopeOptions(Context), 16);
            filters.AddSelect("status", "All statuses", ProfileFormSupport.StatusOptions(Context), 18);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<WorkflowProfile>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("search", EntityUi.Blank(Filter("search")));
            q.With("scope", EntityUi.Blank(Filter("scope")));
            string status = Filter("status");
            q.With("active", status == "active" ? "true" : status == "inactive" ? "false" : null);
            EnumerationResult<WorkflowProfile>? result = await Context.Client.ListWorkflowProfilesAsync(q, token).ConfigureAwait(false);
            return PageOf(result);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(WorkflowProfile row, string sortKey)
        {
            switch (sortKey)
            {
                case "updated": return row.LastUpdateUtc;
                case "capabilities": return WorkflowProfileForms.CountCapabilities(row);
                case "targets": return row.Environments?.Count ?? 0;
                case "scope": return (int)row.Scope;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            List<WorkflowProfile> all = await EntityLookups.WorkflowProfilesAsync(Context.Client, token).ConfigureAwait(false);
            return new List<KpiItem>
            {
                new KpiItem("Total Profiles", EntityUi.Number(Context, all.Count)),
                new KpiItem("Active", EntityUi.Number(Context, all.Count(p => p.Active)), t => t.Success),
                new KpiItem("Defaults", EntityUi.Number(Context, all.Count(p => p.IsDefault))),
                new KpiItem("Environment Targets", EntityUi.Number(Context, all.Sum(p => p.Environments?.Count ?? 0)))
            };
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(WorkflowProfile row)
        {
            List<ActionMenuItem> items = base.RowActions(row);
            int json = items.FindIndex(i => i.Key == ActionMenuItem.JsonKey);
            items.Insert(Math.Max(0, json), new ActionMenuItem("Duplicate", () => WorkflowProfileForms.Duplicate(Context, row)));
            return items;
        }

        /// <inheritdoc />
        protected override IEnumerable<ArmadaCommand> ExtraCommands()
        {
            return new List<ArmadaCommand>
            {
                Cmd(CommandPrefix + ".duplicate", "Duplicate", () => { if (Grid.Current != null) WorkflowProfileForms.Duplicate(Context, Grid.Current); }, () => Grid.Current != null)
            };
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            WorkflowProfileForms.Open(Context, null, p => Context.Navigate("/workflow-profiles/" + p.Id));
        }

        /// <inheritdoc />
        protected override void OpenEdit(WorkflowProfile row)
        {
            if (!CanEdit(row)) return;
            WorkflowProfileForms.Open(Context, row, p => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(WorkflowProfile row, CancellationToken token)
        {
            return Context.Client.DeleteWorkflowProfileAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(WorkflowProfile row)
        {
            return EntityUi.T(Context, "Delete \"{{name}}\"? Existing check runs remain, but this profile will no longer be available.", "name", row.Name);
        }

        /// <inheritdoc />
        protected override string DeletedText(WorkflowProfile row)
        {
            return EntityUi.T(Context, "Workflow profile \"{{name}}\" deleted.", "name", row.Name);
        }

        #endregion
    }
}
