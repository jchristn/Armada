namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Home (W3.1, <c>/</c>), the dashboard's System Status page as a scrolling page of sections: header actions
    /// (Setup Wizard, Dispatch, New Voyage), the Ask band (Ask Armada, Needs You, Dispatch, Diagnostics), alert banners
    /// (stalled captains, failed missions, failed landings, pending work with idle captains, pending work with no
    /// captains) with View, KPI tiles (captains, active voyages with memory-pressure deferrals, missions by status,
    /// active fleet action runs), tenant-admin shortcuts (Import repositories, Run fleet action), vessel health tiles,
    /// the Mission History chart (range, fleet, and vessel filters), Voyage Progress, Recent Missions (status,
    /// vessel, captain filters; View Detail, View JSON, Restart, Delete; View All), and Recent Signals. Reloads on any
    /// socket message (at most every two seconds), polls every 30 s, and follows the auto-refresh interval.
    /// <c>Tab</c> moves between sections and the page scrolls to keep the focused one visible. Not thread-safe.
    /// </summary>
    public class HomeScreen : OpsScreen
    {
        #region Public-Members

        /// <summary>
        /// Header actions.
        /// </summary>
        public ButtonRow HeaderButtons { get; } = new ButtonRow();

        /// <summary>
        /// Ask band actions.
        /// </summary>
        public ButtonRow AskButtons { get; } = new ButtonRow();

        /// <summary>
        /// Alert banners.
        /// </summary>
        public OpsLinkList Alerts { get; } = new OpsLinkList();

        /// <summary>
        /// KPI tiles.
        /// </summary>
        public OpsTileRow Kpis { get; } = new OpsTileRow();

        /// <summary>
        /// Tenant-admin shortcuts.
        /// </summary>
        public OpsTileRow Shortcuts { get; } = new OpsTileRow();

        /// <summary>
        /// Vessel health tiles.
        /// </summary>
        public OpsTileRow Health { get; } = new OpsTileRow();

        /// <summary>
        /// Mission History filters (fleet, vessel, range).
        /// </summary>
        public OpsFilterBar HistoryFilters { get; } = new OpsFilterBar();

        /// <summary>
        /// Mission History chart.
        /// </summary>
        public OpsStackedBarChart Chart { get; } = new OpsStackedBarChart();

        /// <summary>
        /// Voyage Progress grid.
        /// </summary>
        public ArmadaGrid<VoyageProgress> Voyages { get; }

        /// <summary>
        /// Recent Missions filters.
        /// </summary>
        public OpsFilterBar MissionFilters { get; } = new OpsFilterBar();

        /// <summary>
        /// Recent Missions grid.
        /// </summary>
        public ArmadaGrid<MissionSummary> Missions { get; }

        /// <summary>
        /// Recent Signals.
        /// </summary>
        public OpsLinkList Signals { get; } = new OpsLinkList();

        /// <summary>
        /// Last status.
        /// </summary>
        public ArmadaStatus? Status { get; private set; } = null;

        /// <summary>
        /// Recent missions (unfiltered).
        /// </summary>
        public List<MissionSummary> RecentMissions { get; private set; } = new List<MissionSummary>();

        /// <summary>
        /// Active fleet action runs (pending, running), or null when unavailable.
        /// </summary>
        public int[]? ActiveRuns { get; private set; } = null;

        /// <summary>
        /// Vessel health summary, or null.
        /// </summary>
        public VesselHealthSummary? HealthSummary { get; private set; } = null;

        /// <summary>
        /// Mission history, or null.
        /// </summary>
        public MissionHistorySummaryResult? History { get; private set; } = null;

        /// <summary>
        /// True until the first load finishes.
        /// </summary>
        public bool Loading { get; private set; } = true;

        /// <summary>
        /// Number of full loads (tests).
        /// </summary>
        public int Loads { get; private set; } = 0;

        /// <summary>
        /// Mission History range select (hour, day, week, month).
        /// </summary>
        public SelectField<string> Range { get; }

        /// <summary>
        /// Mission History fleet select.
        /// </summary>
        public SelectField<string> HistoryFleet { get; }

        /// <summary>
        /// Mission History vessel select.
        /// </summary>
        public SelectField<string> HistoryVessel { get; }

        /// <summary>
        /// Recent Missions status filter.
        /// </summary>
        public SelectField<string> StatusFilter { get; }

        /// <summary>
        /// Recent Missions vessel filter.
        /// </summary>
        public SelectField<string> VesselFilter { get; }

        /// <summary>
        /// Recent Missions captain filter.
        /// </summary>
        public SelectField<string> CaptainFilter { get; }

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                hints.Add(new KeyValuePair<string, string>("Tab", "Next section"));
                hints.Add(new KeyValuePair<string, string>("D", "Dispatch"));
                hints.Add(new KeyValuePair<string, string>("a", "Ask Armada"));
                hints.Add(new KeyValuePair<string, string>("i", "Needs You"));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private static readonly string[] _RecentStatuses = new string[] { "Pending", "Assigned", "InProgress", "Testing", "Review", "Complete", "Failed", "Cancelled" };
        private readonly MissionOps _Ops;
        private readonly Button _ViewAll;
        private readonly List<OpsScreenAction> _Actions = new List<OpsScreenAction>();
        private Timer? _Poll = null;
        private int _Scroll = 0;
        private DateTime _LastLoadUtc = DateTime.MinValue;
        private bool _LoadPending = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public HomeScreen(RouteMatch route, TuiContext context)
            : base(route, context, "HomeScreen", "System Status")
        {
            _Ops = new MissionOps(this);
            Reference.Ensure("vessels", "captains", "fleets");

            _Actions.Add(new OpsScreenAction("setup", "Setup Wizard", () => Context.Navigate("/setup"), "w"));
            _Actions.Add(new OpsScreenAction("dispatch", "+ Dispatch", () => Context.Navigate("/dispatch"), "D"));
            _Actions.Add(new OpsScreenAction("voyage", "+ Voyage", () => Context.Navigate("/voyages/create"), "V"));
            _Actions.Add(new OpsScreenAction("ask", "Ask Armada", () => Context.Navigate("/ask"), "a"));
            _Actions.Add(new OpsScreenAction("inbox", "Needs You", () => Context.Navigate("/inbox"), "i"));
            _Actions.Add(new OpsScreenAction("diagnostics", "Diagnostics", () => Context.Navigate("/server?tab=diagnostics"), "G"));
            _Actions.Add(new OpsScreenAction("import", "Import repositories", () => Context.Navigate("/vessels/import"), "I", () => IsTenantAdmin));
            _Actions.Add(new OpsScreenAction("run-action", "Run fleet action", () => Context.Navigate("/fleet-actions?tab=actions&run=new"), "F", () => IsTenantAdmin));
            _Actions.Add(new OpsScreenAction("missions", "View All", () => Context.Navigate("/missions"), "M"));

            HeaderButtons.Add(Btn("Setup Wizard", "w", () => RunAction("setup")));
            HeaderButtons.Add(Btn("+ Dispatch", "D", () => RunAction("dispatch")));
            HeaderButtons.Add(Btn("+ Voyage", "V", () => RunAction("voyage")));
            AskButtons.Add(Btn("Ask Armada ->", "a", () => RunAction("ask")));
            AskButtons.Add(Btn("Needs You", "i", () => RunAction("inbox")));
            AskButtons.Add(Btn("Dispatch", null, () => RunAction("dispatch")));
            AskButtons.Add(Btn("Diagnostics", "G", () => RunAction("diagnostics")));
            _ViewAll = Btn("View All ->", "M", () => RunAction("missions"));

            Range = NewSelect("Range", new List<SelectOption<string>>
            {
                new SelectOption<string>("hour", Tr("Last Hour")),
                new SelectOption<string>("day", Tr("Last Day")),
                new SelectOption<string>("week", Tr("Last Week")),
                new SelectOption<string>("month", Tr("Last Month")),
            });
            Range.SetValue("week");
            HistoryFleet = NewSelect("Fleet", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Fleets")) });
            HistoryFleet.SetValue("");
            HistoryVessel = NewSelect("Vessel", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Vessels")) });
            HistoryVessel.SetValue("");
            HistoryFilters.Add("Fleet", HistoryFleet, 18);
            HistoryFilters.Add("Vessel", HistoryVessel, 18);
            HistoryFilters.Add("Range", Range, 14);
            Range.ValueChanged += (s, e) => LoadHistory();
            HistoryFleet.ValueChanged += (s, e) =>
            {
                HistoryVessel.Options = HistoryVesselOptions();
                HistoryVessel.SetValue("");
                LoadHistory();
            };
            HistoryVessel.ValueChanged += (s, e) => LoadHistory();

            List<SelectOption<string>> statusOptions = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Statuses")) };
            statusOptions.AddRange(_RecentStatuses.Select(s => new SelectOption<string>(s, Tr(s))));
            StatusFilter = NewSelect("Status", statusOptions);
            StatusFilter.SetValue("");
            VesselFilter = NewSelect("Vessel", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Vessels")) });
            VesselFilter.SetValue("");
            CaptainFilter = NewSelect("Captain", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Captains")) });
            CaptainFilter.SetValue("");
            MissionFilters.Add("Status", StatusFilter, 16);
            MissionFilters.Add("Vessel", VesselFilter, 18);
            MissionFilters.Add("Captain", CaptainFilter, 18);
            StatusFilter.ValueChanged += (s, e) => ApplyMissionFilters();
            VesselFilter.ValueChanged += (s, e) => ApplyMissionFilters();
            CaptainFilter.ValueChanged += (s, e) => ApplyMissionFilters();

            Voyages = new ArmadaGrid<VoyageProgress>(v => v.Voyage?.Id ?? "");
            Voyages.MultiSelect = false;
            Voyages.ShowPagingBar = false;
            Voyages.PageSize = 250;
            Voyages.Dispatcher = context.Dispatcher;
            Voyages.ModalHost = context.Modals;
            Voyages.AddColumn(new GridColumn<VoyageProgress>("voyage", "Voyage", v => (v.Voyage?.Title ?? v.Voyage?.Id ?? "") + "  " + (v.Voyage?.Id ?? "")) { Weight = 4 });
            Voyages.AddColumn(new GridColumn<VoyageProgress>("status", "Status", v => StatusBadge.Label(v.Voyage?.Status.ToString())) { Width = 14, Style = (v, t) => StatusBadge.Style(v.Voyage?.Status.ToString(), t) });
            Voyages.AddColumn(new GridColumn<VoyageProgress>("vessel", "Vessel", v => v.VesselIds == null || v.VesselIds.Count == 0 ? "-" : String.Join(", ", v.VesselIds.Select(id => Reference.VesselName(id)))) { Weight = 2 });
            Voyages.AddColumn(new GridColumn<VoyageProgress>("progress", "Progress", v => ProgressBar(Percent(v), 10) + " " + Percent(v) + "%") { Width = 17 });
            Voyages.AddColumn(new GridColumn<VoyageProgress>("missions", "Missions", v => v.CompletedMissions + "/" + v.TotalMissions + " " + Tr("done") + (v.FailedMissions > 0 ? ", " + v.FailedMissions + " " + Tr("failed") : "")) { Weight = 2 });
            Voyages.Activated += (s, v) => { if (v.Voyage != null) Context.Navigate("/voyages/" + Uri.EscapeDataString(v.Voyage.Id)); };
            Voyages.MenuRequested += (s, v) => VoyageMenu(v);

            Missions = new ArmadaGrid<MissionSummary>(m => m.Id);
            Missions.MultiSelect = false;
            Missions.ShowPagingBar = false;
            Missions.PageSize = 250;
            Missions.EmptyText = "No missions found.";
            Missions.Dispatcher = context.Dispatcher;
            Missions.ModalHost = context.Modals;
            Missions.AddColumn(new GridColumn<MissionSummary>("title", "Mission", m => m.Title) { Weight = 4 });
            Missions.AddColumn(new GridColumn<MissionSummary>("id", "ID", m => m.Id) { Width = 24 });
            Missions.AddColumn(new GridColumn<MissionSummary>("status", "Status", m => StatusBadge.Label(m.Status.ToString())) { Width = 16, Style = (m, t) => StatusBadge.Style(m.Status.ToString(), t) });
            Missions.AddColumn(new GridColumn<MissionSummary>("vessel", "Vessel", m => Reference.VesselName(m.VesselId)) { Weight = 2 });
            Missions.AddColumn(new GridColumn<MissionSummary>("captain", "Captain", m => Reference.CaptainName(m.CaptainId)) { Weight = 2 });
            Missions.AddColumn(new GridColumn<MissionSummary>("created", "Created", m => Context.Loc.FormatRelative(m.CreatedUtc, Context.Clock.UtcNow)) { Width = 16 });
            Missions.Activated += (s, m) => Context.Navigate("/missions/" + Uri.EscapeDataString(m.Id));
            Missions.MenuRequested += (s, m) => MissionMenu(m);

            Health.TileHeight = 4;
            Chart.Buckets = new List<OpsChartBucket>();
            Chart.EmptyText = "Loading mission history...";

            AddChild(HeaderButtons);
            AddChild(AskButtons);
            AddChild(Alerts);
            AddChild(Kpis);
            AddChild(Shortcuts);
            AddChild(Health);
            AddChild(HistoryFilters);
            AddChild(Voyages);
            AddChild(MissionFilters);
            AddChild(_ViewAll);
            AddChild(Missions);
            AddChild(Signals);
            HistoryFilters.Exited += (s, e) => Scope.Move(true);
            MissionFilters.Exited += (s, e) => Scope.Focus(Missions);
            Reference.Changed += (s, name) => ReferenceArrived(name);
            Scope.Focus(AskButtons);

            SubscribeCoalesced("*", ThrottledLoad);
            _Poll = new Timer(_ => Context.Dispatcher.Post(() => { if (IsLive) LoadAll(); }), null, 30000, 30000);
            Track(new OpsCallbackDisposable(() => { _Poll?.Dispose(); _Poll = null; }));
            BuildTiles();
            LoadAll();
            LoadHistory();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The dashboard's alert banners for the current status (level, message, action, link).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Alerts.</returns>
        public static List<string[]> ComputeAlerts(ArmadaStatus? status)
        {
            List<string[]> result = new List<string[]>();
            if (status == null) return result;
            Dictionary<string, int> ms = status.MissionsByStatus ?? new Dictionary<string, int>();
            int stalled = status.StalledCaptains;
            int failed = Count(ms, "Failed");
            int landingFailed = Count(ms, "LandingFailed");
            int pending = Count(ms, "Pending");
            int idle = status.IdleCaptains;
            int working = status.WorkingCaptains;
            int total = status.TotalCaptains;
            if (stalled > 0) result.Add(new string[] { "error", stalled + " captain(s) stalled -- recovery attempts exhausted.", "Stop and restart stalled captains to resume work.", "/captains" });
            if (failed > 0) result.Add(new string[] { "warning", failed + " mission(s) failed.", "Review and restart failed missions.", "/missions" });
            if (landingFailed > 0) result.Add(new string[] { "warning", landingFailed + " mission(s) failed to land -- work was produced but could not be merged.", "Retry landing or restart these missions.", "/missions" });
            if (pending > 0 && idle > 0 && working == 0) result.Add(new string[] { "warning", pending + " pending mission(s) but no captains are working. " + idle + " captain(s) idle.", "Vessels may have concurrent mission limits blocking dispatch, or missions may be assigned to a vessel with an active mission.", "" });
            if (total == 0 && pending > 0) result.Add(new string[] { "error", pending + " pending mission(s) but no captains exist.", "Create a captain to start processing missions.", "/captains" });
            return result;
        }

        /// <summary>
        /// Reload status, recent missions, runs, and health.
        /// </summary>
        public void LoadAll()
        {
            _LastLoadUtc = Context.Clock.UtcNow;
            _LoadPending = false;
            Call(async (c, t) =>
            {
                Task<ArmadaStatus?> status = Safe(c.GetStatusAsync(t));
                ArmadaPageQuery mq = new ArmadaPageQuery();
                mq.PageSize = 10;
                Task<EnumerationResult<MissionSummary>?> missions = Safe(c.ListMissionSummariesAsync(mq, t));
                FleetActionRunEnumerateQuery pq = new FleetActionRunEnumerateQuery();
                pq.Status = FleetActionRunStatusEnum.Pending;
                pq.PageSize = 1;
                FleetActionRunEnumerateQuery rq = new FleetActionRunEnumerateQuery();
                rq.Status = FleetActionRunStatusEnum.Running;
                rq.PageSize = 1;
                Task<EnumerationResult<FleetActionRun>?> pending = Safe(c.EnumerateFleetActionRunsAsync(pq, t));
                Task<EnumerationResult<FleetActionRun>?> running = Safe(c.EnumerateFleetActionRunsAsync(rq, t));
                Task<VesselHealthSummary?> health = Safe(c.GetVesselHealthSummaryAsync(t));
                await Task.WhenAll(status, missions, pending, running, health).ConfigureAwait(false);
                HomeData data = new HomeData();
                data.Status = status.Result;
                data.Missions = missions.Result?.Objects;
                data.MissionsLoaded = missions.Result != null;
                data.Pending = pending.Result;
                data.Running = running.Result;
                data.Health = health.Result;
                return data;
            }, data =>
            {
                Loading = false;
                Loads++;
                if (data.Status != null) Status = data.Status;
                if (data.MissionsLoaded) RecentMissions = data.Missions ?? new List<MissionSummary>();
                ActiveRuns = data.Pending != null && data.Running != null ? new int[] { (int)data.Pending.TotalRecords, (int)data.Running.TotalRecords } : null;
                HealthSummary = data.Health;
                if (data.Status == null && !data.MissionsLoaded) ShowMessage(Tr("Failed to load dashboard data."));
                BuildTiles();
                ApplyMissionFilters();
            }, "Failed to load dashboard data.");
            Reference.Ensure(true, "vessels", "captains", "fleets");
        }

        /// <summary>
        /// Reload the Mission History chart for the selected range and filters.
        /// </summary>
        public void LoadHistory()
        {
            int hours = RangeHours(Range.Value);
            int step = RangeStep(Range.Value);
            MissionHistoryFilter filter = new MissionHistoryFilter();
            filter.ToUtc = DateTime.UtcNow;
            filter.FromUtc = filter.ToUtc.Value.AddHours(-hours);
            filter.BucketMinutes = step;
            filter.FleetId = String.IsNullOrEmpty(HistoryFleet.Value) ? null : HistoryFleet.Value;
            filter.VesselId = String.IsNullOrEmpty(HistoryVessel.Value) ? null : HistoryVessel.Value;
            Chart.EmptyText = "Loading mission history...";
            Call((c, t) => c.GetMissionHistoryAsync(filter, t), result =>
            {
                History = result;
                Chart.EmptyText = "No mission data for this time range";
                Chart.Buckets = (result?.Buckets ?? new List<MissionHistoryBucket>()).Select(b => new OpsChartBucket
                {
                    StartUtc = b.StartUtc,
                    Label = BucketLabel(b.StartUtc, step, hours),
                    Complete = b.CompleteCount,
                    Failed = b.FailedCount,
                    Other = b.OtherCount,
                }).ToList();
            }, null, ex =>
            {
                History = null;
                Chart.EmptyText = "No mission data for this time range";
                Chart.Buckets = new List<OpsChartBucket>();
            });
        }

        /// <summary>
        /// Run a page action by id.
        /// </summary>
        /// <param name="id">Action id.</param>
        /// <returns>True when it ran.</returns>
        public bool RunAction(string id)
        {
            OpsScreenAction? a = _Actions.FirstOrDefault(x => x.Id == id);
            if (a == null || !a.Available) return false;
            a.Run();
            return true;
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return () =>
            {
                LoadAll();
                LoadHistory();
            };
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            foreach (OpsScreenAction a in _Actions)
            {
                OpsScreenAction action = a;
                ArmadaCommand c = new ArmadaCommand("home." + a.Id, a.Label.Replace("+ ", "").Replace(" ->", ""), CommandMenuEnum.Actions, () => { if (action.Available) action.Run(); }, a.Key!);
                c.Group = Title;
                c.Dispatch = false;
                c.IsEnabled = () => action.Available;
                list.Add(c);
            }

            ArmadaCommand mission = new ArmadaCommand("home.mission-menu", "Recent mission actions", CommandMenuEnum.Actions, () => { if (Missions.Current != null) MissionMenu(Missions.Current); });
            mission.Group = Title;
            mission.Dispatch = false;
            mission.IsEnabled = () => Missions.Current != null;
            list.Add(mission);
            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            IWidget? focused = Scope.Focused;
            bool inFilters = ReferenceEquals(focused, HistoryFilters) || ReferenceEquals(focused, MissionFilters);
            if (!inFilters && key.Code == KeyCode.Character && (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) == 0)
            {
                if (ReferenceEquals(focused, Missions) && Missions.Current != null && MissionKey(Missions.Current, (char)key.Rune)) return true;
                if (ReferenceEquals(focused, Voyages) && Voyages.Current != null && key.Rune == 'j')
                {
                    ShowVoyageJson(Voyages.Current);
                    return true;
                }

                foreach (OpsScreenAction a in _Actions)
                {
                    if (a.Key != null && a.Key.Length == 1 && a.Key[0] == (char)key.Rune && a.Available)
                    {
                        a.Run();
                        return true;
                    }
                }
            }

            if (ReferenceEquals(focused, Missions) && key.Code == KeyCode.Delete && Missions.Current != null)
            {
                DeleteMission(Missions.Current);
                return true;
            }

            if (Scope.HandleKey(key)) return true;
            if (key.Code == KeyCode.PageDown)
            {
                _Scroll += 5;
                return true;
            }

            if (key.Code == KeyCode.PageUp)
            {
                _Scroll = Math.Max(0, _Scroll - 5);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse.Kind == MouseEventKind.Wheel && Scope.HandleMouse(mouse)) return true;
            if (mouse.Kind == MouseEventKind.Wheel)
            {
                if (mouse.Button == MouseButton.WheelUp) _Scroll = Math.Max(0, _Scroll - 3);
                else if (mouse.Button == MouseButton.WheelDown) _Scroll += 3;
                return true;
            }

            return Scope.HandleMouse(mouse);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 30 || height < 6) return;
            List<HomeBlock> blocks = Layout(width);
            int total = blocks.Count == 0 ? 0 : blocks.Max(b => b.Top + b.Height);
            HomeBlock? focusBlock = blocks.FirstOrDefault(b => b.Widget != null && ReferenceEquals(b.Widget, Scope.Focused));
            if (focusBlock != null)
            {
                int top = focusBlock.FocusTop;
                int bottom = focusBlock.Top + focusBlock.Height;
                if (top < _Scroll) _Scroll = top;
                if (bottom > _Scroll + height) _Scroll = Math.Min(top, bottom - height);
            }

            _Scroll = Math.Clamp(_Scroll, 0, Math.Max(0, total - height));
            foreach (HomeBlock block in blocks)
            {
                int y = block.Top - _Scroll;
                if (y + block.Height <= 0 || y >= height) continue;
                if (y < 0) continue;
                int h = Math.Min(block.Height, height - y);
                if (block.Draw != null) block.Draw(new SurfaceView(surface, new Rect(0, y, width, h)));
                if (block.Widget != null) Scope.RenderChild(surface, block.Widget, new Rect(block.Left, y, Math.Max(1, (block.Width > 0 ? block.Width : width) - block.Left), h));
            }

            foreach (IWidget child in Scope.Children)
            {
                if (!blocks.Any(b => ReferenceEquals(b.Widget, child) && b.Top - _Scroll >= 0 && b.Top - _Scroll < height)) Scope.Place(child, Rect.Empty);
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<T?> Safe<T>(Task<T?> task) where T : class
        {
            try { return await task.ConfigureAwait(false); }
            catch (ArmadaApiException) { return null; }
        }

        private static int Count(Dictionary<string, int> map, string key)
        {
            return map.TryGetValue(key, out int v) ? v : 0;
        }

        private static int Percent(VoyageProgress v)
        {
            if (v.TotalMissions <= 0) return 0;
            return (int)Math.Round((double)v.CompletedMissions / v.TotalMissions * 100);
        }

        private static string ProgressBar(int percent, int width)
        {
            int filled = Math.Clamp((int)Math.Round(percent / 100.0 * width), 0, width);
            return "[" + new string('=', filled) + new string('-', width - filled) + "]";
        }

        private static int RangeHours(string? range)
        {
            switch (range)
            {
                case "hour": return 1;
                case "day": return 24;
                case "month": return 720;
                default: return 168;
            }
        }

        private static int RangeStep(string? range)
        {
            switch (range)
            {
                case "hour": return 1;
                case "day": return 15;
                case "month": return 360;
                default: return 60;
            }
        }

        private static string BucketLabel(DateTime startUtc, int stepMinutes, int hours)
        {
            DateTime local = startUtc.ToLocalTime();
            if (stepMinutes <= 15) return local.ToString("HH:mm", CultureInfo.CurrentCulture);
            if (hours > 48) return local.ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
            return local.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        private Button Btn(string label, string? key, Action run)
        {
            Button b = new Button(label, run);
            b.Hint = key;
            return b;
        }

        private void ThrottledLoad()
        {
            if ((Context.Clock.UtcNow - _LastLoadUtc).TotalSeconds >= 2)
            {
                LoadAll();
                return;
            }

            if (_LoadPending) return;
            _LoadPending = true;
            _ = Task.Delay(2000).ContinueWith(t => Context.Dispatcher.Post(() =>
            {
                if (IsLive && _LoadPending) LoadAll();
            }), TaskScheduler.Default);
        }

        private void ReferenceArrived(string name)
        {
            if (name == "fleets")
            {
                string? current = HistoryFleet.Value;
                HistoryFleet.Options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Fleets")) }.Concat(Reference.Fleets.Select(f => new SelectOption<string>(f.Id, f.Name))).ToList();
                HistoryFleet.SetValue(current ?? "");
            }

            if (name == "vessels")
            {
                string? hv = HistoryVessel.Value;
                HistoryVessel.Options = HistoryVesselOptions();
                HistoryVessel.SetValue(hv ?? "");
                string? mv = VesselFilter.Value;
                VesselFilter.Options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Vessels")) }.Concat(Reference.Vessels.Select(v => new SelectOption<string>(v.Id, v.Name))).ToList();
                VesselFilter.SetValue(mv ?? "");
            }

            if (name == "captains")
            {
                string? cv = CaptainFilter.Value;
                CaptainFilter.Options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Captains")) }.Concat(Reference.Captains.Select(c => new SelectOption<string>(c.Id, c.Name))).ToList();
                CaptainFilter.SetValue(cv ?? "");
            }
        }

        private List<SelectOption<string>> HistoryVesselOptions()
        {
            string fleet = HistoryFleet.Value ?? "";
            IEnumerable<Vessel> vessels = Reference.Vessels.Where(v => fleet.Length == 0 || v.FleetId == fleet);
            return new List<SelectOption<string>> { new SelectOption<string>("", Tr("All Vessels")) }.Concat(vessels.Select(v => new SelectOption<string>(v.Id, v.Name))).ToList();
        }

        private void ApplyMissionFilters()
        {
            string status = StatusFilter.Value ?? "";
            string vessel = VesselFilter.Value ?? "";
            string captain = CaptainFilter.Value ?? "";
            Missions.SetLocalRows(RecentMissions.Where(m =>
                (status.Length == 0 || m.Status.ToString() == status) &&
                (vessel.Length == 0 || m.VesselId == vessel) &&
                (captain.Length == 0 || m.CaptainId == captain)).ToList());
            Voyages.SetLocalRows(Status?.Voyages ?? new List<VoyageProgress>());
            Signals.Items = (Status?.RecentSignals ?? new List<Signal>()).Take(5).Select(s => new OpsLinkItem(
                "[" + s.Type + "] " + (s.Payload ?? ""), null, null, Context.Loc.FormatRelative(s.CreatedUtc, Context.Clock.UtcNow))).ToList();
            Alerts.Items = ComputeAlerts(Status).Select(a =>
            {
                string link = a[3];
                bool error = a[0] == "error";
                string text = (error ? "x " : "! ") + Tr(a[1]) + "  " + Tr(a[2]);
                return new OpsLinkItem(text, link.Length > 0 ? () => Context.Navigate(link) : (Action?)null, t => error ? t.Error : t.Warning, link.Length > 0 ? "[" + Tr("View") + "]" : "");
            }).ToList();
        }

        private void BuildTiles()
        {
            ArmadaStatus? s = Status;
            int totalMissions = s?.MissionsByStatus?.Values.Sum() ?? 0;
            string captainDetail = (s?.IdleCaptains ?? 0) + " idle  " + (s?.WorkingCaptains ?? 0) + " working" + ((s?.StalledCaptains ?? 0) > 0 ? "  " + s!.StalledCaptains + " stalled" : "");
            string voyageDetail = (s?.MemoryPressureDeferrals ?? 0) > 0 ? Tr("{{count}} deferred for memory pressure", LocalizationArgs.Of("count", s!.MemoryPressureDeferrals)) : "";
            string missionDetail = s?.MissionsByStatus == null ? "" : String.Join("  ", s.MissionsByStatus.Select(kv => kv.Value + " " + kv.Key));
            int[]? runs = ActiveRuns;
            string runsValue = runs != null ? Context.Loc.FormatNumber(runs[0] + runs[1]) : "-";
            string runsDetail = runs != null
                ? Tr("{{count}} running", LocalizationArgs.Of("count", Context.Loc.FormatNumber(runs[1]))) + "  " + Tr("{{count}} pending", LocalizationArgs.Of("count", Context.Loc.FormatNumber(runs[0])))
                : "";
            string runsRoute = runs != null && runs[1] == 0 && runs[0] > 0 ? "/fleet-actions?tab=runs&status=Pending" : "/fleet-actions?tab=runs&status=Running";
            Kpis.Tiles = new List<OpsTile>
            {
                new OpsTile(Tr("Captains"), (s?.TotalCaptains ?? 0).ToString(CultureInfo.InvariantCulture), captainDetail, () => Context.Navigate("/captains")),
                new OpsTile(Tr("Active Voyages"), (s?.ActiveVoyages ?? 0).ToString(CultureInfo.InvariantCulture), voyageDetail, () => Context.Navigate("/missions?tab=voyages")),
                new OpsTile(Tr("Missions"), totalMissions.ToString(CultureInfo.InvariantCulture), missionDetail, () => Context.Navigate("/missions")),
                new OpsTile(Tr("Active fleet action runs"), runsValue, runsDetail, () => Context.Navigate(runsRoute)),
            };
            Shortcuts.Visible = IsTenantAdmin;
            Shortcuts.Tiles = new List<OpsTile>
            {
                new OpsTile(Tr("Import repositories"), "", Tr("Discover local git repositories and onboard them as vessels in bulk."), () => RunAction("import")) { KeyHint = "I" },
                new OpsTile(Tr("Run fleet action"), "", Tr("Run a command or a captain mission across many vessels at once."), () => RunAction("run-action")) { KeyHint = "F" },
            };
            VesselHealthSummary? h = HealthSummary;
            Health.Visible = h != null && h.TotalVessels > 0;
            if (h != null)
            {
                Health.Tiles = new List<OpsTile>
                {
                    new OpsTile(Tr("Vessels failing health"), Context.Loc.FormatNumber(h.Fail), Tr("{{count}} warn, {{unknown}} not evaluated", LocalizationArgs.Of("count", Context.Loc.FormatNumber(h.Warn), "unknown", Context.Loc.FormatNumber(h.NotEvaluated))), () => Context.Navigate("/vessels/health?overall=Fail")) { ValueStyle = t => h.Fail > 0 ? t.Error : t.Muted },
                    new OpsTile(Tr("Outdated majors"), Context.Loc.FormatNumber(h.OutdatedMajorVessels), Tr("Vessels with a dependency a major version behind"), () => Context.Navigate("/vessels/health?deps=Fail&dir=desc&sort=OutdatedMajorCount")) { ValueStyle = t => h.OutdatedMajorVessels > 0 ? t.Warning : t.Muted },
                    new OpsTile(Tr("High/critical vulnerabilities"), Context.Loc.FormatNumber(h.HighOrCriticalVulnerabilityVessels), Tr("Vessels with a high or critical advisory"), () => Context.Navigate("/vessels/health?dir=desc&sort=VulnerableCount")) { ValueStyle = t => h.HighOrCriticalVulnerabilityVessels > 0 ? t.Error : t.Muted },
                };
            }
        }

        private List<HomeBlock> Layout(int width)
        {
            List<HomeBlock> blocks = new List<HomeBlock>();
            int y = 0;
            blocks.Add(HomeBlock.Drawn(y, 1, s =>
            {
                int x = SurfaceText.Draw(s, 0, 0, Tr("System Status"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
                SurfaceText.Draw(s, x + 2, 0, Tr("Overview of fleet health, active missions, and recent activity."), Theme.Muted, width - x - 2);
            }));
            y++;
            string refresh = Context.Refresh.StatusText;
            string right = (refresh.Length > 0 ? "[" + Tr(refresh) + "]  " : "") + "F5 " + Tr("Refresh");
            blocks.Add(HomeBlock.Drawn(y, 1, s => SurfaceText.Draw(s, Math.Max(0, width - TextCells.Width(right)), 0, right, Theme.Muted, width)));
            blocks.Add(HomeBlock.For(HeaderButtons, y, 1, 0, Math.Max(10, width - TextCells.Width(right) - 2)));
            y += 2;
            if (Loading)
            {
                blocks.Add(HomeBlock.Drawn(y, 1, s => SurfaceText.Draw(s, 0, 0, Tr("Loading dashboard..."), Theme.Muted, width)));
                y += 2;
            }

            blocks.Add(HomeBlock.Drawn(y, 1, s =>
            {
                int x = SurfaceText.Draw(s, 0, 0, Tr("Ask Armada"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
                SurfaceText.Draw(s, x + 2, 0, Tr("Ask about fleet state in plain language and dispatch work straight from the conversation."), Theme.Muted, width - x - 2);
            }));
            y++;
            blocks.Add(HomeBlock.For(AskButtons, y, 1));
            y += 2;
            if (Alerts.Items.Count > 0)
            {
                blocks.Add(HomeBlock.For(Alerts, y, Alerts.Items.Count));
                y += Alerts.Items.Count + 1;
            }

            blocks.Add(HomeBlock.For(Kpis, y, 3));
            y += 4;
            if (Shortcuts.Visible)
            {
                blocks.Add(HomeBlock.For(Shortcuts, y, 3));
                y += 4;
            }

            if (Health.Visible)
            {
                blocks.Add(HomeBlock.Drawn(y, 1, s => SurfaceText.Draw(s, 0, 0, Tr("Vessel health"), Theme.Accent, width)));
                blocks.Add(HomeBlock.For(Health, y + 1, 4, 0, 0, y));
                y += 6;
            }

            MissionHistorySummaryResult? h = History;
            int historyTop = y;
            blocks.Add(HomeBlock.Drawn(y, 1, s =>
            {
                int x = SurfaceText.Draw(s, 0, 0, Tr("Mission History"), Theme.Accent, width) + 3;
                x += SurfaceText.Draw(s, x, 0, (h?.TotalCount ?? 0) + " " + Tr("Total"), Theme.Text, width - x) + 3;
                x += SurfaceText.Draw(s, x, 0, (h?.CompleteCount ?? 0) + " " + Tr("Complete"), Theme.Success, width - x) + 3;
                x += SurfaceText.Draw(s, x, 0, (h?.FailedCount ?? 0) + " " + Tr("Failed"), Theme.Error, width - x) + 3;
                if ((h?.OtherCount ?? 0) > 0) SurfaceText.Draw(s, x, 0, h!.OtherCount + " " + Tr("Other"), Theme.Muted, width - x);
            }));
            y++;
            int fh = HistoryFilters.PreferredHeight(width);
            blocks.Add(HomeBlock.For(HistoryFilters, y, fh, 0, 0, historyTop));
            y += fh;
            blocks.Add(HomeBlock.For(Chart, y, 8));
            y += 8;
            blocks.Add(HomeBlock.Drawn(y, 1, s =>
            {
                int x = SurfaceText.Draw(s, 0, 0, "# " + Tr("Complete"), Theme.Success, width) + 3;
                x += SurfaceText.Draw(s, x, 0, "x " + Tr("Failed"), Theme.Error, width - x) + 3;
                SurfaceText.Draw(s, x, 0, ". " + Tr("Other"), Theme.Muted, width - x);
            }));
            y += 2;

            if (Status?.Voyages != null && Status.Voyages.Count > 0)
            {
                int vt = y;
                blocks.Add(HomeBlock.Drawn(y, 1, s => SurfaceText.Draw(s, 0, 0, Tr("Voyage Progress"), Theme.Accent, width)));
                y++;
                int rows = Math.Min(Status.Voyages.Count, 8) + 1;
                blocks.Add(HomeBlock.For(Voyages, y, rows, 0, 0, vt));
                y += rows + 1;
            }

            int mt = y;
            blocks.Add(HomeBlock.Drawn(y, 1, s => SurfaceText.Draw(s, 0, 0, Tr("Recent Missions"), Theme.Accent, width)));
            y++;
            int mfh = MissionFilters.PreferredHeight(Math.Max(10, width - 20));
            blocks.Add(HomeBlock.For(MissionFilters, y, mfh, 0, Math.Max(10, width - 20), mt));
            blocks.Add(HomeBlock.For(_ViewAll, y, 1, Math.Max(0, width - 18), width, mt));
            y += mfh;
            int mrows = Math.Max(2, Math.Min(Missions.Rows.Count, 10) + 1);
            blocks.Add(HomeBlock.For(Missions, y, mrows, 0, 0, mt));
            y += mrows + 1;

            if (Signals.Items.Count > 0)
            {
                blocks.Add(HomeBlock.Drawn(y, 1, s => SurfaceText.Draw(s, 0, 0, Tr("Recent Signals"), Theme.Accent, width)));
                y++;
                blocks.Add(HomeBlock.For(Signals, y, Signals.Items.Count));
                y += Signals.Items.Count;
            }

            return blocks;
        }

        private bool MissionKey(MissionSummary m, char c)
        {
            switch (c)
            {
                case 'o':
                    Context.Navigate("/missions/" + Uri.EscapeDataString(m.Id));
                    return true;
                case 'j':
                    ShowJson("Mission: " + m.Title, m);
                    return true;
                case 'r':
                    if (!Restartable(m)) return false;
                    RestartMission(m);
                    return true;
                case 'y':
                    Copy(m.Id, "Mission ID");
                    return true;
                default:
                    return false;
            }
        }

        private static bool Restartable(MissionSummary m)
        {
            return m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.Cancelled || m.Status == MissionStatusEnum.LandingFailed;
        }

        private void MissionMenu(MissionSummary m)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>
            {
                new ActionMenuItem(Tr("View Detail"), () => Context.Navigate("/missions/" + Uri.EscapeDataString(m.Id)), "o"),
                new ActionMenuItem(Tr("View JSON"), () => ShowJson("Mission: " + m.Title, m), "j"),
            };
            if (Restartable(m)) items.Add(new ActionMenuItem(Tr("Restart"), () => RestartMission(m), "r"));
            items.Add(new ActionMenuItem("! " + Tr("Delete"), () => DeleteMission(m), "Del"));
            ShowMenu(m.Title, items);
        }

        private void RestartMission(MissionSummary m)
        {
            Call((c, t) => c.RestartMissionAsync(m.Id, t), r => LoadAll(), null, ex => { });
        }

        private void DeleteMission(MissionSummary m)
        {
            Confirm("Delete Mission", Tr("Delete this mission?"), () =>
            {
                Run((c, t) => c.DeleteMissionAsync(m.Id, t), LoadAll, null, ex => ShowMessage(Tr("Failed to delete mission.")));
            }, "Delete");
        }

        private void VoyageMenu(VoyageProgress v)
        {
            ShowMenu(v.Voyage?.Title ?? v.Voyage?.Id ?? "", new List<ActionMenuItem>
            {
                new ActionMenuItem(Tr("View JSON"), () => ShowVoyageJson(v), "j"),
            });
        }

        private void ShowVoyageJson(VoyageProgress v)
        {
            ShowJson("Voyage: " + (v.Voyage?.Title ?? v.Voyage?.Id ?? ""), v.Voyage);
        }

        #endregion
    }
}
