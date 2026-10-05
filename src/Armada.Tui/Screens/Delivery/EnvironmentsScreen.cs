namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Delivery, Environments tab (dashboard <c>Environments.tsx</c>): overview (total, active, default targets,
    /// require approval), filters (search, kind, vessel, active) applied on the server, the environments grid
    /// (environment, kind, vessel, base URL, health, policy, last updated), row actions (Open, Edit, Duplicate, View
    /// JSON, Delete), and the create/edit form. Tenant admins manage; others get a read-only hint.
    /// </summary>
    public class EnvironmentsScreen : EntityListScreen<DeploymentEnvironment>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Environment"; }
        }

        #endregion

        #region Private-Members

        private IReadOnlyDictionary<string, string> _Vessels = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public EnvironmentsScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(DeploymentEnvironment row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(DeploymentEnvironment row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override string? DetailPath(DeploymentEnvironment row)
        {
            return "/environments/" + row.Id;
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
        protected override string CreateLabel
        {
            get { return "Create Environment"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<DeploymentEnvironment> grid)
        {
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("name", "Environment", e => e.Name + "  (" + T(e.IsDefault ? "Default target" : "Non-default") + ", " + T(e.Active ? "Active" : "Inactive") + ")") { Weight = 3, MinWidth = 16, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("kind", "Kind", e => e.Kind.ToString()) { Width = 15, Sortable = true });
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("vessel", "Vessel", e => EntityLookups.Name(_Vessels, e.VesselId)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("baseUrl", "Base URL", e => EntityUi.Dash(e.BaseUrl)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("health", "Health", e => EntityUi.Dash(e.HealthEndpoint)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("policy", "Policy", e => T(e.RequiresApproval ? "Approval required" : "Self-service")) { Width = 18, Sortable = true, Style = (e, t) => e.RequiresApproval ? t.Warning : (TUIKit.CellStyle?)null });
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("updated", "Last Updated", e => EntityUi.When(Context, e.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("description", "Description", e => EntityUi.Dash(e.Description)) { Weight = 2, DefaultVisible = false });
            grid.AddColumn(new GridColumn<DeploymentEnvironment>("id", "ID", e => e.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No environments match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by name, description, base URL, configuration source, or ID...");
            filters.AddSelect("kind", "All kinds", EntityForm.EnumOptions<EnvironmentKindEnum>(), 18);
            filters.AddSelect("vesselId", "All vessels", new List<SelectOption<string>>(), 22);
            filters.AddSelect("active", "All states", new List<SelectOption<string>>
            {
                new SelectOption<string>("true", T("Active")),
                new SelectOption<string>("false", T("Inactive"))
            }, 14);
        }

        /// <inheritdoc />
        protected override void OnBuilt()
        {
            if (!CanCreate) Notice = "Ask a tenant administrator to create and manage environment records.";
        }

        /// <inheritdoc />
        protected override async Task<GridPage<DeploymentEnvironment>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            DeploymentEnvironmentQuery q = new DeploymentEnvironmentQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            q.Search = EntityUi.Blank(Filter("search"));
            q.VesselId = EntityUi.Blank(Filter("vesselId"));
            if (Enum.TryParse<EnvironmentKindEnum>(Filter("kind"), true, out EnvironmentKindEnum kind)) q.Kind = kind;
            if (Filter("active") == "true") q.Active = true;
            else if (Filter("active") == "false") q.Active = false;
            return PageOf(await Context.Client.ListEnvironmentsAsync(q, token).ConfigureAwait(false));
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(DeploymentEnvironment row, string sortKey)
        {
            switch (sortKey)
            {
                case "updated": return row.LastUpdateUtc;
                case "kind": return (int)row.Kind;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            Task<List<DeploymentEnvironment>> all = EntityLookups.EnvironmentsAsync(Context.Client, token);
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, token);
            await Task.WhenAll(all, vessels).ConfigureAwait(false);
            _Vessels = vessels.Result.ToDictionary(v => v.Id, v => v.Name);
            List<SelectOption<string>> vesselOptions = EntityLookups.Options(vessels.Result, v => v.Id, v => v.Name);
            Context.Dispatcher.Post(() => Filters.SetOptions("vesselId", vesselOptions));
            List<DeploymentEnvironment> e = all.Result;
            return new List<KpiItem>
            {
                new KpiItem("Total Environments", EntityUi.Number(Context, e.Count)),
                new KpiItem("Active", EntityUi.Number(Context, e.Count(x => x.Active)), t => t.Success),
                new KpiItem("Default Targets", EntityUi.Number(Context, e.Count(x => x.IsDefault))),
                new KpiItem("Require Approval", EntityUi.Number(Context, e.Count(x => x.RequiresApproval)), t => t.Warning)
            };
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(DeploymentEnvironment row)
        {
            List<ActionMenuItem> items = base.RowActions(row);
            if (CanEdit(row))
            {
                int idx = Math.Max(0, items.FindIndex(i => i.Key == ActionMenuItem.JsonKey));
                items.Insert(idx, new ActionMenuItem("Duplicate", () => EnvironmentForms.Duplicate(Context, row)));
            }

            return items;
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            if (!CanCreate) return;
            EnvironmentForms.Open(Context, null, e => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(DeploymentEnvironment row)
        {
            if (!CanEdit(row)) return;
            EnvironmentForms.Open(Context, row, e => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(DeploymentEnvironment row, CancellationToken token)
        {
            return Context.Client.DeleteEnvironmentAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(DeploymentEnvironment row)
        {
            return EntityUi.T(Context, "Delete \"{{name}}\"? This removes only the environment record.", "name", row.Name);
        }

        /// <inheritdoc />
        protected override string DeletedText(DeploymentEnvironment row)
        {
            return EntityUi.T(Context, "Environment \"{{name}}\" deleted.", "name", row.Name);
        }

        #endregion
    }
}
