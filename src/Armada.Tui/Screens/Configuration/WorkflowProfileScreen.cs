namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Workflow profile detail and editor (dashboard <c>WorkflowProfileDetail.tsx</c>, route
    /// <c>/workflow-profiles/:id</c>, and <c>/workflow-profiles/new</c> with optional <c>scope</c>, <c>fleetId</c>,
    /// and <c>vesselId</c> query values for create): panels Profile (name, scope, fleet, vessel, description, default,
    /// active, language hints, expected artifacts, required inputs, every command from lint through changelog
    /// generation, and per-environment commands; <c>Ctrl+S</c> saves, <c>Esc</c> discards), Validation (valid or needs
    /// attention, available check types, resolved commands, errors, warnings), Vessel Preview (the profile resolved for
    /// a chosen vessel), and Details (identifiers and timestamps). Actions: Validate, Save, Preview for Vessel, View
    /// JSON, Duplicate, Delete. Viewers who cannot edit (scoped visibility) see the dashboard's read-only notice.
    /// </summary>
    public class WorkflowProfileScreen : EntityDetailScreen<WorkflowProfile>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Workflow Profile"; }
        }

        /// <inheritdoc />
        public override bool IsCreateMode
        {
            get { return String.Equals(EntityId, "new", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// The editor form.
        /// </summary>
        public EntityForm Editor { get; }

        /// <summary>
        /// Validation summary.
        /// </summary>
        public LinkDetailView ValidationView { get; } = new LinkDetailView();

        /// <summary>
        /// Resolved commands from the last validation.
        /// </summary>
        public ArmadaGrid<WorkflowProfileCommandPreview> ValidationCommands { get; }

        /// <summary>
        /// Vessel preview summary.
        /// </summary>
        public LinkDetailView PreviewView { get; } = new LinkDetailView();

        /// <summary>
        /// Resolved commands for the previewed vessel.
        /// </summary>
        public ArmadaGrid<WorkflowProfileCommandPreview> PreviewCommands { get; }

        /// <summary>
        /// Identifiers and timestamps.
        /// </summary>
        public LinkDetailView DetailsView { get; } = new LinkDetailView();

        /// <summary>
        /// Last validation result, or null.
        /// </summary>
        public WorkflowProfileValidationResult? Validation { get; private set; } = null;

        /// <summary>
        /// True when the signed-in user may change this profile.
        /// </summary>
        public bool CanManage
        {
            get
            {
                if (IsCreateMode) return true;
                if (Entity != null) return ScopeRules.CanEdit(Context.Session, Entity.OwnershipScope, Entity.TenantId, Entity.UserId);
                return Context.Session.IsTenantAdmin;
            }
        }

        #endregion

        #region Private-Members

        private readonly TextBlock _Notice = new TextBlock("You can view this workflow profile, but only its owner or a tenant administrator can change it.", t => t.Error);
        private readonly InputField _Name;
        private readonly SelectField<string> _Scope;
        private readonly SelectField<string> _Fleet;
        private readonly SelectField<string> _Vessel;
        private readonly InputField _Description;
        private readonly CheckField _IsDefault;
        private readonly CheckField _Active;
        private readonly TextAreaField _LanguageHints;
        private readonly TextAreaField _ExpectedArtifacts;
        private readonly RecordListField<WorkflowInputReference> _Inputs;
        private readonly Dictionary<string, TextAreaField> _Commands = new Dictionary<string, TextAreaField>(StringComparer.Ordinal);
        private readonly RecordListField<WorkflowEnvironmentProfile> _Environments;
        private List<Fleet> _FleetList = new List<Fleet>();
        private List<Vessel> _VesselList = new List<Vessel>();
        private bool _Saving = false;

        private static readonly string[] _CommandLabels = new[]
        {
            "Lint Command", "Build Command", "Unit Test Command", "Integration Test Command", "E2E Test Command", "Migration Command",
            "Security Scan Command", "Performance Command", "Package Command", "Deployment Verification Command",
            "Rollback Verification Command", "Publish Artifact Command", "Release Versioning Command", "Changelog Generation Command"
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public WorkflowProfileScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Editor = new EntityForm(context);
            _Name = Editor.Text("Name", "Default Workflow", "", true);
            _Scope = Editor.Select("Scope", ProfileFormSupport.ScopeOptions(context), "Global", null, true);
            _Fleet = Editor.Select("Fleet", new List<SelectOption<string>>(), null, "Select a fleet...", false, "Used when the scope is Fleet.");
            _Vessel = Editor.Select("Vessel", new List<SelectOption<string>>(), null, "Select a vessel...", false, "Used when the scope is Vessel.");
            _Description = Editor.Text("Description", "");
            _IsDefault = Editor.Check("Default for this scope", false);
            _Active = Editor.Check("Active", true);
            _LanguageHints = Editor.Area("Language / Runtime Hints", "", 3);
            _LanguageHints.Placeholder = "dotnet\nreact\npostgres";
            _ExpectedArtifacts = Editor.Area("Expected Artifacts", "", 3);
            _ExpectedArtifacts.Placeholder = "bin/Release/app.zip\ncoverage/summary.xml";
            Editor.Section("Required Inputs");
            _Inputs = Editor.List<WorkflowInputReference>("Required Inputs", i => WorkflowProfileForms.DescribeInput(context, i), null, 5,
                "Store provider/key references here so Armada can warn before checks run. No secret values are stored in the workflow profile itself.");
            _Inputs.EmptyText = "No required inputs configured.";
            _Inputs.Fingerprint = i => i.Provider + "|" + i.Key + "|" + i.EnvironmentName + "|" + i.Description;
            Editor.Section("Commands");
            foreach (string label in _CommandLabels) _Commands[label] = Editor.Area(label, "", 2, false, null, ".sh");
            Editor.Section("Environment Commands");
            _Environments = Editor.List<WorkflowEnvironmentProfile>("Environment Commands", e => WorkflowProfileForms.DescribeEnvironment(context, e), null, 5);
            _Environments.EmptyText = "No environment-specific commands configured yet.";
            _Environments.Fingerprint = e => e.EnvironmentName + "|" + e.DeployCommand + "|" + e.RollbackCommand + "|" + e.SmokeTestCommand + "|" + e.HealthCheckCommand + "|" + e.DeploymentVerificationCommand + "|" + e.RollbackVerificationCommand;
            _Inputs.Editor = (item, done) => WorkflowProfileForms.EditInput(Context, item, _Environments.Items.Select(e => e.EnvironmentName), done);
            _Environments.Editor = (item, done) => WorkflowProfileForms.EditEnvironment(Context, item, done);
            Editor.View.SaveRequested += (s, e) => Save();
            Editor.View.DiscardRequested += (s, e) =>
            {
                if (Entity != null) FillForm(Entity);
            };
            ValidationCommands = CommandGrid("No resolved commands are available for this workflow profile.");
            PreviewCommands = CommandGrid("No resolved commands available.");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the payload from the editor (the dashboard's <c>buildPayload</c>).
        /// </summary>
        /// <returns>Profile, or null when the name is blank.</returns>
        public WorkflowProfile? BuildPayload()
        {
            if (String.IsNullOrWhiteSpace(_Name.Value)) return null;
            WorkflowProfile payload = Entity != null ? ProfileFormSupport.Clone(Entity) : new WorkflowProfile();
            payload.Name = _Name.Value.Trim();
            payload.Description = EntityUi.Blank(_Description.Value);
            payload.Scope = EntityForm.EnumValue(_Scope, WorkflowProfileScopeEnum.Global);
            payload.FleetId = payload.Scope == WorkflowProfileScopeEnum.Fleet ? EntityUi.Blank(_Fleet.Value) : null;
            payload.VesselId = payload.Scope == WorkflowProfileScopeEnum.Vessel ? EntityUi.Blank(_Vessel.Value) : null;
            payload.IsDefault = _IsDefault.Value;
            payload.Active = _Active.Value;
            payload.LanguageHints = EntityUi.SplitLines(_LanguageHints.Value);
            payload.ExpectedArtifacts = EntityUi.SplitLines(_ExpectedArtifacts.Value);
            payload.LintCommand = Command("Lint Command");
            payload.BuildCommand = Command("Build Command");
            payload.UnitTestCommand = Command("Unit Test Command");
            payload.IntegrationTestCommand = Command("Integration Test Command");
            payload.E2ETestCommand = Command("E2E Test Command");
            payload.MigrationCommand = Command("Migration Command");
            payload.SecurityScanCommand = Command("Security Scan Command");
            payload.PerformanceCommand = Command("Performance Command");
            payload.PackageCommand = Command("Package Command");
            payload.DeploymentVerificationCommand = Command("Deployment Verification Command");
            payload.RollbackVerificationCommand = Command("Rollback Verification Command");
            payload.PublishArtifactCommand = Command("Publish Artifact Command");
            payload.ReleaseVersioningCommand = Command("Release Versioning Command");
            payload.ChangelogGenerationCommand = Command("Changelog Generation Command");
            payload.RequiredInputs = _Inputs.Items.ToList();
            payload.Environments = _Environments.Items.ToList();
            if (Entity == null) payload.OwnershipScope = ScopeRules.ResolveCreateScope(Context.Session, null);
            return payload;
        }

        /// <summary>
        /// Save (create or update).
        /// </summary>
        public void Save()
        {
            if (!CanManage || _Saving) return;
            if (!Editor.View.ValidateAll()) return;
            WorkflowProfile? payload = BuildPayload();
            if (payload == null) return;
            _Saving = true;
            bool create = Entity == null;
            string id = Entity?.Id ?? "";
            EntityUi.Run<WorkflowProfile?>(Context, ct => create
                ? Context.Client.CreateWorkflowProfileAsync(payload, ct)
                : Context.Client.UpdateWorkflowProfileAsync(id, payload, ct), saved =>
            {
                _Saving = false;
                if (saved == null) return;
                if (create)
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Workflow profile \"{{name}}\" created.", "name", saved.Name));
                    Context.Navigate("/workflow-profiles/" + saved.Id);
                    return;
                }

                Entity = saved;
                Populate(saved);
                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Workflow profile \"{{name}}\" saved.", "name", saved.Name));
            }, "Save failed.", ex => _Saving = false);
        }

        /// <summary>
        /// Validate the editor's current values on the server and show the result.
        /// </summary>
        public void Validate()
        {
            if (!Editor.View.ValidateAll()) return;
            WorkflowProfile? payload = BuildPayload();
            if (payload == null) return;
            EntityUi.Run<WorkflowProfileValidationResult?>(Context, ct => Context.Client.ValidateWorkflowProfileAsync(payload, ct), result =>
            {
                if (result == null) return;
                ShowValidation(result);
                EntityUi.Toast(Context, result.IsValid ? NotificationSeverityEnum.Success : NotificationSeverityEnum.Warning,
                    Context.Loc.T(result.IsValid ? "Workflow profile is valid." : "Workflow profile has validation errors."));
            }, "Validation failed.");
        }

        /// <summary>
        /// Show a validation result in the Validation panel.
        /// </summary>
        /// <param name="result">Result.</param>
        public void ShowValidation(WorkflowProfileValidationResult result)
        {
            Validation = result;
            ValidationView.Reset(false);
            ValidationView.Section("Validation");
            string status = result.IsValid ? "Valid" : "Needs Attention";
            ValidationView.Row("Status", StatusBadge.Marker(result.IsValid ? "Passed" : "Failed") + " " + T(status), t => result.IsValid ? t.Success : t.Warning);
            ValidationView.Row("Available Check Types", result.AvailableCheckTypes.Count > 0 ? String.Join(", ", result.AvailableCheckTypes) : T("None"));
            if (result.Errors.Count > 0)
            {
                ValidationView.Section("Errors");
                foreach (string error in result.Errors) ValidationView.Row("Error", error, t => t.Error);
            }

            if (result.Warnings.Count > 0)
            {
                ValidationView.Section("Warnings");
                foreach (string warning in result.Warnings) ValidationView.Row("Warning", warning, t => t.Warning);
            }

            ValidationCommands.SetLocalRows(result.CommandPreviews ?? new List<WorkflowProfileCommandPreview>());
            ShowPanel("validation");
        }

        /// <summary>
        /// Pick a vessel and show how this profile resolves for it.
        /// </summary>
        public void PreviewForVessel()
        {
            if (Entity == null) return;
            List<SelectOption<string>> options = EntityLookups.Options(_VesselList, v => v.Id, v => v.Name);
            PickerModal<string> picker = new PickerModal<string>("Select a vessel...", options, Context.Loc, Context.Theme.Current);
            Context.Modals.Show(picker, result =>
            {
                if (result is SelectOption<string> chosen) PreviewForVessel(chosen.Value, chosen.Label);
            });
        }

        /// <summary>
        /// Show how this profile resolves for a vessel.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="vesselName">Vessel name.</param>
        public void PreviewForVessel(string vesselId, string vesselName)
        {
            if (Entity == null) return;
            string profileId = Entity.Id;
            EntityUi.Run<WorkflowProfileResolutionPreviewResult?>(Context, ct => Context.Client.PreviewWorkflowProfileForVesselAsync(vesselId, profileId, ct), result =>
            {
                PreviewView.Reset(false);
                PreviewView.Section("Resolved Commands");
                PreviewView.Row("Vessel", vesselName);
                if (result != null)
                {
                    PreviewView.Row("Profile", result.ResolvedProfile?.Name ?? T("None"));
                    PreviewView.Row("Resolution", result.ResolutionMode.ToString());
                    PreviewView.Row("Available Check Types", result.AvailableCheckTypes.Count > 0 ? String.Join(", ", result.AvailableCheckTypes) : T("None"));
                    PreviewCommands.SetLocalRows(result.CommandPreviews ?? new List<WorkflowProfileCommandPreview>());
                }

                ShowPanel("preview");
            }, "Preview failed.");
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Workflow Profile"; }
        }

        /// <inheritdoc />
        protected override Task<WorkflowProfile?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetWorkflowProfileAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(WorkflowProfile entity, CancellationToken token)
        {
            await LoadReferenceAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddAction("validate", "Validate", Validate, null, "v");
            AddAction("save", IsCreateMode ? "Create Workflow Profile" : "Save Changes", Save, () => CanManage);
            AddAction("preview", "Preview for Vessel", PreviewForVessel, () => Entity != null);
            AddJsonAction();
            AddAction("duplicate", "Duplicate", () => { if (Entity != null) WorkflowProfileForms.Duplicate(Context, Entity); }, () => Entity != null && CanManage);
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && CanManage, "delete");
            AddAction("back", "Back", () => Context.Navigate("/configuration?tab=workflow-profiles"));
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            StackPanel editor = new StackPanel();
            editor.Add(_Notice, 1);
            editor.Add(Editor.View, null);
            _Notice.Visible = false;
            AddPanel("profile", "Profile", editor);
            StackPanel validation = new StackPanel();
            validation.Add(ValidationView, null, null, 1);
            validation.Add(ValidationCommands, null, "Resolved Commands", 1);
            ValidationView.Row("Validation", T("Press v or choose Validate to check this profile."));
            AddPanel("validation", "Validation", validation);
            StackPanel preview = new StackPanel();
            preview.Add(PreviewView, 6);
            preview.Add(PreviewCommands, null);
            PreviewView.Row("Vessel", T("Choose Preview for Vessel to resolve this profile for a vessel."));
            AddPanel("preview", "Vessel Preview", preview);
            DetailsView.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("details", "Details", DetailsView);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(WorkflowProfile entity)
        {
            return entity.Name;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(WorkflowProfile entity)
        {
            return new[] { entity.Active ? "Active" : "Inactive" };
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            if (Route.Query.TryGetValue("scope", out string? scope) && (scope == "Global" || scope == "Fleet" || scope == "Vessel")) _Scope.SetValue(scope);
            if (Route.Query.TryGetValue("fleetId", out string? fleetId)) _Fleet.Options = new List<SelectOption<string>> { new SelectOption<string>(fleetId, fleetId) };
            if (Route.Query.TryGetValue("vesselId", out string? vesselId)) _Vessel.Options = new List<SelectOption<string>> { new SelectOption<string>(vesselId, vesselId) };
            if (fleetId != null) _Fleet.SetValue(fleetId);
            if (vesselId != null) _Vessel.SetValue(vesselId);
            Editor.View.SaveButton.Label = "Create Workflow Profile";
            Editor.View.DiscardButton.Visible = false;
            Editor.MarkClean();
            Task.Run(async () =>
            {
                await LoadReferenceAsync(CancellationToken.None).ConfigureAwait(false);
                Context.Dispatcher.Post(ApplyReferenceOptions);
            });
        }

        /// <inheritdoc />
        protected override void Populate(WorkflowProfile entity)
        {
            ApplyReferenceOptions();
            FillForm(entity);
            bool manage = CanManage;
            _Notice.Visible = !manage;
            foreach (TextAreaField area in _Commands.Values) area.ReadOnly = !manage;
            _LanguageHints.ReadOnly = !manage;
            _ExpectedArtifacts.ReadOnly = !manage;
            _IsDefault.ReadOnly = !manage;
            _Active.ReadOnly = !manage;
            _Inputs.ReadOnly = !manage;
            _Environments.ReadOnly = !manage;
            Editor.View.SaveButton.Label = "Save Changes";
            Editor.View.SaveButton.Visible = manage;
            Editor.View.DiscardButton.Visible = manage;

            DetailsView.Reset();
            DetailsView.Section("Details");
            DetailsView.Row("ID", entity.Id, t => t.Code);
            DetailsView.Row("Created", EntityUi.Date(Context, entity.CreatedUtc));
            DetailsView.Row("Last Updated", EntityUi.Date(Context, entity.LastUpdateUtc));
            DetailsView.Row("Scope", entity.Scope.ToString());
            DetailsView.Row("Visibility", T(ScopeRules.Label(entity.OwnershipScope)));
            if (!String.IsNullOrEmpty(entity.FleetId)) DetailsView.Link("Fleet", NameOf(_FleetList.Select(f => new KeyValuePair<string, string>(f.Id, f.Name)), entity.FleetId), () => Context.Navigate("/fleets/" + entity.FleetId));
            if (!String.IsNullOrEmpty(entity.VesselId)) DetailsView.Link("Vessel", NameOf(_VesselList.Select(v => new KeyValuePair<string, string>(v.Id, v.Name)), entity.VesselId), () => Context.Navigate("/vessels/" + entity.VesselId));
            DetailsView.Row("Capabilities", WorkflowProfileForms.CountCapabilities(entity) + " " + T("commands"));
            DetailsView.Row("Targets", (entity.Environments?.Count ?? 0) + " " + T("environments"));
        }

        #endregion

        #region Private-Methods

        private ArmadaGrid<WorkflowProfileCommandPreview> CommandGrid(string empty)
        {
            ArmadaGrid<WorkflowProfileCommandPreview> grid = new ArmadaGrid<WorkflowProfileCommandPreview>(c => c.CheckType + "|" + (c.EnvironmentName ?? "") + "|" + c.Command);
            grid.Dispatcher = Context.Dispatcher;
            grid.ModalHost = Context.Modals;
            grid.MultiSelect = false;
            grid.EmptyText = empty;
            grid.AddColumn(new GridColumn<WorkflowProfileCommandPreview>("checkType", "Check Type", c => c.CheckType.ToString()) { Width = 24, Sortable = true });
            grid.AddColumn(new GridColumn<WorkflowProfileCommandPreview>("environment", "Environment", c => String.IsNullOrEmpty(c.EnvironmentName) ? T("Base") : c.EnvironmentName!) { Width = 16, Sortable = true });
            grid.AddColumn(new GridColumn<WorkflowProfileCommandPreview>("command", "Command", c => c.Command) { Weight = 4, Sortable = true });
            grid.Activated += (s, c) => Context.Clipboard.Copy(c.Command, "Command");
            return grid;
        }

        private string? Command(string label)
        {
            return EntityUi.Blank(_Commands[label].Value);
        }

        private void FillForm(WorkflowProfile p)
        {
            _Name.Value = p.Name;
            _Scope.SetValue(p.Scope.ToString());
            EnsureOption(_Fleet, p.FleetId);
            EnsureOption(_Vessel, p.VesselId);
            _Fleet.SetValue(p.FleetId ?? "");
            _Vessel.SetValue(p.VesselId ?? "");
            _Description.Value = p.Description ?? "";
            _IsDefault.SetValue(p.IsDefault, false);
            _Active.SetValue(p.Active, false);
            _LanguageHints.Value = EntityUi.JoinLines(p.LanguageHints);
            _ExpectedArtifacts.Value = EntityUi.JoinLines(p.ExpectedArtifacts);
            _Commands["Lint Command"].Value = p.LintCommand ?? "";
            _Commands["Build Command"].Value = p.BuildCommand ?? "";
            _Commands["Unit Test Command"].Value = p.UnitTestCommand ?? "";
            _Commands["Integration Test Command"].Value = p.IntegrationTestCommand ?? "";
            _Commands["E2E Test Command"].Value = p.E2ETestCommand ?? "";
            _Commands["Migration Command"].Value = p.MigrationCommand ?? "";
            _Commands["Security Scan Command"].Value = p.SecurityScanCommand ?? "";
            _Commands["Performance Command"].Value = p.PerformanceCommand ?? "";
            _Commands["Package Command"].Value = p.PackageCommand ?? "";
            _Commands["Deployment Verification Command"].Value = p.DeploymentVerificationCommand ?? "";
            _Commands["Rollback Verification Command"].Value = p.RollbackVerificationCommand ?? "";
            _Commands["Publish Artifact Command"].Value = p.PublishArtifactCommand ?? "";
            _Commands["Release Versioning Command"].Value = p.ReleaseVersioningCommand ?? "";
            _Commands["Changelog Generation Command"].Value = p.ChangelogGenerationCommand ?? "";
            _Inputs.SetItems(p.RequiredInputs);
            _Environments.SetItems(p.Environments);
            Editor.MarkClean();
        }

        private async Task LoadReferenceAsync(CancellationToken token)
        {
            Task<List<Fleet>> fleets = EntityLookups.FleetsAsync(Context.Client, token);
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(Context.Client, token);
            await Task.WhenAll(fleets, vessels).ConfigureAwait(false);
            _FleetList = fleets.Result.Where(f => f.Active).ToList();
            _VesselList = vessels.Result.Where(v => v.Active).ToList();
        }

        private void ApplyReferenceOptions()
        {
            bool dirty = Editor.View.IsDirty;
            string? fleet = _Fleet.Value;
            string? vessel = _Vessel.Value;
            Editor.SetOptions(_Fleet, EntityLookups.Options(_FleetList, f => f.Id, f => f.Name), "Select a fleet...");
            Editor.SetOptions(_Vessel, EntityLookups.Options(_VesselList, v => v.Id, v => v.Name), "Select a vessel...");
            EnsureOption(_Fleet, fleet);
            EnsureOption(_Vessel, vessel);
            _Fleet.SetValue(fleet ?? "");
            _Vessel.SetValue(vessel ?? "");
            if (!dirty) Editor.MarkClean();
        }

        private static void EnsureOption(SelectField<string> field, string? id)
        {
            if (String.IsNullOrEmpty(id) || field.Options.Any(o => o.Value == id)) return;
            field.Options.Add(new SelectOption<string>(id!, id!));
        }

        private static string NameOf(IEnumerable<KeyValuePair<string, string>> items, string? id)
        {
            foreach (KeyValuePair<string, string> kvp in items)
            {
                if (kvp.Key == id) return kvp.Value;
            }

            return id ?? "-";
        }

        private void RequestDelete()
        {
            WorkflowProfile? p = Entity;
            if (p == null || !CanManage) return;
            Context.Confirm("Delete Workflow Profile", EntityUi.T(Context, "Delete \"{{name}}\"? Existing check runs remain, but future runs will not be able to resolve this profile.", "name", p.Name), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteWorkflowProfileAsync(p.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Workflow profile \"{{name}}\" deleted.", "name", p.Name));
                    Context.Navigate("/configuration?tab=workflow-profiles");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
