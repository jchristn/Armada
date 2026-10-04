namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Workflow profile forms shared by the list and detail screens (dashboard <c>WorkflowProfiles.tsx</c> quick
    /// create/edit modal and <c>WorkflowProfileDetail.tsx</c> editors): the quick form (name, description, scope,
    /// visibility, fleet, vessel, language hints, expected artifacts, lint through package commands, default, active),
    /// the required-input and environment-command record editors, the duplicate payload, and the capability count.
    /// Use on the UI loop thread.
    /// </summary>
    public static class WorkflowProfileForms
    {
        #region Public-Methods

        /// <summary>
        /// Load fleets and vessels, then open the quick create or edit form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Profile to edit, or null to create.</param>
        /// <param name="onSaved">Runs with the saved profile (UI loop).</param>
        public static void Open(TuiContext context, WorkflowProfile? existing, Action<WorkflowProfile> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, async ct =>
            {
                Task<List<Fleet>> fleets = EntityLookups.FleetsAsync(context.Client, ct);
                Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(context.Client, ct);
                await Task.WhenAll(fleets, vessels).ConfigureAwait(false);
                ProfileReferenceData data = new ProfileReferenceData();
                data.Fleets = fleets.Result.Where(f => f.Active).ToList();
                data.Vessels = vessels.Result.Where(v => v.Active).ToList();
                return data;
            }, data => Show(context, existing, data, onSaved), "Failed to load fleets and vessels.");
        }

        /// <summary>
        /// Open the quick create or edit form with reference data loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Profile to edit, or null.</param>
        /// <param name="data">Fleets and vessels.</param>
        /// <param name="onSaved">Saved callback.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Show(TuiContext context, WorkflowProfile? existing, ProfileReferenceData data, Action<WorkflowProfile> onSaved)
        {
            EntityForm form = new EntityForm(context);
            InputField name = form.Text("Name", existing != null ? existing.Name : "Default Workflow", "", true, existing == null ? "Set the core details here. Required inputs, environment commands, and the remaining commands can be configured after creation." : null);
            InputField description = form.Text("Description", existing?.Description);
            SelectField<string> scope = form.Select("Scope", ProfileFormSupport.ScopeOptions(context), (existing?.Scope ?? WorkflowProfileScopeEnum.Global).ToString(), null, true);
            SelectField<string> visibility = form.Scope(existing != null ? existing.OwnershipScope : ScopeRules.ResolveCreateScope(context.Session, null));
            SelectField<string> fleet = form.Select("Fleet", EntityLookups.Options(data.Fleets, f => f.Id, f => f.Name), existing?.FleetId, "Select a fleet...", false, "Used when the scope is Fleet.");
            SelectField<string> vessel = form.Select("Vessel", EntityLookups.Options(data.Vessels, v => v.Id, v => v.Name), existing?.VesselId, "Select a vessel...", false, "Used when the scope is Vessel.");
            TextAreaField hints = form.Area("Language / Runtime Hints", EntityUi.JoinLines(existing?.LanguageHints), 3);
            hints.Placeholder = "dotnet\nreact\npostgres";
            TextAreaField artifacts = form.Area("Expected Artifacts", EntityUi.JoinLines(existing?.ExpectedArtifacts), 3);
            artifacts.Placeholder = "bin/Release/app.zip\ncoverage/summary.xml";
            TextAreaField lint = form.Area("Lint Command", existing?.LintCommand, 2, false, null, ".sh");
            TextAreaField build = form.Area("Build Command", existing?.BuildCommand, 2, false, null, ".sh");
            TextAreaField unit = form.Area("Unit Test Command", existing?.UnitTestCommand, 2, false, null, ".sh");
            TextAreaField integration = form.Area("Integration Test Command", existing?.IntegrationTestCommand, 2, false, null, ".sh");
            TextAreaField e2e = form.Area("E2E Test Command", existing?.E2ETestCommand, 2, false, null, ".sh");
            TextAreaField package = form.Area("Package Command", existing?.PackageCommand, 2, false, null, ".sh");
            CheckField isDefault = form.Check("Default for this scope", existing?.IsDefault ?? false);
            CheckField active = form.Check("Active", existing?.Active ?? true);
            form.MarkClean();

            WorkflowProfile? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Workflow Profile" : "Create Workflow Profile", form, existing != null ? "Save Changes" : "Create Workflow Profile", async ct =>
            {
                WorkflowProfile payload = existing != null ? ProfileFormSupport.Clone(existing) : new WorkflowProfile();
                payload.Name = name.Value.Trim();
                payload.Description = EntityUi.Blank(description.Value);
                payload.Scope = EntityForm.EnumValue(scope, WorkflowProfileScopeEnum.Global);
                payload.OwnershipScope = ScopeRules.ResolveCreateScope(context.Session, EntityForm.EnumValue(visibility, ScopeEnum.TenantWide));
                payload.FleetId = payload.Scope == WorkflowProfileScopeEnum.Fleet ? EntityUi.Blank(fleet.Value) : null;
                payload.VesselId = payload.Scope == WorkflowProfileScopeEnum.Vessel ? EntityUi.Blank(vessel.Value) : null;
                payload.IsDefault = isDefault.Value;
                payload.Active = active.Value;
                payload.LanguageHints = EntityUi.SplitLines(hints.Value);
                payload.ExpectedArtifacts = EntityUi.SplitLines(artifacts.Value);
                payload.LintCommand = EntityUi.Blank(lint.Value);
                payload.BuildCommand = EntityUi.Blank(build.Value);
                payload.UnitTestCommand = EntityUi.Blank(unit.Value);
                payload.IntegrationTestCommand = EntityUi.Blank(integration.Value);
                payload.E2ETestCommand = EntityUi.Blank(e2e.Value);
                payload.PackageCommand = EntityUi.Blank(package.Value);
                if (existing == null)
                {
                    payload.RequiredInputs = new List<WorkflowInputReference>();
                    payload.Environments = new List<WorkflowEnvironmentProfile>();
                }

                saved = existing != null
                    ? await context.Client.UpdateWorkflowProfileAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateWorkflowProfileAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Workflow profile \"{{name}}\" saved.", "name", saved.Name)
                    : EntityUi.T(context, "Workflow profile \"{{name}}\" created.", "name", saved.Name);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        /// <summary>
        /// Create a copy of a profile ("Name (Copy)", not default) and open it.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="profile">Profile.</param>
        public static void Duplicate(TuiContext context, WorkflowProfile profile)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            EntityUi.Run<WorkflowProfile?>(context, ct => context.Client.CreateWorkflowProfileAsync(DuplicatePayload(profile), ct), created =>
            {
                if (created == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Workflow profile \"{{name}}\" duplicated.", "name", created.Name));
                context.Navigate("/workflow-profiles/" + created.Id);
            }, "Duplicate failed.");
        }

        /// <summary>
        /// The duplicate payload (dashboard <c>buildWorkflowProfileDuplicatePayload</c>).
        /// </summary>
        /// <param name="profile">Source.</param>
        /// <returns>New profile.</returns>
        public static WorkflowProfile DuplicatePayload(WorkflowProfile profile)
        {
            WorkflowProfile copy = ProfileFormSupport.Clone(profile);
            WorkflowProfile fresh = new WorkflowProfile();
            copy.Id = fresh.Id;
            copy.Name = ProfileFormSupport.DuplicateName(profile.Name);
            copy.IsDefault = false;
            copy.TenantId = null;
            copy.UserId = null;
            return copy;
        }

        /// <summary>
        /// Number of configured commands (base commands plus environment commands), as the dashboard counts them.
        /// </summary>
        /// <param name="p">Profile.</param>
        /// <returns>Count.</returns>
        public static int CountCapabilities(WorkflowProfile p)
        {
            int commands = ProfileFormSupport.CountSet(p.LintCommand, p.BuildCommand, p.UnitTestCommand, p.IntegrationTestCommand, p.E2ETestCommand,
                p.PackageCommand, p.PublishArtifactCommand, p.ReleaseVersioningCommand, p.ChangelogGenerationCommand);
            int environments = (p.Environments ?? new List<WorkflowEnvironmentProfile>()).Sum(e => ProfileFormSupport.CountSet(e.DeployCommand, e.RollbackCommand, e.SmokeTestCommand, e.HealthCheckCommand));
            return commands + environments;
        }

        /// <summary>
        /// One-line summary of a required input.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="input">Input.</param>
        /// <returns>Summary.</returns>
        public static string DescribeInput(TuiContext context, WorkflowInputReference input)
        {
            string env = String.IsNullOrEmpty(input.EnvironmentName) ? context.Loc.T("All Environments") : input.EnvironmentName!;
            string text = ProviderLabel(context, input.Provider) + "  " + input.Key + "  [" + env + "]";
            if (!String.IsNullOrEmpty(input.Description)) text += "  " + input.Description;
            return text;
        }

        /// <summary>
        /// One-line summary of an environment command set.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="env">Environment profile.</param>
        /// <returns>Summary.</returns>
        public static string DescribeEnvironment(TuiContext context, WorkflowEnvironmentProfile env)
        {
            List<string> set = new List<string>();
            if (!String.IsNullOrWhiteSpace(env.DeployCommand)) set.Add(context.Loc.T("Deploy Command"));
            if (!String.IsNullOrWhiteSpace(env.RollbackCommand)) set.Add(context.Loc.T("Rollback Command"));
            if (!String.IsNullOrWhiteSpace(env.SmokeTestCommand)) set.Add(context.Loc.T("Smoke Test Command"));
            if (!String.IsNullOrWhiteSpace(env.HealthCheckCommand)) set.Add(context.Loc.T("Health Check Command"));
            if (!String.IsNullOrWhiteSpace(env.DeploymentVerificationCommand)) set.Add(context.Loc.T("Deployment Verification Command"));
            if (!String.IsNullOrWhiteSpace(env.RollbackVerificationCommand)) set.Add(context.Loc.T("Rollback Verification Command"));
            return env.EnvironmentName + ": " + (set.Count == 0 ? context.Loc.T("None") : String.Join(", ", set));
        }

        /// <summary>
        /// Open the required-input editor (provider, environment scope, key or path, description).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Input to edit, or null.</param>
        /// <param name="environmentNames">Environment names for the scope picker.</param>
        /// <param name="onDone">Callback with the edited input.</param>
        public static void EditInput(TuiContext context, WorkflowInputReference? existing, IEnumerable<string> environmentNames, Action<WorkflowInputReference> onDone)
        {
            EntityForm form = new EntityForm(context);
            WorkflowInputReferenceProviderEnum provider = existing?.Provider ?? WorkflowInputReferenceProviderEnum.EnvironmentVariable;
            List<SelectOption<string>> providers = Enum.GetValues(typeof(WorkflowInputReferenceProviderEnum)).Cast<WorkflowInputReferenceProviderEnum>()
                .Select(p => new SelectOption<string>(p.ToString(), ProviderLabel(context, p))).ToList();
            SelectField<string> providerField = form.Select("Provider", providers, provider.ToString(), null, true);
            SelectField<string> env = form.Select("Environment Scope", environmentNames.Distinct().Select(n => new SelectOption<string>(n, n)), existing?.EnvironmentName, "All Environments");
            InputField key = form.Text("Key / Path", existing?.Key, Placeholder(provider), true);
            InputField description = form.Text("Description", existing?.Description, "Optional operator note or secret purpose");
            providerField.ValueChanged += (s, e) => key.Placeholder = Placeholder(EntityForm.EnumValue(providerField, WorkflowInputReferenceProviderEnum.EnvironmentVariable));
            form.MarkClean();
            WorkflowInputReference? result = null;
            EntityUi.ShowForm(context, existing != null ? "Edit Input" : "Add Input", form, "Save", ct =>
            {
                WorkflowInputReference item = new WorkflowInputReference();
                item.Provider = EntityForm.EnumValue(providerField, WorkflowInputReferenceProviderEnum.EnvironmentVariable);
                item.Key = key.Value.Trim();
                item.EnvironmentName = EntityUi.Blank(env.Value);
                item.Description = EntityUi.Blank(description.Value);
                result = item;
                return Task.FromResult<string?>(null);
            }, () => { if (result != null) onDone(result); });
        }

        /// <summary>
        /// Open the environment command editor (name, deploy, rollback, smoke test, health check, deployment and
        /// rollback verification).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Environment to edit, or null for a new "dev" entry.</param>
        /// <param name="onDone">Callback with the edited environment.</param>
        public static void EditEnvironment(TuiContext context, WorkflowEnvironmentProfile? existing, Action<WorkflowEnvironmentProfile> onDone)
        {
            EntityForm form = new EntityForm(context);
            InputField name = form.Text("Name", existing?.EnvironmentName ?? "dev", "", true);
            TextAreaField deploy = form.Area("Deploy Command", existing?.DeployCommand, 2, false, null, ".sh");
            TextAreaField rollback = form.Area("Rollback Command", existing?.RollbackCommand, 2, false, null, ".sh");
            TextAreaField smoke = form.Area("Smoke Test Command", existing?.SmokeTestCommand, 2, false, null, ".sh");
            TextAreaField health = form.Area("Health Check Command", existing?.HealthCheckCommand, 2, false, null, ".sh");
            TextAreaField verify = form.Area("Deployment Verification Command", existing?.DeploymentVerificationCommand, 2, false, null, ".sh");
            TextAreaField rollbackVerify = form.Area("Rollback Verification Command", existing?.RollbackVerificationCommand, 2, false, null, ".sh");
            form.MarkClean();
            WorkflowEnvironmentProfile? result = null;
            EntityUi.ShowForm(context, existing != null ? "Edit Environment" : "Add Environment", form, "Save", ct =>
            {
                WorkflowEnvironmentProfile env = new WorkflowEnvironmentProfile();
                env.EnvironmentName = name.Value.Trim();
                env.DeployCommand = EntityUi.Blank(deploy.Value);
                env.RollbackCommand = EntityUi.Blank(rollback.Value);
                env.SmokeTestCommand = EntityUi.Blank(smoke.Value);
                env.HealthCheckCommand = EntityUi.Blank(health.Value);
                env.DeploymentVerificationCommand = EntityUi.Blank(verify.Value);
                env.RollbackVerificationCommand = EntityUi.Blank(rollbackVerify.Value);
                result = env;
                return Task.FromResult<string?>(null);
            }, () => { if (result != null) onDone(result); });
        }

        /// <summary>
        /// Translated provider label (dashboard option text).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="provider">Provider.</param>
        /// <returns>Label.</returns>
        public static string ProviderLabel(TuiContext context, WorkflowInputReferenceProviderEnum provider)
        {
            switch (provider)
            {
                case WorkflowInputReferenceProviderEnum.EnvironmentVariable: return context.Loc.T("Environment Variable");
                case WorkflowInputReferenceProviderEnum.FilePath: return context.Loc.T("File Path");
                case WorkflowInputReferenceProviderEnum.DirectoryPath: return context.Loc.T("Directory Path");
                case WorkflowInputReferenceProviderEnum.AwsSecretsManager: return context.Loc.T("AWS Secrets Manager");
                case WorkflowInputReferenceProviderEnum.AzureKeyVaultSecret: return context.Loc.T("Azure Key Vault");
                case WorkflowInputReferenceProviderEnum.HashiCorpVault: return context.Loc.T("HashiCorp Vault");
                default: return context.Loc.T("1Password");
            }
        }

        #endregion

        #region Private-Methods

        private static string Placeholder(WorkflowInputReferenceProviderEnum provider)
        {
            switch (provider)
            {
                case WorkflowInputReferenceProviderEnum.EnvironmentVariable: return "AWS_PROFILE";
                case WorkflowInputReferenceProviderEnum.FilePath: return "/path/to/config.json";
                case WorkflowInputReferenceProviderEnum.DirectoryPath: return "/path/to/config-directory";
                case WorkflowInputReferenceProviderEnum.AwsSecretsManager: return "prod/app/database-password";
                case WorkflowInputReferenceProviderEnum.AzureKeyVaultSecret: return "kv://armada-prod/database-password";
                case WorkflowInputReferenceProviderEnum.HashiCorpVault: return "secret/data/armada/prod/database";
                case WorkflowInputReferenceProviderEnum.OnePassword: return "op://Engineering/Armada Prod/database-password";
                default: return "Input reference";
            }
        }

        #endregion
    }
}
