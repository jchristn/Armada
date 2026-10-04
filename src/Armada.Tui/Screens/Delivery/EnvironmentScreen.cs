namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Environment detail (dashboard <c>EnvironmentDetail.tsx</c>, route <c>/environments/:id</c>, and
    /// <c>/environments/new</c> for create with optional <c>?vesselId=</c>, <c>?kind=</c>, <c>?name=</c>): actions Deploy,
    /// Run Check, Create Incident, Runbook (each hands a prefill to the target screen), Open Workspace, View JSON,
    /// Duplicate, Delete, and Save; panels Overview (vessel, kind, default, approval, status, health, verification
    /// definition count, monitoring window and interval, regression alerts, the text fields, identifiers) and
    /// Environment (the full editor for tenant admins: every field, rollout monitoring window and interval, record
    /// regression alerts, and the reusable verification definitions list).
    /// </summary>
    public class EnvironmentScreen : EntityDetailScreen<DeploymentEnvironment>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Environment"; }
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
        /// Editor form (null for users who cannot manage environments).
        /// </summary>
        public EntityForm? Editor { get; private set; } = null;

        /// <summary>
        /// Verification definitions list in the editor.
        /// </summary>
        public RecordListField<DeploymentVerificationDefinition>? Definitions { get; private set; } = null;

        #endregion

        #region Private-Members

        private List<Vessel> _Vessels = new List<Vessel>();
        private SelectField<string>? _Vessel = null;
        private InputField? _Name = null;
        private SelectField<string>? _Kind = null;
        private InputField? _ConfigurationSource = null;
        private InputField? _BaseUrl = null;
        private InputField? _HealthEndpoint = null;
        private TextAreaField? _Description = null;
        private TextAreaField? _AccessNotes = null;
        private TextAreaField? _DeploymentRules = null;
        private InputField? _WindowMinutes = null;
        private InputField? _IntervalSeconds = null;
        private CheckField? _AlertOnRegression = null;
        private CheckField? _RequiresApproval = null;
        private CheckField? _IsDefault = null;
        private CheckField? _Active = null;
        private bool _Saving = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public EnvironmentScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Save the editor (create or update).
        /// </summary>
        public void Save()
        {
            if (Editor == null || _Saving || !Context.Session.IsTenantAdmin) return;
            if (!Editor.View.ValidateAll()) return;
            DeploymentEnvironmentUpsertRequest payload = BuildPayload();
            bool create = IsCreateMode;
            _Saving = true;
            EntityUi.Run<DeploymentEnvironment?>(Context, ct => create
                ? Context.Client.CreateEnvironmentAsync(payload, ct)
                : Context.Client.UpdateEnvironmentAsync(EntityId, payload, ct), saved =>
            {
                _Saving = false;
                if (saved == null) return;
                if (create)
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Environment \"{{name}}\" created.", "name", saved.Name));
                    Context.Navigate("/environments/" + saved.Id);
                    return;
                }

                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Environment \"{{name}}\" saved.", "name", saved.Name));
                Entity = saved;
                Populate(saved);
            }, "Save failed.", ex => _Saving = false);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Environment"; }
        }

        /// <inheritdoc />
        protected override Task<DeploymentEnvironment?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetEnvironmentAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(DeploymentEnvironment entity, CancellationToken token)
        {
            _Vessels = await EntityLookups.VesselsAsync(Context.Client, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            Func<bool> manage = () => Context.Session.IsTenantAdmin;
            Func<bool> loaded = () => Entity != null && !IsCreateMode;
            AddAction("deploy", "Deploy", OpenDeploy, loaded);
            AddAction("run-check", "Run Check", OpenRunCheck, loaded);
            AddAction("create-incident", "Create Incident", OpenIncident, loaded);
            AddAction("runbook", "Runbook", OpenRunbook, loaded);
            AddAction("workspace", "Open Workspace", () => { if (!String.IsNullOrEmpty(Entity?.VesselId)) Context.Navigate("/workspace/" + Entity!.VesselId); }, () => loaded() && !String.IsNullOrEmpty(Entity?.VesselId) && _Vessels.Any(v => v.Id == Entity!.VesselId));
            AddJsonAction();
            AddAction("duplicate", "Duplicate", () => { if (Entity != null) EnvironmentForms.Duplicate(Context, Entity); }, () => loaded() && manage());
            AddAction("save", "Save Environment", Save, () => manage() && (IsCreateMode || Entity != null), "ctrl+s");
            AddAction("delete", "Delete", RequestDelete, () => loaded() && manage(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            if (!IsCreateMode) AddPanel("overview", "Overview", Overview);
            if (!Context.Session.IsTenantAdmin) return;
            EntityForm form = new EntityForm(Context);
            Editor = form;
            form.Section("Environment");
            _Vessel = form.Select("Vessel", new List<SelectOption<string>>(), "", "Select a vessel");
            _Name = form.Text("Name", "Environment", "", true);
            _Kind = form.Enum("Kind", EnvironmentKindEnum.Development);
            _ConfigurationSource = form.Text("Configuration Source", "", Context.Loc.T("e.g. Helm values, appsettings.Production.json, Azure slot config"));
            _BaseUrl = form.Text("Base URL", "", "https://service.example.com");
            _HealthEndpoint = form.Text("Health Endpoint", "", "/health or https://service.example.com/health");
            _Description = form.Area("Description", "", 3);
            _AccessNotes = form.Area("Access Notes", "", 4);
            _AccessNotes.Placeholder = "How do operators reach or authenticate to this environment?";
            _DeploymentRules = form.Area("Deployment Rules", "", 4);
            _DeploymentRules.Placeholder = "Document freeze windows, approval policy, maintenance constraints, or rollout notes.";
            form.Section("Verification & Monitoring");
            _WindowMinutes = form.Number("Rollout Monitoring Window (minutes)", 60, 0, 100000);
            _IntervalSeconds = form.Number("Monitoring Interval (seconds)", 300, 30, 100000);
            _AlertOnRegression = form.Check("Record regression alerts during rollout monitoring", true);
            Definitions = form.List<DeploymentVerificationDefinition>("Reusable Verification Definitions", VerificationDefinitionEditor.Describe, null, 6);
            VerificationDefinitionEditor.Attach(Context, Definitions);
            _RequiresApproval = form.Check("Requires approval", false);
            _IsDefault = form.Check("Default environment for vessel", false);
            _Active = form.Check("Active", true);
            form.View.SaveButton.Label = IsCreateMode ? "Create Environment" : "Save Environment";
            form.View.DiscardButton.Label = "Discard";
            form.View.SaveRequested += (s, e) => Save();
            form.View.DiscardRequested += (s, e) =>
            {
                if (Entity != null) FillEditor(Entity);
            };
            AddPanel("edit", "Environment", form.View);
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            if (Editor == null) return;
            string? kind = Route.Query.TryGetValue("kind", out string? k) ? k : null;
            string? name = Route.Query.TryGetValue("name", out string? n) ? n : null;
            string? vesselId = Route.Query.TryGetValue("vesselId", out string? v) ? v : null;
            if (!String.IsNullOrEmpty(name)) _Name!.Value = name!;
            if (!String.IsNullOrEmpty(kind) && Enum.TryParse<EnvironmentKindEnum>(kind, true, out EnvironmentKindEnum parsed)) _Kind!.SetValue(parsed.ToString());
            Editor.MarkClean();
            EntityUi.Run(Context, ct => EntityLookups.VesselsAsync(Context.Client, ct), vessels =>
            {
                _Vessels = vessels;
                Editor.SetOptions(_Vessel!, EntityLookups.Options(vessels, x => x.Id, x => x.Name), "Select a vessel");
                if (!String.IsNullOrEmpty(vesselId)) _Vessel!.SetValue(vesselId);
                Editor.MarkClean();
            }, "Failed to load vessels.");
        }

        /// <inheritdoc />
        protected override string HeaderTitle(DeploymentEnvironment entity)
        {
            return entity.Name;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(DeploymentEnvironment entity)
        {
            return new[] { entity.Active ? "Active" : "Inactive" };
        }

        /// <inheritdoc />
        protected override void Populate(DeploymentEnvironment e)
        {
            Dictionary<string, string> vessels = _Vessels.ToDictionary(v => v.Id, v => v.Name);
            Overview.Reset();
            Overview.Section("Overview");
            Overview.Link("Vessel", EntityLookups.Name(vessels, e.VesselId), !String.IsNullOrEmpty(e.VesselId) ? () => Context.Navigate("/vessels/" + e.VesselId) : (Action?)null);
            Overview.Row("Kind", e.Kind.ToString());
            Overview.Row("Default", T(e.IsDefault ? "Yes" : "No"));
            Overview.Row("Approval", T(e.RequiresApproval ? "Required" : "Not required"));
            Overview.Row("Status", T(e.Active ? "Active" : "Inactive"), t => e.Active ? t.Success : t.Muted);
            Overview.Row("Health", EntityUi.Dash(e.HealthEndpoint));
            Overview.Row("Base URL", EntityUi.Dash(e.BaseUrl));
            Overview.Row("Configuration Source", EntityUi.Dash(e.ConfigurationSource));
            Overview.Row("Verification Definitions", e.VerificationDefinitions.Count.ToString(CultureInfo.InvariantCulture));
            Overview.Row("Monitoring Window", e.RolloutMonitoringWindowMinutes.ToString(CultureInfo.InvariantCulture) + " " + T("minutes"));
            Overview.Row("Monitoring Interval", e.RolloutMonitoringIntervalSeconds.ToString(CultureInfo.InvariantCulture) + " " + T("seconds"));
            Overview.Row("Regression Alerts", T(e.AlertOnRegression ? "Enabled" : "Disabled"));
            Overview.Section("Environment");
            Overview.Row("Description", EntityUi.Dash(e.Description));
            Overview.Row("Access Notes", EntityUi.Dash(e.AccessNotes));
            Overview.Row("Deployment Rules", EntityUi.Dash(e.DeploymentRules));
            Overview.Section("Reusable Verification Definitions");
            if (e.VerificationDefinitions.Count == 0) Overview.Row("Verification", T("No reusable verification definitions are configured for this environment yet."));
            foreach (DeploymentVerificationDefinition d in e.VerificationDefinitions) Overview.Row(d.Name, VerificationDefinitionEditor.Describe(d));
            Overview.Section("Identifiers");
            Overview.Row("Environment ID", e.Id, t => t.Code);
            Overview.Row("Created", EntityUi.When(Context, e.CreatedUtc) + "  (" + EntityUi.Date(Context, e.CreatedUtc) + ")");
            Overview.Row("Last Updated", EntityUi.When(Context, e.LastUpdateUtc) + "  (" + EntityUi.Date(Context, e.LastUpdateUtc) + ")");
            FillEditor(e);
        }

        #endregion

        #region Private-Methods

        private void FillEditor(DeploymentEnvironment e)
        {
            if (Editor == null) return;
            Editor.SetOptions(_Vessel!, EntityLookups.Options(_Vessels, v => v.Id, v => v.Name), "Select a vessel");
            _Vessel!.SetValue(e.VesselId ?? "");
            _Name!.Value = e.Name;
            _Kind!.SetValue(e.Kind.ToString());
            _ConfigurationSource!.Value = e.ConfigurationSource ?? "";
            _BaseUrl!.Value = e.BaseUrl ?? "";
            _HealthEndpoint!.Value = e.HealthEndpoint ?? "";
            _Description!.Value = e.Description ?? "";
            _AccessNotes!.Value = e.AccessNotes ?? "";
            _DeploymentRules!.Value = e.DeploymentRules ?? "";
            _WindowMinutes!.Value = (e.RolloutMonitoringWindowMinutes > 0 ? e.RolloutMonitoringWindowMinutes : 60).ToString(CultureInfo.InvariantCulture);
            _IntervalSeconds!.Value = (e.RolloutMonitoringIntervalSeconds > 0 ? e.RolloutMonitoringIntervalSeconds : 300).ToString(CultureInfo.InvariantCulture);
            _AlertOnRegression!.SetValue(e.AlertOnRegression, false);
            Definitions!.SetItems(e.VerificationDefinitions.Select(d => VerificationDefinitionEditor.Clone(d, false)));
            _RequiresApproval!.SetValue(e.RequiresApproval, false);
            _IsDefault!.SetValue(e.IsDefault, false);
            _Active!.SetValue(e.Active, false);
            Editor.MarkClean();
        }

        private DeploymentEnvironmentUpsertRequest BuildPayload()
        {
            return new DeploymentEnvironmentUpsertRequest
            {
                VesselId = EntityUi.Blank(_Vessel!.Value),
                Name = EntityUi.Blank(_Name!.Value),
                Description = EntityUi.Blank(_Description!.Value),
                Kind = EntityForm.EnumValue(_Kind!, EnvironmentKindEnum.Development),
                ConfigurationSource = EntityUi.Blank(_ConfigurationSource!.Value),
                BaseUrl = EntityUi.Blank(_BaseUrl!.Value),
                HealthEndpoint = EntityUi.Blank(_HealthEndpoint!.Value),
                AccessNotes = EntityUi.Blank(_AccessNotes!.Value),
                DeploymentRules = EntityUi.Blank(_DeploymentRules!.Value),
                VerificationDefinitions = Definitions!.Items.ToList(),
                RolloutMonitoringWindowMinutes = Math.Max(0, EntityForm.IntValue(_WindowMinutes!) ?? 0),
                RolloutMonitoringIntervalSeconds = Math.Max(30, EntityForm.IntValue(_IntervalSeconds!) ?? 30),
                AlertOnRegression = _AlertOnRegression!.Value,
                RequiresApproval = _RequiresApproval!.Value,
                IsDefault = _IsDefault!.Value,
                Active = _Active!.Value
            };
        }

        private void RequestDelete()
        {
            DeploymentEnvironment? e = Entity;
            if (e == null) return;
            Context.Confirm("Delete Environment", EntityUi.T(Context, "Delete \"{{name}}\"? This removes only the environment record.", "name", e.Name), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteEnvironmentAsync(e.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Environment \"{{name}}\" deleted.", "name", e.Name));
                    Context.Navigate("/delivery?tab=environments");
                }, "Delete failed.");
            }, "Delete");
        }

        private void OpenDeploy()
        {
            DeploymentEnvironment? e = Entity;
            if (e == null) return;
            DeploymentUpsertRequest prefill = new DeploymentUpsertRequest();
            prefill.VesselId = EntityUi.Blank(e.VesselId);
            prefill.EnvironmentId = e.Id;
            prefill.EnvironmentName = e.Name;
            prefill.Title = e.Name + " Deploy";
            NavigationPrefill.Set(Context, PrefillSlots.CreateDeployment, prefill, "/deployments/new");
        }

        private void OpenRunCheck()
        {
            DeploymentEnvironment? e = Entity;
            if (e == null) return;
            CheckRunRequest prefill = new CheckRunRequest();
            prefill.VesselId = e.VesselId ?? "";
            prefill.Label = e.Name;
            prefill.EnvironmentName = e.Name;
            NavigationPrefill.Set(Context, PrefillSlots.RunCheck, prefill, "/delivery?tab=checks");
        }

        private void OpenIncident()
        {
            DeploymentEnvironment? e = Entity;
            if (e == null) return;
            IncidentUpsertRequest prefill = new IncidentUpsertRequest();
            prefill.VesselId = EntityUi.Blank(e.VesselId);
            prefill.EnvironmentId = e.Id;
            prefill.EnvironmentName = e.Name;
            prefill.Title = e.Name + " Incident";
            prefill.Severity = e.Kind == EnvironmentKindEnum.Production ? IncidentSeverityEnum.High : IncidentSeverityEnum.Medium;
            NavigationPrefill.Set(Context, PrefillSlots.CreateIncident, prefill, "/incidents/new");
        }

        private void OpenRunbook()
        {
            DeploymentEnvironment? e = Entity;
            if (e == null) return;
            RunbookExecutionStartRequest prefill = new RunbookExecutionStartRequest();
            prefill.EnvironmentId = e.Id;
            prefill.EnvironmentName = e.Name;
            prefill.CheckType = e.VerificationDefinitions.Count > 0 ? CheckRunTypeEnum.DeploymentVerification : CheckRunTypeEnum.HealthCheck;
            NavigationPrefill.Set(Context, PrefillSlots.RunbookExecution, prefill, "/delivery?tab=runbooks");
        }

        #endregion
    }
}
