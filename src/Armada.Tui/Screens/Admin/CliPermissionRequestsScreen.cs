namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// CLI Tool Permissions, Requests tab (dashboard <c>CliPermissions.tsx</c>, <c>RequestsPanel</c>): the CLI tool calls
    /// captains asked Armada to approve, filtered by status (Pending by default, or All), with tool and command, status,
    /// where the captain runs, age, and the expiry countdown. A pending request the user may decide is decided here with
    /// the Approvals center's keys and the header buttons: <c>a</c> allow once, <c>A</c> allow and remember (rule dialog),
    /// <c>d</c> deny (optional message). <c>Enter</c> shows the whole request; <c>o</c> opens its mission, conversation,
    /// or captain. The <c>request</c> query parameter (the inbox link) selects that request even when the filter would
    /// hide it. Reloads on <c>cli_permission.requested</c> and <c>cli_permission.resolved</c>. Not thread-safe.
    /// </summary>
    public class CliPermissionRequestsScreen : GridScreen<CliPermissionRequest>
    {
        #region Public-Members

        /// <summary>
        /// Most requests listed (the dashboard's limit).
        /// </summary>
        public const int RequestLimit = 200;

        /// <summary>
        /// Filters.
        /// </summary>
        public FilterStrip Filters { get; } = new FilterStrip();

        /// <summary>
        /// Status filter: a status name, or empty for All.
        /// </summary>
        public SelectField<string> StatusFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Decisions (the Approvals center's).
        /// </summary>
        public ApprovalActions Actions { get; }

        /// <summary>
        /// Allow once button (a).
        /// </summary>
        public Button AllowOnceButton { get; }

        /// <summary>
        /// Allow and remember button (A).
        /// </summary>
        public Button RememberButton { get; }

        /// <summary>
        /// Deny button (d).
        /// </summary>
        public Button DenyButton { get; }

        /// <summary>
        /// Request id from the route's <c>request</c> query parameter, or null.
        /// </summary>
        public string? HighlightId { get; }

        /// <summary>
        /// Requests from the last load.
        /// </summary>
        public IReadOnlyList<CliPermissionRequest> Items
        {
            get { return _Items; }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                CliPermissionRequest? row = Grid.Current;
                if (CanDecide(row))
                {
                    hints.Add(new KeyValuePair<string, string>("a", "Allow once"));
                    if (row!.CanRemember) hints.Add(new KeyValuePair<string, string>("A", "Allow and remember"));
                    hints.Add(new KeyValuePair<string, string>("d", "Deny"));
                }

                hints.Add(new KeyValuePair<string, string>("Enter", "Details"));
                if (row != null && RouteOf(row) != null) hints.Add(new KeyValuePair<string, string>("o", "Open"));
                hints.Add(new KeyValuePair<string, string>("j", "JSON"));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private readonly List<IDisposable> _Subscriptions = new List<IDisposable>();
        private List<CliPermissionRequest> _Items = new List<CliPermissionRequest>();
        private bool _Highlighted = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public CliPermissionRequestsScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Requests", "Captains with the Approve in Armada policy ask here before running a shell command, edit, or fetch that no rule allows.", "cli-permission-requests", r => r.Id)
        {
            Actions = new ApprovalActions(context);
            route.Query.TryGetValue("request", out string? highlight);
            HighlightId = String.IsNullOrWhiteSpace(highlight) ? null : highlight;
            AllowOnceButton = Header.AddButton("Allow once", () => AllowOnce(), "a");
            RememberButton = Header.AddButton("Allow and remember", () => Remember(), "A");
            DenyButton = Header.AddButton("Deny", () => Deny(), "d");
            Header.AddButton("Refresh", Refresh, "F5");
            AddFixed(Header, w => Header.HeightFor(w));
            StatusFilter.ModalHost = context.Modals;
            StatusFilter.PickerTitle = "Status";
            List<SelectOption<string>> statuses = new List<SelectOption<string>>();
            foreach (CliPermissionRequestStatusEnum status in new[] { CliPermissionRequestStatusEnum.Pending, CliPermissionRequestStatusEnum.Allowed, CliPermissionRequestStatusEnum.Denied, CliPermissionRequestStatusEnum.Expired, CliPermissionRequestStatusEnum.Cancelled })
                statuses.Add(new SelectOption<string>(status.ToString(), CliPermissionText.Status(context.Loc, status)));
            statuses.Add(new SelectOption<string>("", context.Loc.T("All")));
            StatusFilter.Options = statuses;
            StatusFilter.SetValue(CliPermissionRequestStatusEnum.Pending.ToString());
            StatusFilter.ValueChanged += (s, e) => Load();
            Filters.Add("Status", StatusFilter, 16);
            AddFixed(Filters, w => Filters.HeightFor(w));
            Grid.MultiSelect = false;
            Grid.AddColumn(new GridColumn<CliPermissionRequest>("title", "Request", r => CliPermissionText.Title(r)) { Weight = 3, Style = (r, t) => t.Code });
            Grid.AddColumn(new GridColumn<CliPermissionRequest>("status", "Status", r => CliPermissionText.Status(Context.Loc, r.Status)) { Width = 10, Sortable = true, Style = (r, t) => StatusStyle(r.Status, t) });
            Grid.AddColumn(new GridColumn<CliPermissionRequest>("where", "Where", r => CliPermissionText.Where(Context.Loc, r)) { Weight = 2, Style = (r, t) => t.Muted });
            Grid.AddColumn(new GridColumn<CliPermissionRequest>("createdIso", "Created (UTC)", r => UserAdminOps.SortStamp(r.CreatedUtc)) { Width = 20, Sortable = true, DefaultVisible = false });
            Grid.AddColumn(new GridColumn<CliPermissionRequest>("created", "Created", r => ScreenOps.Relative(Context, r.CreatedUtc)) { Width = 12, Sortable = true, SortKey = "createdIso", Style = (r, t) => t.Muted });
            Grid.AddColumn(new GridColumn<CliPermissionRequest>("expires", "Expires", r => ExpiryText(r)) { Width = 18, Style = (r, t) => r.Status == CliPermissionRequestStatusEnum.Pending ? t.Warning : t.Muted });
            Grid.EmptyText = "No CLI tool requests are waiting.";
            BindPreferences("createdIso", true, 25);
            AddFill(Grid);
            Scope.Focus(Grid);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Refresh()
        {
            Load();
        }

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            return 0;
        }

        /// <inheritdoc />
        public override void OnActivated()
        {
            _Subscriptions.Add(Context.Events.SubscribeCoalesced("cli_permission.", Load));
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            foreach (IDisposable sub in _Subscriptions) sub.Dispose();
            _Subscriptions.Clear();
        }

        /// <summary>
        /// Load the requests for the status filter; the linked request is added when the filter hides it.
        /// </summary>
        public void Load()
        {
            CliPermissionRequestQuery query = new CliPermissionRequestQuery();
            query.Limit = RequestLimit;
            if (Enum.TryParse<CliPermissionRequestStatusEnum>(StatusFilter.Value ?? "", false, out CliPermissionRequestStatusEnum status)) query.Status = status;
            string? highlight = HighlightId;
            if (_Items.Count == 0) Grid.SetLoading();
            ScreenOps.Run(Context, async () =>
            {
                List<CliPermissionRequest> list = await Context.Client.ListCliPermissionRequestsAsync(query).ConfigureAwait(false) ?? new List<CliPermissionRequest>();
                if (highlight != null && !list.Any(r => r.Id == highlight))
                {
                    try
                    {
                        CliPermissionRequest? linked = await Context.Client.GetCliPermissionRequestAsync(highlight).ConfigureAwait(false);
                        if (linked != null && !String.IsNullOrEmpty(linked.Id)) list.Insert(0, linked);
                    }
                    catch (Armada.Client.ArmadaApiException)
                    {
                        // Not visible to this user or gone; the list still renders.
                    }
                }

                return list;
            }, rows =>
            {
                _Items = rows;
                Grid.EmptyText = query.Status == CliPermissionRequestStatusEnum.Pending ? "No CLI tool requests are waiting." : "No CLI tool requests match this filter.";
                Grid.SetLocalRows(_Items);
                if (!_Highlighted && highlight != null)
                {
                    int index = Grid.Rows.ToList().FindIndex(r => r.Id == highlight);
                    if (index >= 0)
                    {
                        Grid.MoveCursor(index);
                        _Highlighted = true;
                    }
                }
            }, "Failed to load CLI tool requests.", ex => Grid.SetError(Context.Loc.T("Failed to load CLI tool requests.")));
        }

        /// <summary>
        /// Allow the selected request once (<c>a</c>).
        /// </summary>
        /// <returns>True when the call started.</returns>
        public bool AllowOnce()
        {
            ApprovalItem? item = ItemFor(Grid.Current);
            return item != null && Actions.AllowCliPermissionOnce(item, r => Load());
        }

        /// <summary>
        /// Allow and remember the selected request (<c>A</c>): the rule dialog.
        /// </summary>
        /// <returns>The dialog, or null.</returns>
        public CliPermissionDecisionModal? Remember()
        {
            ApprovalItem? item = ItemFor(Grid.Current);
            return item == null ? null : Actions.RememberCliPermission(item, r => Load());
        }

        /// <summary>
        /// Deny the selected request (<c>d</c>): the optional message dialog.
        /// </summary>
        /// <returns>The dialog, or null.</returns>
        public CliPermissionDecisionModal? Deny()
        {
            ApprovalItem? item = ItemFor(Grid.Current);
            return item == null ? null : Actions.DenyCliPermission(item, r => Load());
        }

        /// <summary>
        /// The page a request belongs to: its mission, the user's own conversation, or its captain.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Route, or null.</returns>
        public string? RouteOf(CliPermissionRequest request)
        {
            if (request == null) return null;
            if (!String.IsNullOrEmpty(request.MissionId)) return "/missions/" + Uri.EscapeDataString(request.MissionId!);
            string? me = Context.Session.Identity?.User?.Id;
            if (!String.IsNullOrEmpty(request.ThreadId) && !String.IsNullOrEmpty(me) && String.Equals(request.UserId, me, StringComparison.Ordinal)) return "/ask/" + Uri.EscapeDataString(request.ThreadId!);
            if (!String.IsNullOrEmpty(request.CaptainId)) return "/captains/" + Uri.EscapeDataString(request.CaptainId!);
            return null;
        }

        /// <summary>
        /// Plain-text details of a request (what <c>Enter</c> shows).
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Text.</returns>
        public string Details(CliPermissionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ITextLocalizer loc = Context.Loc;
            StringBuilder sb = new StringBuilder();
            sb.Append(CliPermissionText.Title(request)).Append('\n');
            string where = CliPermissionText.Where(loc, request);
            if (where.Length > 0) sb.Append(where).Append('\n');
            sb.Append('\n');
            sb.Append(loc.T("Status")).Append(": ").Append(CliPermissionText.Status(loc, request.Status)).Append('\n');
            if (request.Status == CliPermissionRequestStatusEnum.Pending) sb.Append(CliPermissionText.ExpiresIn(loc, request.ExpiresUtc, Context.Clock.UtcNow)).Append('\n');
            if (request.DecidedUtc.HasValue) sb.Append(loc.T("Decided")).Append(": ").Append(loc.FormatDateTime(request.DecidedUtc.Value)).Append('\n');
            if (!String.IsNullOrWhiteSpace(request.DecisionMessage)) sb.Append(request.DecisionMessage).Append('\n');
            if (!String.IsNullOrWhiteSpace(request.SuggestedRule)) sb.Append(loc.T("Suggested rule")).Append(": ").Append(request.SuggestedRule).Append('\n');
            sb.Append('\n').Append(loc.T("Input")).Append(":\n").Append(ApprovalActions.Pretty(request.InputText)).Append('\n');
            sb.Append('\n').Append(request.Id);
            return sb.ToString();
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            CliPermissionRequest? row = Grid.Current;
            bool decide = CanDecide(row);
            AllowOnceButton.Visible = decide;
            RememberButton.Visible = decide && row!.CanRemember;
            DenyButton.Visible = decide;
        }

        /// <inheritdoc />
        protected override void OnActivate(CliPermissionRequest row)
        {
            ScreenOps.ShowText(Context, CliPermissionText.Title(row), Details(row));
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(CliPermissionRequest row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            if (CanDecide(row))
            {
                items.Add(new ActionMenuItem("Allow once", () => AllowOnce(), "a"));
                if (row.CanRemember) items.Add(new ActionMenuItem("Allow and remember", () => Remember(), "A"));
                ActionMenuItem deny = new ActionMenuItem("Deny", () => Deny(), "d");
                deny.Destructive = true;
                items.Add(deny);
            }

            items.Add(new ActionMenuItem("Details", () => OnActivate(row), "Enter"));
            string? route = RouteOf(row);
            if (route != null) items.Add(new ActionMenuItem("Open", () => Context.Navigate(route), "o"));
            items.Add(new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), row), "j"));
            return items;
        }

        /// <inheritdoc />
        protected override string JsonTitle(CliPermissionRequest row)
        {
            return "CLI permission: " + row.Id;
        }

        /// <inheritdoc />
        protected override IEnumerable<ArmadaCommand> ExtraCommands()
        {
            string prefix = ScreenKey + ".";
            List<ArmadaCommand> commands = new List<ArmadaCommand>();
            commands.Add(Command(prefix + "allow-once", "Allow once", () => AllowOnce(), () => CanDecide(Grid.Current), "a"));
            commands.Add(Command(prefix + "allow-remember", "Allow and remember", () => Remember(), () => CanDecide(Grid.Current) && Grid.Current!.CanRemember, "A"));
            commands.Add(Command(prefix + "deny", "Deny", () => Deny(), () => CanDecide(Grid.Current), "d"));
            commands.Add(Command(prefix + "open", "Open mission, conversation, or captain", () => { CliPermissionRequest? row = Grid.Current; string? route = row != null ? RouteOf(row) : null; if (route != null) Context.Navigate(route); }, () => Grid.Current != null && RouteOf(Grid.Current) != null, "o"));
            return commands;
        }

        #endregion

        #region Private-Methods

        private static bool CanDecide(CliPermissionRequest? request)
        {
            return request != null && request.Status == CliPermissionRequestStatusEnum.Pending && request.CanDecide;
        }

        private ApprovalItem? ItemFor(CliPermissionRequest? request)
        {
            if (request == null) return null;
            ApprovalItem? item = ApprovalSources.FromCliPermission(request, Context.Loc, Context.Session.Identity?.User?.Id);
            if (item == null) Context.Notifications.Toast(NotificationSeverityEnum.Warning, Context.Loc.T("This permission request was already decided or expired."));
            return item;
        }

        private string ExpiryText(CliPermissionRequest request)
        {
            if (request.Status == CliPermissionRequestStatusEnum.Pending) return CliPermissionText.ExpiresIn(Context.Loc, request.ExpiresUtc, Context.Clock.UtcNow);
            return request.DecidedUtc.HasValue ? ScreenOps.Relative(Context, request.DecidedUtc) : "-";
        }

        private static TUIKit.CellStyle StatusStyle(CliPermissionRequestStatusEnum status, Armada.Tui.Theming.ArmadaTheme theme)
        {
            switch (status)
            {
                case CliPermissionRequestStatusEnum.Pending: return theme.Warning;
                case CliPermissionRequestStatusEnum.Allowed: return theme.Success;
                case CliPermissionRequestStatusEnum.Denied: return theme.Error;
                default: return theme.Muted;
            }
        }

        #endregion
    }
}
