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
    /// Configuration, Pipelines tab (dashboard <c>Pipelines.tsx</c>): the pipelines grid (name, ID, description, stage
    /// chain, visibility, built-in, active, created; sortable, default name ascending), name and description filters,
    /// row actions (View Detail, Edit, Duplicate, View JSON, Copy ID, Delete), and the create/edit form with its stage
    /// list. Editing follows the scoping rules; built-in pipelines cannot be deleted. The server only pages, so the
    /// screen reads every pipeline and filters and sorts locally.
    /// </summary>
    public class PipelinesScreen : EntityListScreen<Pipeline>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Pipeline"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public PipelinesScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Pipeline row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Pipeline row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override string? DetailPath(Pipeline row)
        {
            return "/pipelines/" + Uri.EscapeDataString(row.Name);
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
        protected override string CreateLabel
        {
            get { return "Create Pipeline"; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(Pipeline row)
        {
            return ScopeRules.CanEdit(Context.Session, row.Scope, row.TenantId, row.UserId);
        }

        /// <inheritdoc />
        protected override bool CanDelete(Pipeline row)
        {
            return !row.IsBuiltIn && CanEdit(row);
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Pipeline> grid)
        {
            grid.AddColumn(new GridColumn<Pipeline>("name", "Name", p => p.Name) { Weight = 2, MinWidth = 12, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Pipeline>("id", "ID", p => p.Id) { Width = 26 });
            grid.AddColumn(new GridColumn<Pipeline>("description", "Description", p => EntityUi.Dash(p.Description)) { Weight = 3, Sortable = true });
            grid.AddColumn(new GridColumn<Pipeline>("stages", "Stages", p => PipelineForms.Chain(p.Stages)) { Weight = 3, Sortable = true });
            grid.AddColumn(new GridColumn<Pipeline>("visibility", "Visibility", p => T(ScopeRules.Label(p.Scope))) { Width = 12 });
            grid.AddColumn(new GridColumn<Pipeline>("isBuiltIn", "Built-in", p => p.IsBuiltIn ? "[x]" : "-") { Width = 9, Sortable = true });
            grid.AddColumn(new GridColumn<Pipeline>("active", "Active", p => p.Active ? "[x]" : "x") { Width = 7, Sortable = true, Style = (p, t) => p.Active ? t.Success : t.Error });
            grid.AddColumn(new GridColumn<Pipeline>("createdUtc", "Created", p => EntityUi.When(Context, p.CreatedUtc)) { Width = 12, Sortable = true });
            grid.EmptyText = "No pipelines match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("name", "Search by name...", 26);
            filters.AddSearch("description", "Search by description...", 30);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Pipeline>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            List<Pipeline> all = await ArmadaPaging.ReadAllAsync<Pipeline>((p, ct) => Context.Client.ListPipelinesAsync(new ArmadaPageQuery(p, 500), ct), 20, token).ConfigureAwait(false);
            List<Pipeline> filtered = all.Where(p => Matches(Filter("name"), p.Name) && Matches(Filter("description"), p.Description))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Slice(filtered, query);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Pipeline row, string sortKey)
        {
            switch (sortKey)
            {
                case "name": return row.Name.ToLowerInvariant();
                case "description": return (row.Description ?? "").ToLowerInvariant();
                case "stages": return (row.Stages ?? new List<PipelineStage>()).Count;
                case "isBuiltIn": return row.IsBuiltIn ? 1 : 0;
                case "active": return row.Active ? 1 : 0;
                case "createdUtc": return row.CreatedUtc;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(Pipeline row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("View Detail", () => Open(row), "Enter"));
            if (CanEdit(row)) items.Add(new ActionMenuItem("Edit", () => OpenEdit(row), "e"));
            items.Add(new ActionMenuItem("Duplicate", () => PipelineForms.Duplicate(Context, row)));
            items.Add(new ActionMenuItem("View JSON", () => EntityUi.ShowJson(Context, "Pipeline: " + row.Name, row), "j"));
            items.Add(new ActionMenuItem("Copy ID", () => Context.Clipboard.Copy(row.Id, "ID"), "y"));
            if (CanDelete(row))
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => RequestDelete(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            PipelineForms.Open(Context, null, false, name => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(Pipeline row)
        {
            if (!CanEdit(row)) return;
            PipelineForms.Open(Context, row, false, name => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Pipeline row, CancellationToken token)
        {
            return Context.Client.DeletePipelineAsync(row.Name, token);
        }

        /// <inheritdoc />
        protected override string DeleteTitle
        {
            get { return "Delete Pipeline"; }
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Pipeline row)
        {
            return EntityUi.T(Context, "Delete pipeline \"{{name}}\"? This cannot be undone.", "name", row.Name);
        }

        /// <inheritdoc />
        protected override string DeletedText(Pipeline row)
        {
            return EntityUi.T(Context, "Pipeline \"{{name}}\" deleted.", "name", row.Name);
        }

        #endregion
    }
}
