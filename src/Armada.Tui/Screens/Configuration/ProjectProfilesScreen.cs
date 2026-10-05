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
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Configuration, Project Profiles tab (dashboard <c>ProjectProfiles.tsx</c>): overview (total, active, defaults,
    /// persona overrides), filters (search, scope, status) applied on the server, the grid (profile, scope, visibility,
    /// overrides, skills, status, updated), row actions (Open, Edit, View JSON, Delete), and the create/edit form.
    /// Editing follows the scoped-visibility rules.
    /// </summary>
    public class ProjectProfilesScreen : EntityListScreen<ProjectProfile>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Project Profile"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public ProjectProfilesScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(ProjectProfile row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(ProjectProfile row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override string? DetailPath(ProjectProfile row)
        {
            return "/project-profiles/" + row.Id;
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
        protected override bool CanEdit(ProjectProfile row)
        {
            return ScopeRules.CanEdit(Context.Session, row.OwnershipScope, row.TenantId, row.UserId);
        }

        /// <inheritdoc />
        protected override string CreateLabel
        {
            get { return "Create Project Profile"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<ProjectProfile> grid)
        {
            grid.AddColumn(new GridColumn<ProjectProfile>("name", "Profile", p => p.Name) { Weight = 3, MinWidth = 16, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<ProjectProfile>("scope", "Scope", p => p.Scope.ToString() + (p.IsDefault ? " (" + T("Default") + ")" : "")) { Width = 18, Sortable = true });
            grid.AddColumn(new GridColumn<ProjectProfile>("visibility", "Visibility", p => T(ScopeRules.Label(p.OwnershipScope))) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<ProjectProfile>("overrides", "Overrides", p => (p.PersonaOverrides?.Count ?? 0).ToString(CultureInfo.InvariantCulture) + " " + T("personas")) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<ProjectProfile>("skills", "Skills", p => (p.Skills?.Count ?? 0).ToString(CultureInfo.InvariantCulture) + " " + T("skills")) { Width = 11, Sortable = true });
            grid.AddColumn(new GridColumn<ProjectProfile>("status", "Status", p => StatusBadge.Label(p.Active ? "Active" : "Inactive")) { Width = 11, Sortable = true, Style = (p, t) => StatusBadge.Style(p.Active ? "Active" : "Inactive", t) });
            grid.AddColumn(new GridColumn<ProjectProfile>("updated", "Last Updated", p => EntityUi.When(Context, p.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<ProjectProfile>("description", "Description", p => EntityUi.Dash(p.Description)) { Weight = 3, DefaultVisible = false });
            grid.AddColumn(new GridColumn<ProjectProfile>("id", "ID", p => p.Id) { Width = 28, DefaultVisible = false });
            grid.EmptyText = "No project profiles match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by name, description, or ID...");
            filters.AddSelect("scope", "All scopes", ProfileFormSupport.ScopeOptions(Context), 16);
            filters.AddSelect("status", "All statuses", ProfileFormSupport.StatusOptions(Context), 18);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<ProjectProfile>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("search", EntityUi.Blank(Filter("search")));
            q.With("scope", EntityUi.Blank(Filter("scope")));
            string status = Filter("status");
            q.With("active", status == "active" ? "true" : status == "inactive" ? "false" : null);
            EnumerationResult<ProjectProfile>? result = await Context.Client.ListProjectProfilesAsync(q, token).ConfigureAwait(false);
            return PageOf(result);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(ProjectProfile row, string sortKey)
        {
            switch (sortKey)
            {
                case "updated": return row.LastUpdateUtc;
                case "overrides": return row.PersonaOverrides?.Count ?? 0;
                case "skills": return row.Skills?.Count ?? 0;
                case "scope": return (int)row.Scope;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            List<ProjectProfile> all = await ArmadaPaging.ReadAllAsync<ProjectProfile>((p, ct) => Context.Client.ListProjectProfilesAsync(new ArmadaPageQuery(p, EntityLookups.PageSize), ct), 20, token).ConfigureAwait(false);
            return new List<KpiItem>
            {
                new KpiItem("Total Profiles", EntityUi.Number(Context, all.Count)),
                new KpiItem("Active", EntityUi.Number(Context, all.Count(p => p.Active)), t => t.Success),
                new KpiItem("Defaults", EntityUi.Number(Context, all.Count(p => p.IsDefault))),
                new KpiItem("Persona Overrides", EntityUi.Number(Context, all.Sum(p => p.PersonaOverrides?.Count ?? 0)))
            };
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            ProjectProfileForms.Open(Context, null, p => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(ProjectProfile row)
        {
            if (!CanEdit(row)) return;
            ProjectProfileForms.Open(Context, row, p => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(ProjectProfile row, CancellationToken token)
        {
            return Context.Client.DeleteProjectProfileAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(ProjectProfile row)
        {
            return EntityUi.T(Context, "Delete \"{{name}}\"? Projects using it will fall back to their fleet or global profile.", "name", row.Name);
        }

        /// <inheritdoc />
        protected override string DeletedText(ProjectProfile row)
        {
            return EntityUi.T(Context, "Project profile \"{{name}}\" deleted.", "name", row.Name);
        }

        #endregion
    }
}
