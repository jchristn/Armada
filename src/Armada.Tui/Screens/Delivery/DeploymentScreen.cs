namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Deployment detail (dashboard <c>DeploymentDetail.tsx</c>, route <c>/deployments/:id</c>, and
    /// <c>/deployments/new</c> for create): actions Open Workspace, Run Check, Sync GitHub Actions, Runbook, Create
    /// Incident, Open Release, View JSON, Approve and Deny (pending approval), Verify and Rollback (otherwise), Edit,
    /// and Delete, each with the dashboard's confirmation; panels Overview (status, verification, profile,
    /// environment, timing, approval, monitoring window, regression alerts, the deployment's fields, the monitoring
    /// summary, and identifiers with links), Checks (linked check runs), Runbooks (runbook executions), and Requests
    /// (request-history summary with a bucket chart). Reloads live on <c>deployment.changed</c> for this deployment.
    /// </summary>
    public class DeploymentScreen : EntityDetailScreen<Deployment>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Deployment"; }
        }

        /// <inheritdoc />
        public override bool IsCreateMode
        {
            get { return String.Equals(EntityId, "new", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Overview panel.
        /// </summary>
        public LinkDetailView Overview { get; } = new LinkDetailView();

        /// <summary>
        /// Linked checks panel.
        /// </summary>
        public LinkDetailView ChecksView { get; } = new LinkDetailView();

        /// <summary>
        /// Runbook executions panel.
        /// </summary>
        public ArmadaGrid<RunbookExecution> Executions { get; } = new ArmadaGrid<RunbookExecution>(e => e.Id);

        /// <summary>
        /// Request summary.
        /// </summary>
        public LinkDetailView RequestSummary { get; } = new LinkDetailView();

        /// <summary>
        /// Request bucket chart.
        /// </summary>
        public ChartView RequestChart { get; } = new ChartView();

        #endregion

        #region Private-Members

        private List<RunbookExecution> _Executions = new List<RunbookExecution>();
        private Dictionary<string, string> _Profiles = new Dictionary<string, string>();
        private Dictionary<string, string> _Vessels = new Dictionary<string, string>();
        private Dictionary<string, string> _Environments = new Dictionary<string, string>();
        private Dictionary<string, string> _Releases = new Dictionary<string, string>();
        private bool _Syncing = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public DeploymentScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Deployment"; }
        }

        /// <inheritdoc />
        protected override IEnumerable<string> LiveEvents
        {
            get { return new[] { ArmadaEventTypes.DeploymentChanged }; }
        }

        /// <inheritdoc />
        protected override Task<Deployment?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetDeploymentAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(Deployment entity, CancellationToken token)
        {
            Task<List<RunbookExecution>> executions = SafeExecutions(entity.Id, token);
            Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(Context.Client, token);
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, token);
            Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(Context.Client, token);
            Task<List<Release>> releases = EntityLookups.ReleasesAsync(Context.Client, token);
            await Task.WhenAll(executions, profiles, vessels, environments, releases).ConfigureAwait(false);
            _Executions = executions.Result;
            _Profiles = profiles.Result.ToDictionary(p => p.Id, p => p.Name);
            _Vessels = vessels.Result.ToDictionary(v => v.Id, v => v.Name);
            _Environments = environments.Result.ToDictionary(e => e.Id, e => e.Name);
            _Releases = releases.Result.ToDictionary(r => r.Id, r => r.Title);
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            Func<bool> manage = () => Context.Session.IsTenantAdmin;
            AddAction("create", "Create Deployment", () => DeploymentForms.Open(Context, null, NavigationPrefill.Take<DeploymentUpsertRequest>(Context, PrefillSlots.CreateDeployment), Created), () => IsCreateMode && manage());
            AddAction("workspace", "Open Workspace", () => { if (Entity?.VesselId != null) Context.Navigate("/workspace/" + Entity.VesselId); }, () => !String.IsNullOrEmpty(Entity?.VesselId));
            AddAction("run-check", "Run Check", OpenRunCheck, () => Entity != null);
            AddAction("sync-github", "Sync GitHub Actions", SyncGitHubActions, () => !String.IsNullOrEmpty(Entity?.VesselId));
            AddAction("runbook", "Runbook", OpenRunbooks, () => Entity != null);
            AddAction("create-incident", "Create Incident", OpenIncidentCreate, () => Entity != null);
            AddAction("open-release", "Open Release", () => { if (Entity?.ReleaseId != null) Context.Navigate("/releases/" + Entity.ReleaseId); }, () => !String.IsNullOrEmpty(Entity?.ReleaseId));
            AddJsonAction();
            AddAction("approve", "Approve", () => Decide("approve"), () => Entity?.Status == DeploymentStatusEnum.PendingApproval && manage(), "a");
            AddAction("deny", "Deny", () => Decide("deny"), () => Entity?.Status == DeploymentStatusEnum.PendingApproval && manage(), "r");
            AddAction("verify", "Verify", () => Decide("verify"), () => Entity != null && Entity.Status != DeploymentStatusEnum.PendingApproval && manage());
            AddAction("rollback", "Rollback", () => Decide("rollback"), () => Entity != null && Entity.Status != DeploymentStatusEnum.PendingApproval && manage());
            AddAction("edit", "Edit", () => { if (Entity != null) DeploymentForms.Open(Context, Entity, null, d => Reload()); }, () => Entity != null && manage(), "e");
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && manage(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            ChecksView.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("overview", "Overview", Overview);
            AddPanel("checks", "Linked Checks", ChecksView);
            Executions.Dispatcher = Context.Dispatcher;
            Executions.ModalHost = Context.Modals;
            Executions.MultiSelect = false;
            Executions.EmptyText = "No runbook executions are linked to this deployment yet.";
            Executions.AddColumn(new GridColumn<RunbookExecution>("title", "Title", e => e.Title) { Weight = 3, Sortable = true });
            Executions.AddColumn(new GridColumn<RunbookExecution>("status", "Status", e => StatusBadge.Label(e.Status)) { Width = 14, Sortable = true, Style = (e, t) => StatusBadge.Style(e.Status, t) });
            Executions.AddColumn(new GridColumn<RunbookExecution>("environment", "Environment", e => String.IsNullOrEmpty(e.EnvironmentName) ? T("No environment") : e.EnvironmentName!) { Weight = 2 });
            Executions.AddColumn(new GridColumn<RunbookExecution>("checkType", "Check Type", e => e.CheckType.HasValue ? e.CheckType.Value.ToString() : T("No check type")) { Weight = 2 });
            Executions.AddColumn(new GridColumn<RunbookExecution>("id", "ID", e => e.Id) { Width = 26 });
            Executions.Activated += (s, e) => Context.Navigate("/runbooks/" + e.RunbookId + "?executionId=" + Uri.EscapeDataString(e.Id));
            AddPanel("runbooks", "Runbook Executions", Executions);
            StackPanel requests = new StackPanel();
            requests.Add(RequestSummary, 6);
            RequestChart.Kind = ChartKindEnum.Bar;
            RequestChart.Title = "Buckets";
            requests.Add(RequestChart, null);
            AddPanel("requests", "Request Summary", requests);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(Deployment entity)
        {
            return entity.Title;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(Deployment entity)
        {
            return new[] { entity.Status.ToString(), entity.VerificationStatus.ToString() };
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            if (!Context.Session.IsTenantAdmin) return;
            DeploymentUpsertRequest? prefill = NavigationPrefill.Take<DeploymentUpsertRequest>(Context, PrefillSlots.CreateDeployment);
            DeploymentForms.Open(Context, null, prefill, Created);
        }

        /// <inheritdoc />
        protected override void Populate(Deployment d)
        {
            Overview.Reset();
            Overview.Section("Overview");
            Overview.Row("Status", EntityUi.Badge(Context, d.Status.ToString()), t => StatusBadge.Style(d.Status, t));
            Overview.Row("Verification", EntityUi.Badge(Context, d.VerificationStatus.ToString()), t => StatusBadge.Style(d.VerificationStatus, t));
            Overview.Row("Workflow Profile", !String.IsNullOrEmpty(d.WorkflowProfileId) ? EntityLookups.Name(_Profiles, d.WorkflowProfileId) : T("Resolved default"));
            Overview.Link("Environment", !String.IsNullOrEmpty(d.EnvironmentId) ? EntityLookups.Name(_Environments, d.EnvironmentId, d.EnvironmentName ?? "-") : EntityUi.Dash(d.EnvironmentName),
                !String.IsNullOrEmpty(d.EnvironmentId) ? () => Context.Navigate("/environments/" + d.EnvironmentId) : (Action?)null);
            Overview.Row("Check Runs", d.CheckRunIds.Count.ToString(CultureInfo.InvariantCulture));
            Overview.Row("Created", EntityUi.Date(Context, d.CreatedUtc));
            Overview.Row("Started", EntityUi.When(Context, d.StartedUtc));
            Overview.Row("Completed", EntityUi.When(Context, d.CompletedUtc));
            Overview.Row("Approved By", EntityUi.Dash(d.ApprovedByUserId));
            Overview.Row("Monitoring Window", EntityUi.When(Context, d.MonitoringWindowEndsUtc));
            Overview.Row("Last Monitored", EntityUi.When(Context, d.LastMonitoredUtc));
            Overview.Row("Regression Alerts", d.MonitoringFailureCount.ToString(CultureInfo.InvariantCulture), d.MonitoringFailureCount > 0 ? (Func<Armada.Tui.Theming.ArmadaTheme, TUIKit.CellStyle>)(t => t.Error) : null);
            Overview.Row("Last Alert", EntityUi.When(Context, d.LastRegressionAlertUtc));
            Overview.Section("Deployment");
            Overview.Link("Vessel", EntityLookups.Name(_Vessels, d.VesselId), !String.IsNullOrEmpty(d.VesselId) ? () => Context.Navigate("/vessels/" + d.VesselId) : (Action?)null);
            Overview.Row("Environment Name", EntityUi.Dash(d.EnvironmentName));
            Overview.Link("Release", !String.IsNullOrEmpty(d.ReleaseId) ? EntityLookups.Name(_Releases, d.ReleaseId) : T("No linked release"), !String.IsNullOrEmpty(d.ReleaseId) ? () => Context.Navigate("/releases/" + d.ReleaseId) : (Action?)null);
            Overview.Row("Source Ref", String.IsNullOrEmpty(d.SourceRef) ? T("No source ref") : d.SourceRef);
            Overview.Row("Approval required", d.ApprovalRequired ? T("Yes") : T("No"));
            Overview.Row("Title", d.Title);
            Overview.Row("Summary", EntityUi.Dash(d.Summary));
            Overview.Row("Notes", EntityUi.Dash(d.Notes));
            Overview.Section("Verification Monitoring");
            Overview.Row("Latest monitoring summary", String.IsNullOrEmpty(d.LatestMonitoringSummary) ? T("No rollout monitoring summary has been recorded yet for this deployment.") : d.LatestMonitoringSummary);
            Overview.Section("Identifiers");
            Overview.Row("Deployment ID", d.Id, t => t.Code);
            if (!String.IsNullOrEmpty(d.ReleaseId)) Overview.Link("Release", d.ReleaseId, () => Context.Navigate("/releases/" + d.ReleaseId));
            if (!String.IsNullOrEmpty(d.MissionId)) Overview.Link("Mission", d.MissionId, () => Context.Navigate("/missions/" + d.MissionId));
            if (!String.IsNullOrEmpty(d.VoyageId)) Overview.Link("Voyage", d.VoyageId, () => Context.Navigate("/voyages/" + d.VoyageId));

            ChecksView.Reset();
            ChecksView.Section("Linked Checks");
            if (d.CheckRunIds.Count == 0) ChecksView.Row("Checks", T("No linked checks"));
            foreach (string checkId in d.CheckRunIds)
            {
                string id = checkId;
                ChecksView.Link(CheckRole(d, id), id, () => Context.Navigate("/checks/" + id));
            }

            Executions.SetLocalRows(_Executions);

            RequestSummary.Reset();
            RequestChart.Series.Clear();
            RequestChart.Labels.Clear();
            RequestHistorySummaryResult? summary = d.RequestHistorySummary;
            if (summary == null)
            {
                RequestSummary.Row("Request Summary", T("No request-history evidence is recorded yet for this deployment."));
            }
            else
            {
                RequestSummary.Row("Total Requests", EntityUi.Number(Context, summary.TotalCount));
                RequestSummary.Row("Success Rate", summary.SuccessRate.ToString("0.##", CultureInfo.InvariantCulture) + "%");
                RequestSummary.Row("Average Duration", Math.Round(summary.AverageDurationMs).ToString(CultureInfo.InvariantCulture) + " ms");
                RequestSummary.Row("Buckets", summary.Buckets.Count.ToString(CultureInfo.InvariantCulture));
                ChartSeries series = new ChartSeries("Total Requests", summary.Buckets.Select(b => (double)b.TotalCount));
                RequestChart.Series.Add(series);
                foreach (RequestHistorySummaryBucket b in summary.Buckets) RequestChart.Labels.Add(b.BucketStartUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture));
            }
        }

        #endregion

        #region Private-Methods

        private async Task<List<RunbookExecution>> SafeExecutions(string id, CancellationToken token)
        {
            try
            {
                EnumerationResult<RunbookExecution>? result = await Context.Client.ListRunbookExecutionsAsync(new RunbookExecutionQuery { DeploymentId = id, PageSize = 500 }, token).ConfigureAwait(false);
                return result?.Objects ?? new List<RunbookExecution>();
            }
            catch (ArmadaApiException)
            {
                return new List<RunbookExecution>();
            }
        }

        private static string CheckRole(Deployment d, string id)
        {
            if (id == d.DeployCheckRunId) return "Deploy";
            if (id == d.SmokeTestCheckRunId) return "SmokeTest";
            if (id == d.HealthCheckRunId) return "HealthCheck";
            if (id == d.DeploymentVerificationCheckRunId) return "DeploymentVerification";
            if (id == d.RollbackCheckRunId) return "Rollback";
            if (id == d.RollbackVerificationCheckRunId) return "RollbackVerification";
            return "Check";
        }

        private void Created(Deployment created)
        {
            Context.Navigate("/deployments/" + created.Id);
        }

        private void Decide(string action)
        {
            Deployment? d = Entity;
            if (d == null) return;
            string title;
            string message;
            switch (action)
            {
                case "approve":
                    title = "Approve Deployment";
                    message = EntityUi.T(Context, "Approve and execute \"{{title}}\"?", "title", d.Title);
                    break;
                case "deny":
                    title = "Deny Deployment";
                    message = EntityUi.T(Context, "Deny \"{{title}}\" without executing it?", "title", d.Title);
                    break;
                case "verify":
                    title = "Run Verification";
                    message = EntityUi.T(Context, "Re-run post-deploy verification for \"{{title}}\"?", "title", d.Title);
                    break;
                default:
                    title = "Rollback Deployment";
                    message = EntityUi.T(Context, "Run rollback for \"{{title}}\"?", "title", d.Title);
                    break;
            }

            Context.Confirm(title, message, () =>
            {
                EntityUi.Run<Deployment?>(Context, ct =>
                {
                    if (action == "approve") return Context.Client.ApproveDeploymentAsync(d.Id, null, ct);
                    if (action == "deny") return Context.Client.DenyDeploymentAsync(d.Id, null, ct);
                    if (action == "verify") return Context.Client.VerifyDeploymentAsync(d.Id, ct);
                    return Context.Client.RollbackDeploymentAsync(d.Id, ct);
                }, updated =>
                {
                    if (updated != null)
                    {
                        Entity = updated;
                        Populate(updated);
                        EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Deployment \"{{title}}\" updated.", "title", updated.Title));
                    }

                    Reload();
                }, "Action failed.");
            }, title);
        }

        private void RequestDelete()
        {
            Deployment? d = Entity;
            if (d == null) return;
            Context.Confirm("Delete Deployment", EntityUi.T(Context, "Delete \"{{title}}\"? This removes only the deployment record.", "title", d.Title), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteDeploymentAsync(d.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Deployment \"{{title}}\" deleted.", "title", d.Title));
                    Context.Navigate("/delivery?tab=deployments");
                }, "Delete failed.");
            }, "Delete");
        }

        private void OpenRunCheck()
        {
            Deployment? d = Entity;
            if (d == null) return;
            CheckRunRequest prefill = new CheckRunRequest();
            prefill.VesselId = d.VesselId ?? "";
            prefill.WorkflowProfileId = d.WorkflowProfileId;
            prefill.DeploymentId = d.Id;
            prefill.MissionId = d.MissionId;
            prefill.VoyageId = d.VoyageId;
            prefill.EnvironmentName = d.EnvironmentName;
            prefill.Type = CheckRunTypeEnum.DeploymentVerification;
            prefill.Label = d.Title + " verification";
            NavigationPrefill.Set(Context, PrefillSlots.RunCheck, prefill, "/delivery?tab=checks");
        }

        private void OpenIncidentCreate()
        {
            Deployment? d = Entity;
            if (d == null) return;
            IncidentUpsertRequest prefill = new IncidentUpsertRequest();
            prefill.Title = d.Title + " Incident";
            prefill.Summary = d.LatestMonitoringSummary ?? d.Summary;
            prefill.VesselId = d.VesselId;
            prefill.EnvironmentId = d.EnvironmentId;
            prefill.EnvironmentName = d.EnvironmentName;
            prefill.DeploymentId = d.Id;
            prefill.ReleaseId = d.ReleaseId;
            prefill.MissionId = d.MissionId;
            prefill.VoyageId = d.VoyageId;
            prefill.Severity = d.Status == DeploymentStatusEnum.VerificationFailed || d.Status == DeploymentStatusEnum.Failed ? IncidentSeverityEnum.High : IncidentSeverityEnum.Medium;
            NavigationPrefill.Set(Context, PrefillSlots.CreateIncident, prefill, "/incidents/new");
        }

        private void OpenRunbooks()
        {
            Deployment? d = Entity;
            if (d == null) return;
            RunbookExecutionStartRequest prefill = new RunbookExecutionStartRequest();
            prefill.Title = d.Title + " Runbook";
            prefill.WorkflowProfileId = d.WorkflowProfileId;
            prefill.EnvironmentId = d.EnvironmentId;
            prefill.EnvironmentName = d.EnvironmentName;
            prefill.DeploymentId = d.Id;
            prefill.CheckType = d.Status == DeploymentStatusEnum.RolledBack ? CheckRunTypeEnum.RollbackVerification : CheckRunTypeEnum.DeploymentVerification;
            prefill.Notes = d.Summary ?? d.Notes;
            NavigationPrefill.Set(Context, PrefillSlots.RunbookExecution, prefill, "/delivery?tab=runbooks");
        }

        private void SyncGitHubActions()
        {
            Deployment? d = Entity;
            if (d == null || String.IsNullOrEmpty(d.VesselId) || _Syncing) return;
            _Syncing = true;
            EntityUi.Toast(Context, NotificationSeverityEnum.Info, T("Syncing GitHub..."));
            EntityUi.Run<GitHubActionsSyncResult?>(Context, ct => Context.Client.SyncGitHubActionsAsync(new GitHubActionsSyncRequest
            {
                VesselId = d.VesselId,
                WorkflowProfileId = d.WorkflowProfileId,
                DeploymentId = d.Id,
                EnvironmentName = d.EnvironmentName,
                BranchName = d.SourceRef,
                RunCount = 20
            }, ct), result =>
            {
                _Syncing = false;
                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "GitHub Actions sync complete: {{created}} created, {{updated}} updated.", "created", result?.CreatedCount ?? 0, "updated", result?.UpdatedCount ?? 0));
                Reload();
            }, "GitHub Actions sync failed.", ex => _Syncing = false);
        }

        #endregion
    }
}
