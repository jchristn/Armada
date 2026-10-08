namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;
    using ChartSeries = Armada.Tui.Widgets.ChartSeries;
    using Button = Armada.Tui.Widgets.Button;
    using TabStrip = Armada.Tui.Widgets.TabStrip;

    /// <summary>
    /// Activity, API Requests tab (dashboard <c>RequestHistory.tsx</c>; also <c>/requests/:id</c>, which opens the
    /// detail drawer for that entry): captured API traffic with summary KPIs (total, success rate, failures, average
    /// duration), the activity chart over Last Hour, Last Day, Last Week, or Last Month (success and failure stacked),
    /// collapsible filters (method, status code, route, principal, credential, result, tenant for global admins, user
    /// for admins, from, to; Reset), server-side paging, a row menu (View, Replay in API Explorer, Delete), bulk
    /// deletes (Delete Selected; Delete Filtered or Delete Visible Range for the whole filtered set), and the request
    /// detail drawer (summary, parameters, headers, bodies with truncation notes; Replay, Delete, Copy). Not
    /// thread-safe.
    /// </summary>
    public class RequestHistoryScreen : GridScreen<RequestHistoryEntry>, IRegionOverlayHost
    {
        #region Public-Members

        /// <inheritdoc />
        public IWidget? RegionOverlay
        {
            get { return DetailDrawer.IsOpen ? DetailDrawer : null; }
        }

        /// <inheritdoc />
        public Rect RegionOverlayRect { get; private set; } = Rect.Empty;

        /// <summary>
        /// Summary cards.
        /// </summary>
        public KpiBar Kpis { get; } = new KpiBar();

        /// <summary>
        /// Activity range tabs.
        /// </summary>
        public TabStrip RangeTabs { get; } = new TabStrip();

        /// <summary>
        /// Activity chart.
        /// </summary>
        public MultiSeriesChart Chart { get; } = new MultiSeriesChart();

        /// <summary>
        /// Filters (collapsed by default, like the dashboard).
        /// </summary>
        public FilterStrip Filters { get; } = new FilterStrip();

        /// <summary>
        /// Method filter.
        /// </summary>
        public SelectField<string> MethodFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Status code filter.
        /// </summary>
        public InputField StatusCodeFilter { get; } = new InputField();

        /// <summary>
        /// Route filter.
        /// </summary>
        public InputField RouteFilter { get; } = new InputField();

        /// <summary>
        /// Principal filter.
        /// </summary>
        public InputField PrincipalFilter { get; } = new InputField();

        /// <summary>
        /// Credential filter.
        /// </summary>
        public InputField CredentialFilter { get; } = new InputField();

        /// <summary>
        /// Result filter ("all", "true", "false").
        /// </summary>
        public SelectField<string> ResultFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Tenant filter (global admins only).
        /// </summary>
        public InputField TenantFilter { get; } = new InputField();

        /// <summary>
        /// User filter (global and tenant admins).
        /// </summary>
        public InputField UserFilter { get; } = new InputField();

        /// <summary>
        /// From filter (local time).
        /// </summary>
        public DateField FromFilter { get; } = new DateField();

        /// <summary>
        /// To filter (local time).
        /// </summary>
        public DateField ToFilter { get; } = new DateField();

        /// <summary>
        /// The detail drawer.
        /// </summary>
        public Drawer DetailDrawer { get; } = new Drawer();

        /// <summary>
        /// The drawer body.
        /// </summary>
        public RequestHistoryDetailPane Detail { get; } = new RequestHistoryDetailPane();

        /// <summary>
        /// Last summary, or null.
        /// </summary>
        public RequestHistorySummaryResult? Summary { get; private set; } = null;

        /// <summary>
        /// Selected activity range.
        /// </summary>
        public RequestHistoryRange Range { get; private set; } = RequestHistoryRange.Find("lastDay");

        /// <summary>
        /// True when any filter other than the date range is set (labels the bulk button Delete Filtered).
        /// </summary>
        public bool HasActiveFilters
        {
            get
            {
                return !String.IsNullOrEmpty(MethodFilter.Value) || RouteFilter.Value.Length > 0 || StatusCodeFilter.Value.Length > 0
                    || PrincipalFilter.Value.Length > 0 || TenantFilter.Value.Length > 0 || UserFilter.Value.Length > 0
                    || CredentialFilter.Value.Length > 0 || (ResultFilter.Value ?? "all") != "all";
            }
        }

        /// <summary>
        /// Delete Selected button.
        /// </summary>
        public Button DeleteSelectedButton { get; }

        /// <summary>
        /// Delete Filtered / Delete Visible Range button.
        /// </summary>
        public Button DeleteFilteredButton { get; }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                if (DetailDrawer.IsOpen)
                {
                    return new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("r", "Replay"),
                        new KeyValuePair<string, string>("Del", "Delete"),
                        new KeyValuePair<string, string>("y", "Copy"),
                        new KeyValuePair<string, string>("Esc", "Close"),
                    };
                }

                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>(base.Hints);
                hints.Add(new KeyValuePair<string, string>("f", "Filters"));
                hints.Add(new KeyValuePair<string, string>("p", "Replay"));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private readonly Button _FiltersButton;
        private readonly Button _ResetButton;
        private readonly TextBlock _ActivityTitle = new TextBlock("", t => t.Accent);
        private bool _SummaryLoading = true;
        private int _SummaryGeneration = 0;
        private string? _DetailRouteId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public RequestHistoryScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Requests", "Inspect captured Armada API traffic, filter by route or principal, and replay stored requests into API Explorer.", "request-history", e => e.Id)
        {
            DeleteSelectedButton = Header.AddButton("Delete Selected", OnDeleteSelected, "Del");
            DeleteFilteredButton = Header.AddButton("Delete Filtered", ConfirmDeleteFiltered);
            _FiltersButton = Header.AddButton("Filters", ToggleFilters, "f");
            _ResetButton = Header.AddButton("Reset", ResetFilters);
            Header.AddButton("Refresh", Refresh, "F5");
            DeleteSelectedButton.Visible = false;
            DeleteFilteredButton.Visible = false;
            _ResetButton.Visible = false;
            AddFixed(Header, w => Header.HeightFor(w));
            AddFixed(Kpis, w => Kpis.PreferredHeight);

            foreach (RequestHistoryRange r in RequestHistoryRange.All) RangeTabs.Add(r.Id, r.Label);
            RangeTabs.SelectKey(Range.Id);
            RangeTabs.SelectedChanged += (s, e) =>
            {
                Range = RequestHistoryRange.Find(e.NewValue);
                LoadSummary();
            };
            _ActivityTitle.Text = "Activity";
            AddFixed(_ActivityTitle, w => 1);
            AddFixed(RangeTabs, w => 1);
            Chart.Kind = ChartKindEnum.Bar;
            Chart.EmptyText = "No requests in this time range. Widen the range above to see older traffic.";
            AddFixed(Chart, w => 14);

            BuildFilters(context);
            Filters.Visible = false;
            AddFixed(Filters, w => Filters.HeightFor(w));

            BuildColumns();
            Grid.EmptyText = "No request history entries match the current filters.";
            Grid.Loader = LoadAsync;
            BindPreferences(null, false, 25);
            AddFill(Grid);

            DetailDrawer.WidthRatio = 0.6;
            DetailDrawer.Localizer = context.Loc;
            DetailDrawer.ApplyTheme(context.Theme.Current);
            DetailDrawer.Closed += (s, e) => OnDrawerClosed();
            Detail.ReplayRequested += (s, e) => { if (Detail.Record != null) Replay(Detail.Record.Entry.Id); };
            Detail.DeleteRequested += (s, e) => { if (Detail.Record != null) OnDeleteRow(Detail.Record.Entry); };
            Detail.CopyRequested += (s, e) => CopyBlock();
            Detail.CloseRequested += (s, e) => DetailDrawer.Close();

            Scope.Focus(Grid);
            Grid.Reload();
            LoadSummary();
            if (route.Parameters.TryGetValue("id", out string? id) && !String.IsNullOrEmpty(id))
            {
                _DetailRouteId = id;
                OpenDetail(id);
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Refresh()
        {
            Grid.Reload();
            LoadSummary();
        }

        /// <summary>
        /// Build the list query from the filters (the dashboard's <c>query</c>).
        /// </summary>
        /// <param name="pageNumber">Page number.</param>
        /// <param name="pageSize">Page size.</param>
        /// <returns>Query.</returns>
        public RequestHistoryQuery BuildQuery(int pageNumber, int pageSize)
        {
            RequestHistoryQuery q = BaseQuery();
            q.PageNumber = pageNumber;
            q.PageSize = pageSize;
            q.FromUtc = FromFilter.ValueUtc;
            q.ToUtc = ToFilter.ValueUtc;
            return q;
        }

        /// <summary>
        /// Build the summary query (filters plus the activity window and bucket size).
        /// </summary>
        /// <returns>Query.</returns>
        public RequestHistoryQuery BuildSummaryQuery()
        {
            RequestHistoryQuery q = BaseQuery();
            DateTime now = Context.Clock.UtcNow;
            q.FromUtc = Range.StartUtc(now);
            q.ToUtc = Range.EndUtc(now);
            q.BucketMinutes = Range.BucketMinutes;
            return q;
        }

        /// <summary>
        /// Open the detail drawer for an entry and load it.
        /// </summary>
        /// <param name="id">Entry id.</param>
        public void OpenDetail(string id)
        {
            Detail.ShowLoading();
            DetailDrawer.Open("Request Detail", Detail);
            DetailDrawer.OnFocusChanged(true);
            ScreenOps.Run(Context, () => Context.Client.GetRequestHistoryEntryAsync(id), record =>
            {
                if (record == null)
                {
                    DetailDrawer.Close();
                    return;
                }

                Detail.Show(record, Context.Loc);
            }, "Failed to load request detail.", ex => DetailDrawer.Close());
        }

        /// <summary>
        /// Replay an entry in the API Explorer.
        /// </summary>
        /// <param name="id">Entry id.</param>
        public void Replay(string id)
        {
            Context.Navigate("/api-explorer?replay=" + Uri.EscapeDataString(id));
        }

        /// <summary>
        /// Switch the activity range and reload the summary.
        /// </summary>
        /// <param name="id">Range id ("lastHour", "lastDay", "lastWeek", "lastMonth").</param>
        public void SetRange(string id)
        {
            Range = RequestHistoryRange.Find(id);
            RangeTabs.SelectKey(Range.Id);
            LoadSummary();
        }

        /// <summary>
        /// Show or hide the filters.
        /// </summary>
        public void ToggleFilters()
        {
            Filters.Visible = !Filters.Visible;
            _ResetButton.Visible = Filters.Visible;
            if (Filters.Visible) Scope.Focus(Filters);
            else if (!ReferenceEquals(Scope.Focused, Grid)) Scope.Focus(Grid);
        }

        /// <summary>
        /// Reset the filters to the defaults (last 24 hours) and reload.
        /// </summary>
        public void ResetFilters()
        {
            MethodFilter.SetValue("");
            StatusCodeFilter.Value = "";
            RouteFilter.Value = "";
            PrincipalFilter.Value = "";
            CredentialFilter.Value = "";
            ResultFilter.SetValue("all");
            TenantFilter.Value = "";
            UserFilter.Value = "";
            DefaultDates();
            ApplyFilters();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (DetailDrawer.IsOpen)
            {
                if (Detail.HandleKey(key)) return true;
                if (key.Code == KeyCode.Escape)
                {
                    DetailDrawer.Close();
                    return true;
                }

                bool chord = (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0;
                bool function = key.Code >= KeyCode.F1 && key.Code <= KeyCode.F12;
                return !chord && !function;
            }

            return base.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            base.Render(surface);
            RegionOverlayRect = Rect.Empty;
            if (!DetailDrawer.IsOpen) return;
            Rect rect = DetailDrawer.RectIn(surface.Size);
            DetailDrawer.Render(new SurfaceView(surface, rect));
            // The drawer's left rule is its box's left edge (see RegionFrames).
            RegionOverlayRect = new Rect(rect.X + 1, rect.Y, Math.Max(1, rect.Width - 1), rect.Height);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            DeleteSelectedButton.Visible = Grid.Marked.Count > 0;
            DeleteSelectedButton.Hint = "(" + Grid.Marked.Count + ")";
            DeleteFilteredButton.Visible = Grid.TotalRecords > 0;
            DeleteFilteredButton.Label = HasActiveFilters ? "Delete Filtered" : "Delete Visible Range";
            _FiltersButton.Hint = HasActiveFilters ? "f [" + Context.Loc.T("Active") + "]" : "f";
            _ActivityTitle.Text = Context.Loc.T("Activity") + "  " + Context.Loc.T("Bucketed request volume with success and failure breakdown.");
            _ActivityTitle.Translate = false;
            if (_SummaryLoading) Chart.EmptyText = "Loading summary...";
            else Chart.EmptyText = "No requests in this time range. Widen the range above to see older traffic.";
        }

        /// <inheritdoc />
        protected override void OnActivate(RequestHistoryEntry row)
        {
            OpenDetail(row.Id);
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(RequestHistoryEntry row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("View", () => OpenDetail(row.Id), "Enter"));
            items.Add(new ActionMenuItem("Replay in API Explorer", () => Replay(row.Id), "p"));
            items.Add(new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), row), "j"));
            ActionMenuItem delete = new ActionMenuItem("Delete", () => OnDeleteRow(row), "Del");
            delete.Destructive = true;
            items.Add(delete);
            return items;
        }

        /// <inheritdoc />
        protected override string JsonTitle(RequestHistoryEntry row)
        {
            return Context.Loc.T("Request") + ": " + row.Id;
        }

        /// <inheritdoc />
        protected override bool SupportsDelete()
        {
            return true;
        }

        /// <inheritdoc />
        protected override void OnDeleteRow(RequestHistoryEntry row)
        {
            string id = row.Id;
            Context.Confirm("Delete Request Entry", Context.Loc.T("Delete the stored request-history entry for {{route}}?", LocalizationArgs.Of("route", row.Route)), () =>
            {
                ScreenOps.RunVoid(Context, () => Context.Client.DeleteRequestHistoryEntryAsync(id), () =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Request entry deleted.");
                    if (DetailDrawer.IsOpen && Detail.Record != null && Detail.Record.Entry.Id == id) DetailDrawer.Close();
                    Refresh();
                }, "Failed to delete request history entry.");
            }, "Delete");
        }

        /// <inheritdoc />
        protected override void OnDeleteSelected()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Context.Confirm("Delete Selected Requests", Context.Loc.T("Delete {{count}} selected request-history entries?", LocalizationArgs.Of("count", ids.Count)), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.DeleteRequestHistoryEntriesAsync(ids), r =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Deleted {{count}} request entries.", LocalizationArgs.Of("count", ids.Count));
                    Grid.ClearMarks();
                    Refresh();
                }, "Failed to delete selected request entries.");
            }, "Delete Selected");
        }

        /// <inheritdoc />
        protected override IEnumerable<ArmadaCommand> ExtraCommands()
        {
            return new List<ArmadaCommand>
            {
                Command(ScreenKey + ".replay", "Replay in API Explorer", () => { RequestHistoryEntry? row = Grid.Current; if (row != null) Replay(row.Id); }, () => Grid.Current != null, "p"),
                Command(ScreenKey + ".filters", "Filters", ToggleFilters, null, "f"),
                Command(ScreenKey + ".reset", "Reset", ResetFilters, null),
                Command(ScreenKey + ".delete-filtered", "Delete Filtered", ConfirmDeleteFiltered, () => Grid.TotalRecords > 0),
            };
        }

        #endregion

        #region Private-Methods

        private void BuildFilters(TuiContext context)
        {
            MethodFilter.ModalHost = context.Modals;
            MethodFilter.PickerTitle = "Method";
            MethodFilter.Options = new List<SelectOption<string>> { new SelectOption<string>("", context.Loc.T("All methods")) };
            foreach (string m in new string[] { "GET", "POST", "PUT", "PATCH", "DELETE" }) MethodFilter.Options.Add(new SelectOption<string>(m, m));
            MethodFilter.SetValue("");
            ResultFilter.ModalHost = context.Modals;
            ResultFilter.PickerTitle = "Result";
            ResultFilter.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("all", context.Loc.T("All")),
                new SelectOption<string>("true", context.Loc.T("Success")),
                new SelectOption<string>("false", context.Loc.T("Failure")),
            };
            ResultFilter.SetValue("all");
            StatusCodeFilter.Placeholder = "200";
            RouteFilter.Placeholder = "/api/v1/missions";
            PrincipalFilter.Placeholder = "user@tenant";
            CredentialFilter.Placeholder = "cred_";
            TenantFilter.Placeholder = "ten_";
            UserFilter.Placeholder = "usr_";
            Filters.Add("Method", MethodFilter, 12);
            Filters.Add("Status Code", StatusCodeFilter, 6);
            Filters.Add("Route", RouteFilter, 22);
            Filters.Add("Principal", PrincipalFilter, 16);
            Filters.Add("Credential", CredentialFilter, 14);
            Filters.Add("Result", ResultFilter, 10);
            Filters.Add("Tenant", TenantFilter, 14);
            Filters.Add("User", UserFilter, 14);
            Filters.Add("From", FromFilter, 17);
            Filters.Add("To", ToFilter, 17);
            TenantFilter.Visible = context.Session.IsGlobalAdmin;
            UserFilter.Visible = context.Session.IsGlobalAdmin || context.Session.IsTenantAdmin;
            DefaultDates();
            MethodFilter.ValueChanged += (s, e) => ApplyFilters();
            ResultFilter.ValueChanged += (s, e) => ApplyFilters();
            Filters.Applied += (s, e) => ApplyFilters();
        }

        private void DefaultDates()
        {
            DateTime now = Context.Clock.UtcNow.ToLocalTime();
            FromFilter.Value = now.AddHours(-24).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            ToFilter.Value = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private void ApplyFilters()
        {
            if (!FromFilter.ValidateField() || !ToFilter.ValidateField()) return;
            Grid.ClearMarks();
            if (Grid.PageNumber != 1) Grid.GoToPage(1);
            Refresh();
        }

        private RequestHistoryQuery BaseQuery()
        {
            RequestHistoryQuery q = new RequestHistoryQuery();
            q.Method = Blank(MethodFilter.Value);
            q.Route = Blank(RouteFilter.Value);
            q.Principal = Blank(PrincipalFilter.Value);
            q.TenantId = TenantFilter.Visible ? Blank(TenantFilter.Value) : null;
            q.UserId = UserFilter.Visible ? Blank(UserFilter.Value) : null;
            q.CredentialId = Blank(CredentialFilter.Value);
            q.StatusCode = Int32.TryParse(StatusCodeFilter.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) ? code : (int?)null;
            string result = ResultFilter.Value ?? "all";
            q.IsSuccess = result == "all" ? (bool?)null : result == "true";
            return q;
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private void BuildColumns()
        {
            Grid.AddColumn(new GridColumn<RequestHistoryEntry>("when", "When", e => ScreenOps.Relative(Context, e.CreatedUtc)) { Width = 10, Style = (e, t) => t.Muted });
            Grid.AddColumn(new GridColumn<RequestHistoryEntry>("method", "Method", e => e.Method) { Width = 7, Style = (e, t) => t.Accent });
            Grid.AddColumn(new GridColumn<RequestHistoryEntry>("route", "Route", e => e.Route) { Weight = 4, Style = (e, t) => t.Code });
            Grid.AddColumn(new GridColumn<RequestHistoryEntry>("principal", "Principal", e => String.IsNullOrEmpty(e.PrincipalDisplay) ? Context.Loc.T("Anonymous") : e.PrincipalDisplay!) { Weight = 2 });
            Grid.AddColumn(new GridColumn<RequestHistoryEntry>("status", "Status", e => (e.IsSuccess ? "" : "! ") + e.StatusCode.ToString(CultureInfo.InvariantCulture)) { Width = 7, Style = (e, t) => e.IsSuccess ? t.Success : t.Error });
            Grid.AddColumn(new GridColumn<RequestHistoryEntry>("duration", "Duration", e => RequestHistoryFormat.Ms(e.DurationMs)) { Width = 11, Align = CellAlignment.Right });
            Grid.AddColumn(new GridColumn<RequestHistoryEntry>("payloads", "Payloads", e => RequestHistoryFormat.Bytes(e.RequestSizeBytes) + " / " + RequestHistoryFormat.Bytes(e.ResponseSizeBytes)) { Width = 17, Style = (e, t) => t.Muted });
        }

        private async Task<GridPage<RequestHistoryEntry>> LoadAsync(GridQuery query, CancellationToken token)
        {
            RequestHistoryQuery q = BuildQuery(query.PageNumber, query.PageSize);
            EnumerationResult<RequestHistoryEntry>? result = await Context.Client.ListRequestHistoryAsync(q, token).ConfigureAwait(false);
            List<RequestHistoryEntry> rows = result?.Objects ?? new List<RequestHistoryEntry>();
            return new GridPage<RequestHistoryEntry>(rows, result?.TotalRecords ?? rows.Count);
        }

        private void LoadSummary()
        {
            int generation = ++_SummaryGeneration;
            _SummaryLoading = true;
            UpdateKpis();
            RequestHistoryQuery q = BuildSummaryQuery();
            RequestHistoryRange range = Range;
            DateTime now = Context.Clock.UtcNow;
            ScreenOps.Run(Context, () => Context.Client.GetRequestHistorySummaryAsync(q), summary =>
            {
                if (generation != _SummaryGeneration) return;
                _SummaryLoading = false;
                Summary = summary;
                UpdateKpis();
                UpdateChart(range, now);
            }, "Failed to load request history summary.", ex =>
            {
                if (generation == _SummaryGeneration) _SummaryLoading = false;
            });
        }

        private void UpdateKpis()
        {
            RequestHistorySummaryResult? s = Summary;
            string dots = "...";
            Kpis.SetCards(new List<KpiCard>
            {
                new KpiCard("Total Requests", _SummaryLoading ? dots : Context.Loc.FormatNumber(s?.TotalCount ?? 0), null, Context.Loc.T("Current activity window")),
                new KpiCard("Success Rate", _SummaryLoading ? dots : (s?.SuccessRate ?? 0).ToString("0.0", CultureInfo.InvariantCulture) + "%", t => t.Success, Context.Loc.T("Based on visible summary range")),
                new KpiCard("Failures", _SummaryLoading ? dots : Context.Loc.FormatNumber(s?.FailureCount ?? 0), t => t.Error, Context.Loc.T("Non-successful responses")),
                new KpiCard("Average Duration", _SummaryLoading ? dots : RequestHistoryFormat.Ms(s?.AverageDurationMs ?? 0), null, Context.Loc.T("Across summary buckets")),
            });
        }

        private void UpdateChart(RequestHistoryRange range, DateTime nowUtc)
        {
            DateTime start = range.StartUtc(nowUtc);
            long bucketTicks = TimeSpan.FromMinutes(range.BucketMinutes).Ticks;
            Dictionary<long, RequestHistorySummaryBucket> byStart = new Dictionary<long, RequestHistorySummaryBucket>();
            foreach (RequestHistorySummaryBucket b in Summary?.Buckets ?? new List<RequestHistorySummaryBucket>())
            {
                DateTime utc = b.BucketStartUtc.Kind == DateTimeKind.Local ? b.BucketStartUtc.ToUniversalTime() : b.BucketStartUtc;
                byStart[(utc.Ticks / bucketTicks) * bucketTicks] = b;
            }

            List<string> labels = new List<string>();
            List<double> success = new List<double>();
            List<double> failed = new List<double>();
            for (int i = 0; i < range.SliceCount; i++)
            {
                long ticks = start.Ticks + i * bucketTicks;
                byStart.TryGetValue(ticks, out RequestHistorySummaryBucket? bucket);
                success.Add(bucket?.SuccessCount ?? 0);
                failed.Add(bucket?.FailureCount ?? 0);
                labels.Add(ChartLabel(new DateTime(ticks, DateTimeKind.Utc), range));
            }

            Chart.SetData(labels, new List<ChartSeries> { new ChartSeries("Success", success), new ChartSeries("Failed", failed) });
        }

        private string ChartLabel(DateTime utc, RequestHistoryRange range)
        {
            DateTime local = utc.ToLocalTime();
            CultureInfo culture = (Context.Loc as LocalizationService)?.Culture ?? CultureInfo.InvariantCulture;
            if (range.Id == "lastHour" || range.Id == "lastDay") return local.ToString("t", culture);
            if (range.Id == "lastWeek") return local.ToString("ddd HH", culture) + "h";
            return local.ToString("MMM d", culture);
        }

        private void ConfirmDeleteFiltered()
        {
            if (Grid.TotalRecords <= 0) return;
            RequestHistoryQuery q = BuildQuery(1, Grid.PageSize);
            Context.Confirm("Delete Filtered Requests", Context.Loc.T("Delete all request-history entries matching the current filters?"), () =>
            {
                ScreenOps.Run(Context, () => Context.Client.DeleteRequestHistoryByFilterAsync(q), r =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "Deleted the current filtered request set.");
                    Grid.ClearMarks();
                    if (Grid.PageNumber != 1) Grid.GoToPage(1);
                    Refresh();
                }, "Failed to delete filtered request entries.");
            }, "Delete Filtered");
        }

        private void CopyBlock()
        {
            List<RequestHistoryBlock> blocks = Detail.Blocks();
            if (blocks.Count == 0) return;
            List<ActionMenuItem> items = blocks.Select(b => new ActionMenuItem(Context.Loc.T("Copy") + " " + Context.Loc.T(b.Title), () => Context.Clipboard.Copy(b.Text, b.Title)) { Key = b.Kind.ToString() }).ToList();
            items.Add(new ActionMenuItem("Copy all", () => Context.Clipboard.Copy(Detail.View.PlainText, "Request Detail")));
            ActionMenu.Show(Context.Modals, "Copy", items, Context.Loc, Context.Theme.Current);
        }

        private void OnDrawerClosed()
        {
            DetailDrawer.OnFocusChanged(false);
            if (_DetailRouteId != null)
            {
                _DetailRouteId = null;
                Context.Navigate("/activity?source=requests");
            }
        }

        #endregion
    }
}
