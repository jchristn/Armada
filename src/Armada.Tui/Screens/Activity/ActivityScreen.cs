namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Activity, All Activity tab (dashboard <c>History.tsx</c>, route <c>/activity?source=history</c>): the unified
    /// operational timeline with KPIs (visible entries, errors, warnings, source types), filters (text, backlog item,
    /// actor, vessel, source type, postmortem only; Apply and Reset), saved views (save, apply, delete; stored in
    /// <c>tui-activity-views.json</c> beside the TUI preferences), exports to a file (JSON, CSV, Markdown), per-source
    /// counts, and a row menu (View, View JSON, Open Workspace, Delete for request entries). Deep links accept the
    /// dashboard's query parameters (objectiveId, text, actor, vesselId, sourceType, postmortemOnly). Not
    /// thread-safe.
    /// </summary>
    public class ActivityScreen : GridScreen<HistoricalTimelineEntry>
    {
        #region Public-Members

        /// <summary>
        /// KPI cards.
        /// </summary>
        public KpiBar Kpis { get; } = new KpiBar();

        /// <summary>
        /// Filter row.
        /// </summary>
        public FilterStrip Filters { get; } = new FilterStrip();

        /// <summary>
        /// Text search.
        /// </summary>
        public InputField TextFilter { get; } = new InputField();

        /// <summary>
        /// Backlog item filter ("all" for every item).
        /// </summary>
        public SelectField<string> ObjectiveFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Actor filter.
        /// </summary>
        public InputField ActorFilter { get; } = new InputField();

        /// <summary>
        /// Vessel filter ("all" for every vessel).
        /// </summary>
        public SelectField<string> VesselFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Source type filter ("all" for every type).
        /// </summary>
        public SelectField<string> SourceTypeFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Postmortem context only.
        /// </summary>
        public ToggleField PostmortemOnly { get; } = new ToggleField(false, "Postmortem context only");

        /// <summary>
        /// Saved views.
        /// </summary>
        public ActivitySavedViewStore SavedViews { get; }

        /// <summary>
        /// Entries from the last load.
        /// </summary>
        public IReadOnlyList<HistoricalTimelineEntry> Entries
        {
            get { return _Entries; }
        }

        /// <summary>
        /// Export in progress ("json", "csv", "md"), or null.
        /// </summary>
        public string? Exporting { get; private set; } = null;

        /// <summary>
        /// True while loading.
        /// </summary>
        public bool Loading { get; private set; } = false;

        #endregion

        #region Private-Members

        private readonly TextBlock _SourceCounts = new TextBlock("", t => t.Muted);
        private readonly TextBlock _SavedViewsLine = new TextBlock("", t => t.Muted);
        private readonly Button _ExportJson;
        private readonly Button _ExportCsv;
        private readonly Button _ExportMd;
        private List<HistoricalTimelineEntry> _Entries = new List<HistoricalTimelineEntry>();
        private Dictionary<string, string> _VesselNames = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public ActivityScreen(RouteMatch route, TuiContext context)
            : base(route, context, "History", "Unified operational memory across backlog refinement, planning, dispatch, releases, deployments, incidents, requests, and events.", "history", e => e.Id)
        {
            SavedViews = new ActivitySavedViewStore(ActivitySavedViewStore.PathFor(context.Prefs.FilePath));

            Header.AddButton("Save View", OpenSaveView, "w");
            Header.AddButton("Saved Views", OpenSavedViews, "v");
            _ExportJson = Header.AddButton("Export JSON", () => Export("json"));
            _ExportCsv = Header.AddButton("Export CSV", () => Export("csv"));
            _ExportMd = Header.AddButton("Export Markdown", () => Export("md"));
            Header.AddButton("Apply", Load, "a");
            Header.AddButton("Reset", ResetFilters);
            Header.AddButton("Refresh", Load, "F5");
            AddFixed(Header, w => Header.HeightFor(w));
            AddFixed(Kpis, w => Kpis.PreferredHeight);

            TextFilter.Placeholder = "Search title, status, route, or metadata...";
            ActorFilter.Placeholder = "Filter by actor or principal...";
            ConfigureSelect(ObjectiveFilter, "All backlog items");
            ConfigureSelect(VesselFilter, "All vessels");
            ConfigureSelect(SourceTypeFilter, "All source types");
            Filters.Add("Search", TextFilter, 34);
            Filters.Add("Backlog Item", ObjectiveFilter, 24);
            Filters.Add("Actor", ActorFilter, 24);
            Filters.Add("Vessel", VesselFilter, 20);
            Filters.Add("Source", SourceTypeFilter, 18);
            Filters.Add("Postmortem", PostmortemOnly, 28);
            Filters.Applied += (s, e) => Load();
            AddFixed(Filters, w => Filters.HeightFor(w));
            AddFixed(_SavedViewsLine, w => SavedViews.Views.Count > 0 ? Math.Min(2, _SavedViewsLine.Lines(w).Count) : 0);
            AddFixed(_SourceCounts, w => _Entries.Count > 0 ? Math.Min(2, _SourceCounts.Lines(w).Count) : 0);
            _SavedViewsLine.Translate = false;
            _SourceCounts.Translate = false;

            Grid.MultiSelect = false;
            Grid.EmptyText = "No history entries match the current filters.";
            Grid.AddColumn(new GridColumn<HistoricalTimelineEntry>("when", "When", e => ScreenOps.Relative(Context, e.OccurredUtc)) { Width = 10, Style = (e, t) => t.Muted });
            Grid.AddColumn(new GridColumn<HistoricalTimelineEntry>("title", "Title", e => String.IsNullOrEmpty(e.Description) ? e.Title : e.Title + "  " + e.Description) { Weight = 4 });
            Grid.AddColumn(new GridColumn<HistoricalTimelineEntry>("source", "Source", e => e.SourceType) { Width = 16 });
            Grid.AddColumn(new GridColumn<HistoricalTimelineEntry>("status", "Status", e => String.IsNullOrEmpty(e.Status) ? "-" : StatusMarker(e.Severity) + e.Status) { Width = 14, Style = (e, t) => SeverityStyle(e.Severity, t) });
            Grid.AddColumn(new GridColumn<HistoricalTimelineEntry>("actor", "Actor", e => String.IsNullOrEmpty(e.ActorDisplay) ? "-" : e.ActorDisplay!) { Width = 18, Style = (e, t) => t.Muted });
            Grid.AddColumn(new GridColumn<HistoricalTimelineEntry>("vessel", "Vessel", e => VesselName(e.VesselId)) { Width = 16, Style = (e, t) => t.Muted });
            BindPreferences(null, false, 50);
            AddFill(Grid);

            ApplyRouteQuery(route.Query);
            UpdateSavedViewsLine();
            UpdateKpis();
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

        /// <summary>
        /// The timeline query for the current filters (the dashboard's <c>buildQuery</c>).
        /// </summary>
        /// <param name="pageSize">Page size (250 for the list, 5000 for exports).</param>
        /// <returns>Query.</returns>
        public HistoricalTimelineQuery BuildQuery(int pageSize = 250)
        {
            HistoricalTimelineQuery q = new HistoricalTimelineQuery();
            q.PageNumber = 1;
            q.PageSize = pageSize;
            q.ObjectiveId = Selected(ObjectiveFilter);
            q.Text = TextFilter.Value.Length > 0 ? TextFilter.Value : null;
            q.Actor = ActorFilter.Value.Length > 0 ? ActorFilter.Value : null;
            q.VesselId = Selected(VesselFilter);
            string? source = Selected(SourceTypeFilter);
            q.SourceTypes = source == null ? new List<string>() : new List<string> { source };
            q.PostmortemOnly = PostmortemOnly.Value;
            return q;
        }

        /// <summary>
        /// Load the timeline with the current filters, plus the vessel and backlog item pickers.
        /// </summary>
        public void Load()
        {
            HistoricalTimelineQuery query = BuildQuery();
            Loading = true;
            Grid.SetLoading();
            ScreenOps.Run(Context, async () =>
            {
                Task<EnumerationResult<HistoricalTimelineEntry>?> history = Context.Client.EnumerateHistoryTimelineAsync(query);
                Task<EnumerationResult<Vessel>?> vessels = Context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 9999));
                ObjectiveQuery oq = new ObjectiveQuery();
                oq.PageSize = 9999;
                Task<EnumerationResult<Objective>?> objectives = Context.Client.ListObjectivesAsync(oq);
                await Task.WhenAll(history, vessels, objectives).ConfigureAwait(false);
                ActivityLoadResult result = new ActivityLoadResult();
                result.Entries = history.Result?.Objects ?? new List<HistoricalTimelineEntry>();
                result.Vessels = vessels.Result?.Objects ?? new List<Vessel>();
                result.Objectives = objectives.Result?.Objects ?? new List<Objective>();
                return result;
            }, result =>
            {
                Loading = false;
                _Entries = result.Entries;
                _VesselNames = result.Vessels.ToDictionary(v => v.Id, v => v.Name, StringComparer.Ordinal);
                SetOptions(ObjectiveFilter, "All backlog items", result.Objectives.Select(o => new SelectOption<string>(o.Id, o.Title)));
                SetOptions(VesselFilter, "All vessels", result.Vessels.Select(v => new SelectOption<string>(v.Id, v.Name)));
                SetOptions(SourceTypeFilter, "All source types", _Entries.Select(e => e.SourceType).Distinct().OrderBy(s => s, StringComparer.Ordinal).Select(s => new SelectOption<string>(s, s)));
                Grid.SetLocalRows(_Entries);
                UpdateKpis();
            }, "Failed to load history.", ex =>
            {
                Loading = false;
                Grid.SetError(ex.Message);
            });
        }

        /// <summary>
        /// Clear every filter and reload.
        /// </summary>
        public void ResetFilters()
        {
            TextFilter.Value = "";
            ActorFilter.Value = "";
            ObjectiveFilter.SetValue("all");
            VesselFilter.SetValue("all");
            SourceTypeFilter.SetValue("all");
            PostmortemOnly.SetValue(false, false);
            Load();
        }

        /// <summary>
        /// Save the current filters as a named view.
        /// </summary>
        /// <param name="name">View name.</param>
        /// <returns>The view, or null for a blank name.</returns>
        public ActivitySavedView? SaveView(string name)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length == 0) return null;
            ActivitySavedView view = new ActivitySavedView();
            view.Id = "hsv_" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            view.Name = trimmed;
            view.Query = BuildQuery();
            view.CreatedUtc = Context.Clock.UtcNow;
            SavedViews.Add(view);
            UpdateSavedViewsLine();
            return view;
        }

        /// <summary>
        /// Apply a saved view's filters (like the dashboard, this sets the filters; Apply reloads). The TUI reloads
        /// right away.
        /// </summary>
        /// <param name="view">View.</param>
        public void ApplySavedView(ActivitySavedView view)
        {
            if (view == null) return;
            HistoricalTimelineQuery q = view.Query;
            EnsureOption(ObjectiveFilter, q.ObjectiveId);
            EnsureOption(VesselFilter, q.VesselId);
            string? source = q.SourceTypes != null && q.SourceTypes.Count > 0 ? q.SourceTypes[0] : null;
            EnsureOption(SourceTypeFilter, source);
            ObjectiveFilter.SetValue(q.ObjectiveId ?? "all");
            VesselFilter.SetValue(q.VesselId ?? "all");
            SourceTypeFilter.SetValue(source ?? "all");
            TextFilter.Value = q.Text ?? "";
            ActorFilter.Value = q.Actor ?? "";
            PostmortemOnly.SetValue(q.PostmortemOnly, false);
            Load();
        }

        /// <summary>
        /// Delete a saved view.
        /// </summary>
        /// <param name="id">View id.</param>
        public void DeleteSavedView(string id)
        {
            SavedViews.Remove(id);
            UpdateSavedViewsLine();
        }

        /// <summary>
        /// Ask for a path and export the current view (up to 5000 entries) as JSON, CSV, or Markdown.
        /// </summary>
        /// <param name="format">"json", "csv", or "md".</param>
        public void Export(string format)
        {
            if (Exporting != null) return;
            string extension = format == "md" ? "md" : format;
            PathPrompt.AskSave(Context, "Export History", ActivityExports.FileName(Context.Clock.UtcNow, extension), path => ExportTo(format, path));
        }

        /// <summary>
        /// Export the current view to a path without prompting.
        /// </summary>
        /// <param name="format">"json", "csv", or "md".</param>
        /// <param name="path">Full path.</param>
        public void ExportTo(string format, string path)
        {
            if (Exporting != null) return;
            Exporting = format;
            UpdateExportButtons();
            HistoricalTimelineQuery query = BuildQuery(5000);
            ScreenOps.Run(Context, () => Context.Client.EnumerateHistoryTimelineAsync(query), result =>
            {
                List<HistoricalTimelineEntry> all = result?.Objects ?? new List<HistoricalTimelineEntry>();
                DateTime now = Context.Clock.UtcNow;
                string content = format == "json" ? ActivityExports.Json(query, all, now)
                    : format == "csv" ? ActivityExports.Csv(all)
                    : ActivityExports.Markdown(query, all, now);
                Exporting = null;
                UpdateExportButtons();
                try
                {
                    string written = Context.External.SaveText(path, content);
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Exported {{count}} entries to {{path}}.", LocalizationArgs.Of("count", all.Count, "path", written));
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
                {
                    Context.Notifications.Toast(NotificationSeverityEnum.Error, Context.Loc.T("Failed to export history.") + " " + ex.Message);
                }
            }, "Failed to export history.", ex =>
            {
                Exporting = null;
                UpdateExportButtons();
            });
        }

        /// <summary>
        /// True when a timeline entry maps to a deletable request-history record (source type Request).
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <returns>True when deletable.</returns>
        public static bool CanDeleteEntry(HistoricalTimelineEntry entry)
        {
            return entry != null && !String.IsNullOrEmpty(entry.SourceId) && String.Equals(entry.SourceType, HistoricalTimelineSourceTypes.Request, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Vessel display name (name, else the id, else "-").
        /// </summary>
        /// <param name="id">Vessel id.</param>
        /// <returns>Name.</returns>
        public string VesselName(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "-";
            return _VesselNames.TryGetValue(id, out string? name) && !String.IsNullOrEmpty(name) ? name : id;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void OnActivate(HistoricalTimelineEntry row)
        {
            if (!String.IsNullOrEmpty(row.Route)) Context.Navigate(row.Route!);
            else ShowRowMenu(row);
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(HistoricalTimelineEntry row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            if (!String.IsNullOrEmpty(row.Route)) items.Add(new ActionMenuItem("View", () => Context.Navigate(row.Route!), "Enter"));
            if (!String.IsNullOrEmpty(row.MetadataJson)) items.Add(new ActionMenuItem("View JSON", () => OpenMetadata(row), "j"));
            if (!String.IsNullOrEmpty(row.VesselId)) items.Add(new ActionMenuItem("Open Workspace", () => Context.Navigate("/workspace/" + row.VesselId)));
            if (CanDeleteEntry(row))
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => OnDeleteRow(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <inheritdoc />
        protected override object? JsonFor(HistoricalTimelineEntry row)
        {
            return row;
        }

        /// <inheritdoc />
        protected override string JsonTitle(HistoricalTimelineEntry row)
        {
            return row.SourceType + ": " + row.Title;
        }

        /// <inheritdoc />
        protected override bool SupportsDelete()
        {
            return true;
        }

        /// <inheritdoc />
        protected override bool CanDeleteRows()
        {
            HistoricalTimelineEntry? row = Grid.Current;
            return row != null && CanDeleteEntry(row);
        }

        /// <inheritdoc />
        protected override void OnDeleteRow(HistoricalTimelineEntry row)
        {
            if (!CanDeleteEntry(row)) return;
            string sourceId = row.SourceId;
            Context.Confirm("Delete History Entry", Context.Loc.T("Delete this request-history entry? This cannot be undone."), () =>
            {
                ScreenOps.RunVoid(Context, () => Context.Client.DeleteRequestHistoryEntryAsync(sourceId), () =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Warning, "History entry deleted.");
                    Load();
                }, "Failed to delete history entry.");
            }, "Delete");
        }

        /// <inheritdoc />
        protected override IEnumerable<ArmadaCommand> ExtraCommands()
        {
            return new List<ArmadaCommand>
            {
                Command(ScreenKey + ".apply", "Apply", Load, null, "a"),
                Command(ScreenKey + ".reset", "Reset", ResetFilters, null),
                Command(ScreenKey + ".save-view", "Save View", OpenSaveView, null, "w"),
                Command(ScreenKey + ".saved-views", "Saved Views", OpenSavedViews, () => SavedViews.Views.Count > 0, "v"),
                Command(ScreenKey + ".export-json", "Export JSON", () => Export("json"), () => Exporting == null),
                Command(ScreenKey + ".export-csv", "Export CSV", () => Export("csv"), () => Exporting == null),
                Command(ScreenKey + ".export-md", "Export Markdown", () => Export("md"), () => Exporting == null),
            };
        }

        #endregion

        #region Private-Methods

        private void ConfigureSelect(SelectField<string> field, string allLabel)
        {
            field.ModalHost = Context.Modals;
            field.PickerTitle = allLabel;
            field.Options = new List<SelectOption<string>> { new SelectOption<string>("all", Context.Loc.T(allLabel)) };
            field.SetValue("all");
        }

        private void SetOptions(SelectField<string> field, string allLabel, IEnumerable<SelectOption<string>> options)
        {
            string current = field.Value ?? "all";
            List<SelectOption<string>> list = new List<SelectOption<string>> { new SelectOption<string>("all", Context.Loc.T(allLabel)) };
            list.AddRange(options);
            if (current != "all" && !list.Any(o => o.Value == current)) list.Add(new SelectOption<string>(current, current));
            field.Options = list;
            field.SetValue(current);
        }

        private static void EnsureOption(SelectField<string> field, string? value)
        {
            if (String.IsNullOrEmpty(value) || field.Options.Any(o => o.Value == value)) return;
            List<SelectOption<string>> list = new List<SelectOption<string>>(field.Options);
            list.Add(new SelectOption<string>(value!, value!));
            field.Options = list;
        }

        private static string? Selected(SelectField<string> field)
        {
            string? value = field.Value;
            return String.IsNullOrEmpty(value) || value == "all" ? null : value;
        }

        private void ApplyRouteQuery(IReadOnlyDictionary<string, string> query)
        {
            if (query.TryGetValue("objectiveId", out string? objectiveId) && objectiveId.Length > 0)
            {
                EnsureOption(ObjectiveFilter, objectiveId);
                ObjectiveFilter.SetValue(objectiveId);
            }

            if (query.TryGetValue("vesselId", out string? vesselId) && vesselId.Length > 0)
            {
                EnsureOption(VesselFilter, vesselId);
                VesselFilter.SetValue(vesselId);
            }

            if (query.TryGetValue("sourceType", out string? sourceType) && sourceType.Length > 0)
            {
                EnsureOption(SourceTypeFilter, sourceType);
                SourceTypeFilter.SetValue(sourceType);
            }

            if (query.TryGetValue("postmortemOnly", out string? postmortem)) PostmortemOnly.SetValue(postmortem == "true", false);
            if (query.TryGetValue("text", out string? text)) TextFilter.Value = text;
            if (query.TryGetValue("actor", out string? actor)) ActorFilter.Value = actor;
        }

        private void UpdateKpis()
        {
            int errors = _Entries.Count(e => String.Equals(e.Severity, "error", StringComparison.OrdinalIgnoreCase));
            int warnings = _Entries.Count(e => String.Equals(e.Severity, "warning", StringComparison.OrdinalIgnoreCase));
            int sources = _Entries.Select(e => e.SourceType).Distinct().Count();
            Kpis.SetCards(new List<KpiCard>
            {
                new KpiCard("Visible Entries", Context.Loc.FormatNumber(_Entries.Count)),
                new KpiCard("Errors", Context.Loc.FormatNumber(errors), t => t.Error),
                new KpiCard("Warnings", Context.Loc.FormatNumber(warnings), t => t.Warning),
                new KpiCard("Source Types", Context.Loc.FormatNumber(sources)),
            });
            List<string> counts = new List<string>();
            foreach (IGrouping<string, HistoricalTimelineEntry> group in _Entries.GroupBy(e => e.SourceType))
            {
                counts.Add(group.Key + " (" + group.Count().ToString(CultureInfo.InvariantCulture) + ")");
            }

            _SourceCounts.Text = String.Join("   ", counts);
        }

        private void UpdateSavedViewsLine()
        {
            _SavedViewsLine.Text = SavedViews.Views.Count == 0 ? "" : Context.Loc.T("Saved Views") + ": " + String.Join("   ", SavedViews.Views.Select(v => "[" + v.Name + "]")) + "   (v)";
        }

        private void UpdateExportButtons()
        {
            bool busy = Exporting != null;
            _ExportJson.Enabled = !busy;
            _ExportCsv.Enabled = !busy;
            _ExportMd.Enabled = !busy;
            _ExportJson.Label = Exporting == "json" ? "Exporting..." : "Export JSON";
            _ExportCsv.Label = Exporting == "csv" ? "Exporting..." : "Export CSV";
            _ExportMd.Label = Exporting == "md" ? "Exporting..." : "Export Markdown";
        }

        private void OpenSaveView()
        {
            FormView form = new FormView();
            InputField name = form.AddField("View Name", new InputField());
            name.Placeholder = "Staging failures";
            name.Validator = v => String.IsNullOrWhiteSpace(v) ? "A name is required." : null;
            FormModal modal = new FormModal("Save History View", form, Context, "Save");
            modal.Submit = () =>
            {
                SaveView(name.Value);
                return null;
            };
            Context.Modals.Show(modal);
        }

        private void OpenSavedViews()
        {
            if (SavedViews.Views.Count == 0)
            {
                ScreenOps.Toast(Context, NotificationSeverityEnum.Info, "No saved views.");
                return;
            }

            List<ActionMenuItem> items = new List<ActionMenuItem>();
            foreach (ActivitySavedView view in SavedViews.Views)
            {
                ActivitySavedView captured = view;
                items.Add(new ActionMenuItem(Context.Loc.T("Apply") + ": " + view.Name, () => ApplySavedView(captured)));
            }

            foreach (ActivitySavedView view in SavedViews.Views)
            {
                ActivitySavedView captured = view;
                ActionMenuItem delete = new ActionMenuItem(Context.Loc.T("Delete") + ": " + view.Name, () => DeleteSavedView(captured.Id));
                delete.Destructive = true;
                items.Add(delete);
            }

            ActionMenu.Show(Context.Modals, "Saved Views", items, Context.Loc, Context.Theme.Current);
        }

        private void OpenMetadata(HistoricalTimelineEntry entry)
        {
            if (String.IsNullOrEmpty(entry.MetadataJson)) return;
            ScreenOps.ShowText(Context, entry.SourceType + ": " + entry.Title, entry.MetadataJson!);
        }

        private static string StatusMarker(string? severity)
        {
            string s = (severity ?? "").ToLowerInvariant();
            if (s == "error") return "x ";
            if (s == "warning") return "! ";
            if (s == "success" || s == "passed" || s == "healthy") return "+ ";
            return "";
        }

        private static TUIKit.CellStyle SeverityStyle(string? severity, Armada.Tui.Theming.ArmadaTheme theme)
        {
            string s = (severity ?? "").ToLowerInvariant();
            if (s == "error") return theme.Error;
            if (s == "warning") return theme.Warning;
            if (s == "success" || s == "passed" || s == "healthy") return theme.Success;
            return theme.Text;
        }

        #endregion
    }
}
