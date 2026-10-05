namespace Armada.Tui.Screens.Delivery
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The environment create and edit modal of the Environments tab (dashboard <c>Environments.tsx</c>): vessel, name
    /// (required), kind, configuration source, base URL, health endpoint, description, access notes, deployment rules,
    /// requires approval, default for vessel, and active. Editing keeps the verification definitions and monitoring
    /// settings; creating uses the dashboard defaults (60 minute window, 300 second interval, regression alerts on).
    /// Also provides Duplicate (the dashboard's <c>buildEnvironmentDuplicatePayload</c>). Use on the UI loop.
    /// </summary>
    public static class EnvironmentForms
    {
        #region Public-Methods

        /// <summary>
        /// Load vessels and open the create or edit modal.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Environment to edit, or null to create.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved environment.</param>
        public static void Open(TuiContext context, DeploymentEnvironment? existing, Action<DeploymentEnvironment> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, ct => EntityLookups.VesselsAsync(context.Client, ct), vessels => Show(context, existing, vessels, onSaved), "Failed to load environments.");
        }

        /// <summary>
        /// Open the modal with vessels already loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Environment to edit, or null.</param>
        /// <param name="vessels">Vessels.</param>
        /// <param name="onSaved">Saved callback.</param>
        /// <returns>The dialog.</returns>
        public static Modals.FormDialog Show(TuiContext context, DeploymentEnvironment? existing, List<Vessel> vessels, Action<DeploymentEnvironment> onSaved)
        {
            EntityForm form = new EntityForm(context);
            SelectField<string> vessel = form.Select("Vessel", EntityLookups.Options(vessels, v => v.Id, v => v.Name), existing?.VesselId, "Select a vessel");
            InputField name = form.Text("Name", existing != null ? existing.Name : "Environment", "", true);
            SelectField<string> kind = form.Enum("Kind", existing != null ? existing.Kind : EnvironmentKindEnum.Development);
            InputField configurationSource = form.Text("Configuration Source", existing?.ConfigurationSource, context.Loc.T("e.g. Helm values, appsettings.Production.json, Azure slot config"));
            InputField baseUrl = form.Text("Base URL", existing?.BaseUrl, "https://service.example.com");
            InputField healthEndpoint = form.Text("Health Endpoint", existing?.HealthEndpoint, "/health or https://service.example.com/health");
            TextAreaField description = form.Area("Description", existing?.Description, 3);
            TextAreaField accessNotes = form.Area("Access Notes", existing?.AccessNotes, 3);
            accessNotes.Placeholder = "How do operators reach or authenticate to this environment?";
            TextAreaField deploymentRules = form.Area("Deployment Rules", existing?.DeploymentRules, 3);
            deploymentRules.Placeholder = "Document freeze windows, approval policy, maintenance constraints, or rollout notes.";
            CheckField requiresApproval = form.Check("Requires approval", existing?.RequiresApproval ?? false);
            CheckField isDefault = form.Check("Default environment for vessel", existing?.IsDefault ?? false);
            CheckField active = form.Check("Active", existing?.Active ?? true);
            form.MarkClean();

            DeploymentEnvironment? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Environment" : "Create Environment", form, existing != null ? "Save Changes" : "Create Environment", async ct =>
            {
                DeploymentEnvironmentUpsertRequest payload = new DeploymentEnvironmentUpsertRequest
                {
                    VesselId = EntityUi.Blank(vessel.Value),
                    Name = EntityUi.Blank(name.Value),
                    Description = EntityUi.Blank(description.Value),
                    Kind = EntityForm.EnumValue(kind, EnvironmentKindEnum.Development),
                    ConfigurationSource = EntityUi.Blank(configurationSource.Value),
                    BaseUrl = EntityUi.Blank(baseUrl.Value),
                    HealthEndpoint = EntityUi.Blank(healthEndpoint.Value),
                    AccessNotes = EntityUi.Blank(accessNotes.Value),
                    DeploymentRules = EntityUi.Blank(deploymentRules.Value),
                    VerificationDefinitions = existing != null ? existing.VerificationDefinitions : new List<DeploymentVerificationDefinition>(),
                    RolloutMonitoringWindowMinutes = existing != null ? existing.RolloutMonitoringWindowMinutes : 60,
                    RolloutMonitoringIntervalSeconds = existing != null ? existing.RolloutMonitoringIntervalSeconds : 300,
                    AlertOnRegression = existing != null ? existing.AlertOnRegression : true,
                    RequiresApproval = requiresApproval.Value,
                    IsDefault = isDefault.Value,
                    Active = active.Value
                };
                saved = existing != null
                    ? await context.Client.UpdateEnvironmentAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateEnvironmentAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Environment \"{{name}}\" saved.", "name", saved.Name)
                    : EntityUi.T(context, "Environment \"{{name}}\" created.", "name", saved.Name);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        /// <summary>
        /// Create a copy of an environment ("Name (Copy)", not default, fresh verification ids) and open it.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="environment">Source environment.</param>
        public static void Duplicate(TuiContext context, DeploymentEnvironment environment)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            DeploymentEnvironmentUpsertRequest payload = DuplicatePayload(environment);
            EntityUi.Run<DeploymentEnvironment?>(context, ct => context.Client.CreateEnvironmentAsync(payload, ct), created =>
            {
                if (created == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Environment \"{{name}}\" duplicated.", "name", created.Name));
                context.Navigate("/environments/" + created.Id);
            }, "Duplicate failed.");
        }

        /// <summary>
        /// The duplicate payload for an environment.
        /// </summary>
        /// <param name="environment">Source.</param>
        /// <returns>Payload.</returns>
        public static DeploymentEnvironmentUpsertRequest DuplicatePayload(DeploymentEnvironment environment)
        {
            string trimmed = (environment.Name ?? "").Trim();
            return new DeploymentEnvironmentUpsertRequest
            {
                VesselId = environment.VesselId,
                Name = trimmed.Length > 0 ? trimmed + " (Copy)" : "Copy",
                Description = environment.Description,
                Kind = environment.Kind,
                ConfigurationSource = environment.ConfigurationSource,
                BaseUrl = environment.BaseUrl,
                HealthEndpoint = environment.HealthEndpoint,
                AccessNotes = environment.AccessNotes,
                DeploymentRules = environment.DeploymentRules,
                VerificationDefinitions = (environment.VerificationDefinitions ?? new List<DeploymentVerificationDefinition>()).Select(d => VerificationDefinitionEditor.Clone(d, true)).ToList(),
                RolloutMonitoringWindowMinutes = environment.RolloutMonitoringWindowMinutes,
                RolloutMonitoringIntervalSeconds = environment.RolloutMonitoringIntervalSeconds,
                AlertOnRegression = environment.AlertOnRegression,
                RequiresApproval = environment.RequiresApproval,
                IsDefault = false,
                Active = environment.Active
            };
        }

        #endregion
    }
}
