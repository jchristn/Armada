namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Delivery, Deployments tab (dashboard <c>Deployments.tsx</c>): overview (total, pending approval, running,
    /// succeeded, failed or verification failed), filters (search, status, verification, vessel) applied on the
    /// server, the deployments grid, row actions (Open, Edit, View JSON, Delete), and the create/edit form. Tenant
    /// admins manage; others get a read-only hint. Refreshes live on <c>deployment.changed</c>.
    /// </summary>
    public class DeploymentsScreen : EntityListScreen<Deployment>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Deployment"; }
        }

        #endregion

        #region Private-Members

        private IReadOnlyDictionary<string, string> _Vessels = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, string> _Environments = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, string> _Releases = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public DeploymentsScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Deployment row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Deployment row)
        {
            return row.Title;
        }

        /// <inheritdoc />
        protected override string? DetailPath(Deployment row)
        {
            return "/deployments/" + row.Id;
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
            get { return "Create Deployment"; }
        }

        /// <inheritdoc />
        protected override IEnumerable<string> LiveEvents
        {
            get { return new[] { ArmadaEventTypes.DeploymentChanged }; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Deployment> grid)
        {
            grid.AddColumn(new GridColumn<Deployment>("title", "Deployment", d => d.Title + (d.ApprovalRequired ? "  (" + T("Approval required") + ")" : "")) { Weight = 3, MinWidth = 16, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Deployment>("status", "Status", d => StatusBadge.Label(d.Status.ToString())) { Width = 20, Sortable = true, Style = (d, t) => StatusBadge.Style(d.Status.ToString(), t) });
            grid.AddColumn(new GridColumn<Deployment>("verification", "Verification", d => StatusBadge.Label(d.VerificationStatus.ToString())) { Width = 14, Sortable = true, Style = (d, t) => StatusBadge.Style(d.VerificationStatus.ToString(), t) });
            grid.AddColumn(new GridColumn<Deployment>("vessel", "Vessel", d => EntityLookups.Name(_Vessels, d.VesselId)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Deployment>("environment", "Environment", d => !String.IsNullOrEmpty(d.EnvironmentId) ? EntityLookups.Name(_Environments, d.EnvironmentId, d.EnvironmentName ?? "-") : EntityUi.Dash(d.EnvironmentName)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Deployment>("release", "Release", d => EntityLookups.Name(_Releases, d.ReleaseId)) { Weight = 2, Sortable = true, DefaultVisible = true });
            grid.AddColumn(new GridColumn<Deployment>("sourceRef", "Source Ref", d => EntityUi.Dash(d.SourceRef)) { Weight = 2, Sortable = true, DefaultVisible = false });
            grid.AddColumn(new GridColumn<Deployment>("checks", "Checks", d => d.CheckRunIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)) { Width = 7, Sortable = true, Align = TUIKit.Widgets.CellAlignment.Right });
            grid.AddColumn(new GridColumn<Deployment>("updated", "Last Updated", d => EntityUi.When(Context, d.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<Deployment>("id", "ID", d => d.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No deployments match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by title, environment, source ref, summary, or ID...");
            filters.AddSelect("status", "All statuses", EntityForm.EnumOptions<DeploymentStatusEnum>(), 22);
            filters.AddSelect("verification", "All verification states", EntityForm.EnumOptions<DeploymentVerificationStatusEnum>(), 26);
            filters.AddSelect("vesselId", "All vessels", new List<SelectOption<string>>(), 22);
        }

        /// <inheritdoc />
        protected override void OnBuilt()
        {
            if (!CanCreate) Notice = "Ask a tenant administrator to create and manage deployment records.";
            DeploymentUpsertRequest? prefill = NavigationPrefill.Take<DeploymentUpsertRequest>(Context, PrefillSlots.CreateDeployment);
            if (prefill != null && CanCreate) DeploymentForms.Open(Context, null, prefill, d => Context.Navigate("/deployments/" + d.Id));
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Deployment>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            DeploymentQuery q = new DeploymentQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            q.Search = EntityUi.Blank(Filter("search"));
            q.VesselId = EntityUi.Blank(Filter("vesselId"));
            if (Enum.TryParse<DeploymentStatusEnum>(Filter("status"), true, out DeploymentStatusEnum status)) q.Status = status;
            if (Enum.TryParse<DeploymentVerificationStatusEnum>(Filter("verification"), true, out DeploymentVerificationStatusEnum verification)) q.VerificationStatus = verification;
            EnumerationResult<Deployment>? result = await Context.Client.ListDeploymentsAsync(q, token).ConfigureAwait(false);
            return PageOf(result);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Deployment row, string sortKey)
        {
            switch (sortKey)
            {
                case "updated": return row.LastUpdateUtc;
                case "checks": return row.CheckRunIds.Count;
                case "status": return (int)row.Status;
                case "verification": return (int)row.VerificationStatus;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            Task<List<Deployment>> all = EntityLookups.DeploymentsAsync(Context.Client, token);
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, token);
            Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(Context.Client, token);
            Task<List<Release>> releases = EntityLookups.ReleasesAsync(Context.Client, token);
            await Task.WhenAll(all, vessels, environments, releases).ConfigureAwait(false);
            _Vessels = vessels.Result.ToDictionary(v => v.Id, v => v.Name);
            _Environments = environments.Result.ToDictionary(e => e.Id, e => e.Name);
            _Releases = releases.Result.ToDictionary(r => r.Id, r => r.Title);
            List<SelectOption<string>> vesselOptions = EntityLookups.Options(vessels.Result, v => v.Id, v => v.Name);
            Context.Dispatcher.Post(() => Filters.SetOptions("vesselId", vesselOptions));
            List<Deployment> d = all.Result;
            return new List<KpiItem>
            {
                new KpiItem("Total Deployments", EntityUi.Number(Context, d.Count)),
                new KpiItem("Pending Approval", EntityUi.Number(Context, d.Count(x => x.Status == DeploymentStatusEnum.PendingApproval)), t => t.Warning),
                new KpiItem("Running", EntityUi.Number(Context, d.Count(x => x.Status == DeploymentStatusEnum.Running || x.Status == DeploymentStatusEnum.RollingBack)), t => t.Info),
                new KpiItem("Succeeded", EntityUi.Number(Context, d.Count(x => x.Status == DeploymentStatusEnum.Succeeded)), t => t.Success),
                new KpiItem("Failed / Verification Failed", EntityUi.Number(Context, d.Count(x => x.Status == DeploymentStatusEnum.Failed || x.Status == DeploymentStatusEnum.VerificationFailed)), t => t.Error)
            };
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            if (!CanCreate) return;
            DeploymentForms.Open(Context, null, null, d => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(Deployment row)
        {
            if (!CanEdit(row)) return;
            DeploymentForms.Open(Context, row, null, d => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Deployment row, CancellationToken token)
        {
            return Context.Client.DeleteDeploymentAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Deployment row)
        {
            return EntityUi.T(Context, "Delete \"{{title}}\"? This removes only the deployment record and leaves linked checks, releases, and environments intact.", "title", row.Title);
        }

        /// <inheritdoc />
        protected override string DeletedText(Deployment row)
        {
            return EntityUi.T(Context, "Deployment \"{{title}}\" deleted.", "title", row.Title);
        }

        #endregion
    }
}
