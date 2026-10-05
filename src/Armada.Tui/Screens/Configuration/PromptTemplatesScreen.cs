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
    /// Configuration, Prompts tab (dashboard <c>PromptTemplates.tsx</c>): the category pills as a category filter
    /// (deep link <c>?category=persona</c>), name and description filters, the templates grid (name, description,
    /// category, visibility, built-in, content length, active, last updated; sortable, default category ascending),
    /// row actions (Edit or Open, Duplicate, View JSON, Copy ID, Reset to Default for built-ins), and New Prompt
    /// Template (<c>n</c>, opens <c>/prompt-templates/create</c>). The server only pages, so the screen reads every
    /// template and filters and sorts locally.
    /// </summary>
    public class PromptTemplatesScreen : EntityListScreen<PromptTemplate>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Prompt Template"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public PromptTemplatesScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(PromptTemplate row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(PromptTemplate row)
        {
            return row.Name;
        }

        /// <inheritdoc />
        protected override string? DetailPath(PromptTemplate row)
        {
            return "/prompt-templates/" + Uri.EscapeDataString(row.Name);
        }

        /// <inheritdoc />
        protected override bool HasCreate
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
            get { return "New Prompt Template"; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(PromptTemplate row)
        {
            return ScopeRules.CanEdit(Context.Session, row.Scope, row.TenantId, row.UserId);
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<PromptTemplate> grid)
        {
            grid.AddColumn(new GridColumn<PromptTemplate>("name", "Name", p => p.Name) { Weight = 3, MinWidth = 14, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<PromptTemplate>("description", "Description", p => EntityUi.Dash(p.Description)) { Weight = 4, Sortable = true });
            grid.AddColumn(new GridColumn<PromptTemplate>("category", "Category", p => T(p.Category)) { Width = 11, Sortable = true });
            grid.AddColumn(new GridColumn<PromptTemplate>("visibility", "Visibility", p => T(ScopeRules.Label(p.Scope))) { Width = 12 });
            grid.AddColumn(new GridColumn<PromptTemplate>("isBuiltIn", "Built-in", p => p.IsBuiltIn ? "+ " + T("Built-in") : "-") { Width = 10, Sortable = true });
            grid.AddColumn(new GridColumn<PromptTemplate>("contentLength", "Content Length", p => EntityUi.Number(Context, (p.Content ?? "").Length) + " " + T("chars")) { Width = 15, Sortable = true, Align = TUIKit.Widgets.CellAlignment.Right });
            grid.AddColumn(new GridColumn<PromptTemplate>("active", "Active", p => StatusBadge.Label(p.Active ? "Active" : "Inactive")) { Width = 11, Sortable = true, Style = (p, t) => StatusBadge.Style(p.Active ? "Active" : "Inactive", t) });
            grid.AddColumn(new GridColumn<PromptTemplate>("lastUpdateUtc", "Last Updated", p => EntityUi.When(Context, p.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<PromptTemplate>("id", "ID", p => p.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No prompt templates match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSelect("category", "All", PromptParameter.Categories.Select(c => new SelectOption<string>(c, T(c))), 14);
            filters.AddSearch("name", "Filter by name...", 26);
            filters.AddSearch("description", "Filter by description...", 30);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<PromptTemplate>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            List<PromptTemplate> all = await ArmadaPaging.ReadAllAsync<PromptTemplate>((p, ct) => Context.Client.ListPromptTemplatesAsync(new ArmadaPageQuery(p, 500), ct), 20, token).ConfigureAwait(false);
            string category = Filter("category");
            List<PromptTemplate> filtered = all.Where(p => Matches(Filter("name"), p.Name)
                && Matches(Filter("description"), p.Description)
                && (category.Length == 0 || String.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Slice(filtered, query);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(PromptTemplate row, string sortKey)
        {
            switch (sortKey)
            {
                case "name": return row.Name.ToLowerInvariant();
                case "description": return (row.Description ?? "").ToLowerInvariant();
                case "category": return row.Category.ToLowerInvariant();
                case "isBuiltIn": return row.IsBuiltIn ? 1 : 0;
                case "contentLength": return (row.Content ?? "").Length;
                case "active": return row.Active ? 1 : 0;
                case "lastUpdateUtc": return row.LastUpdateUtc;
                default: return null;
            }
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(PromptTemplate row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem(CanEdit(row) ? "Edit" : "Open", () => Open(row), "Enter"));
            items.Add(new ActionMenuItem("Duplicate", () => PromptTemplateActions.Duplicate(Context, row)));
            items.Add(new ActionMenuItem("View JSON", () => EntityUi.ShowJson(Context, T("Template") + ": " + row.Name, row), "j"));
            items.Add(new ActionMenuItem("Copy ID", () => Context.Clipboard.Copy(row.Id, "ID"), "y"));
            if (row.IsBuiltIn && CanEdit(row))
            {
                ActionMenuItem reset = new ActionMenuItem("Reset to Default", () => PromptTemplateActions.Reset(Context, row.Name, false, r => Reload()));
                reset.Destructive = true;
                items.Add(reset);
            }

            return items;
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            Context.Navigate("/prompt-templates/create");
        }

        #endregion
    }
}
