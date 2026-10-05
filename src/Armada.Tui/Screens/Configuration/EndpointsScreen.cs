namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Configuration, Endpoints tab (dashboard <c>Endpoints.tsx</c>): Run Health Sweep (tenant admins), overview
    /// (total, healthy, unhealthy, enabled), filters (search and kind; the API returns a plain list, so filtering and
    /// paging are local), the grid (endpoint, kind, provider, model, visibility, health with a history strip, last
    /// checked), row actions (Health, Validate, Edit, View JSON, Delete), the create/edit form, Validate Now results,
    /// and the health detail. Enter edits rows the user may edit and opens Health otherwise, as the dashboard does.
    /// </summary>
    public class EndpointsScreen : EntityListScreen<ModelEndpoint>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Endpoint"; }
        }

        /// <summary>
        /// True while a health sweep runs.
        /// </summary>
        public bool Sweeping { get; private set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public EndpointsScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Open(ModelEndpoint row)
        {
            if (CanEdit(row)) OpenEdit(row);
            else ShowHealth(row);
        }

        /// <summary>
        /// Show the health detail for a row.
        /// </summary>
        /// <param name="row">Endpoint.</param>
        public void ShowHealth(ModelEndpoint row)
        {
            EndpointForms.ShowHealth(Context, row, CanEdit(row), Reload);
        }

        /// <summary>
        /// Probe every enabled endpoint (deduplicated by base URL), as the dashboard does (no confirmation).
        /// </summary>
        public void RunHealthSweep()
        {
            if (!Context.Session.IsTenantAdmin || Sweeping) return;
            Sweeping = true;
            EntityUi.Toast(Context, NotificationSeverityEnum.Info, T("Sweeping..."));
            EntityUi.Run<ModelEndpointHealthSweepResponse?>(Context, ct => Context.Client.HealthCheckModelEndpointsAsync(ct), result =>
            {
                Sweeping = false;
                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Health sweep probed {{count}} base URL(s).", "count", result?.DistinctBaseUrlsProbed ?? 0));
                Reload();
            }, "Health sweep failed.", ex => Sweeping = false);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(ModelEndpoint row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(ModelEndpoint row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override bool CanCreate
        {
            get { return Context.Session.IsSignedIn; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(ModelEndpoint row)
        {
            return ScopeRules.CanEdit(Context.Session, row.Scope, row.TenantId, row.UserId);
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
            get { return "Create Endpoint"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<ModelEndpoint> grid)
        {
            grid.AddColumn(new GridColumn<ModelEndpoint>("name", "Endpoint", e => e.Name + (e.Enabled ? "" : " (" + T("disabled") + ")")) { Weight = 3, MinWidth = 14, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<ModelEndpoint>("kind", "Kind", e => e.Kind.ToString()) { Width = 10, Sortable = true });
            grid.AddColumn(new GridColumn<ModelEndpoint>("provider", "Provider", e => e.Provider.ToString()) { Width = 16, Sortable = true });
            grid.AddColumn(new GridColumn<ModelEndpoint>("model", "Model", e => EntityUi.Dash(e.Model)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<ModelEndpoint>("baseUrl", "Base URL", e => EntityUi.Dash(e.BaseUrl)) { Weight = 3, Sortable = true, DefaultVisible = false });
            grid.AddColumn(new GridColumn<ModelEndpoint>("visibility", "Visibility", e => T(ScopeRules.Label(e.Scope))) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<ModelEndpoint>("health", "Health", e => StatusBadge.Label(e.HealthStatus) + " " + EndpointHealthHistogram.Strip(e.HealthHistory, Context.Clock.UtcNow, 12)) { Width = 24, Sortable = true, Style = (e, t) => StatusBadge.Style(e.HealthStatus, t) });
            grid.AddColumn(new GridColumn<ModelEndpoint>("lastChecked", "Last Checked", e => e.LastHealthCheckUtc.HasValue ? EntityUi.When(Context, e.LastHealthCheckUtc) : T("Never")) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<ModelEndpoint>("id", "ID", e => e.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No endpoints match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by name, base URL, model, or ID...");
            filters.AddSelect("kind", "All kinds", EntityForm.EnumOptions<ModelEndpointKindEnum>(), 16);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<ModelEndpoint>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            List<ModelEndpoint> all = await ReadAllAsync(token).ConfigureAwait(false);
            string search = Filter("search");
            string kind = Filter("kind");
            List<ModelEndpoint> filtered = all.Where(e => Matches(search, e.Name, e.BaseUrl, e.Model, e.Id)
                && (kind == "" || String.Equals(e.Kind.ToString(), kind, StringComparison.OrdinalIgnoreCase))).ToList();
            return Slice(filtered, query);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(ModelEndpoint row, string sortKey)
        {
            if (sortKey == "lastChecked") return row.LastHealthCheckUtc ?? DateTime.MinValue;
            if (sortKey == "health") return (int)row.HealthStatus;
            return null;
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            List<ModelEndpoint> all = await ReadAllAsync(token).ConfigureAwait(false);
            return new List<KpiItem>
            {
                new KpiItem("Total Endpoints", EntityUi.Number(Context, all.Count)),
                new KpiItem("Healthy", EntityUi.Number(Context, all.Count(e => e.HealthStatus == EndpointHealthStatusEnum.Healthy)), t => t.Success),
                new KpiItem("Unhealthy", EntityUi.Number(Context, all.Count(e => e.HealthStatus == EndpointHealthStatusEnum.Unhealthy)), t => t.Error),
                new KpiItem("Enabled", EntityUi.Number(Context, all.Count(e => e.Enabled)))
            };
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(ModelEndpoint row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("Health", () => ShowHealth(row), "Enter"));
            if (CanEdit(row))
            {
                items.Add(new ActionMenuItem("Validate", () => EndpointForms.Validate(Context, row, Reload), "v"));
                items.Add(new ActionMenuItem("Edit", () => OpenEdit(row), "e"));
            }

            items.Add(new ActionMenuItem("View JSON", () => EntityUi.ShowJson(Context, row.Name, row), "j"));
            items.Add(new ActionMenuItem("Copy ID", () => Context.Clipboard.Copy(row.Id, "ID"), "y"));
            if (CanEdit(row))
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => RequestDelete(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <inheritdoc />
        protected override IEnumerable<ArmadaCommand> ExtraCommands()
        {
            string p = CommandPrefix;
            return new List<ArmadaCommand>
            {
                Cmd(p + ".health-sweep", "Run Health Sweep", RunHealthSweep, () => Context.Session.IsTenantAdmin && !Sweeping, "H"),
                Cmd(p + ".health", "Health", () => { ModelEndpoint? e = Grid.Current; if (e != null) ShowHealth(e); }, () => Grid.Current != null, "h"),
                Cmd(p + ".validate", "Validate", () => { ModelEndpoint? e = Grid.Current; if (e != null && CanEdit(e)) EndpointForms.Validate(Context, e, Reload); }, () => Grid.Current != null && CanEdit(Grid.Current), "v")
            };
        }

        /// <inheritdoc />
        protected override void OnBuilt()
        {
            if (!Context.Session.IsTenantAdmin) Notice = "Ask a tenant administrator to configure model endpoints.";
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            EndpointForms.Open(Context, null, e => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(ModelEndpoint row)
        {
            if (!CanEdit(row)) return;
            EndpointForms.Open(Context, row, e => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(ModelEndpoint row, CancellationToken token)
        {
            return Context.Client.DeleteModelEndpointAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(ModelEndpoint row)
        {
            return EntityUi.T(Context, "Delete \"{{name}}\"? This cannot be undone.", "name", row.Name);
        }

        /// <inheritdoc />
        protected override string DeletedText(ModelEndpoint row)
        {
            return EntityUi.T(Context, "Endpoint \"{{name}}\" deleted.", "name", row.Name);
        }

        #endregion

        #region Private-Methods

        private async Task<List<ModelEndpoint>> ReadAllAsync(CancellationToken token)
        {
            List<ModelEndpoint>? list = await Context.Client.ListModelEndpointsAsync(token).ConfigureAwait(false);
            return list ?? new List<ModelEndpoint>();
        }

        #endregion
    }
}
