namespace Armada.Tui.Screens.Delivery
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
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Check run detail (dashboard <c>CheckRunDetail.tsx</c>, route <c>/checks/:id</c>): actions View JSON, Retry,
    /// Draft Release, and Delete (with confirmation); panels Overview (every field with links, the command, and the
    /// summary), Comparison (deltas against the previous comparable run), Results (test results and coverage),
    /// Artifacts (path, size, last write), and Output (log viewer with follow, search, and copy).
    /// </summary>
    public class CheckRunScreen : EntityDetailScreen<CheckRun>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Check Run"; }
        }

        /// <summary>
        /// Overview panel.
        /// </summary>
        public LinkDetailView Overview { get; } = new LinkDetailView();

        /// <summary>
        /// Comparison panel.
        /// </summary>
        public LinkDetailView ComparisonView { get; } = new LinkDetailView();

        /// <summary>
        /// Test results and coverage panel.
        /// </summary>
        public LinkDetailView Results { get; } = new LinkDetailView();

        /// <summary>
        /// Artifacts panel.
        /// </summary>
        public ArmadaGrid<CheckRunArtifact> Artifacts { get; } = new ArmadaGrid<CheckRunArtifact>(a => a.Path);

        /// <summary>
        /// Output panel.
        /// </summary>
        public LogViewer Output { get; } = new LogViewer();

        /// <summary>
        /// Comparison with the previous run, or null.
        /// </summary>
        public CheckRunComparison? Comparison { get; private set; } = null;

        #endregion

        #region Private-Members

        private Vessel? _Vessel = null;
        private WorkflowProfile? _Profile = null;
        private bool _Retrying = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public CheckRunScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override Task<CheckRun?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetCheckRunAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(CheckRun run, CancellationToken token)
        {
            Task<Vessel?> vessel = String.IsNullOrEmpty(run.VesselId) ? Task.FromResult<Vessel?>(null) : Safe(() => Context.Client.GetVesselAsync(run.VesselId!, token));
            Task<WorkflowProfile?> profile = String.IsNullOrEmpty(run.WorkflowProfileId) ? Task.FromResult<WorkflowProfile?>(null) : Safe(() => Context.Client.GetWorkflowProfileAsync(run.WorkflowProfileId!, token));
            Task<EnumerationResult<CheckRun>?> related = String.IsNullOrEmpty(run.VesselId)
                ? Task.FromResult<EnumerationResult<CheckRun>?>(null)
                : Safe(() => Context.Client.ListCheckRunsAsync(new ArmadaPageQuery(1, 1000).With("vesselId", run.VesselId).With("type", run.Type.ToString()), token));
            await Task.WhenAll(vessel, profile, related).ConfigureAwait(false);
            _Vessel = vessel.Result;
            _Profile = profile.Result;
            Comparison = related.Result?.Objects != null ? CheckRunComparer.Build(run, related.Result.Objects) : null;
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddJsonAction();
            AddAction("retry", "Retry", Retry, () => Entity != null, "r");
            AddAction("draft-release", "Draft Release", () => { if (Entity != null) ChecksScreen.DraftRelease(Context, Entity); }, () => Entity != null);
            AddAction("delete", "Delete", RequestDelete, () => Entity != null, "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            ComparisonView.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            Results.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("overview", "Overview", Overview);
            AddPanel("comparison", "Compare to Previous Run", ComparisonView);
            AddPanel("results", "Test Results", Results);
            Artifacts.Dispatcher = Context.Dispatcher;
            Artifacts.ModalHost = Context.Modals;
            Artifacts.MultiSelect = false;
            Artifacts.EmptyText = "No artifacts were collected for this run.";
            Artifacts.AddColumn(new GridColumn<CheckRunArtifact>("path", "Path", a => a.Path) { Weight = 4, Sortable = true });
            Artifacts.AddColumn(new GridColumn<CheckRunArtifact>("size", "Size", a => a.SizeBytes.HasValue ? a.SizeBytes.Value.ToString("N0", CultureInfo.InvariantCulture) + " " + T("bytes") : "-") { Width = 18, Sortable = true, Align = TUIKit.Widgets.CellAlignment.Right });
            Artifacts.AddColumn(new GridColumn<CheckRunArtifact>("lastWrite", "Last Write", a => EntityUi.Date(Context, a.LastWriteUtc)) { Width = 20, Sortable = true });
            AddPanel("artifacts", "Artifacts", Artifacts);
            Output.Localizer = Context.Loc;
            AddPanel("output", "Output", Output);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(CheckRun entity)
        {
            return !String.IsNullOrEmpty(entity.Label) ? entity.Label! : entity.Type.ToString();
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(CheckRun entity)
        {
            return new[] { entity.Status.ToString() };
        }

        /// <inheritdoc />
        protected override void Populate(CheckRun r)
        {
            Overview.Reset();
            Overview.Section("Check Run");
            Overview.Row("ID", r.Id, t => t.Code);
            Overview.Row("Type", r.Type.ToString());
            Overview.Row("Status", EntityUi.Badge(Context, r.Status.ToString()), t => StatusBadge.Style(r.Status.ToString(), t));
            Overview.Row("Exit Code", r.ExitCode.HasValue ? r.ExitCode.Value.ToString(CultureInfo.InvariantCulture) : "-");
            Overview.Link("Vessel", String.IsNullOrEmpty(r.VesselId) ? "-" : (_Vessel?.Name ?? r.VesselId), !String.IsNullOrEmpty(r.VesselId) ? () => Context.Navigate("/vessels/" + r.VesselId) : (Action?)null);
            Overview.Link("Workflow Profile", String.IsNullOrEmpty(r.WorkflowProfileId) ? T("Resolved override only") : (_Profile?.Name ?? r.WorkflowProfileId),
                !String.IsNullOrEmpty(r.WorkflowProfileId) ? () => Context.Navigate("/workflow-profiles/" + r.WorkflowProfileId) : (Action?)null);
            Overview.Row("Source", r.Source.ToString());
            Overview.Row("Provider", EntityUi.Dash(r.ProviderName));
            Overview.Row("External ID", EntityUi.Dash(r.ExternalId));
            Overview.Link("External URL", EntityUi.Dash(r.ExternalUrl), !String.IsNullOrEmpty(r.ExternalUrl) ? () => Context.External.OpenUrl(r.ExternalUrl!) : (Action?)null);
            Overview.Row("Environment", EntityUi.Dash(r.EnvironmentName));
            Overview.Row("Duration", r.DurationMs.HasValue ? CheckRunComparer.Duration(r.DurationMs.Value) : "-");
            Overview.Link("Mission ID", EntityUi.Dash(r.MissionId), !String.IsNullOrEmpty(r.MissionId) ? () => Context.Navigate("/missions/" + r.MissionId) : (Action?)null);
            Overview.Link("Voyage ID", EntityUi.Dash(r.VoyageId), !String.IsNullOrEmpty(r.VoyageId) ? () => Context.Navigate("/voyages/" + r.VoyageId) : (Action?)null);
            Overview.Link("Deployment ID", EntityUi.Dash(r.DeploymentId), !String.IsNullOrEmpty(r.DeploymentId) ? () => Context.Navigate("/deployments/" + r.DeploymentId) : (Action?)null);
            Overview.Row("Branch", EntityUi.Dash(r.BranchName));
            Overview.Row("Commit", EntityUi.Dash(r.CommitHash));
            Overview.Row("Created", EntityUi.Date(Context, r.CreatedUtc));
            Overview.Row("Started", EntityUi.Date(Context, r.StartedUtc));
            Overview.Row("Completed", EntityUi.Date(Context, r.CompletedUtc));
            Overview.Section("Command");
            Overview.Row("Command", EntityUi.Dash(r.Command), t => t.Code);
            if (!String.IsNullOrEmpty(r.Summary))
            {
                Overview.Section("Summary");
                Overview.Row("Summary", r.Summary);
            }

            PopulateComparison(r);
            PopulateResults(r);
            Artifacts.SetLocalRows(r.Artifacts ?? new List<CheckRunArtifact>());
            Output.SetText(String.IsNullOrEmpty(r.Output) ? T("No output captured.") : r.Output);
        }

        #endregion

        #region Private-Methods

        private void PopulateComparison(CheckRun r)
        {
            ComparisonView.Reset();
            CheckRunComparison? c = Comparison;
            ComparisonView.Section("Compare to Previous Run");
            if (c == null)
            {
                ComparisonView.Row("Compared to", T("None"));
                return;
            }

            string baselineName = !String.IsNullOrEmpty(c.Baseline.Label) ? c.Baseline.Label! : c.Baseline.Id;
            string baselineId = c.Baseline.Id;
            ComparisonView.Link("Compared to", baselineName + " (" + T(CheckRunComparer.ScopeLabel(c.Scope)) + ") - " + EntityUi.Date(Context, c.Baseline.CreatedUtc), () => Context.Navigate("/checks/" + baselineId));
            ComparisonView.Row("Summary", CheckRunComparer.Summary(c));
            string verdict = c.HasRegression ? "Regression detected" : c.HasImprovement ? "Improvement detected" : "No significant change";
            ComparisonView.Row("Result", T(verdict), c.HasRegression ? (Func<Armada.Tui.Theming.ArmadaTheme, TUIKit.CellStyle>)(t => t.Error) : c.HasImprovement ? (t => t.Success) : null);
            ComparisonView.Row("Status", c.Baseline.Status + " -> " + r.Status);
            ComparisonView.Row("Duration Delta", CheckRunComparer.DurationDelta(c.DurationDeltaMs) ?? "-");
            ComparisonView.Row("Artifact Delta", CheckRunComparer.CountDelta(c.ArtifactCountDelta, "artifacts") ?? "-");
            ComparisonView.Row("Passed Delta", CheckRunComparer.CountDelta(c.PassedDelta, "passed") ?? "-");
            ComparisonView.Row("Failed Delta", CheckRunComparer.CountDelta(c.FailedDelta, "failed") ?? "-");
            ComparisonView.Row("Skipped Delta", CheckRunComparer.CountDelta(c.SkippedDelta, "skipped") ?? "-");
            ComparisonView.Row("Total Delta", CheckRunComparer.CountDelta(c.TotalDelta, "tests") ?? "-");
            ComparisonView.Row("Line Coverage Delta", CheckRunComparer.PercentDelta(c.LinesPctDelta) ?? "-");
            ComparisonView.Row("Branch Coverage Delta", CheckRunComparer.PercentDelta(c.BranchesPctDelta) ?? "-");
            ComparisonView.Row("Function Coverage Delta", CheckRunComparer.PercentDelta(c.FunctionsPctDelta) ?? "-");
            ComparisonView.Row("Statement Coverage Delta", CheckRunComparer.PercentDelta(c.StatementsPctDelta) ?? "-");
        }

        private void PopulateResults(CheckRun r)
        {
            Results.Reset();
            Results.Section("Test Results");
            if (r.TestSummary == null)
            {
                Results.Row("Test Results", "-");
            }
            else
            {
                CheckRunTestSummary s = r.TestSummary;
                Results.Row("Format", EntityUi.Dash(s.Format));
                Results.Row("Passed", Num(s.Passed));
                Results.Row("Failed", Num(s.Failed), s.Failed.HasValue && s.Failed.Value > 0 ? (Func<Armada.Tui.Theming.ArmadaTheme, TUIKit.CellStyle>)(t => t.Error) : null);
                Results.Row("Skipped", Num(s.Skipped));
                Results.Row("Total", Num(s.Total));
                Results.Row("Test Duration", s.DurationMs.HasValue ? CheckRunComparer.Duration(s.DurationMs.Value) : "-");
            }

            Results.Section("Coverage");
            if (r.CoverageSummary == null)
            {
                Results.Row("Coverage", "-");
            }
            else
            {
                CheckRunCoverageSummary c = r.CoverageSummary;
                Results.Row("Format", EntityUi.Dash(c.Format));
                Results.Row("Source", EntityUi.Dash(c.SourcePath));
                Results.Row("Lines", CheckRunComparer.Metric(c.Lines) ?? "-");
                Results.Row("Branches", CheckRunComparer.Metric(c.Branches) ?? "-");
                Results.Row("Functions", CheckRunComparer.Metric(c.Functions) ?? "-");
                Results.Row("Statements", CheckRunComparer.Metric(c.Statements) ?? "-");
            }
        }

        private static string Num(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "-";
        }

        private void Retry()
        {
            CheckRun? run = Entity;
            if (run == null || _Retrying) return;
            _Retrying = true;
            EntityUi.Toast(Context, NotificationSeverityEnum.Info, T("Retrying..."));
            EntityUi.Run<CheckRun?>(Context, ct => Context.Client.RetryCheckRunAsync(run.Id, ct), retried =>
            {
                _Retrying = false;
                if (retried == null) return;
                NotificationSeverityEnum severity = retried.Status == CheckRunStatusEnum.Passed ? NotificationSeverityEnum.Success : NotificationSeverityEnum.Warning;
                EntityUi.Toast(Context, severity, EntityUi.T(Context, "Retry completed with status {{status}}.", "status", retried.Status.ToString()));
                Context.Navigate("/checks/" + retried.Id);
            }, "Retry failed.", ex => _Retrying = false);
        }

        private void RequestDelete()
        {
            CheckRun? run = Entity;
            if (run == null) return;
            Context.Confirm("Delete Check Run", EntityUi.T(Context, "Delete check run \"{{id}}\"?", "id", run.Id), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteCheckRunAsync(run.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, T("Check run deleted."));
                    Context.Navigate("/delivery?tab=checks");
                }, "Delete failed.");
            }, "Delete");
        }

        private static async Task<TResult?> Safe<TResult>(Func<Task<TResult?>> call) where TResult : class
        {
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (ArmadaApiException)
            {
                return null;
            }
        }

        #endregion
    }
}
