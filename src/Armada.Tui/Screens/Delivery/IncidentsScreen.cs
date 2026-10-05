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
    /// Delivery, Incidents tab (dashboard <c>Incidents.tsx</c>): overview (total, open, monitoring, mitigated, closed or
    /// rolled back), filters (search, status, severity) applied on the server, the incidents grid, row actions (Open,
    /// View JSON, Delete), and the Create Incident modal. Tenant admins manage; others get a read-only hint.
    /// Refreshes live on <c>incident.changed</c>.
    /// </summary>
    public class IncidentsScreen : EntityListScreen<Incident>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Incident"; }
        }

        #endregion

        #region Private-Members

        private IReadOnlyDictionary<string, string> _Environments = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, string> _Deployments = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, string> _Releases = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public IncidentsScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Incident row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Incident row)
        {
            return row.Title;
        }

        /// <inheritdoc />
        protected override string? DetailPath(Incident row)
        {
            return "/incidents/" + row.Id;
        }

        /// <inheritdoc />
        protected override bool HasCreate
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
            get { return "Create Incident"; }
        }

        /// <inheritdoc />
        protected override IEnumerable<string> LiveEvents
        {
            get { return new[] { ArmadaEventTypes.IncidentChanged }; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Incident> grid)
        {
            grid.AddColumn(new GridColumn<Incident>("title", "Incident", i => i.Title) { Weight = 3, MinWidth = 16, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Incident>("status", "Status", i => StatusBadge.Label(i.Status)) { Width = 14, Sortable = true, Style = (i, t) => StatusBadge.Style(i.Status, t) });
            grid.AddColumn(new GridColumn<Incident>("severity", "Severity", i => SeverityLabel(i.Severity)) { Width = 12, Sortable = true, Style = (i, t) => SeverityStyle(i.Severity, t) });
            grid.AddColumn(new GridColumn<Incident>("environment", "Environment", i => !String.IsNullOrEmpty(i.EnvironmentId) ? EntityLookups.Name(_Environments, i.EnvironmentId, i.EnvironmentName ?? "-") : EntityUi.Dash(i.EnvironmentName)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Incident>("deployment", "Deployment", i => EntityLookups.Name(_Deployments, i.DeploymentId)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Incident>("release", "Release", i => EntityLookups.Name(_Releases, i.ReleaseId)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Incident>("summary", "Summary", i => !String.IsNullOrEmpty(i.Summary) ? i.Summary! : !String.IsNullOrEmpty(i.Impact) ? i.Impact! : T("No summary provided")) { Weight = 3, DefaultVisible = false });
            grid.AddColumn(new GridColumn<Incident>("updated", "Last Updated", i => EntityUi.When(Context, i.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<Incident>("id", "ID", i => i.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No incidents match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by title, summary, impact, environment, or ID...");
            filters.AddSelect("status", "All statuses", EntityForm.EnumOptions<IncidentStatusEnum>(), 18);
            filters.AddSelect("severity", "All severities", EntityForm.EnumOptions<IncidentSeverityEnum>(), 18);
        }

        /// <inheritdoc />
        protected override void OnBuilt()
        {
            if (!CanCreate) Notice = "Ask a tenant administrator to create and manage incident records.";
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Incident>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            IncidentQuery q = new IncidentQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            q.Search = EntityUi.Blank(Filter("search"));
            if (Enum.TryParse<IncidentStatusEnum>(Filter("status"), true, out IncidentStatusEnum status)) q.Status = status;
            if (Enum.TryParse<IncidentSeverityEnum>(Filter("severity"), true, out IncidentSeverityEnum severity)) q.Severity = severity;
            EnumerationResult<Incident>? result = await Context.Client.ListIncidentsAsync(q, token).ConfigureAwait(false);
            return PageOf(result);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Incident row, string sortKey)
        {
            switch (sortKey)
            {
                case "updated": return row.LastUpdateUtc;
                case "status": return (int)row.Status;
                case "severity": return (int)row.Severity;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            Task<List<Incident>> all = ReadAllIncidentsAsync(token);
            Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(Context.Client, token);
            Task<List<Deployment>> deployments = EntityLookups.DeploymentsAsync(Context.Client, token);
            Task<List<Release>> releases = EntityLookups.ReleasesAsync(Context.Client, token);
            await Task.WhenAll(all, environments, deployments, releases).ConfigureAwait(false);
            _Environments = environments.Result.ToDictionary(e => e.Id, e => e.Name);
            _Deployments = deployments.Result.ToDictionary(d => d.Id, d => d.Title);
            _Releases = releases.Result.ToDictionary(r => r.Id, r => r.Title);
            List<Incident> i = all.Result;
            return new List<KpiItem>
            {
                new KpiItem("Total Incidents", EntityUi.Number(Context, i.Count)),
                new KpiItem("Open", EntityUi.Number(Context, i.Count(x => x.Status == IncidentStatusEnum.Open)), t => t.Error),
                new KpiItem("Monitoring", EntityUi.Number(Context, i.Count(x => x.Status == IncidentStatusEnum.Monitoring)), t => t.Warning),
                new KpiItem("Mitigated", EntityUi.Number(Context, i.Count(x => x.Status == IncidentStatusEnum.Mitigated)), t => t.Info),
                new KpiItem("Closed / Rolled Back", EntityUi.Number(Context, i.Count(x => x.Status == IncidentStatusEnum.Closed || x.Status == IncidentStatusEnum.RolledBack)), t => t.Success)
            };
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            if (!CanCreate) return;
            IncidentForms.Open(Context, null, null, false, i => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Incident row, CancellationToken token)
        {
            return Context.Client.DeleteIncidentAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Incident row)
        {
            return EntityUi.T(Context, "Delete \"{{title}}\"? This removes the incident snapshot chain but does not affect deployments, checks, or releases.", "title", row.Title);
        }

        /// <inheritdoc />
        protected override string DeletedText(Incident row)
        {
            return EntityUi.T(Context, "Incident \"{{title}}\" deleted.", "title", row.Title);
        }

        #endregion

        #region Private-Methods

        private static string SeverityLabel(IncidentSeverityEnum severity)
        {
            string marker = severity == IncidentSeverityEnum.Critical || severity == IncidentSeverityEnum.High ? "x" : severity == IncidentSeverityEnum.Medium ? "!" : "-";
            return marker + " " + severity;
        }

        private static TUIKit.CellStyle? SeverityStyle(IncidentSeverityEnum severity, Armada.Tui.Theming.ArmadaTheme theme)
        {
            if (severity == IncidentSeverityEnum.Critical || severity == IncidentSeverityEnum.High) return theme.Error;
            if (severity == IncidentSeverityEnum.Medium) return theme.Warning;
            return theme.Muted;
        }

        private async Task<List<Incident>> ReadAllIncidentsAsync(CancellationToken token)
        {
            try
            {
                return await ArmadaPaging.ReadAllAsync<Incident>((p, ct) => Context.Client.ListIncidentsAsync(new IncidentQuery { PageNumber = p, PageSize = EntityLookups.PageSize }, ct), 20, token).ConfigureAwait(false);
            }
            catch (ArmadaApiException)
            {
                return new List<Incident>();
            }
        }

        #endregion
    }
}
