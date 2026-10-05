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
    /// Configuration, Playbooks tab (dashboard <c>Playbooks.tsx</c>): overview (total, active, inactive, stored
    /// Markdown), filters (search and status; the playbooks API has no search, so they apply locally over the full
    /// list), the grid (file, description, visibility, status, content size, last updated), row actions (Open,
    /// Edit, Duplicate, View JSON, Delete), and the create/edit form. Anyone may create a personal playbook; editing
    /// and deleting follow the scoping rules.
    /// </summary>
    public class PlaybooksScreen : EntityListScreen<Playbook>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Playbook"; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public PlaybooksScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string IdOf(Playbook row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Playbook row)
        {
            return row.FileName;
        }

        /// <inheritdoc />
        protected override string? DetailPath(Playbook row)
        {
            return "/playbooks/" + row.Id;
        }

        /// <inheritdoc />
        protected override bool CanCreate
        {
            get { return Context.Session.IsSignedIn; }
        }

        /// <inheritdoc />
        protected override bool CanEdit(Playbook row)
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
            get { return "Create Playbook"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Playbook> grid)
        {
            grid.AddColumn(new GridColumn<Playbook>("fileName", "File", p => p.FileName) { Weight = 3, MinWidth = 14, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Playbook>("description", "Description", p => EntityUi.Dash(p.Description)) { Weight = 3, Sortable = true });
            grid.AddColumn(new GridColumn<Playbook>("visibility", "Visibility", p => T(ScopeRules.Label(p.Scope))) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<Playbook>("status", "Status", p => StatusBadge.Label(p.Active ? "Active" : "Inactive")) { Width = 11, Sortable = true, Style = (p, t) => StatusBadge.Style(p.Active ? "Active" : "Inactive", t) });
            grid.AddColumn(new GridColumn<Playbook>("content", "Content", p => Localizer.FormatNumber((p.Content ?? "").Length) + " " + T("chars")) { Width = 14, Sortable = true, Align = TUIKit.Widgets.CellAlignment.Right });
            grid.AddColumn(new GridColumn<Playbook>("updated", "Last Updated", p => EntityUi.When(Context, p.LastUpdateUtc)) { Width = 13, Sortable = true });
            grid.AddColumn(new GridColumn<Playbook>("id", "ID", p => p.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No playbooks match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by filename, description, or ID...");
            filters.AddSelect("status", "All statuses", new List<SelectOption<string>>
            {
                new SelectOption<string>("active", T("Active only")),
                new SelectOption<string>("inactive", T("Inactive only"))
            }, 18);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Playbook>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            List<Playbook> all = await ReadAllAsync(token).ConfigureAwait(false);
            string search = Filter("search");
            string status = Filter("status");
            List<Playbook> filtered = all.Where(p => Matches(search, p.FileName, p.Description, p.Id)
                && (status == "" || (status == "active" && p.Active) || (status == "inactive" && !p.Active))).ToList();
            return Slice(filtered, query);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Playbook row, string sortKey)
        {
            if (sortKey == "updated") return row.LastUpdateUtc;
            if (sortKey == "content") return (row.Content ?? "").Length;
            return null;
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            List<Playbook> all = await ReadAllAsync(token).ConfigureAwait(false);
            long chars = all.Sum(p => (long)(p.Content ?? "").Length);
            return new List<KpiItem>
            {
                new KpiItem("Total Playbooks", EntityUi.Number(Context, all.Count)),
                new KpiItem("Active", EntityUi.Number(Context, all.Count(p => p.Active)), t => t.Success),
                new KpiItem("Inactive", EntityUi.Number(Context, all.Count(p => !p.Active)), t => t.Muted),
                new KpiItem("Stored Markdown", EntityUi.Number(Context, chars) + " " + T("chars"))
            };
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(Playbook row)
        {
            List<ActionMenuItem> items = base.RowActions(row);
            int json = items.FindIndex(i => i.Key == ActionMenuItem.JsonKey);
            items.Insert(json < 0 ? items.Count : json, new ActionMenuItem("Duplicate", () => PlaybookForms.Duplicate(Context, row, true)));
            return items;
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            PlaybookForms.Open(Context, null, p => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(Playbook row)
        {
            if (!CanEdit(row)) return;
            PlaybookForms.Open(Context, row, p => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Playbook row, CancellationToken token)
        {
            return Context.Client.DeletePlaybookAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Playbook row)
        {
            return EntityUi.T(Context, "Delete \"{{name}}\"? Existing mission snapshots will remain, but this playbook will no longer be selectable.", "name", row.FileName);
        }

        /// <inheritdoc />
        protected override string DeletedText(Playbook row)
        {
            return EntityUi.T(Context, "Playbook \"{{name}}\" deleted.", "name", row.FileName);
        }

        #endregion

        #region Private-Methods

        private Task<List<Playbook>> ReadAllAsync(CancellationToken token)
        {
            return ArmadaPaging.ReadAllAsync<Playbook>((p, ct) => Context.Client.ListPlaybooksAsync(new ArmadaPageQuery(p, EntityLookups.PageSize), ct), 20, token);
        }

        #endregion
    }
}
