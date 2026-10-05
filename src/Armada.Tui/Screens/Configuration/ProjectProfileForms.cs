namespace Armada.Tui.Screens.Configuration
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
    /// Project profile forms shared by the list and detail screens (dashboard <c>ProjectProfiles.tsx</c> modal and
    /// <c>ProjectProfileDetail.tsx</c> override editor): name, description, scope, visibility, fleet, vessel, default
    /// pipeline, workflow profile, skills, default, and active; and the persona override editor (persona, prompt
    /// template, enabled, additional instructions). Use on the UI loop thread.
    /// </summary>
    public static class ProjectProfileForms
    {
        #region Public-Methods

        /// <summary>
        /// Load the reference lists and open the create or edit form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Profile to edit, or null.</param>
        /// <param name="onSaved">Runs with the saved profile.</param>
        public static void Open(TuiContext context, ProjectProfile? existing, Action<ProjectProfile> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, ct => LoadAsync(context, ct), data => Show(context, existing, data, onSaved), "Failed to load fleets and vessels.");
        }

        /// <summary>
        /// Load fleets, vessels, pipelines, and workflow profiles.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Reference data.</returns>
        public static async Task<ProfileReferenceData> LoadAsync(TuiContext context, System.Threading.CancellationToken token)
        {
            Task<List<Fleet>> fleets = EntityLookups.FleetsAsync(context.Client, token);
            Task<List<Vessel>> vessels = EntityLookups.VesselsAsync(context.Client, token);
            Task<List<Pipeline>> pipelines = EntityLookups.PipelinesAsync(context.Client, token);
            Task<List<WorkflowProfile>> profiles = EntityLookups.WorkflowProfilesAsync(context.Client, token);
            await Task.WhenAll(fleets, vessels, pipelines, profiles).ConfigureAwait(false);
            ProfileReferenceData data = new ProfileReferenceData();
            data.Fleets = fleets.Result;
            data.Vessels = vessels.Result;
            data.Pipelines = pipelines.Result;
            data.WorkflowProfiles = profiles.Result;
            return data;
        }

        /// <summary>
        /// Pipeline options (value = id).
        /// </summary>
        /// <param name="data">Reference data.</param>
        /// <param name="current">Current id kept even when unknown.</param>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> PipelineOptions(ProfileReferenceData data, string? current)
        {
            List<SelectOption<string>> options = EntityLookups.Options(data.Pipelines, p => p.Id, p => p.Name, p => p.Id);
            if (!String.IsNullOrEmpty(current) && !options.Any(o => o.Value == current)) options.Add(new SelectOption<string>(current!, current!));
            return options;
        }

        /// <summary>
        /// Workflow profile options (value = id).
        /// </summary>
        /// <param name="data">Reference data.</param>
        /// <param name="current">Current id kept even when unknown.</param>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> WorkflowOptions(ProfileReferenceData data, string? current)
        {
            List<SelectOption<string>> options = EntityLookups.Options(data.WorkflowProfiles, p => p.Id, p => p.Name, p => p.Id);
            if (!String.IsNullOrEmpty(current) && !options.Any(o => o.Value == current)) options.Add(new SelectOption<string>(current!, current!));
            return options;
        }

        /// <summary>
        /// Open the create or edit form with reference data loaded.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Profile to edit, or null.</param>
        /// <param name="data">Reference data.</param>
        /// <param name="onSaved">Saved callback.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Show(TuiContext context, ProjectProfile? existing, ProfileReferenceData data, Action<ProjectProfile> onSaved)
        {
            EntityForm form = new EntityForm(context);
            InputField name = form.Text("Name", existing != null ? existing.Name : "Default Project Profile", "", true);
            InputField description = form.Text("Description", existing?.Description);
            SelectField<string> scope = form.Select("Scope", ProfileFormSupport.ScopeOptions(context), (existing?.Scope ?? ProjectProfileScopeEnum.Global).ToString(), null, true);
            SelectField<string> visibility = form.Scope(existing != null ? existing.OwnershipScope : ScopeRules.ResolveCreateScope(context.Session, null));
            SelectField<string> fleet = form.Select("Fleet", EntityLookups.Options(data.Fleets, f => f.Id, f => f.Name), existing?.FleetId, "Select a fleet...", false, "Used when the scope is Fleet.");
            SelectField<string> vessel = form.Select("Vessel", EntityLookups.Options(data.Vessels, v => v.Id, v => v.Name), existing?.VesselId, "Select a vessel...", false, "Used when the scope is Vessel.");
            SelectField<string> pipeline = form.Select("Default Pipeline ID", PipelineOptions(data, existing?.DefaultPipelineId), existing?.DefaultPipelineId, "None");
            SelectField<string> workflow = form.Select("Workflow Profile ID", WorkflowOptions(data, existing?.WorkflowProfileId), existing?.WorkflowProfileId, "None");
            TextAreaField skills = form.Area("Skills", EntityUi.JoinLines(existing?.Skills), 4);
            skills.Placeholder = "dotnet\ntdd";
            CheckField isDefault = form.Check("Default for scope", existing?.IsDefault ?? false);
            CheckField active = form.Check("Active", existing?.Active ?? true);
            form.MarkClean();

            ProjectProfile? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Project Profile" : "Create Project Profile", form, existing != null ? "Save Changes" : "Create Project Profile", async ct =>
            {
                ProjectProfile payload = existing != null ? ProfileFormSupport.Clone(existing) : new ProjectProfile();
                payload.Name = name.Value.Trim();
                payload.Description = EntityUi.Blank(description.Value);
                payload.Scope = EntityForm.EnumValue(scope, ProjectProfileScopeEnum.Global);
                payload.OwnershipScope = ScopeRules.ResolveCreateScope(context.Session, EntityForm.EnumValue(visibility, ScopeEnum.TenantWide));
                payload.FleetId = payload.Scope == ProjectProfileScopeEnum.Fleet ? EntityUi.Blank(fleet.Value) : null;
                payload.VesselId = payload.Scope == ProjectProfileScopeEnum.Vessel ? EntityUi.Blank(vessel.Value) : null;
                payload.IsDefault = isDefault.Value;
                payload.Active = active.Value;
                payload.DefaultPipelineId = EntityUi.Blank(pipeline.Value);
                payload.WorkflowProfileId = EntityUi.Blank(workflow.Value);
                payload.Skills = EntityUi.SplitLines(skills.Value);
                if (existing == null) payload.PersonaOverrides = new List<PersonaOverride>();
                saved = existing != null
                    ? await context.Client.UpdateProjectProfileAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateProjectProfileAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Project profile \"{{name}}\" saved.", "name", saved.Name)
                    : EntityUi.T(context, "Project profile \"{{name}}\" created.", "name", saved.Name);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        /// <summary>
        /// One-line summary of a persona override.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="o">Override.</param>
        /// <returns>Summary.</returns>
        public static string DescribeOverride(TuiContext context, PersonaOverride o)
        {
            string text = (o.Enabled ? "[x] " : "[ ] ") + o.PersonaName;
            if (!String.IsNullOrEmpty(o.PromptTemplateName)) text += "  -> " + o.PromptTemplateName;
            if (!String.IsNullOrEmpty(o.AdditionalInstructions)) text += "  + " + context.Loc.T("Additional Instructions");
            return text;
        }

        /// <summary>
        /// Open the persona override editor.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Override to edit, or null for a new Architect override.</param>
        /// <param name="personaNames">Persona names to offer.</param>
        /// <param name="onDone">Callback with the edited override.</param>
        public static void EditOverride(TuiContext context, PersonaOverride? existing, IEnumerable<string> personaNames, Action<PersonaOverride> onDone)
        {
            EntityForm form = new EntityForm(context);
            string current = existing?.PersonaName ?? "Architect";
            List<string> names = ProfileFormSupport.KnownPersonas.Concat(personaNames ?? Enumerable.Empty<string>()).Concat(new[] { current }).Distinct(StringComparer.Ordinal).ToList();
            SelectField<string> persona = form.Select("Persona", names.Select(n => new SelectOption<string>(n, n)), current, null, true);
            InputField template = form.Text("Prompt Template Name", existing?.PromptTemplateName, "persona.architect");
            CheckField enabled = form.Check("Enabled", existing?.Enabled ?? true);
            TextAreaField instructions = form.Area("Additional Instructions", existing?.AdditionalInstructions, 4);
            form.MarkClean();
            PersonaOverride? result = null;
            EntityUi.ShowForm(context, existing != null ? "Edit Override" : "Add Override", form, "Save", ct =>
            {
                PersonaOverride o = new PersonaOverride();
                o.PersonaName = (persona.Value ?? current).Trim();
                o.PromptTemplateName = EntityUi.Blank(template.Value);
                o.AdditionalInstructions = EntityUi.Blank(instructions.Value);
                o.Enabled = enabled.Value;
                result = o;
                return Task.FromResult<string?>(null);
            }, () => { if (result != null) onDone(result); });
        }

        #endregion
    }
}
