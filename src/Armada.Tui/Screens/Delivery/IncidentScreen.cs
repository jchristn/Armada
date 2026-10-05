namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
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
    /// Incident detail (dashboard <c>IncidentDetail.tsx</c>, route <c>/incidents/:id</c>, and <c>/incidents/new</c> for
    /// create, which takes a <see cref="PrefillSlots.CreateIncident"/> hand-off): actions Plan Hotfix, Dispatch Hotfix,
    /// Runbook, Rollback Deployment (confirmed; rolls back and records it on the incident), Open Deployment, Open
    /// Environment, View JSON, Edit, and Delete; panels Overview (every field, failure kind, rescue attempts and
    /// missions, identifiers with links) and Runbook Executions. Reloads live on <c>incident.changed</c>.
    /// </summary>
    public class IncidentScreen : EntityDetailScreen<Incident>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Incident"; }
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
        /// Runbook executions panel.
        /// </summary>
        public ArmadaGrid<RunbookExecution> Executions { get; } = new ArmadaGrid<RunbookExecution>(e => e.Id);

        #endregion

        #region Private-Members

        private List<RunbookExecution> _Executions = new List<RunbookExecution>();
        private List<Vessel> _Vessels = new List<Vessel>();
        private List<DeploymentEnvironment> _Environments = new List<DeploymentEnvironment>();
        private List<Deployment> _Deployments = new List<Deployment>();
        private List<Release> _Releases = new List<Release>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public IncidentScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The prompt the dashboard hands to planning and dispatch for a hotfix.
        /// </summary>
        /// <param name="incident">Incident.</param>
        /// <returns>Prompt.</returns>
        public static string HotfixPrompt(Incident incident)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("Investigate and mitigate the incident \"").Append(incident.Title).Append("\".\n\n");
            if (!String.IsNullOrEmpty(incident.Summary)) sb.Append("Summary: ").Append(incident.Summary).Append('\n');
            if (!String.IsNullOrEmpty(incident.Impact)) sb.Append("Impact: ").Append(incident.Impact).Append('\n');
            if (!String.IsNullOrEmpty(incident.EnvironmentName)) sb.Append("Environment: ").Append(incident.EnvironmentName).Append('\n');
            if (!String.IsNullOrEmpty(incident.DeploymentId)) sb.Append("Deployment: ").Append(incident.DeploymentId).Append('\n');
            if (!String.IsNullOrEmpty(incident.ReleaseId)) sb.Append("Release: ").Append(incident.ReleaseId).Append('\n');
            sb.Append('\n').Append("Produce a concrete hotfix plan and, if appropriate, implementation scope for the linked vessel.");
            return sb.ToString();
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Incident"; }
        }

        /// <inheritdoc />
        protected override IEnumerable<string> LiveEvents
        {
            get { return new[] { ArmadaEventTypes.IncidentChanged }; }
        }

        /// <inheritdoc />
        protected override Task<Incident?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetIncidentAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(Incident entity, CancellationToken token)
        {
            Task<List<RunbookExecution>> executions = SafeExecutions(entity.Id, token);
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, token);
            Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(Context.Client, token);
            Task<List<Deployment>> deployments = EntityLookups.DeploymentsAsync(Context.Client, token);
            Task<List<Release>> releases = EntityLookups.ReleasesAsync(Context.Client, token);
            await Task.WhenAll(executions, vessels, environments, deployments, releases).ConfigureAwait(false);
            _Executions = executions.Result;
            _Vessels = vessels.Result;
            _Environments = environments.Result;
            _Deployments = deployments.Result;
            _Releases = releases.Result;
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            Func<bool> manage = () => Context.Session.IsTenantAdmin;
            AddAction("create", "Create Incident", () => IncidentForms.Open(Context, null, NavigationPrefill.Take<IncidentUpsertRequest>(Context, PrefillSlots.CreateIncident), true, Created), () => IsCreateMode && manage());
            AddAction("plan-hotfix", "Plan Hotfix", () => Context.Navigate("/planning"), () => HotfixVessel() != null);
            AddAction("dispatch-hotfix", "Dispatch Hotfix", () => Context.Navigate("/dispatch"), () => HotfixVessel() != null);
            AddAction("runbook", "Runbook", OpenRunbooks, () => Entity != null);
            AddAction("rollback", "Rollback Deployment", Rollback, () => !String.IsNullOrEmpty(Entity?.DeploymentId));
            AddAction("open-deployment", "Open Deployment", () => { if (Entity?.DeploymentId != null) Context.Navigate("/deployments/" + Entity.DeploymentId); }, () => !String.IsNullOrEmpty(Entity?.DeploymentId));
            AddAction("open-environment", "Open Environment", () => { if (Entity?.EnvironmentId != null) Context.Navigate("/environments/" + Entity.EnvironmentId); }, () => !String.IsNullOrEmpty(Entity?.EnvironmentId));
            AddJsonAction();
            AddAction("edit", "Edit", () => { if (Entity != null) IncidentForms.Open(Context, Entity, null, true, i => Reload()); }, () => Entity != null && manage(), "e");
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && manage(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("overview", "Overview", Overview);
            Executions.Dispatcher = Context.Dispatcher;
            Executions.ModalHost = Context.Modals;
            Executions.MultiSelect = false;
            Executions.EmptyText = "No runbook executions are linked to this incident yet.";
            Executions.AddColumn(new GridColumn<RunbookExecution>("title", "Title", e => e.Title) { Weight = 3, Sortable = true });
            Executions.AddColumn(new GridColumn<RunbookExecution>("status", "Status", e => StatusBadge.Label(e.Status)) { Width = 14, Sortable = true, Style = (e, t) => StatusBadge.Style(e.Status, t) });
            Executions.AddColumn(new GridColumn<RunbookExecution>("environment", "Environment", e => String.IsNullOrEmpty(e.EnvironmentName) ? T("No environment") : e.EnvironmentName!) { Weight = 2 });
            Executions.AddColumn(new GridColumn<RunbookExecution>("checkType", "Check Type", e => e.CheckType.HasValue ? e.CheckType.Value.ToString() : T("No check type")) { Weight = 2 });
            Executions.AddColumn(new GridColumn<RunbookExecution>("steps", "steps complete", e => e.CompletedStepIds.Count.ToString(CultureInfo.InvariantCulture)) { Width = 15, Align = TUIKit.Widgets.CellAlignment.Right });
            Executions.AddColumn(new GridColumn<RunbookExecution>("updated", "Last updated", e => EntityUi.When(Context, e.LastUpdateUtc)) { Width = 13 });
            Executions.Activated += (s, e) => Context.Navigate("/runbooks/" + e.RunbookId + "?executionId=" + Uri.EscapeDataString(e.Id));
            AddPanel("runbooks", "Runbook Executions", Executions);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(Incident entity)
        {
            return entity.Title;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(Incident entity)
        {
            return new[] { entity.Status.ToString(), entity.Severity.ToString() };
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            if (!Context.Session.IsTenantAdmin) return;
            IncidentUpsertRequest? prefill = NavigationPrefill.Take<IncidentUpsertRequest>(Context, PrefillSlots.CreateIncident);
            IncidentForms.Open(Context, null, prefill, true, Created);
        }

        /// <inheritdoc />
        protected override void Populate(Incident i)
        {
            Dictionary<string, string> vessels = _Vessels.ToDictionary(v => v.Id, v => v.Name);
            Dictionary<string, string> environments = _Environments.ToDictionary(e => e.Id, e => e.Name);
            Overview.Reset();
            Overview.Section("Overview");
            Overview.Row("Status", EntityUi.Badge(Context, i.Status.ToString()), t => StatusBadge.Style(i.Status, t));
            Overview.Row("Severity", i.Severity.ToString(), i.Severity == IncidentSeverityEnum.Critical || i.Severity == IncidentSeverityEnum.High ? (Func<Armada.Tui.Theming.ArmadaTheme, TUIKit.CellStyle>)(t => t.Error) : null);
            Overview.Link("Vessel", EntityLookups.Name(vessels, i.VesselId), !String.IsNullOrEmpty(i.VesselId) ? () => Context.Navigate("/vessels/" + i.VesselId) : (Action?)null);
            Overview.Link("Environment", !String.IsNullOrEmpty(i.EnvironmentId) ? EntityLookups.Name(environments, i.EnvironmentId, i.EnvironmentName ?? "-") : EntityUi.Dash(i.EnvironmentName),
                !String.IsNullOrEmpty(i.EnvironmentId) ? () => Context.Navigate("/environments/" + i.EnvironmentId) : (Action?)null);
            Overview.Row("Environment Name", EntityUi.Dash(i.EnvironmentName));
            Overview.Link("Deployment", EntityUi.Dash(i.DeploymentId), !String.IsNullOrEmpty(i.DeploymentId) ? () => Context.Navigate("/deployments/" + i.DeploymentId) : (Action?)null);
            Overview.Link("Release", EntityUi.Dash(i.ReleaseId), !String.IsNullOrEmpty(i.ReleaseId) ? () => Context.Navigate("/releases/" + i.ReleaseId) : (Action?)null);
            Overview.Link("Mission ID", EntityUi.Dash(i.MissionId), !String.IsNullOrEmpty(i.MissionId) ? () => Context.Navigate("/missions/" + i.MissionId) : (Action?)null);
            Overview.Link("Voyage ID", EntityUi.Dash(i.VoyageId), !String.IsNullOrEmpty(i.VoyageId) ? () => Context.Navigate("/voyages/" + i.VoyageId) : (Action?)null);
            Overview.Link("Rollback Deployment", EntityUi.Dash(i.RollbackDeploymentId), !String.IsNullOrEmpty(i.RollbackDeploymentId) ? () => Context.Navigate("/deployments/" + i.RollbackDeploymentId) : (Action?)null);
            Overview.Row("Detected", EntityUi.Date(Context, i.DetectedUtc));
            Overview.Row("Mitigated", EntityUi.Date(Context, i.MitigatedUtc));
            Overview.Row("Closed", EntityUi.Date(Context, i.ClosedUtc));
            Overview.Row("Last Updated", EntityUi.When(Context, i.LastUpdateUtc));
            if (!String.IsNullOrEmpty(i.FailureKind))
            {
                Overview.Row("Failure Kind", i.FailureKind);
                Overview.Row("Rescue Attempts", i.RecoveryAttempts.ToString(CultureInfo.InvariantCulture));
                Overview.Row("Rescue Missions", i.RescueMissionIds != null && i.RescueMissionIds.Count > 0 ? String.Join(", ", i.RescueMissionIds) : "-");
            }

            Overview.Section("Incident");
            Overview.Row("Title", i.Title);
            Overview.Row("Summary", EntityUi.Dash(i.Summary));
            Overview.Row("Impact", EntityUi.Dash(i.Impact));
            Overview.Row("Root Cause", EntityUi.Dash(i.RootCause));
            Overview.Row("Recovery Notes", EntityUi.Dash(i.RecoveryNotes));
            Overview.Row("Postmortem", EntityUi.Dash(i.Postmortem));
            Overview.Section("Identifiers");
            Overview.Row("Incident ID", i.Id, t => t.Code);
            if (!String.IsNullOrEmpty(i.DeploymentId)) Overview.Link("Deployment", i.DeploymentId, () => Context.Navigate("/deployments/" + i.DeploymentId));
            if (!String.IsNullOrEmpty(i.EnvironmentId)) Overview.Link("Environment", i.EnvironmentId, () => Context.Navigate("/environments/" + i.EnvironmentId));
            if (!String.IsNullOrEmpty(i.ReleaseId)) Overview.Link("Release", i.ReleaseId, () => Context.Navigate("/releases/" + i.ReleaseId));
            Executions.SetLocalRows(_Executions);
        }

        #endregion

        #region Private-Methods

        private string? HotfixVessel()
        {
            Incident? i = Entity;
            if (i == null) return null;
            if (!String.IsNullOrEmpty(i.VesselId)) return i.VesselId;
            return _Environments.FirstOrDefault(e => e.Id == i.EnvironmentId)?.VesselId;
        }

        private async Task<List<RunbookExecution>> SafeExecutions(string id, CancellationToken token)
        {
            try
            {
                EnumerationResult<RunbookExecution>? result = await Context.Client.ListRunbookExecutionsAsync(new RunbookExecutionQuery { IncidentId = id, PageSize = 500 }, token).ConfigureAwait(false);
                return result?.Objects ?? new List<RunbookExecution>();
            }
            catch (ArmadaApiException)
            {
                return new List<RunbookExecution>();
            }
        }

        private void Created(Incident created)
        {
            Context.Navigate("/incidents/" + created.Id);
        }

        private void OpenRunbooks()
        {
            Incident? i = Entity;
            if (i == null) return;
            Deployment? deployment = _Deployments.FirstOrDefault(d => d.Id == i.DeploymentId);
            RunbookExecutionStartRequest prefill = new RunbookExecutionStartRequest();
            prefill.Title = i.Title + " Response";
            prefill.WorkflowProfileId = deployment?.WorkflowProfileId;
            prefill.EnvironmentId = i.EnvironmentId ?? deployment?.EnvironmentId;
            prefill.EnvironmentName = i.EnvironmentName ?? deployment?.EnvironmentName;
            prefill.DeploymentId = EntityUi.Blank(i.DeploymentId);
            prefill.IncidentId = i.Id;
            prefill.CheckType = !String.IsNullOrEmpty(i.DeploymentId) ? CheckRunTypeEnum.DeploymentVerification : CheckRunTypeEnum.Custom;
            prefill.Notes = HotfixPrompt(i);
            NavigationPrefill.Set(Context, PrefillSlots.RunbookExecution, prefill, "/delivery?tab=runbooks");
        }

        private void Rollback()
        {
            Incident? i = Entity;
            if (i == null || String.IsNullOrEmpty(i.DeploymentId)) return;
            string deploymentId = i.DeploymentId!;
            Context.Confirm("Rollback Deployment", EntityUi.T(Context, "Rollback deployment \"{{deploymentId}}\" and attach the result to this incident?", "deploymentId", deploymentId), () =>
            {
                EntityUi.Run<Incident?>(Context, async ct =>
                {
                    await Context.Client.RollbackDeploymentAsync(deploymentId, ct).ConfigureAwait(false);
                    IncidentUpsertRequest update = new IncidentUpsertRequest();
                    update.RollbackDeploymentId = deploymentId;
                    update.Status = IncidentStatusEnum.RolledBack;
                    return await Context.Client.UpdateIncidentAsync(i.Id, update, ct).ConfigureAwait(false);
                }, updated =>
                {
                    if (updated != null)
                    {
                        Entity = updated;
                        Populate(updated);
                    }

                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Rollback started for \"{{deploymentId}}\".", "deploymentId", deploymentId));
                }, "Rollback failed.");
            }, "Rollback Deployment");
        }

        private void RequestDelete()
        {
            Incident? i = Entity;
            if (i == null) return;
            Context.Confirm("Delete Incident", EntityUi.T(Context, "Delete \"{{title}}\"? This removes only the incident record.", "title", i.Title), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteIncidentAsync(i.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Incident \"{{title}}\" deleted.", "title", i.Title));
                    Context.Navigate("/delivery?tab=incidents");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
