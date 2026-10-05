namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Settings, Tenants tab (dashboard <c>admin/Tenants.tsx</c>). Global admins list every tenant (select, name, id,
    /// active, created, last updated) with name search, create (name), edit (name, active), delete and bulk delete
    /// with a typed "delete" confirmation, and View JSON. Other users see their own tenant read-only. Writes are
    /// blocked behind Armada.Proxy. Not thread-safe.
    /// </summary>
    public class TenantsScreen : GridScreen<TenantMetadata>
    {
        #region Public-Members

        /// <summary>
        /// Filters.
        /// </summary>
        public FilterStrip Filters { get; } = new FilterStrip();

        /// <summary>
        /// Name search.
        /// </summary>
        public InputField NameFilter { get; } = new InputField();

        /// <summary>
        /// All tenants loaded (before the search).
        /// </summary>
        public IReadOnlyList<TenantMetadata> Items
        {
            get { return _Items; }
        }

        #endregion

        #region Private-Members

        private readonly Button _DeleteSelected;
        private readonly Button _Create;
        private List<TenantMetadata> _Items = new List<TenantMetadata>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public TenantsScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Tenants", context.Session.IsGlobalAdmin ? "Manage tenants in the system. Each tenant is an isolated organizational unit." : "View your tenant information.", "tenants", t => t.Id)
        {
            _DeleteSelected = Header.AddButton("Delete Selected", OnDeleteSelected, "Del");
            _Create = Header.AddButton("+ Tenant", OnCreate, "n");
            Header.AddButton("Refresh", Refresh, "F5");
            AddFixed(Header, w => Header.HeightFor(w));
            NameFilter.Placeholder = "Search...";
            NameFilter.ValueChanged += (s, e) => Apply();
            Filters.Add("Name", NameFilter, 24);
            AddFixed(Filters, w => Filters.HeightFor(w));
            Grid.AddColumn(new GridColumn<TenantMetadata>("createdIso", "Created (UTC)", t => UserAdminOps.SortStamp(t.CreatedUtc)) { Width = 20, Sortable = true, DefaultVisible = false });
            Grid.AddColumn(new GridColumn<TenantMetadata>("name", "Name", t => t.Name) { Weight = 2, Sortable = true, SortKey = "name" });
            Grid.AddColumn(new GridColumn<TenantMetadata>("id", "ID", t => t.Id) { Width = 28, Style = (t, th) => th.Muted });
            Grid.AddColumn(new GridColumn<TenantMetadata>("active", "Active", t => UserAdminOps.YesNo(Context, t.Active)) { Width = 8, Sortable = true });
            Grid.AddColumn(new GridColumn<TenantMetadata>("created", "Created", t => ScreenOps.Relative(Context, t.CreatedUtc)) { Width = 12, Sortable = true, SortKey = "createdIso", Style = (t, th) => th.Muted });
            Grid.AddColumn(new GridColumn<TenantMetadata>("updated", "Last Updated", t => ScreenOps.Relative(Context, t.LastUpdateUtc)) { Width = 14, Style = (t, th) => th.Muted });
            Grid.EmptyText = "No tenants found.";
            BindPreferences("name", false, 25);
            AddFill(Grid);
            Scope.Focus(Grid);
            Banner = UserAdminOps.RemoteBanner(context, "Tenant");
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the user may create, edit, and delete tenants (global admin outside proxy mode).
        /// </summary>
        public bool CanWrite
        {
            get { return Context.Session.IsGlobalAdmin && UserAdminOps.RemoteInstance(Context) == null; }
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            Load();
        }

        /// <summary>
        /// Load tenants (all for admins, the user's own tenant otherwise).
        /// </summary>
        public void Load()
        {
            if (!Context.Session.IsGlobalAdmin)
            {
                TenantMetadata? own = Context.Session.Identity?.Tenant;
                _Items = own != null ? new List<TenantMetadata> { own } : new List<TenantMetadata>();
                Apply();
                return;
            }

            Grid.SetLoading();
            ScreenOps.Run(Context, () => Context.Client.ListTenantsAsync(), r =>
            {
                _Items = r?.Objects ?? new List<TenantMetadata>();
                Apply();
            }, "Failed to load tenants.", ex => Grid.SetError(Context.Loc.T("Failed to load tenants.")));
        }

        /// <summary>
        /// Open the create (null) or edit form.
        /// </summary>
        /// <param name="editing">Tenant to edit, or null.</param>
        /// <returns>The modal, or null when not allowed.</returns>
        public FormModal? OpenForm(TenantMetadata? editing)
        {
            if (!CanWrite) return null;
            FormView form = new FormView();
            InputField name = form.AddField("Name", new InputField());
            name.Value = editing?.Name ?? "";
            name.Validator = v => String.IsNullOrWhiteSpace(v) ? "Name is required." : null;
            ToggleField active = new ToggleField(editing?.Active ?? true);
            if (editing != null) form.AddField("Active", active);
            form.MarkClean();
            FormModal modal = new FormModal(editing != null ? "Edit Tenant" : "Create Tenant", form, Context, "Save");
            modal.SubmitAsync = async () =>
            {
                TenantMetadata body = new TenantMetadata(name.Value.Trim());
                body.Active = active.Value;
                if (editing != null) await Context.Client.UpdateTenantAsync(editing.Id, body).ConfigureAwait(false);
                else await Context.Client.CreateTenantAsync(body).ConfigureAwait(false);
                return null;
            };
            Context.Modals.Show(modal, result =>
            {
                if (!(result is bool ok) || !ok) return;
                ScreenOps.Toast(Context, NotificationSeverityEnum.Success, editing != null ? "Tenant \"{{name}}\" saved." : "Tenant \"{{name}}\" created.", LocalizationArgs.Of("name", name.Value.Trim()));
                Load();
            });
            return modal;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            _DeleteSelected.Visible = CanWrite && Grid.Marked.Count > 0;
            _DeleteSelected.Hint = "(" + Grid.Marked.Count + ")";
            _Create.Visible = CanWrite;
        }

        /// <inheritdoc />
        protected override void OnActivate(TenantMetadata row)
        {
            if (CanWrite) OpenForm(row);
            else ScreenOps.ShowJson(Context, JsonTitle(row), row);
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(TenantMetadata row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            if (CanWrite) items.Add(new ActionMenuItem("Edit", () => OpenForm(row), "Enter"));
            items.Add(new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), row), "j"));
            if (CanWrite)
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => OnDeleteRow(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <inheritdoc />
        protected override string JsonTitle(TenantMetadata row)
        {
            return "Tenant: " + row.Name;
        }

        /// <inheritdoc />
        protected override bool SupportsCreate()
        {
            return true;
        }

        /// <inheritdoc />
        protected override bool CanCreate()
        {
            return CanWrite;
        }

        /// <inheritdoc />
        protected override void OnCreate()
        {
            if (CanWrite) OpenForm(null);
        }

        /// <inheritdoc />
        protected override bool SupportsDelete()
        {
            return true;
        }

        /// <inheritdoc />
        protected override bool CanDeleteRows()
        {
            return CanWrite && base.CanDeleteRows();
        }

        /// <inheritdoc />
        protected override void OnDeleteRow(TenantMetadata row)
        {
            if (!CanWrite) return;
            string id = row.Id;
            string name = row.Name;
            Context.Confirm("Delete Tenant", Context.Loc.T("Delete tenant \"{{name}}\"? This is destructive and cannot be undone.", LocalizationArgs.Of("name", name)), () =>
            {
                ScreenOps.RunVoid(Context, () => Context.Client.DeleteTenantAsync(id), () =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Tenant \"{{name}}\" deleted.", LocalizationArgs.Of("name", name));
                    Load();
                }, "Delete failed.");
            }, "Delete", "delete");
        }

        /// <inheritdoc />
        protected override void OnDeleteSelected()
        {
            if (!CanWrite) return;
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Context.Confirm("Delete Selected Tenants", Context.Loc.T("Delete {{count}} tenant(s)? This cannot be undone.", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                UserAdminOps.BulkDelete(Context, ids, id => Context.Client.DeleteTenantAsync(id), "tenants", Load);
            }, "Delete", "delete");
        }

        #endregion

        #region Private-Methods

        private void Apply()
        {
            string search = NameFilter.Value.Trim();
            Grid.EmptyText = _Items.Count == 0 ? "No tenants found." : "No tenants match filters.";
            Grid.SetLocalRows(_Items.Where(t => search.Length == 0 || (t.Name ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        #endregion
    }
}
