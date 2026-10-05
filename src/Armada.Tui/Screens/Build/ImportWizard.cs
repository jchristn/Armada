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
    using Armada.Tui.Input;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Text;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// The vessel import wizard (W4.2, <c>/vessels/import</c>, <c>?batch=ID</c> opens a batch), the dashboard's
    /// ImportWizard: Source (paste folders on the Admiral host or browse its allowed roots, allow worktrees, max depth
    /// 1-16) starts discovery as a background job; Discovering shows that it runs in the background and can be left;
    /// Review lists the candidates with status chips, a text filter, Select all new and Clear selection, the defaults
    /// for the new vessels (fleet, pipeline, landing mode), and the optional captain-driven fleet recommendations
    /// (idle captains only, editable instructions with reset, apply automatically); Results shows progress while the
    /// import runs in the background, the counts, the per-item outcomes with a filter, and the fleet recommendations
    /// (rename, describe, move repositories, add and remove fleets, apply with a confirmation, stop the captain,
    /// retry, edit and apply again); and Import history lists every batch with a status filter (Continue or View).
    /// Leaving a review asks first, like the dashboard. Not thread-safe.
    /// </summary>
    public class ImportWizard : OpsScreen
    {
        #region Public-Members

        /// <summary>
        /// Poll interval for background discovery, import, and categorization, in milliseconds (the dashboard's 2000).
        /// </summary>
        public int PollMilliseconds
        {
            get { return _PollMilliseconds; }
            set { _PollMilliseconds = Math.Clamp(value, 50, 60000); }
        }

        /// <summary>
        /// Current step: <c>source</c>, <c>discovering</c>, <c>review</c>, <c>results</c>, <c>history</c>, or
        /// <c>batch</c>.
        /// </summary>
        public string Step { get; private set; } = "source";

        /// <summary>
        /// Source mode: paste or browse.
        /// </summary>
        public SelectField<string> SourceMode { get; } = new SelectField<string>();

        /// <summary>
        /// Pasted folders, one per line.
        /// </summary>
        public OpsTextArea PasteArea { get; } = new OpsTextArea();

        /// <summary>
        /// The Browse tree.
        /// </summary>
        public ImportBrowseTree Browse { get; } = new ImportBrowseTree();

        /// <summary>
        /// Allow selecting worktrees.
        /// </summary>
        public OpsCheckField AllowWorktrees { get; } = new OpsCheckField("Allow selecting worktrees", false);

        /// <summary>
        /// Max depth (1-16, empty for the server default).
        /// </summary>
        public InputField MaxDepth { get; } = new InputField();

        /// <summary>
        /// The batch being reviewed or shown.
        /// </summary>
        public VesselImportBatch? Batch { get; private set; } = null;

        /// <summary>
        /// Discovered candidates.
        /// </summary>
        public List<VesselImportItem> Candidates { get; private set; } = new List<VesselImportItem>();

        /// <summary>
        /// Discovery hints.
        /// </summary>
        public List<VesselImportHint> DiscoveryHints { get; private set; } = new List<VesselImportHint>();

        /// <summary>
        /// Candidate table (marks are the selection).
        /// </summary>
        public ArmadaGrid<VesselImportItem> CandidateGrid { get; } = new ArmadaGrid<VesselImportItem>(i => i.Path);

        /// <summary>
        /// Candidate text filter.
        /// </summary>
        public TextInput CandidateSearch { get; } = new TextInput();

        /// <summary>
        /// Review options (defaults and fleet recommendations).
        /// </summary>
        public FormView Options { get; } = new FormView();

        /// <summary>
        /// Default fleet.
        /// </summary>
        public SelectField<string> DefaultFleet { get; } = new SelectField<string>();

        /// <summary>
        /// Default pipeline.
        /// </summary>
        public SelectField<string> DefaultPipeline { get; } = new SelectField<string>();

        /// <summary>
        /// Default landing mode.
        /// </summary>
        public SelectField<string> DefaultLanding { get; } = new SelectField<string>();

        /// <summary>
        /// Recommend fleets with a captain.
        /// </summary>
        public OpsCheckField Categorize { get; } = new OpsCheckField("Recommend fleets with a captain", false);

        /// <summary>
        /// Captain that recommends fleets.
        /// </summary>
        public SelectField<string> CategorizeCaptain { get; } = new SelectField<string>();

        /// <summary>
        /// Instructions for the captain.
        /// </summary>
        public OpsTextArea CategorizePrompt { get; } = new OpsTextArea();

        /// <summary>
        /// Apply recommendations automatically.
        /// </summary>
        public OpsCheckField ApplyAutomatically { get; } = new OpsCheckField("Apply recommendations automatically", false);

        /// <summary>
        /// Result items.
        /// </summary>
        public List<VesselImportItem> ResultItems { get; private set; } = new List<VesselImportItem>();

        /// <summary>
        /// Result table.
        /// </summary>
        public ArmadaGrid<VesselImportItem> ResultGrid { get; } = new ArmadaGrid<VesselImportItem>(i => String.IsNullOrEmpty(i.Id) ? i.Path : i.Id);

        /// <summary>
        /// Fleet recommendations.
        /// </summary>
        public ImportFleetRecommendationsView Fleets { get; } = new ImportFleetRecommendationsView();

        /// <summary>
        /// Import history table.
        /// </summary>
        public ArmadaGrid<VesselImportBatch> HistoryGrid { get; } = new ArmadaGrid<VesselImportBatch>(b => b.Id);

        /// <summary>
        /// History status filter.
        /// </summary>
        public SelectField<string> HistoryStatus { get; } = new SelectField<string>();

        /// <summary>
        /// The visible panel on the Review step (<c>candidates</c> or <c>options</c>) and the Results step
        /// (<c>items</c> or <c>fleets</c>).
        /// </summary>
        public string Panel { get; private set; } = "candidates";

        /// <summary>
        /// Candidate status filter, or null for all.
        /// </summary>
        public VesselImportCandidateStatusEnum? CandidateFilter { get; private set; } = null;

        /// <summary>
        /// Outcome filter, or null for all.
        /// </summary>
        public VesselImportOutcomeEnum? OutcomeFilter { get; private set; } = null;

        /// <summary>
        /// The current error message, or null.
        /// </summary>
        public string? Error { get; private set; } = null;

        /// <summary>
        /// True while polling a background batch.
        /// </summary>
        public bool Polling { get; private set; } = false;

        /// <inheritdoc />
        public override bool CanFocus { get; set; } = true;

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> TypingHints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                foreach (OpsScreenAction a in _Actions.Where(a => a.Key != null && a.Available && KeyGesture.WorksWhileTyping(a.Key)).Take(5))
                    hints.Add(new KeyValuePair<string, string>(KeyLabel(a.Key!), a.Label));
                return hints;
            }
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>();
                foreach (OpsScreenAction a in _Actions.Where(a => a.Key != null && a.Available).Take(5))
                    hints.Add(new KeyValuePair<string, string>(KeyLabel(a.Key!), a.Label));
                return hints;
            }
        }

        #endregion

        #region Private-Members

        private readonly List<OpsScreenAction> _Actions = new List<OpsScreenAction>();
        private List<Fleet> _Fleets = new List<Fleet>();
        private List<Pipeline> _Pipelines = new List<Pipeline>();
        private List<Captain> _Captains = new List<Captain>();
        private bool _CaptainsRequested = false;
        private string _DefaultPrompt = "";
        private string? _DefaultPromptError = null;
        private int? _TimeoutMinutes = null;
        private bool _Discovering = false;
        private bool _Importing = false;
        private bool _FleetBusy = false;
        private string? _ImportErrorCode = null;
        private string? _PollError = null;
        private string? _LastStatus = null;
        private string? _JobId = null;
        private int _SelectedCount = 0;
        private bool _Truncated = false;
        private bool _ShowCategorizationErrors = false;
        private Timer? _PollTimer = null;
        private string? _PollingBatchId = null;
        private string _RecommendationKey = "";
        private int _PollMilliseconds = 2000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public ImportWizard(RouteMatch route, TuiContext context)
            : base(route, context, "ImportWizard", "Import repositories")
        {
            SourceMode.ModalHost = context.Modals;
            SourceMode.PickerTitle = "Source";
            SourceMode.Options = new List<SelectOption<string>> { new SelectOption<string>("paste", Tr("Paste paths")), new SelectOption<string>("browse", Tr("Browse")) };
            SourceMode.SetValue("paste");
            SourceMode.ValueChanged += (s, e) =>
            {
                if (SourceMode.Value == "browse" && Browse.Roots == null) LoadRoots();
                ShowStep(Step);
            };
            PasteArea.Placeholder = "/Users/alex/Code\n/Users/alex/Code/api";
            PasteArea.ExternalEditor = (text, done) => EditExternally(text, done, ".txt");
            Browse.LoadFolder = LoadFolder;
            AllowWorktrees.Changed += (s, e) => Browse.AllowWorktrees = AllowWorktrees.Checked;
            MaxDepth.Placeholder = "Server default";
            MaxDepth.Validator = v => DepthError(v);

            CandidateGrid.ModalHost = context.Modals;
            CandidateGrid.PageSize = 50;
            CandidateGrid.EmptyText = "No candidates match the current filters.";
            CandidateGrid.AddColumn(new GridColumn<VesselImportItem>("name", "Name", i => i.ProposedName) { Weight = 2 });
            GridColumn<VesselImportItem> cstatus = new GridColumn<VesselImportItem>("status", "Status", i => Tr(ImportText.CandidateLabel(i.CandidateStatus)) + (i.CandidateStatus == VesselImportCandidateStatusEnum.AlreadyOnboarded && !String.IsNullOrEmpty(i.ExistingVesselId) ? " (" + i.ExistingVesselId + ")" : "")) { Width = 22 };
            cstatus.Style = (i, t) => ImportText.CandidateStyle(i.CandidateStatus, t);
            CandidateGrid.AddColumn(cstatus);
            CandidateGrid.AddColumn(new GridColumn<VesselImportItem>("path", "Path", i => i.Path) { Weight = 4 });
            CandidateGrid.AddColumn(new GridColumn<VesselImportItem>("remote", "Remote", i => String.IsNullOrEmpty(i.RemoteUrl) ? Tr("(no origin)") : i.RemoteUrl!) { Weight = 3 });
            CandidateGrid.AddColumn(new GridColumn<VesselImportItem>("branch", "Branch", i => String.IsNullOrEmpty(i.DefaultBranch) ? "-" : i.DefaultBranch!) { Width = 12 });
            CandidateGrid.Activated += (s, i) =>
            {
                if (i.CandidateStatus == VesselImportCandidateStatusEnum.AlreadyOnboarded && !String.IsNullOrEmpty(i.ExistingVesselId)) Context.Navigate("/vessels/" + Uri.EscapeDataString(i.ExistingVesselId!));
                else ToggleCandidate(i);
            };
            CandidateSearch.Placeholder = "Path or name contains...";
            CandidateSearch.ValueChanged += (s, e) => ApplyCandidateFilter();
            CandidateSearch.Submitted += (s, e) => Scope.Focus(CandidateGrid);

            Options.ShowButtons = false;
            DefaultFleet.ModalHost = context.Modals;
            DefaultFleet.PickerTitle = "Fleet";
            DefaultPipeline.ModalHost = context.Modals;
            DefaultPipeline.PickerTitle = "Default Pipeline";
            DefaultLanding.ModalHost = context.Modals;
            DefaultLanding.PickerTitle = "Landing Mode";
            DefaultLanding.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("", Tr("Default (use global setting)")),
                new SelectOption<string>("LocalMerge", Tr("Local Merge")),
                new SelectOption<string>("PullRequest", Tr("Pull Request")),
                new SelectOption<string>("MergeQueue", Tr("Merge Queue")),
                new SelectOption<string>("None", Tr("None")),
            };
            DefaultLanding.SetValue("");
            CategorizeCaptain.ModalHost = context.Modals;
            CategorizeCaptain.PickerTitle = "Captain that recommends fleets";
            CategorizeCaptain.Placeholder = "Select a captain";
            CategorizePrompt.ExternalEditor = (text, done) => EditExternally(text, done);
            CategorizeCaptain.ValueChanged += (s, e) => SyncCategorizationVisibility();
            Button resetPrompt = new Button("Reset to default", () => { if (_DefaultPrompt.Length > 0) CategorizePrompt.Text = _DefaultPrompt; });
            Categorize.Changed += (s, e) =>
            {
                if (Categorize.Checked && CategorizePrompt.Text.Length == 0 && _DefaultPrompt.Length > 0) CategorizePrompt.Text = _DefaultPrompt;
                if (Categorize.Checked) LoadCategorizationData();
                SyncCategorizationVisibility();
            };
            ApplyAutomatically.Changed += (s, e) => SyncCategorizationVisibility();
            foreach (ArmadaWidget w in new ArmadaWidget[] { DefaultFleet, DefaultPipeline, DefaultLanding, Categorize, CategorizeCaptain, CategorizePrompt, resetPrompt, ApplyAutomatically, Options })
            {
                w.Localizer = Localizer;
                w.ApplyTheme(Theme);
            }

            Options.AddSection("Defaults for the new vessels");
            Options.AddField("Fleet", DefaultFleet);
            Options.AddField("Default Pipeline", DefaultPipeline);
            Options.AddField("Landing Mode", DefaultLanding, "Imported vessels use the repository origin as the remote and the discovered folder as the working directory. The checkout is never deleted when a vessel is removed.");
            Options.AddSection("Fleet recommendations");
            Options.AddField("", Categorize, "After the vessels are created, a captain reads every imported repository, works out what each one does, and suggests a set of fleets. It runs in the background; you can review and edit the fleets before applying them.");
            Options.AddField("Captain", CategorizeCaptain);
            Options.AddField("Instructions for the captain", CategorizePrompt, null, 6);
            Options.AddField(" ", resetPrompt);
            Options.AddField("  ", ApplyAutomatically);
            SyncCategorizationVisibility();

            ResultGrid.ModalHost = context.Modals;
            ResultGrid.MultiSelect = false;
            ResultGrid.PageSize = 50;
            ResultGrid.EmptyText = "No items match the current filter.";
            ResultGrid.AddColumn(new GridColumn<VesselImportItem>("name", "Name", i => i.ProposedName) { Weight = 2 });
            GridColumn<VesselImportItem> outcome = new GridColumn<VesselImportItem>("outcome", "Outcome", i => Tr(ImportText.OutcomeLabel(i.Outcome))) { Width = 18 };
            outcome.Style = (i, t) => ImportText.OutcomeStyle(i.Outcome, t);
            ResultGrid.AddColumn(outcome);
            ResultGrid.AddColumn(new GridColumn<VesselImportItem>("reason", "Reason", i =>
            {
                string reason = Tr(ImportText.ReasonLabel(i.OutcomeReason));
                if (!String.IsNullOrEmpty(i.OutcomeMessage)) reason = (reason.Length > 0 ? reason + ": " : "") + i.OutcomeMessage;
                return reason.Length > 0 ? reason : "-";
            }) { Weight = 3 });
            ResultGrid.AddColumn(new GridColumn<VesselImportItem>("path", "Path", i => i.Path) { Weight = 3 });
            ResultGrid.AddColumn(new GridColumn<VesselImportItem>("vessel", "Vessel", i => i.VesselId ?? i.ExistingVesselId ?? "-") { Width = 26 });
            ResultGrid.Activated += (s, i) =>
            {
                string? id = i.VesselId ?? i.ExistingVesselId;
                if (!String.IsNullOrEmpty(id)) Context.Navigate("/vessels/" + Uri.EscapeDataString(id!));
            };

            HistoryGrid.ModalHost = context.Modals;
            HistoryGrid.MultiSelect = false;
            HistoryGrid.Dispatcher = context.Dispatcher;
            HistoryGrid.EmptyText = "No imports yet";
            HistoryGrid.PageSizes = new List<int> { 10, 25, 50, 100 };
            HistoryGrid.AddColumn(new GridColumn<VesselImportBatch>("created", "Created", b => Context.Loc.FormatRelative(b.CreatedUtc, Context.Clock.UtcNow) + "  " + b.Id) { Weight = 3 });
            GridColumn<VesselImportBatch> bstatus = new GridColumn<VesselImportBatch>("status", "Status", b => Tr(ImportText.BatchLabel(b.Status)) + (b.CategorizationStatus != VesselImportCategorizationStatusEnum.None ? "; " + Tr(ImportText.CategorizationLabel(b.CategorizationStatus)) : "")) { Weight = 3 };
            bstatus.Style = (b, t) => ImportText.BatchStyle(b.Status, t);
            HistoryGrid.AddColumn(bstatus);
            HistoryGrid.AddColumn(new GridColumn<VesselImportBatch>("paths", "Paths", b => b.RequestedPathCount.ToString(CultureInfo.InvariantCulture)) { Width = 7, Align = CellAlignment.Right });
            HistoryGrid.AddColumn(new GridColumn<VesselImportBatch>("candidates", "Candidates", b => b.CandidateCount.ToString(CultureInfo.InvariantCulture)) { Width = 11, Align = CellAlignment.Right });
            HistoryGrid.AddColumn(new GridColumn<VesselImportBatch>("createdCount", "Created", b => b.CreatedCount.ToString(CultureInfo.InvariantCulture)) { Width = 8, Align = CellAlignment.Right });
            HistoryGrid.AddColumn(new GridColumn<VesselImportBatch>("skipped", "Skipped", b => b.SkippedCount.ToString(CultureInfo.InvariantCulture)) { Width = 8, Align = CellAlignment.Right });
            HistoryGrid.AddColumn(new GridColumn<VesselImportBatch>("failed", "Failed", b => b.FailedCount.ToString(CultureInfo.InvariantCulture)) { Width = 7, Align = CellAlignment.Right });
            HistoryGrid.AddColumn(new GridColumn<VesselImportBatch>("action", "Actions", b => Tr(b.Status == VesselImportBatchStatusEnum.Discovered || b.Status == VesselImportBatchStatusEnum.Discovering || b.CategorizationStatus == VesselImportCategorizationStatusEnum.Completed ? "Continue" : "View")) { Width = 10 });
            HistoryGrid.Loader = LoadHistoryAsync;
            HistoryGrid.Activated += (s, b) => OpenBatch(b.Id);
            HistoryStatus.ModalHost = context.Modals;
            HistoryStatus.PickerTitle = "Filter batches by status";
            List<SelectOption<string>> statuses = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All statuses")) };
            statuses.AddRange(ImportText.BatchStatuses.Select(s => new SelectOption<string>(s.ToString(), Tr(ImportText.BatchLabel(s)))));
            HistoryStatus.Options = statuses;
            HistoryStatus.SetValue("");
            HistoryStatus.ValueChanged += (s, e) =>
            {
                if (HistoryGrid.PageNumber != 1) HistoryGrid.GoToPage(1);
                else HistoryGrid.Reload();
            };

            foreach (ArmadaWidget w in new ArmadaWidget[] { SourceMode, PasteArea, Browse, AllowWorktrees, MaxDepth, CandidateGrid, CandidateSearch, ResultGrid, Fleets, HistoryGrid, HistoryStatus })
            {
                w.Localizer = Localizer;
                w.ApplyTheme(Theme);
            }

            Act("discover", "Discover", Discover, "ctrl+s", () => Step == "source" && !_Discovering && SourcePaths().Count > 0 && DepthError(MaxDepth.Value) == null);
            Act("import", "Import", RunImport, "ctrl+s", () => Step == "review" && !_Importing && CandidateGrid.Marked.Count > 0);
            Act("history", "Import history", () => ShowHistory(), "h", () => Step == "source" || Step == "results" || Step == "discovering");
            Act("start-over", "Start over", ResetAll, "s", () => Step == "discovering");
            Act("back", "Back", () => ShowStep("source"), "b", () => Step == "review" && !_Importing);
            Act("back-history", "Back to history", () => ShowHistory(), "b", () => Step == "batch");
            Act("import-more", "Import more", ResetAll, "n", () => Step == "results" || Step == "history" || Step == "batch");
            Act("view-vessels", "View vessels", () => Context.Navigate("/vessels"), "v", () => Step == "results");
            Act("select-new", "Select all new", SelectAllNew, "a", () => Step == "review" && Panel == "candidates");
            Act("clear-selection", "Clear selection", () => CandidateGrid.ClearMarks(), "C", () => Step == "review" && CandidateGrid.Marked.Count > 0);
            Act("candidate-filter", "Filter by status", CycleCandidateFilter, "f", () => Step == "review" && Panel == "candidates");
            Act("outcome-filter", "Filter by outcome", CycleOutcomeFilter, "f", () => (Step == "results" || Step == "batch") && Panel == "items");
            Act("search", "Path or name contains...", () => Scope.Focus(CandidateSearch), "/", () => Step == "review" && Panel == "candidates");
            Act("panel-next", "Next panel", () => SwitchPanel(1), "]", () => Step == "review" || ((Step == "results" || Step == "batch") && ShowFleets()));
            Act("panel-previous", "Previous panel", () => SwitchPanel(-1), "[", () => Step == "review" || ((Step == "results" || Step == "batch") && ShowFleets()));
            Act("rename-fleet", "Rename fleet", RenameFleet, "r", () => FleetEditing() && Fleets.Current != null);
            Act("describe-fleet", "Edit description", DescribeFleet, "e", () => FleetEditing() && Fleets.Current != null);
            Act("move-vessel", "Move to another fleet", MoveVessel, "m", () => FleetEditing() && Fleets.Current?.VesselId != null && Fleets.Drafts.Count > 1);
            Act("add-fleet", "+ Add fleet", AddFleet, "a", () => FleetEditing());
            Act("remove-fleet", "Remove fleet", RemoveFleet, "del", () => FleetEditing() && Fleets.Current != null && Fleets.Current.Kind == "fleet" && Fleets.Current.Draft.VesselIds.Count == 0);
            Act("apply-fleets", "Apply fleets", RequestApply, "A", () => FleetEditing() && !_FleetBusy);
            Act("stop-captain", "Stop the captain", StopCaptain, "x", () => FleetPanel() && Batch != null && Batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Running && !String.IsNullOrEmpty(Batch.CategorizationJobId) && !_FleetBusy);
            Act("retry-categorization", "Retry categorization", RetryCategorization, "R", () => FleetPanel() && Batch != null && Batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Failed && !_FleetBusy);
            Act("edit-again", "Edit and apply again", () => Fleets.Editing = true, "E", () => FleetPanel() && Batch != null && Batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Applied && !Fleets.Editing);
            Act("jobs", "Open the Jobs page", () => Context.Navigate("/jobs"), "J", () => (Step == "results" || Step == "batch") && (_JobId != null || !String.IsNullOrEmpty(Batch?.CategorizationJobId)));
            Act("close", "Close", RequestClose, "esc");

            Track(new OpsCallbackDisposable(StopPolling));
            Call(async (c, t) =>
            {
                Task<EnumerationResult<Fleet>?> fleets = c.ListFleetsAsync(new ArmadaPageQuery(1, 9999), t);
                Task<EnumerationResult<Pipeline>?> pipelines = c.ListPipelinesAsync(new ArmadaPageQuery(1, 9999), t);
                await Task.WhenAll(fleets, pipelines).ConfigureAwait(false);
                return new KeyValuePair<List<Fleet>, List<Pipeline>>(fleets.Result?.Objects ?? new List<Fleet>(), pipelines.Result?.Objects ?? new List<Pipeline>());
            }, r =>
            {
                _Fleets = r.Key;
                _Pipelines = r.Value;
                string? fleet = DefaultFleet.Value;
                List<SelectOption<string>> fleetOptions = new List<SelectOption<string>> { new SelectOption<string>("", Tr("No fleet")) };
                fleetOptions.AddRange(_Fleets.Select(f => new SelectOption<string>(f.Id, f.Name)));
                DefaultFleet.Options = fleetOptions;
                DefaultFleet.SetValue(fleet ?? "");
                List<SelectOption<string>> pipelineOptions = new List<SelectOption<string>> { new SelectOption<string>("", Tr("None (WorkerOnly)")) };
                pipelineOptions.AddRange(_Pipelines.Select(p => new SelectOption<string>(p.Id, p.Name)));
                DefaultPipeline.Options = pipelineOptions;
                DefaultPipeline.SetValue(DefaultPipeline.Value ?? "");
            }, null, ex => { });

            string? batchId = OpsHandoff.Get(route, "batch");
            if (batchId != null) OpenBatch(batchId);
            else ShowStep("source");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The folders to discover from the current source mode.
        /// </summary>
        /// <returns>Paths.</returns>
        public List<string> SourcePaths()
        {
            return SourceMode.Value == "browse" ? Browse.Selected.ToList() : ImportText.ParsePaths(PasteArea.Text);
        }

        /// <summary>
        /// Start discovery as a background job.
        /// </summary>
        public void Discover()
        {
            List<string> paths = SourcePaths();
            if (paths.Count == 0 || DepthError(MaxDepth.Value) != null || _Discovering) return;
            VesselDiscoveryRequest request = new VesselDiscoveryRequest();
            request.Directories = paths;
            if (Int32.TryParse(MaxDepth.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int depth)) request.MaxDepth = depth;
            request.RunInBackground = true;
            _Discovering = true;
            Error = null;
            Call((c, t) => c.DiscoverVesselImportAsync(request, t), result =>
            {
                _Discovering = false;
                if (result == null) return;
                if (result.RunsInBackground)
                {
                    Batch = result.Batch;
                    ShowStep("discovering");
                    StartPolling(result.BatchId, "Discovering");
                }
                else if (result.Batch != null)
                {
                    LoadReview(result.Batch, result.Candidates ?? new List<VesselImportItem>(), result.Truncated, result.Hints ?? new List<VesselImportHint>());
                }
            }, null, ex =>
            {
                _Discovering = false;
                Error = Describe(ex, Tr("Discovery failed."));
            });
        }

        /// <summary>
        /// Import the selected candidates.
        /// </summary>
        public void RunImport()
        {
            if (Batch == null || CandidateGrid.Marked.Count == 0 || _Importing) return;
            if (CategorizationError() != null)
            {
                _ShowCategorizationErrors = true;
                SyncCategorizationVisibility();
                Panel = "options";
                ShowStep("review");
                return;
            }

            VesselImportRequest request = new VesselImportRequest();
            request.BatchId = Batch.Id;
            request.Paths = CandidateGrid.Marked.ToList();
            request.FleetId = String.IsNullOrEmpty(DefaultFleet.Value) ? null : DefaultFleet.Value;
            if (!String.IsNullOrEmpty(DefaultPipeline.Value) || !String.IsNullOrEmpty(DefaultLanding.Value))
            {
                VesselImportDefaults defaults = new VesselImportDefaults();
                defaults.DefaultPipelineId = String.IsNullOrEmpty(DefaultPipeline.Value) ? null : DefaultPipeline.Value;
                defaults.LandingMode = Enum.TryParse<LandingModeEnum>(DefaultLanding.Value ?? "", out LandingModeEnum lm) ? lm : (LandingModeEnum?)null;
                request.Defaults = defaults;
            }

            if (Categorize.Checked)
            {
                VesselImportCategorizationRequest cat = new VesselImportCategorizationRequest();
                cat.Enabled = true;
                cat.CaptainId = CategorizeCaptain.Value;
                cat.Prompt = CategorizePrompt.Text == _DefaultPrompt ? null : CategorizePrompt.Text;
                cat.ApplyAutomatically = ApplyAutomatically.Checked;
                request.Categorization = cat;
            }

            int selectedCount = request.Paths.Count;
            _Importing = true;
            Error = null;
            _ImportErrorCode = null;
            Call((c, t) => c.ImportVesselsAsync(request, t), response =>
            {
                _Importing = false;
                if (response == null) return;
                _SelectedCount = selectedCount;
                Batch = response.Batch;
                ResultItems = response.Items ?? new List<VesselImportItem>();
                OutcomeFilter = null;
                ApplyOutcomeFilter();
                Fleets.Load(new List<VesselImportFleetRecommendation>());
                _JobId = response.JobId;
                ShowStep("results");
                if (response.RunsInBackground)
                {
                    StartPolling(response.BatchId, "Importing");
                }
                else if (response.Batch != null)
                {
                    ToastImportFinished(response.Batch);
                    if (ImportText.IsBusy(response.Batch)) StartPolling(response.BatchId, response.Batch.Status.ToString());
                }
            }, null, ex =>
            {
                _Importing = false;
                _ImportErrorCode = (ex as ArmadaApiException)?.Code;
                Error = Describe(ex, Tr("Import failed."));
            });
        }

        /// <summary>
        /// Open a batch from the history (or a deep link): discovering batches poll, discovered ones open the
        /// review, and the rest open their results.
        /// </summary>
        /// <param name="batchId">Batch id.</param>
        public void OpenBatch(string batchId)
        {
            StopPolling();
            ShowStep("batch");
            Error = null;
            Call((c, t) => c.GetVesselImportBatchAsync(batchId, t), detail =>
            {
                if (detail?.Batch == null)
                {
                    Error = Tr("Failed to load the batch.");
                    return;
                }

                _LastStatus = detail.Batch.Status.ToString();
                if (detail.Batch.Status == VesselImportBatchStatusEnum.Discovering)
                {
                    Batch = detail.Batch;
                    ShowStep("discovering");
                    StartPolling(detail.Batch.Id, "Discovering");
                }
                else if (detail.Batch.Status == VesselImportBatchStatusEnum.Discovered)
                {
                    LoadReview(detail.Batch, detail.Items ?? new List<VesselImportItem>(), detail.Batch.Truncated, detail.Hints ?? new List<VesselImportHint>());
                }
                else
                {
                    ApplyBatchDetail(detail);
                    _JobId = detail.Batch.JobId;
                    _SelectedCount = ResultItems.Count(i => i.Selected || (i.Outcome != VesselImportOutcomeEnum.SkippedNotSelected && i.Outcome != VesselImportOutcomeEnum.Pending));
                    if (detail.Batch.Status == VesselImportBatchStatusEnum.Importing) ShowStep("results");
                    else ShowStep("batch");
                    if (ImportText.IsBusy(detail.Batch)) StartPolling(detail.Batch.Id, detail.Batch.Status.ToString());
                }
            }, null, ex => Error = Describe(ex, Tr("Failed to load the batch.")));
        }

        /// <summary>
        /// Poll a batch once (also run by the poll timer).
        /// </summary>
        /// <param name="batchId">Batch id.</param>
        public void Poll(string batchId)
        {
            Call((c, t) => c.GetVesselImportBatchAsync(batchId, t), detail =>
            {
                if (detail?.Batch == null) return;
                string? previous = _LastStatus;
                _LastStatus = detail.Batch.Status.ToString();
                _PollError = null;
                if (detail.Batch.Status == VesselImportBatchStatusEnum.Discovering)
                {
                    Batch = detail.Batch;
                    return;
                }

                if (previous == "Discovering")
                {
                    StopPolling();
                    if (detail.Batch.Status == VesselImportBatchStatusEnum.Discovered)
                    {
                        LoadReview(detail.Batch, detail.Items ?? new List<VesselImportItem>(), detail.Batch.Truncated, detail.Hints ?? new List<VesselImportHint>());
                    }
                    else
                    {
                        Error = String.IsNullOrEmpty(detail.Batch.ErrorMessage) ? Tr("Discovery failed.") : detail.Batch.ErrorMessage;
                        ShowStep("source");
                    }

                    return;
                }

                ApplyBatchDetail(detail);
                if (previous == "Importing" && detail.Batch.Status != VesselImportBatchStatusEnum.Importing) ToastImportFinished(detail.Batch);
                if (!ImportText.IsBusy(detail.Batch)) StopPolling();
            }, null, ex => _PollError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to refresh import progress.") : ex.Message);
        }

        /// <inheritdoc />
        public override Action? RefreshAction()
        {
            return () =>
            {
                if (Step == "history") HistoryGrid.Reload();
                else if (Batch != null && (Step == "results" || Step == "batch" || Step == "discovering")) Poll(Batch.Id);
            };
        }

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            return 0;
        }

        /// <inheritdoc />
        public override IEnumerable<ArmadaCommand> Commands()
        {
            List<ArmadaCommand> list = new List<ArmadaCommand>();
            foreach (OpsScreenAction a in _Actions)
            {
                OpsScreenAction action = a;
                ArmadaCommand c = a.Key != null && a.Key != "esc"
                    ? new ArmadaCommand(ScreenKey + "." + a.Id, a.Label, CommandMenuEnum.Actions, () => { if (action.Available) action.Run(); }, a.Key)
                    : new ArmadaCommand(ScreenKey + "." + a.Id, a.Label, CommandMenuEnum.Actions, () => { if (action.Available) action.Run(); });
                c.Group = Title;
                c.Dispatch = false;
                c.IsEnabled = () => action.Available;
                list.Add(c);
            }

            return list;
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            IWidget? leaf = Scope.FocusedLeaf() ?? Scope.Focused;
            IWidget? optionLeaf = ReferenceEquals(Scope.Focused, Options) ? (Options.Scope.FocusedLeaf() ?? Options.Scope.Focused) : null;
            bool typing = leaf is TextInput || leaf is OpsTextArea || optionLeaf is TextInput || optionLeaf is OpsTextArea;
            bool ctrl = (key.Modifiers & KeyModifiers.Ctrl) != 0;
            if (typing && key.Code == KeyCode.Escape)
            {
                LeaveTextField();
                return true;
            }

            if (Step == "review" && ReferenceEquals(Scope.Focused, CandidateGrid) && !typing)
            {
                if (key.Code == KeyCode.Character && key.Rune == ' ' && key.Modifiers == KeyModifiers.None)
                {
                    if (CandidateGrid.Current != null) ToggleCandidate(CandidateGrid.Current);
                    return true;
                }

                if (ctrl && key.Code == KeyCode.Character && Char.ToLowerInvariant((char)key.Rune) == 'a')
                {
                    ToggleAllFiltered();
                    return true;
                }
            }

            foreach (OpsScreenAction a in _Actions)
            {
                if (a.Key == null) continue;
                bool chord = a.Key.StartsWith("ctrl+", StringComparison.Ordinal);
                if (!chord && typing) continue;
                if (Matches(a.Key, key) && a.Available)
                {
                    a.Run();
                    return true;
                }
            }

            if (key.Code == KeyCode.Tab)
            {
                bool forward = (key.Modifiers & KeyModifiers.Shift) == 0;
                if (ReferenceEquals(Scope.Focused, Options) && Options.Scope.Move(forward)) return true;
                Scope.Move(forward);
                if (ReferenceEquals(Scope.Focused, Options))
                {
                    if (forward) Options.Scope.FocusFirst();
                    else Options.Scope.FocusLast();
                }

                return true;
            }

            if (Scope.HandleKey(key)) return true;
            return false;
        }

        /// <inheritdoc />
        public override bool HandlePaste(string text)
        {
            if (ReferenceEquals(Scope.Focused, PasteArea)) return PasteArea.HandlePaste(text);
            if (ReferenceEquals(Scope.Focused, Options)) return Options.HandlePaste(text);
            return false;
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            SurfaceText.FillRect(surface, new Rect(0, 0, width, height), Theme.Text);
            if (width < 20 || height < 8) return;
            int y = 0;
            int x = SurfaceText.Draw(surface, 0, y, Tr("Import repositories"), Theme.Accent.WithAttribute(CellAttributes.Bold, true), width);
            SurfaceText.Draw(surface, x + 2, y, Tr("Onboard existing local git repositories as vessels in bulk. Nothing is created until you confirm."), Theme.Muted, Math.Max(0, width - x - 2));
            y++;
            int stepIndex = Step == "source" || Step == "discovering" ? 0 : Step == "review" ? 1 : Step == "results" ? 2 : -1;
            if (stepIndex >= 0)
            {
                string[] steps = new string[] { "Source", "Review", "Results" };
                int sx = 0;
                for (int i = 0; i < steps.Length; i++)
                {
                    string label = (i + 1) + " " + Tr(steps[i]);
                    string text = i == stepIndex ? "[" + label + "]" : (i < stepIndex ? " " + label + " (" + Tr("Done") + ")" : " " + label + " ");
                    sx += SurfaceText.Draw(surface, sx, y, text, i == stepIndex ? Theme.Accent : Theme.Muted, width - sx);
                    if (i < steps.Length - 1) sx += SurfaceText.Draw(surface, sx, y, " > ", Theme.Muted, width - sx);
                }
            }
            else
            {
                SurfaceText.Draw(surface, 0, y, Tr("Import history"), Theme.Accent, width);
            }

            y++;
            string keys = KeysLine();
            if (keys.Length > 0) SurfaceText.Draw(surface, 0, y++, keys, Theme.Muted, width);
            if (Error != null)
            {
                string retry = Step == "review" ? (_ImportErrorCode == "BatchBusy" ? "  (h " + Tr("Open import history") + ")" : _ImportErrorCode == "BatchNotFound" ? "  (b " + Tr("Discover again") + ")" : "  (Ctrl+S " + Tr("Retry") + ")") : Step == "source" ? "  (Ctrl+S " + Tr("Retry") + ")" : "";
                foreach (string line in TextCells.Wrap("! " + Error + retry, width))
                {
                    if (y >= height) break;
                    SurfaceText.Draw(surface, 0, y++, line, Theme.Error, width);
                }
            }

            // A blank row, not a rule: the boxes of the fields below use it as their top line (see RegionFrames).
            y++;
            Rect body = new Rect(0, y, width, Math.Max(1, height - y));
            switch (Step)
            {
                case "source":
                    RenderSource(surface, body);
                    break;
                case "discovering":
                    RenderDiscovering(surface, body);
                    break;
                case "review":
                    RenderReview(surface, body);
                    break;
                case "history":
                    RenderHistory(surface, body);
                    break;
                default:
                    RenderResults(surface, body);
                    break;
            }
        }

        #endregion

        #region Private-Methods

        private static bool Matches(string keyText, KeyEvent key)
        {
            if (keyText == "esc") return key.Code == KeyCode.Escape;
            try { return KeyStroke.Parse(keyText).Matches(key); }
            catch (FormatException) { return false; }
        }

        private static string KeyLabel(string key)
        {
            if (key == "esc") return "Esc";
            try { return KeyStroke.Parse(key).ToLabel(); }
            catch (FormatException) { return key; }
        }

        private static string? DepthError(string? value)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0) return null;
            return Int32.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 16 ? null : "Max depth must be a whole number from 1 to 16.";
        }

        private void Act(string id, string label, Action run, string? key, Func<bool>? when = null)
        {
            _Actions.Add(new OpsScreenAction(id, label, run, key, when));
        }

        private string KeysLine()
        {
            List<string> parts = new List<string>();
            foreach (OpsScreenAction a in _Actions.Where(a => a.Key != null && a.Available && a.Id != "panel-previous"))
            {
                parts.Add(KeyLabel(a.Key!) + " " + Tr(a.Label));
                if (parts.Count >= 6) break;
            }

            return String.Join("  ", parts);
        }

        private string Describe(Exception ex, string fallback)
        {
            if (ex is ArmadaApiException api)
            {
                string? label = ImportText.ErrorLabel(api.Code);
                if (label != null) return Tr(label) + (String.IsNullOrEmpty(api.Message) ? "" : " (" + api.Message + ")");
                return String.IsNullOrEmpty(api.Message) ? fallback : api.Message;
            }

            return String.IsNullOrEmpty(ex.Message) ? fallback : ex.Message;
        }

        private bool ShowFleets()
        {
            return Batch != null && Batch.CategorizationStatus != VesselImportCategorizationStatusEnum.None;
        }

        private bool FleetPanel()
        {
            return (Step == "results" || Step == "batch") && Panel == "fleets" && ShowFleets();
        }

        private bool FleetEditing()
        {
            return FleetPanel() && Fleets.Editing && ReferenceEquals(Scope.Focused, Fleets);
        }

        private void ShowStep(string step)
        {
            Step = step;
            if (step == "review" && Panel != "candidates" && Panel != "options") Panel = "candidates";
            if ((step == "results" || step == "batch") && Panel != "items" && Panel != "fleets") Panel = "items";
            if ((step == "results" || step == "batch") && Panel == "fleets" && !ShowFleets()) Panel = "items";
            List<IWidget> widgets = new List<IWidget>();
            switch (step)
            {
                case "source":
                    widgets.Add(SourceMode);
                    if (SourceMode.Value == "browse")
                    {
                        widgets.Add(Browse);
                        widgets.Add(AllowWorktrees);
                    }
                    else
                    {
                        widgets.Add(PasteArea);
                    }

                    widgets.Add(MaxDepth);
                    break;
                case "review":
                    if (Panel == "options") widgets.Add(Options);
                    else
                    {
                        widgets.Add(CandidateGrid);
                        widgets.Add(CandidateSearch);
                    }

                    break;
                case "history":
                    widgets.Add(HistoryGrid);
                    widgets.Add(HistoryStatus);
                    break;
                case "results":
                case "batch":
                    if (Panel == "fleets") widgets.Add(Fleets);
                    else widgets.Add(ResultGrid);
                    break;
            }

            foreach (IWidget w in Scope.Children.ToList()) Scope.Remove(w);
            foreach (IWidget w in widgets) AddChild(w);
            if (widgets.Count > 0)
            {
                IWidget first = step == "source" ? (SourceMode.Value == "browse" ? (IWidget)Browse : PasteArea) : widgets[0];
                Scope.Focus(first);
                if (ReferenceEquals(first, Options)) Options.Scope.FocusFirst();
            }
        }

        private void SwitchPanel(int direction)
        {
            if (Step == "review") Panel = Panel == "candidates" ? "options" : "candidates";
            else if (ShowFleets()) Panel = Panel == "items" ? "fleets" : "items";
            ShowStep(Step);
        }

        /// <summary>
        /// Esc in a text field leaves it for a widget that is not a text field, so the next Esc closes the wizard: the
        /// search returns to the candidates, an options field to the first option, a source field to the source mode.
        /// (Moving to the next widget left Esc stuck in the last text field of a step.)
        /// </summary>
        private void LeaveTextField()
        {
            if (ReferenceEquals(Scope.Focused, CandidateSearch)) Scope.Focus(CandidateGrid);
            else if (ReferenceEquals(Scope.Focused, Options)) Options.Scope.FocusFirst();
            else if (Step == "source") Scope.Focus(SourceMode);
            else if (!Scope.Move(true)) Scope.Move(false);
        }

        private void ShowHistory()
        {
            StopPolling();
            ShowStep("history");
            HistoryGrid.Reload();
        }

        private void RequestClose()
        {
            if (Step == "review" && Candidates.Count > 0)
            {
                Confirm("Leave the import review?", Tr("Nothing has been imported yet. The discovered batch stays in the import history, so you can continue it later."), () => Context.Navigate("/vessels"), "Leave");
                return;
            }

            Context.Navigate("/vessels");
        }

        private void ResetAll()
        {
            StopPolling();
            _LastStatus = null;
            Batch = null;
            Candidates = new List<VesselImportItem>();
            DiscoveryHints = new List<VesselImportHint>();
            _Truncated = false;
            CandidateGrid.ClearMarks();
            CandidateGrid.SetLocalRows(new List<VesselImportItem>());
            ResultItems = new List<VesselImportItem>();
            ResultGrid.SetLocalRows(ResultItems);
            Fleets.Load(new List<VesselImportFleetRecommendation>());
            _JobId = null;
            Error = null;
            _ImportErrorCode = null;
            _PollError = null;
            _ShowCategorizationErrors = false;
            ShowStep("source");
        }

        private void LoadRoots()
        {
            Browse.EmptyText = "Loading allowed roots...";
            Call((c, t) => c.BrowseVesselImportAsync(null, t), r =>
            {
                Browse.Roots = r?.Entries ?? new List<VesselBrowseEntry>();
                if (Browse.Roots.Count == 0) Browse.EmptyText = "No browsable folders. An administrator can configure allowed roots under Settings > Import.";
            }, null, ex =>
            {
                Browse.Roots = null;
                Browse.EmptyText = Describe(ex, Tr("Failed to list the directory.")) + "  (F5)";
            });
        }

        private void LoadFolder(string path)
        {
            Browse.Loading.Add(path);
            Browse.Errors.Remove(path);
            Call((c, t) => c.BrowseVesselImportAsync(path, t), r =>
            {
                Browse.Loading.Remove(path);
                Browse.Children[path] = r?.Entries ?? new List<VesselBrowseEntry>();
            }, null, ex =>
            {
                Browse.Loading.Remove(path);
                Browse.Errors[path] = Describe(ex, Tr("Failed to list the directory."));
            });
        }

        private void LoadReview(VesselImportBatch batch, List<VesselImportItem> items, bool truncated, List<VesselImportHint> hints)
        {
            Batch = batch;
            Candidates = items;
            _Truncated = truncated;
            DiscoveryHints = hints;
            CandidateGrid.ClearMarks();
            foreach (VesselImportItem i in items.Where(c => c.CandidateStatus == VesselImportCandidateStatusEnum.New)) CandidateGrid.Marked.Add(i.Path);
            if (!String.IsNullOrEmpty(batch.FleetId)) DefaultFleet.SetValue(batch.FleetId);
            Error = null;
            _ImportErrorCode = null;
            CandidateFilter = null;
            Panel = "candidates";
            ApplyCandidateFilter();
            ShowStep("review");
        }

        private void ApplyCandidateFilter()
        {
            string term = CandidateSearch.Value.Trim();
            CandidateGrid.SetLocalRows(Candidates.Where(c => (!CandidateFilter.HasValue || c.CandidateStatus == CandidateFilter.Value)
                && (term.Length == 0 || c.Path.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || c.ProposedName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)));
        }

        private void CycleCandidateFilter()
        {
            List<VesselImportCandidateStatusEnum?> options = new List<VesselImportCandidateStatusEnum?> { null };
            options.AddRange(ImportText.CandidateStatuses.Where(s => Candidates.Any(c => c.CandidateStatus == s)).Select(s => (VesselImportCandidateStatusEnum?)s));
            int idx = options.IndexOf(CandidateFilter);
            CandidateFilter = options[(idx + 1) % options.Count];
            ApplyCandidateFilter();
        }

        private void ToggleCandidate(VesselImportItem item)
        {
            if (CandidateGrid.Marked.Contains(item.Path))
            {
                CandidateGrid.Marked.Remove(item.Path);
                return;
            }

            if (ImportText.IsImportable(item.CandidateStatus)) CandidateGrid.Marked.Add(item.Path);
        }

        private void ToggleAllFiltered()
        {
            string term = CandidateSearch.Value.Trim();
            List<VesselImportItem> selectable = Candidates.Where(c => ImportText.IsImportable(c.CandidateStatus) && (!CandidateFilter.HasValue || c.CandidateStatus == CandidateFilter.Value)
                && (term.Length == 0 || c.Path.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || c.ProposedName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            bool all = selectable.Count > 0 && selectable.All(c => CandidateGrid.Marked.Contains(c.Path));
            foreach (VesselImportItem c in selectable)
            {
                if (all) CandidateGrid.Marked.Remove(c.Path);
                else CandidateGrid.Marked.Add(c.Path);
            }
        }

        private void SelectAllNew()
        {
            CandidateGrid.ClearMarks();
            foreach (VesselImportItem c in Candidates.Where(c => c.CandidateStatus == VesselImportCandidateStatusEnum.New)) CandidateGrid.Marked.Add(c.Path);
        }

        private string? CategorizationError()
        {
            if (!Categorize.Checked) return null;
            if (String.IsNullOrEmpty(CategorizeCaptain.Value)) return "Choose the captain that will recommend fleets.";
            if (CategorizePrompt.Text.Length > 32768) return "Instructions must be 32768 characters or fewer.";
            return null;
        }

        private void SyncCategorizationVisibility()
        {
            bool on = Categorize.Checked;
            CategorizeCaptain.Visible = on;
            CategorizePrompt.Visible = on;
            ApplyAutomatically.Visible = on;
            FormRow? reset = Options.Rows.FirstOrDefault(r => r.Label == " ");
            if (reset?.Field is ArmadaWidget rw) rw.Visible = on;
            FormRow? captainRow = Options.Rows.FirstOrDefault(r => ReferenceEquals(r.Field, CategorizeCaptain));
            if (captainRow != null)
            {
                string? error = _ShowCategorizationErrors ? CategorizationError() : null;
                int available = _Captains.Count(c => c.State == CaptainStateEnum.Idle);
                string help = error != null ? Tr(error)
                    : _Captains.Count == 0 && _CaptainsRequested ? Tr("No captains yet. Create one under Captains first.")
                    : available == 0 && _Captains.Count > 0 ? Tr("Every captain is busy right now. Only idle captains can be chosen.")
                    : Tr("Only idle captains can be chosen; busy ones are listed but disabled.");
                Captain? selected = _Captains.FirstOrDefault(c => c.Id == CategorizeCaptain.Value);
                if (selected != null && !String.IsNullOrEmpty(selected.Model)) help += " " + Tr("Model: {{model}}", LocalizationArgs.Of("model", selected.Model));
                captainRow.Hint = help;
            }

            FormRow? promptRow = Options.Rows.FirstOrDefault(r => ReferenceEquals(r.Field, CategorizePrompt));
            if (promptRow != null)
            {
                string help = _DefaultPromptError != null
                    ? Tr("The default instructions could not be loaded: {{error}}", LocalizationArgs.Of("error", _DefaultPromptError))
                    : Tr("Pre-filled from the import.fleet_categorization prompt (Configuration > Prompts). Armada always adds the output format, so editing this text cannot break the result.");
                if (_TimeoutMinutes.HasValue) help += " " + Tr(_TimeoutMinutes.Value == 1 ? "The captain has up to {{count}} minute." : "The captain has up to {{count}} minutes.", LocalizationArgs.Of("count", _TimeoutMinutes.Value));
                promptRow.Hint = help;
            }

            FormRow? applyRow = Options.Rows.FirstOrDefault(r => ReferenceEquals(r.Field, ApplyAutomatically));
            if (applyRow != null)
            {
                applyRow.Hint = ApplyAutomatically.Checked
                    ? "Fleets are created (or reused by name) and vessels assigned as soon as the captain finishes."
                    : "You review, edit, and apply the recommended fleets yourself when the captain finishes.";
            }
        }

        private void LoadCategorizationData()
        {
            if (_CaptainsRequested) return;
            _CaptainsRequested = true;
            string? tenantId = Context.Session.Identity?.Tenant?.Id;
            Call((c, t) => c.ListCaptainsAsync(new ArmadaPageQuery(1, 9999), t), r =>
            {
                _Captains = (r?.Objects ?? new List<Captain>()).Where(c => String.IsNullOrEmpty(tenantId) || c.TenantId == null || c.TenantId == tenantId).ToList();
                CategorizeCaptain.Options = _Captains.Select(c =>
                {
                    SelectOption<string> o = new SelectOption<string>(c.Id, c.Name, c.State.ToString() + (String.IsNullOrEmpty(c.Model) ? "" : " - " + c.Model));
                    o.Enabled = c.State == CaptainStateEnum.Idle;
                    return o;
                }).ToList();
                SyncCategorizationVisibility();
            }, null, ex => SyncCategorizationVisibility());
            Call((c, t) => c.GetFleetCategorizationDefaultPromptAsync(t), r =>
            {
                if (r == null) return;
                _DefaultPrompt = r.Prompt ?? "";
                _TimeoutMinutes = r.TimeoutMinutes;
                if (CategorizePrompt.Text.Length == 0) CategorizePrompt.Text = _DefaultPrompt;
                SyncCategorizationVisibility();
            }, null, ex =>
            {
                _DefaultPromptError = String.IsNullOrEmpty(ex.Message) ? Tr("unknown error") : ex.Message;
                SyncCategorizationVisibility();
            });
        }

        private void ApplyBatchDetail(VesselImportBatchDetail detail)
        {
            Batch = detail.Batch;
            ResultItems = detail.Items ?? new List<VesselImportItem>();
            List<VesselImportFleetRecommendation> recommendations = detail.FleetRecommendations ?? new List<VesselImportFleetRecommendation>();
            string key = String.Join("|", recommendations.Select(r => r.Id + ":" + String.Join(",", r.VesselIds ?? new List<string>()) + ":" + (r.AppliedFleetId ?? ""))) + "#" + detail.Batch.CategorizationStatus;
            if (key != _RecommendationKey)
            {
                _RecommendationKey = key;
                Fleets.Load(recommendations);
                Fleets.Editing = detail.Batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Completed;
                Fleets.ShowErrors = false;
            }

            Fleets.VesselNames.Clear();
            foreach (VesselImportItem i in ResultItems)
            {
                string? id = i.VesselId ?? i.ExistingVesselId;
                if (!String.IsNullOrEmpty(id)) Fleets.VesselNames[id!] = i.ProposedName;
            }

            ApplyOutcomeFilter();
        }

        private void ApplyOutcomeFilter()
        {
            ResultGrid.SetLocalRows(ResultItems.Where(i => !OutcomeFilter.HasValue || i.Outcome == OutcomeFilter.Value));
        }

        private void CycleOutcomeFilter()
        {
            List<VesselImportOutcomeEnum?> options = new List<VesselImportOutcomeEnum?> { null };
            options.AddRange(ImportText.Outcomes.Where(o => ResultItems.Any(i => i.Outcome == o)).Select(o => (VesselImportOutcomeEnum?)o));
            int idx = options.IndexOf(OutcomeFilter);
            OutcomeFilter = options[(idx + 1) % options.Count];
            ApplyOutcomeFilter();
        }

        private void ToastImportFinished(VesselImportBatch batch)
        {
            string text = Context.Loc.T("{count, plural, one {Import finished: # vessel created.} other {Import finished: # vessels created.}}", LocalizationArgs.Of("count", batch.CreatedCount));
            Toast(batch.FailedCount > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success, text);
        }

        private void StartPolling(string batchId, string status)
        {
            StopPolling();
            _LastStatus = status;
            _PollingBatchId = batchId;
            Polling = true;
            _PollTimer = new Timer(_ => Context.Dispatcher.Post(() =>
            {
                if (!IsLive || _PollingBatchId != batchId) return;
                Poll(batchId);
            }), null, PollMilliseconds, PollMilliseconds);
        }

        private void StopPolling()
        {
            _PollTimer?.Dispose();
            _PollTimer = null;
            _PollingBatchId = null;
            Polling = false;
        }

        private async Task<GridPage<VesselImportBatch>> LoadHistoryAsync(GridQuery query, CancellationToken token)
        {
            VesselImportBatchEnumerateQuery q = new VesselImportBatchEnumerateQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            string? status = HistoryStatus.Value;
            if (!String.IsNullOrEmpty(status)) q.Status = status;
            EnumerationResult<VesselImportBatch>? result = await Context.Client.EnumerateVesselImportBatchesAsync(q, token).ConfigureAwait(false);
            List<VesselImportBatch> rows = result?.Objects ?? new List<VesselImportBatch>();
            Post(() => HistoryGrid.EmptyText = String.IsNullOrEmpty(status) ? Tr("No imports yet") + ". " + Tr("Each discovery is saved as a batch here, so you can come back to a review or check what an import created.") : Tr("No batches match this status"));
            return new GridPage<VesselImportBatch>(rows, result?.TotalRecords ?? rows.Count);
        }

        private void RenameFleet()
        {
            ImportFleetDraft? draft = Fleets.Current?.Draft;
            if (draft == null) return;
            FleetPrompt("Fleet name", "Fleet name", draft.Name, value => draft.Name = value);
        }

        private void DescribeFleet()
        {
            ImportFleetDraft? draft = Fleets.Current?.Draft;
            if (draft == null) return;
            FleetPrompt("Description", "Description", draft.Description, value => draft.Description = value);
        }

        private void FleetPrompt(string title, string label, string initial, Action<string> done)
        {
            OpsFormDialog dialog = NewForm(title, "OK");
            InputField input = new InputField();
            input.Value = initial ?? "";
            dialog.AddField(label, input);
            dialog.Submit = d =>
            {
                string value = input.Value;
                Post(() => done(value));
                return true;
            };
            Context.Modals.Show(dialog);
        }

        private void MoveVessel()
        {
            ImportFleetRow? row = Fleets.Current;
            if (row?.VesselId == null) return;
            string vesselId = row.VesselId;
            string name = Fleets.VesselNames.TryGetValue(vesselId, out string? n) ? n : vesselId;
            SelectField<string> picker = NewSelect(Tr("Move {{name}} to another fleet", LocalizationArgs.Of("name", name)),
                Fleets.Drafts.Select(d => new SelectOption<string>(d.Key, String.IsNullOrWhiteSpace(d.Name) ? Tr("Unnamed fleet") : d.Name)).ToList());
            picker.SetValue(row.Draft.Key);
            picker.ValueChanged += (s, e) =>
            {
                if (!String.IsNullOrEmpty(picker.Value) && picker.Value != row.Draft.Key) Fleets.MoveVessel(vesselId, picker.Value!);
            };
            picker.Open();
        }

        private void AddFleet()
        {
            ImportFleetDraft draft = Fleets.AddFleet();
            Fleets.Reveal(draft.Key);
            FleetPrompt("Fleet name", "Fleet name", "", value => draft.Name = value);
        }

        private void RemoveFleet()
        {
            ImportFleetRow? row = Fleets.Current;
            if (row == null || row.Draft.VesselIds.Count > 0) return;
            Fleets.Drafts.Remove(row.Draft);
        }

        private void RequestApply()
        {
            Fleets.ShowErrors = true;
            if (Fleets.Validate().Count > 0)
            {
                Error = Tr("Fix the highlighted fleets first.");
                return;
            }

            if (Fleets.FleetCount() == 0)
            {
                Error = Tr("Assign at least one repository to a named fleet.");
                return;
            }

            Error = null;
            int assigned = Fleets.AssignedCount();
            string message = Context.Loc.T("{count, plural, one {# repository will be assigned to its recommended fleet. Missing fleets are created; fleets with the same name are reused.} other {# repositories will be assigned to their recommended fleets. Missing fleets are created; fleets with the same name are reused.}}", LocalizationArgs.Of("count", assigned));
            Confirm("Apply these fleets?", message, ApplyFleets, "Apply fleets");
        }

        private void ApplyFleets()
        {
            if (Batch == null) return;
            string batchId = Batch.Id;
            FleetRecommendationApplyRequest request = Fleets.BuildApply();
            _FleetBusy = true;
            Call((c, t) => c.ApplyFleetRecommendationsAsync(batchId, request, t), result =>
            {
                _FleetBusy = false;
                Fleets.Editing = false;
                int count = result?.Assignments?.Count ?? 0;
                Toast(NotificationSeverityEnum.Success, Context.Loc.T("{count, plural, one {Fleets applied: # vessel assigned.} other {Fleets applied: # vessels assigned.}}", LocalizationArgs.Of("count", count)));
                RefreshAfterChange();
            }, null, ex =>
            {
                _FleetBusy = false;
                Error = Describe(ex, Tr("Applying the fleets failed."));
            });
        }

        private void StopCaptain()
        {
            if (Batch == null || String.IsNullOrEmpty(Batch.CategorizationJobId)) return;
            string jobId = Batch.CategorizationJobId!;
            _FleetBusy = true;
            Call((c, t) => c.CancelJobAsync(jobId, t), r =>
            {
                _FleetBusy = false;
                RefreshAfterChange();
            }, null, ex =>
            {
                _FleetBusy = false;
                Error = String.IsNullOrEmpty(ex.Message) ? Tr("Could not stop the captain.") : ex.Message;
            });
        }

        private void RetryCategorization()
        {
            if (Batch == null) return;
            string batchId = Batch.Id;
            _FleetBusy = true;
            Call((c, t) => c.CategorizeVesselImportAsync(batchId, null, t), r =>
            {
                _FleetBusy = false;
                RefreshAfterChange();
            }, null, ex =>
            {
                _FleetBusy = false;
                Error = Describe(ex, Tr("Could not start fleet categorization."));
            });
        }

        private void RefreshAfterChange()
        {
            if (Batch == null) return;
            string id = Batch.Id;
            _LastStatus = Batch.Status.ToString();
            Poll(id);
            StartPolling(id, Batch.Status.ToString());
        }

        private void RenderSource(ISurface surface, Rect rect)
        {
            int y = rect.Y;
            int bottom = rect.Y + rect.Height;
            int labelWidth = 22;
            SurfaceText.Draw(surface, 0, y, Tr("Source"), Theme.Muted, labelWidth);
            Scope.RenderChild(surface, SourceMode, new Rect(labelWidth, y, Math.Min(30, rect.Width - labelWidth), 1));
            y += 2;
            List<string> paths = SourcePaths();
            if (SourceMode.Value == "browse")
            {
                int treeHeight = Math.Max(3, bottom - y - 6);
                SurfaceText.Draw(surface, 0, y++, Tr("Folders on the Admiral host") + "  (Space " + Tr("Select") + ", Enter/Right " + Tr("Expand") + ")", Theme.Muted, rect.Width);
                y++;
                Scope.RenderChild(surface, Browse, new Rect(0, y, rect.Width, treeHeight));
                y += treeHeight + 1;
                if (y < bottom) Scope.RenderChild(surface, AllowWorktrees, new Rect(0, y++, Math.Min(40, rect.Width), 1));
                y++;
                if (y < bottom && Browse.Selected.Count > 0)
                    SurfaceText.Draw(surface, 0, y++, Context.Loc.T("{count, plural, one {# folder selected} other {# folders selected}}", LocalizationArgs.Of("count", Browse.Selected.Count)) + ": " + String.Join(", ", Browse.Selected), Theme.Info, rect.Width);
            }
            else
            {
                SurfaceText.Draw(surface, 0, y++, Tr("Folders on the Admiral host, one per line"), Theme.Muted, rect.Width);
                y++;
                int areaHeight = Math.Max(3, Math.Min(10, bottom - y - 6));
                Scope.RenderChild(surface, PasteArea, new Rect(0, y, rect.Width, areaHeight));
                y += areaHeight + 1;
                if (y < bottom) SurfaceText.Draw(surface, 0, y++, Context.Loc.T("{count, plural, one {# path} other {# paths}}", LocalizationArgs.Of("count", paths.Count)) + " - " + Tr("A repository folder becomes one candidate; any other folder is scanned for repositories below it."), Theme.Muted, rect.Width);
            }

            y++;
            if (y < bottom)
            {
                SurfaceText.Draw(surface, 0, y, Tr("Max depth"), Theme.Muted, labelWidth);
                Scope.RenderChild(surface, MaxDepth, new Rect(labelWidth, y, Math.Min(18, rect.Width - labelWidth), 1));
                y += 2;
            }

            string? depthError = DepthError(MaxDepth.Value);
            if (y < bottom) SurfaceText.Draw(surface, labelWidth, y++, depthError != null ? "! " + Tr(depthError) : Tr("Folder levels searched below each folder that is not itself a repository (1-16). Leave empty for the server default."), depthError != null ? Theme.Error : Theme.Muted, rect.Width - labelWidth);
            if (y < bottom) SurfaceText.Draw(surface, 0, y, _Discovering ? Tr("Discovering...") : "Ctrl+S " + Tr("Discover") + (paths.Count == 0 ? "  (" + Tr("add folders first") + ")" : ""), _Discovering ? Theme.Info : Theme.Accent, rect.Width);
        }

        private void RenderDiscovering(ISurface surface, Rect rect)
        {
            int y = rect.Y;
            SurfaceText.Draw(surface, 0, y++, "~ " + Tr("Scanning for repositories in the background."), Theme.Info, rect.Width);
            foreach (string line in TextCells.Wrap(Tr("You can close this dialog; discovery keeps running and the header shows it while it runs. Reopen it from the import history to review the candidates."), rect.Width))
                SurfaceText.Draw(surface, 0, y++, line, Theme.Muted, rect.Width);
            if (Batch != null) SurfaceText.Draw(surface, 0, y++, Batch.Id, Theme.Code, rect.Width);
            if (_PollError != null) SurfaceText.Draw(surface, 0, y++, "! " + _PollError, Theme.Error, rect.Width);
            y++;
            SurfaceText.Draw(surface, 0, y, "s " + Tr("Start over") + "  h " + Tr("Import history") + "  Esc " + Tr("Close"), Theme.Muted, rect.Width);
        }

        private void RenderReview(ISurface surface, Rect rect)
        {
            int y = rect.Y;
            int bottom = rect.Y + rect.Height;
            string panels = (Panel == "candidates" ? "[" + Tr("Candidates") + "]" : " " + Tr("Candidates") + " ") + "  " + (Panel == "options" ? "[" + Tr("Defaults and fleet recommendations") + "]" : " " + Tr("Defaults and fleet recommendations") + " ") + "   ([ ])";
            string import = _Importing ? Tr("Importing...") : "Ctrl+S " + Context.Loc.T("{count, plural, =0 {Import repositories} one {Import # repository} other {Import # repositories}}", LocalizationArgs.Of("count", CandidateGrid.Marked.Count));
            SurfaceText.Draw(surface, 0, y, panels, Theme.Accent, rect.Width);
            SurfaceText.Draw(surface, Math.Max(0, rect.Width - TextCells.Width(import)), y, import, CandidateGrid.Marked.Count > 0 ? Theme.Success : Theme.Muted, rect.Width);
            y++;
            if (Candidates.Count == 0)
            {
                SurfaceText.Draw(surface, 0, y++, Tr("No repositories found"), Theme.Warning, rect.Width);
                List<string> lines = DiscoveryHints.Select(h => h.Code == "PathNotVisibleToAdmiral" ? Tr(ImportText.HintLabel(h.Code, h.Message)) : h.Message).ToList();
                lines.Add(Tr("Discovery searches each folder up to the max depth, skips excluded names (such as node_modules, bin, obj) and anything starting with a dot, and never descends into a repository it already found."));
                lines.Add(Tr("Try a higher max depth, a folder closer to the repositories, or check the excluded names under Settings > Import."));
                foreach (string l in lines)
                {
                    foreach (string w in TextCells.Wrap(l, rect.Width))
                    {
                        if (y >= bottom) return;
                        SurfaceText.Draw(surface, 0, y++, w, Theme.Muted, rect.Width);
                    }
                }

                return;
            }

            if (Panel == "options")
            {
                y++;
                Scope.RenderChild(surface, Options, new Rect(0, y, rect.Width, Math.Max(1, bottom - y)));
                return;
            }

            List<string> hintLines = DiscoveryHints.Select(h => Tr(ImportText.HintLabel(h.Code, h.Message))).ToList();
            if (_Truncated && !DiscoveryHints.Any(h => h.Code == "CandidateLimitReached")) hintLines.Add(Tr(ImportText.HintLabel("CandidateLimitReached", "")));
            foreach (string h in hintLines)
            {
                if (y >= bottom) break;
                SurfaceText.Draw(surface, 0, y++, "i " + h, Theme.Info, rect.Width);
            }

            int x = 0;
            string all = (CandidateFilter == null ? "[" : " ") + Tr("All") + " " + Candidates.Count + (CandidateFilter == null ? "]" : " ");
            x += SurfaceText.Draw(surface, x, y, all + " ", Theme.Text, rect.Width);
            foreach (VesselImportCandidateStatusEnum s in ImportText.CandidateStatuses)
            {
                int count = Candidates.Count(c => c.CandidateStatus == s);
                if (count == 0 || x >= rect.Width) continue;
                bool active = CandidateFilter == s;
                x += SurfaceText.Draw(surface, x, y, (active ? "[" : " ") + Tr(ImportText.CandidateLabel(s)) + " " + count + (active ? "]" : " ") + " ", ImportText.CandidateStyle(s, Theme), rect.Width - x);
            }

            if (x < rect.Width) SurfaceText.Draw(surface, x, y, "  (f)", Theme.Muted, rect.Width - x);
            y += 2;
            string selectedText = Context.Loc.T("{count, plural, one {# selected} other {# selected}}", LocalizationArgs.Of("count", CandidateGrid.Marked.Count));
            int sx = SurfaceText.Draw(surface, 0, y, Tr("Search") + " ", Theme.Muted, rect.Width);
            Scope.RenderChild(surface, CandidateSearch, new Rect(sx, y, Math.Min(30, Math.Max(1, rect.Width - sx)), 1));
            string tail = "  a " + Tr("Select all new") + "  C " + Tr("Clear selection") + "  Space " + Tr("Select") + "  Ctrl+A " + Tr("All") + "   " + selectedText;
            SurfaceText.Draw(surface, Math.Min(rect.Width, sx + 31), y, tail, Theme.Muted, Math.Max(0, rect.Width - sx - 31));
            y += 2;
            if (y < bottom) Scope.RenderChild(surface, CandidateGrid, new Rect(0, y, rect.Width, Math.Max(1, bottom - y)));
        }

        private void RenderResults(ISurface surface, Rect rect)
        {
            int y = rect.Y;
            int bottom = rect.Y + rect.Height;
            VesselImportBatch? b = Batch;
            if (b == null)
            {
                SurfaceText.Draw(surface, 0, y, Error == null ? Tr("Loading...") : "", Theme.Muted, rect.Width);
                return;
            }

            int x = SurfaceText.Draw(surface, 0, y, "[" + Tr(ImportText.BatchLabel(b.Status)) + "]", ImportText.BatchStyle(b.Status, Theme), rect.Width);
            SurfaceText.Draw(surface, x + 2, y, b.Id + "  " + Context.Loc.FormatDateTime(b.CreatedUtc), Theme.Muted, Math.Max(0, rect.Width - x - 2));
            y++;
            bool importPolling = Polling && b.Status == VesselImportBatchStatusEnum.Importing;
            if (importPolling)
            {
                int processed = b.CreatedCount + b.SkippedCount + b.FailedCount;
                int target = Math.Max(Math.Max(_SelectedCount, processed), 1);
                int pct = Math.Min(100, (int)Math.Round(processed * 100.0 / target));
                SurfaceText.Draw(surface, 0, y++, Context.Loc.T("{count, plural, one {Importing # repository in the background.} other {Importing # repositories in the background.}}", LocalizationArgs.Of("count", _SelectedCount)), Theme.Info, rect.Width);
                int filled = pct * 30 / 100;
                string bar = "[" + new string('#', filled) + new string('-', 30 - filled) + "] " + Tr("{{processed}} of {{total}} processed", LocalizationArgs.Of("processed", processed, "total", target)) + (_JobId != null ? "  J " + Tr("Open the Jobs page") + " (" + _JobId + ")" : "");
                SurfaceText.Draw(surface, 0, y++, bar, Theme.Info, rect.Width);
                SurfaceText.Draw(surface, 0, y++, Tr("You can close this dialog; the import keeps running and appears in the import history."), Theme.Muted, rect.Width);
            }

            if (_PollError != null) SurfaceText.Draw(surface, 0, y++, "! " + _PollError, Theme.Error, rect.Width);
            SurfaceText.Draw(surface, 0, y++, Tr("Created") + " " + b.CreatedCount + "  |  " + Tr("Skipped") + " " + b.SkippedCount + "  |  " + Tr("Failed") + " " + b.FailedCount + "  |  " + Tr("Candidates") + " " + b.CandidateCount, Theme.Accent, rect.Width);
            if (ShowFleets())
            {
                string panels = (Panel == "items" ? "[" + Tr("Items") + "]" : " " + Tr("Items") + " ") + "  " + (Panel == "fleets" ? "[" + Tr("Fleet recommendations") + "]" : " " + Tr("Fleet recommendations") + " ") + "   ([ ])  " + Tr(ImportText.CategorizationLabel(b.CategorizationStatus));
                SurfaceText.Draw(surface, 0, y++, panels, Theme.Accent, rect.Width);
            }

            if (Panel == "fleets" && ShowFleets())
            {
                RenderFleets(surface, new Rect(0, y, rect.Width, Math.Max(1, bottom - y)));
                return;
            }

            if (ResultItems.Count == 0)
            {
                if (!importPolling && y < bottom) SurfaceText.Draw(surface, 0, y, Tr("This batch has no items."), Theme.Muted, rect.Width);
                return;
            }

            int cx = 0;
            cx += SurfaceText.Draw(surface, cx, y, (OutcomeFilter == null ? "[" : " ") + Tr("All") + " " + ResultItems.Count + (OutcomeFilter == null ? "]" : " ") + " ", Theme.Text, rect.Width);
            foreach (VesselImportOutcomeEnum o in ImportText.Outcomes)
            {
                int count = ResultItems.Count(i => i.Outcome == o);
                if (count == 0 || cx >= rect.Width) continue;
                bool active = OutcomeFilter == o;
                cx += SurfaceText.Draw(surface, cx, y, (active ? "[" : " ") + Tr(ImportText.OutcomeLabel(o)) + " " + count + (active ? "]" : " ") + " ", ImportText.OutcomeStyle(o, Theme), rect.Width - cx);
            }

            if (cx < rect.Width) SurfaceText.Draw(surface, cx, y, "  (f)  Enter " + Tr("Open vessel"), Theme.Muted, rect.Width - cx);
            y += 2;
            if (y < bottom) Scope.RenderChild(surface, ResultGrid, new Rect(0, y, rect.Width, Math.Max(1, bottom - y)));
        }

        private void RenderFleets(ISurface surface, Rect rect)
        {
            int y = rect.Y;
            int bottom = rect.Y + rect.Height;
            VesselImportBatch b = Batch!;
            VesselImportCategorizationStatusEnum status = b.CategorizationStatus;
            if (status == VesselImportCategorizationStatusEnum.Pending || status == VesselImportCategorizationStatusEnum.Running)
            {
                string text = status == VesselImportCategorizationStatusEnum.Pending
                    ? Tr("Fleet recommendations will start when the import finishes.")
                    : Tr("A captain is reading the imported repositories and recommending fleets. This runs in the background and can take several minutes.");
                SurfaceText.Draw(surface, 0, y++, "~ " + text, Theme.Info, rect.Width);
                foreach (string line in TextCells.Wrap(Tr("You can close this dialog; the header shows the task while it runs and you are notified when it finishes. Reopen this batch from the import history to review the fleets."), rect.Width))
                {
                    if (y >= bottom) return;
                    SurfaceText.Draw(surface, 0, y++, line, Theme.Muted, rect.Width);
                }

                if (y < bottom && !String.IsNullOrEmpty(b.CategorizationJobId)) SurfaceText.Draw(surface, 0, y, "J " + Tr("Open the Jobs page") + (status == VesselImportCategorizationStatusEnum.Running ? "  x " + Tr("Stop the captain") : ""), Theme.Muted, rect.Width);
                return;
            }

            if (status == VesselImportCategorizationStatusEnum.Failed)
            {
                SurfaceText.Draw(surface, 0, y++, "! " + Tr("The captain could not recommend fleets."), Theme.Error, rect.Width);
                if (!String.IsNullOrEmpty(b.CategorizationError) && y < bottom) SurfaceText.Draw(surface, 0, y++, b.CategorizationError!, Theme.Code, rect.Width);
                if (y < bottom) SurfaceText.Draw(surface, 0, y, _FleetBusy ? Tr("Starting...") : "R " + Tr("Retry categorization"), Theme.Muted, rect.Width);
                return;
            }

            if (status == VesselImportCategorizationStatusEnum.Applied && !Fleets.Editing)
            {
                SurfaceText.Draw(surface, 0, y++, "+ " + Tr("These fleets were applied. Vessels in Uncategorized kept their fleet.") + "  E " + Tr("Edit and apply again"), Theme.Success, rect.Width);
            }

            if (Fleets.Editing)
            {
                SurfaceText.Draw(surface, 0, y++, Tr("Review the captain's fleets. Rename them, move repositories between fleets, add or remove fleets, then apply. Existing fleets with the same name are reused."), Theme.Muted, rect.Width);
                string footer = Context.Loc.T("{count, plural, one {# fleet} other {# fleets}}", LocalizationArgs.Of("count", Fleets.FleetCount())) + " - " + Context.Loc.T("{count, plural, one {# repository assigned} other {# repositories assigned}}", LocalizationArgs.Of("count", Fleets.AssignedCount()))
                    + "   r " + Tr("Rename") + "  e " + Tr("Description") + "  m " + Tr("Move") + "  a " + Tr("+ Add fleet") + "  Del " + Tr("Remove fleet") + "  A " + (_FleetBusy ? Tr("Applying...") : Tr("Apply fleets"));
                SurfaceText.Draw(surface, 0, y++, footer, Theme.Accent, rect.Width);
            }

            y++;
            if (y < bottom) Scope.RenderChild(surface, Fleets, new Rect(0, y, rect.Width, Math.Max(1, bottom - y)));
        }

        private void RenderHistory(ISurface surface, Rect rect)
        {
            int y = rect.Y;
            int bottom = rect.Y + rect.Height;
            int x = SurfaceText.Draw(surface, 0, y, Tr("Status") + " ", Theme.Muted, rect.Width);
            Scope.RenderChild(surface, HistoryStatus, new Rect(x, y, Math.Min(28, rect.Width - x), 1));
            SurfaceText.Draw(surface, Math.Min(rect.Width, x + 30), y, "Enter " + Tr("Continue") + "/" + Tr("View") + "  n " + Tr("New import") + "  F5 " + Tr("Refresh"), Theme.Muted, Math.Max(0, rect.Width - x - 30));
            y += 2;
            if (y < bottom) Scope.RenderChild(surface, HistoryGrid, new Rect(0, y, rect.Width, Math.Max(1, bottom - y)));
        }

        #endregion
    }
}
