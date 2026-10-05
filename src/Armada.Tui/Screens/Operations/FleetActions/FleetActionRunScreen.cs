namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Fleet action run (W3.7, <c>/fleet-actions/runs/:id</c>), the dashboard's FleetActionRunDetail: the run's
    /// status, progress bar and counts, concurrency, duration, timestamps, timeout and clean-tree check for commands,
    /// and the command or prompt snapshot; actions (Refresh, View JSON, Re-run failed targets, Cancel run); the
    /// targets table with its status filter (vessel, status, reason, exit code, duration, voyage or output with the
    /// Truncated tag); and the target drawer (<c>Enter</c> or <c>o</c> on a Command target) with the rendered command
    /// and output. While the run is active the page refreshes every 5 seconds, paused while the drawer or a dialog is
    /// open. Not thread-safe.
    /// </summary>
    public class FleetActionRunScreen : OpsDetailScreen, IRegionOverlayHost
    {
        #region Public-Members

        /// <inheritdoc />
        public IWidget? RegionOverlay
        {
            get { return TargetDrawer.IsOpen ? TargetDrawer : null; }
        }

        /// <inheritdoc />
        public Rect RegionOverlayRect { get; private set; } = Rect.Empty;

        /// <summary>
        /// Refresh cadence while the run is active, in seconds (RUN_DETAIL_REFRESH_SECONDS).
        /// </summary>
        public const int RefreshSeconds = 5;

        /// <summary>
        /// Run id.
        /// </summary>
        public string RunId { get; }

        /// <summary>
        /// The run, or null while loading.
        /// </summary>
        public FleetActionRun? CurrentRun { get; private set; } = null;

        /// <summary>
        /// Body (summary, filter, targets).
        /// </summary>
        public FleetRunBody Body { get; } = new FleetRunBody();

        /// <summary>
        /// Target drawer.
        /// </summary>
        public Drawer TargetDrawer { get; } = new Drawer();

        /// <summary>
        /// Drawer content.
        /// </summary>
        public FleetTargetView TargetView { get; } = new FleetTargetView();

        /// <summary>
        /// Number of loads (tests).
        /// </summary>
        public int Loads { get; private set; } = 0;

        #endregion

        #region Private-Members

        private Timer? _Timer = null;
        private string? _DrawerTargetId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public FleetActionRunScreen(RouteMatch route, TuiContext context)
            : base(route, context, "FleetActionRunScreen", "Fleet action run")
        {
            RunId = route.Param("id") ?? "";
            List<SelectOption<string>> statuses = new List<SelectOption<string>> { new SelectOption<string>("", Tr("All statuses")) };
            statuses.AddRange(FleetActionLabels.TargetStatuses.Select(s => new SelectOption<string>(s.ToString(), Tr(FleetActionLabels.TargetStatusLabel(s)))));
            Body.Filter.Options = statuses;
            Body.Filter.ModalHost = context.Modals;
            Body.Filter.PickerTitle = "Filter targets by status";
            string? initial = OpsHandoff.Get(route, "status");
            Body.Filter.SetValue(initial != null && statuses.Any(o => o.Value == initial) ? initial : "");
            Body.Filter.ValueChanged += (s, e) =>
            {
                if (Body.Grid.PageNumber != 1) Body.Grid.GoToPage(1);
                else Body.Grid.Reload();
            };
            Body.Summary.Builder = BuildSummary;
            Body.Summary.Empty = "Loading run...";
            ArmadaGrid<FleetActionRunTargetSummary> grid = Body.Grid;
            grid.MultiSelect = false;
            grid.Dispatcher = context.Dispatcher;
            grid.ModalHost = context.Modals;
            grid.EmptyText = "This run has no targets";
            grid.AddColumn(new GridColumn<FleetActionRunTargetSummary>("vessel", "Vessel", t => t.VesselName) { Weight = 2 });
            grid.AddColumn(new GridColumn<FleetActionRunTargetSummary>("status", "Status", t => Armada.Tui.Widgets.StatusBadge.Marker(FleetActionLabels.TargetTone(t.Status)) + " " + Tr(FleetActionLabels.TargetStatusLabel(t.Status))) { Width = 13, Style = (t, th) => Armada.Tui.Widgets.StatusBadge.Style(FleetActionLabels.TargetTone(t.Status), th) });
            grid.AddColumn(new GridColumn<FleetActionRunTargetSummary>("reason", "Reason", t => Blank(FleetActionLabels.ReasonLabel(Context.Loc, t.SkipReason, t.FailureReason))) { Weight = 2 });
            grid.AddColumn(new GridColumn<FleetActionRunTargetSummary>("exit", "Exit code", t => CurrentRun != null && CurrentRun.Kind == FleetActionKindEnum.Mission ? "" : t.ExitCode.HasValue ? t.ExitCode.Value.ToString(CultureInfo.InvariantCulture) : "-") { Width = 9, Align = TUIKit.Widgets.CellAlignment.Right });
            grid.AddColumn(new GridColumn<FleetActionRunTargetSummary>("duration", "Duration", t => FleetActionLabels.FormatDuration(Context.Loc, t.DurationMs ?? FleetActionLabels.DurationBetween(t.StartedUtc, t.CompletedUtc, t.Status == FleetActionTargetStatusEnum.Running, Context.Clock.UtcNow))) { Width = 12, Align = TUIKit.Widgets.CellAlignment.Right });
            grid.AddColumn(new GridColumn<FleetActionRunTargetSummary>("voyage", "Output", OutputCell) { Weight = 2 });
            grid.Loader = LoadTargetsAsync;
            grid.Activated += (s, t) => OpenTarget(t);
            Body.OutputRequested += (s, t) => OpenDrawer(t.Id);
            TargetDrawer.Content = TargetView;
            TargetDrawer.WidthRatio = 0.6;
            TargetDrawer.Closed += (s, e) => { _DrawerTargetId = null; };
            TargetView.ActionRequested += (s, a) => DrawerAction(a);
            TargetDrawer.Localizer = context.Loc;
            TargetDrawer.ApplyTheme(context.Theme.Current);

            Action("json", "View JSON", () => { if (CurrentRun != null) ShowJson(Tr("Fleet action run: {{name}}", LocalizationArgs.Of("name", CurrentRun.ActionName)), CurrentRun); }, "j", () => CurrentRun != null, true);
            Action("rerun", "Re-run failed targets", RerunFailed, "R", () => CurrentRun != null && !FleetActionLabels.IsRunActive(CurrentRun.Status) && CurrentRun.FailedCount > 0 && IsTenantAdmin, true);
            Action("cancel", "Cancel run", () => { if (CurrentRun != null) ConfirmCancel(this, CurrentRun, Load); }, "x", () => CurrentRun != null && FleetActionLabels.IsRunActive(CurrentRun.Status) && IsTenantAdmin, true, true);
            Action("filter", "Filter targets by status", () => Body.Filter.Open(), "F", null, false);
            Action("output", "View output", () => { FleetActionRunTargetSummary? t = Body.Grid.Current; if (t != null) OpenDrawer(t.Id); }, null, () => CurrentRun != null && CurrentRun.Kind == FleetActionKindEnum.Command && Body.Grid.Current != null, false);
            Action("copy-id", "Copy ID", () => Copy(RunId, "Run ID"), "y", null, false);
            Action("runs", "Runs", () => Context.Navigate("/fleet-actions?tab=runs"), null, null, false);
            AddPanel("run", "Run", Body);
            SubscribeCoalesced("voyage.changed", () => { if (CurrentRun != null && FleetActionLabels.IsRunActive(CurrentRun.Status) && !TargetDrawer.IsOpen) Load(); });
            Load();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Confirm and cancel a run (shared with the Runs tab).
        /// </summary>
        /// <param name="screen">Screen.</param>
        /// <param name="run">CurrentRun.</param>
        /// <param name="after">Runs after success.</param>
        /// <returns>The dialog.</returns>
        public static ConfirmDialog ConfirmCancel(OpsScreen screen, FleetActionRun run, Action? after)
        {
            TuiContext ctx = screen.Context;
            ConfirmDialog dialog = new ConfirmDialog("Cancel run", screen.Tr("Cancel \"{{name}}\"? Pending targets are cancelled and running commands are stopped. Mission runs cancel voyages that have not finished.", LocalizationArgs.Of("name", run.ActionName)), "Cancel run", "Keep running", null, ctx.Loc, ctx.Theme.Current);
            dialog.Destructive = true;
            ctx.Modals.Show(dialog, result =>
            {
                if (!(result is bool ok && ok)) return;
                screen.Call((c, t) => c.CancelFleetActionRunAsync(run.Id, t), updated =>
                {
                    screen.Toast(NotificationSeverityEnum.Warning, screen.Tr("Run \"{{name}}\" cancelled.", LocalizationArgs.Of("name", run.ActionName)));
                    after?.Invoke();
                }, null, ex => screen.ShowMessage(String.IsNullOrEmpty(ex.Message) ? screen.Tr("Failed to cancel the run.") : ex.Message));
            });
            return dialog;
        }

        /// <inheritdoc />
        public override void Load()
        {
            Loads++;
            Call((c, t) => c.GetFleetActionRunAsync(RunId, t), detail =>
            {
                if (detail?.Run != null)
                {
                    CurrentRun = detail.Run;
                    Loaded = true;
                    LoadError = null;
                    Heading = CurrentRun.ActionName;
                    Status = FleetActionLabels.RunStatusLabel(CurrentRun.Status);
                    Body.SummaryRows = 5 + Math.Min(6, ((CurrentRun.Kind == FleetActionKindEnum.Command ? CurrentRun.CommandText : CurrentRun.PromptTemplate) ?? "").Split('\n').Length);
                    Body.Grid.Columns.First(col => col.Key == "exit").DefaultVisible = CurrentRun.Kind == FleetActionKindEnum.Command;
                    Body.Grid.Columns.First(col => col.Key == "voyage").Title = CurrentRun.Kind == FleetActionKindEnum.Command ? "Output" : "Voyage";
                    Body.Summary.Invalidate();
                    UpdateSubtitle();
                    Schedule();
                }
            }, null, ex =>
            {
                LoadError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load the run.") : ex.Message;
                if (CurrentRun == null) Heading = null;
            });
            Body.Grid.Reload();
        }

        /// <summary>
        /// Open the target drawer (Command runs).
        /// </summary>
        /// <param name="targetId">Target id.</param>
        public void OpenDrawer(string targetId)
        {
            _DrawerTargetId = targetId;
            TargetView.Target = null;
            TargetView.Error = null;
            TargetView.Invalidate();
            FleetActionRunTargetSummary? summary = Body.Grid.Rows.FirstOrDefault(t => t.Id == targetId);
            TargetDrawer.Open(summary != null ? summary.VesselName : Tr("Target"), TargetView);
            TargetDrawer.OnFocusChanged(true);
            LoadTarget();
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (TargetDrawer.IsOpen)
            {
                TargetDrawer.HandleKey(key);
                return true;
            }

            return base.HandleKey(key);
        }

        /// <inheritdoc />
        public override void Render(ISurface surface)
        {
            UpdateSubtitle();
            base.Render(surface);
            RegionOverlayRect = Rect.Empty;
            if (TargetDrawer.IsOpen)
            {
                Rect r = TargetDrawer.RectIn(surface.Size);
                TargetDrawer.Render(new SurfaceView(surface, r));
                // The drawer's left rule is its box's left edge (see RegionFrames).
                RegionOverlayRect = new Rect(r.X + 1, r.Y, Math.Max(1, r.Width - 1), r.Height);
            }
        }

        /// <inheritdoc />
        public override int DefaultRefreshSeconds()
        {
            return 0;
        }

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            _Timer?.Dispose();
            _Timer = null;
            base.OnDeactivated();
        }

        #endregion

        #region Private-Methods

        private static string Blank(string text)
        {
            return String.IsNullOrEmpty(text) ? "-" : text;
        }

        private string OutputCell(FleetActionRunTargetSummary t)
        {
            if (CurrentRun != null && CurrentRun.Kind == FleetActionKindEnum.Mission) return String.IsNullOrEmpty(t.VoyageId) ? "-" : t.VoyageId!;
            return Tr("View output") + " (o)" + (t.OutputTruncated ? "  [" + Tr("Truncated") + "]" : "");
        }

        private void UpdateSubtitle()
        {
            if (CurrentRun == null)
            {
                SubtitleText = RunId;
                return;
            }

            bool active = FleetActionLabels.IsRunActive(CurrentRun.Status);
            string state = !active
                ? Tr("Run finished; auto-refresh stopped.")
                : (TargetDrawer.IsOpen || Context.Modals.IsModalOpen)
                    ? Tr("Auto-refresh paused while a panel is open.")
                    : Tr("Live: refreshing every {{seconds}} s.", LocalizationArgs.Of("seconds", RefreshSeconds));
            SubtitleText = Tr(CurrentRun.Kind.ToString()) + (CurrentRun.ActionId == null ? "  [" + Tr("Ad hoc") + "]" : "") + "  " + CurrentRun.Id + "   " + state;
        }

        private void Schedule()
        {
            bool active = CurrentRun != null && FleetActionLabels.IsRunActive(CurrentRun.Status);
            if (!active)
            {
                _Timer?.Dispose();
                _Timer = null;
                return;
            }

            if (_Timer != null) return;
            _Timer = new Timer(_ => Post(Tick), null, RefreshSeconds * 1000, RefreshSeconds * 1000);
        }

        private void Tick()
        {
            if (CurrentRun == null || !FleetActionLabels.IsRunActive(CurrentRun.Status)) return;
            if (TargetDrawer.IsOpen || Context.Modals.IsModalOpen) return;
            Load();
        }

        private async Task<GridPage<FleetActionRunTargetSummary>> LoadTargetsAsync(GridQuery query, CancellationToken token)
        {
            FleetActionRunTargetEnumerateQuery q = new FleetActionRunTargetEnumerateQuery();
            q.PageNumber = query.PageNumber;
            q.PageSize = query.PageSize;
            string? status = Body.Filter.Value;
            if (!String.IsNullOrEmpty(status) && Enum.TryParse(status, out FleetActionTargetStatusEnum parsed)) q.Status = parsed;
            EnumerationResult<FleetActionRunTargetSummary>? result = await Context.Client.EnumerateFleetActionRunTargetsAsync(RunId, q, token).ConfigureAwait(false);
            List<FleetActionRunTargetSummary> rows = result?.Objects ?? new List<FleetActionRunTargetSummary>();
            bool filtered = !String.IsNullOrEmpty(status);
            Post(() => Body.Grid.EmptyText = filtered ? "No targets match this status" : "This run has no targets");
            return new GridPage<FleetActionRunTargetSummary>(rows, result?.TotalRecords ?? rows.Count);
        }

        private void OpenTarget(FleetActionRunTargetSummary target)
        {
            if (CurrentRun != null && CurrentRun.Kind == FleetActionKindEnum.Mission)
            {
                if (!String.IsNullOrEmpty(target.VoyageId)) Context.Navigate("/voyages/" + Uri.EscapeDataString(target.VoyageId!));
                return;
            }

            OpenDrawer(target.Id);
        }

        private void LoadTarget()
        {
            string? id = _DrawerTargetId;
            if (id == null) return;
            Call((c, t) => c.GetFleetActionRunTargetAsync(RunId, id, t), target =>
            {
                if (_DrawerTargetId != id) return;
                TargetView.Target = target;
                TargetView.Error = null;
                if (target != null) TargetDrawer.Title = target.VesselName;
                TargetView.Invalidate();
            }, null, ex =>
            {
                TargetView.Error = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load the target.") : ex.Message;
                TargetView.Invalidate();
            });
        }

        private void DrawerAction(string action)
        {
            FleetActionRunTarget? t = TargetView.Target;
            switch (action)
            {
                case "refresh":
                    LoadTarget();
                    return;
                case "copy":
                    if (t != null && !String.IsNullOrEmpty(t.RenderedText)) Copy(t.RenderedText, "Command");
                    return;
                case "vessel":
                    if (t != null) Context.Navigate("/vessels/" + Uri.EscapeDataString(t.VesselId));
                    return;
                case "voyage":
                    if (t != null && !String.IsNullOrEmpty(t.VoyageId)) Context.Navigate("/voyages/" + Uri.EscapeDataString(t.VoyageId!));
                    return;
                case "stdout":
                case "stderr":
                    if (t == null) return;
                    string text = action == "stderr" ? t.ErrorText ?? "" : t.OutputText ?? "";
                    if (text.Length == 0) return;
                    string stream = Tr(action == "stderr" ? "Standard error" : "Standard output");
                    LogViewer viewer = new LogViewer();
                    viewer.Follow = false;
                    viewer.SetText(text);
                    ViewerModal modal = new ViewerModal(Tr("{{vessel}}: {{stream}}", LocalizationArgs.Of("vessel", t.VesselName, "stream", stream)), viewer, Context.Loc, Context.Theme.Current);
                    modal.WidthRatio = 0.92;
                    modal.HeightRatio = 0.9;
                    modal.CopyRequested += (s, e) => Copy(text, "Output");
                    Context.Modals.Show(modal);
                    return;
            }
        }

        private void RerunFailed()
        {
            FleetActionRun? run = CurrentRun;
            if (run == null) return;
            Call(async (c, t) =>
            {
                FleetActionRunTargetEnumerateQuery failed = new FleetActionRunTargetEnumerateQuery { PageNumber = 1, PageSize = 500, Status = FleetActionTargetStatusEnum.Failed };
                FleetActionRunTargetEnumerateQuery timedOut = new FleetActionRunTargetEnumerateQuery { PageNumber = 1, PageSize = 500, Status = FleetActionTargetStatusEnum.TimedOut };
                List<FleetActionRunTargetSummary> a = (await c.EnumerateFleetActionRunTargetsAsync(run.Id, failed, t).ConfigureAwait(false))?.Objects ?? new List<FleetActionRunTargetSummary>();
                List<FleetActionRunTargetSummary> b = (await c.EnumerateFleetActionRunTargetsAsync(run.Id, timedOut, t).ConfigureAwait(false))?.Objects ?? new List<FleetActionRunTargetSummary>();
                List<string> ids = a.Concat(b).Select(x => x.VesselId).Distinct().ToList();
                string? actionId = null;
                if (ids.Count > 0 && !String.IsNullOrEmpty(run.ActionId))
                {
                    try
                    {
                        FleetAction? action = await c.GetFleetActionAsync(run.ActionId!, t).ConfigureAwait(false);
                        if (action != null && action.Active) actionId = action.Id;
                    }
                    catch (Armada.Client.ArmadaApiException)
                    {
                        actionId = null;
                    }
                }

                return new KeyValuePair<List<string>, string?>(ids, actionId);
            }, result =>
            {
                if (result.Key.Count == 0)
                {
                    Toast(NotificationSeverityEnum.Info, Tr("No failed targets to re-run."));
                    return;
                }

                FleetActionUpsertRequest? definition = null;
                if (result.Value == null)
                {
                    definition = new FleetActionUpsertRequest();
                    definition.Name = run.ActionName;
                    definition.Kind = run.Kind;
                    definition.CommandText = run.CommandText;
                    definition.PromptTemplate = run.PromptTemplate;
                    definition.PipelineId = run.PipelineId;
                    definition.TimeoutSeconds = run.TimeoutSeconds;
                    definition.RequiresCleanWorkingTree = run.RequiresCleanWorkingTree;
                }

                FleetActionRunFlow.Start(this, result.Key, result.Value, definition);
            }, null, ex => ShowMessage(String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load failed targets.") : ex.Message));
        }

        private OpsDocument BuildSummary(OpsDocument doc)
        {
            FleetActionRun? run = CurrentRun;
            if (run == null) return doc;
            bool active = FleetActionLabels.IsRunActive(run.Status);
            doc.LabelWidth = 18;
            doc.Text(FleetActionLabels.ProgressBar(run, 40) + "  " + FleetActionLabels.ProgressText(doc.Loc, run, true));
            doc.Text(Tr("Targets") + " " + doc.Loc.FormatNumber(run.TargetCount)
                + "   " + Tr("Succeeded") + " " + doc.Loc.FormatNumber(run.SucceededCount)
                + "   " + Tr("Failed") + " " + doc.Loc.FormatNumber(run.FailedCount)
                + "   " + Tr("Skipped") + " " + doc.Loc.FormatNumber(run.SkippedCount)
                + "   " + Tr("Cancelled") + " " + doc.Loc.FormatNumber(run.CancelledCount)
                + "   " + Tr("Concurrency") + " " + doc.Loc.FormatNumber(run.Concurrency)
                + "   " + Tr("Duration") + " " + FleetActionLabels.FormatDuration(doc.Loc, FleetActionLabels.DurationBetween(run.StartedUtc, run.CompletedUtc, active, Context.Clock.UtcNow)));
            DateTime now = Context.Clock.UtcNow;
            string timing = Tr("Created") + " " + doc.Loc.FormatRelative(run.CreatedUtc, now)
                + "   " + Tr("Started") + " " + (run.StartedUtc.HasValue ? doc.Loc.FormatRelative(run.StartedUtc.Value, now) : "-")
                + "   " + Tr("Completed") + " " + (run.CompletedUtc.HasValue ? doc.Loc.FormatRelative(run.CompletedUtc.Value, now) : "-");
            if (run.Kind == FleetActionKindEnum.Command)
            {
                timing += "   " + Tr("Timeout") + " " + Tr("{{value}} s", LocalizationArgs.Of("value", doc.Loc.FormatNumber(run.TimeoutSeconds)))
                    + "   " + Tr("Clean-tree check") + " " + Tr(run.RequiresCleanWorkingTree ? "On" : "Off");
            }

            doc.Text(timing, doc.Theme.Muted);
            doc.Section(run.Kind == FleetActionKindEnum.Command ? "Command (snapshot)" : "Prompt template (snapshot)");
            doc.Text((run.Kind == FleetActionKindEnum.Command ? run.CommandText : run.PromptTemplate) ?? "", doc.Theme.Code);
            return doc;
        }

        #endregion
    }
}
