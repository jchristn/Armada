namespace Armada.Tui.Screens.Build
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
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;

    /// <summary>
    /// Vessel Health (W4.3, <c>/vessels/health</c>), the dashboard's Health tab: the summary chips (Fail, Warn, Pass,
    /// Unknown, Not applicable, Not evaluated; <c>1</c> to <c>6</c> filter by one), evaluation progress and the last run,
    /// Evaluate all; server filters (name, fleet, overall, dependency, and test statuses, dirty, CI, divergence, branch
    /// count range, last commit range; <c>X</c> clears them) that round-trip through the route query like the
    /// dashboard's URL; the column chooser with Vessel and Overall pinned; server sort on all 17 fields and server
    /// paging; bulk Re-evaluate selected and Run action; and the row menu (View details, Branches, Override...,
    /// Re-evaluate, Open vessel, View JSON). Not thread-safe.
    /// </summary>
    public class VesselHealthScreen : OpsListScreen<VesselHealth>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string? Subtitle
        {
            get { return "Find repositories that need attention: divergence, dirty checkouts, stale branches, outdated or vulnerable dependencies, tests, and CI."; }
        }

        /// <summary>
        /// Route query keys the screen owns.
        /// </summary>
        public static readonly string[] QueryKeys = new string[] { "q", "fleet", "overall", "deps", "tests", "dirty", "ci", "div", "minBranches", "maxBranches", "after", "before", "sort", "dir", "page" };

        /// <summary>
        /// Name filter.
        /// </summary>
        public TextInput NameFilter { get; }

        /// <summary>
        /// Fleet filter.
        /// </summary>
        public SelectField<string> FleetFilter { get; }

        /// <summary>
        /// Overall status filter.
        /// </summary>
        public MultiSelectField<string> OverallFilter { get; }

        /// <summary>
        /// Dependency status filter.
        /// </summary>
        public MultiSelectField<string> DepsFilter { get; }

        /// <summary>
        /// Test status filter.
        /// </summary>
        public MultiSelectField<string> TestsFilter { get; }

        /// <summary>
        /// Dirty filter.
        /// </summary>
        public SelectField<string> DirtyFilter { get; }

        /// <summary>
        /// CI filter.
        /// </summary>
        public SelectField<string> CiFilter { get; }

        /// <summary>
        /// Divergence filter.
        /// </summary>
        public SelectField<string> DivergenceFilter { get; }

        /// <summary>
        /// Minimum branch count.
        /// </summary>
        public TextInput MinBranches { get; }

        /// <summary>
        /// Maximum branch count.
        /// </summary>
        public TextInput MaxBranches { get; }

        /// <summary>
        /// Last commit on or after.
        /// </summary>
        public DateField CommitAfter { get; } = new DateField();

        /// <summary>
        /// Last commit on or before.
        /// </summary>
        public DateField CommitBefore { get; } = new DateField();

        /// <summary>
        /// The summary from the last load, or null.
        /// </summary>
        public VesselHealthSummary? Summary { get; private set; } = null;

        /// <summary>
        /// Evaluation tracker.
        /// </summary>
        public HealthEvaluation Evaluation { get; }

        /// <summary>
        /// The open detail dialog, or null.
        /// </summary>
        public VesselHealthDialog? OpenDialog { get; private set; } = null;

        #endregion

        #region Private-Members

        private Dictionary<string, string> _DefaultBranches = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _SummaryFailed = false;
        private bool _Restoring = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public VesselHealthScreen(RouteMatch route, TuiContext context)
            : base(route, context, h => h.VesselId, "VesselHealthScreen", "Vessel Health")
        {
            Evaluation = new HealthEvaluation(this);
            Evaluation.Finished += (s, job) =>
            {
                string text = HealthEvaluation.FinishedText(job, Context.Loc, out NotificationSeverityEnum severity);
                Toast(severity, text);
                OpenDialog?.Load();
                Refresh();
            };
            Track(Evaluation);
            Grid.EmptyText = "No vessels match the current filters.";
            Grid.PageSizes = new List<int> { 10, 25, 50, 100, 250 };

            _Restoring = true;
            NameFilter = TextFilter("Name", 18, "Vessel name contains", true);
            FleetFilter = SelectFilter("Fleet", new List<SelectOption<string>> { new SelectOption<string>("", Tr("All fleets")) }, 16, true);
            OverallFilter = StatusFilter("Overall");
            DepsFilter = StatusFilter("Dependencies");
            TestsFilter = StatusFilter("Tests");
            DirtyFilter = SelectFilter("Dirty", new List<SelectOption<string>> { new SelectOption<string>("", Tr("Any")), new SelectOption<string>("yes", Tr("Yes")), new SelectOption<string>("no", Tr("No")) }, 8, true);
            CiFilter = SelectFilter("CI", new List<SelectOption<string>> { new SelectOption<string>("", Tr("Any")), new SelectOption<string>("yes", Tr("Has CI")), new SelectOption<string>("no", Tr("No CI")) }, 9, true);
            DivergenceFilter = SelectFilter("Divergence", new List<SelectOption<string>>
            {
                new SelectOption<string>("", Tr("Any")),
                new SelectOption<string>("Ahead", Tr("Ahead only")),
                new SelectOption<string>("Behind", Tr("Behind only")),
                new SelectOption<string>("Diverged", Tr("Diverged")),
                new SelectOption<string>("Even", Tr("Even")),
            }, 12, true);
            MinBranches = TextFilter("Branches min", 5, "Min", true);
            MaxBranches = TextFilter("max", 5, "Max", true);
            CommitAfter.ValueChanged += (s, e) => { if (!_Restoring && (CommitAfter.Value.Length == 0 || CommitAfter.ValueLocal.HasValue)) ServerFilterChanged(); };
            CommitBefore.ValueChanged += (s, e) => { if (!_Restoring && (CommitBefore.Value.Length == 0 || CommitBefore.ValueLocal.HasValue)) ServerFilterChanged(); };
            Filters.Add("Last commit after", CommitAfter, 12);
            Filters.Add("before", CommitBefore, 12);

            Column("vessel", "Vessel", h => (h.VesselName ?? h.VesselId) + (String.IsNullOrEmpty(h.CurrentBranch) ? "" : "  " + h.CurrentBranch), 4).Pinned = true;
            ServerSort(Column("fleet", "Fleet", h => h.FleetName ?? h.FleetId ?? "-", 2), "FleetName");
            GridColumn<VesselHealth> overall = Column("overall", "Overall", h => HealthText.Badge(h.OverallStatus, Context.Loc), 0, 16, null, (h, t) => StatusBadge.Style(h.OverallStatus, t));
            overall.Pinned = true;
            ServerSort(overall, "OverallStatus");
            Grid.Columns.First(c => c.Key == "vessel").Sortable = true;
            Grid.Columns.First(c => c.Key == "vessel").SortKey = "VesselName";
            ServerSort(Column("divergence", "Divergence", DivergenceText, 0, 12, null, (h, t) => StatusBadge.Style(h.DivergenceStatus, t)), "Divergence");
            ServerSort(Column("dirty", "Dirty", DirtyText, 0, 14), "IsDirty");
            ServerSort(Column("branches", "Branches", BranchesText, 0, 14), "BranchCount");
            ServerSort(Column("dependencies", "Dependencies", DepsText, 0, 22, null, (h, t) => StatusBadge.Style(h.DependencyStatus, t)), "OutdatedCount");
            ServerSort(Column("vulnerabilities", "Vulnerabilities", VulnText, 0, 22, null, (h, t) => StatusBadge.Style(h.VulnerabilityStatus, t)), "VulnerableCount");
            ServerSort(Column("tests", "Tests", h => HealthText.Badge(h.TestInfraStatus, Context.Loc), 0, 14, null, (h, t) => StatusBadge.Style(h.TestInfraStatus, t)), "TestInfraStatus");
            ServerSort(Column("ci", "CI", h => HealthText.Badge(h.CiStatus, Context.Loc), 0, 12, null, (h, t) => StatusBadge.Style(h.CiStatus, t)), "CiStatus");
            GridColumn<VesselHealth> lastCommit = Column("lastCommit", "Last commit", h => h.LastCommitUtc.HasValue ? Context.Loc.FormatRelative(h.LastCommitUtc.Value, Context.Clock.UtcNow) : "-", 0, 14);
            lastCommit.DefaultVisible = false;
            ServerSort(lastCommit, "LastCommitUtc");
            GridColumn<VesselHealth> evaluated = Column("evaluated", "Evaluated", h => h.EvaluatedUtc.HasValue ? Context.Loc.FormatRelative(h.EvaluatedUtc.Value, Context.Clock.UtcNow) : Tr("Never"), 0, 14);
            evaluated.DefaultVisible = false;
            ServerSort(evaluated, "EvaluatedUtc");

            ScreenActions.Add(new OpsScreenAction("evaluate-all", "Evaluate all", () => Evaluation.Start(null), "E", () => IsTenantAdmin && !Evaluation.Running && !Evaluation.Starting) { DynamicLabel = () => Evaluation.Running ? Tr("Evaluating...") : Tr("Evaluate all") });
            ScreenActions.Add(new OpsScreenAction("reevaluate-selected", "Re-evaluate selected", ReevaluateSelected, "R", () => IsTenantAdmin && Grid.Marked.Count > 0 && !Evaluation.Running));
            ScreenActions.Add(new OpsScreenAction("run-action", "Run action...", RunAction, "A", () => IsTenantAdmin && Grid.Marked.Count > 0));
            ScreenActions.Add(new OpsScreenAction("clear-selection", "Clear selection", () => Grid.ClearMarks(), null, () => Grid.Marked.Count > 0));
            ScreenActions.Add(new OpsScreenAction("clear-filters", "Clear filters", ClearFilters, "X", HasActiveFilters));
            ScreenActions.Add(new OpsScreenAction("import", "Import repositories", () => Context.Navigate("/vessels/import"), "I", () => HasLoaded && ServerTotal == 0 && !HasActiveFilters() && (Summary == null || Summary.TotalVessels == 0)));
            string[] chipKeys = new string[] { "1", "2", "3", "4", "5", "6" };
            VesselHealthStatusEnum[] chipStatuses = new VesselHealthStatusEnum[] { VesselHealthStatusEnum.Fail, VesselHealthStatusEnum.Warn, VesselHealthStatusEnum.Pass, VesselHealthStatusEnum.Unknown, VesselHealthStatusEnum.NotApplicable, VesselHealthStatusEnum.Unknown };
            string[] chipLabels = new string[] { "Fail", "Warn", "Pass", "Unknown", "Not applicable", "Not evaluated" };
            for (int i = 0; i < chipKeys.Length; i++)
            {
                VesselHealthStatusEnum status = chipStatuses[i];
                OpsScreenAction chip = new OpsScreenAction("chip-" + (i + 1), Tr("Show") + ": " + Tr(chipLabels[i]), () => ToggleChip(status), chipKeys[i]);
                chip.Toolbar = false;
                ScreenActions.Add(chip);
            }

            RowActions.Add(new OpsAction<VesselHealth>("details", "View details", h => OpenDetail(h, "summary"), "o"));
            RowActions.Add(new OpsAction<VesselHealth>("branches", "Branches", h => OpenBranches(h.VesselId, h.VesselName ?? h.VesselId), "b"));
            RowActions.Add(new OpsAction<VesselHealth>("override", "Override...", h => OpenDetail(h, "overrides"), "O", h => IsTenantAdmin));
            RowActions.Add(new OpsAction<VesselHealth>("reevaluate", "Re-evaluate", h => Evaluation.Start(new List<string> { h.VesselId }), "e", h => IsTenantAdmin && !Evaluation.Running));
            RowActions.Add(new OpsAction<VesselHealth>("vessel", "Open vessel", h => Context.Navigate("/vessels/" + Uri.EscapeDataString(h.VesselId)), "v"));
            RowActions.Add(new OpsAction<VesselHealth>("json", "View JSON", h => ShowJson(Tr("Health: {{name}}", LocalizationArgs.Of("name", h.VesselName ?? h.VesselId)), h), "j"));
            RowActions.Add(new OpsAction<VesselHealth>("copy-id", "Copy ID", h => Copy(h.VesselId, "Vessel ID"), "y"));

            RestoreFromRoute();
            _Restoring = false;
            Reference.Ensure("fleets");
            Reference.Changed += (s, n) =>
            {
                if (n != "fleets") return;
                string? current = FleetFilter.Value;
                List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All fleets")) };
                options.AddRange(Reference.Fleets.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => new SelectOption<string>(f.Id, f.Name)));
                FleetFilter.Options = options;
                FleetFilter.SetValue(current ?? "");
            };
            LoadDefaultBranches();
            Evaluation.Discover();
            string? sort = OpsHandoff.Get(route, "sort");
            string? dir = OpsHandoff.Get(route, "dir");
            Start(SortKeyFor(sort) ?? "OverallStatus", dir == "desc");
            if (sort != null || dir != null) Grid.Restore(SortKeyFor(sort) ?? "OverallStatus", dir == "desc", Grid.PageSize);
            if (Int32.TryParse(OpsHandoff.Get(route, "page"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int page) && page > 1) Grid.GoToPage(page);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when any filter (not sort or page) is set.
        /// </summary>
        /// <returns>True when filtered.</returns>
        public bool HasActiveFilters()
        {
            return NameFilter.Value.Trim().Length > 0 || !String.IsNullOrEmpty(FleetFilter.Value) || OverallFilter.Values.Count > 0 || DepsFilter.Values.Count > 0
                || TestsFilter.Values.Count > 0 || !String.IsNullOrEmpty(DirtyFilter.Value) || !String.IsNullOrEmpty(CiFilter.Value) || !String.IsNullOrEmpty(DivergenceFilter.Value)
                || MinBranches.Value.Trim().Length > 0 || MaxBranches.Value.Trim().Length > 0 || CommitAfter.Value.Trim().Length > 0 || CommitBefore.Value.Trim().Length > 0;
        }

        /// <summary>
        /// Build the server request for a grid query (the dashboard's <c>buildEnumerateRequest</c>). Date bounds are
        /// inclusive local days: "after" becomes the start of that day and "before" the start of the following day.
        /// </summary>
        /// <param name="query">Grid query.</param>
        /// <returns>Request.</returns>
        public VesselHealthEnumerateRequest BuildRequest(GridQuery query)
        {
            VesselHealthEnumerateRequest req = new VesselHealthEnumerateRequest();
            req.PageNumber = Math.Max(1, query.PageNumber);
            req.PageSize = Math.Min(500, Math.Max(1, query.PageSize));
            req.SortBy = Enum.TryParse<VesselHealthSortEnum>(query.SortKey ?? "OverallStatus", out VesselHealthSortEnum sort) ? sort : VesselHealthSortEnum.OverallStatus;
            req.SortDescending = query.SortKey != null && query.SortDescending;
            string name = NameFilter.Value.Trim();
            if (name.Length > 0) req.NameContains = name;
            if (!String.IsNullOrEmpty(FleetFilter.Value)) req.FleetId = FleetFilter.Value;
            if (OverallFilter.Values.Count > 0) req.OverallStatus = ParseStatuses(OverallFilter.Values);
            if (DepsFilter.Values.Count > 0) req.DependencyStatus = ParseStatuses(DepsFilter.Values);
            if (TestsFilter.Values.Count > 0) req.TestInfraStatus = ParseStatuses(TestsFilter.Values);
            if (DirtyFilter.Value == "yes") req.IsDirty = true;
            else if (DirtyFilter.Value == "no") req.IsDirty = false;
            if (CiFilter.Value == "yes") req.HasCiConfig = true;
            else if (CiFilter.Value == "no") req.HasCiConfig = false;
            if (Enum.TryParse<VesselDivergenceFilterEnum>(DivergenceFilter.Value ?? "", out VesselDivergenceFilterEnum div)) req.Divergence = div;
            if (Int32.TryParse(MinBranches.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int min)) req.MinBranchCount = min;
            if (Int32.TryParse(MaxBranches.Value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int max)) req.MaxBranchCount = max;
            DateTime? after = CommitAfter.ValueLocal;
            DateTime? before = CommitBefore.ValueLocal;
            if (after.HasValue) req.LastCommitAfterUtc = DateTime.SpecifyKind(after.Value.Date, DateTimeKind.Local).ToUniversalTime();
            if (before.HasValue) req.LastCommitBeforeUtc = DateTime.SpecifyKind(before.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
            return req;
        }

        /// <summary>
        /// The current filters, sort, and page as the dashboard's query string (defaults omitted).
        /// </summary>
        /// <returns>Query values.</returns>
        public Dictionary<string, string> ToQuery()
        {
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            string name = NameFilter.Value.Trim();
            if (name.Length > 0) q["q"] = name;
            if (!String.IsNullOrEmpty(FleetFilter.Value)) q["fleet"] = FleetFilter.Value!;
            if (OverallFilter.Values.Count > 0) q["overall"] = String.Join(",", OverallFilter.Values);
            if (DepsFilter.Values.Count > 0) q["deps"] = String.Join(",", DepsFilter.Values);
            if (TestsFilter.Values.Count > 0) q["tests"] = String.Join(",", TestsFilter.Values);
            if (!String.IsNullOrEmpty(DirtyFilter.Value)) q["dirty"] = DirtyFilter.Value!;
            if (!String.IsNullOrEmpty(CiFilter.Value)) q["ci"] = CiFilter.Value!;
            if (!String.IsNullOrEmpty(DivergenceFilter.Value)) q["div"] = DivergenceFilter.Value!;
            if (MinBranches.Value.Trim().Length > 0) q["minBranches"] = MinBranches.Value.Trim();
            if (MaxBranches.Value.Trim().Length > 0) q["maxBranches"] = MaxBranches.Value.Trim();
            if (CommitAfter.ValueLocal.HasValue) q["after"] = CommitAfter.ValueLocal.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (CommitBefore.ValueLocal.HasValue) q["before"] = CommitBefore.ValueLocal.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string sort = Grid.SortKey ?? "OverallStatus";
            bool desc = Grid.SortKey != null && Grid.SortDescending;
            if (sort != "OverallStatus" || desc)
            {
                q["sort"] = sort;
                q["dir"] = desc ? "desc" : "asc";
            }

            if (Grid.PageNumber > 1) q["page"] = Grid.PageNumber.ToString(CultureInfo.InvariantCulture);
            return q;
        }

        /// <summary>
        /// Open the health detail dialog for a row.
        /// </summary>
        /// <param name="row">Row.</param>
        /// <param name="section">Section to open.</param>
        /// <returns>The dialog.</returns>
        public VesselHealthDialog OpenDetail(VesselHealth row, string section)
        {
            _DefaultBranches.TryGetValue(row.VesselId, out string? branch);
            VesselHealthDialog dialog = new VesselHealthDialog(this, row.VesselId, row.VesselName ?? row.VesselId, branch, IsTenantAdmin, Evaluation, section, null, Refresh, (id, name) => OpenBranches(id, name));
            OpenDialog = dialog;
            Context.Modals.Show(dialog, r => { if (ReferenceEquals(OpenDialog, dialog)) OpenDialog = null; });
            return dialog;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override async Task<GridPage<VesselHealth>> FetchAsync(GridQuery query, CancellationToken token)
        {
            VesselHealthEnumerateRequest req = BuildRequest(query);
            Task<EnumerationResult<VesselHealth>?> rows = Context.Client.EnumerateVesselHealthAsync(req, token);
            Task<VesselHealthSummary?> summary = Context.Client.GetVesselHealthSummaryAsync(token);
            try { await Task.WhenAll(rows, summary).ConfigureAwait(false); }
            catch (Exception) when (rows.IsCompletedSuccessfully)
            {
                // The summary is optional; the table still works without it.
            }

            VesselHealthSummary? s = summary.IsCompletedSuccessfully ? summary.Result : null;
            bool failed = !summary.IsCompletedSuccessfully;
            Post(() =>
            {
                if (s != null) Summary = s;
                _SummaryFailed = failed && Summary == null;
            });
            List<VesselHealth> list = rows.Result?.Objects ?? new List<VesselHealth>();
            return new GridPage<VesselHealth>(list, rows.Result?.TotalRecords ?? list.Count);
        }

        /// <inheritdoc />
        protected override void OnLoaded(IReadOnlyList<VesselHealth> rows)
        {
            bool filtered = HasActiveFilters();
            Grid.EmptyText = rows.Count == 0 && !filtered && (Summary == null || Summary.TotalVessels == 0)
                ? Tr("No vessels yet") + ". " + Tr("Import repositories to start tracking their health.") + "  (I " + Tr("Import repositories") + ")"
                : Tr("No vessels match the current filters.") + (filtered ? "  (X " + Tr("Clear filters") + ")" : "");
            UpdateRoute();
        }

        /// <inheritdoc />
        protected override void OpenRow(VesselHealth row)
        {
            OpenDetail(row, "summary");
        }

        /// <inheritdoc />
        protected override string RowTitle(VesselHealth row)
        {
            return row.VesselName ?? row.VesselId;
        }

        /// <inheritdoc />
        protected override int AboveHeight(int width)
        {
            int rows = 1;
            if (Summary != null && Summary.TotalVessels > 0 && Summary.NotEvaluated == Summary.TotalVessels) rows++;
            if (Grid.Marked.Count > 0) rows++;
            return rows;
        }

        /// <inheritdoc />
        protected override void RenderAbove(ISurface surface, int width)
        {
            int y = 0;
            int x = 0;
            if (Summary != null)
            {
                List<KeyValuePair<string, long>> tiles = new List<KeyValuePair<string, long>>
                {
                    new KeyValuePair<string, long>("Fail", Summary.Fail),
                    new KeyValuePair<string, long>("Warn", Summary.Warn),
                    new KeyValuePair<string, long>("Pass", Summary.Pass),
                    new KeyValuePair<string, long>("Unknown", Summary.Unknown),
                    new KeyValuePair<string, long>("Not applicable", Summary.NotApplicable),
                    new KeyValuePair<string, long>("Not evaluated", Summary.NotEvaluated),
                };
                for (int i = 0; i < tiles.Count; i++)
                {
                    if (i == 4 && Summary.NotApplicable == 0) continue;
                    string status = tiles[i].Key == "Not evaluated" ? "Unknown" : tiles[i].Key == "Not applicable" ? "NotApplicable" : tiles[i].Key;
                    bool active = i != 5 && OverallFilter.Values.Count == 1 && OverallFilter.Values[0] == status;
                    string text = (active ? "[" : "") + (i + 1) + " " + Context.Loc.FormatNumber(tiles[i].Value) + " " + Tr(tiles[i].Key) + (active ? "]" : "") + "  ";
                    if (x >= width) break;
                    x += SurfaceText.Draw(surface, x, y, text, StatusBadge.Style(status, Theme), width - x);
                }

                string total = "| " + Tr("{{count}} vessels", LocalizationArgs.Of("count", Context.Loc.FormatNumber(Summary.TotalVessels))) + "  ";
                if (x < width) x += SurfaceText.Draw(surface, x, y, total, Theme.Muted, width - x);
            }
            else if (_SummaryFailed)
            {
                x += SurfaceText.Draw(surface, x, y, Tr("Summary unavailable.") + "  ", Theme.Muted, width);
            }

            string progress = Evaluation.ProgressText(Context.Loc, Context.Clock.UtcNow);
            if (progress.Length > 0 && x < width) SurfaceText.Draw(surface, x, y, progress, Evaluation.Running ? Theme.Info : Theme.Muted, width - x);
            y++;
            if (Summary != null && Summary.TotalVessels > 0 && Summary.NotEvaluated == Summary.TotalVessels)
            {
                string cta = Tr("No vessel has been evaluated yet.") + " " + Tr("Run an evaluation to grade every vessel. Dependency checks can take a minute per repository.") + (IsTenantAdmin ? "  (E " + Tr("Evaluate now") + ")" : "");
                SurfaceText.Draw(surface, 0, y++, cta, Theme.Warning, width);
            }

            if (Grid.Marked.Count > 0)
            {
                string count = Tr(Grid.Marked.Count == 1 ? "{{count}} vessel selected" : "{{count}} vessels selected", LocalizationArgs.Of("count", Context.Loc.FormatNumber(Grid.Marked.Count)));
                string actions = IsTenantAdmin ? "   R " + Tr("Re-evaluate selected") + "  A " + Tr("Run action...") : "";
                SurfaceText.Draw(surface, 0, y, count + actions, Theme.Info, width);
            }
        }

        #endregion

        #region Private-Methods

        private static List<VesselHealthStatusEnum> ParseStatuses(IEnumerable<string> values)
        {
            List<VesselHealthStatusEnum> list = new List<VesselHealthStatusEnum>();
            foreach (string v in values)
            {
                if (Enum.TryParse<VesselHealthStatusEnum>(v, true, out VesselHealthStatusEnum s) && !list.Contains(s)) list.Add(s);
            }

            return HealthText.Statuses.Where(list.Contains).ToList();
        }

        private static string? SortKeyFor(string? raw)
        {
            if (String.IsNullOrEmpty(raw)) return null;
            foreach (string name in Enum.GetNames(typeof(VesselHealthSortEnum)))
            {
                if (String.Equals(name, raw, StringComparison.OrdinalIgnoreCase)) return name;
            }

            return null;
        }

        private static void ServerSort(GridColumn<VesselHealth> column, string sortKey)
        {
            column.Sortable = true;
            column.SortKey = sortKey;
        }

        private MultiSelectField<string> StatusFilter(string label)
        {
            MultiSelectField<string> field = new MultiSelectField<string>();
            field.ModalHost = Context.Modals;
            field.PickerTitle = label;
            field.Placeholder = "Any";
            field.Options = HealthText.Statuses.Select(s => new SelectOption<string>(s.ToString(), Tr(HealthText.StatusLabel(s)))).ToList();
            field.ValueChanged += (s, e) => { if (!_Restoring) ServerFilterChanged(); };
            return Filters.Add(label, field, 14);
        }

        private void RestoreFromRoute()
        {
            RouteMatch route = Route;
            NameFilter.Value = OpsHandoff.Get(route, "q") ?? "";
            FleetFilter.Options = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All fleets")) };
            string? fleet = OpsHandoff.Get(route, "fleet");
            if (fleet != null)
            {
                FleetFilter.Options.Add(new SelectOption<string>(fleet, fleet));
                FleetFilter.SetValue(fleet);
            }

            OverallFilter.SetValues(Statuses(OpsHandoff.Get(route, "overall")), false);
            DepsFilter.SetValues(Statuses(OpsHandoff.Get(route, "deps")), false);
            TestsFilter.SetValues(Statuses(OpsHandoff.Get(route, "tests")), false);
            DirtyFilter.SetValue(Tri(OpsHandoff.Get(route, "dirty")));
            CiFilter.SetValue(Tri(OpsHandoff.Get(route, "ci")));
            string? div = OpsHandoff.Get(route, "div");
            string divValue = new string[] { "Ahead", "Behind", "Diverged", "Even" }.FirstOrDefault(d => String.Equals(d, div, StringComparison.OrdinalIgnoreCase)) ?? "";
            DivergenceFilter.SetValue(divValue);
            MinBranches.Value = Digits(OpsHandoff.Get(route, "minBranches"));
            MaxBranches.Value = Digits(OpsHandoff.Get(route, "maxBranches"));
            CommitAfter.Value = DateOnly(OpsHandoff.Get(route, "after"));
            CommitBefore.Value = DateOnly(OpsHandoff.Get(route, "before"));
        }

        private static IEnumerable<string> Statuses(string? raw)
        {
            if (String.IsNullOrEmpty(raw)) return Enumerable.Empty<string>();
            return ParseStatuses(raw!.Split(',').Select(s => s.Trim())).Select(s => s.ToString());
        }

        private static string Tri(string? raw)
        {
            string v = (raw ?? "").ToLowerInvariant();
            if (v == "yes" || v == "true" || v == "1") return "yes";
            if (v == "no" || v == "false" || v == "0") return "no";
            return "";
        }

        private static string Digits(string? raw)
        {
            string v = (raw ?? "").Trim();
            return v.Length > 0 && v.All(Char.IsDigit) ? Int32.Parse(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : "";
        }

        private static string DateOnly(string? raw)
        {
            string v = (raw ?? "").Trim();
            return DateTime.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) ? v : "";
        }

        private void UpdateRoute()
        {
            if (!IsLive) return;
            RouteMatch? current = Context.Router.Current;
            if (current == null || !ReferenceEquals(current.Route, Route.Route) && current.Path != Route.Path) return;
            Context.Router.ReplaceQuietly("/vessels/health" + RouteMatch.BuildQuery(ToQuery()));
        }

        private void ToggleChip(VesselHealthStatusEnum status)
        {
            string value = status.ToString();
            bool active = OverallFilter.Values.Count == 1 && OverallFilter.Values[0] == value;
            OverallFilter.SetValues(active ? Enumerable.Empty<string>() : new string[] { value });
        }

        private void ClearFilters()
        {
            _Restoring = true;
            NameFilter.Value = "";
            FleetFilter.SetValue("");
            OverallFilter.SetValues(null, false);
            DepsFilter.SetValues(null, false);
            TestsFilter.SetValues(null, false);
            DirtyFilter.SetValue("");
            CiFilter.SetValue("");
            DivergenceFilter.SetValue("");
            MinBranches.Value = "";
            MaxBranches.Value = "";
            CommitAfter.Value = "";
            CommitBefore.Value = "";
            _Restoring = false;
            ServerFilterChanged();
        }

        private void ReevaluateSelected()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            Evaluation.Start(ids, () => Grid.ClearMarks());
        }

        private void RunAction()
        {
            List<string> ids = Grid.Marked.ToList();
            if (ids.Count == 0) return;
            FleetActionRunFlow flow = FleetActionRunFlow.Start(this, ids, null, null, result =>
            {
                Grid.ClearMarks();
                Context.Navigate("/fleet-actions/runs/" + Uri.EscapeDataString(result.RunId));
            });
            flow.Kind.SetValue("Mission");
        }

        private VesselBranchesDialog OpenBranches(string vesselId, string vesselName)
        {
            VesselBranchesDialog dialog = new VesselBranchesDialog(this, vesselId, vesselName);
            Context.Modals.Show(dialog);
            return dialog;
        }

        private void LoadDefaultBranches()
        {
            Call((c, t) => c.ListVesselsAsync(new ArmadaPageQuery(1, 9999), t), result =>
            {
                Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Vessel v in result?.Objects ?? new List<Vessel>())
                {
                    if (!String.IsNullOrEmpty(v.DefaultBranch)) map[v.Id] = v.DefaultBranch;
                }

                _DefaultBranches = map;
            }, null, ex => { });
        }

        private string DivergenceText(VesselHealth h)
        {
            if (!h.AheadOfDefault.HasValue && !h.BehindDefault.HasValue) return "-";
            return "^" + Context.Loc.FormatNumber(h.AheadOfDefault ?? 0) + " v" + Context.Loc.FormatNumber(h.BehindDefault ?? 0);
        }

        private string DirtyText(VesselHealth h)
        {
            if (!h.IsDirty.HasValue) return "-";
            if (h.IsDirty.Value) return "! " + Tr("Dirty");
            return (h.UntrackedCount ?? 0) > 0 ? Tr("{{count}} untracked", LocalizationArgs.Of("count", Context.Loc.FormatNumber(h.UntrackedCount ?? 0))) : Tr("Clean");
        }

        private string BranchesText(VesselHealth h)
        {
            if (!h.BranchCount.HasValue) return "-";
            string text = Context.Loc.FormatNumber(h.BranchCount.Value);
            if ((h.StaleBranchCount ?? 0) > 0) text += " " + Tr("({{count}} stale)", LocalizationArgs.Of("count", Context.Loc.FormatNumber(h.StaleBranchCount ?? 0)));
            return text;
        }

        private string DepsText(VesselHealth h)
        {
            string text = HealthText.Badge(h.DependencyStatus, Context.Loc);
            if (h.OutdatedCount.HasValue)
            {
                text += " " + Context.Loc.FormatNumber(h.OutdatedCount.Value);
                if ((h.OutdatedMajorCount ?? 0) > 0) text += " " + Tr("({{count}} major)", LocalizationArgs.Of("count", Context.Loc.FormatNumber(h.OutdatedMajorCount ?? 0)));
            }

            return text;
        }

        private string VulnText(VesselHealth h)
        {
            string text = HealthText.Badge(h.VulnerabilityStatus, Context.Loc);
            if ((h.VulnerableCount ?? 0) > 0) text += " " + Context.Loc.FormatNumber(h.VulnerableCount ?? 0) + " " + Tr(HealthText.SeverityLabel(h.MaxVulnerabilitySeverity));
            return text;
        }

        #endregion
    }
}
