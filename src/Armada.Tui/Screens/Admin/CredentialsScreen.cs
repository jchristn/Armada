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
    /// Settings, Credentials tab (dashboard <c>admin/Credentials.tsx</c>): API bearer tokens with name, id, user,
    /// tenant, masked token, active, and created; name search and user and tenant filters; create (user, tenant for
    /// global admins, optional name) which shows the new token once with copy; edit (name, active); Copy Token,
    /// View JSON, delete and bulk delete with a typed "delete" confirmation. Writes are blocked behind
    /// Armada.Proxy. Not thread-safe.
    /// </summary>
    public class CredentialsScreen : GridScreen<Credential>
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
        /// User filter ("" for all users).
        /// </summary>
        public SelectField<string> UserFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Tenant filter ("" for all tenants).
        /// </summary>
        public SelectField<string> TenantFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Token of the credential created last (shown once), or null.
        /// </summary>
        public string? NewToken { get; private set; } = null;

        /// <summary>
        /// Credentials loaded (before filters).
        /// </summary>
        public IReadOnlyList<Credential> Items
        {
            get { return _Items; }
        }

        /// <summary>
        /// True outside proxy mode.
        /// </summary>
        public bool CanWrite
        {
            get { return UserAdminOps.RemoteInstance(Context) == null; }
        }

        #endregion

        #region Private-Members

        private readonly Button _DeleteSelected;
        private readonly Button _Create;
        private List<Credential> _Items = new List<Credential>();
        private List<UserMaster> _Users = new List<UserMaster>();
        private List<TenantMetadata> _Tenants = new List<TenantMetadata>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public CredentialsScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Credentials", Subtitle(context), "credentials", c => c.Id)
        {
            _DeleteSelected = Header.AddButton("Delete Selected", OnDeleteSelected, "Del");
            _Create = Header.AddButton("+ Credential", OnCreate, "n");
            Header.AddButton("Refresh", Refresh, "F5");
            AddFixed(Header, w => Header.HeightFor(w));
            NameFilter.Placeholder = "Search...";
            NameFilter.ValueChanged += (s, e) => Apply();
            UserFilter.ModalHost = context.Modals;
            UserFilter.PickerTitle = "All users";
            UserFilter.ValueChanged += (s, e) => Apply();
            TenantFilter.ModalHost = context.Modals;
            TenantFilter.PickerTitle = "All tenants";
            TenantFilter.ValueChanged += (s, e) => Apply();
            Filters.Add("Name", NameFilter, 18);
            Filters.Add("User", UserFilter, 24);
            Filters.Add("Tenant", TenantFilter, 20);
            AddFixed(Filters, w => Filters.HeightFor(w));
            SetFilterOptions();
            Grid.AddColumn(new GridColumn<Credential>("createdIso", "Created (UTC)", c => UserAdminOps.SortStamp(c.CreatedUtc)) { Width = 20, Sortable = true, DefaultVisible = false });
            Grid.AddColumn(new GridColumn<Credential>("name", "Name", c => String.IsNullOrEmpty(c.Name) ? "-" : c.Name) { Weight = 2, Sortable = true });
            Grid.AddColumn(new GridColumn<Credential>("id", "ID", c => c.Id) { Width = 26, Style = (c, t) => t.Muted });
            Grid.AddColumn(new GridColumn<Credential>("userId", "User", c => UserName(c.UserId)) { Width = 20, Sortable = true, Style = (c, t) => t.Muted });
            Grid.AddColumn(new GridColumn<Credential>("tenant", "Tenant", c => TenantName(c.TenantId)) { Width = 16, Style = (c, t) => t.Muted });
            Grid.AddColumn(new GridColumn<Credential>("token", "Bearer Token", c => c.BearerToken) { Width = 14, Style = (c, t) => t.Code });
            Grid.AddColumn(new GridColumn<Credential>("active", "Active", c => UserAdminOps.YesNo(Context, c.Active)) { Width = 7, Sortable = true });
            Grid.AddColumn(new GridColumn<Credential>("created", "Created", c => ScreenOps.Relative(Context, c.CreatedUtc)) { Width = 10, Sortable = true, SortKey = "createdIso", Style = (c, t) => t.Muted });
            Grid.EmptyText = "No credentials found.";
            BindPreferences("name", false, 25);
            AddFill(Grid);
            Scope.Focus(Grid);
            Banner = UserAdminOps.RemoteBanner(context, "Credential");
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// User email for an id, or the id.
        /// </summary>
        /// <param name="userId">User id.</param>
        /// <returns>Email.</returns>
        public string UserName(string userId)
        {
            return _Users.FirstOrDefault(u => u.Id == userId)?.Email ?? userId;
        }

        /// <summary>
        /// Tenant name for an id, or the id.
        /// </summary>
        /// <param name="tenantId">Tenant id.</param>
        /// <returns>Name.</returns>
        public string TenantName(string tenantId)
        {
            return _Tenants.FirstOrDefault(t => t.Id == tenantId)?.Name ?? tenantId;
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            Load();
        }

        /// <summary>
        /// Load credentials, users, and tenants.
        /// </summary>
        public void Load()
        {
            Grid.SetLoading();
            ScreenOps.Run(Context, async () =>
            {
                CredentialsLoad load = new CredentialsLoad();
                load.Credentials = (await Context.Client.ListCredentialsAsync().ConfigureAwait(false))?.Objects ?? new List<Credential>();
                load.Users = (await Context.Client.ListUsersAsync().ConfigureAwait(false))?.Objects ?? new List<UserMaster>();
                if (Context.Session.IsGlobalAdmin)
                {
                    load.Tenants = (await Context.Client.ListTenantsAsync().ConfigureAwait(false))?.Objects ?? new List<TenantMetadata>();
                }
                else
                {
                    TenantMetadata? own = Context.Session.Identity?.Tenant;
                    load.Tenants = own != null ? new List<TenantMetadata> { own } : new List<TenantMetadata>();
                }

                return load;
            }, load =>
            {
                _Items = load.Credentials;
                _Users = load.Users;
                _Tenants = load.Tenants;
                SetFilterOptions();
                Apply();
            }, "Failed to load credentials.", ex => Grid.SetError(Context.Loc.T("Failed to load credentials.")));
        }

        /// <summary>
        /// Open the create (null) or edit form.
        /// </summary>
        /// <param name="editing">Credential to edit, or null.</param>
        /// <returns>The modal, or null in proxy mode.</returns>
        public FormModal? OpenForm(Credential? editing)
        {
            if (!CanWrite) return null;
            bool admin = Context.Session.IsGlobalAdmin;
            bool manager = admin || Context.Session.IsTenantAdmin;
            FormView form = new FormView();
            SelectField<string> user = form.AddField("User", new SelectField<string>());
            user.ModalHost = Context.Modals;
            user.PickerTitle = "User";
            user.Placeholder = "Select user...";
            user.Required = true;
            user.Options = _Users.Select(u => new SelectOption<string>(u.Id, u.Email)).ToList();
            string initialUser = editing?.UserId ?? _Users.FirstOrDefault()?.Id ?? Context.Session.Identity?.User?.Id ?? "";
            if (initialUser.Length > 0 && !user.Options.Any(o => o.Value == initialUser)) user.Options.Add(new SelectOption<string>(initialUser, UserName(initialUser)));
            user.SetValue(initialUser.Length > 0 ? initialUser : null);
            if (editing != null || !manager) user.CanFocus = false;
            SelectField<string> tenant = form.AddField("Tenant", new SelectField<string>());
            tenant.ModalHost = Context.Modals;
            tenant.PickerTitle = "Tenant";
            tenant.Placeholder = "Select tenant...";
            tenant.Required = true;
            tenant.Options = _Tenants.Select(t => new SelectOption<string>(t.Id, t.Name)).ToList();
            string initialTenant = editing?.TenantId ?? _Tenants.FirstOrDefault()?.Id ?? Context.Session.Identity?.Tenant?.Id ?? "";
            if (initialTenant.Length > 0 && !tenant.Options.Any(o => o.Value == initialTenant)) tenant.Options.Add(new SelectOption<string>(initialTenant, TenantName(initialTenant)));
            tenant.SetValue(initialTenant.Length > 0 ? initialTenant : null);
            if (editing != null || !admin) tenant.CanFocus = false;
            InputField name = form.AddField("Name (optional)", new InputField());
            name.Placeholder = "e.g., CI/CD Token";
            name.Value = editing?.Name ?? "";
            ToggleField active = new ToggleField(editing?.Active ?? true);
            if (editing != null) form.AddField("Active", active);
            form.MarkClean();
            form.Scope.FocusFirst();
            FormModal modal = new FormModal(editing != null ? "Edit Credential" : "Create Credential", form, Context, editing != null ? "Save" : "Create");
            string? createdToken = null;
            modal.SubmitAsync = async () =>
            {
                if (editing != null)
                {
                    Credential body = new Credential(editing.TenantId, editing.UserId);
                    body.Id = editing.Id;
                    body.Name = String.IsNullOrEmpty(name.Value) ? null : name.Value;
                    body.Active = active.Value;
                    await Context.Client.UpdateCredentialAsync(editing.Id, body).ConfigureAwait(false);
                }
                else
                {
                    if (String.IsNullOrEmpty(user.Value) || String.IsNullOrEmpty(tenant.Value)) return "A selection is required.";
                    Credential body = new Credential(tenant.Value!, user.Value!);
                    body.Name = String.IsNullOrEmpty(name.Value) ? null : name.Value;
                    Credential? created = await Context.Client.CreateCredentialAsync(body).ConfigureAwait(false);
                    createdToken = created?.BearerToken;
                }

                return null;
            };
            Context.Modals.Show(modal, result =>
            {
                if (!(result is bool ok) || !ok) return;
                if (editing != null)
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Credential \"{{name}}\" saved.", LocalizationArgs.Of("name", String.IsNullOrEmpty(name.Value) ? editing.Id : name.Value));
                }
                else
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Credential created.");
                    if (!String.IsNullOrEmpty(createdToken)) ShowNewToken(createdToken!);
                }

                Load();
            });
            return modal;
        }

        /// <summary>
        /// Show a new token once (y copies it).
        /// </summary>
        /// <param name="token">Token.</param>
        public void ShowNewToken(string token)
        {
            NewToken = token;
            string text = Context.Loc.T("Copy this bearer token now. It is shown only once; later reads show it masked.") + "\n\n" + token + "\n\n" + Context.Loc.T("Copy token") + ": y";
            ScreenOps.ShowViewer(Context, "Credential created", new JsonOrTextViewer(text), token, "Token");
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
        protected override void OnActivate(Credential row)
        {
            if (CanWrite) OpenForm(row);
            else ScreenOps.ShowJson(Context, JsonTitle(row), row);
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(Credential row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            if (CanWrite) items.Add(new ActionMenuItem("Edit", () => OpenForm(row), "Enter"));
            items.Add(new ActionMenuItem("Copy Token", () => Context.Clipboard.Copy(row.BearerToken, "Token")));
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
        protected override string JsonTitle(Credential row)
        {
            return "Credential: " + (String.IsNullOrEmpty(row.Name) ? row.Id : row.Name);
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
            OpenForm(null);
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
        protected override void OnDeleteRow(Credential row)
        {
            if (!CanWrite) return;
            string id = row.Id;
            string label = String.IsNullOrEmpty(row.Name) ? row.Id : row.Name!;
            Context.Confirm("Delete Credential", Context.Loc.T("Delete credential \"{{name}}\"? This cannot be undone.", LocalizationArgs.Of("name", label)), () =>
            {
                ScreenOps.RunVoid(Context, () => Context.Client.DeleteCredentialAsync(id), () =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Credential \"{{name}}\" deleted.", LocalizationArgs.Of("name", label));
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
            Context.Confirm("Delete Selected Credentials", Context.Loc.T("Delete {{count}} credential(s)?", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                UserAdminOps.BulkDelete(Context, ids, id => Context.Client.DeleteCredentialAsync(id), "credentials", Load);
            }, "Delete", "delete");
        }

        #endregion

        #region Private-Methods

        private static string Subtitle(TuiContext context)
        {
            if (context.Session.IsGlobalAdmin) return "Manage API bearer tokens across all tenants.";
            if (context.Session.IsTenantAdmin) return "Manage API bearer tokens within your tenant.";
            return "Manage your API bearer tokens.";
        }

        private void SetFilterOptions()
        {
            string currentUser = UserFilter.Value ?? "";
            List<SelectOption<string>> users = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("All users")) };
            users.AddRange(_Users.Select(u => new SelectOption<string>(u.Id, u.Email)));
            UserFilter.Options = users;
            UserFilter.SetValue(users.Any(o => o.Value == currentUser) ? currentUser : "");
            string currentTenant = TenantFilter.Value ?? "";
            List<SelectOption<string>> tenants = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("All tenants")) };
            tenants.AddRange(_Tenants.Select(t => new SelectOption<string>(t.Id, t.Name)));
            TenantFilter.Options = tenants;
            TenantFilter.SetValue(tenants.Any(o => o.Value == currentTenant) ? currentTenant : "");
        }

        private void Apply()
        {
            string name = NameFilter.Value.Trim();
            string userId = UserFilter.Value ?? "";
            string tenantId = TenantFilter.Value ?? "";
            Grid.EmptyText = _Items.Count == 0 ? "No credentials found." : "No credentials match filters.";
            Grid.SetLocalRows(_Items.Where(c =>
                (name.Length == 0 || (c.Name ?? "").Contains(name, StringComparison.OrdinalIgnoreCase))
                && (userId.Length == 0 || c.UserId == userId)
                && (tenantId.Length == 0 || c.TenantId == tenantId)));
        }

        #endregion
    }
}
