namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Activity, Signals tab (dashboard <c>Signals.tsx</c>): messages between the Admiral and captains with server
    /// paging and filters (type, captain, unread only, admin user scope; Clear Filters), column filters on type,
    /// from, to, and payload, sorting within the page, a row menu (View Detail, Mark Read, View JSON, Delete), bulk
    /// delete, and the Send Signal dialog (type, payload, to a captain or the Admiral broadcast). Not thread-safe.
    /// </summary>
    public class SignalsScreen : GridScreen<Signal>
    {
        #region Public-Members

        /// <summary>
        /// Signal types offered by the filter and the Send Signal dialog (the dashboard's list).
        /// </summary>
        public static IReadOnlyList<string> SignalTypes { get; } = new List<string> { "Nudge", "Mail", "Assignment", "Progress", "Completion", "Error" };

        /// <summary>
        /// Server-side filters.
        /// </summary>
        public FilterStrip Filters { get; } = new FilterStrip();

        /// <summary>
        /// Column filters (current page).
        /// </summary>
        public FilterStrip ColumnFilters { get; } = new FilterStrip();

        /// <summary>
        /// Type filter ("" for all types).
        /// </summary>
        public SelectField<string> TypeFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Captain filter ("" for all captains).
        /// </summary>
        public SelectField<string> CaptainFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Unread only.
        /// </summary>
        public ToggleField UnreadOnly { get; } = new ToggleField(false);

        /// <summary>
        /// Admin user scope.
        /// </summary>
        public UserScopeField UserScope { get; }

        /// <summary>
        /// Type column filter.
        /// </summary>
        public InputField TypeColumn { get; } = new InputField();

        /// <summary>
        /// From column filter.
        /// </summary>
        public InputField FromColumn { get; } = new InputField();

        /// <summary>
        /// To column filter.
        /// </summary>
        public InputField ToColumn { get; } = new InputField();

        /// <summary>
        /// Payload column filter.
        /// </summary>
        public InputField PayloadColumn { get; } = new InputField();

        #endregion

        #region Private-Members

        private readonly Button _DeleteSelected;
        private readonly Button _ClearFilters;
        private List<Signal> _PageRows = new List<Signal>();
        private long _ServerTotal = 0;
        private List<Captain> _Captains = new List<Captain>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public SignalsScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Signals", "Messages exchanged between the admiral and captains. View signal payloads and delivery status.", "signals", s => s.Id)
        {
            _DeleteSelected = Header.AddButton("Delete Selected", OnDeleteSelected, "Del");
            _DeleteSelected.Visible = false;
            Header.AddButton("+ Signal", OnCreate, "n");
            _ClearFilters = Header.AddButton("Clear Filters", ResetFilters);
            Header.AddButton("Refresh", Refresh, "F5");
            AddFixed(Header, w => Header.HeightFor(w));

            TypeFilter.ModalHost = context.Modals;
            TypeFilter.PickerTitle = "All Types";
            List<SelectOption<string>> types = new List<SelectOption<string>> { new SelectOption<string>("", context.Loc.T("All Types")) };
            types.AddRange(SignalTypes.Select(t => new SelectOption<string>(t, context.Loc.T(t))));
            TypeFilter.Options = types;
            TypeFilter.SetValue("");
            CaptainFilter.ModalHost = context.Modals;
            CaptainFilter.PickerTitle = "All Captains";
            SetCaptainOptions();
            UserScope = new UserScopeField(context);
            Filters.Add("Type", TypeFilter, 14);
            Filters.Add("Captain", CaptainFilter, 18);
            Filters.Add("Unread Only", UnreadOnly, 6);
            Filters.Add("User", UserScope, 24);
            TypeFilter.ValueChanged += (s, e) => ServerFilterChanged();
            CaptainFilter.ValueChanged += (s, e) => ServerFilterChanged();
            UnreadOnly.ValueChanged += (s, e) => ServerFilterChanged();
            UserScope.ValueChanged += (s, e) => ServerFilterChanged();
            AddFixed(Filters, w => Filters.HeightFor(w));

            foreach (InputField f in new InputField[] { TypeColumn, FromColumn, ToColumn, PayloadColumn })
            {
                f.Placeholder = "Filter...";
                f.ValueChanged += (s, e) => ApplyColumnFilters();
            }

            ColumnFilters.Add("Type", TypeColumn, 12);
            ColumnFilters.Add("From", FromColumn, 14);
            ColumnFilters.Add("To", ToColumn, 14);
            ColumnFilters.Add("Payload", PayloadColumn, 20);
            AddFixed(ColumnFilters, w => ColumnFilters.HeightFor(w));

            Grid.AddColumn(new GridColumn<Signal>("id", "ID", s => s.Id) { Width = 20, Sortable = true, Style = (s, t) => t.Link });
            Grid.AddColumn(new GridColumn<Signal>("type", "Type", s => s.Type.ToString()) { Width = 12, Sortable = true });
            Grid.AddColumn(new GridColumn<Signal>("fromCaptainId", "From", s => CaptainName(s.FromCaptainId)) { Width = 14, Sortable = true });
            Grid.AddColumn(new GridColumn<Signal>("toCaptainId", "To", s => CaptainName(s.ToCaptainId)) { Width = 14, Sortable = true });
            Grid.AddColumn(new GridColumn<Signal>("read", "Read", s => Context.Loc.T(s.Read ? "Yes" : "No")) { Width = 5 });
            Grid.AddColumn(new GridColumn<Signal>("payload", "Payload", s => String.IsNullOrEmpty(s.Payload) ? "-" : s.Payload.Replace("\r", " ").Replace("\n", " ")) { Weight = 3 });
            Grid.AddColumn(new GridColumn<Signal>("createdUtc", "Time", s => ScreenOps.Relative(Context, s.CreatedUtc)) { Width = 10, Sortable = true, Style = (s, t) => t.Muted });
            Grid.EmptyText = "No signals found.";
            Grid.Loader = LoadAsync;
            BindPreferences(null, false, 25);
            AddFill(Grid);
            Scope.Focus(Grid);
            Grid.Reload();
            ScreenOps.Quiet(context, () => context.Client.ListCaptainsAsync(new ArmadaPageQuery(1, 1000)), r =>
            {
                _Captains = r?.Objects ?? new List<Captain>();
                SetCaptainOptions();
            });
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Captain name for an id; "Admiral" when null.
        /// </summary>
        /// <param name="id">Captain id.</param>
        /// <returns>Name.</returns>
        public string CaptainName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return Context.Loc.T("Admiral");
            Captain? c = _Captains.FirstOrDefault(x => x.Id == id);
            return c != null && !String.IsNullOrEmpty(c.Name) ? c.Name : id;
        }

        /// <summary>
        /// The server query for the current filters and a page.
        /// </summary>
        /// <param name="pageNumber">Page.</param>
        /// <param name="pageSize">Page size.</param>
        /// <returns>Query.</returns>
        public ArmadaPageQuery BuildQuery(int pageNumber, int pageSize)
        {
            ArmadaPageQuery q = new ArmadaPageQuery(pageNumber, pageSize);
            q.With("type", TypeFilter.Value);
            q.With("toCaptainId", CaptainFilter.Value);
            if (UnreadOnly.Value) q.With("unreadOnly", "true");
            q.With("userId", UserScope.UserId);
            return q;
        }

        /// <summary>
        /// Open the Send Signal dialog.
        /// </summary>
        /// <returns>The modal.</returns>
        public FormModal OpenSendSignal()
        {
            FormView form = new FormView();
            SelectField<string> type = form.AddField("Type", new SelectField<string>());
            type.ModalHost = Context.Modals;
            type.PickerTitle = "Type";
            type.Options = SignalTypes.Select(t => new SelectOption<string>(t, Context.Loc.T(t))).ToList();
            type.SetValue("Nudge");
            MultilineField payload = form.AddField("Payload", new MultilineField(), null, 4);
            payload.Dispatcher = Context.Dispatcher;
            payload.ExternalEditor = text => Context.External.EditTextAsync(text, ".txt");
            SelectField<string> to = form.AddField("To Captain (optional)", new SelectField<string>());
            to.ModalHost = Context.Modals;
            to.PickerTitle = "To Captain (optional)";
            List<SelectOption<string>> targets = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("Admiral (broadcast)")) };
            targets.AddRange(_Captains.Select(c => new SelectOption<string>(c.Id, String.IsNullOrEmpty(c.Name) ? c.Id : c.Name)));
            to.Options = targets;
            to.SetValue("");
            form.MarkClean();
            FormModal modal = new FormModal("Send Signal", form, Context, "Send");
            modal.SubmitAsync = async () =>
            {
                SendSignalRequest body = new SendSignalRequest();
                body.Type = String.IsNullOrEmpty(type.Value) ? "Nudge" : type.Value!;
                body.Payload = String.IsNullOrEmpty(payload.Value) ? null : payload.Value;
                body.ToCaptainId = String.IsNullOrEmpty(to.Value) ? null : to.Value;
                try
                {
                    await Context.Client.SendSignalAsync(body).ConfigureAwait(false);
                }
                catch (Armada.Client.ArmadaApiException)
                {
                    return "Failed to send signal.";
                }

                return null;
            };
            Context.Modals.Show(modal, result =>
            {
                if (!(result is bool ok) || !ok) return;
                ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Signal sent.");
                Refresh();
            });
            return modal;
        }

        /// <summary>
        /// Mark a signal read.
        /// </summary>
        /// <param name="id">Signal id.</param>
        public void MarkRead(string id)
        {
            ScreenOps.RunVoid(Context, () => Context.Client.MarkSignalReadAsync(id), () =>
            {
                ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Signal marked as read.");
                Refresh();
            }, "Failed to mark signal as read.");
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            _DeleteSelected.Visible = Grid.Marked.Count > 0;
            _DeleteSelected.Hint = "(" + Grid.Marked.Count + ")";
            _ClearFilters.Visible = !String.IsNullOrEmpty(TypeFilter.Value) || !String.IsNullOrEmpty(CaptainFilter.Value) || UnreadOnly.Value;
        }

        /// <inheritdoc />
        protected override void OnActivate(Signal row)
        {
            Context.Navigate("/signals/" + row.Id);
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(Signal row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("View Detail", () => Context.Navigate("/signals/" + row.Id), "Enter"));
            if (!row.Read) items.Add(new ActionMenuItem("Mark Read", () => MarkRead(row.Id), "r"));
            items.Add(new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), row), "j"));
            if (!String.IsNullOrEmpty(row.FromCaptainId)) items.Add(new ActionMenuItem("Open Captain", () => Context.Navigate("/captains/" + row.FromCaptainId)));
            if (!String.IsNullOrEmpty(row.ToCaptainId) && row.ToCaptainId != row.FromCaptainId) items.Add(new ActionMenuItem("Open Captain", () => Context.Navigate("/captains/" + row.ToCaptainId)));
            ActionMenuItem delete = new ActionMenuItem("Delete", () => OnDeleteRow(row), "Del");
            delete.Destructive = true;
            items.Add(delete);
            return items;
        }

        /// <inheritdoc />
        protected override string JsonTitle(Signal row)
        {
            return Context.Loc.T("Signal") + ": " + row.Id;
        }

        /// <inheritdoc />
        protected override bool SupportsCreate()
        {
            return true;
        }

        /// <inheritdoc />
        protected override void OnCreate()
        {
            OpenSendSignal();
        }

        /// <inheritdoc />
        protected override bool SupportsDelete()
        {
            return true;
        }

        /// <inheritdoc />
        protected override IEnumerable<Armada.Tui.Input.ArmadaCommand> ExtraCommands()
        {
            return new List<Armada.Tui.Input.ArmadaCommand>
            {
                Command(ScreenKey + ".mark-read", "Mark Read", () => { Signal? s = Grid.Current; if (s != null && !s.Read) MarkRead(s.Id); }, () => Grid.Current != null && !Grid.Current.Read, "r"),
                Command(ScreenKey + ".clear-filters", "Clear Filters", ResetFilters, null),
            };
        }

        /// <inheritdoc />
        protected override void OnDeleteRow(Signal row)
        {
            string id = row.Id;
            Context.Confirm("Delete", Context.Loc.T("Delete signal {{id}}?", LocalizationArgs.Of("id", id)), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.DeleteSignalsBatchAsync(new List<string> { id }), r =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Signal {{id}} deleted.", LocalizationArgs.Of("id", id));
                    Refresh();
                }, "Delete failed.");
            }, "Delete");
        }

        /// <inheritdoc />
        protected override void OnDeleteSelected()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Context.Confirm("Delete", Context.Loc.T("Delete {{count}} selected signal(s)?", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.DeleteSignalsBatchAsync(ids), r =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Deleted {{count}} signal(s).", LocalizationArgs.Of("count", ids.Count));
                    Grid.ClearMarks();
                    Refresh();
                }, "Bulk delete failed.");
            }, "Delete");
        }

        #endregion

        #region Private-Methods

        private void SetCaptainOptions()
        {
            string current = CaptainFilter.Value ?? "";
            List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Context.Loc.T("All Captains")) };
            options.AddRange(_Captains.Select(c => new SelectOption<string>(c.Id, String.IsNullOrEmpty(c.Name) ? c.Id : c.Name)));
            CaptainFilter.Options = options;
            CaptainFilter.SetValue(options.Any(o => o.Value == current) ? current : "");
        }

        private void ServerFilterChanged()
        {
            Grid.ClearMarks();
            if (Grid.PageNumber != 1) Grid.GoToPage(1);
            else Refresh();
        }

        private void ResetFilters()
        {
            TypeFilter.SetValue("");
            CaptainFilter.SetValue("");
            UnreadOnly.SetValue(false, false);
            ServerFilterChanged();
        }

        private async Task<GridPage<Signal>> LoadAsync(GridQuery query, CancellationToken token)
        {
            EnumerationResult<Signal>? result = await Context.Client.ListSignalsAsync(BuildQuery(query.PageNumber, query.PageSize), token).ConfigureAwait(false);
            List<Signal> rows = result?.Objects ?? new List<Signal>();
            long total = result?.TotalRecords ?? rows.Count;
            Context.Dispatcher.Post(() =>
            {
                _PageRows = rows;
                _ServerTotal = total;
            });
            return new GridPage<Signal>(SortAndFilter(rows, query.SortKey, query.SortDescending), total);
        }

        private void ApplyColumnFilters()
        {
            Grid.SetPage(new GridPage<Signal>(SortAndFilter(_PageRows, Grid.SortKey, Grid.SortDescending), _ServerTotal), Grid.PageNumber);
        }

        private List<Signal> SortAndFilter(List<Signal> rows, string? sortKey, bool descending)
        {
            string type = TypeColumn.Value.Trim();
            string from = FromColumn.Value.Trim();
            string to = ToColumn.Value.Trim();
            string payload = PayloadColumn.Value.Trim();
            IEnumerable<Signal> q = rows.Where(s =>
                (type.Length == 0 || s.Type.ToString().Contains(type, StringComparison.OrdinalIgnoreCase))
                && (from.Length == 0 || CaptainName(s.FromCaptainId).Contains(from, StringComparison.OrdinalIgnoreCase))
                && (to.Length == 0 || CaptainName(s.ToCaptainId).Contains(to, StringComparison.OrdinalIgnoreCase))
                && (payload.Length == 0 || (s.Payload ?? "").Contains(payload, StringComparison.OrdinalIgnoreCase)));
            if (String.IsNullOrEmpty(sortKey)) return q.ToList();
            Func<Signal, string> key;
            switch (sortKey)
            {
                case "id": key = s => s.Id; break;
                case "type": key = s => s.Type.ToString(); break;
                case "fromCaptainId": key = s => s.FromCaptainId ?? ""; break;
                case "toCaptainId": key = s => s.ToCaptainId ?? ""; break;
                default: key = s => s.CreatedUtc.ToString("o"); break;
            }

            return (descending ? q.OrderByDescending(key, StringComparer.OrdinalIgnoreCase) : q.OrderBy(key, StringComparer.OrdinalIgnoreCase)).ToList();
        }

        #endregion
    }
}
