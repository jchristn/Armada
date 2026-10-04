namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
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
    /// Project profile detail and editor (dashboard <c>ProjectProfileDetail.tsx</c>, route
    /// <c>/project-profiles/:id</c>, and <c>/project-profiles/new</c> for create): panels Profile (name, description,
    /// scope, fleet, vessel, default pipeline, workflow profile, default, active, persona overrides with persona,
    /// prompt template, enabled, and additional instructions, and skills; <c>Ctrl+S</c> saves), Prompt Diff (pick a
    /// persona and preview the base against the effective persona prompt as a diff), and Details. Actions: Save, Preview
    /// Prompt, View JSON, Delete (confirmed), Back. Viewers who cannot edit see the dashboard's read-only notice.
    /// </summary>
    public class ProjectProfileScreen : EntityDetailScreen<ProjectProfile>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Project Profile"; }
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
        /// Persona chooser for the prompt diff.
        /// </summary>
        public EntityForm DiffForm { get; }

        /// <summary>
        /// Prompt diff viewer (base against effective).
        /// </summary>
        public DiffViewer Diff { get; } = new DiffViewer();

        /// <summary>
        /// Last prompt preview, or null.
        /// </summary>
        public PersonaPromptPreview? Preview { get; private set; } = null;

        /// <summary>
        /// Identifiers and timestamps.
        /// </summary>
        public LinkDetailView DetailsView { get; } = new LinkDetailView();

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

        private readonly TextBlock _Notice = new TextBlock("You can view this project profile, but only tenant administrators can change it.", t => t.Error);
        private readonly TextBlock _PreviewSummary = new TextBlock("Save the profile first, then preview the base vs. effective persona prompt for the current overrides.", t => t.Muted);
        private readonly InputField _Name;
        private readonly InputField _Description;
        private readonly SelectField<string> _Scope;
        private readonly SelectField<string> _Fleet;
        private readonly SelectField<string> _Vessel;
        private readonly SelectField<string> _Pipeline;
        private readonly SelectField<string> _Workflow;
        private readonly CheckField _IsDefault;
        private readonly CheckField _Active;
        private readonly RecordListField<PersonaOverride> _Overrides;
        private readonly TextAreaField _Skills;
        private readonly SelectField<string> _PreviewPersona;
        private ProfileReferenceData _Data = new ProfileReferenceData();
        private List<string> _PersonaNames = new List<string>();
        private bool _Saving = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public ProjectProfileScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Editor = new EntityForm(context);
            _Name = Editor.Text("Name", "Default Project Profile", "", true);
            _Description = Editor.Text("Description", "");
            _Scope = Editor.Select("Scope", ProfileFormSupport.ScopeOptions(context), "Global", null, true);
            _Fleet = Editor.Select("Fleet", new List<SelectOption<string>>(), null, "Select a fleet...", false, "Used when the scope is Fleet.");
            _Vessel = Editor.Select("Vessel", new List<SelectOption<string>>(), null, "Select a vessel...", false, "Used when the scope is Vessel.");
            _Pipeline = Editor.Select("Default Pipeline ID", new List<SelectOption<string>>(), null, "None");
            _Workflow = Editor.Select("Workflow Profile ID", new List<SelectOption<string>>(), null, "None");
            _IsDefault = Editor.Check("Default for scope", false);
            _Active = Editor.Check("Active", true);
            Editor.Section("Persona Overrides");
            _Overrides = Editor.List<PersonaOverride>("Persona Overrides", o => ProjectProfileForms.DescribeOverride(context, o), null, 6,
                "Swap a persona prompt template and/or append per-project instructions. Applied to this project's pipeline personas at dispatch.");
            _Overrides.EmptyText = "No persona overrides. Personas use their built-in prompts.";
            _Overrides.Fingerprint = o => o.PersonaName + "|" + o.PromptTemplateName + "|" + o.Enabled + "|" + o.AdditionalInstructions;
            _Overrides.Editor = (item, done) => ProjectProfileForms.EditOverride(Context, item, _PersonaNames, done);
            _Overrides.Toggle = o =>
            {
                PersonaOverride copy = ProfileFormSupport.Clone(o);
                copy.Enabled = !o.Enabled;
                return copy;
            };
            Editor.Section("Skills");
            _Skills = Editor.Area("Skills", "", 4, false, "One skill per line. Attached to this project.");
            _Skills.Placeholder = "dotnet\ntdd";
            Editor.View.SaveRequested += (s, e) => Save();
            Editor.View.DiscardRequested += (s, e) =>
            {
                if (Entity != null) FillForm(Entity);
            };

            DiffForm = new EntityForm(context);
            _PreviewPersona = DiffForm.Select("Persona", ProfileFormSupport.KnownPersonas.Select(n => new SelectOption<string>(n, n)), "Architect", null, true);
            DiffForm.View.SaveButton.Label = "Preview";
            DiffForm.View.SaveButton.Hint = null;
            DiffForm.View.DiscardButton.Visible = false;
            DiffForm.View.SaveRequested += (s, e) => LoadPreview();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the payload from the editor.
        /// </summary>
        /// <returns>Profile, or null when the name is blank.</returns>
        public ProjectProfile? BuildPayload()
        {
            if (String.IsNullOrWhiteSpace(_Name.Value)) return null;
            ProjectProfile payload = Entity != null ? ProfileFormSupport.Clone(Entity) : new ProjectProfile();
            payload.Name = _Name.Value.Trim();
            payload.Description = EntityUi.Blank(_Description.Value);
            payload.Scope = EntityForm.EnumValue(_Scope, ProjectProfileScopeEnum.Global);
            payload.FleetId = payload.Scope == ProjectProfileScopeEnum.Fleet ? EntityUi.Blank(_Fleet.Value) : null;
            payload.VesselId = payload.Scope == ProjectProfileScopeEnum.Vessel ? EntityUi.Blank(_Vessel.Value) : null;
            payload.IsDefault = _IsDefault.Value;
            payload.Active = _Active.Value;
            payload.DefaultPipelineId = EntityUi.Blank(_Pipeline.Value);
            payload.WorkflowProfileId = EntityUi.Blank(_Workflow.Value);
            payload.PersonaOverrides = _Overrides.Items.ToList();
            payload.Skills = EntityUi.SplitLines(_Skills.Value);
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
            ProjectProfile? payload = BuildPayload();
            if (payload == null) return;
            _Saving = true;
            bool create = Entity == null;
            string id = Entity?.Id ?? "";
            EntityUi.Run<ProjectProfile?>(Context, ct => create
                ? Context.Client.CreateProjectProfileAsync(payload, ct)
                : Context.Client.UpdateProjectProfileAsync(id, payload, ct), saved =>
            {
                _Saving = false;
                if (saved == null) return;
                if (create)
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Project profile \"{{name}}\" created.", "name", saved.Name));
                    Context.Navigate("/project-profiles/" + saved.Id);
                    return;
                }

                Entity = saved;
                Populate(saved);
                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Project profile \"{{name}}\" saved.", "name", saved.Name));
            }, "Save failed.", ex => _Saving = false);
        }

        /// <summary>
        /// Preview the base against the effective prompt for the chosen persona.
        /// </summary>
        public void LoadPreview()
        {
            if (Entity == null) return;
            string persona = _PreviewPersona.Value ?? "Architect";
            string id = Entity.Id;
            EntityUi.Run<PersonaPromptPreview?>(Context, ct => Context.Client.PreviewPersonaPromptAsync(id, persona, ct), preview =>
            {
                if (preview == null) return;
                Preview = preview;
                _PreviewSummary.Text = preview.IsOverridden
                    ? EntityUi.T(Context, "Overridden: {{base}} to {{eff}}", "base", preview.BaseTemplateName, "eff", preview.EffectiveTemplateName)
                    : EntityUi.T(Context, "No override applied for {{persona}}.", "persona", preview.PersonaName);
                _PreviewSummary.Translate = false;
                Diff.Diff = ProfileFormSupport.LineDiff(T("Base") + " (" + preview.BaseTemplateName + ")", preview.BasePrompt, T("Effective") + " (" + preview.EffectiveTemplateName + ")", preview.EffectivePrompt);
                ShowPanel("diff");
            }, "Preview failed.");
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Project Profile"; }
        }

        /// <inheritdoc />
        protected override Task<ProjectProfile?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetProjectProfileAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(ProjectProfile entity, CancellationToken token)
        {
            await LoadReferenceAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddAction("save", IsCreateMode ? "Create Project Profile" : "Save Changes", Save, () => CanManage);
            AddAction("preview", "Preview", () => { ShowPanel("diff"); LoadPreview(); }, () => Entity != null, "p");
            AddJsonAction();
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && CanManage, "delete");
            AddAction("back", "Back", () => Context.Navigate("/configuration?tab=project-profiles"));
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            StackPanel editor = new StackPanel();
            editor.Add(_Notice, 1);
            editor.Add(Editor.View, null);
            _Notice.Visible = false;
            AddPanel("profile", "Profile", editor);
            StackPanel diff = new StackPanel();
            diff.Add(DiffForm.View, 3);
            diff.Add(_PreviewSummary, 2);
            diff.Add(Diff, null);
            AddPanel("diff", "Persona Prompt Diff", diff);
            DetailsView.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            AddPanel("details", "Details", DetailsView);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(ProjectProfile entity)
        {
            return entity.Name;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(ProjectProfile entity)
        {
            return new[] { entity.Active ? "Active" : "Inactive" };
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            Editor.View.SaveButton.Label = "Create Project Profile";
            Editor.View.DiscardButton.Visible = false;
            Editor.MarkClean();
            Task.Run(async () =>
            {
                await LoadReferenceAsync(CancellationToken.None).ConfigureAwait(false);
                Context.Dispatcher.Post(ApplyReferenceOptions);
            });
        }

        /// <inheritdoc />
        protected override void Populate(ProjectProfile entity)
        {
            ApplyReferenceOptions();
            FillForm(entity);
            bool manage = CanManage;
            _Notice.Visible = !manage;
            _IsDefault.ReadOnly = !manage;
            _Active.ReadOnly = !manage;
            _Overrides.ReadOnly = !manage;
            _Skills.ReadOnly = !manage;
            Editor.View.SaveButton.Label = "Save Changes";
            Editor.View.SaveButton.Visible = manage;
            Editor.View.DiscardButton.Visible = manage;
            List<string> personas = ProfileFormSupport.KnownPersonas.Concat(_PersonaNames).Concat(entity.PersonaOverrides.Select(o => o.PersonaName)).Distinct(StringComparer.Ordinal).ToList();
            string? chosen = _PreviewPersona.Value;
            DiffForm.SetOptions(_PreviewPersona, personas.Select(n => new SelectOption<string>(n, n)));
            _PreviewPersona.SetValue(chosen ?? "Architect");
            DiffForm.MarkClean();

            DetailsView.Reset();
            DetailsView.Section("Details");
            DetailsView.Row("ID", entity.Id, t => t.Code);
            DetailsView.Row("Created", EntityUi.Date(Context, entity.CreatedUtc));
            DetailsView.Row("Last Updated", EntityUi.Date(Context, entity.LastUpdateUtc));
            DetailsView.Row("Status", EntityUi.Badge(Context, entity.Active ? "Active" : "Inactive"));
            DetailsView.Row("Scope", entity.Scope.ToString());
            DetailsView.Row("Visibility", T(ScopeRules.Label(entity.OwnershipScope)));
            if (!String.IsNullOrEmpty(entity.WorkflowProfileId)) DetailsView.Link("Workflow Profile", entity.WorkflowProfileId, () => Context.Navigate("/workflow-profiles/" + entity.WorkflowProfileId));
            if (!String.IsNullOrEmpty(entity.DefaultPipelineId))
            {
                Pipeline? pipeline = _Data.Pipelines.FirstOrDefault(p => p.Id == entity.DefaultPipelineId);
                DetailsView.Link("Default Pipeline ID", entity.DefaultPipelineId, pipeline != null ? () => Context.Navigate("/pipelines/" + Uri.EscapeDataString(pipeline.Name)) : (Action?)null);
            }
        }

        #endregion

        #region Private-Methods

        private void FillForm(ProjectProfile p)
        {
            _Name.Value = p.Name;
            _Description.Value = p.Description ?? "";
            _Scope.SetValue(p.Scope.ToString());
            EnsureOption(_Fleet, p.FleetId);
            EnsureOption(_Vessel, p.VesselId);
            EnsureOption(_Pipeline, p.DefaultPipelineId);
            EnsureOption(_Workflow, p.WorkflowProfileId);
            _Fleet.SetValue(p.FleetId ?? "");
            _Vessel.SetValue(p.VesselId ?? "");
            _Pipeline.SetValue(p.DefaultPipelineId ?? "");
            _Workflow.SetValue(p.WorkflowProfileId ?? "");
            _IsDefault.SetValue(p.IsDefault, false);
            _Active.SetValue(p.Active, false);
            _Overrides.SetItems(p.PersonaOverrides);
            _Skills.Value = EntityUi.JoinLines(p.Skills);
            Editor.MarkClean();
        }

        private async Task LoadReferenceAsync(CancellationToken token)
        {
            Task<ProfileReferenceData> data = ProjectProfileForms.LoadAsync(Context, token);
            Task<List<Persona>> personas = EntityLookups.PersonasAsync(Context.Client, token);
            await Task.WhenAll(data, personas).ConfigureAwait(false);
            _Data = data.Result;
            _PersonaNames = personas.Result.Select(p => p.Name).ToList();
        }

        private void ApplyReferenceOptions()
        {
            bool dirty = Editor.View.IsDirty;
            string? fleet = _Fleet.Value;
            string? vessel = _Vessel.Value;
            string? pipeline = _Pipeline.Value;
            string? workflow = _Workflow.Value;
            Editor.SetOptions(_Fleet, EntityLookups.Options(_Data.Fleets, f => f.Id, f => f.Name), "Select a fleet...");
            Editor.SetOptions(_Vessel, EntityLookups.Options(_Data.Vessels, v => v.Id, v => v.Name), "Select a vessel...");
            Editor.SetOptions(_Pipeline, ProjectProfileForms.PipelineOptions(_Data, pipeline), "None");
            Editor.SetOptions(_Workflow, ProjectProfileForms.WorkflowOptions(_Data, workflow), "None");
            EnsureOption(_Fleet, fleet);
            EnsureOption(_Vessel, vessel);
            _Fleet.SetValue(fleet ?? "");
            _Vessel.SetValue(vessel ?? "");
            _Pipeline.SetValue(pipeline ?? "");
            _Workflow.SetValue(workflow ?? "");
            if (!dirty) Editor.MarkClean();
        }

        private static void EnsureOption(SelectField<string> field, string? id)
        {
            if (String.IsNullOrEmpty(id) || field.Options.Any(o => o.Value == id)) return;
            field.Options.Add(new SelectOption<string>(id!, id!));
        }

        private void RequestDelete()
        {
            ProjectProfile? p = Entity;
            if (p == null || !CanManage) return;
            Context.Confirm("Delete Project Profile", T("Delete this project profile? This cannot be undone."), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteProjectProfileAsync(p.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, T("Project profile deleted."));
                    Context.Navigate("/configuration?tab=project-profiles");
                }, "Delete failed.");
            }, "Delete");
        }

        #endregion
    }
}
