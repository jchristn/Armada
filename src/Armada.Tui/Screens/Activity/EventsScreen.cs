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
    /// Activity, Events tab (dashboard <c>Events.tsx</c>): the system event log with server paging, the admin user
    /// scope, column filters on event type, entity type, and message, sorting by event type, entity type, and
    /// created time within the page, links to the captain, mission, vessel, and voyage, a row menu (View Detail,
    /// View JSON, Delete), and bulk delete of the selected rows. Not thread-safe.
    /// </summary>
    public class EventsScreen : GridScreen<ArmadaEvent>
    {
        #region Public-Members

        /// <summary>
        /// Column filters.
        /// </summary>
        public FilterStrip Filters { get; }

        /// <summary>
        /// Admin user scope.
        /// </summary>
        public UserScopeField UserScope { get; }

        /// <summary>
        /// Event type filter (client-side, current page).
        /// </summary>
        public InputField EventTypeFilter { get; } = new InputField();

        /// <summary>
        /// Entity type filter (client-side, current page).
        /// </summary>
        public InputField EntityTypeFilter { get; } = new InputField();

        /// <summary>
        /// Message filter (client-side, current page).
        /// </summary>
        public InputField MessageFilter { get; } = new InputField();

        /// <summary>
        /// Rows of the last page fetched (before column filters).
        /// </summary>
        public IReadOnlyList<ArmadaEvent> PageRows
        {
            get { return _PageRows; }
        }

        #endregion

        #region Private-Members

        private readonly Button _DeleteSelected;
        private List<ArmadaEvent> _PageRows = new List<ArmadaEvent>();
        private long _ServerTotal = 0;
        private Dictionary<string, string> _CaptainNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private Dictionary<string, string> _VesselNames = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public EventsScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Events", "System event log capturing state changes, completions, failures, and other notable occurrences.", "events", e => e.Id)
        {
            Header.AddButton("Refresh", Refresh, "F5");
            _DeleteSelected = Header.AddButton("Delete Selected", OnDeleteSelected, "Del");
            _DeleteSelected.Visible = false;
            AddFixed(Header, w => Header.HeightFor(w));

            Filters = new FilterStrip();
            UserScope = Filters.Add("User", new UserScopeField(context), 28);
            UserScope.ValueChanged += (s, e) => { Grid.GoToPage(1); Refresh(); };
            EventTypeFilter.Placeholder = "Filter...";
            EntityTypeFilter.Placeholder = "Filter...";
            MessageFilter.Placeholder = "Filter...";
            Filters.Add("Event Type", EventTypeFilter, 18);
            Filters.Add("Entity Type", EntityTypeFilter, 14);
            Filters.Add("Message", MessageFilter, 24);
            EventTypeFilter.ValueChanged += (s, e) => ApplyColumnFilters();
            EntityTypeFilter.ValueChanged += (s, e) => ApplyColumnFilters();
            MessageFilter.ValueChanged += (s, e) => ApplyColumnFilters();
            AddFixed(Filters, w => Filters.HeightFor(w));

            BuildColumns();
            Grid.EmptyText = "No events found.";
            Grid.Loader = LoadAsync;
            BindPreferences("createdUtc", true, 50);
            AddFill(Grid);
            Scope.Focus(Grid);
            Grid.Reload();
            ScreenOps.Quiet(context, () => context.Client.ListCaptainsAsync(new ArmadaPageQuery(1, 1000)), r =>
            {
                _CaptainNames = (r?.Objects ?? new List<Captain>()).ToDictionary(c => c.Id, c => c.Name, StringComparer.Ordinal);
            });
            ScreenOps.Quiet(context, () => context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 1000)), r =>
            {
                _VesselNames = (r?.Objects ?? new List<Vessel>()).ToDictionary(v => v.Id, v => v.Name, StringComparer.Ordinal);
            });
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Captain display name (the name, else the first 8 characters of the id).
        /// </summary>
        /// <param name="id">Captain id.</param>
        /// <returns>Name.</returns>
        public string CaptainName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            return _CaptainNames.TryGetValue(id, out string? name) && !String.IsNullOrEmpty(name) ? name : id.Substring(0, Math.Min(8, id.Length));
        }

        /// <summary>
        /// Vessel display name (the name, else the first 8 characters of the id).
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <returns>Name.</returns>
        public string VesselName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            return _VesselNames.TryGetValue(id, out string? name) && !String.IsNullOrEmpty(name) ? name : id.Substring(0, Math.Min(8, id.Length));
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            _DeleteSelected.Visible = Grid.Marked.Count > 0;
            _DeleteSelected.Hint = "(" + Grid.Marked.Count + ")";
        }

        /// <inheritdoc />
        protected override void OnActivate(ArmadaEvent row)
        {
            Context.Navigate("/events/" + row.Id);
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(ArmadaEvent row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("View Detail", () => Context.Navigate("/events/" + row.Id), "Enter"));
            items.Add(new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), row), "j"));
            string? entityRoute = ScreenOps.EntityRoute(row.EntityType, row.EntityId);
            if (entityRoute != null) items.Add(new ActionMenuItem("Open Entity", () => Context.Navigate(entityRoute)));
            if (!String.IsNullOrEmpty(row.CaptainId)) items.Add(new ActionMenuItem("Open Captain", () => Context.Navigate("/captains/" + row.CaptainId)));
            if (!String.IsNullOrEmpty(row.MissionId)) items.Add(new ActionMenuItem("Open Mission", () => Context.Navigate("/missions/" + row.MissionId)));
            if (!String.IsNullOrEmpty(row.VesselId)) items.Add(new ActionMenuItem("Open Vessel", () => Context.Navigate("/vessels/" + row.VesselId)));
            if (!String.IsNullOrEmpty(row.VoyageId)) items.Add(new ActionMenuItem("Open Voyage", () => Context.Navigate("/voyages/" + row.VoyageId)));
            ActionMenuItem delete = new ActionMenuItem("Delete", () => OnDeleteRow(row), "Del");
            delete.Destructive = true;
            items.Add(delete);
            return items;
        }

        /// <inheritdoc />
        protected override string JsonTitle(ArmadaEvent row)
        {
            return Context.Loc.T("Event") + ": " + row.Id;
        }

        /// <inheritdoc />
        protected override bool SupportsDelete()
        {
            return true;
        }

        /// <inheritdoc />
        protected override void OnDeleteRow(ArmadaEvent row)
        {
            string id = row.Id;
            Context.Confirm("Delete Event", Context.Loc.T("Delete event {{id}}?", LocalizationArgs.Of("id", id)), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.DeleteEventsBatchAsync(new List<string> { id }), r =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Event {{id}} deleted.", LocalizationArgs.Of("id", id));
                    Refresh();
                }, "Delete failed.");
            }, "Delete");
        }

        /// <inheritdoc />
        protected override void OnDeleteSelected()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Context.Confirm("Delete Selected Events", Context.Loc.T("Delete {{count}} selected event(s)?", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.DeleteEventsBatchAsync(ids), r =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Deleted {{count}} event(s).", LocalizationArgs.Of("count", ids.Count));
                    Grid.ClearMarks();
                    Refresh();
                }, "Bulk delete failed.");
            }, "Delete");
        }

        #endregion

        #region Private-Methods

        private void BuildColumns()
        {
            Grid.AddColumn(new GridColumn<ArmadaEvent>("id", "ID", e => e.Id) { Width = 20, Style = (e, t) => t.Muted });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("eventType", "Event Type", e => e.EventType) { Width = 20, Sortable = true });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("entityType", "Entity Type", e => e.EntityType ?? "-") { Width = 12, Sortable = true, Style = (e, t) => t.Muted });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("entityId", "Entity ID", e => e.EntityId ?? "-") { Width = 20, Style = (e, t) => t.Link });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("captain", "Captain", e => CaptainName(e.CaptainId)) { Width = 12 });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("mission", "Mission", e => e.MissionId ?? "-") { Width = 24, DefaultVisible = false });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("vessel", "Vessel", e => VesselName(e.VesselId)) { Width = 12 });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("voyage", "Voyage", e => e.VoyageId ?? "-") { Width = 24, DefaultVisible = false });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("message", "Message", e => e.Message) { Weight = 3 });
            Grid.AddColumn(new GridColumn<ArmadaEvent>("createdUtc", "Created", e => ScreenOps.Relative(Context, e.CreatedUtc)) { Width = 10, Sortable = true, Style = (e, t) => t.Muted });
        }

        private async Task<GridPage<ArmadaEvent>> LoadAsync(GridQuery query, CancellationToken token)
        {
            ArmadaPageQuery page = new ArmadaPageQuery(query.PageNumber, query.PageSize);
            page.With("userId", UserScope.UserId);
            EnumerationResult<ArmadaEvent>? result = await Context.Client.ListEventsAsync(page, token).ConfigureAwait(false);
            List<ArmadaEvent> rows = result?.Objects ?? new List<ArmadaEvent>();
            long total = result?.TotalRecords ?? rows.Count;
            Context.Dispatcher.Post(() =>
            {
                _PageRows = rows;
                _ServerTotal = total;
            });
            return new GridPage<ArmadaEvent>(SortAndFilter(rows, query.SortKey, query.SortDescending), total);
        }

        private void ApplyColumnFilters()
        {
            Grid.SetPage(new GridPage<ArmadaEvent>(SortAndFilter(_PageRows, Grid.SortKey, Grid.SortDescending), _ServerTotal), Grid.PageNumber);
        }

        private List<ArmadaEvent> SortAndFilter(List<ArmadaEvent> rows, string? sortKey, bool descending)
        {
            string type = EventTypeFilter.Value.Trim();
            string entity = EntityTypeFilter.Value.Trim();
            string message = MessageFilter.Value.Trim();
            IEnumerable<ArmadaEvent> q = rows.Where(e =>
                (type.Length == 0 || (e.EventType ?? "").Contains(type, StringComparison.OrdinalIgnoreCase))
                && (entity.Length == 0 || (e.EntityType ?? "").Contains(entity, StringComparison.OrdinalIgnoreCase))
                && (message.Length == 0 || (e.Message ?? "").Contains(message, StringComparison.OrdinalIgnoreCase)));
            Func<ArmadaEvent, string> key;
            switch (sortKey)
            {
                case "eventType":
                    key = e => (e.EventType ?? "").ToLowerInvariant();
                    break;
                case "entityType":
                    key = e => (e.EntityType ?? "").ToLowerInvariant();
                    break;
                default:
                    key = e => e.CreatedUtc.ToString("o");
                    break;
            }

            List<ArmadaEvent> list = (descending ? q.OrderByDescending(key, StringComparer.Ordinal) : q.OrderBy(key, StringComparer.Ordinal)).ToList();
            return list;
        }

        #endregion
    }
}
