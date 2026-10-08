namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Metrics;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Widgets;

    /// <summary>
    /// Configuration, Harbors tab (dashboard <c>Harbors.tsx</c>): overview (total, connected, disconnected, enabled),
    /// filters (search and connection status; the API returns a plain list, so filtering and paging are local), the
    /// grid (harbor, status, enabled, capabilities, capacity, platform, protocol, last seen), row actions (Details
    /// with capabilities, Edit, Enable or Disable, View JSON, Delete), and the register/edit form. Enter opens
    /// Details. Tenant admins manage harbors. Under the grid, the Activity panel charts the selected Harbor (GET
    /// /api/v1/harbors/{id}/metrics): <c>r</c> cycles the range (1h, 24h, 7d) and <c>u</c> opens Token Usage filtered to
    /// that Harbor.
    /// </summary>
    public class HarborsScreen : EntityListScreen<Harbor>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Harbor"; }
        }

        /// <summary>
        /// The selected Harbor's activity panel.
        /// </summary>
        public HarborMetricsPanel Activity { get; } = new HarborMetricsPanel();

        /// <summary>
        /// Range of the activity panel (1h, 24h, or 7d).
        /// </summary>
        public string MetricsRange
        {
            get { return _Range; }
        }

        /// <summary>
        /// Completed metrics loads (tests).
        /// </summary>
        public int MetricsLoadCount
        {
            get { return _MetricsLoads; }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                if (Scope.Focused == Activity && Activity.CanScroll) hints.Add(new KeyValuePair<string, string>("Up/Down", "Scroll"));
                // The activity keys come before the list's own so they stay visible on an 80-column status bar.
                hints.Add(new KeyValuePair<string, string>("r", "Range"));
                hints.Add(new KeyValuePair<string, string>("u", "Token usage"));
                hints.AddRange(base.Hints);
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private static readonly string[] _Ranges = new string[] { "1h", "24h", "7d" };
        private string _Range = HarborMetricsRanges.DefaultWireName;
        private string? _LoadedKey = null;
        private bool _MetricsInFlight = false;
        private bool _MetricsPending = false;
        private int _MetricsLoads = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public HarborsScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Open(Harbor row)
        {
            HarborForms.ShowDetails(Context, row);
        }

        /// <summary>
        /// Enable a disabled harbor or disable an enabled one.
        /// </summary>
        /// <param name="row">Harbor.</param>
        public void Toggle(Harbor row)
        {
            if (!CanCreate) return;
            bool wasEnabled = row.Enabled;
            EntityUi.Run<Harbor?>(Context, ct => wasEnabled ? Context.Client.DisableHarborAsync(row.Id, ct) : Context.Client.EnableHarborAsync(row.Id, ct), h =>
            {
                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Harbor \"{{name}}\" {{state}}.", "name", row.Name, "state", T(wasEnabled ? "disabled" : "enabled")));
                Reload();
            }, "Update failed.");
        }

        /// <summary>
        /// Show another range in the activity panel and load it.
        /// </summary>
        /// <param name="range">1h, 24h, or 7d.</param>
        public void SetMetricsRange(string range)
        {
            if (Array.IndexOf(_Ranges, range) < 0) return;
            _Range = range;
            Activity.Range = range;
            LoadMetrics(true);
        }

        /// <summary>
        /// Next range: 1h, then 24h, then 7d, then 1h again.
        /// </summary>
        public void CycleMetricsRange()
        {
            int index = Array.IndexOf(_Ranges, _Range);
            SetMetricsRange(_Ranges[(index + 1) % _Ranges.Length]);
        }

        /// <summary>
        /// Open Token Usage filtered to the selected Harbor, over the panel's range.
        /// </summary>
        public void OpenTokenUsage()
        {
            Harbor? row = Grid.Current;
            if (row == null) return;
            string range = _Range == "1h" ? "hour" : _Range == "7d" ? "week" : "day";
            Context.Navigate("/activity?source=tokens&range=" + range + "&harborId=" + Uri.EscapeDataString(row.Id));
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return () =>
            {
                Reload();
                LoadMetrics(true);
            };
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IWidget? DetailPanel
        {
            get { return Activity; }
        }

        /// <inheritdoc />
        protected override int DetailPanelRows(int width, int rowsLeft)
        {
            int wanted = Activity.ContentHeight(width);
            return Math.Min(wanted, Math.Max(4, rowsLeft * 11 / 20));
        }

        /// <inheritdoc />
        protected override string IdOf(Harbor row)
        {
            return row.Id;
        }

        /// <inheritdoc />
        protected override string NameOf(Harbor row)
        {
            return row.Name;
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
            get { return "Register Harbor"; }
        }

        /// <inheritdoc />
        protected override void BuildColumns(ArmadaGrid<Harbor> grid)
        {
            grid.AddColumn(new GridColumn<Harbor>("name", "Harbor", h => h.Name + (h.Enabled ? "" : " (" + T("disabled") + ")")) { Weight = 3, MinWidth = 14, Sortable = true, Pinned = true });
            grid.AddColumn(new GridColumn<Harbor>("status", "Status", h => StatusBadge.Label(h.ConnectionStatus)) { Width = 15, Sortable = true, Style = (h, t) => StatusBadge.Style(h.ConnectionStatus, t) });
            grid.AddColumn(new GridColumn<Harbor>("enabled", "Enabled", h => h.Enabled ? T("Yes") : T("No")) { Width = 8, Sortable = true });
            grid.AddColumn(new GridColumn<Harbor>("capabilities", "Capabilities", h => h.Capabilities.Count == 0 ? "-" : String.Join(", ", h.Capabilities.Select(c => c.Name))) { Weight = 3, Sortable = true });
            grid.AddColumn(new GridColumn<Harbor>("capacity", "Capacity", h => h.MaxConcurrentJobs.ToString(System.Globalization.CultureInfo.InvariantCulture)) { Width = 9, Sortable = true, Align = TUIKit.Widgets.CellAlignment.Right });
            grid.AddColumn(new GridColumn<Harbor>("platform", "Platform", h => HarborForms.Platform(h)) { Weight = 2, Sortable = true });
            grid.AddColumn(new GridColumn<Harbor>("protocol", "Protocol", h => EntityUi.Dash(h.ProtocolVersion)) { Width = 9, Sortable = true });
            grid.AddColumn(new GridColumn<Harbor>("lastSeen", "Last Seen", h => h.LastSeenUtc.HasValue ? EntityUi.When(Context, h.LastSeenUtc) : T("Never")) { Width = 12, Sortable = true });
            grid.AddColumn(new GridColumn<Harbor>("id", "ID", h => h.Id) { Width = 26, DefaultVisible = false });
            grid.EmptyText = "No harbors match the current filters.";
        }

        /// <inheritdoc />
        protected override void BuildFilters(FilterBar filters)
        {
            filters.AddSearch("search", "Search by name, platform, or ID...");
            filters.AddSelect("status", "All statuses", new List<SelectOption<string>>
            {
                new SelectOption<string>("Connected", T("Connected")),
                new SelectOption<string>("Degraded", T("Degraded")),
                new SelectOption<string>("Disconnected", T("Disconnected")),
                new SelectOption<string>("Unknown", T("Unknown"))
            }, 18);
        }

        /// <inheritdoc />
        protected override void OnBuilt()
        {
            if (!CanCreate) Notice = "Ask a tenant administrator to connect a Harbor.";
            AddChild(Activity);
            Activity.Message = "Select a Harbor to see its activity.";
            Grid.CursorChanged += (s, row) => LoadMetrics(false);
        }

        /// <inheritdoc />
        protected override async Task<GridPage<Harbor>> FetchPageAsync(GridQuery query, CancellationToken token)
        {
            List<Harbor> all = await ReadAllAsync(token).ConfigureAwait(false);
            string search = Filter("search");
            string status = Filter("status");
            List<Harbor> filtered = all.Where(h => Matches(search, h.Name, h.Id, h.OsPlatform)
                && (status == "" || String.Equals(h.ConnectionStatus.ToString(), status, StringComparison.OrdinalIgnoreCase))).ToList();
            return Slice(filtered, query);
        }

        /// <inheritdoc />
        protected override IComparable? SortValue(Harbor row, string sortKey)
        {
            if (sortKey == "lastSeen") return row.LastSeenUtc ?? DateTime.MinValue;
            if (sortKey == "capacity") return row.MaxConcurrentJobs;
            return null;
        }

        /// <inheritdoc />
        protected override async Task<List<KpiItem>?> FetchKpisAsync(CancellationToken token)
        {
            List<Harbor> all = await ReadAllAsync(token).ConfigureAwait(false);
            return new List<KpiItem>
            {
                new KpiItem("Total Harbors", EntityUi.Number(Context, all.Count)),
                new KpiItem("Connected", EntityUi.Number(Context, all.Count(h => h.ConnectionStatus == HarborConnectionStatusEnum.Connected)), t => t.Success),
                new KpiItem("Disconnected", EntityUi.Number(Context, all.Count(h => h.ConnectionStatus == HarborConnectionStatusEnum.Disconnected)), t => t.Error),
                new KpiItem("Enabled", EntityUi.Number(Context, all.Count(h => h.Enabled)))
            };
        }

        /// <inheritdoc />
        protected override List<ActionMenuItem> RowActions(Harbor row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            items.Add(new ActionMenuItem("Details", () => HarborForms.ShowDetails(Context, row), "Enter"));
            if (CanCreate)
            {
                items.Add(new ActionMenuItem("Edit", () => OpenEdit(row), "e"));
                items.Add(new ActionMenuItem(row.Enabled ? "Disable" : "Enable", () => Toggle(row), "t"));
            }

            items.Add(new ActionMenuItem("View JSON", () => EntityUi.ShowJson(Context, row.Name, row), "j"));
            items.Add(new ActionMenuItem("Copy ID", () => Context.Clipboard.Copy(row.Id, "ID"), "y"));
            if (CanCreate)
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => RequestDelete(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <inheritdoc />
        protected override IEnumerable<ArmadaCommand> ExtraCommands()
        {
            return new List<ArmadaCommand>
            {
                Cmd(CommandPrefix + ".toggle", "Enable / Disable", () => { Harbor? h = Grid.Current; if (h != null) Toggle(h); }, () => Grid.Current != null && CanCreate, "t"),
                Cmd(CommandPrefix + ".metrics-range", "Activity range: next (1h, 24h, 7d)", CycleMetricsRange, null, "r"),
                Cmd(CommandPrefix + ".metrics-1h", "Activity range: last hour", () => SetMetricsRange("1h"), null),
                Cmd(CommandPrefix + ".metrics-24h", "Activity range: last 24 hours", () => SetMetricsRange("24h"), null),
                Cmd(CommandPrefix + ".metrics-7d", "Activity range: last 7 days", () => SetMetricsRange("7d"), null),
                Cmd(CommandPrefix + ".token-usage", "Token usage for this Harbor", OpenTokenUsage, () => Grid.Current != null, "u")
            };
        }

        /// <inheritdoc />
        protected override void OpenCreate()
        {
            if (!CanCreate) return;
            HarborForms.Open(Context, null, h => Reload());
        }

        /// <inheritdoc />
        protected override void OpenEdit(Harbor row)
        {
            if (!CanEdit(row)) return;
            HarborForms.Open(Context, row, h => Reload());
        }

        /// <inheritdoc />
        protected override Task DeleteAsync(Harbor row, CancellationToken token)
        {
            return Context.Client.DeleteHarborAsync(row.Id, token);
        }

        /// <inheritdoc />
        protected override string DeleteMessage(Harbor row)
        {
            return EntityUi.T(Context, "Delete \"{{name}}\"? Its registration is removed; running work on it is not affected until it reconnects.", "name", row.Name);
        }

        /// <inheritdoc />
        protected override string DeletedText(Harbor row)
        {
            return EntityUi.T(Context, "Harbor \"{{name}}\" deleted.", "name", row.Name);
        }

        #endregion

        #region Private-Methods

        private void LoadMetrics(bool force)
        {
            Harbor? row = Grid.Current;
            if (row == null)
            {
                _LoadedKey = null;
                Activity.HarborName = "";
                Activity.SetMetrics(null);
                Activity.Message = "Select a Harbor to see its activity.";
                return;
            }

            string key = row.Id + "|" + _Range;
            if (!force && key == _LoadedKey) return;
            if (_MetricsInFlight)
            {
                // One request at a time; the newest selection loads when it returns.
                _MetricsPending = true;
                return;
            }

            if (Activity.Metrics == null || !String.Equals(Activity.Metrics.HarborId, row.Id, StringComparison.Ordinal))
            {
                Activity.SetMetrics(null);
                Activity.Message = "Loading activity...";
            }

            Activity.HarborName = row.Name;
            _LoadedKey = key;
            _MetricsInFlight = true;
            string id = row.Id;
            string range = _Range;
            _ = Task.Run(async () =>
            {
                HarborMetrics? metrics = null;
                string message = "";
                try
                {
                    metrics = await Context.Client.GetHarborMetricsAsync(id, range).ConfigureAwait(false);
                    if (metrics == null) message = "The server returned no activity for this Harbor.";
                }
                catch (ArmadaApiException ex)
                {
                    message = MetricsError(ex);
                }

                Context.Dispatcher.Post(() => OnMetrics(key, metrics, message));
            });
        }

        private void OnMetrics(string key, HarborMetrics? metrics, string message)
        {
            _MetricsInFlight = false;
            _MetricsLoads++;
            if (key == _LoadedKey)
            {
                if (metrics != null)
                {
                    Activity.SetMetrics(metrics);
                    Activity.Message = "";
                }
                else
                {
                    // Keep the last charts of the same Harbor and range, and say why they are not fresh.
                    if (Activity.Metrics != null && !String.Equals(Activity.Metrics.HarborId + "|" + Activity.Metrics.Range, key, StringComparison.Ordinal)) Activity.SetMetrics(null);
                    Activity.Message = message;
                }
            }

            if (_MetricsPending)
            {
                _MetricsPending = false;
                LoadMetrics(false);
            }
        }

        private static string MetricsError(ArmadaApiException ex)
        {
            if (ex.IsTransport) return "Cannot reach the server; showing the last activity loaded.";
            if (ex.StatusCode == 401 || ex.StatusCode == 403) return "You are not allowed to see this Harbor's activity.";
            if (ex.StatusCode == 404) return "The server has no activity for this Harbor (it may need updating).";
            return "Could not load activity (HTTP " + ex.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").";
        }

        private async Task<List<Harbor>> ReadAllAsync(CancellationToken token)
        {
            List<Harbor>? list = await Context.Client.ListHarborsAsync(token).ConfigureAwait(false);
            return list ?? new List<Harbor>();
        }

        #endregion
    }
}
