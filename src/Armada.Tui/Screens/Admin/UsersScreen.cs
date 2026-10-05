namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Settings, Users tab (dashboard <c>admin/Users.tsx</c>): users with email, id, name, tenant, global admin, tenant
    /// admin, active, and created; email and name search and a tenant filter; create and edit (email, first and last
    /// name, password and confirmation where a blank password keeps the current one on edit, the current password when
    /// the signed-in user changes their own password (F-37), tenant, Global Admin for
    /// global admins, Tenant Admin for tenant admins, Active on edit); delete and bulk delete with a typed "delete"
    /// confirmation; View JSON. Writes are blocked behind Armada.Proxy. Not thread-safe.
    /// </summary>
    public class UsersScreen : GridScreen<UserMaster>
    {
        #region Public-Members

        /// <summary>
        /// Filters.
        /// </summary>
        public FilterStrip Filters { get; } = new FilterStrip();

        /// <summary>
        /// Email search.
        /// </summary>
        public InputField EmailFilter { get; } = new InputField();

        /// <summary>
        /// Name search.
        /// </summary>
        public InputField NameFilter { get; } = new InputField();

        /// <summary>
        /// Tenant filter ("" for all tenants).
        /// </summary>
        public SelectField<string> TenantFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Users loaded (before filters).
        /// </summary>
        public IReadOnlyList<UserMaster> Items
        {
            get { return _Items; }
        }

        /// <summary>
        /// Tenants known to the screen (all for global admins, the user's own otherwise).
        /// </summary>
        public IReadOnlyList<TenantMetadata> Tenants
        {
            get { return _Tenants; }
        }

        /// <summary>
        /// True when the user may create and delete users (an admin outside proxy mode).
        /// </summary>
        public bool CanManage
        {
            get { return (Context.Session.IsGlobalAdmin || Context.Session.IsTenantAdmin) && UserAdminOps.RemoteInstance(Context) == null; }
        }

        /// <summary>
        /// True outside proxy mode (editing is allowed for everyone; the server limits regular users to themselves).
        /// </summary>
        public bool CanEdit
        {
            get { return UserAdminOps.RemoteInstance(Context) == null; }
        }

        #endregion

        #region Private-Members

        private readonly Button _DeleteSelected;
        private readonly Button _Create;
        private List<UserMaster> _Items = new List<UserMaster>();
        private List<TenantMetadata> _Tenants = new List<TenantMetadata>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public UsersScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Users", Subtitle(context), "users", u => u.Id)
        {
            _DeleteSelected = Header.AddButton("Delete Selected", OnDeleteSelected, "Del");
            _Create = Header.AddButton("+ User", OnCreate, "n");
            Header.AddButton("Refresh", Refresh, "F5");
            AddFixed(Header, w => Header.HeightFor(w));
            EmailFilter.Placeholder = "Search...";
            NameFilter.Placeholder = "Search...";
            EmailFilter.ValueChanged += (s, e) => Apply();
            NameFilter.ValueChanged += (s, e) => Apply();
            TenantFilter.ModalHost = context.Modals;
            TenantFilter.PickerTitle = "All tenants";
            TenantFilter.ValueChanged += (s, e) => Apply();
            Filters.Add("Email", EmailFilter, 22);
            Filters.Add("Name", NameFilter, 18);
            Filters.Add("Tenant", TenantFilter, 22);
            AddFixed(Filters, w => Filters.HeightFor(w));
            SetTenantOptions();
            Grid.AddColumn(new GridColumn<UserMaster>("createdIso", "Created (UTC)", u => UserAdminOps.SortStamp(u.CreatedUtc)) { Width = 20, Sortable = true, DefaultVisible = false });
            Grid.AddColumn(new GridColumn<UserMaster>("email", "Email", u => u.Email) { Weight = 2, Sortable = true });
            Grid.AddColumn(new GridColumn<UserMaster>("id", "ID", u => u.Id) { Width = 26, Style = (u, t) => t.Muted });
            Grid.AddColumn(new GridColumn<UserMaster>("name", "Name", u => FullName(u)) { Weight = 1, Sortable = true });
            Grid.AddColumn(new GridColumn<UserMaster>("tenant", "Tenant", u => TenantName(u.TenantId)) { Width = 16, Style = (u, t) => t.Muted });
            Grid.AddColumn(new GridColumn<UserMaster>("isAdmin", "Global Admin", u => UserAdminOps.YesNo(Context, u.IsAdmin)) { Width = 13, Sortable = true });
            Grid.AddColumn(new GridColumn<UserMaster>("isTenantAdmin", "Tenant Admin", u => UserAdminOps.YesNo(Context, u.IsTenantAdmin)) { Width = 13 });
            Grid.AddColumn(new GridColumn<UserMaster>("active", "Active", u => UserAdminOps.YesNo(Context, u.Active)) { Width = 7, Sortable = true });
            Grid.AddColumn(new GridColumn<UserMaster>("created", "Created", u => ScreenOps.Relative(Context, u.CreatedUtc)) { Width = 10, Sortable = true, SortKey = "createdIso", Style = (u, t) => t.Muted });
            Grid.EmptyText = "No users found.";
            BindPreferences("email", false, 25);
            AddFill(Grid);
            Scope.Focus(Grid);
            Banner = UserAdminOps.RemoteBanner(context, "User");
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// "First Last" or "-".
        /// </summary>
        /// <param name="user">User.</param>
        /// <returns>Name.</returns>
        public static string FullName(UserMaster user)
        {
            string name = String.Join(" ", new string?[] { user.FirstName, user.LastName }.Where(s => !String.IsNullOrEmpty(s))).Trim();
            return name.Length > 0 ? name : "-";
        }

        /// <summary>
        /// Tenant name for an id, or the id.
        /// </summary>
        /// <param name="tenantId">Tenant id.</param>
        /// <returns>Name.</returns>
        public string TenantName(string tenantId)
        {
            TenantMetadata? tenant = _Tenants.FirstOrDefault(t => t.Id == tenantId);
            return tenant?.Name ?? tenantId;
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            Load();
        }

        /// <summary>
        /// Load users and tenants.
        /// </summary>
        public void Load()
        {
            Grid.SetLoading();
            ScreenOps.Run(Context, async () =>
            {
                EnumerationResult<UserMaster>? users = await Context.Client.ListUsersAsync().ConfigureAwait(false);
                List<TenantMetadata> tenants;
                if (Context.Session.IsGlobalAdmin)
                {
                    EnumerationResult<TenantMetadata>? t = await Context.Client.ListTenantsAsync().ConfigureAwait(false);
                    tenants = t?.Objects ?? new List<TenantMetadata>();
                }
                else
                {
                    TenantMetadata? own = Context.Session.Identity?.Tenant;
                    tenants = own != null ? new List<TenantMetadata> { own } : new List<TenantMetadata>();
                }

                UsersLoad load = new UsersLoad();
                load.Users = users?.Objects ?? new List<UserMaster>();
                load.Tenants = tenants;
                return load;
            }, load =>
            {
                _Items = load.Users;
                _Tenants = load.Tenants;
                SetTenantOptions();
                Apply();
            }, "Failed to load users.", ex => Grid.SetError(Context.Loc.T("Failed to load users.")));
        }

        /// <summary>
        /// Open the create (null) or edit form.
        /// </summary>
        /// <param name="editing">User to edit, or null.</param>
        /// <returns>The modal, or null when not allowed.</returns>
        public FormModal? OpenForm(UserMaster? editing)
        {
            if (editing == null ? !CanManage : !CanEdit) return null;
            bool admin = Context.Session.IsGlobalAdmin;
            FormView form = new FormView();
            InputField email = form.AddField("Email", new InputField());
            email.Value = editing?.Email ?? "";
            email.Validator = v => String.IsNullOrWhiteSpace(v) ? "Email is required." : (!v.Contains('@') ? "Enter a valid email address." : null);
            InputField first = form.AddField("First Name", new InputField());
            first.Value = editing?.FirstName ?? "";
            InputField last = form.AddField("Last Name", new InputField());
            last.Value = editing?.LastName ?? "";
            InputField password = form.AddField(editing != null ? "New Password" : "Password", new InputField());
            password.Masked = true;
            password.Placeholder = editing != null ? "Leave blank to keep current password" : "Enter password";
            InputField confirm = form.AddField(editing != null ? "Confirm New Password" : "Confirm Password", new InputField());
            confirm.Masked = true;
            confirm.Placeholder = editing != null ? "Repeat new password" : "Repeat password";
            bool self = IsSignedInUser(editing);
            InputField current = new InputField();
            current.Masked = true;
            current.Placeholder = "Required to change your own password";
            if (self) form.AddField("Current Password", current);
            SelectField<string> tenant = form.AddField("Tenant", new SelectField<string>());
            tenant.ModalHost = Context.Modals;
            tenant.PickerTitle = "Tenant";
            tenant.Placeholder = "Select tenant...";
            tenant.Required = true;
            string initialTenant = editing?.TenantId ?? _Tenants.FirstOrDefault()?.Id ?? Context.Session.Identity?.Tenant?.Id ?? "";
            IEnumerable<TenantMetadata> choices = admin ? _Tenants : _Tenants.Where(t => t.Id == initialTenant || t.Id == Context.Session.Identity?.Tenant?.Id);
            tenant.Options = choices.Select(t => new SelectOption<string>(t.Id, t.Name)).ToList();
            if (!tenant.Options.Any(o => o.Value == initialTenant) && initialTenant.Length > 0) tenant.Options.Add(new SelectOption<string>(initialTenant, TenantName(initialTenant)));
            tenant.SetValue(initialTenant.Length > 0 ? initialTenant : null);
            if (!admin) tenant.CanFocus = false;
            ToggleField isAdmin = new ToggleField(editing?.IsAdmin ?? false);
            if (admin) form.AddField("Global Admin", isAdmin);
            ToggleField isTenantAdmin = new ToggleField(editing?.IsTenantAdmin ?? false);
            if (Context.Session.IsTenantAdmin) form.AddField("Tenant Admin", isTenantAdmin);
            ToggleField active = new ToggleField(editing?.Active ?? true);
            if (editing != null) form.AddField("Active", active);
            form.MarkClean();
            FormModal modal = new FormModal(editing != null ? "Edit User" : "Create User", form, Context, "Save");
            modal.SubmitAsync = async () =>
            {
                string? error = ValidatePasswords(editing != null, password.Value, confirm.Value);
                if (error == null && self) error = ValidateCurrentPassword(password.Value, current.Value);
                if (error != null) return error;
                UserUpsertRequest body = new UserUpsertRequest();
                body.Email = email.Value.Trim();
                body.FirstName = String.IsNullOrEmpty(first.Value) ? null : first.Value;
                body.LastName = String.IsNullOrEmpty(last.Value) ? null : last.Value;
                body.TenantId = tenant.Value ?? "";
                body.IsAdmin = isAdmin.Value;
                body.IsTenantAdmin = isTenantAdmin.Value;
                body.Active = active.Value;
                if (password.Value.Trim().Length > 0)
                {
                    body.Password = password.Value;
                    if (self) body.CurrentPassword = current.Value;
                }

                if (editing != null)
                {
                    try
                    {
                        await Context.Client.UpdateUserAsync(editing.Id, body).ConfigureAwait(false);
                    }
                    catch (ArmadaApiException ex) when (body.CurrentPassword != null && ex.StatusCode == 403)
                    {
                        // On a self-update the only 403 is a wrong CurrentPassword (F-37).
                        return "Current password is incorrect.";
                    }
                }
                else
                {
                    await Context.Client.CreateUserAsync(body).ConfigureAwait(false);
                }

                return null;
            };
            Context.Modals.Show(modal, result =>
            {
                if (!(result is bool ok) || !ok) return;
                ScreenOps.Toast(Context, NotificationSeverityEnum.Success, editing != null ? "User \"{{email}}\" saved." : "User \"{{email}}\" created.", LocalizationArgs.Of("email", email.Value.Trim()));
                Load();
            });
            return modal;
        }

        /// <summary>
        /// The dashboard's password checks: required on create, and the confirmation must match.
        /// </summary>
        /// <param name="editing">True when editing.</param>
        /// <param name="password">Password.</param>
        /// <param name="confirm">Confirmation.</param>
        /// <returns>English error, or null.</returns>
        public static string? ValidatePasswords(bool editing, string password, string confirm)
        {
            if (!editing && String.IsNullOrWhiteSpace(password)) return "Password is required when creating a user.";
            if (!String.Equals(password ?? "", confirm ?? "", StringComparison.Ordinal)) return "Passwords do not match.";
            return null;
        }

        /// <summary>
        /// The server's self-service check (F-37): changing your own password through the user edit form needs the
        /// current password. A blank new password keeps the stored one and needs nothing.
        /// </summary>
        /// <param name="password">New password.</param>
        /// <param name="current">Current password.</param>
        /// <returns>English error, or null.</returns>
        public static string? ValidateCurrentPassword(string password, string current)
        {
            if (String.IsNullOrWhiteSpace(password)) return null;
            if (String.IsNullOrEmpty(current)) return "Enter your current password to change your own password.";
            return null;
        }

        /// <summary>
        /// True when <paramref name="user"/> is the signed-in user (whose own password change needs the current one).
        /// </summary>
        /// <param name="user">User being edited, or null.</param>
        /// <returns>True for the signed-in user.</returns>
        public bool IsSignedInUser(UserMaster? user)
        {
            string? me = Context.Session.Identity?.User?.Id;
            return user != null && !String.IsNullOrEmpty(me) && String.Equals(user.Id, me, StringComparison.Ordinal);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            _DeleteSelected.Visible = CanManage && Grid.Marked.Count > 0;
            _DeleteSelected.Hint = "(" + Grid.Marked.Count + ")";
            _Create.Visible = CanManage;
        }

        /// <inheritdoc />
        protected override void OnActivate(UserMaster row)
        {
            if (CanEdit) OpenForm(row);
            else ScreenOps.ShowJson(Context, JsonTitle(row), row);
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(UserMaster row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            if (CanEdit) items.Add(new ActionMenuItem("Edit", () => OpenForm(row), "Enter"));
            items.Add(new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), row), "j"));
            if (CanManage)
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => OnDeleteRow(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <inheritdoc />
        protected override string JsonTitle(UserMaster row)
        {
            return "User: " + row.Email;
        }

        /// <inheritdoc />
        protected override bool SupportsCreate()
        {
            return true;
        }

        /// <inheritdoc />
        protected override bool CanCreate()
        {
            return CanManage;
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
            return CanManage && base.CanDeleteRows();
        }

        /// <inheritdoc />
        protected override void OnDeleteRow(UserMaster row)
        {
            if (!CanManage) return;
            string id = row.Id;
            string mail = row.Email;
            Context.Confirm("Delete User", Context.Loc.T("Delete user \"{{email}}\"? This cannot be undone.", LocalizationArgs.Of("email", mail)), () =>
            {
                ScreenOps.RunVoid(Context, () => Context.Client.DeleteUserAsync(id), () =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "User \"{{email}}\" deleted.", LocalizationArgs.Of("email", mail));
                    Load();
                }, "Delete failed.");
            }, "Delete", "delete");
        }

        /// <inheritdoc />
        protected override void OnDeleteSelected()
        {
            if (!CanManage) return;
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Context.Confirm("Delete Selected Users", Context.Loc.T("Delete {{count}} user(s)?", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                Grid.ClearMarks();
                UserAdminOps.BulkDelete(Context, ids, id => Context.Client.DeleteUserAsync(id), "users", Load);
            }, "Delete", "delete");
        }

        #endregion

        #region Private-Methods

        private static string Subtitle(TuiContext context)
        {
            if (context.Session.IsGlobalAdmin) return "Manage user accounts across all tenants.";
            if (context.Session.IsTenantAdmin) return "Manage user accounts within your tenant.";
            return "View and update your own user account.";
        }

        private void SetTenantOptions()
        {
            string current = TenantFilter.Value ?? "";
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("All tenants")) };
            options.AddRange(_Tenants.Select(t => new SelectOption<string>(t.Id, t.Name)));
            TenantFilter.Options = options;
            TenantFilter.SetValue(options.Any(o => o.Value == current) ? current : "");
        }

        private void Apply()
        {
            string email = EmailFilter.Value.Trim();
            string name = NameFilter.Value.Trim();
            string tenant = TenantFilter.Value ?? "";
            Grid.EmptyText = _Items.Count == 0 ? "No users found." : "No users match filters.";
            Grid.SetLocalRows(_Items.Where(u =>
                (email.Length == 0 || (u.Email ?? "").Contains(email, StringComparison.OrdinalIgnoreCase))
                && (name.Length == 0 || ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Contains(name, StringComparison.OrdinalIgnoreCase))
                && (tenant.Length == 0 || u.TenantId == tenant)));
        }

        #endregion
    }
}
