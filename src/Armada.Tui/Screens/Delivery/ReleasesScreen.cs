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
    /// Delivery, Releases tab (dashboard <c>Releases.tsx</c>): overview (total, shipped, candidates, failed or rolled
    /// back), filters (search, status, vessel) applied on the server, the releases grid (release, status, vessel,
    /// workflow, linked work, published, last updated), row actions (Open, Edit, View JSON, Delete), and the
    /// create/edit form. Tenant admins manage; others get a read-only hint.
    /// </summary>
    public class ReleasesScreen : EntityListScreen<Release>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Release"; }
        }

        #endregion

        #region Private-Members

        private IReadOnlyDictionary<string, string> _Vessels = new Dictionary<string, string>();
        private IReadOnlyDictionary<string, string> _Profiles = new Dictionary<string, string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public ReleasesScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Release row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Release row)
        {
            return row.Title;
        }

        /// <inheritdoc />
        protected override string? DetailPath(Release row)
        {
            return "/releases/" + row.Id;
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
            get { return "Create Release"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Release> grid)
        {
            grid.AddColumn(new GridColumn<Release>("title", "Release", r => r.Title + "  (" + (String.IsNullOrEmpty(r.Version) ? T("Unversioned") : r.Version) + (String.IsNullOrEmpty(r.TagName) ? "" : ", " + r.TagName) + ")") { Weight = 3, MinWidth = 16, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Release>("status", "Status", r => StatusBadge.Label(r.Status.ToString())) { Width = 13, Sortable = true, Style = (r, t) => StatusBadge.Style(r.Status.ToString(), t) });
            grid.AddColumn(new GridColumn<Release>("vessel", "Vessel", r => EntityLookups.Name(_Vessels, r.VesselId)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Release>("workflow", "Workflow", r => !String.IsNullOrEmpty(r.WorkflowProfileId) ? EntityLookups.Name(_Profiles, r.WorkflowProfileId) : T("Resolved default")) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Release>("linked", "Linked Work", r => r.VoyageIds.Count + " " + T("voyages") + ", " + r.MissionIds.Count + " " + T("missions") + ", " + r.CheckRunIds.Count + " " + T("checks") + ", " + r.Artifacts.Count + " " + T("artifacts")) { Weight = 3, Sortable = true });
            grid.AddColumn(new GridColumn<Release>("published", "Published", r => EntityUi.When(Context, r.PublishedUtc)) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<Release>("updated", "Last Updated", r => EntityUi.When(Context, r.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<Release>("summary", "Summary", r => EntityUi.Dash(r.Summary)) { Weight = 2, DefaultVisible = false });
            grid.AddColumn(new GridColumn<Release>("id", "ID", r => r.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No releases match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by title, version, tag, summary, or ID...");
            filters.AddSelect("status", "All statuses", EntityForm.EnumOptions<ReleaseStatusEnum>(), 18);
            filters.AddSelect("vesselId", "All vessels", new List<SelectOption<string>>(), 22);
        }

        /// <inheritdoc />
        protected override void OnBuilt()
        {
            if (!CanCreate) Notice = "Ask a tenant administrator to create and manage release records.";
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Release>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            ReleaseQuery q = new ReleaseQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            q.Search = EntityUi.Blank(Filter("search"));
            q.VesselId = EntityUi.Blank(Filter("vesselId"));
            if (Enum.TryParse<ReleaseStatusEnum>(Filter("status"), true, out ReleaseStatusEnum status)) q.Status = status;
            return PageOf(await Context.Client.ListReleasesAsync(q, token).ConfigureAwait(false));
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Release row, string sortKey)
        {
            switch (sortKey)
            {
                case "updated": return row.LastUpdateUtc;
                case "published": return row.PublishedUtc ?? DateTime.MinValue;
                case "status": return (int)row.Status;
                case "linked": return row.VoyageIds.Count + row.MissionIds.Count + row.CheckRunIds.Count + row.Artifacts.Count;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            Task<List<Release>> all = EntityLookups.ReleasesAsync(Context.Client, token);
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, token);
            Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(Context.Client, token);
            await Task.WhenAll(all, vessels, profiles).ConfigureAwait(false);
            _Vessels = vessels.Result.ToDictionary(v => v.Id, v => v.Name);
            _Profiles = profiles.Result.ToDictionary(p => p.Id, p => p.Name);
            List<SelectOption<string>> vesselOptions = EntityLookups.Options(vessels.Result, v => v.Id, v => v.Name);
            Context.Dispatcher.Post(() => Filters.SetOptions("vesselId", vesselOptions));
            List<Release> r = all.Result;
            return new List<KpiItem>
            {
                new KpiItem("Total Releases", EntityUi.Number(Context, r.Count)),
                new KpiItem("Shipped", EntityUi.Number(Context, r.Count(x => x.Status == ReleaseStatusEnum.Shipped)), t => t.Success),
                new KpiItem("Candidates", EntityUi.Number(Context, r.Count(x => x.Status == ReleaseStatusEnum.Candidate)), t => t.Info),
                new KpiItem("Failed / Rolled Back", EntityUi.Number(Context, r.Count(x => x.Status == ReleaseStatusEnum.Failed || x.Status == ReleaseStatusEnum.RolledBack)), t => t.Error)
            };
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            if (!CanCreate) return;
            ReleaseForms.Open(Context, null, r => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(Release row)
        {
            if (!CanEdit(row)) return;
            ReleaseForms.Open(Context, row, r => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Release row, CancellationToken token)
        {
            return Context.Client.DeleteReleaseAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Release row)
        {
            return EntityUi.T(Context, "Delete \"{{title}}\"? This removes the release record but does not delete linked work or artifacts on disk.", "title", row.Title);
        }

        /// <inheritdoc />
        protected override string DeletedText(Release row)
        {
            return EntityUi.T(Context, "Release \"{{title}}\" deleted.", "title", row.Title);
        }

        #endregion
    }
}
