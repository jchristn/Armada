namespace Armada.Tui.Screens.Configuration
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
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Configuration, Skills tab (dashboard <c>Skills.tsx</c>): overview (total, active, inactive, categories),
    /// filters (search and category, sent to the server), the skills grid (skill, category, visibility, status, last
    /// updated), row actions (Open, Edit, View JSON, Delete), and the create/edit form. Anyone may create a personal
    /// skill; editing and deleting follow the scoping rules.
    /// </summary>
    public class SkillsScreen : EntityListScreen<Skill>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Skill"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public SkillsScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Skill row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Skill row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override string? DetailPath(Skill row)
        {
            return "/skills/" + row.Id;
        }

        /// <inheritdoc />
        protected override bool CanCreate
        {
            get { return Context.Session.IsSignedIn; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(Skill row)
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
            get { return "Create Skill"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Skill> grid)
        {
            grid.AddColumn(new GridColumn<Skill>("name", "Skill", s => s.Name) { Weight = 3, MinWidth = 14, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Skill>("category", "Category", s => EntityUi.Dash(s.Category)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Skill>("description", "Description", s => EntityUi.Dash(s.Description)) { Weight = 3, Sortable = true, DefaultVisible = false });
            grid.AddColumn(new GridColumn<Skill>("visibility", "Visibility", s => T(ScopeRules.Label(s.Scope))) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<Skill>("status", "Status", s => StatusBadge.Label(s.Active ? "Active" : "Inactive")) { Width = 11, Sortable = true, Style = (s, t) => StatusBadge.Style(s.Active ? "Active" : "Inactive", t) });
            grid.AddColumn(new GridColumn<Skill>("updated", "Last Updated", s => EntityUi.When(Context, s.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<Skill>("id", "ID", s => s.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No skills match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by name, description, or ID...");
            List<SelectOption<string>> seed = new List<SelectOption<string>>();
            if (Route.Query.TryGetValue("category", out string? linked) && !String.IsNullOrEmpty(linked)) seed.Add(new SelectOption<string>(linked, linked));
            filters.AddSelect("category", "All categories", seed, 22);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Skill>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("search", EntityUi.Blank(Filter("search")));
            q.With("category", EntityUi.Blank(Filter("category")));
            EnumerationResult<Skill>? result = await Context.Client.ListSkillsAsync(q, token).ConfigureAwait(false);
            return PageOf(result);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Skill row, string sortKey)
        {
            if (sortKey == "updated") return row.LastUpdateUtc;
            return null;
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            List<Skill> all = await ArmadaPaging.ReadAllAsync<Skill>((p, ct) => Context.Client.ListSkillsAsync(new ArmadaPageQuery(p, EntityLookups.PageSize), ct), 20, token).ConfigureAwait(false);
            List<string> categories = all.Where(s => !String.IsNullOrEmpty(s.Category)).Select(s => s.Category!).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList();
            List<SelectOption<string>> options = categories.Select(c => new SelectOption<string>(c, c)).ToList();
            Context.Dispatcher.Post(() => Filters.SetOptions("category", options));
            return new List<KpiItem>
            {
                new KpiItem("Total Skills", EntityUi.Number(Context, all.Count)),
                new KpiItem("Active", EntityUi.Number(Context, all.Count(s => s.Active)), t => t.Success),
                new KpiItem("Inactive", EntityUi.Number(Context, all.Count(s => !s.Active)), t => t.Muted),
                new KpiItem("Categories", EntityUi.Number(Context, categories.Count))
            };
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            SkillForms.Open(Context, null, s => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(Skill row)
        {
            if (!CanEdit(row)) return;
            SkillForms.Open(Context, row, s => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Skill row, CancellationToken token)
        {
            return Context.Client.DeleteSkillAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Skill row)
        {
            return EntityUi.T(Context, "Delete \"{{name}}\"? Project profiles referencing it will simply skip it.", "name", row.Name);
        }

        /// <inheritdoc />
        protected override string DeletedText(Skill row)
        {
            return EntityUi.T(Context, "Skill \"{{name}}\" deleted.", "name", row.Name);
        }

        #endregion
    }
}
