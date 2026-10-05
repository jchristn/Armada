namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Runbook detail (dashboard <c>RunbookDetail.tsx</c>, route <c>/runbooks/:id</c> with optional
    /// <c>?executionId=</c>, and <c>/runbooks/new</c> for create): actions Start Execution, Save Execution, Mark
    /// Completed and Cancel Execution (confirmed), Run Check, Save Runbook, View JSON, Duplicate, and Delete; panels
    /// Overview (binding, counts, dates, identifiers), Runbook (file name, title, description, workflow profile,
    /// environment, default check type, overview Markdown with <c>$EDITOR</c>, active, parameters, and steps, edited
    /// in place and saved with <c>Ctrl+S</c>), Executions (select one with Enter), and Execution Progress (status,
    /// environment, deployment and incident links, notes, the step checklist with <c>Space</c> and per-step notes,
    /// Save Progress). Editing follows the runbook's scope (<c>lib/scoping.ts</c>). A prefilled execution handed
    /// off from a deployment or incident seeds Start Execution.
    /// </summary>
    public class RunbookScreen : EntityDetailScreen<Runbook>
    {
        #region Public-Members

        /// <inheritdoc />
        public override string EntityLabel
        {
            get { return "Runbook"; }
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
        /// The runbook edit form.
        /// </summary>
        public EntityForm Editor { get; }

        /// <summary>
        /// Executions grid.
        /// </summary>
        public ArmadaGrid<RunbookExecution> ExecutionGrid { get; } = new ArmadaGrid<RunbookExecution>(e => e.Id);

        /// <summary>
        /// Progress summary of the selected execution.
        /// </summary>
        public LinkDetailView ProgressSummary { get; } = new LinkDetailView();

        /// <summary>
        /// Progress form (notes and steps) of the selected execution.
        /// </summary>
        public EntityForm ProgressForm { get; }

        /// <summary>
        /// Step checklist of the selected execution.
        /// </summary>
        public RecordListField<RunbookStepProgress> StepChecklist { get; private set; }

        /// <summary>
        /// Parameter list in the edit form.
        /// </summary>
        public RecordListField<RunbookParameter> ParameterList { get; private set; }

        /// <summary>
        /// Step list in the edit form.
        /// </summary>
        public RecordListField<RunbookStep> StepList { get; private set; }

        /// <summary>
        /// The selected execution (with unsaved progress), or null.
        /// </summary>
        public RunbookExecution? SelectedExecution { get; private set; } = null;

        /// <summary>
        /// Hand-off from a deployment or incident, or null.
        /// </summary>
        public RunbookExecutionStartRequest? Prefill { get; }

        #endregion

        #region Private-Members

        private RunbookReferenceData _Data = new RunbookReferenceData();
        private List<RunbookExecution> _Executions = new List<RunbookExecution>();
        private InputField _FileName;
        private InputField _Title;
        private TextAreaField _Description;
        private SelectField<string> _Profile;
        private SelectField<string> _Environment;
        private InputField _EnvironmentName;
        private SelectField<string> _CheckType;
        private TextAreaField _Overview;
        private CheckField _Active;
        private TextAreaField _ExecutionNotes;
        private bool _CreateDataLoaded = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public RunbookScreen(RouteMatch route, TuiContext context)
            : base(route, context)
        {
            Prefill = NavigationPrefill.Take<RunbookExecutionStartRequest>(context, PrefillSlots.RunbookExecution);
            Editor = new EntityForm(context);
            _FileName = Editor.Text("File Name", "RUNBOOK.md");
            _Title = Editor.Text("Title", "Runbook", "", true);
            _Description = Editor.Area("Description", "", 2);
            _Profile = Editor.Select("Workflow Profile", new List<SelectOption<string>>(), "", "No workflow profile");
            _Environment = Editor.Select("Environment", new List<SelectOption<string>>(), "", "No environment");
            _EnvironmentName = Editor.Text("Environment Name", "");
            _CheckType = Editor.Select("Default Check Type", RunbookForms.CheckTypeOptions(), "", "No default check");
            _Overview = Editor.Area("Overview Markdown", "", 10);
            _Active = Editor.Check("Active", true);
            ParameterList = Editor.List<RunbookParameter>("Parameters", p => p.Name + (String.IsNullOrEmpty(p.Label) ? "" : " (" + p.Label + ")") + (p.Required ? " *" : "") + (String.IsNullOrEmpty(p.DefaultValue) ? "" : " = " + p.DefaultValue), null, 6);
            ParameterList.EmptyText = "No parameters defined.";
            ParameterList.Fingerprint = p => p.Name + "|" + p.Label + "|" + p.DefaultValue + "|" + p.Description + "|" + p.Required;
            ParameterList.Editor = (p, done) => RunbookForms.EditParameter(Context, p, done);
            StepList = Editor.List<RunbookStep>("Steps", s => s.Title + (String.IsNullOrEmpty(s.Instructions) ? "" : ": " + FirstLine(s.Instructions)), null, 8);
            StepList.EmptyText = "No steps defined yet.";
            StepList.Fingerprint = s => s.Id + "|" + s.Title + "|" + s.Instructions;
            StepList.Editor = (s, done) => RunbookForms.EditStep(Context, s, done);
            Editor.View.SaveButton.Label = "Save Runbook";
            Editor.View.DiscardButton.Visible = false;
            Editor.View.SaveRequested += (s, e) => SaveRunbook();
            _Environment.ValueChanged += (s, e) =>
            {
                DeploymentEnvironment? selected = _Data.Environments.FirstOrDefault(x => x.Id == _Environment.Value);
                if (selected != null) _EnvironmentName.Value = selected.Name;
            };

            ProgressForm = new EntityForm(context);
            _ExecutionNotes = ProgressForm.Area("Execution Notes", "", 3);
            StepChecklist = ProgressForm.List<RunbookStepProgress>("Steps", p => (p.Done ? "[x] " : "[ ] ") + p.Step.Title + (String.IsNullOrEmpty(p.Note) ? "" : "  - " + FirstLine(p.Note)), null, 10);
            StepChecklist.EmptyText = "No steps defined yet.";
            StepChecklist.Fingerprint = p => p.Step.Id + "|" + p.Done + "|" + p.Note;
            StepChecklist.AllowAdd = false;
            StepChecklist.AllowRemove = false;
            StepChecklist.AllowReorder = false;
            StepChecklist.Toggle = p => new RunbookStepProgress(p.Step, !p.Done, p.Note);
            StepChecklist.Editor = (p, done) => { if (p != null) EditStepNote(p, done); };
            ProgressForm.View.SaveButton.Label = "Save Progress";
            ProgressForm.View.DiscardButton.Visible = false;
            ProgressForm.View.SaveRequested += (s, e) => SaveExecution(null);
            Editor.MarkClean();
            ProgressForm.MarkClean();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Select an execution and show its progress.
        /// </summary>
        /// <param name="executionId">Execution id.</param>
        /// <returns>True when found.</returns>
        public bool SelectExecution(string? executionId)
        {
            RunbookExecution? execution = _Executions.FirstOrDefault(e => e.Id == executionId);
            if (execution == null) return false;
            SelectedExecution = Clone(execution);
            PopulateProgress();
            ShowPanel("progress");
            return true;
        }

        /// <summary>
        /// Open Start Execution.
        /// </summary>
        public void OpenStartExecution()
        {
            Runbook? runbook = Entity;
            if (runbook == null) return;
            RunbookForms.ShowStartExecution(Context, runbook, _Data, Prefill, started =>
            {
                _Executions.Insert(0, started);
                Reload();
                SelectedExecution = Clone(started);
                PopulateProgress();
                ShowPanel("progress");
            });
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override string CreateTitle
        {
            get { return "Create Runbook"; }
        }

        /// <inheritdoc />
        protected override Task<Runbook?> FetchAsync(CancellationToken token)
        {
            return Context.Client.GetRunbookAsync(EntityId, token);
        }

        /// <inheritdoc />
        protected override async Task FetchRelatedAsync(Runbook entity, CancellationToken token)
        {
            Task<RunbookReferenceData> data = LoadReferenceAsync(token);
            Task<List<RunbookExecution>> executions = LoadExecutionsAsync(entity.Id, token);
            await Task.WhenAll(data, executions).ConfigureAwait(false);
            _Data = data.Result;
            _Executions = executions.Result;
        }

        /// <inheritdoc />
        protected override void BuildActions()
        {
            AddAction("save", "Save Runbook", SaveRunbook, () => CanManage() && (IsCreateMode || Entity != null), "ctrl+s");
            AddAction("start", "Start Execution", OpenStartExecution, () => !IsCreateMode && Entity != null, "x");
            AddAction("save-execution", "Save Execution", () => SaveExecution(null), () => SelectedExecution != null);
            AddAction("complete", "Mark Completed", () => ConfirmStatus(RunbookExecutionStatusEnum.Completed), () => SelectedExecution != null);
            AddAction("cancel-execution", "Cancel Execution", () => ConfirmStatus(RunbookExecutionStatusEnum.Cancelled), () => SelectedExecution != null);
            AddAction("run-check", "Run Check", LaunchCheck, CanLaunchCheck);
            AddJsonAction();
            AddAction("duplicate", "Duplicate", () => { if (Entity != null) RunbookForms.Duplicate(Context, Entity, Carry); }, () => Entity != null && CanManage());
            AddAction("delete", "Delete", RequestDelete, () => Entity != null && CanManage(), "delete");
        }

        /// <inheritdoc />
        protected override void BuildPanels()
        {
            Overview.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            ProgressSummary.CopyRequested += (s, v) => Context.Clipboard.Copy(v, "Value");
            if (!IsCreateMode) AddPanel("overview", "Overview", Overview);
            AddPanel("runbook", "Runbook", Editor.View);
            if (IsCreateMode) return;
            ExecutionGrid.Dispatcher = Context.Dispatcher;
            ExecutionGrid.ModalHost = Context.Modals;
            ExecutionGrid.MultiSelect = false;
            ExecutionGrid.EmptyText = "No executions recorded yet.";
            ExecutionGrid.AddColumn(new GridColumn<RunbookExecution>("title", "Title", e => e.Title) { Weight = 3, Sortable = true });
            ExecutionGrid.AddColumn(new GridColumn<RunbookExecution>("status", "Status", e => StatusBadge.Label(e.Status)) { Width = 14, Sortable = true, Style = (e, t) => StatusBadge.Style(e.Status, t) });
            ExecutionGrid.AddColumn(new GridColumn<RunbookExecution>("environment", "Environment", e => String.IsNullOrEmpty(e.EnvironmentName) ? T("No environment") : e.EnvironmentName!) { Weight = 2 });
            ExecutionGrid.AddColumn(new GridColumn<RunbookExecution>("checkType", "Check Type", e => e.CheckType.HasValue ? e.CheckType.Value.ToString() : T("No check type")) { Weight = 2 });
            ExecutionGrid.AddColumn(new GridColumn<RunbookExecution>("steps", "Steps", e => e.CompletedStepIds.Count.ToString(CultureInfo.InvariantCulture) + "/" + (Entity?.Steps.Count ?? 0).ToString(CultureInfo.InvariantCulture) + " " + T("steps")) { Width = 12 });
            ExecutionGrid.AddColumn(new GridColumn<RunbookExecution>("started", "Started", e => EntityUi.When(Context, e.StartedUtc)) { Width = 13, Sortable = true });
            ExecutionGrid.AddColumn(new GridColumn<RunbookExecution>("id", "ID", e => e.Id) { Width = 26 });
            ExecutionGrid.Activated += (s, e) => SelectExecution(e.Id);
            AddPanel("executions", "Executions", ExecutionGrid);
            StackPanel progress = new StackPanel();
            progress.Add(ProgressSummary, 9);
            progress.Add(ProgressForm.View, null);
            AddPanel("progress", "Execution Progress", progress);
        }

        /// <inheritdoc />
        protected override string HeaderTitle(Runbook entity)
        {
            return entity.Title;
        }

        /// <inheritdoc />
        protected override IEnumerable<string> HeaderStatuses(Runbook entity)
        {
            return new[] { entity.Active ? "Active" : "Inactive" };
        }

        /// <inheritdoc />
        protected override void OnCreateMode()
        {
            if (_CreateDataLoaded) return;
            _CreateDataLoaded = true;
            EntityUi.Run(Context, ct => LoadReferenceAsync(ct), data =>
            {
                _Data = data;
                ApplyReferenceOptions();
                if (Prefill != null)
                {
                    _Profile.SetValue(Prefill.WorkflowProfileId ?? "");
                    _Environment.SetValue(Prefill.EnvironmentId ?? "");
                    _EnvironmentName.Value = Prefill.EnvironmentName ?? "";
                    if (Prefill.CheckType.HasValue) _CheckType.SetValue(Prefill.CheckType.Value.ToString());
                }

                Editor.MarkClean();
            }, "Failed to load runbook reference data.");
        }

        /// <inheritdoc />
        protected override void Populate(Runbook r)
        {
            ApplyReferenceOptions();
            bool canManage = CanManage();
            if (!Editor.View.IsDirty)
            {
                _FileName.Value = r.FileName;
                _Title.Value = r.Title;
                _Description.Value = r.Description ?? "";
                _Profile.SetValue(r.WorkflowProfileId ?? "");
                _Environment.SetValue(r.EnvironmentId ?? "");
                _EnvironmentName.Value = r.EnvironmentName ?? "";
                _CheckType.SetValue(r.DefaultCheckType.HasValue ? r.DefaultCheckType.Value.ToString() : "");
                _Overview.Value = r.OverviewMarkdown ?? "";
                _Active.SetValue(r.Active, false);
                ParameterList.SetItems((r.Parameters ?? new List<RunbookParameter>()).Select(RunbookForms.CloneParameter));
                StepList.SetItems((r.Steps ?? new List<RunbookStep>()).Select(s => new RunbookStep { Id = s.Id, Title = s.Title, Instructions = s.Instructions }));
                Editor.MarkClean();
            }

            _Description.ReadOnly = !canManage;
            _Overview.ReadOnly = !canManage;
            _Active.ReadOnly = !canManage;
            ParameterList.ReadOnly = !canManage;
            StepList.ReadOnly = !canManage;
            Editor.View.ShowButtons = canManage;

            Overview.Reset();
            Overview.Section("Overview");
            Overview.Row("Status", T(r.Active ? "Active" : "Inactive"), t => StatusBadge.Style(r.Active ? "Active" : "Inactive", t));
            Overview.Row("Workflow Profile", !String.IsNullOrEmpty(r.WorkflowProfileId) ? ProfileName(r.WorkflowProfileId) : "-");
            Overview.Link("Environment", !String.IsNullOrEmpty(r.EnvironmentId) ? EnvironmentName(r.EnvironmentId, r.EnvironmentName) : EntityUi.Dash(r.EnvironmentName),
                !String.IsNullOrEmpty(r.EnvironmentId) ? () => Context.Navigate("/environments/" + r.EnvironmentId) : (Action?)null);
            Overview.Row("Default Check", r.DefaultCheckType.HasValue ? r.DefaultCheckType.Value.ToString() : "-");
            Overview.Row("Parameters", (r.Parameters?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            Overview.Row("Steps", (r.Steps?.Count ?? 0).ToString(CultureInfo.InvariantCulture));
            Overview.Row("Executions", _Executions.Count.ToString(CultureInfo.InvariantCulture));
            Overview.Row("Visibility", T(ScopeRules.Label(r.Scope)));
            Overview.Row("Created", EntityUi.Date(Context, r.CreatedUtc));
            Overview.Row("Last Updated", EntityUi.When(Context, r.LastUpdateUtc));
            if (Prefill != null)
            {
                Overview.Section("Start Execution");
                Overview.Row("Note", T("An incident or deployment handed off a prefilled runbook execution context. Open a runbook to start the execution with those defaults."));
            }

            Overview.Section("Identifiers");
            Overview.Row("Runbook ID", r.Id, t => t.Code);
            Overview.Link("Playbook ID", r.PlaybookId, !String.IsNullOrEmpty(r.PlaybookId) ? () => Context.Navigate("/playbooks/" + r.PlaybookId) : (Action?)null);
            Overview.Section("Overview Markdown");
            Overview.Row("Overview Markdown", EntityUi.Dash(r.OverviewMarkdown));

            ExecutionGrid.SetLocalRows(_Executions);

            string? keep = SelectedExecution?.Id;
            string? requested = Route.Query.TryGetValue("executionId", out string? q) ? q : null;
            RunbookExecution? next = null;
            if (keep != null) next = _Executions.FirstOrDefault(e => e.Id == keep);
            if (next == null && requested != null) next = _Executions.FirstOrDefault(e => e.Id == requested);
            if (next == null) next = _Executions.FirstOrDefault();
            bool firstLoad = LoadCount <= 1;
            if (next != null && (SelectedExecution == null || SelectedExecution.Id != next.Id || !ProgressForm.View.IsDirty))
            {
                SelectedExecution = Clone(next);
                PopulateProgress();
            }
            else if (next == null)
            {
                SelectedExecution = null;
                PopulateProgress();
            }

            if (firstLoad && requested != null && SelectedExecution != null && SelectedExecution.Id == requested) ShowPanel("progress");
        }

        #endregion

        #region Private-Methods

        private bool CanManage()
        {
            if (IsCreateMode) return true;
            Runbook? r = Entity;
            if (r == null) return Context.Session.IsTenantAdmin;
            return ScopeRules.CanEdit(Context.Session, r.Scope, r.TenantId, r.UserId);
        }

        private async Task<RunbookReferenceData> LoadReferenceAsync(CancellationToken token)
        {
            RunbookReferenceData data = new RunbookReferenceData();
            Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(Context.Client, token);
            Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(Context.Client, token);
            await Task.WhenAll(profiles, environments).ConfigureAwait(false);
            data.Profiles = profiles.Result;
            data.Environments = environments.Result;
            return data;
        }

        private async Task<List<RunbookExecution>> LoadExecutionsAsync(string runbookId, CancellationToken token)
        {
            try
            {
                return await ArmadaPaging.ReadAllAsync<RunbookExecution>((p, ct) => Context.Client.ListRunbookExecutionsAsync(new RunbookExecutionQuery { RunbookId = runbookId, PageNumber = p, PageSize = EntityLookups.PageSize }, ct), 20, token).ConfigureAwait(false);
            }
            catch (ArmadaApiException)
            {
                return new List<RunbookExecution>();
            }
        }

        private void ApplyReferenceOptions()
        {
            Editor.SetOptions(_Profile, EntityLookups.Options(_Data.Profiles, p => p.Id, p => p.Name), "No workflow profile");
            Editor.SetOptions(_Environment, EntityLookups.Options(_Data.Environments, e => e.Id, e => e.Name), "No environment");
        }

        private string ProfileName(string? id)
        {
            WorkflowProfile? p = _Data.Profiles.FirstOrDefault(x => x.Id == id);
            return p != null ? p.Name : (id ?? "-");
        }

        private string EnvironmentName(string? id, string? fallback)
        {
            DeploymentEnvironment? e = _Data.Environments.FirstOrDefault(x => x.Id == id);
            return e != null ? e.Name : (fallback ?? id ?? "-");
        }

        private void PopulateProgress()
        {
            RunbookExecution? x = SelectedExecution;
            ProgressSummary.Reset(false);
            if (x == null)
            {
                ProgressSummary.Row("Execution Progress", T("No executions recorded yet."));
                _ExecutionNotes.Value = "";
                StepChecklist.SetItems(null);
                ProgressForm.MarkClean();
                return;
            }

            ProgressSummary.Row("Title", x.Title);
            ProgressSummary.Row("Status", EntityUi.Badge(Context, x.Status.ToString()), t => StatusBadge.Style(x.Status, t));
            ProgressSummary.Row("Environment", EntityUi.Dash(x.EnvironmentName));
            ProgressSummary.Row("Check Type", x.CheckType.HasValue ? x.CheckType.Value.ToString() : "-");
            ProgressSummary.Link("Deployment", EntityUi.Dash(x.DeploymentId), !String.IsNullOrEmpty(x.DeploymentId) ? () => Context.Navigate("/deployments/" + x.DeploymentId) : (Action?)null);
            ProgressSummary.Link("Incident", EntityUi.Dash(x.IncidentId), !String.IsNullOrEmpty(x.IncidentId) ? () => Context.Navigate("/incidents/" + x.IncidentId) : (Action?)null);
            ProgressSummary.Row("Started", EntityUi.Date(Context, x.StartedUtc));
            ProgressSummary.Row("Completed", EntityUi.Date(Context, x.CompletedUtc));
            ProgressSummary.Row("Last Updated", EntityUi.When(Context, x.LastUpdateUtc));
            _ExecutionNotes.Value = x.Notes ?? "";
            List<RunbookStep> steps = Entity?.Steps ?? new List<RunbookStep>();
            StepChecklist.SetItems(steps.Select(s => new RunbookStepProgress(s, x.CompletedStepIds.Contains(s.Id), x.StepNotes.TryGetValue(s.Id, out string? n) ? n : "")));
            ProgressForm.MarkClean();
        }

        private void EditStepNote(RunbookStepProgress item, Action<RunbookStepProgress> done)
        {
            EntityForm form = new EntityForm(Context);
            CheckField complete = form.Check(item.Step.Title, item.Done);
            TextBlockHint(form, item.Step.Instructions);
            TextAreaField note = form.Area("Step Notes", item.Note, 4);
            form.MarkClean();
            RunbookStepProgress? result = null;
            EntityUi.ShowForm(Context, "Step Notes", form, "Save", ct =>
            {
                result = new RunbookStepProgress(item.Step, complete.Value, note.Value);
                return Task.FromResult<string?>(null);
            }, () => { if (result != null) done(result); });
        }

        private static void TextBlockHint(EntityForm form, string instructions)
        {
            if (String.IsNullOrWhiteSpace(instructions)) return;
            TextAreaField view = form.Area("Instructions", instructions, Math.Min(8, Math.Max(2, instructions.Split('\n').Length + 1)));
            view.ReadOnly = true;
            view.ShowFooter = false;
        }

        private void SaveRunbook()
        {
            if (!CanManage()) return;
            if (!Editor.View.ValidateAll()) return;
            DeploymentEnvironment? env = _Data.Environments.FirstOrDefault(e => e.Id == _Environment.Value);
            RunbookUpsertRequest payload = new RunbookUpsertRequest
            {
                FileName = EntityUi.Blank(_FileName.Value),
                Title = EntityUi.Blank(_Title.Value),
                Description = EntityUi.Blank(_Description.Value),
                WorkflowProfileId = EntityUi.Blank(_Profile.Value),
                EnvironmentId = EntityUi.Blank(_Environment.Value),
                EnvironmentName = EntityUi.Blank(_EnvironmentName.Value) ?? env?.Name,
                DefaultCheckType = RunbookForms.ParseCheckType(_CheckType.Value),
                Parameters = ParameterList.Items.ToList(),
                Steps = StepList.Items.ToList(),
                OverviewMarkdown = _Overview.Value,
                Active = _Active.Value
            };
            if (IsCreateMode)
            {
                payload.Scope = ScopeRules.ResolveCreateScope(Context.Session, null);
                EntityUi.Run<Runbook?>(Context, ct => Context.Client.CreateRunbookAsync(payload, ct), created =>
                {
                    if (created == null) return;
                    Editor.MarkClean();
                    EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Runbook \"{{title}}\" created.", "title", created.Title));
                    Carry(created);
                }, "Save failed.");
                return;
            }

            Runbook? current = Entity;
            if (current == null) return;
            EntityUi.Run<Runbook?>(Context, ct => Context.Client.UpdateRunbookAsync(current.Id, payload, ct), updated =>
            {
                if (updated == null) return;
                Editor.MarkClean();
                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Runbook \"{{title}}\" saved.", "title", updated.Title));
                Reload();
            }, "Save failed.");
        }

        private void Carry(Runbook target)
        {
            if (Prefill != null) NavigationPrefill.Set(Context, PrefillSlots.RunbookExecution, Prefill);
            Context.Navigate("/runbooks/" + target.Id);
        }

        private void ConfirmStatus(RunbookExecutionStatusEnum status)
        {
            RunbookExecution? x = SelectedExecution;
            if (x == null) return;
            string title = status == RunbookExecutionStatusEnum.Completed ? "Mark Completed" : "Cancel Execution";
            string message = status == RunbookExecutionStatusEnum.Completed
                ? EntityUi.T(Context, "Mark \"{{title}}\" as completed?", "title", x.Title)
                : EntityUi.T(Context, "Cancel \"{{title}}\"? Progress is kept, but the execution stops.", "title", x.Title);
            Context.Confirm(title, message, () => SaveExecution(status), title);
        }

        private void SaveExecution(RunbookExecutionStatusEnum? status)
        {
            RunbookExecution? x = SelectedExecution;
            if (x == null) return;
            RunbookExecutionUpdateRequest payload = new RunbookExecutionUpdateRequest
            {
                Status = status ?? x.Status,
                CompletedStepIds = StepChecklist.Items.Where(i => i.Done).Select(i => i.Step.Id).ToList(),
                StepNotes = StepChecklist.Items.Where(i => !String.IsNullOrEmpty(i.Note)).ToDictionary(i => i.Step.Id, i => i.Note),
                Notes = EntityUi.Blank(_ExecutionNotes.Value)
            };
            foreach (KeyValuePair<string, string> kvp in x.StepNotes)
            {
                if (!payload.StepNotes.ContainsKey(kvp.Key) && !StepChecklist.Items.Any(i => i.Step.Id == kvp.Key)) payload.StepNotes[kvp.Key] = kvp.Value;
            }

            EntityUi.Run<RunbookExecution?>(Context, ct => Context.Client.UpdateRunbookExecutionAsync(x.Id, payload, ct), updated =>
            {
                if (updated == null) return;
                int idx = _Executions.FindIndex(e => e.Id == updated.Id);
                if (idx >= 0) _Executions[idx] = updated;
                ExecutionGrid.SetLocalRows(_Executions);
                SelectedExecution = Clone(updated);
                PopulateProgress();
                EntityUi.Toast(Context, NotificationSeverityEnum.Success, EntityUi.T(Context, "Execution \"{{title}}\" updated.", "title", updated.Title));
            }, "Failed to update execution.");
        }

        private DeploymentEnvironment? SelectedEnvironment()
        {
            string? id = SelectedExecution?.EnvironmentId;
            DeploymentEnvironment? env = id != null ? _Data.Environments.FirstOrDefault(e => e.Id == id) : null;
            if (env == null && !String.IsNullOrEmpty(Entity?.EnvironmentId)) env = _Data.Environments.FirstOrDefault(e => e.Id == Entity!.EnvironmentId);
            return env;
        }

        private bool CanLaunchCheck()
        {
            if (IsCreateMode || Entity == null) return false;
            CheckRunTypeEnum? type = SelectedExecution?.CheckType ?? Entity.DefaultCheckType;
            return type.HasValue && !String.IsNullOrEmpty(SelectedEnvironment()?.VesselId);
        }

        private void LaunchCheck()
        {
            Runbook? r = Entity;
            DeploymentEnvironment? env = SelectedEnvironment();
            if (r == null || env == null || String.IsNullOrEmpty(env.VesselId)) return;
            CheckRunTypeEnum? type = SelectedExecution?.CheckType ?? r.DefaultCheckType;
            if (!type.HasValue) return;
            CheckRunRequest prefill = new CheckRunRequest();
            prefill.VesselId = env.VesselId!;
            prefill.WorkflowProfileId = SelectedExecution?.WorkflowProfileId ?? r.WorkflowProfileId;
            prefill.DeploymentId = SelectedExecution?.DeploymentId ?? Prefill?.DeploymentId;
            prefill.Type = type.Value;
            prefill.EnvironmentName = SelectedExecution?.EnvironmentName ?? r.EnvironmentName ?? env.Name;
            prefill.Label = SelectedExecution?.Title ?? r.Title;
            NavigationPrefill.Set(Context, PrefillSlots.RunCheck, prefill, "/delivery?tab=checks");
        }

        private void RequestDelete()
        {
            Runbook? r = Entity;
            if (r == null || !CanManage()) return;
            Context.Confirm("Delete Runbook", EntityUi.T(Context, "Delete \"{{title}}\"? This removes the runbook definition and keeps existing executions only in the event log history.", "title", r.Title), () =>
            {
                EntityUi.Run(Context, ct => Context.Client.DeleteRunbookAsync(r.Id, ct), () =>
                {
                    EntityUi.Toast(Context, NotificationSeverityEnum.Warning, EntityUi.T(Context, "Runbook \"{{title}}\" deleted.", "title", r.Title));
                    Context.Navigate("/delivery?tab=runbooks");
                }, "Delete failed.");
            }, "Delete");
        }

        private static RunbookExecution Clone(RunbookExecution e)
        {
            RunbookExecution copy = ArmadaJson.Deserialize<RunbookExecution>(ArmadaJson.Serialize(e)) ?? e;
            return copy;
        }

        private static string FirstLine(string text)
        {
            string trimmed = (text ?? "").Trim();
            int nl = trimmed.IndexOf('\n');
            return nl >= 0 ? trimmed.Substring(0, nl).Trim() + " ..." : trimmed;
        }

        #endregion
    }
}
