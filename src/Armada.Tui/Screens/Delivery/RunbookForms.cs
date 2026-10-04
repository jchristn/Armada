namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Runbook forms shared by the Runbooks tab and the runbook detail screen: Create Runbook (file name, title,
    /// description, workflow profile, environment, default check type, scope, active), Start Execution (title,
    /// workflow profile, environment, environment name, check type, notes, parameter values), the parameter and step
    /// editors, and the duplicate payload (the dashboard's <c>buildRunbookDuplicatePayload</c>). Use on the UI loop.
    /// </summary>
    public static class RunbookForms
    {
        #region Public-Members

        /// <summary>
        /// Check types offered for runbooks (the dashboard's <c>RUNBOOK_CHECK_TYPES</c>).
        /// </summary>
        public static readonly CheckRunTypeEnum[] CheckTypes = new[]
        {
            CheckRunTypeEnum.Build, CheckRunTypeEnum.UnitTest, CheckRunTypeEnum.IntegrationTest, CheckRunTypeEnum.E2ETest,
            CheckRunTypeEnum.Migration, CheckRunTypeEnum.SecurityScan, CheckRunTypeEnum.Performance, CheckRunTypeEnum.Deploy,
            CheckRunTypeEnum.Rollback, CheckRunTypeEnum.SmokeTest, CheckRunTypeEnum.HealthCheck, CheckRunTypeEnum.DeploymentVerification,
            CheckRunTypeEnum.RollbackVerification, CheckRunTypeEnum.Custom
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check type options.
        /// </summary>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> CheckTypeOptions()
        {
            return CheckTypes.Select(c => new SelectOption<string>(c.ToString(), c.ToString())).ToList();
        }

        /// <summary>
        /// Open Create Runbook after loading profiles and environments.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="onCreated">Runs on the UI loop with the created runbook.</param>
        public static void OpenCreate(TuiContext context, Action<Runbook> onCreated)
        {
            EntityUi.Run(context, async ct =>
            {
                RunbookReferenceData data = new RunbookReferenceData();
                Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(context.Client, ct);
                Task<List<DeploymentEnvironment>> environments = EntityLookups.EnvironmentsAsync(context.Client, ct);
                await Task.WhenAll(profiles, environments).ConfigureAwait(false);
                data.Profiles = profiles.Result;
                data.Environments = environments.Result;
                return data;
            }, data => ShowCreate(context, data, onCreated), "Failed to load runbook reference data.");
        }

        /// <summary>
        /// Show Create Runbook with reference data already loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="data">Reference data.</param>
        /// <param name="onCreated">Created callback.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog ShowCreate(TuiContext context, RunbookReferenceData data, Action<Runbook> onCreated)
        {
            EntityForm form = new EntityForm(context);
            InputField fileName = form.Text("File Name", "RUNBOOK.md", "", true);
            InputField title = form.Text("Title", "Runbook", "", true);
            TextAreaField description = form.Area("Description", "", 2);
            SelectField<string> profile = form.Select("Workflow Profile", EntityLookups.Options(data.Profiles, p => p.Id, p => p.Name), "", "No workflow profile");
            SelectField<string> environment = form.Select("Environment", EntityLookups.Options(data.Environments, e => e.Id, e => e.Name), "", "No environment");
            SelectField<string> checkType = form.Select("Default Check Type", CheckTypeOptions(), "", "No default check");
            SelectField<string> scope = form.Scope(ScopeRules.ResolveCreateScope(context.Session, null));
            CheckField active = form.Check("Active", true);
            form.MarkClean();

            Runbook? created = null;
            return EntityUi.ShowForm(context, "Create Runbook", form, "Create Runbook", async ct =>
            {
                DeploymentEnvironment? env = data.Environments.FirstOrDefault(e => e.Id == environment.Value);
                RunbookUpsertRequest payload = new RunbookUpsertRequest
                {
                    FileName = EntityUi.Blank(fileName.Value),
                    Title = EntityUi.Blank(title.Value),
                    Description = EntityUi.Blank(description.Value),
                    WorkflowProfileId = EntityUi.Blank(profile.Value),
                    EnvironmentId = EntityUi.Blank(environment.Value),
                    EnvironmentName = env?.Name,
                    DefaultCheckType = ParseCheckType(checkType.Value),
                    Parameters = new List<RunbookParameter>(),
                    Steps = new List<RunbookStep>(),
                    OverviewMarkdown = "",
                    Active = active.Value,
                    Scope = ScopeRules.ResolveCreateScope(context.Session, EntityForm.EnumValue(scope, ScopeEnum.TenantWide))
                };
                created = await context.Client.CreateRunbookAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (created == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Runbook \"{{title}}\" created.", "title", created.Title));
                onCreated?.Invoke(created);
            });
        }

        /// <summary>
        /// Duplicate a runbook (title and file name get " (Copy)", steps get new ids).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="runbook">Runbook.</param>
        /// <param name="onCreated">Runs on the UI loop with the copy.</param>
        public static void Duplicate(TuiContext context, Runbook runbook, Action<Runbook> onCreated)
        {
            RunbookUpsertRequest payload = DuplicatePayload(runbook);
            EntityUi.Run<Runbook?>(context, ct => context.Client.CreateRunbookAsync(payload, ct), created =>
            {
                if (created == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Runbook \"{{title}}\" duplicated.", "title", created.Title));
                onCreated?.Invoke(created);
            }, "Duplicate failed.");
        }

        /// <summary>
        /// The duplicate payload.
        /// </summary>
        /// <param name="runbook">Runbook.</param>
        /// <returns>Payload.</returns>
        public static RunbookUpsertRequest DuplicatePayload(Runbook runbook)
        {
            if (runbook == null) throw new ArgumentNullException(nameof(runbook));
            return new RunbookUpsertRequest
            {
                FileName = DuplicateFileName(runbook.FileName),
                Title = DuplicateName(runbook.Title),
                Description = runbook.Description,
                WorkflowProfileId = runbook.WorkflowProfileId,
                EnvironmentId = runbook.EnvironmentId,
                EnvironmentName = runbook.EnvironmentName,
                DefaultCheckType = runbook.DefaultCheckType,
                Parameters = (runbook.Parameters ?? new List<RunbookParameter>()).Select(CloneParameter).ToList(),
                Steps = (runbook.Steps ?? new List<RunbookStep>()).Select(s => new RunbookStep { Title = s.Title, Instructions = s.Instructions }).ToList(),
                OverviewMarkdown = runbook.OverviewMarkdown,
                Active = runbook.Active
            };
        }

        /// <summary>
        /// "Name (Copy)".
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Copy name.</returns>
        public static string DuplicateName(string? name)
        {
            string trimmed = (name ?? "").Trim();
            return trimmed.Length > 0 ? trimmed + " (Copy)" : "Copy";
        }

        /// <summary>
        /// "NAME (Copy).ext".
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <returns>Copy file name.</returns>
        public static string DuplicateFileName(string? fileName)
        {
            string trimmed = (fileName ?? "").Trim();
            if (trimmed.Length == 0) return "Copy";
            int dot = trimmed.LastIndexOf('.');
            if (dot > 0) return trimmed.Substring(0, dot) + " (Copy)" + trimmed.Substring(dot);
            return trimmed + " (Copy)";
        }

        /// <summary>
        /// Copy a parameter.
        /// </summary>
        /// <param name="p">Parameter.</param>
        /// <returns>Copy.</returns>
        public static RunbookParameter CloneParameter(RunbookParameter p)
        {
            return new RunbookParameter { Name = p.Name, Label = p.Label ?? "", Description = p.Description ?? "", DefaultValue = p.DefaultValue ?? "", Required = p.Required };
        }

        /// <summary>
        /// Open the parameter editor (name, label, default value, description, required).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Parameter, or null for a new one.</param>
        /// <param name="done">Receives the edited parameter.</param>
        public static void EditParameter(TuiContext context, RunbookParameter? existing, Action<RunbookParameter> done)
        {
            RunbookParameter seed = existing ?? new RunbookParameter { Name = "parameter", Label = "", Description = "", DefaultValue = "", Required = false };
            EntityForm form = new EntityForm(context);
            InputField name = form.Text("Name", seed.Name, "", true);
            InputField label = form.Text("Label", seed.Label);
            InputField defaultValue = form.Text("Default Value", seed.DefaultValue);
            TextAreaField description = form.Area("Description", seed.Description, 2);
            CheckField required = form.Check("Required", seed.Required);
            form.MarkClean();
            RunbookParameter? result = null;
            EntityUi.ShowForm(context, existing != null ? "Parameters" : "Add Parameter", form, existing != null ? "Save" : "Add Parameter", ct =>
            {
                result = new RunbookParameter { Name = name.Value.Trim(), Label = label.Value, DefaultValue = defaultValue.Value, Description = description.Value, Required = required.Value };
                return Task.FromResult<string?>(null);
            }, () => { if (result != null) done(result); });
        }

        /// <summary>
        /// Open the step editor (title, instructions).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Step, or null for a new one.</param>
        /// <param name="done">Receives the edited step (the id is kept).</param>
        public static void EditStep(TuiContext context, RunbookStep? existing, Action<RunbookStep> done)
        {
            RunbookStep seed = existing ?? new RunbookStep { Title = "Step", Instructions = "" };
            EntityForm form = new EntityForm(context);
            InputField title = form.Text("Title", seed.Title, "", true);
            TextAreaField instructions = form.Area("Instructions", seed.Instructions, 6);
            form.MarkClean();
            RunbookStep? result = null;
            EntityUi.ShowForm(context, existing != null ? "Steps" : "Add Step", form, existing != null ? "Save" : "Add Step", ct =>
            {
                result = new RunbookStep { Id = seed.Id, Title = title.Value.Trim(), Instructions = instructions.Value };
                return Task.FromResult<string?>(null);
            }, () => { if (result != null) done(result); });
        }

        /// <summary>
        /// Show Start Execution.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="runbook">Runbook.</param>
        /// <param name="data">Reference data.</param>
        /// <param name="prefill">Hand-off from a deployment or incident, or null.</param>
        /// <param name="onStarted">Runs on the UI loop with the started execution.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog ShowStartExecution(TuiContext context, Runbook runbook, RunbookReferenceData data, RunbookExecutionStartRequest? prefill, Action<RunbookExecution> onStarted)
        {
            EntityForm form = new EntityForm(context);
            InputField title = form.Text("Title", "", runbook.Title);
            SelectField<string> profile = form.Select("Workflow Profile", EntityLookups.Options(data.Profiles, p => p.Id, p => p.Name), prefill?.WorkflowProfileId ?? runbook.WorkflowProfileId, "Use runbook binding");
            SelectField<string> environment = form.Select("Environment", EntityLookups.Options(data.Environments, e => e.Id, e => e.Name), prefill?.EnvironmentId ?? runbook.EnvironmentId, "Use runbook binding");
            InputField environmentName = form.Text("Environment Name", prefill?.EnvironmentName ?? runbook.EnvironmentName);
            string? check = prefill?.CheckType?.ToString() ?? runbook.DefaultCheckType?.ToString();
            SelectField<string> checkType = form.Select("Check Type", CheckTypeOptions(), check, "No default check");
            TextAreaField notes = form.Area("Execution Notes", prefill?.Notes, 3);
            Dictionary<string, InputField> values = new Dictionary<string, InputField>(StringComparer.Ordinal);
            List<RunbookParameter> parameters = runbook.Parameters ?? new List<RunbookParameter>();
            if (parameters.Count > 0) form.Section("Parameters");
            foreach (RunbookParameter p in parameters)
            {
                string initial = "";
                if (prefill?.ParameterValues != null && prefill.ParameterValues.TryGetValue(p.Name, out string? pv) && !String.IsNullOrEmpty(pv)) initial = pv;
                else if (!String.IsNullOrEmpty(p.DefaultValue)) initial = p.DefaultValue!;
                string label = (String.IsNullOrEmpty(p.Label) ? p.Name : p.Label!) + (p.Required ? " *" : "");
                values[p.Name] = form.Text(label, initial, p.Description ?? p.DefaultValue ?? "", p.Required);
            }

            environment.ValueChanged += (s, e) =>
            {
                DeploymentEnvironment? selected = data.Environments.FirstOrDefault(x => x.Id == environment.Value);
                if (selected != null) environmentName.Value = selected.Name;
            };
            form.MarkClean();

            RunbookExecution? started = null;
            return EntityUi.ShowForm(context, "Start Execution", form, "Start Execution", async ct =>
            {
                Dictionary<string, string> parameterValues = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, InputField> kvp in values) parameterValues[kvp.Key] = kvp.Value.Value;
                RunbookExecutionStartRequest payload = new RunbookExecutionStartRequest
                {
                    Title = EntityUi.Blank(title.Value),
                    WorkflowProfileId = EntityUi.Blank(profile.Value),
                    EnvironmentId = EntityUi.Blank(environment.Value),
                    EnvironmentName = EntityUi.Blank(environmentName.Value),
                    CheckType = ParseCheckType(checkType.Value),
                    ParameterValues = parameterValues,
                    DeploymentId = prefill?.DeploymentId,
                    IncidentId = prefill?.IncidentId,
                    Notes = EntityUi.Blank(notes.Value)
                };
                started = await context.Client.StartRunbookExecutionAsync(runbook.Id, payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (started == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Execution \"{{title}}\" started.", "title", started.Title));
                onStarted?.Invoke(started);
            });
        }

        /// <summary>
        /// Parse a check type select value (null when blank).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Check type or null.</returns>
        public static CheckRunTypeEnum? ParseCheckType(string? value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            return Enum.TryParse<CheckRunTypeEnum>(value, true, out CheckRunTypeEnum parsed) ? parsed : null;
        }

        #endregion
    }
}
