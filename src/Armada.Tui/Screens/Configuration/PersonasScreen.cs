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
    /// Configuration, Personas tab (dashboard <c>Personas.tsx</c>): the personas grid (name, ID, description, prompt
    /// template, visibility, built-in, active, created; every column but ID and visibility sortable, default name
    /// ascending), name/description/template filters, row actions (View Detail, Edit, Duplicate, Edit Backing Prompt,
    /// View JSON, Copy ID, Delete), and the create/edit form. Editing follows the scoping rules; built-in personas
    /// cannot be deleted. The server only pages, so the screen reads every persona and filters and sorts locally.
    /// </summary>
    public class PersonasScreen : EntityListScreen<Persona>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Persona"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public PersonasScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Persona row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Persona row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override string? DetailPath(Persona row)
        {
            return "/personas/" + Uri.EscapeDataString(row.Name);
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
            get { return "Create Persona"; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(Persona row)
        {
            return ScopeRules.CanEdit(Context.Session, row.Scope, row.TenantId, row.UserId);
        }

        /// <inheritdoc />
        protected override bool CanDelete(Persona row)
        {
            return !row.IsBuiltIn && CanEdit(row);
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Persona> grid)
        {
            grid.AddColumn(new GridColumn<Persona>("name", "Name", p => p.Name) { Weight = 2, MinWidth = 12, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Persona>("id", "ID", p => p.Id) { Width = 26 });
            grid.AddColumn(new GridColumn<Persona>("description", "Description", p => EntityUi.Dash(p.Description)) { Weight = 4, Sortable = true });
            grid.AddColumn(new GridColumn<Persona>("promptTemplateName", "Prompt Template", p => p.PromptTemplateName) { Weight = 2, MinWidth = 16, Sortable = true });
            grid.AddColumn(new GridColumn<Persona>("visibility", "Visibility", p => T(ScopeRules.Label(p.Scope))) { Width = 12 });
            grid.AddColumn(new GridColumn<Persona>("isBuiltIn", "Built-in", p => p.IsBuiltIn ? "+ " + T("Built-in") : "-") { Width = 10, Sortable = true });
            grid.AddColumn(new GridColumn<Persona>("active", "Active", p => StatusBadge.Label(p.Active ? "Active" : "Inactive")) { Width = 11, Sortable = true, Style = (p, t) => StatusBadge.Style(p.Active ? "Active" : "Inactive", t) });
            grid.AddColumn(new GridColumn<Persona>("createdUtc", "Created", p => EntityUi.When(Context, p.CreatedUtc)) { Width = 12, Sortable = true });
            grid.EmptyText = "No personas match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("name", "Filter by name...", 24);
            filters.AddSearch("description", "Filter by description...", 26);
            filters.AddSearch("template", "Filter by prompt template...", 28);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Persona>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            List<Persona> all = await ArmadaPaging.ReadAllAsync<Persona>((p, ct) => Context.Client.ListPersonasAsync(new ArmadaPageQuery(p, 500), ct), 20, token).ConfigureAwait(false);
            List<Persona> filtered = all.Where(p => Matches(Filter("name"), p.Name)
                && Matches(Filter("description"), p.Description)
                && Matches(Filter("template"), p.PromptTemplateName))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Slice(filtered, query);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Persona row, string sortKey)
        {
            switch (sortKey)
            {
                case "name": return row.Name.ToLowerInvariant();
                case "description": return (row.Description ?? "").ToLowerInvariant();
                case "promptTemplateName": return row.PromptTemplateName.ToLowerInvariant();
                case "isBuiltIn": return row.IsBuiltIn ? 1 : 0;
                case "active": return row.Active ? 1 : 0;
                case "createdUtc": return row.CreatedUtc;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(Persona row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("View Detail", () => Open(row), "Enter"));
            if (CanEdit(row)) items.Add(new ActionMenuItem("Edit", () => OpenEdit(row), "e"));
            items.Add(new ActionMenuItem("Duplicate", () => PersonaForms.Duplicate(Context, row)));
            items.Add(new ActionMenuItem("Edit Backing Prompt", () => Context.Navigate("/prompt-templates/" + Uri.EscapeDataString(row.PromptTemplateName))));
            items.Add(new ActionMenuItem("View JSON", () => EntityUi.ShowJson(Context, T("Persona") + ": " + row.Name, row), "j"));
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
            PersonaForms.OpenListForm(Context, null, Reload);
        }

        /// <inheritdoc />
        protected override void OpenEdit(Persona row)
        {
            if (!CanEdit(row)) return;
            PersonaForms.OpenListForm(Context, row, Reload);
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Persona row, CancellationToken token)
        {
            return Context.Client.DeletePersonaAsync(row.Name, token);
        }

        /// <inheritdoc />
        protected override string DeleteTitle
        {
            get { return "Delete Persona"; }
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Persona row)
        {
            return EntityUi.T(Context, "Delete persona \"{{name}}\"? This cannot be undone.", "name", row.Name);
        }

        /// <inheritdoc />
        protected override string DeletedText(Persona row)
        {
            return EntityUi.T(Context, "Persona \"{{name}}\" deleted.", "name", row.Name);
        }

        #endregion
    }
}
