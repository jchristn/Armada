namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Configuration, Memory tab (dashboard <c>Memories.tsx</c>): filters (type Episodic, Semantic, or Procedural, and
    /// search; both sent to the server), the server-paged grid (type, topic, summary, salience, vessel, visibility,
    /// updated), Enter or <c>j</c> for View JSON, Copy ID, and Delete with confirmation. Extension beyond the
    /// dashboard: <c>n</c> creates and <c>e</c> edits a memory (createMemory and updateMemory).
    /// </summary>
    public class MemoryScreen : EntityListScreen<Memory>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Memory"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public MemoryScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Open(Memory row)
        {
            EntityUi.ShowJson(Context, "Memory: " + row.Id, row);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Memory row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Memory row)
        {
            return String.IsNullOrEmpty(row.Topic) ? row.Id : row.Topic!;
        }

        /// <inheritdoc />
        protected override bool CanCreate
        {
            get { return Context.Session.IsSignedIn; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(Memory row)
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
            get { return "Create Memory"; }
        }

        /// <inheritdoc />
        protected override string DeleteTitle
        {
            get { return "Delete Memory"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Memory> grid)
        {
            grid.AddColumn(new GridColumn<Memory>("type", "Type", m => T(m.Type.ToString())) { Width = 11, Sortable = true });
            grid.AddColumn(new GridColumn<Memory>("topic", "Topic", m => EntityUi.Dash(m.Topic)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Memory>("summary", "Summary", m => Summary(m)) { Weight = 5, MinWidth = 16, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Memory>("salience", "Salience", m => m.Salience.ToString("0.00", CultureInfo.InvariantCulture)) { Width = 9, Sortable = true, Align = TUIKit.Widgets.CellAlignment.Right });
            grid.AddColumn(new GridColumn<Memory>("vessel", "Vessel", m => EntityUi.Dash(m.VesselId ?? m.SourceVesselId)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Memory>("visibility", "Visibility", m => T(ScopeRules.Label(m.Scope))) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<Memory>("updated", "Updated", m => EntityUi.When(Context, m.LastUpdateUtc)) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<Memory>("id", "ID", m => m.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No memories recorded yet.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSelect("type", "All types", Enum.GetNames(typeof(MemoryTypeEnum)).Select(n => new SelectOption<string>(n, T(n))).ToList(), 16);
            filters.AddSearch("search", "Search content, topic, tags...");
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Memory>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            q.With("type", EntityUi.Blank(Filter("type")));
            q.With("search", EntityUi.Blank(Filter("search")));
            EnumerationResult<Memory>? result = await Context.Client.ListMemoriesAsync(q, token).ConfigureAwait(false);
            return PageOf(result);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Memory row, string sortKey)
        {
            if (sortKey == "salience") return row.Salience;
            if (sortKey == "updated") return row.LastUpdateUtc;
            return null;
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(Memory row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("View JSON", () => Open(row), "Enter"));
            if (CanEdit(row)) items.Add(new ActionMenuItem("Edit", () => OpenEdit(row), "e"));
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
            MemoryForms.Open(Context, null, m => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(Memory row)
        {
            if (!CanEdit(row)) return;
            MemoryForms.Open(Context, row, m => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Memory row, CancellationToken token)
        {
            return Context.Client.DeleteMemoryAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Memory row)
        {
            return T("Delete this memory? This cannot be undone.");
        }

        /// <inheritdoc />
        protected override string DeletedText(Memory row)
        {
            return T("Memory deleted.");
        }

        #endregion

        #region Private-Methods

        private static string Summary(Memory m)
        {
            if (!String.IsNullOrEmpty(m.Summary)) return m.Summary!;
            string content = (m.Content ?? "").Replace('\n', ' ');
            return content.Length > 80 ? content.Substring(0, 80) + "..." : content;
        }

        #endregion
    }
}
